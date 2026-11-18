// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameTranslator.Shell;

// "Bu bilgisayarda (tam ekran oyunun üstünde)" modunun platformdan bağımsız, test edilebilir mantığı:
// pencere sıralaması, kayıtlı kaynağın yeniden bulunması, monitör seçimi ve TV ile aynı altyazı düzeni ayarları.

/// <summary>Seçilebilir bir üst düzey pencere (istemci alanı piksel olarak).</summary>
public sealed record WindowEntry(nint Handle, string Title, string Process, int Width, int Height);

/// <summary>Ekranda bir dikdörtgen (sanal masaüstü pikseli).</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public long IntersectionArea(ScreenRect other)
    {
        var w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        var h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w > 0 && h > 0 ? (long)w * h : 0;
    }
}

/// <summary>Seçici listesi: Xbox Remote Play tarayıcı pencereleri en üstte.</summary>
public static class WindowRanking
{
    /// <summary>Tarayıcı süreçleri (Xbox Remote Play / Cloud Gaming burada çalışır).</summary>
    public static readonly string[] BrowserProcesses = ["msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "arc"];

    /// <summary>Başlıkta bunlardan biri geçen tarayıcı penceresi "Xbox" penceresi sayılır.</summary>
    public static readonly string[] XboxTitleHints = ["xbox", "remote play", "uzaktan oynatma", "uzaktan oyna", "cloud gaming", "bulut oyun"];

    public static bool IsBrowser(string process) =>
        BrowserProcesses.Any(b => string.Equals(b, process, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tarayıcı penceresi ve başlığında Xbox / Remote Play / Uzaktan Oynatma / Cloud Gaming geçiyor mu.</summary>
    public static bool IsXboxRemotePlay(WindowEntry window) =>
        IsBrowser(window.Process) && XboxTitleHints.Any(h => window.Title.Contains(h, StringComparison.OrdinalIgnoreCase));

    private static int Score(WindowEntry window) =>
        IsXboxRemotePlay(window) ? 2 : IsBrowser(window.Process) ? 1 : 0;

    /// <summary>
    /// Sıra: Xbox Remote Play tarayıcı pencereleri, diğer tarayıcılar, kalanlar; her grup içinde büyük pencere önce,
    /// sonra başlığa göre. Boş başlıklı ya da çok küçük pencereler listeye girmez.
    /// </summary>
    public static IReadOnlyList<WindowEntry> Rank(IEnumerable<WindowEntry> windows) =>
        windows.Where(w => !string.IsNullOrWhiteSpace(w.Title) && w.Width >= 200 && w.Height >= 120)
            .OrderByDescending(Score)
            .ThenByDescending(w => (long)w.Width * w.Height)
            .ThenBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
}

/// <summary>Kaydedilen kaynak: süreç adı + başlık deseni. Oyun adı başlıkta değiştiği için desen sabit bir parçadır.</summary>
public sealed record SavedWindowSource(string Process, string TitlePattern)
{
    /// <summary>
    /// Başlıktan kalıcı desen çıkarır: bilinen ipucu sözcüklerinden biri geçiyorsa o ("Xbox"), yoksa başlığın kendisi.
    /// Örnek: "Forza Horizon 5 - Xbox Cloud Gaming - Microsoft Edge" -> "Cloud Gaming" yerine ilk ipucu "xbox".
    /// </summary>
    public static SavedWindowSource From(WindowEntry window)
    {
        foreach (var hint in WindowRanking.XboxTitleHints)
        {
            var index = window.Title.IndexOf(hint, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) return new SavedWindowSource(window.Process, window.Title.Substring(index, hint.Length));
        }
        return new SavedWindowSource(window.Process, window.Title.Trim());
    }

    /// <summary>
    /// Açık pencereler arasında kayıtlı kaynağa uyan en iyisi (aynı süreç adı ve başlık deseni geçen; büyük olan kazanır).
    /// Süreç adı aynı olan ama başlığı hiç uymayan pencere seçilmez.
    /// </summary>
    public WindowEntry? Find(IEnumerable<WindowEntry> windows) =>
        windows.Where(w => string.Equals(w.Process, Process, StringComparison.OrdinalIgnoreCase)
                           && w.Width >= 200 && w.Height >= 120
                           && (string.IsNullOrWhiteSpace(TitlePattern) || w.Title.Contains(TitlePattern, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(w => (long)w.Width * w.Height)
            .FirstOrDefault();
}

public static class MonitorPicker
{
    /// <summary>Pencerenin en çok alanını kapsayan monitör; hiçbiriyle kesişmiyorsa merkeze en yakını; liste boşsa null.</summary>
    public static ScreenRect? For(ScreenRect window, IReadOnlyList<ScreenRect> monitors)
    {
        if (monitors.Count == 0) return null;
        var best = monitors.Select(m => (m, area: m.IntersectionArea(window))).OrderByDescending(p => p.area).First();
        if (best.area > 0) return best.m;
        double cx = window.Left + window.Width / 2.0, cy = window.Top + window.Height / 2.0;
        return monitors.OrderBy(m => Math.Pow(m.Left + m.Width / 2.0 - cx, 2) + Math.Pow(m.Top + m.Height / 2.0 - cy, 2)).First();
    }
}

/// <summary>
/// TV altyazı ayarları (tv/webos/subtitle-settings.js ile aynı alanlar ve sınırlar). Uzunluklar "sayfa pikseli"dir:
/// genişlik 1920, yükseklik monitörün en-boy oranına göre. JSON anahtarları TV'dekiyle aynıdır.
/// </summary>
public sealed record OverlayLayoutSettings
{
    public const int Version = 1;
    public const double PageWidth = 1920;
    public const double PageHeight = 1080;

    public double X { get; init; } = 960;
    public double Y { get; init; } = Math.Round(PageHeight - PageHeight * 0.07);
    public double Width { get; init; } = 1400;
    public double FontSize { get; init; } = 44;
    public double Margin { get; init; } = 48;
    public string TextColor { get; init; } = "#FFFFFF";
    public string BgColor { get; init; } = "#000000";
    public double BgOpacity { get; init; } = 60;
    public string Outline { get; init; } = "shadow";
    public string Align { get; init; } = "center";
    public bool Bold { get; init; } = true;

    public static readonly string[] Outlines = ["none", "shadow", "outline", "both"];
    public static readonly string[] Aligns = ["left", "center", "right"];

    public static OverlayLayoutSettings Default => new();

    private static double Num(JsonElement obj, string name, double fallback)
    {
        if (!obj.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && double.IsFinite(s)) return s;
        return fallback;
    }

    private static string Hex(JsonElement obj, string name, string fallback)
    {
        if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } text
            && text.Length == 7 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit)) return text.ToUpperInvariant();
        return fallback;
    }

    private static string Pick(JsonElement obj, string name, string[] allowed, string fallback) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { } text && allowed.Contains(text) ? text : fallback;

    private static double Clamp(double v, double lo, double hi) => lo > hi ? hi : Math.Max(lo, Math.Min(hi, v));
    private static double Round(double v) => Math.Floor(v + 0.5);

    /// <summary>Geçersiz alanlar varsayılana döner, sayılar sınırlara çekilir (JS: sanitize).</summary>
    public OverlayLayoutSettings Sanitized() => new()
    {
        X = Round(Clamp(X, 0, PageWidth)),
        Y = Round(Clamp(Y, 0, PageHeight)),
        Width = Round(Clamp(Width, 300, Math.Max(300, Math.Min(1920, PageWidth)))),
        FontSize = Round(Clamp(FontSize, 20, 120)),
        Margin = Round(Clamp(Margin, 0, 200)),
        TextColor = IsHex(TextColor) ? TextColor.ToUpperInvariant() : Default.TextColor,
        BgColor = IsHex(BgColor) ? BgColor.ToUpperInvariant() : Default.BgColor,
        BgOpacity = Round(Clamp(BgOpacity, 0, 100)),
        Outline = Outlines.Contains(Outline) ? Outline : Default.Outline,
        Align = Aligns.Contains(Align) ? Align : Default.Align,
        Bold = Bold,
    };

    private static bool IsHex(string v) => v.Length == 7 && v[0] == '#' && v.Skip(1).All(Uri.IsHexDigit);

    /// <summary>Geçersiz JSON'da null döner (çağıran varsayılana düşer).</summary>
    public static OverlayLayoutSettings? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object) return null;
            var d = Default;
            return new OverlayLayoutSettings
            {
                X = Num(o, "x", d.X),
                Y = Num(o, "y", d.Y),
                Width = Num(o, "width", d.Width),
                FontSize = Num(o, "fontSize", d.FontSize),
                Margin = Num(o, "margin", d.Margin),
                TextColor = Hex(o, "textColor", d.TextColor),
                BgColor = Hex(o, "bgColor", d.BgColor),
                BgOpacity = Num(o, "bgOpacity", d.BgOpacity),
                Outline = Pick(o, "outline", Outlines, d.Outline),
                Align = Pick(o, "align", Aligns, d.Align),
                Bold = o.TryGetProperty("bold", out var b) && b.ValueKind is JsonValueKind.True or JsonValueKind.False ? b.GetBoolean() : d.Bold,
            }.Sanitized();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>TV'nin ve düzenleyicinin beklediği JSON (anahtarlar subtitle-settings.js ile aynı).</summary>
    public string ToJson()
    {
        var s = Sanitized();
        return new JsonObject
        {
            ["x"] = s.X, ["y"] = s.Y, ["width"] = s.Width, ["fontSize"] = s.FontSize, ["margin"] = s.Margin,
            ["textColor"] = s.TextColor, ["bgColor"] = s.BgColor, ["bgOpacity"] = s.BgOpacity,
            ["outline"] = s.Outline, ["align"] = s.Align, ["bold"] = s.Bold,
            ["positionSource"] = "pc", ["version"] = Version,
        }.ToJsonString();
    }

    /// <summary>"#rrggbb" ve 0-100 saydamlıktan (alfa 0-255, r, g, b).</summary>
    public static (byte A, byte R, byte G, byte B) Argb(string hex, double opacityPercent)
    {
        var h = IsHex(hex) ? hex : "#000000";
        return ((byte)Math.Round(Clamp(opacityPercent, 0, 100) / 100 * 255),
                Convert.ToByte(h.Substring(1, 2), 16), Convert.ToByte(h.Substring(3, 2), 16), Convert.ToByte(h.Substring(5, 2), 16));
    }
}

/// <summary>Kutunun sayfadaki yeri (sayfa pikseli).</summary>
public readonly record struct PageRect(double Left, double Top, double Width, double Bottom);

/// <summary>
/// Altyazı kutusu yerleşimi: TV'deki GT_SUBSETTINGS.computeRect'in C# karşılığı. Kutu 1920 genişlikli bir sayfada
/// hesaplanır; sayfa, katman monitörüne <see cref="Scale"/> ile oturtulur.
/// </summary>
public static class OverlayLayoutMath
{
    private static double Clamp(double v, double lo, double hi) => lo > hi ? hi : Math.Max(lo, Math.Min(hi, v));
    private static double Round(double v) => Math.Floor(v + 0.5);

    /// <summary>Monitör pikselini sayfa pikseline çeviren oran: sayfa genişliği her zaman 1920.</summary>
    public static double Scale(double monitorWidthPx) => monitorWidthPx > 0 ? monitorWidthPx / OverlayLayoutSettings.PageWidth : 1;

    /// <summary>Sayfa yüksekliği = monitör yüksekliği / ölçek (16:9 olmayan monitörlerde 1080'den farklı olur).</summary>
    public static double PageHeight(double monitorWidthPx, double monitorHeightPx) =>
        monitorWidthPx > 0 && monitorHeightPx > 0 ? monitorHeightPx / Scale(monitorWidthPx) : OverlayLayoutSettings.PageHeight;

    /// <summary>Sığıyorsa [margin, vw-margin] x [margin, vh-margin] dışına çıkmaz; dikeyde sığmazsa top = margin.</summary>
    public static PageRect ComputeRect(OverlayLayoutSettings s, double vw, double vh, double contentHeight)
    {
        var margin = Math.Max(0, Math.Min(s.Margin, Math.Floor(vw / 2) - 1));
        var ch = Math.Max(0, contentHeight);
        var width = Math.Min(s.Width, vw - 2 * margin);
        var left = Clamp(s.X - width / 2, margin, vw - margin - width);
        double top, bottom;
        if (ch > vh - 2 * margin)
        {
            top = margin;
            bottom = margin + ch;
        }
        else
        {
            bottom = Clamp(s.Y, margin + ch, vh - margin);
            top = bottom - ch;
        }
        return new PageRect(Round(left), Round(top), Round(width), Round(bottom));
    }

    /// <summary>Katman penceresinin monitörü tam kaplayan dikdörtgeni (kenar boşluğu yok).</summary>
    public static ScreenRect OverlayRect(ScreenRect monitor) => monitor;
}

/// <summary>Katman düzeninin dosyası (%LOCALAPPDATA%\AltyaziKoprusu\overlay-layout.json): <c>{at, settings}</c>.</summary>
public sealed class OverlayLayoutStore(string? path)
{
    public string? Path { get; } = path;

    public static string DefaultPath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AltyaziKoprusu", "overlay-layout.json");

    /// <summary>Kayıtlı düzen ve kaydedilme anı; dosya yok ya da bozuksa null.</summary>
    public (OverlayLayoutSettings Settings, DateTimeOffset? At)? Load()
    {
        if (Path is null) return null;
        try
        {
            if (!File.Exists(Path)) return null;
            var node = JsonNode.Parse(File.ReadAllText(Path)) as JsonObject;
            if (node?["settings"] is not JsonObject settings || OverlayLayoutSettings.Parse(settings.ToJsonString()) is not { } parsed) return null;
            DateTimeOffset? at = node["at"]?.GetValue<string>() is { } text && DateTimeOffset.TryParse(text, out var parsedAt) ? parsedAt : null;
            return (parsed, at);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public OverlayLayoutSettings Current => Load()?.Settings ?? OverlayLayoutSettings.Default;

    public void Save(OverlayLayoutSettings settings, DateTimeOffset? now = null)
    {
        if (Path is null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
            var body = new JsonObject
            {
                ["at"] = (now ?? DateTimeOffset.UtcNow).ToString("O"),
                ["settings"] = JsonNode.Parse(settings.ToJson()),
            };
            File.WriteAllText(Path, body.ToJsonString());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// "Altyazı Düzeni" düzenleyicisinin bu bilgisayardaki katmana bağlanan ucu (TV yerine): önizleme katmana canlı uygulanır,
/// kaydet dosyaya yazar, vazgeç kayıtlı düzene döner.
/// </summary>
public sealed class OverlayLayoutPort(OverlayLayoutStore store, Action<OverlayLayoutSettings?> apply) : ITvLayoutPort
{
    public const string ConnectedLabel = "Bu bilgisayardaki katman hazır";

    public bool TvConnected => true;

    public (string Json, DateTimeOffset? At)? LastTvLayout =>
        store.Load() is { } saved ? (saved.Settings.ToJson(), saved.At) : null;

    public void SendLayout(string mode, string settingsJson)
    {
        switch (mode)
        {
            case "preview":
                if (OverlayLayoutSettings.Parse(settingsJson) is { } preview) apply(preview);
                break;
            case "save":
                if (OverlayLayoutSettings.Parse(settingsJson) is { } saved)
                {
                    store.Save(saved);
                    apply(saved);
                }
                break;
            default:
                apply(null);
                break;
        }
    }
}
