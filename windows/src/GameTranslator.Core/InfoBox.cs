// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace GameTranslator.Core;

/// <summary>Kare genelinde normalize (0..1), başlangıç SOL-ÜST dikdörtgen (Mac'teki CGRect karşılığı).</summary>
public readonly record struct ScreenBox(double X, double Y, double Width, double Height)
{
    public static readonly ScreenBox Zero = new(0, 0, 0, 0);
    public double MinX => X;
    public double MinY => Y;
    public double MaxX => X + Width;
    public double MaxY => Y + Height;
    public double MidX => X + Width / 2;
    public double MidY => Y + Height / 2;

    public static ScreenBox FromEdges(double x0, double y0, double x1, double y1) => new(x0, y0, x1 - x0, y1 - y0);

    public ScreenBox Union(ScreenBox o) => FromEdges(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    /// <summary>Kesişim; kesişmiyorsa null.</summary>
    public ScreenBox? Intersection(ScreenBox o)
    {
        double x0 = Math.Max(MinX, o.MinX), y0 = Math.Max(MinY, o.MinY), x1 = Math.Min(MaxX, o.MaxX), y1 = Math.Min(MaxY, o.MaxY);
        return x1 < x0 || y1 < y0 ? null : FromEdges(x0, y0, x1, y1);
    }

    public ScreenBox Inset(double dx, double dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);
}

/// <summary>Bilgi/öğretici kutusu bloğu. Mac: InfoBlock.</summary>
public sealed record InfoBlock(string Text, ScreenBox Box, double LineHeight);

/// <summary>
/// Oyundaki bilgi/öğretici kutusu için saf mantık (satırları bloğa gruplama, temizleme). Mac: mac/InfoBox.swift.
/// OcrTextLine kutuları Mac Vision gibi başlangıç SOL-ALT'tır (bestBlock/blocks bunu çevirir).
/// </summary>
public static class InfoBox
{
    public sealed record Region(double Left, double Top, double Right, double Bottom);

    /// <summary>Satır sonuna yapışan tek başına simge/glif belirteci ("wardrobe &amp;", "advance time =") atılır.</summary>
    public static string StripTrailingGlyph(string line)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        const string keep = ".!?…,;:'\"”’)";
        if (tokens.Count > 1)
        {
            var last = tokens[^1];
            var onlySymbols = last.All(c => !char.IsLetterOrDigit(c));
            var isPunct = last.All(keep.Contains);
            var isOurGlyph = last.Length == 1 && GlyphNormalizer.FaceGlyphs.Contains(last[0]);
            if (onlySymbols && !isPunct && !isOurGlyph) tokens.RemoveAt(tokens.Count - 1);
        }
        return string.Join(' ', tokens);
    }

    private static readonly HashSet<string> LeadingIcons = ["$", "i", "€", "©", "®", "ⓘ", "£"];

    /// <summary>Satır başındaki tek karakterlik simge ("$", "i", "€", "©", "®", "ⓘ") atılır; ardından sözcük kalıyorsa.</summary>
    public static string StripLeadingIcon(string line)
    {
        var t = line.Trim(' ', '\t');
        var idx = t.IndexOf(' ');
        if (idx != 1 || !LeadingIcons.Contains(t[..1])) return t;
        var rest = t[(idx + 1)..].Trim(' ', '\t');
        return rest.Length == 0 ? t : rest;
    }

    private static readonly HashSet<string> NameConnectors = ["of", "the", "and", "&", "de", "la", "el", "von", "van"];

    /// <summary>
    /// Salt ad/etiket mi ("Key Lento Coffee Morning"): ≤5 sözcük, tümü Büyük Harfle başlar (bağlaçlar hariç), cümle noktalaması yok.
    /// Küçük harfli fiil benzeri sözcük ("Get in your car") varsa ad değildir.
    /// </summary>
    public static bool IsNameLabel(string s)
    {
        var t = s.Trim(' ', '\t');
        if (t.Any(c => ".!?,;:…".Contains(c))) return false;
        var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Any(char.IsLetter)).ToList();
        if (words.Count is < 1 or > 5) return false;
        return words.All(w =>
            (NameConnectors.Contains(w.ToLowerInvariant()) && w != words[0]) || char.IsUpper(w.First(char.IsLetter)));
    }

    internal static int WordCount(string s) =>
        s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter));

    internal static bool IsSentenceLike(string s) => WordCount(s) >= 3 && s.Any(char.IsLower);

    /// <summary>Satırları birleştirir; satır sonu tireli sözcük ("main-" + "tenance") bitiştirilir.</summary>
    public static string Join(IEnumerable<string> lines)
    {
        var o = "";
        foreach (var l in lines)
        {
            var t = l.Trim(' ', '\t');
            if (t.Length == 0) continue;
            if (o.Length == 0) { o = t; continue; }
            if (o.EndsWith('-') && o.Length >= 2 && char.IsLetter(o[^2]) && char.IsLower(t[0])) o = o[..^1] + t;
            else o += " " + t;
        }
        return o;
    }

    private sealed record Item(string Text, double Left, double Top, double Right, double Bottom);

    private static List<Item> ToItems(IEnumerable<OcrTextLine> lines, Region region, bool dropChrome)
    {
        double w = region.Right - region.Left, h = region.Bottom - region.Top;
        var items = new List<Item>();
        foreach (var line in lines)
        {
            var text = StripTrailingGlyph(OcrNoise.Clean(GlyphNormalizer.Normalize(line.Text)));
            if (text.Length == 0) continue;
            if (dropChrome && IsPhoneChrome(text)) continue;
            var b = line.Box;
            items.Add(new Item(text, region.Left + b.MinX * w, region.Top + (1 - (b.Y + b.Height)) * h,
                region.Left + b.MaxX * w, region.Top + (1 - b.Y) * h));
        }
        return items.OrderBy(i => i.Top).ToList();
    }

    /// <summary>OCR satırları (kırpma içinde normalize, başlangıç sol-alt) → en iyi bilgi bloğu ya da null.</summary>
    public static InfoBlock? BestBlock(IEnumerable<OcrTextLine> lines, Region region)
    {
        var items = ToItems(lines, region, false);
        var groups = new List<List<Item>>();
        foreach (var it in items)
        {
            if (groups.Count > 0)
            {
                var cur = groups[^1];
                var prev = cur[^1];
                var lh = prev.Bottom - prev.Top;
                if (Math.Abs(it.Left - prev.Left) <= 0.02 && it.Top - prev.Bottom < 1.6 * lh) { cur.Add(it); continue; }
            }
            groups.Add([it]);
        }
        (InfoBlock Block, int Words)? best = null;
        foreach (var g in groups)
        {
            var text = Join(g.Select(i => i.Text));
            var words = WordCount(text);
            if (words < 4 || !g.Any(i => IsSentenceLike(i.Text))) continue;
            var heights = g.Select(i => i.Bottom - i.Top).Order().ToList();
            var rect = ScreenBox.FromEdges(g.Min(i => i.Left), g.Min(i => i.Top), g.Max(i => i.Right), g.Max(i => i.Bottom));
            if (best is null || words > best.Value.Words) best = (new InfoBlock(text, rect, heights[heights.Count / 2]), words);
        }
        return best?.Block;
    }

    private static readonly Regex TimeRx = new(@"^\s*\d{1,2}\s*[:.]\s*\d{2}\s*([AaPp]\.?[Mm]\.?)?\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex PercentRx = new(@"^\s*\d{1,3}\s*%\s*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Telefon ekranındaki arayüz kırıntısı mı: saat, pil yüzdesi, harf içermeyen satır, tek sözcüklük kısa BÜYÜK HARF başlık.
    /// "OK"/"LOL" gibi kısa gerçek mesajlar elenmez.
    /// </summary>
    internal static bool IsPhoneChrome(string t)
    {
        if (TimeRx.IsMatch(t) || PercentRx.IsMatch(t)) return true;
        if (!t.Any(char.IsLetter)) return true;
        var words = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            var letters = t.Count(char.IsLetter);
            if (letters is >= 4 and <= 14 && !t.Any(char.IsLower)) return true;
        }
        return false;
    }

    /// <summary>
    /// Telefon mesajları: satırlar üstten alta okunur, arayüz kırıntıları atılır. Dikey boşluk splitGap × satır yüksekliğinden
    /// büyükse ayrı balon (ayrı blok).
    /// </summary>
    public static List<InfoBlock> Blocks(IEnumerable<OcrTextLine> lines, Region region, double splitGap = 1.2)
    {
        var items = ToItems(lines, region, true);
        var groups = new List<List<Item>>();
        foreach (var it in items)
        {
            if (groups.Count > 0)
            {
                var prev = groups[^1][^1];
                var lh = Math.Max(prev.Bottom - prev.Top, 0.001);
                if (it.Top - prev.Bottom <= splitGap * lh) { groups[^1].Add(it); continue; }
            }
            groups.Add([it]);
        }
        var res = new List<InfoBlock>();
        foreach (var g in groups)
        {
            var text = Join(g.Select(i => i.Text));
            if (WordCount(text) < 2 || !text.Any(char.IsLower)) continue;
            var heights = g.Select(i => i.Bottom - i.Top).Order().ToList();
            res.Add(new InfoBlock(text, ScreenBox.FromEdges(g.Min(i => i.Left), g.Min(i => i.Top), g.Max(i => i.Right), g.Max(i => i.Bottom)),
                heights[heights.Count / 2]));
        }
        return res;
    }

    /// <summary>Birden çok balonu izleyiciye tek blok olarak vermek için (metinler satır sonuyla birleşir, kutu birleşimi).</summary>
    public static InfoBlock? Combined(IReadOnlyList<InfoBlock> blocks)
    {
        if (blocks.Count == 0) return null;
        var rect = blocks.Skip(1).Aggregate(blocks[0].Box, (a, b) => a.Union(b.Box));
        var hs = blocks.Select(b => b.LineHeight).Order().ToList();
        return new InfoBlock(string.Join('\n', blocks.Select(b => b.Text)), rect, hs[hs.Count / 2]);
    }
}

public enum InfoBoxActionKind { None, Translate, Clear }

public readonly record struct InfoBoxAction(InfoBoxActionKind Kind, InfoBlock? Block = null)
{
    public static readonly InfoBoxAction None = new(InfoBoxActionKind.None);
    public static readonly InfoBoxAction Clear = new(InfoBoxActionKind.Clear);
    public static InfoBoxAction Translate(InfoBlock b) => new(InfoBoxActionKind.Translate, b);
}

/// <summary>
/// Bilgi kutusu kararlılığı: metin 2 geçiş boyunca benzer kalınca çevrilir. Kutu ancak kesintisiz ClearAfter sn kaybolunca
/// temizlenir; ekranda kalan aynı metin yeniden gönderilmez. Zaman dışarıdan verilir (saniye, monoton).
/// </summary>
public sealed class InfoBoxTracker
{
    public const double ClearAfter = 2.0;
    private string? candidate;
    private int stableCount;
    private string? shown;
    private bool visible;
    private double? absentSince;

    public void Reset() { candidate = null; stableCount = 0; shown = null; visible = false; absentSince = null; }

    public InfoBoxAction Feed(InfoBlock? block, double now)
    {
        if (block is null)
        {
            candidate = null; stableCount = 0;
            absentSince ??= now;
            if (visible && now - absentSince.Value >= ClearAfter) { visible = false; return InfoBoxAction.Clear; }
            return InfoBoxAction.None;
        }
        absentSince = null;
        var norm = TextMatch.Normalize(block.Text);
        if (candidate is not null && TextMatch.IsSimilar(candidate, norm, 0.9)) stableCount++; else { candidate = norm; stableCount = 1; }
        if (stableCount < 2) return InfoBoxAction.None;
        if (visible && shown is not null && TextMatch.IsSimilar(shown, norm, 0.9)) return InfoBoxAction.None;
        shown = norm; visible = true;
        return InfoBoxAction.Translate(block);
    }

    /// <summary>Çeviri başarısız olduysa yeniden denensin.</summary>
    public void ForgetShown() { shown = null; visible = false; }
}

/// <summary>Aynı (ya da ≥0.9 benzer) metni kısa sürede yeniden göndermemek için.</summary>
public sealed class ResendGuard
{
    public const double Window = 8;
    private readonly List<(string Text, double At)> recent = [];

    public void Reset() => recent.Clear();

    /// <summary>true: gönder (ve kaydet). false: son 8 sn içinde aynısı gitti.</summary>
    public bool ShouldSend(IEnumerable<string> texts, double time)
    {
        recent.RemoveAll(r => time - r.At > Window);
        var norms = texts.Select(TextMatch.Normalize).Where(n => n.Length > 0).ToList();
        foreach (var n in norms)
            if (recent.Any(r => TextMatch.IsSimilar(r.Text, n, 0.9))) return false;
        foreach (var n in norms) recent.Add((n, time));
        return true;
    }
}
