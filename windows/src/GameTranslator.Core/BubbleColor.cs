// SPDX-License-Identifier: AGPL-3.0-only

namespace GameTranslator.Core;

/// <summary>
/// Telefon balonlarının zemin rengi: oyuncunun balonları MAVİ (beyaz yazı), karşı tarafınkiler beyaz/açık gri (koyu yazı).
/// Konum güvenilmez; renk güvenilir. Mac: mac/BubbleColor.swift. Piksel erişimi çağıranda: BGRA bayt tamponu + satır adımı verilir.
/// </summary>
public static class BubbleColor
{
    /// <summary>Sıkı mavi kuralı: B belirgin baskın ve doygunluk yeterli.</summary>
    public static bool IsBlue(double r, double g, double b)
    {
        var mx = Math.Max(r, Math.Max(g, b));
        var mn = Math.Min(r, Math.Min(g, b));
        if (mx <= 0) return false;
        return b > r + 40 && b > g + 15 && (mx - mn) / mx > 0.25;
    }

    /// <summary>
    /// <paramref name="rect"/>: satır kutusu (kareye göre normalize, sol-üst). Biraz genişletilmiş bölgenin EN SIK rengi (4 bit/kanal kutuları,
    /// kazanan kutunun ortalaması) balon zeminidir; yazı pikselleri azınlıkta kaldığı için dışarıda kalır. null: bölge okunamadı.
    /// </summary>
    /// <param name="bgra">B,G,R,A sıralı satırlar; <paramref name="stride"/> bir satırın bayt uzunluğu.</param>
    public static (double R, double G, double B)? DominantColor(ReadOnlySpan<byte> bgra, int stride, int width, int height, ScreenBox rect)
    {
        const double padX = 0.004;
        double w = width, h = height;
        var x0 = Math.Max(Math.Floor((rect.MinX - padX) * w), 0);
        var y0 = Math.Max(Math.Floor(rect.MinY * h), 0);
        var x1 = Math.Min(Math.Ceiling((rect.MaxX + padX) * w), w);
        var y1 = Math.Min(Math.Ceiling(rect.MaxY * h), h);
        double pw = x1 - x0, ph = y1 - y0;
        if (pw < 2 || ph < 2 || stride < width * 4 || bgra.Length < (long)stride * (height - 1) + width * 4L) return null;
        int cw = (int)Math.Min(pw, 96), ch = (int)Math.Min(ph, 48);
        var count = new int[4096];
        var sum = new long[4096 * 3];
        for (var j = 0; j < ch; j++)
        {
            var sy = Math.Min((int)(y0 + (j + 0.5) * ph / ch), height - 1);
            for (var i = 0; i < cw; i++)
            {
                var sx = Math.Min((int)(x0 + (i + 0.5) * pw / cw), width - 1);
                var o = sy * stride + sx * 4;
                int b = bgra[o], g = bgra[o + 1], r = bgra[o + 2];
                var k = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
                count[k]++;
                sum[k * 3] += r; sum[k * 3 + 1] += g; sum[k * 3 + 2] += b;
            }
        }
        var best = 0;
        for (var k = 1; k < 4096; k++) if (count[k] > count[best]) best = k;
        if (count[best] == 0) return null;
        double n = count[best];
        return (sum[best * 3] / n, sum[best * 3 + 1] / n, sum[best * 3 + 2] / n);
    }

    /// <summary>true: mavi (oyuncunun balonu), false: beyaz/gri, null: okunamadı.</summary>
    public static bool? IsBlueBubble(ReadOnlySpan<byte> bgra, int stride, int width, int height, ScreenBox rect) =>
        DominantColor(bgra, stride, width, height, rect) is { } c ? IsBlue(c.R, c.G, c.B) : null;

    /// <summary>
    /// <see cref="ScreenText.Analyze"/>'ın bubbleBlue parametresi için: tam kare BGRA tamponunu yakalayan geri çağrı.
    /// Tampon, analiz bitene kadar geçerli kalmalıdır.
    /// </summary>
    public static Func<ScreenBox, bool?> ForFrame(byte[] bgra, int stride, int width, int height) =>
        rect => IsBlueBubble(bgra, stride, width, height, rect);
}
