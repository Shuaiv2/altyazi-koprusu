// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Shell;

/// <summary>Ekran kartının yeri (sayfa pikseli).</summary>
public readonly record struct CardRect(double Left, double Top, double Width, double Height, double MaxHeight);

/// <summary>Kartın renkleri (alfa 0-1).</summary>
public readonly record struct OverCardStyle(bool Light, double Alpha, (byte R, byte G, byte B) Background, (byte R, byte G, byte B) Text);

/// <summary>"over" kartının metin parçaları: konuşmacı ayrı satır, gövde "\n" ile bölünür; ilk parça başlık ise kalın.</summary>
public sealed record OverParts(string Speaker, IReadOnlyList<string> Segments, bool TitleBold);

/// <summary>
/// TV'deki "ekranı çevir" kartlarının yerleşim ve sığdırma hesabı (tv/webos/subtitle-settings.js: overRect, overFontSize,
/// overCardStyle, overParts, overMaxGrow, overFit, besideRect, besideFontSize, besideFirstVisible, besideMessages,
/// stackRects). Bilgisayar katmanı aynı sonucu üretsin diye birebir aktarılmıştır; sayfa pikseli: genişlik 1920.
/// </summary>
public static class ScreenCards
{
    public const double OverMinFont = 18;
    public const double StackGap = 8;

    private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    private static double Round(double v) => Math.Floor(v + 0.5);

    /// <summary>Türkçe kart İngilizce kutunun tam üstünü örter; içerik uzunsa aşağı (sığmazsa yukarı) uzar.</summary>
    public static CardRect OverRect(TextBox box, double vw, double vh, double margin, double contentHeight)
    {
        double w = Math.Max(1, vw), h = Math.Max(1, vh);
        var m = Math.Max(0, Math.Min(margin, Math.Floor(w / 2) - 1));
        var boxW = Math.Max(0, (box.Right - box.Left) * w);
        var width = Math.Min(Math.Max(boxW, 0.18 * w), w - (2 * m));
        var left = Clamp(box.Left * w, m, w - m - width);
        var vm = Math.Min(m, 8);
        var boxTop = box.Top * h;
        var boxBottom = Math.Max(boxTop, box.Bottom * h);
        var boxH = boxBottom - boxTop;
        var ch = Math.Max(boxH, contentHeight);
        var top = boxTop;
        if (top + ch > h - vm) top = Math.Max(vm, boxBottom - ch);
        if (top + ch > h - vm) top = Math.Max(vm, h - vm - ch);
        var maxH = Math.Max(h - vm - boxTop, boxBottom - vm);
        return new CardRect(Round(left), Round(top), Round(width), Round(boxH), Math.Max(0, Round(maxH)));
    }

    /// <summary>Yazı boyu İngilizce satır yüksekliğinden (0..1): 18 ile kullanıcının altyazı boyu arasına sıkışır.</summary>
    public static double OverFontSize(double line, double vh, double userFont)
    {
        var ln = line > 0 ? line : 0.03;
        var px = Round(0.75 * ln * Math.Max(1, vh));
        return Clamp(px, OverMinFont, Math.Max(OverMinFont, userFont));
    }

    /// <summary>Açık tema neredeyse beyaz, koyu tema yarı opak siyah; kullanıcının zemin saydamlığı alfayı artırabilir (en az 0.92).</summary>
    public static OverCardStyle OverStyle(string? theme, double bgOpacity)
    {
        var light = theme == "light";
        var baseAlpha = light ? 0.98 : 0.93;
        var user = Clamp(bgOpacity, 0, 100) / 100;
        var alpha = Math.Round(Math.Min(1, Math.Max(0.92, Math.Max(baseAlpha, user))), 2);
        return light
            ? new OverCardStyle(true, alpha, (246, 246, 248), (0x15, 0x16, 0x1a))
            : new OverCardStyle(false, alpha, (14, 14, 18), (255, 255, 255));
    }

    public static OverParts Parts(ScreenItem item)
    {
        var speaker = item.Speaker?.Trim() ?? "";
        var segments = item.Text.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        return new OverParts(speaker, segments, speaker.Length == 0 && segments.Count > 1);
    }

    /// <summary>Kartın en çok büyüyebileceği yükseklik: İngilizce kutunun 1.6 katı, ekran sınırını aşmaz.</summary>
    public static double OverMaxGrow(double boxHeightPx, double rectMaxHeight)
    {
        var bh = Math.Max(0, boxHeightPx);
        var cap = Math.Max(0, rectMaxHeight);
        return Math.Max(0, Math.Min(Round(bh * 1.6), cap));
    }

    /// <summary>true: yazıyı küçült (sığmadı ve en küçük boya inilmedi).</summary>
    public static bool ShouldShrink(double contentHeight, double limitHeight, double size) =>
        contentHeight > limitHeight && size > OverMinFont;

    /// <summary>Telefon panelinin yeri: telefonun soluna, yer yoksa sağına; genişlik min(0.24 w, 460).</summary>
    public static (CardRect Rect, bool Left) BesideRect(TextBox box, double vw, double vh, double margin)
    {
        double w = Math.Max(1, vw), h = Math.Max(1, vh);
        var m = Math.Max(0, Math.Min(margin, Math.Floor(w / 4)));
        const double gap = 24;
        var width = Math.Min(0.24 * w, 460);
        double bl = box.Left * w, br = box.Right * w;
        var vm = Math.Min(m, 24);
        var boxTop = box.Top * h;
        var boxH = Math.Max(0, (box.Bottom - box.Top) * h);
        var maxH = Math.Max(60, Math.Min(boxH, h - (2 * vm)));
        var top = Clamp(boxTop, vm, Math.Max(vm, h - vm - maxH));
        double leftRoom = bl - gap - m, rightRoom = w - m - (br + gap);
        bool placedLeft;
        double left;
        if (leftRoom >= width) { placedLeft = true; left = bl - gap - width; }
        else if (rightRoom >= width) { placedLeft = false; left = br + gap; }
        else
        {
            placedLeft = leftRoom >= rightRoom;
            width = Math.Max(1, Math.Min(width, Math.Max(leftRoom, rightRoom)));
            left = placedLeft ? bl - gap - width : br + gap;
        }
        left = Clamp(left, m, Math.Max(m, w - m - width));
        return (new CardRect(Round(left), Round(top), Round(width), 0, Round(maxH)), placedLeft);
    }

    /// <summary>Sohbet panel yazı boyu: 1080p'de 23 px, yükseklikle ölçeklenir, en az 18.</summary>
    public static double BesideFontSize(double vh) => Math.Max(18, Round(23 * Math.Max(1, vh) / 1080));

    /// <summary>İlk görünen balonun indeksi: en eski balonlar atılır, en yeni her zaman kalır.</summary>
    public static int BesideFirstVisible(IReadOnlyList<double> heights, double gap, double avail)
    {
        var n = heights.Count;
        if (n == 0) return 0;
        double used = 0;
        var first = n;
        for (var i = n - 1; i >= 0; i--)
        {
            var add = Math.Max(0, heights[i]) + (first == n ? 0 : Math.Max(0, gap));
            if (first != n && used + add > avail) break;
            used += add;
            first = i;
        }
        return first;
    }

    /// <summary>Geçerli iletiler (boş metin atılır).</summary>
    public static IReadOnlyList<ScreenMessage> Messages(ScreenItem item) =>
        (item.Messages ?? []).Where(m => !string.IsNullOrWhiteSpace(m.Text)).Select(m => m with { Text = m.Text.Trim() }).ToList();

    /// <summary>Aynı bölgedeki kutular üst üste binmesin: çakışan kutu öncekinin altına itilir.</summary>
    public static IReadOnlyList<CardRect> Stack(IReadOnlyList<CardRect> rects, double gap)
    {
        var g = Math.Max(0, gap);
        var output = new List<CardRect>();
        foreach (var rect in rects)
        {
            var r = rect;
            foreach (var p in output)
            {
                var overlapX = r.Left < p.Left + p.Width && p.Left < r.Left + r.Width;
                var overlapY = r.Top < p.Top + p.Height && p.Top < r.Top + r.Height;
                if (overlapX && overlapY) r = r with { Top = p.Top + p.Height + g };
            }
            output.Add(r);
        }
        return output;
    }
}
