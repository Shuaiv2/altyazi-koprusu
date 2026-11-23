// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace GameTranslator.Core;

/// <summary>Tam kare OCR'ından gelen tek satır. Kutu tüm karede normalize (0..1), başlangıç SOL-ÜST. Mac: ScreenLine.</summary>
/// <param name="BlueBubble">Yalnızca telefon balonu: zemin MAVİ mi? null: bilinmiyor (analyze'ın bubbleBlue geri çağrısı/konum kuralı kullanılır).</param>
public sealed record ScreenLine(string Text, ScreenBox Rect, float Confidence = 1, bool? BlueBubble = null);

public enum ScreenMode { Game, Map, Menu, Phone }

public enum ScreenKind { Objective, Info, Notify, Description, Phone }

/// <summary>Telefon konuşmasındaki tek balon. <c>FromMe</c>: oyuncunun kendi yanıtı.</summary>
public sealed record PhoneMessage(bool FromMe, string Text);

/// <summary>Elde tutulan telefon (mesaj listesi ya da açık konuşma). <c>IsList</c>: hiçbir şey çevrilmez.</summary>
public sealed record PhoneInfo(ScreenBox Box, bool IsList);

/// <summary>Sınıflandırılmış ekran metni bloğu. Mac: ScreenBlock.</summary>
public sealed record ScreenBlock(ScreenKind Kind, string Text, string? Title, string? Speaker, ScreenBox Box, double LineHeight, string Slot)
{
    /// <summary>Yalnızca açık telefon konuşması: sıralı mesajlar (Text = hepsi "\n" ile birleşik).</summary>
    public IReadOnlyList<PhoneMessage> Messages { get; init; } = [];
}

public sealed record ScreenAnalysis(ScreenMode Mode, IReadOnlyList<ScreenBlock> Blocks, PhoneInfo? Phone);

/// <summary>
/// Tam kare OCR satırlarından oyun içi metin bloklarını çıkaran saf mantık. Mac: mac/ScreenText.swift.
/// Oyun modunda yalnızca objective/info/notify üretilir; serbest metin (tabela, reklam panosu) asla çevrilmez.
/// </summary>
public static class ScreenText
{
    internal sealed class L
    {
        public string Text = "";
        public string Raw = "";
        public double X0, Y0, X1, Y1;
        public double H => Y1 - Y0;
        public double Cx => (X0 + X1) / 2;
        public int Words => InfoBox.WordCount(Text);
    }

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MapPromptRx = Rx(@"(ADD MARKER|RECENTER|CLOSE MENU|2D VIEW|3D VIEW|SET DESTINATION|REMOVE DESTINATION)");
    private static readonly Regex MenuSelectRx = Rx(@"^\W*SELECT\b");
    private static readonly Regex MenuBackRx = Rx(@"^\W*(BACK|EXIT)\b");
    private static readonly Regex ToastRx = Rx(@"(Uzaktan\s*Oynama|Remote\s*Play)");
    private static readonly Regex MoneyRx = Rx(@"^\W{0,2}[\$€£]?\s*\d[\d,\.]*\W*$");
    private static readonly Regex ClockRx = Rx(@"^\s*\d{1,2}\s*[:.]\s*\d{2}\s*([ap]\.?m\.?)?\s*$");
    private static readonly Regex DistanceRx = Rx(@"^\s*\d+([.,]\d+)?\s*(ft|mi|m|km|yd|yds)\.?\s*$");
    private static readonly Regex GpsRx = Rx(@"^\s*(turn|keep|continue|head|take|make a|bear|merge)\s+(left|right|straight|u-?turn|ahead)\b");
    private static readonly Regex CallRx = Rx(@"(incoming call|dialing|dialling|calling\.|call ended|missed call|call failed|^\s*\d{1,2}\s*[:=]\s*\d{2}\b)");
    private static readonly Regex ArrowRx = Rx(@"^\s*[→➜➔>»]");

    internal static bool IsCall(string t) => CallRx.IsMatch(t);
    internal static bool HasLower(string s) => s.Any(char.IsLower);
    internal static bool IsCapsOnly(string s) => s.Any(char.IsLetter) && !HasLower(s);

    /// <summary>Tüm satırlara bakarak ekran modu (ham metinle; BÜYÜK HARF istemleri bu yüzden görülür).</summary>
    public static ScreenMode DetectMode(IReadOnlyList<ScreenLine> lines)
    {
        var texts = lines.Where(l => l.Confidence >= 0.3).Select(l => l.Text.Trim(' ', '\t'));
        if (texts.Any(MapPromptRx.IsMatch)) return ScreenMode.Map;
        var lowRight = lines.Where(l => l.Rect.MinY > 0.84 && l.Rect.MinX > 0.7).Select(l => l.Text).ToList();
        if (lowRight.Any(MenuSelectRx.IsMatch) && lowRight.Any(MenuBackRx.IsMatch)) return ScreenMode.Menu;
        return ScreenMode.Game;
    }

    /// <summary>Ortak elemeler: düşük güven, PS bildirimi, kenara değen, para/saat/mesafe/GPS, arama arayüzü, ok istemi.</summary>
    internal static List<L> Prepare(IReadOnlyList<ScreenLine> lines)
    {
        var res = new List<L>();
        foreach (var line in lines)
        {
            if (line.Confidence < 0.5) continue;
            var raw = line.Text.Trim();
            if (ToastRx.IsMatch(raw)) continue;
            var r = line.Rect;
            if (!(r.MinX >= 0.003 && r.MaxX <= 0.997 && r.MinY >= 0 && r.MaxY <= 1.0)) continue;
            var text = InfoBox.StripLeadingIcon(InfoBox.StripTrailingGlyph(OcrNoise.Clean(GlyphNormalizer.Normalize(raw))));
            if (text.Length == 0 || !text.Any(char.IsLetter)) continue;
            if (MoneyRx.IsMatch(text) || ClockRx.IsMatch(text) || DistanceRx.IsMatch(text) || GpsRx.IsMatch(text)) continue;
            if (IsCall(text) || ArrowRx.IsMatch(raw)) continue;
            res.Add(new L { Text = text, Raw = InfoBox.StripLeadingIcon(GlyphNormalizer.Normalize(raw)), X0 = r.MinX, Y0 = r.MinY, X1 = r.MaxX, Y1 = r.MaxY });
        }
        return res;
    }

    /// <summary>
    /// Satırları bloklara gruplar: sol ya da orta hizalı, dikeyde bitişik (boşluk &lt; 1 satır yüksekliği), benzer yükseklik.
    /// Soldaki öğretici kutularda satır başındaki simge ikinci satırı sağa iter; sol kenarı gruptan en çok 0.035 içeride olan satır da kabul edilir.
    /// </summary>
    internal static List<List<L>> Group(IEnumerable<L> items)
    {
        var groups = new List<List<L>>();
        foreach (var it in items.OrderBy(i => i.Y0))
        {
            (int Idx, double Gap)? best = null;
            for (var i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var p = g[^1];
                var gap = it.Y0 - p.Y1;
                var rf = Math.Max(Math.Max(p.H, it.H), 0.001);
                if (!(gap >= -0.2 * rf && gap < 1.0 * rf)) continue;
                var ratio = Math.Min(p.H, it.H) / rf;
                if (ratio < 0.55) continue;
                var gx0 = g.Min(x => x.X0);
                var left = Math.Abs(it.X0 - p.X0) < 0.015 || Math.Abs(it.X0 - gx0) < 0.015;
                var center = Math.Abs(it.Cx - p.Cx) < 0.015;
                var indent = gx0 < 0.08 && it.X0 >= gx0 - 0.01 && it.X0 <= gx0 + 0.035;
                if (!(left || center || indent)) continue;
                if (best is null || gap < best.Value.Gap) best = (i, gap);
            }
            if (best is { } b) groups[b.Idx].Add(it); else groups.Add([it]);
        }
        return groups;
    }

    private sealed class Cand(List<L> lines)
    {
        public List<L> Lines { get; } = lines;
        public string Text => GlyphNormalizer.StripIconRemnants(InfoBox.Join(Lines.Select(l => l.Text)));
        public double X0 => Lines.Min(l => l.X0);
        public double X1 => Lines.Max(l => l.X1);
        public double Y0 => Lines.Min(l => l.Y0);
        public double Y1 => Lines.Max(l => l.Y1);
        public double Lh { get { var h = Lines.Select(l => l.H).Order().ToList(); return h[h.Count / 2]; } }
        public ScreenBox Rect => ScreenBox.FromEdges(X0, Y0, X1, Y1);
    }

    private static bool IsCapitalizedName(string s)
    {
        var w = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (w.Length is < 1 or > 2) return false;
        return w.All(x => char.IsUpper(x[0]) && x.All(c => char.IsLetter(c) || c == '\'' || c == '-'));
    }

    /// <summary>
    /// Tam kare satırlarından ekran modu + bloklar (+ telefon). <paramref name="bubbleBlue"/>: telefon konuşmasında bir satır kutusunun
    /// balon zemini mavi mi (oyuncunun balonu)? null döner ya da verilmezse konum kuralı (IsMine) kullanılır. Yalnızca açık konuşma
    /// bulunduğunda çağrılır (bkz. <see cref="BubbleColor.ForFrame"/>).
    /// </summary>
    public static ScreenAnalysis Analyze(IReadOnlyList<ScreenLine> lines, Func<ScreenBox, bool?>? bubbleBlue = null)
    {
        var bm = DetectMode(lines);
        if (bm != ScreenMode.Map && DetectPhone(lines) is { } det)
        {
            var (info, chain) = det;
            IReadOnlyList<ScreenBlock> pb = info.IsList ? [] : PhoneBlocks(chain, info.Box, bubbleBlue);
            return new ScreenAnalysis(ScreenMode.Phone, pb, info);
        }
        var items = Prepare(lines);
        return bm == ScreenMode.Game
            ? new ScreenAnalysis(ScreenMode.Game, GameBlocks(items), null)
            : new ScreenAnalysis(bm, DescriptionBlocks(items), null);
    }

    // --- Telefon ---

    private static readonly HashSet<string> PhoneHeaders = ["suggested", "messages", "message", "contacts", "recent", "recents", "favorites", "favourites", "calls"];
    private static readonly Regex TruncRx = Rx(@"(\.\.\.|…|\s\.)\s*$");
    private static readonly HashSet<string> PhoneSystemLabels = ["new message", "new messages", "today", "yesterday", "scroll", "send", "reply"];

    private static string TrimNonLetters(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && !char.IsLetter(s[a])) a++;
        while (b > a && !char.IsLetter(s[b - 1])) b--;
        return s[a..b];
    }

    private static string TrimNonLettersKeepSpace(string s)
    {
        bool Junk(char c) => !char.IsLetter(c) && !char.IsWhiteSpace(c);
        int a = 0, b = s.Length;
        while (a < b && Junk(s[a])) a++;
        while (b > a && Junk(s[b - 1])) b--;
        return s[a..b];
    }

    /// <summary>
    /// Dar dikey sütun: ≥5 üst üste kısa satır. Liste yapısı (başlık sözcüğü ya da ≥2 "..." ile biten önizleme) varsa liste; yoksa
    /// ancak sağ altta BACK istemi, ≥4 cümle benzeri satır ve kesik satırsız ise açık konuşma.
    /// </summary>
    internal static (PhoneInfo Info, List<L> Chain)? DetectPhone(IReadOnlyList<ScreenLine> lines)
    {
        var cand = new List<L>();
        foreach (var line in lines)
        {
            if (!(line.Confidence >= 0.3 && line.Rect.Width <= 0.16 && line.Rect.MinX >= 0.003 && line.Rect.MaxX <= 0.997)) continue;
            var raw = line.Text.Trim();
            if (ToastRx.IsMatch(raw) || MapPromptRx.IsMatch(raw) || MenuSelectRx.IsMatch(raw) || MenuBackRx.IsMatch(raw)) continue;
            var t = InfoBox.StripLeadingIcon(GlyphNormalizer.Normalize(raw));
            if (!t.Any(char.IsLetter)) continue;
            if (MoneyRx.IsMatch(t) || ClockRx.IsMatch(t) || DistanceRx.IsMatch(t)) continue;
            var r = line.Rect;
            cand.Add(new L { Text = t, Raw = t, X0 = r.MinX, Y0 = r.MinY, X1 = r.MaxX, Y1 = r.MaxY });
        }
        var chains = new List<List<L>>();
        foreach (var it in cand.OrderBy(c => c.Y0))
        {
            (int, double)? best = null;
            for (var i = 0; i < chains.Count; i++)
            {
                var p = chains[i][^1];
                var gap = it.Y0 - p.Y1;
                var mh = Math.Max(p.H, it.H);
                if (!(gap >= -0.6 * mh && gap <= Math.Max(0.05, 3.2 * mh) && Math.Abs(it.Cx - p.Cx) <= 0.10)) continue;
                if (best is null || gap < best.Value.Item2) best = (i, gap);
            }
            if (best is { } b) chains[b.Item1].Add(it); else chains.Add([it]);
        }
        List<L>? chain = null;
        foreach (var c in chains) if (chain is null || c.Count > chain.Count) chain = c;
        if (chain is null || chain.Count < 5 || chain[^1].Y1 - chain[0].Y0 < 0.18) return null;
        var texts = chain.Select(c => c.Text).ToList();
        var hasHeader = texts.Any(t => PhoneHeaders.Contains(TrimNonLetters(t.ToLowerInvariant())));
        var truncated = texts.Count(TruncRx.IsMatch);
        var rect = chain.Skip(1).Aggregate(new ScreenBox(chain[0].X0, chain[0].Y0, chain[0].X1 - chain[0].X0, chain[0].H),
            (a, c) => a.Union(new ScreenBox(c.X0, c.Y0, c.X1 - c.X0, c.H)));
        var box = rect.Inset(-0.03, -0.02).Intersection(new ScreenBox(0, 0, 1, 1)) ?? rect;
        if (hasHeader || truncated >= 2) return (new PhoneInfo(box, true), chain);
        // Açık konuşma
        var backPrompt = lines.Any(l => l.Rect.MinY > 0.84 && l.Rect.MinX > 0.7 && MenuBackRx.IsMatch(l.Text));
        var sentences = chain.Count(c => c.Words >= 3 && HasLower(c.Text));
        var words = chain.Sum(c => c.Words);
        var cx = chain.Sum(c => c.Cx) / chain.Count;
        if (!(backPrompt && truncated == 0 && chain.Count >= 4 && sentences >= 2 && words >= 8 && cx >= 0.3)) return null;
        return (new PhoneInfo(box, false), chain);
    }

    /// <summary>Açık konuşma: dikey boşluğu ≥0.8 satır yüksekliği olan ya da sol kenarı ≥0.03 kayan satırlar ayrı balondur.</summary>
    private static List<ScreenBlock> PhoneBlocks(List<L> chainIn, ScreenBox box, Func<ScreenBox, bool?>? bubbleBlue)
    {
        var chain = chainIn.OrderBy(c => c.Y0).ToList();
        chain.RemoveAll(c => PhoneSystemLabels.Contains(TrimNonLettersKeepSpace(c.Text.ToLowerInvariant())));
        // Üstteki kısa özel ad satırı konuşmanın başlığıdır (karşı taraf): gönderen olarak taşınır, gövdeye girmez.
        string? speaker = null;
        if (chain.Count >= 2 && IsCapitalizedName(chain[0].Text) && chain[1].Y0 - chain[0].Y1 > 0.02)
        {
            speaker = chain[0].Text;
            chain.RemoveAt(0);
        }
        // Renk ipucu (satır başına): mavi = oyuncu. Yan yana iki satırın rengi farklıysa ya da (renk biliniyorsa) aralarında boşluk
        // varsa ayrı balondur.
        var hint = chain.Select(c => bubbleBlue?.Invoke(new ScreenBox(c.X0, c.Y0, c.X1 - c.X0, c.H))).ToList();
        var groups = new List<List<L>>();
        var groupHints = new List<List<bool?>>();
        for (var i = 0; i < chain.Count; i++)
        {
            var it = chain[i];
            if (groups.Count > 0)
            {
                var p = groups[^1][^1];
                var ph = groupHints[^1][^1];
                var sameColor = ph is null || hint[i] is null || ph == hint[i];
                var hasColor = ph is not null && hint[i] is not null;
                var gap = it.Y0 - p.Y1;
                var close = gap < 0.8 * Math.Max(p.H, 0.001) && (!hasColor || gap < 0.2 * Math.Max(p.H, 0.001));
                if (sameColor && close && Math.Abs(it.X0 - p.X0) < 0.03)
                {
                    groups[^1].Add(it); groupHints[^1].Add(hint[i]);
                    continue;
                }
            }
            groups.Add([it]); groupHints.Add([hint[i]]);
        }
        var msgs = new List<PhoneMessage>();
        ScreenBox? rect = null;
        var lhs = new List<double>();
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var c = new Cand(groups[gi]);
            var ctext = c.Text;
            if (InfoBox.WordCount(ctext) < 2 || !HasLower(ctext)) continue;
            var known = groupHints[gi].Where(h => h is not null).Select(h => h!.Value).ToList();
            var mine = known.Count == 0 ? IsMine(c.Rect, box) : known.Count(k => k) * 2 > known.Count;
            msgs.Add(new PhoneMessage(mine, ctext));
            rect = rect is { } r0 ? r0.Union(c.Rect) : c.Rect;
            lhs.Add(c.Lh);
        }
        if (rect is not { } rr || msgs.Count == 0) return [];
        lhs.Sort();
        return [new ScreenBlock(ScreenKind.Phone, string.Join('\n', msgs.Select(m => m.Text)), null, speaker, rr, lhs[lhs.Count / 2], "phone") { Messages = msgs }];
    }

    /// <summary>Balon telefon kutusunun merkezinin sağındaysa ve sağ kenara sola olduğundan yakınsa oyuncunundur (sağa yaslı).</summary>
    internal static bool IsMine(ScreenBox bubble, ScreenBox box) =>
        bubble.MidX > box.MidX && (box.MaxX - bubble.MaxX) < (bubble.MinX - box.MinX);

    // --- Oyun modu ---

    private static List<ScreenBlock> GameBlocks(List<L> items)
    {
        // Küçük harfsiz satırlar (tabela) en baştan dışarıda.
        var cands = Group(items.Where(i => HasLower(i.Text))).Select(g => new Cand(g)).ToList();
        var res = new List<ScreenBlock>();

        // Bildirim: gönderen satırı (1-2 büyük harfli sözcük, mesajdan sağa girintili, daha küçük) + mesaj bloğu
        var used = new HashSet<int>();
        for (var si = 0; si < cands.Count; si++)
        {
            var s = cands[si];
            if (!(s.Lines.Count == 1 && s.X0 > 0.74 && s.Y0 >= 0.75 && s.Y0 <= 0.97 && IsCapitalizedName(s.Text))) continue;
            var mi = -1;
            for (var k = 0; k < cands.Count; k++)
            {
                var m = cands[k];
                var gap = m.Y0 - s.Y1;
                var off = s.X0 - m.X0;
                if (k != si && !used.Contains(k) && gap >= -0.005 && gap < 1.3 * Math.Max(s.Lh, m.Lh) && off >= 0.012 && off <= 0.06
                    && s.Lh <= 0.92 * m.Lh && m.X1 >= 0.9 && m.X1 <= 0.997 && m.X0 > 0.74) { mi = k; break; }
            }
            if (mi < 0) continue;
            var mc = cands[mi];
            var mtext = mc.Text;
            if (InfoBox.WordCount(mtext) < 2) continue;
            used.Add(si); used.Add(mi);
            res.Add(new ScreenBlock(ScreenKind.Notify, mtext, null, s.Text, mc.Rect.Union(s.Rect), mc.Lh, "notify-bottom"));
        }
        cands = cands.Where((_, i) => !used.Contains(i)).ToList();

        foreach (var c in cands)
        {
            var text = c.Text;
            // Küçük harfle başlayan blok bir parçadır (kesik tabela/pano ya da simge arkasından kalan yazı): çevrilmez.
            if (text.Length > 0 && char.IsLower(text[0])) continue;
            var words = InfoBox.WordCount(text);
            var cxMid = (c.X0 + c.X1) / 2;
            // Salt ad/etiket ("Key Lento Coffee Morning"): çevrilmez.
            var nameLike = InfoBox.IsNameLabel(text);
            if (c.Lines.Count == 1 && words >= 2 && Math.Abs(cxMid - 0.5) <= 0.12 && c.Y0 > 0.93)
            {
                if (nameLike) continue;
                res.Add(new ScreenBlock(ScreenKind.Objective, text, null, null, c.Rect, c.Lh, "objective"));
            }
            else if (c.X0 < 0.08 && c.Y0 >= 0.18 && c.Y0 <= 0.55 && words >= 3 && InfoBox.IsSentenceLike(text))
            {
                if (nameLike) continue;
                res.Add(new ScreenBlock(ScreenKind.Info, text, null, null, c.Rect, c.Lh, "info"));
            }
            else if (c.X0 > 0.74)
            {
                if (c.Y0 >= 0.75 && c.Y0 <= 0.97 && c.X1 >= 0.9 && words >= 2)
                    // gönderensiz bildirim ("Workout Incomplete / Gains lost")
                    res.Add(new ScreenBlock(ScreenKind.Notify, text, null, null, c.Rect, c.Lh, "notify-bottom"));
                else if (c.Y0 >= 0.20 && c.Y0 <= 0.45 && c.X1 >= 0.93 && c.Lines.Count <= 2 && words >= 2)
                    res.Add(new ScreenBlock(ScreenKind.Notify, text, null, null, c.Rect, c.Lh, "notify-top"));
            }
        }
        return res.OrderBy(b => b.Box.MinY).ToList();
    }

    // --- Harita / menü ---

    private static List<ScreenBlock> DescriptionBlocks(List<L> items)
    {
        var cands = Group(items.Where(i => HasLower(i.Text))).Select(g => new Cand(g)).ToList();
        var caps = items.Where(i => IsCapsOnly(i.Text) && i.Text.Count(char.IsLetter) >= 3
            && !MapPromptRx.IsMatch(i.Text) && !MenuSelectRx.IsMatch(i.Text) && !MenuBackRx.IsMatch(i.Text)).ToList();
        var res = new List<ScreenBlock>();
        foreach (var c in cands)
        {
            // İlk satır başlık gibiyse (≤6 sözcük, noktalamasız, ardından BÜYÜK harfle başlayan satır) gövdeden ayrı parça olur.
            var segments = new List<List<L>> { c.Lines };
            if (c.Lines.Count >= 2 && c.Lines[0].Words <= 6 && c.Lines[0].Text.Length > 0
                && !".!?\"”’:".Contains(c.Lines[0].Text[^1]) && c.Lines[1].Text.Length > 0 && char.IsUpper(c.Lines[1].Text[0]))
                segments = [[c.Lines[0]], c.Lines.Skip(1).ToList()];
            var text = string.Join('\n', segments.Select(s => InfoBox.Join(s.Select(l => l.Text))));
            var flat = InfoBox.Join(c.Lines.Select(l => l.Text));
            var words = InfoBox.WordCount(flat);
            // Cümle gibi: noktalama ile biter (liste öğeleri bitmez), ≥4 sözcük.
            if (words < 4 || flat.Length == 0 || !".!?\"”’".Contains(flat[^1])) continue;
            if (!(c.Lines.Count >= 2 || words >= 6)) continue;
            string? title = null;
            var box = c.Rect;
            L? t = null;
            foreach (var cp in caps)
            {
                if (cp.Y1 <= c.Y0 + 0.004 && c.Y0 - cp.Y1 < 1.5 * Math.Max(cp.H, c.Lh) && Math.Abs(cp.X0 - c.X0) <= 0.05 && cp.Words >= 1
                    && (t is null || t.Y1 < cp.Y1)) t = cp;
            }
            if (t is not null)
            {
                title = t.Raw.Trim(' ', '\t');
                box = box.Union(new ScreenBox(t.X0, t.Y0, t.X1 - t.X0, t.H));
            }
            res.Add(new ScreenBlock(ScreenKind.Description, text, title, null, box, c.Lh, "description"));
        }
        return res.OrderBy(b => b.Box.MinY).ToList();
    }
}

public abstract record ScreenAction
{
    public sealed record Translate(ScreenBlock Block) : ScreenAction;
    public sealed record Clear(string Slot, ScreenKind Kind) : ScreenAction;
}

/// <summary>
/// Blok kararlılığı: yuva başına izleyici. Aynı metin (≥0.85 benzer) 2 ardışık tam kare geçişinde görülünce çevrilir;
/// kesintisiz <see cref="ClearAfter"/> sn kaybolunca temizlenir. Ekranda kalan aynı metin yeniden gönderilmez.
/// Mod değişince (2 ardışık geçiş) o modda üretilmeyen türler hemen temizlenir. Zaman dışarıdan verilir. Mac: ScreenTracker.
/// </summary>
public sealed class ScreenTracker
{
    public const double ClearAfter = 2.0;
    public const double Similarity = 0.85;
    /// <summary>Mod değişimi için gereken ardışık geçiş sayısı (game↔menu titremesini önler).</summary>
    public const int ModeConfirm = 2;

    private sealed class Track(ScreenKind kind)
    {
        public ScreenKind Kind = kind;
        public string Candidate = "";
        public int Stable;
        public string? Shown;
        public bool Visible;
        public double? AbsentSince;
        /// <summary>Bu görünürlük döneminde yuvada gösterilmiş sürümler (titreyen kısa sondaki sözcük yeniden gönderilmesin).</summary>
        public HashSet<string> History = [];
        /// <summary>Telefon konuşması: gösterilen mesaj sayısı ve son mesaj.</summary>
        public int ShownCount;
        public string ShownLast = "";
    }

    private readonly Dictionary<string, Track> tracks = [];
    private ScreenMode mode = ScreenMode.Game;
    private ScreenMode? pendingMode;
    private int pendingCount;

    /// <summary>Hata ayıklama/susturma için: onaylanmış (debounce edilmiş) mod.</summary>
    public ScreenMode CurrentMode => mode;

    public void Reset() { tracks.Clear(); mode = ScreenMode.Game; pendingMode = null; pendingCount = 0; }

    private static bool Produces(ScreenMode m, ScreenKind k) => m switch
    {
        ScreenMode.Game => k is ScreenKind.Objective or ScreenKind.Info or ScreenKind.Notify,
        ScreenMode.Map or ScreenMode.Menu => k == ScreenKind.Description,
        _ => k == ScreenKind.Phone,
    };

    /// <summary>Bu geçişte görünen blokları ver; yapılacak eylemler döner.</summary>
    public List<ScreenAction> Feed(IEnumerable<ScreenBlock> blocks, ScreenMode newMode, double now)
    {
        var actions = new List<ScreenAction>();
        if (newMode == mode) { pendingMode = null; pendingCount = 0; }
        else
        {
            if (pendingMode == newMode) pendingCount++; else { pendingMode = newMode; pendingCount = 1; }
            if (pendingCount >= ModeConfirm)
            {
                // Mod 2 ardışık geçişte görüldü: yeni modda üretilmeyen türleri hemen kaldır.
                foreach (var (slot, t) in tracks.ToList().Where(kv => !Produces(newMode, kv.Value.Kind)).Select(kv => (kv.Key, kv.Value)))
                {
                    if (t.Visible) actions.Add(new ScreenAction.Clear(slot, t.Kind));
                    tracks.Remove(slot);
                }
                mode = newMode;
                pendingMode = null; pendingCount = 0;
            }
        }
        var seen = new HashSet<string>();
        foreach (var b in blocks)
        {
            // Aynı yuvada birden fazla blok varsa ilk (en üstteki) alınır.
            if (!seen.Add(b.Slot)) continue;
            if (!tracks.TryGetValue(b.Slot, out var t)) t = new Track(b.Kind);
            t.AbsentSince = null;
            var norm = TextMatch.Normalize((b.Title is null ? "" : b.Title + " ") + b.Text);
            if (t.Candidate.Length > 0 && TextMatch.IsSimilar(t.Candidate, norm, Similarity)) t.Stable++; else t.Stable = 1;
            t.Candidate = norm;   // her zaman en son okunanı tut (kayarak gelen bildirim tamamlanınca güncellensin)
            var lastNorm = TextMatch.Normalize(b.Messages.Count > 0 ? b.Messages[^1].Text : "");
            // Yeni mesaj göründü ya da son mesaj değişti (kaydırma): metin ≥0.85 benzese de yeniden gönder.
            var grown = t.Visible && b.Messages.Count > 0
                && (b.Messages.Count > t.ShownCount || !TextMatch.IsSimilar(t.ShownLast, lastNorm, Similarity));
            if (t.Stable >= 2)
            {
                if (t.Visible && !grown && t.Shown is { } s && TextMatch.IsSimilar(s, norm, Similarity) && norm.Length < s.Length + 3)
                {
                    // aynı metin ekranda (uzamadı): yeniden gönderme
                }
                else if (t.Visible && t.Shown is { } s2 && norm.Length > 0 && s2.StartsWith(norm + " ", StringComparison.Ordinal))
                {
                    // yalnızca sondaki sözcükler düştü (OCR titremesi): yeniden gönderme
                }
                else if (t.Visible && t.History.Contains(norm))
                {
                    // bu görünürlük döneminde zaten gösterilmiş sürüm: yeniden gönderme
                }
                else
                {
                    t.Shown = norm; t.Visible = true; t.History.Add(norm);
                    t.ShownCount = b.Messages.Count; t.ShownLast = lastNorm;
                    actions.Add(new ScreenAction.Translate(b));
                }
            }
            tracks[b.Slot] = t;
        }
        foreach (var (slot, t) in tracks.ToList().Where(kv => !seen.Contains(kv.Key)).Select(kv => (kv.Key, kv.Value)))
        {
            t.Candidate = ""; t.Stable = 0;
            t.AbsentSince ??= now;
            if (t.Visible && now - t.AbsentSince.Value >= ClearAfter)
            {
                actions.Add(new ScreenAction.Clear(slot, t.Kind));
                tracks.Remove(slot);
            }
            else if (!t.Visible && now - t.AbsentSince.Value >= ClearAfter) tracks.Remove(slot);
        }
        return actions;
    }

    /// <summary>Çeviri başarısızsa aynı blok yeniden denensin.</summary>
    public void ForgetShown(string slot)
    {
        if (tracks.TryGetValue(slot, out var t)) { t.Shown = null; t.Visible = false; t.History = []; }
    }
}

/// <summary>
/// Altyazı bandı (kırpma) OCR'ından gelen satırlar için diyalog denetimi: kesik önizleme, telefon/bildirim içindeki satır,
/// ortalanmamış satır elenir; tam kare okuması ile çapraz doğrulama yapılır. Kutular tüm karede normalize, sol-üst başlangıç.
/// Mac: DialogueGate.
/// </summary>
public static class DialogueGate
{
    public enum Reason { EllipsisPreview, InsideExcluded, OffCenter, ObjectiveBand }

    /// <summary>Satır kutusunun ≥%60'ı dışlanan bir alanın içinde mi (telefon alanı, bildirim kartı).</summary>
    internal static bool IsInside(ScreenBox r, IEnumerable<ScreenBox> areas)
    {
        var a = Math.Max(r.Width * r.Height, 1e-9);
        return areas.Any(area => r.Intersection(area) is { } i && i.Width * i.Height / a >= 0.6);
    }

    private static readonly Regex EllipsisRx = new(@"(\.\.\.|…)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex CommaEllipsisRx = new(@",\s*(\.\.\.|…)\s*$", RegexOptions.CultureInvariant);

    public static Reason? ReasonFor(ScreenLine line, IReadOnlyList<ScreenBox> exclude)
    {
        var r = line.Rect;
        var text = line.Text.Trim();
        if (IsInside(r, exclude)) return Reason.InsideExcluded;
        if (r.MinY >= 0.94) return Reason.ObjectiveBand;
        double cx = r.MidX, w = r.Width;
        if (Math.Abs(cx - 0.5) > 0.12 && w <= 0.35) return Reason.OffCenter;
        // "Welcome to BuckMe, ..." gibi kesik önizleme. Gerçek diyalogda sona yaslanan "you know..." satırı korunur:
        // yalnızca ", ..." ile biten ya da ≥3 sözcüğü çok dar (<0.15) kutuya sığdırılmış satır kesik sayılır.
        if (EllipsisRx.IsMatch(text))
        {
            if (CommaEllipsisRx.IsMatch(text)) return Reason.EllipsisPreview;
            if (w < 0.15 && InfoBox.WordCount(text) >= 3) return Reason.EllipsisPreview;
        }
        return null;
    }

    /// <summary>Korunan satırların indeksleri ve elenenler.</summary>
    public static (List<int> Kept, List<(int Index, Reason Reason)> Dropped) Evaluate(IReadOnlyList<ScreenLine> lines, IReadOnlyList<ScreenBox> exclude)
    {
        var kept = new List<int>();
        var dropped = new List<(int, Reason)>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (ReasonFor(lines[i], exclude) is { } r) dropped.Add((i, r)); else kept.Add(i);
        }
        return (kept, dropped);
    }

    /// <summary>
    /// Son tam kare geçişinde (≤~1 sn önce) alt bantta ortalanmış satır kümesi kırpma metnine ≥0.6 benziyorsa o metin döner.
    /// </summary>
    public static (string Text, ScreenBox Rect)? CrossCheck(string cropText, IReadOnlyList<ScreenLine> screen, IReadOnlyList<ScreenBox> exclude)
    {
        var target = TextMatch.Normalize(cropText);
        if (target.Length == 0) return null;
        var cands = new List<(string Text, ScreenBox Rect)>();
        foreach (var l in screen)
        {
            if (!(l.Confidence >= 0.5 && l.Rect.MinY >= 0.70 && l.Rect.MaxY <= 0.94)) continue;
            if (ReasonFor(l, exclude) is not null) continue;
            var text = OcrNoise.Clean(GlyphNormalizer.Normalize(l.Text.Trim()));
            if (text.Length == 0 || SubtitleFilter.LineReason(text, new SubtitleFilter.Options()) is not null) continue;
            cands.Add((text, l.Rect));
        }
        cands = cands.OrderBy(c => c.Rect.MinY).ToList();
        var groups = new List<List<(string Text, ScreenBox Rect)>>();
        foreach (var c in cands)
        {
            if (groups.Count > 0 && c.Rect.MinY - groups[^1][^1].Rect.MaxY < Math.Max(groups[^1][^1].Rect.Height, c.Rect.Height)) groups[^1].Add(c);
            else groups.Add([c]);
        }
        (string Text, ScreenBox Rect, double Score)? best = null;
        foreach (var g in groups)
        {
            var text = InfoBox.Join(g.Select(c => c.Text));
            var n = TextMatch.Normalize(text);
            if (!TextMatch.IsSimilar(n, target, 0.6)) continue;
            var score = Similarity(n, target);
            if (best is null || score > best.Value.Score) best = (text, g.Skip(1).Aggregate(g[0].Rect, (a, c) => a.Union(c.Rect)), score);
        }
        return best is { } b ? (b.Text, b.Rect) : null;
    }

    private static double Similarity(string a, string b)
    {
        double lo = 0, hi = 1;
        for (var i = 0; i < 8; i++)
        {
            var mid = (lo + hi) / 2;
            if (TextMatch.IsSimilar(a, b, mid)) lo = mid; else hi = mid;
        }
        return lo;
    }
}
