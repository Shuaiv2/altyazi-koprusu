// SPDX-License-Identifier: AGPL-3.0-only

using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameTranslator.Core;

namespace GameTranslator.Translation;

/// <summary>
/// Google'ın resmi olmayan ücretsiz çeviri uç noktası (Google ücretsiz). Metin bilgisayardan çıkıp Google'a gider;
/// bu yüzden uygulama yalnız kullanıcı "İnternet yedeğine izin ver" dediğinde kullanır.
/// translate.googleapis.com 429/403 verirse 10 dakika atlanır ve yalnız clients5 aynası kullanılır.
/// </summary>
public sealed class GoogleGtxTranslator : ITextTranslator
{
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    /// <summary>translate.googleapis.com 429/403 verince ne kadar süre atlanır.</summary>
    public static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(10);
    private static DateTime _blockedUntil = DateTime.MinValue;
    private static readonly object BlockGate = new();

    /// <summary>Ana uç nokta engelliyse kullanıcıya gösterilecek kısa not; yoksa null.</summary>
    public static string? Notice { get; private set; }

    internal static bool IsBlocked(DateTime now) { lock (BlockGate) return now < _blockedUntil; }
    internal static void ResetBreaker() { lock (BlockGate) { _blockedUntil = DateTime.MinValue; Notice = null; } }

    private readonly HttpClient _client;
    private readonly Func<DateTime> _utcNow;

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex PrefixRegex = new(@"^(?<prefix>(?:[-–—]+|>{1,3})\s*)", RegexOptions.Compiled);

    private readonly string _source;
    private readonly string _target;

    public GoogleGtxTranslator(string source = "en", string target = "tr") : this(source, target, SharedClient, null) { }

    internal GoogleGtxTranslator(string source, string target, HttpClient client, Func<DateTime>? utcNow)
    {
        _client = client;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _source = string.IsNullOrWhiteSpace(source) ? "en" : source.ToLowerInvariant();
        _target = string.IsNullOrWhiteSpace(target) ? "tr" : target.ToLowerInvariant();
    }

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();

        // Konuşmacı işareti (>> veya -) varsa ayrıştır
        string prefix = "";
        var match = PrefixRegex.Match(trimmed);
        if (match.Success)
        {
            prefix = match.Groups["prefix"].Value;
            trimmed = trimmed[prefix.Length..].Trim();
            if (trimmed.Length == 0) return prefix;
        }

        trimmed = JoinWrappedLines(trimmed);

        // 1. Önbellek kontrolü (anında yanıt, 0 ağ gecikmesi)
        var cacheKey = $"{_source}->{_target}:{trimmed}";
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return prefix + cached;
        }

        // 2. Google uç noktalarını dene; ana uç nokta engelliyse (429/403) yalnız clients5 aynası kullanılır.
        var q = Uri.EscapeDataString(trimmed);
        var endpoints = new List<string>();
        if (!IsBlocked(_utcNow()))
        {
            endpoints.Add($"https://translate.googleapis.com/translate_a/single?client=gtx&sl={_source}&tl={_target}&dt=t&q={q}");
            endpoints.Add($"https://translate.googleapis.com/translate_a/single?client=dict-chrome-ex&sl={_source}&tl={_target}&dt=t&q={q}");
        }
        endpoints.Add($"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl={_source}&tl={_target}&q={q}");

        var skipMain = false;
        foreach (var url in endpoints)
        {
            var isMain = url.StartsWith("https://translate.googleapis.com/", StringComparison.Ordinal);
            if (isMain && skipMain) continue;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                using var resp = await _client.SendAsync(req, cancellationToken);
                if (resp.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.Forbidden && isMain)
                {
                    lock (BlockGate)
                    {
                        _blockedUntil = _utcNow() + BlockDuration;
                        Notice = $"Google ücretsiz uç nokta HTTP {(int)resp.StatusCode} verdi; 10 dk atlanacak, yedek ayna kullanılacak.";
                    }
                    // İkinci translate.googleapis.com adresini de denemeden aynaya geç.
                    skipMain = true;
                    continue;
                }
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                var translated = ParseTranslationJson(json);

                if (!string.IsNullOrWhiteSpace(translated))
                {
                    Cache[cacheKey] = translated;
                    if (Cache.Count > 1000) Cache.Clear(); // Bellek sızıntısını önle
                    return prefix + translated;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Bir sonraki yedek uç noktaya geç
            }
        }

        // Çeviri başarısız olduysa İngilizce metni Türkçe gibi gösterme, hata fırlatarak hattın yeniden denemesini sağla
        throw new TranslatorException(TranslatorStatus.EngineFailed);
    }

    /// <summary>
    /// Ekranda iki satıra sarılmış tek cümleyi ("...he was running\ndown Broadway...") tek satır yapar. Google satır
    /// sonunu cümle sonu sayıp her satırı ayrı çeviriyordu ("in no\ntime." → "...sokacaksınız\nzaman."). Sonraki satır
    /// konuşmacı işaretiyle (- ya da >>) başlıyorsa ayrı konuşmadır; satır sonu korunur.
    /// </summary>
    internal static string JoinWrappedLines(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return text;
        var joined = new StringBuilder(lines[0]);
        var previous = lines[0];
        foreach (var line in lines.Skip(1))
        {
            // Tire yalnız önceki satır cümleyi bitirdiyse konuşmacı işaretidir; yarım cümlenin ortasında okunan tire
            // yazı tanıma artığıdır ("There's a\n- locksmith up the street!") ve atılır.
            var finished = previous.TrimEnd('"', '\'', '”', '’', ' ') is { Length: > 0 } end && ".!?…".Contains(end[^1]);
            if (PrefixRegex.IsMatch(line) && finished) joined.Append('\n').Append(line);
            else joined.Append(' ').Append(PrefixRegex.Replace(line, ""));
            previous = line;
        }
        return joined.ToString();
    }

    private static string? ParseTranslationJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                if (first.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var s in first.EnumerateArray())
                    {
                        if (s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0)
                        {
                            var part = s[0].GetString();
                            if (!string.IsNullOrEmpty(part)) sb.Append(part);
                        }
                    }
                    var result = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(result)) return System.Net.WebUtility.HtmlDecode(result);
                }
                else if (first.ValueKind == JsonValueKind.String)
                {
                    var result = first.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(result)) return System.Net.WebUtility.HtmlDecode(result);
                }
            }
        }
        catch { }

        return null;
    }
}
