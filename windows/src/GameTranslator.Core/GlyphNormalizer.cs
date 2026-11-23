// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace GameTranslator.Core;

/// <summary>
/// PlayStation düğme simgelerinin OCR okumalarını cümle içinde kararlı belirteçlere çevirir.
/// Biçim: "L1" "R1" "L2" "R2" "L3" "R3" "OPTIONS" "TOUCHPAD" (çıplak büyük harf kodu) ve yüz düğmeleri için
/// "✕" "○" "□" "△" glif karakterleri. Hangi düğme olduğu okunamayan simge ("oa", "@") dürüstçe "[button]" olur.
/// Mac: mac/InfoBox.swift GlyphNormalizer (kurallar birebir aynı).
/// </summary>
public static class GlyphNormalizer
{
    public static readonly IReadOnlySet<char> FaceGlyphs = new HashSet<char> { '✕', '○', '□', '△' };

    private const RegexOptions Cs = RegexOptions.CultureInvariant;
    private static readonly Regex ShoulderL = new(@"(?<![A-Za-z0-9])L[1Il|](?![A-Za-z0-9])", Cs);
    private static readonly Regex ShoulderR = new(@"(?<![A-Za-z0-9])R[1Il|](?![A-Za-z0-9])", Cs);
    private static readonly Regex BracketCode = new(@"[\(\[\{]\s*(L[123]|R[123]|OPTIONS|TOUCHPAD)\s*[\)\]\}]", Cs | RegexOptions.IgnoreCase);
    private static readonly Regex BracketLetter = new(@"[\(\[\{]\s*([XxOo0])\s*[\)\]\}]", Cs);
    private static readonly Regex WordCodes = new(@"(?<![A-Za-z0-9])(options|touchpad)(?=\s+button)", Cs | RegexOptions.IgnoreCase);
    private static readonly Regex PlusSpacing = new(@"((?:L|R)[123]|✕|○|□|△|\[button\])\s*\+\s*(?=(?:L|R)[123]\b|✕|○|□|△|\[button\])", Cs);

    private static readonly (string From, string To)[] DirectMap =
    [
        ("ⓧ", "✕"), ("Ⓧ", "✕"), ("⊗", "✕"), ("✖", "✕"), ("✗", "✕"), ("✘", "✕"), ("❌", "✕"), ("⨯", "✕"),
        ("⭕", "○"), ("◯", "○"), ("◎", "○"), ("ⓞ", "○"), ("Ⓞ", "○"), ("⚪", "○"),
        ("◻", "□"), ("▢", "□"), ("☐", "□"), ("⬜", "□"), ("▫", "□"),
        ("▵", "△"), ("∆", "△"), ("⛛", "△"), ("🔺", "△"), ("▲", "△"),
    ];
    private static readonly HashSet<string> Blobs = ["oa", "0a", "oo", "@", "©", "®", "ⓐ"];
    private static readonly HashSet<string> LeadWords = ["press", "hold", "tap"];
    private static readonly HashSet<string> CodeWords = ["L1", "R1", "L2", "R2", "L3", "R3", "OPTIONS", "TOUCHPAD", "✕", "○", "□", "△", "[button]"];
    private static readonly HashSet<char> Edge = ['.', ',', '!', '?', ';', ':', '"', '\'', '”', '’', '“', '‘'];

    /// <summary>Sözcük çevresindeki noktalama ayrılır: (önek, çekirdek, sonek).</summary>
    private static (string Lead, string Core, string Trail) Split(string t)
    {
        int a = 0, b = t.Length;
        while (a < b && Edge.Contains(t[a])) a++;
        while (b > a && Edge.Contains(t[b - 1])) b--;
        return (t[..a], t[a..b], t[b..]);
    }

    private static bool IsGlyphLike(string core) =>
        CodeWords.Contains(core) || core is "X" or "x" or "O" or "o" or "0" || Blobs.Contains(core.ToLowerInvariant());

    public static string Normalize(string input)
    {
        var s = input;
        foreach (var (from, to) in DirectMap) s = s.Replace(from, to, StringComparison.Ordinal);
        s = ShoulderL.Replace(s, "L1");
        s = ShoulderR.Replace(s, "R1");
        s = BracketCode.Replace(s, m => m.Groups[1].Value.ToUpperInvariant());
        // Parantezli kodların büyük harfe getirilmesi
        s = string.Join(' ', s.Split(' ').Select(tok =>
        {
            var (l, c, t) = Split(tok);
            return c.ToLowerInvariant() is "l1" or "r1" or "l2" or "r2" or "l3" or "r3" ? l + c.ToUpperInvariant() + t : tok;
        }));
        s = BracketLetter.Replace(s, m => m.Groups[1].Value is "X" or "x" ? "✕" : "○");
        s = WordCodes.Replace(s, m => m.Value.ToUpperInvariant());
        s = PlusSpacing.Replace(s, "$1 + ");

        // Belirsiz tek belirteçler (X, O, "oa", "@"): yalnızca "+" / Press-Hold-Tap yanında ya da "and/or" ile glif arasında.
        var toks = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var parts = toks.Select(Split).ToArray();
        string? Core(int i) => i >= 0 && i < toks.Length ? parts[i].Core : null;
        string? Raw(int i) => i >= 0 && i < toks.Length ? toks[i] : null;
        var outToks = (string[])toks.Clone();
        for (var i = 0; i < toks.Length; i++)
        {
            var c = parts[i].Core;
            var lc = c.ToLowerInvariant();
            var isLetterGlyph = lc is "x" or "o" or "0";
            if (!isLetterGlyph && !Blobs.Contains(lc)) continue;
            var prev = Core(i - 1)?.ToLowerInvariant();
            var next = Core(i + 1)?.ToLowerInvariant();
            var ctx = false;
            if (Raw(i - 1) == "+" || Raw(i + 1) == "+") ctx = true;
            if (prev is not null && LeadWords.Contains(prev)) ctx = true;
            if (prev is "and" or "or" && Core(i - 2) is { } pp && IsGlyphLike(pp)) ctx = true;
            if (next is "and" or "or" && Core(i + 2) is { } nn && IsGlyphLike(nn)) ctx = true;
            if (!ctx) continue;
            var g = lc switch { "x" => "✕", "o" or "0" => "○", _ => "[button]" };
            outToks[i] = parts[i].Lead + g + parts[i].Trail;
        }
        return PlusSpacing.Replace(string.Join(' ', outToks), "$1 + ");
    }

    private static readonly HashSet<string> JunkTokens = ["o", "O", "©", "®", "@", "•"];
    private static readonly HashSet<string> AFollowers = ["or", "and", "to", "for", "depending", "at", "in", "on", "by", "with", "of", "into", "from"];

    private static bool IsAsciiUpper(char c) => c is >= 'A' and <= 'Z';

    /// <summary>Bilgi kutusu metninde kalan düğme simgesi artıkları ("stash a or", "bar o depending", "ATM IP,") temizlenir.</summary>
    public static string StripIconRemnants(string input)
    {
        var toks = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        var i = 0;
        while (i < toks.Count)
        {
            var t = toks[i];
            if (JunkTokens.Contains(t)) { toks.RemoveAt(i); continue; }
            if (t == "a" && (i == toks.Count - 1 || AFollowers.Contains(toks[i + 1].ToLowerInvariant()))) { toks.RemoveAt(i); continue; }
            i++;
        }
        // Sondaki 1–2 büyük harfli belirteç: virgülle bitiyorsa ya da önünde tümü büyük harfli bir sözcük varsa (ATM IP)
        if (toks.Count >= 2)
        {
            var last = toks[^1];
            var hasComma = last.EndsWith(',');
            var core = hasComma ? last[..^1] : last;
            var isCaps = core.Length is >= 1 and <= 2 && core.All(IsAsciiUpper);
            var prev = toks[^2];
            var prevCaps = prev.Length >= 2 && prev.All(IsAsciiUpper);
            if (isCaps && (hasComma || prevCaps))
            {
                toks.RemoveAt(toks.Count - 1);
                if (toks.Count > 0 && toks[^1].EndsWith(',')) toks[^1] = toks[^1][..^1];
            }
        }
        return string.Join(' ', toks);
    }

    /// <summary>Metindeki düğme belirteçleri sırayla (çeviri sonrası eksik/fazla denetimi için).</summary>
    public static List<string> Tokens(string s)
    {
        var res = new List<string>();
        foreach (var t in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var c = Split(t).Core;
            if (CodeWords.Contains(c)) res.Add(c);
        }
        return res;
    }
}
