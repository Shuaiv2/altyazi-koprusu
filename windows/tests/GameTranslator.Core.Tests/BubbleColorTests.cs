// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

public class BubbleColorTests
{
    private const int W = 200, H = 100;

    /// <summary>BGRA kare: zemin <paramref name="bg"/>, içine bir dikdörtgen <paramref name="fill"/>.</summary>
    private static byte[] Frame((byte r, byte g, byte b) bg, ScreenBox? box = null, (byte r, byte g, byte b)? fill = null, (byte, byte, byte)? text = null)
    {
        var buf = new byte[W * H * 4];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var c = bg;
                if (box is { } bx && fill is { } f && x >= bx.MinX * W && x < bx.MaxX * W && y >= bx.MinY * H && y < bx.MaxY * H) c = f;
                var o = (y * W + x) * 4;
                buf[o] = c.b; buf[o + 1] = c.g; buf[o + 2] = c.r; buf[o + 3] = 255;
            }
        return buf;
    }

    [Theory]
    [InlineData(10, 120, 255, true)]
    [InlineData(0, 122, 255, true)]
    [InlineData(255, 255, 255, false)]
    [InlineData(235, 235, 240, false)]
    [InlineData(30, 30, 30, false)]
    [InlineData(0, 0, 0, false)]
    [InlineData(120, 120, 130, false)]
    public void IsBlue_Rule(double r, double g, double b, bool expected) => Assert.Equal(expected, BubbleColor.IsBlue(r, g, b));

    [Fact]
    public void BlueBubbleOnWhiteBackground_DetectedByDominantColor()
    {
        var box = new ScreenBox(0.5, 0.3, 0.3, 0.1);
        var buf = Frame((240, 240, 240), box, (10, 120, 255));
        Assert.True(BubbleColor.IsBlueBubble(buf, W * 4, W, H, box));
        Assert.False(BubbleColor.IsBlueBubble(buf, W * 4, W, H, new ScreenBox(0.05, 0.6, 0.3, 0.1)));
    }

    [Fact]
    public void TextPixelsAreMinority()
    {
        // Mavi zeminde beyaz yazı çizgileri: baskın renk yine mavi.
        var box = new ScreenBox(0.5, 0.3, 0.3, 0.1);
        var buf = Frame((240, 240, 240), box, (10, 120, 255));
        for (var x = 105; x < 160; x += 3)
            for (var y = 33; y < 37; y++)
            {
                var o = (y * W + x) * 4;
                buf[o] = buf[o + 1] = buf[o + 2] = 255;
            }
        Assert.True(BubbleColor.IsBlueBubble(buf, W * 4, W, H, box));
    }

    [Fact]
    public void UnreadableRegion_ReturnsNull()
    {
        var buf = Frame((240, 240, 240));
        Assert.Null(BubbleColor.IsBlueBubble(buf, W * 4, W, H, new ScreenBox(2, 2, 0.1, 0.1)));
        Assert.Null(BubbleColor.IsBlueBubble(buf, W * 4, W, H, new ScreenBox(0.1, 0.1, 0.0001, 0.0001)));
    }

    [Fact]
    public void ForFrame_FeedsAnalyze()
    {
        var buf = Frame((240, 240, 240), new ScreenBox(0.5, 0.3, 0.3, 0.1), (10, 120, 255));
        var hint = BubbleColor.ForFrame(buf, W * 4, W, H);
        Assert.True(hint(new ScreenBox(0.5, 0.3, 0.3, 0.1)));
        Assert.False(hint(new ScreenBox(0.05, 0.6, 0.3, 0.1)));
    }
}
