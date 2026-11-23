// SPDX-License-Identifier: AGPL-3.0-only

namespace GameTranslator.Core;

/// <summary>Telefon sohbetindeki bir çevrilmiş mesaj (TV: <c>messages[{from,text,source}]</c>).</summary>
public sealed record ScreenMessage(bool FromMe, string Text, string Source);

/// <summary>
/// TV'ye ve bilgisayar katmanına giden tek bir ekran çevirisi (Mac: sendScreen öğesi). <paramref name="Placement"/>:
/// "over" (İngilizce öğenin üstünü örter) ya da "beside" (telefonun yanında sohbet paneli). <paramref name="Theme"/>:
/// "light" | "dark" (kutunun zemini). Kutu karenin kenarlarına oranla, sol-üst başlangıçlı.
/// </summary>
public sealed record ScreenItem(string Text, string Source, TextBox Box, double LineHeight, string Placement, string Theme,
    string? Speaker = null, IReadOnlyList<ScreenMessage>? Messages = null)
{
    public static ScreenItem Over(ScreenBlock block, string text, string source, string theme) =>
        new(text, source, ToTextBox(block.Box), block.LineHeight, "over", theme, block.Speaker);

    public static TextBox ToTextBox(ScreenBox box) => new(box.MinX, box.MinY, box.MaxX, box.MaxY);
}

/// <summary>Kutu bölgesinin açık/koyu zemini: ortalama parlaklık &gt; 0.55 ise "light" (Mac: NativeApp.theme).</summary>
public static class ScreenTheme
{
    public const double LightAbove = 0.55;

    public static string Of(Frame frame, ScreenBox box)
    {
        int x0 = (int)Math.Max(0, Math.Floor(box.MinX * frame.Width)), y0 = (int)Math.Max(0, Math.Floor(box.MinY * frame.Height));
        int x1 = (int)Math.Min(frame.Width, Math.Ceiling(box.MaxX * frame.Width)), y1 = (int)Math.Min(frame.Height, Math.Ceiling(box.MaxY * frame.Height));
        if (x1 - x0 < 1 || y1 - y0 < 1) return "dark";
        const int n = 16;
        double sum = 0;
        for (var j = 0; j < n; j++)
        {
            var y = Math.Min(y0 + (int)((j + 0.5) * (y1 - y0) / n), frame.Height - 1);
            for (var i = 0; i < n; i++)
            {
                var x = Math.Min(x0 + (int)((i + 0.5) * (x1 - x0) / n), frame.Width - 1);
                var o = (y * frame.Stride) + (x * 4);
                sum += (0.299 * frame.Pixels[o + 2]) + (0.587 * frame.Pixels[o + 1]) + (0.114 * frame.Pixels[o]);
            }
        }
        return sum / (n * n) / 255.0 > LightAbove ? "light" : "dark";
    }
}
