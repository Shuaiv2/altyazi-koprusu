// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace GameTranslator.Core;

/// <summary>Yazı tanımanın normalize kutusu (0...1). Başlangıç sol-alttadır (Mac Vision/CoreGraphics ile aynı): <c>Y</c> alttan yukarı.</summary>
public readonly record struct OcrRect(double X, double Y, double Width, double Height)
{
    public double MinX => X;
    public double MaxX => X + Width;
    public double MidX => X + Width / 2;
}

/// <summary>
/// Yazı tanımanın bulduğu tek satır. (<c>Windows.Media.Ocr.OcrLine</c> ile karışmasın diye bu ad.)
/// Mac: <c>OCRLine</c> (mac/SubtitleFilter.swift).
/// </summary>
public sealed record OcrTextLine(string Text, OcrRect Box, float Confidence = 1);

/// <summary>
/// Oyun altyazısı olmayan satırları (tabela, plaka, HUD sayıları, tuş ipuçları) birleştirmeden önce eler.
/// Saf mantık: arayüz ya da OCR bağımlılığı yok. Mac: mac/SubtitleFilter.swift (kurallar birebir aynı).
/// </summary>
public static partial class SubtitleFilter
{
    public sealed class Options
    {
        /// <summary>&lt; 2 harf ("1/3", "00:12", "$500")</summary>
        public bool DropFewLetters { get; set; } = true;
        /// <summary>rakam ağırlıklı ya da plaka biçimli</summary>
        public bool DropDigitHeavy { get; set; } = true;
        /// <summary>≤2 sözcüklük BÜYÜK HARF ("EXIT", "GAS STATION")</summary>
        public bool DropShortCaps { get; set; } = true;
        /// <summary>"Press ⓧ", "X Atla", "Hold R2", "Skip"</summary>
        public bool DropButtonPrompts { get; set; } = true;
        /// <summary>diğer satırlara göre çok düşük güven</summary>
        public bool DropLowConfidence { get; set; } = true;
        /// <summary>merkez ve satır yüksekliği kuralları</summary>
        public bool UseGeometry { get; set; } = true;
        /// <summary>baskın satırın merkezinden en çok bu kadar (kırpma genişliği oranı)</summary>
        public double CenterTolerance { get; set; } = 0.30;
        /// <summary>referans satır yüksekliğinden en çok bu oranda sapma</summary>
        public double HeightTolerance { get; set; } = 0.45;
    }

    public enum Reason { FewLetters, DigitHeavy, ShortCaps, ButtonPrompt, LowConfidence, OffCenter, HeightMismatch }

    public sealed record Result(IReadOnlyList<OcrTextLine> Kept, IReadOnlyList<(OcrTextLine Line, Reason Reason)> Dropped);

    public static IReadOnlyList<OcrTextLine> Filter(IEnumerable<OcrTextLine> lines, Options? options = null) =>
        Evaluate(lines, options).Kept;

    /// <summary>Hata ayıklama günlüğü için: elenen satırlar ve nedenleri.</summary>
    public static Result Evaluate(IEnumerable<OcrTextLine> input, Options? options = null)
    {
        options ??= new Options();
        var kept = new List<OcrTextLine>();
        var dropped = new List<(OcrTextLine, Reason)>();

        // 1) Satır düzeyi kurallar
        foreach (var line in input)
        {
            if (LineReason(line.Text, options) is { } reason) dropped.Add((line, reason)); else kept.Add(line);
        }

        // 2) Güven: kalan satırlar arasında belirgin düşük olanlar
        if (options.DropLowConfidence && kept.Count >= 2)
        {
            var best = kept.Max(l => l.Confidence);
            var next = new List<OcrTextLine>();
            foreach (var line in kept)
            {
                if (line.Confidence < best * 0.5f && line.Confidence < 0.6f) dropped.Add((line, Reason.LowConfidence)); else next.Add(line);
            }
            kept = next;
        }

        // 3) Geometri
        if (options.UseGeometry && kept.Count >= 2)
        {
            // Eşitlikte ilk satır baskın olur (Swift max(by:) ile aynı).
            var dominant = kept[0];
            foreach (var l in kept) if (LetterCount(dominant.Text) < LetterCount(l.Text)) dominant = l;
            var heights = kept.Select(l => l.Box.Height).Order().ToList();
            var reference = kept.Count >= 3 ? heights[heights.Count / 2] : dominant.Box.Height;
            var dMid = dominant.Box.MidX;
            var dMin = dominant.Box.MinX;
            var dMax = dominant.Box.MaxX;
            var next = new List<OcrTextLine>();
            foreach (var line in kept)
            {
                if (line == dominant) { next.Add(line); continue; }
                var farCenter = Math.Abs(line.Box.MidX - dMid) > options.CenterTolerance;
                // Sola yaslı çok satırlı altyazıda kısa satır baskın satırın kapladığı aralığın içindedir.
                var inside = line.Box.MinX >= dMin - 0.02 && line.Box.MaxX <= dMax + 0.02;
                if (farCenter && !inside) dropped.Add((line, Reason.OffCenter));
                else if (reference > 0 && Math.Abs(line.Box.Height - reference) / reference > options.HeightTolerance)
                    dropped.Add((line, Reason.HeightMismatch));
                else next.Add(line);
            }
            kept = next;
        }
        return new Result(kept, dropped);
    }

    // --- Satır kuralları ---

    internal static Reason? LineReason(string raw, Options options)
    {
        var text = raw.Trim();
        var letters = LetterCount(text);
        var digits = text.Count(char.IsNumber);
        var hasLower = text.Any(char.IsLower);
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var wordTokens = tokens.Where(t => t.Any(char.IsLetter)).ToList();

        if (options.DropFewLetters && letters < 2) return Reason.FewLetters;

        if (options.DropButtonPrompts && IsButtonPrompt(text, tokens)) return Reason.ButtonPrompt;

        if (options.DropDigitHeavy)
        {
            if (digits >= letters) return Reason.DigitHeavy;
            // Küçük harfsiz, rakamlı, kısa: plaka / kod ("34 ABC 123", "AB-1234-XY")
            if (!hasLower && digits > 0 && wordTokens.Count <= 4 && tokens.Length <= 4 && !IsSpeakerLabeled(text)) return Reason.DigitHeavy;
        }

        if (options.DropShortCaps && !hasLower && wordTokens.Count <= 2 && !IsSpeakerLabeled(text))
        {
            if (!EndsWithTerminalPunctuation(text)) return Reason.ShortCaps;
        }
        return null;
    }

    private static readonly HashSet<char> Closers = ['"', '\'', '”', '’', '»', ')', ']'];

    /// <summary>Cümle sonu noktalaması (. ! ? …), ardından isteğe bağlı kapanan tırnak/parantez. Tabela ve menüler böyle bitmez.</summary>
    internal static bool EndsWithTerminalPunctuation(string text)
    {
        var t = text.Trim();
        var end = t.Length;
        while (end > 0 && Closers.Contains(t[end - 1])) end--;
        if (end == 0) return false;
        return ".!?…".Contains(t[end - 1]);
    }

    internal static int LetterCount(string s) => s.Count(char.IsLetter);

    /// <summary>"NAME: metin": iki noktadan sonra metin var.</summary>
    internal static bool IsSpeakerLabeled(string text) => SpeakerLabel().IsMatch(text);

    [GeneratedRegex(@"^[\p{L}][\p{L}\p{N} .'’\-]{0,24}:\s*\S")]
    private static partial Regex SpeakerLabel();

    private static readonly HashSet<string> StrongGlyphs =
    [
        "○", "△", "□", "✕", "✖", "⨯", "ⓧ", "Ⓧ", "◯", "⊗",
        "R1", "R2", "R3", "L1", "L2", "L3", "OPTIONS", "TOUCHPAD",
    ];
    private const string GlyphSymbols = "○△□✕✖⨯ⓧⓍ◯⊗";
    private static readonly HashSet<string> AmbiguousGlyphs = ["X", "O"];
    private static readonly HashSet<string> PromptKeywords =
    [
        "skip", "atla", "continue", "devam", "press", "hold", "tap", "bas", "back", "geri", "select", "seç", "sec",
        "confirm", "onayla", "cancel", "iptal", "close", "kapat", "retry", "resume", "accept", "kabul", "cutscene",
        "to", "için", "icin", "button", "düğme", "tuş", "tus", "tut",
    ];
    private static readonly HashSet<string> MenuWords =
    [
        "skip", "atla", "continue", "devam", "back", "geri", "select", "seç", "sec", "confirm", "onayla",
        "cancel", "iptal", "cutscene", "ara", "sahneyi", "sahne", "resume", "retry", "close", "kapat", "et",
    ];
    private static readonly char[] EdgeChars = "()[]<>{}.,:;!?\"'“”‘’«»|".ToCharArray();
    private static readonly string[] ButtonWords = ["button", "buttons", "key", "düğme", "düğmesi", "tuş", "tuşu", "tuşuna"];
    private static readonly string[] ButtonishWords = ["press", "hold", "tap", "bas", "basın", "dokun", "any", "herhangi"];

    private static string StripEdges(string token) => token.Trim(EdgeChars);

    internal static bool IsButtonPrompt(string text, string[] tokens)
    {
        var stripped = tokens.Select(StripEdges).Where(t => t.Length > 0).ToList();
        var lowered = stripped.Select(t => t.ToLowerInvariant()).ToList();
        var hasSymbolGlyph = text.IndexOfAny(GlyphSymbols.ToCharArray()) >= 0;
        var strongTokens = stripped.Where(StrongGlyphs.Contains).ToList();
        var hasAmbiguous = stripped.Any(AmbiguousGlyphs.Contains);
        var hasKeyword = lowered.Any(PromptKeywords.Contains);
        var otherWords = stripped.Where(t => !StrongGlyphs.Contains(t) && !AmbiguousGlyphs.Contains(t) && t.Any(char.IsLetter)).ToList();

        // Kesin düğme simgesi/adı + en çok 4 başka sözcük
        if ((hasSymbolGlyph || strongTokens.Count > 0) && otherWords.Count <= 4) return true;
        // Belirsiz "X"/"O": yalnız yanında ipucu sözcüğü varsa ("Malcolm X" altyazıdır)
        if (hasAmbiguous && hasKeyword && otherWords.Count <= 4) return true;

        // Hold/Press/Tap/Basılı tut/Bas ile başlayan ipuçları ("Hold on!" altyazıdır)
        if (lowered.Count > 0)
        {
            var first = lowered[0];
            var startsPrompt = first is "hold" or "press" or "tap" or "bas"
                || (first == "basılı" && lowered.Count > 1 && lowered[1] == "tut");
            if (startsPrompt)
            {
                var second = lowered.Count > 1 ? lowered[1] : "";
                var hasButtonWord = lowered.Any(w => ButtonWords.Contains(w));
                if (hasButtonWord || second == "to" || (lowered.Contains("to") && lowered.Count <= 5) || lowered.Count == 1)
                    return true;
            }
        }
        // "... to continue/skip", "devam etmek için" kalıbı kısa satırda
        if (stripped.Count <= 6)
        {
            var joined = string.Join(" ", lowered);
            if (ToContinue().IsMatch(joined) && HasButtonish(lowered)) return true;
        }

        // Kısa menü sözcüğü satırı: "Skip", "Atla", "Back", "Geri"
        if (otherWords.Count is > 0 and <= 2 && otherWords.All(w => MenuWords.Contains(w.ToLowerInvariant()))
            && !EndsWithTerminalPunctuation(text))
            return true;
        return false;
    }

    [GeneratedRegex(@"\b(to|için|icin) (continue|skip|start|retry|resume|devam|atla|başla|basla)\b")]
    private static partial Regex ToContinue();

    private static bool HasButtonish(List<string> lowered) => lowered.Any(w => ButtonishWords.Contains(w));
}
