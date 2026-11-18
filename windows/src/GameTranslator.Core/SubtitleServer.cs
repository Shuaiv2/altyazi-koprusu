// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace GameTranslator.Core;

/// <summary>TV'de çevirinin oyundaki İngilizce altyazıya göre yeri.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubtitlePlacement>))]
public enum SubtitlePlacement
{
    /// <summary>İngilizce altyazının üstüne, onu örterek.</summary>
    Cover,
    /// <summary>İngilizce altyazının hemen üstünde.</summary>
    Above,
    /// <summary>İngilizce altyazının hemen altında.</summary>
    Below,
    /// <summary>Ekranda sabit bir yükseklikte (<see cref="SubtitleStyle.BottomPercent"/>).</summary>
    Fixed,
}

/// <summary>TV'de altyazının görünümü. Eski sürümün kaydettiği ayarlarda olmayan alanlar varsayılanla dolar.</summary>
/// <param name="FontSize">36 (küçük), 46 (orta), 58 (büyük), 72 (çok büyük)</param>
/// <param name="BottomPercent">Sabit yerleşimde altyazının ekranın altından uzaklığı, yüzde.</param>
/// <param name="Background">Yazının arkasındaki siyah zeminin koyuluğu, yüzde (0: zemin yok).</param>
/// <param name="StackPrevious">
/// Hızlı konuşmada önceki satır(lar) yeni satırın üstünde kısa süre soluk kalsın mı; kapalıysa yeni satır eskisinin
/// yerine geçer. TV uygulaması 0.9.16 ve sonrası tanır.
/// </param>
public sealed record SubtitleStyle(
    int FontSize = 46,
    int BottomPercent = 7,
    SubtitlePlacement Placement = SubtitlePlacement.Cover,
    int Background = 72,
    bool Bold = false,
    SubtitleTextColor TextColor = SubtitleTextColor.White,
    TvDisplayMode DisplayMode = TvDisplayMode.InApp,
    bool StackPrevious = true,
    bool SpeakerColor = false);

/// <summary>
/// Android TV'de PS5 görüntüsünün nasıl gösterildiği. Yalnız Android TV uygulaması kullanır; diğer TV'ler ve OBS
/// katmanı alanı tanımaz.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TvDisplayMode>))]
public enum TvDisplayMode
{
    /// <summary>HDMI GameTranslator'ın içinde; görüntü hiç gelmezse TV uygulaması kendiliğinden katmana geçer.</summary>
    InApp,
    /// <summary>TV HDMI'yı kendi ekranında gösterir, altyazı üstteki şeffaf katmanda (bazı TV'lerde uygulama içi görüntü siyah kalıyor).</summary>
    Overlay,
}

/// <summary>TV'de altyazı yazısının rengi.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubtitleTextColor>))]
public enum SubtitleTextColor
{
    White,
    Yellow,
}

/// <summary>Altyazıları TV'ye ileten taraf (gerçekte <see cref="SubtitleServer"/>).</summary>
public interface ISubtitleSender
{
    /// <param name="box">Oyundaki İngilizce altyazının ekrandaki yeri (kenarlara oranla); TV çeviriyi buna göre yerleştirir.</param>
    /// <param name="paragraphs">true ise metin satır satır, sola yaslı gösterilir (görev paneli gibi başlık ve maddeli yazılar).</param>
    /// <param name="replaces">Satır az önce gösterilenin uzamış ya da düzelmiş okumasıdır (<see cref="TextMatch.IsRevision"/>); TV onu öncekinin üstüne yığmaz.</param>
    void SendSubtitle(string text, int durationMs = 15000, TextBox? box = null, bool paragraphs = false, bool replaces = false);
    void Clear();
}

public enum SubtitleEventKind { Subtitle, Clear }

/// <summary>Sunucudan geçen ve bilgisayar ekranındaki altyazı penceresinin de gösterdiği olay.</summary>
/// <param name="Replaces">Satır öncekinin uzamış ya da düzelmiş okumasıdır; öncekinin üstüne yığılmaz, yerine geçer.</param>
public sealed record SubtitleEvent(SubtitleEventKind Kind, string Text = "", int DurationMs = 0, bool Paragraphs = false, bool Replaces = false)
{
    public static readonly SubtitleEvent Cleared = new(SubtitleEventKind.Clear);
}

/// <summary>
/// TV uygulamasının bağlandığı yerel Server-Sent Events sunucusu.
/// <c>GET /events</c> akışında <c>subtitle</c>, <c>clear</c>, <c>style</c> ve <c>layout</c> olaylarını yayınlar.
/// <c>/tv-layout</c> uzaktan düzen protokolünü de konuşur (docs/uzaktan-duzen-protokolu.md; Mac: NativeSubtitleServer).
/// </summary>
/// <param name="layoutStorePath">
/// TV'nin bildirdiği son düzenin saklandığı dosya (Mac'teki UserDefaults <c>gt_tv_layout</c> karşılığı); null ise bellekte kalır.
/// </param>
public sealed class SubtitleServer(int port = 8787, TimeSpan? pingInterval = null, string? layoutStorePath = null) : ISubtitleSender, IDisposable
{
    private sealed class Client(TcpClient tcp, bool viewer = false)
    {
        public TcpClient Tcp { get; } = tcp;
        /// <summary>OBS katmanı gibi TV olmayan izleyici: olayları alır ama "TV bağlı" sayısına katılmaz.</summary>
        public bool Viewer { get; } = viewer;
        public Stream Stream { get; } = tcp.GetStream();
        /// <summary>Aynı bağlantıya iki yazma iç içe geçmesin.</summary>
        public SemaphoreSlim WriteLock { get; } = new(1, 1);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Türkçe harfler ı gibi kaçışlarla değil olduğu gibi gönderilir.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Bağlantı canlı tutma çerçevesi: yorum satırını EventSource görmez; adlı "ping" olayını TV görür ve
    /// bir süre gelmezse yarı açık kalmış bağlantıyı kapatıp yeniden bağlanır (Mac ile aynı).
    /// </summary>
    internal const string PingFrame = ": ping\n\nevent: ping\ndata: {}\n\n";

    private readonly object gate = new();
    private readonly StableCover stableCover = new();
    private readonly List<Client> clients = [];
    private TcpListener? listener;
    private Timer? pingTimer;
    private SubtitleStyle? lastStyle;
    /// <summary>GET ile verilen dosyalar: dublaj sesleri ve seslendirme modelinin ses örnekleri.</summary>
    private readonly Dictionary<string, (byte[] Data, string Type)> files = new(StringComparer.Ordinal);
    private readonly Queue<string> speechPaths = new();
    private int speechNumber;
    private string provider = "";
    // TV'nin bildirdiği altyazı düzeni (yalnız "settings" nesnesi, sıkıştırılmış JSON) ve zamanı.
    private string? lastLayoutJson;
    private DateTimeOffset? lastLayoutAt;
    private int layoutRev;
    private bool layoutLoaded;

    /// <summary>İstek gövdesi ve başlıklar için üst sınır (64 KB; Mac ile aynı).</summary>
    internal const int MaxRequestBytes = 65536;

    /// <summary>Bağlı TV sayısı değişince çağrılır (iş parçacığı havuzunda).</summary>
    public event Action<int>? ClientsChanged;

    /// <summary>
    /// TV bağlantısı (izleyici olmayan SSE istemcisi) değiştiğinde ya da TV yeni bir düzen bildirdiğinde çağrılır
    /// (iş parçacığı havuzunda; arayüz kendi iş parçacığına geçmelidir).
    /// </summary>
    public event Action? TvStatusChanged;

    /// <summary>
    /// Yalnız ortam değişkeni <c>ALTYAZI_DEV=1</c> iken <c>GET /translate-test?text=...</c> için: çeviri + gönderim yapıp
    /// JSON gövdesini döner (Mac ile aynı geliştirici kancası).
    /// </summary>
    public Func<string, Task<string>>? OnTranslateTest { get; set; }

    /// <summary>
    /// Seçili çeviri sağlayıcısının adı; TV "Çeviri sağlayıcısı" satırında gösterir (anahtar değil, yalnız ad).
    /// Değişince <c>style</c> olayı yeni adla yayınlanır; her altyazıya ve yeni bağlanan istemcinin ilk çerçevesine de girer.
    /// </summary>
    public string Provider
    {
        get { lock (gate) return provider; }
        set
        {
            SubtitleStyle style;
            lock (gate)
            {
                if (provider == value) return;
                provider = value;
                style = lastStyle ?? new SubtitleStyle();
            }
            Broadcast(StyleFrame(style, value), subtitle: true);
        }
    }

    /// <summary>En az bir izleyici olmayan SSE istemcisi (TV) bağlı mı.</summary>
    public bool TvConnected => ConnectedClients > 0;

    /// <summary>TV'nin bildirdiği son düzen (<c>settings</c> nesnesi, JSON) ve zamanı; hiç bildirim gelmediyse null.</summary>
    public (string Json, DateTimeOffset? At)? LastTvLayout
    {
        get
        {
            lock (gate)
            {
                LoadLayout();
                return lastLayoutJson is { } json ? (json, lastLayoutAt) : null;
            }
        }
    }

    /// <summary>
    /// TV'ye <c>layout</c> olayı gönderir (<paramref name="mode"/>: preview, save, cancel). <c>rev</c> her çağrıda artar.
    /// Altyazı olayı sayılmaz: <see cref="SendsToTv"/> kapalıyken de gider.
    /// </summary>
    public void SendLayout(string mode, JsonNode? settings)
    {
        int rev;
        lock (gate) rev = ++layoutRev;
        Broadcast(Frame("layout", new SortedDictionary<string, object?>
        {
            ["mode"] = mode,
            ["rev"] = rev,
            ["settings"] = settings?.DeepClone() ?? new JsonObject(),
        }));
    }

    /// <summary><see cref="SendLayout(string, JsonNode?)"/>; ayarlar JSON metni olarak verilir (geçersizse boş nesne).</summary>
    public void SendLayout(string mode, string settingsJson)
    {
        JsonNode? node = null;
        try { node = JsonNode.Parse(settingsJson); } catch (JsonException) { }
        SendLayout(mode, node as JsonObject);
    }

    /// <summary>
    /// Altyazı ve temizleme olayları gönderildiği anda buraya da verilir (çağıranın iş parçacığında). Bilgisayar
    /// ekranındaki pencere TV'ye gidenin aynısını gösterir; sunucu başlamamış olsa da çalışır.
    /// </summary>
    public event Action<SubtitleEvent>? EventSent;

    /// <summary>false ise altyazı olayları TV'lere gitmez (altyazı yalnız bilgisayar ekranında); OBS katmanı almaya devam eder.</summary>
    public bool SendsToTv { get; set; } = true;

    /// <summary>Son bağlanan veya /hello sorgulayan istemcinin IP adresi (TV adresi).</summary>
    public string? LastClientAddress { get; private set; }

    public int ConnectedClients
    {
        get { lock (gate) return TvCount(); }
    }

    private int TvCount() => clients.Count(c => !c.Viewer);

    /// <summary>OBS'de "Tarayıcı kaynağı" olarak eklenen altyazı katmanı (<c>GET /obs</c>; Mac ile aynı dosya).</summary>
    public static byte[] ObsOverlay { get; } = LoadObsOverlay();

    private static byte[] LoadObsOverlay()
    {
        using var stream = typeof(SubtitleServer).Assembly.GetManifestResourceStream("obs-overlay.html");
        if (stream is null) return [];
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>Sunucuyu başlatır ve dinlenen portu döner (<c>port: 0</c> ise rastgele seçilen port).</summary>
    public Task<int> StartAsync()
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        this.listener = listener;
        _ = AcceptLoop(listener);
        var every = pingInterval ?? TimeSpan.FromSeconds(15);
        pingTimer = new Timer(_ => Broadcast(PingFrame), null, every, every);
        return Task.FromResult(((IPEndPoint)listener.LocalEndpoint).Port);
    }

    public void Stop()
    {
        pingTimer?.Dispose();
        pingTimer = null;
        listener?.Stop();
        listener = null;
        List<Client> closing;
        lock (gate)
        {
            closing = [.. clients];
            clients.Clear();
        }
        foreach (var client in closing) client.Tcp.Dispose();
    }

    public void Dispose() => Stop();

    /// <param name="paragraphs">true ise metin satır satır, sola yaslı gösterilir (görev paneli gibi başlık ve maddeli yazılar).</param>
    /// <param name="replaces">TV satırı öncekinin üstüne yığmaz, yerine koyar (<c>replace</c> alanı; eski TV uygulamaları yok sayar).</param>
    public void SendSubtitle(string text, int durationMs = 15000, TextBox? box = null, bool paragraphs = false, bool replaces = false)
    {
        var json = new SortedDictionary<string, object>
        {
            ["at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["duration"] = durationMs,
            ["text"] = text,
        };
        if (paragraphs) json["paragraphs"] = true;
        if (replaces) json["replace"] = true;
        SubtitleStyle? style;
        string providerName;
        lock (gate) { style = lastStyle; providerName = provider; }
        if (providerName.Length > 0) json["provider"] = providerName;
        // Örtme yerleşiminde zemin tek parça olsun diye kutu çevirinin sığacağı kadar büyütülür.
        if (box is { } b)
        {
            var cover = style is { Placement: SubtitlePlacement.Cover } && !paragraphs;
            // Zemin satırdan satıra zıplamasın, aynı altyazıda daralmasın (StableCover).
            json["box"] = BoxJson(cover ? stableCover.Next(CoverFit.Box(b, text, style!.FontSize), newLine: !replaces) : b);
        }
        Broadcast(Frame("subtitle", json), subtitle: true);
        EventSent?.Invoke(new SubtitleEvent(SubtitleEventKind.Subtitle, text, durationMs, paragraphs, replaces));
    }

    /// <summary>Dört basamağa yuvarlanır (decimal: JSON'da 0.8 olarak yazılsın).</summary>
    /// Bölme sondaki sıfırları atar (0.8750 → 0.875).
    private static decimal Round(double value) =>
        Math.Round((decimal)Math.Clamp(value, 0, 1), 4) / 1.000000000000000000000000000000000m;

    public void Clear()
    {
        Broadcast(Frame("clear", new SortedDictionary<string, object>()), subtitle: true);
        EventSent?.Invoke(SubtitleEvent.Cleared);
    }

    /// <summary>
    /// "Ekranı çevir" sonuçları: <c>screen</c> olayı her çeviriyi yazının yeri (<c>box</c>), tipik satır yüksekliği
    /// (<c>line</c>, ekrana oranla) ve çok parçalıysa <c>paragraphs</c> ile taşır. TV öncekinin yerine bunları koyar.
    /// </summary>
    public void SendScreen(IReadOnlyList<ScreenTranslation.Item> items, int durationMs)
    {
        var list = items.Select(item =>
        {
            var json = new SortedDictionary<string, object>
            {
                ["box"] = BoxJson(item.Box),
                ["line"] = Round(item.LineHeight),
                ["text"] = item.Translation,
            };
            if (item.Paragraphs) json["paragraphs"] = true;
            return json;
        }).ToList();
        Broadcast(Frame("screen", new SortedDictionary<string, object> { ["duration"] = durationMs, ["items"] = list }), subtitle: true);
    }

    /// <summary>TV'deki "ekranı çevir" çevirilerini kaldırır (altyazıya dokunmaz).</summary>
    public void ClearScreen() => Broadcast(Frame("screen-clear", new SortedDictionary<string, object>()), subtitle: true);

    private static SortedDictionary<string, object> BoxJson(TextBox b) => new()
    {
        ["bottom"] = Round(b.Bottom), ["left"] = Round(b.Left), ["right"] = Round(b.Right), ["top"] = Round(b.Top),
    };

    /// <summary>Dosyayı <paramref name="path"/> adresinden verir ("/voices/male_1.wav").</summary>
    public void Publish(byte[] data, string path, string type = "audio/wav")
    {
        lock (gate) files[path] = (data, type);
    }

    /// <summary>
    /// Dublaj sesini TV'ye gönderir: ses <c>/speech/&lt;n&gt;.wav</c> adresinden verilir, <c>speech</c> olayı adresi
    /// bildirir. Son 16 ses tutulur.
    /// </summary>
    public void SendSpeech(byte[] wav, int durationMs)
    {
        string path;
        lock (gate)
        {
            path = $"/speech/{++speechNumber}.wav";
            files[path] = (wav, "audio/wav");
            speechPaths.Enqueue(path);
            while (speechPaths.Count > 16) files.Remove(speechPaths.Dequeue());
        }
        Broadcast(Frame("speech", new SortedDictionary<string, object> { ["duration"] = durationMs, ["url"] = path }));
    }

    /// <summary>TV'de çalan dublajı durdurur.</summary>
    public void StopSpeech() => Broadcast(Frame("speech-stop", new SortedDictionary<string, object>()));

    public void SendStyle(SubtitleStyle style)
    {
        string providerName;
        lock (gate) { lastStyle = style; providerName = provider; }
        Broadcast(StyleFrame(style, providerName), subtitle: true);
    }

    // --- Bağlantılar ---

    private async Task AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(); }
            catch { return; }
            _ = Serve(tcp);
        }
    }

    private async Task Serve(TcpClient tcp)
    {
        try
        {
            EnableKeepAlive(tcp.Client);
            // Altyazı çerçeveleri küçüktür; Nagle beklemesi her satırı TV'ye geç ulaştırmasın.
            tcp.NoDelay = true;
            var stream = tcp.GetStream();
            var request = await ReadRequest(stream);
            if (request is null) { tcp.Dispose(); return; }
            if (request.TooLarge)
            {
                await RespondAndClose(tcp, "413 Payload Too Large", "Too Large");
                return;
            }
            var requestLine = request.Line;
            var parts = requestLine?.Split(' ') ?? [];
            var method = parts.Length > 0 ? parts[0] : "";
            var target = parts.Length > 1 ? parts[1] : "";
            var path = target.Split('?')[0];

            if (method == "OPTIONS")
            {
                var optionsHead = "HTTP/1.1 204 No Content\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: *\r\nConnection: close\r\n\r\n";
                await tcp.GetStream().WriteAsync(Encoding.ASCII.GetBytes(optionsHead));
                tcp.Dispose();
            }
            else if (method == "POST" && path == "/tv-layout")
            {
                if (StoreTvLayout(request.Body)) await RespondAndClose(tcp, "200 OK", "{\"status\":\"ok\"}");
                else await RespondAndClose(tcp, "400 Bad Request", "{\"status\":\"error\"}");
            }
            else if (method == "GET" && path == "/tv-layout")
            {
                string json;
                lock (gate) { LoadLayout(); json = lastLayoutJson ?? "{}"; }
                await RespondAndClose(tcp, "200 OK", json);
            }
            else if (method == "GET" && path == "/translate-test" && OnTranslateTest is { } translateTest
                     && Environment.GetEnvironmentVariable("ALTYAZI_DEV") == "1")
            {
                var text = "";
                if (target.IndexOf('?') is >= 0 and var q)
                {
                    foreach (var pair in target[(q + 1)..].Split('&'))
                        if (pair.StartsWith("text=", StringComparison.Ordinal))
                            text = Uri.UnescapeDataString(pair[5..].Replace('+', ' '));
                }
                await RespondAndClose(tcp, "200 OK", await translateTest(text));
            }
            else if (method == "GET" && path == "/events") await OpenStream(tcp, target.Contains("viewer=obs", StringComparison.Ordinal));
            else if (method == "GET" && path == "/obs") await RespondAndClose(tcp, ObsOverlay, "text/html; charset=utf-8");
            // TV uygulaması ağdaki bilgisayarı bu yanıtla tanır (Android TV hem gametranslator hem couchsubs arar).
            else if (method == "GET" && path == "/hello")
            {
                if (tcp.Client.RemoteEndPoint is IPEndPoint ep && ep.Address.ToString() is { } ip && ip != "127.0.0.1" && ip != "::1")
                {
                    LastClientAddress = ip;
                }
                await RespondAndClose(tcp, "200 OK", "{\"app\":\"gametranslator\",\"name\":\"GameTranslator\",\"type\":\"gametrans\",\"legacy\":\"couchsubs\",\"product\":\"couchsubs\"}");
            }
            else if (method == "GET" && FindFile(path) is { } file) await RespondAndClose(tcp, file.Data, file.Type);
            else await RespondAndClose(tcp, "404 Not Found");
        }
        catch
        {
            tcp.Dispose();
        }
    }

    private sealed record Request(string Line, byte[] Body, bool TooLarge = false);

    /// <summary>
    /// Başlıkların sonuna (boş satır) kadar okur; Content-Length varsa gövdeyi de okur. 64 KB'yi aşan başlık kapatılır
    /// (null), 64 KB'yi aşan gövde <c>TooLarge</c> olarak bildirilir (413).
    /// </summary>
    private static async Task<Request?> ReadRequest(Stream stream)
    {
        var buffer = new byte[8192];
        using var data = new MemoryStream();
        int headEnd;
        while (true)
        {
            if (data.Length >= MaxRequestBytes) return null;
            var count = await stream.ReadAsync(buffer);
            if (count == 0) return null;
            data.Write(buffer, 0, count);
            headEnd = IndexOfHeaderEnd(data.GetBuffer(), (int)data.Length);
            if (headEnd >= 0) break;
        }
        var head = Encoding.ASCII.GetString(data.GetBuffer(), 0, headEnd);
        var lines = head.Split("\r\n");
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("content-length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), out var n))
                contentLength = n;
        }
        if (contentLength > MaxRequestBytes) return new Request(lines[0], [], TooLarge: true);
        var bodyStart = headEnd + 4;
        while (data.Length - bodyStart < contentLength)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0) return null;
            data.Write(buffer, 0, count);
        }
        var body = contentLength > 0 ? data.GetBuffer().AsSpan(bodyStart, contentLength).ToArray() : [];
        return new Request(lines[0], body);
    }

    private static int IndexOfHeaderEnd(byte[] bytes, int length)
    {
        for (var i = 0; i + 3 < length; i++)
            if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10) return i;
        return -1;
    }

    // --- TV düzeni ---

    /// <summary>Kalıcı dosyadaki son düzeni bir kez okur (gate kilidi içinde çağrılır).</summary>
    private void LoadLayout()
    {
        if (layoutLoaded) return;
        layoutLoaded = true;
        if (layoutStorePath is null) return;
        try
        {
            if (!File.Exists(layoutStorePath)) return;
            var node = JsonNode.Parse(File.ReadAllText(layoutStorePath)) as JsonObject;
            if (node?["settings"] is JsonObject settings)
            {
                lastLayoutJson = settings.ToJsonString(JsonOptions);
                lastLayoutAt = node["at"] is { } at && DateTimeOffset.TryParse(at.ToString(), out var when) ? when : null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
    }

    /// <summary>POST /tv-layout gövdesi <c>{"settings":{...}}</c>; geçerliyse saklar, TV durumu değişti diye bildirir.</summary>
    private bool StoreTvLayout(byte[] body)
    {
        JsonObject? settings;
        try { settings = (JsonNode.Parse(body) as JsonObject)?["settings"] as JsonObject; }
        catch (JsonException) { return false; }
        if (settings is null) return false;
        // Anahtarlar sıralı saklanır (Mac: .sortedKeys).
        var sorted = new JsonObject();
        foreach (var pair in settings.OrderBy(p => p.Key, StringComparer.Ordinal)) sorted[pair.Key] = pair.Value?.DeepClone();
        var json = sorted.ToJsonString(JsonOptions);
        var now = DateTimeOffset.UtcNow;
        lock (gate)
        {
            layoutLoaded = true;
            lastLayoutJson = json;
            lastLayoutAt = now;
        }
        if (layoutStorePath is not null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(layoutStorePath))!);
                File.WriteAllText(layoutStorePath, new JsonObject { ["at"] = now.ToString("O"), ["settings"] = sorted.DeepClone() }.ToJsonString(JsonOptions));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        TvStatusChanged?.Invoke();
        return true;
    }

    private (byte[] Data, string Type)? FindFile(string path)
    {
        lock (gate) return files.TryGetValue(path, out var file) ? file : null;
    }

    private static async Task RespondAndClose(TcpClient tcp, byte[] data, string type)
    {
        var head = $"HTTP/1.1 200 OK\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: *\r\nContent-Type: {type}\r\nCache-Control: no-cache\r\n" +
                   $"Content-Length: {data.Length}\r\nConnection: close\r\n\r\n";
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(data);
        tcp.Dispose();
    }

    private static async Task RespondAndClose(TcpClient tcp, string status, string body = "")
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 {status}\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, OPTIONS\r\nAccess-Control-Allow-Headers: *\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        if (bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes);
        }
        tcp.Dispose();
    }

    private async Task OpenStream(TcpClient tcp, bool viewer = false)
    {
        var head = new StringBuilder()
            .Append("HTTP/1.1 200 OK\r\n")
            .Append("Content-Type: text/event-stream; charset=utf-8\r\n")
            .Append("Cache-Control: no-cache, no-transform\r\n")
            .Append("Connection: keep-alive\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Methods: GET, OPTIONS\r\n")
            .Append("Access-Control-Allow-Headers: *\r\n\r\n")
            .Append("retry: 2000\n\n")
            .Append(": ping\n\n");
        var client = new Client(tcp, viewer);
        if (!viewer && tcp.Client.RemoteEndPoint is IPEndPoint ep && ep.Address.ToString() is { } ip && ip != "127.0.0.1" && ip != "::1")
        {
            LastClientAddress = ip;
        }
        int count;
        lock (gate)
        {
            // Sağlayıcı adı biliniyorsa stil henüz gönderilmemiş olsa da ilk çerçeveye girer (Mac'te stil her zaman gider).
            if ((lastStyle ?? (provider.Length > 0 ? new SubtitleStyle() : null)) is { } style) head.Append(StyleFrame(style, provider));
            clients.Add(client);
            count = TvCount();
        }
        await Write(client, head.ToString());
        ClientsChanged?.Invoke(count);
        TvStatusChanged?.Invoke();
        await WatchForClose(client);
    }

    /// <summary>İstemci bağlantıyı kapattığında listeden çıkarır.</summary>
    private async Task WatchForClose(Client client)
    {
        var buffer = new byte[1024];
        try
        {
            while (await client.Stream.ReadAsync(buffer) > 0) { }
        }
        catch { }
        Remove(client);
    }

    private void Remove(Client client)
    {
        int count;
        lock (gate)
        {
            if (!clients.Remove(client)) return;
            count = TvCount();
        }
        client.Tcp.Dispose();
        ClientsChanged?.Invoke(count);
        TvStatusChanged?.Invoke();
    }

    /// <summary>
    /// Gönderim hatası alan bağlantı kopmuş sayılır ve listeden çıkarılır. <paramref name="subtitle"/>: altyazı olayı;
    /// altyazı TV'ye gitmiyorsa yalnız izleyiciler (OBS) alır. Dublaj sesi ve ping her zaman herkese gider.
    /// </summary>
    private void Broadcast(string frame, bool subtitle = false)
    {
        List<Client> targets;
        var toTvs = !subtitle || SendsToTv;
        lock (gate) targets = [.. clients.Where(c => toTvs || c.Viewer)];
        foreach (var client in targets)
            _ = Write(client, frame).ContinueWith(t => Remove(client), TaskContinuationOptions.OnlyOnFaulted);
    }

    private static async Task Write(Client client, string text)
    {
        await client.WriteLock.WaitAsync();
        try
        {
            await client.Stream.WriteAsync(Encoding.UTF8.GetBytes(text));
            await client.Stream.FlushAsync();
        }
        finally
        {
            client.WriteLock.Release();
        }
    }

    /// <summary>Sessizce kaybolan TV'ler (fişten çekme, Wi-Fi kopması) TCP canlılık denetimiyle fark edilir.</summary>
    private static void EnableKeepAlive(Socket socket)
    {
        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 10);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 5);
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
        }
        catch (SocketException) { }
    }

    /// <summary>
    /// Örtme yerleşiminde TV iki zemin çizer: İngilizce altyazıyı örten kutu ve çevirinin kendi zemini. Boyları farklı
    /// olduğu için arkada iki ayrı çubuk görünüyordu. Çevirinin kendi zemini kapatılır (yalnız örtü kalır), önceki
    /// satır da üstüne yığılmaz; örtü kutusu çeviriye göre büyütülür (<see cref="CoverFit"/>).
    /// </summary>
    private static string StyleFrame(SubtitleStyle style, string provider = "")
    {
        var json = new SortedDictionary<string, object?>
        {
            ["background"] = style.Placement == SubtitlePlacement.Cover ? 0 : Math.Clamp(style.Background, 0, 100),
            ["bold"] = style.Bold,
            ["bottomPercent"] = style.BottomPercent,
            ["displayMode"] = style.DisplayMode == TvDisplayMode.Overlay ? "overlay" : "inApp",
            ["fontSize"] = style.FontSize,
            ["placement"] = style.Placement.ToString().ToLowerInvariant(),
            // Konuşmacı adının rengi ("" kapalı): yazı sarıysa ad açık mavi, değilse sarı yazılır (yeni TV uygulaması).
            ["speakerColor"] = !style.SpeakerColor ? "" : style.TextColor == SubtitleTextColor.Yellow ? "#64D2FF" : "#FFD60A",
            ["stackPrevious"] = style.StackPrevious && style.Placement != SubtitlePlacement.Cover,
            ["textColor"] = style.TextColor.ToString().ToLowerInvariant(),
        };
        // Çeviri sağlayıcısının adı (yalnız ad, anahtar değil); boşsa alan yok.
        if (provider.Length > 0) json["provider"] = provider;
        return Frame("style", json);
    }

    private static string Frame<T>(string name, SortedDictionary<string, T> json) =>
        $"event: {name}\ndata: {JsonSerializer.Serialize(json, JsonOptions)}\n\n";
}

