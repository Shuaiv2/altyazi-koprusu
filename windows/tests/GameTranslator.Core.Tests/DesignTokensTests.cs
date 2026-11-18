// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// Tasarım değerleri design/tokens.json'dan gelir (Mac sürümü de aynı dosyayı kullanır).
/// Bu testler yazıların her iki görünümde de rahat okunmasını güvenceye alır.
public class DesignTokensTests
{
    private readonly DesignTokens tokens = DesignTokens.Shared;

    private void AssertContrast(string text, string background, double minimum)
    {
        foreach (var dark in new[] { false, true })
        {
            var foreground = tokens.Color[text].For(dark);
            var back = tokens.Color[background].For(dark);
            var ratio = DesignTokens.Contrast(foreground, back);
            Assert.True(ratio >= minimum, $"{text} / {background} ({foreground} – {back}): {ratio}");
        }
    }

    [Fact]
    public void TokensAreBundled()
    {
        Assert.Equal("GameTranslator", tokens.Name);
        foreach (var (name, pair) in tokens.Color)
        {
            Assert.True(DesignTokens.Rgb(pair.Light) is not null, name);
            Assert.True(DesignTokens.Rgb(pair.Dark) is not null, name);
        }
    }

    [Fact]
    public void ContrastFormulaMatchesWcag()
    {
        Assert.Equal(21, DesignTokens.Contrast("#000000", "#FFFFFF"), 2);
        Assert.Equal(4.48, DesignTokens.Contrast("#777777", "#FFFFFF"), 2);
    }

    [Fact]
    public void MainTextIsHighlyReadable()
    {
        AssertContrast("textPrimary", "background", 7);
        AssertContrast("textPrimary", "surface", 7);
    }

    [Fact]
    public void SecondaryAndColoredTextIsReadable()
    {
        foreach (var text in new[] { "textSecondary", "accentText", "success", "warning", "danger" })
        {
            AssertContrast(text, "background", 4.5);
            AssertContrast(text, "surface", 4.5);
        }
        AssertContrast("accentText", "accentSoft", 4.5);
    }

    /// <summary>Onaylı ölçekler (design/tokens.json ve docs/TASARIM.md): iki köşe değeri (denetim 10, kart 14),
    /// yazı ölçeği 12/14/15/17/22/30, kalınlık en çok yarı kalın; parıltı tokenı yok.</summary>
    [Fact]
    public void ScalesAreTheApprovedOnes()
    {
        Assert.Equal(new SortedSet<double> { 10, 14 }, new SortedSet<double>(tokens.Radius.Values));
        Assert.All(tokens.Font.Values, font => Assert.Contains(font.Size, new[] { 12.0, 14, 15, 17, 22, 30 }));
        Assert.All(tokens.Font.Values, font => Assert.Contains(font.Weight, new[] { "regular", "medium", "semibold" }));
        using var stream = typeof(DesignTokens).Assembly.GetManifestResourceStream("tokens.json")!;
        using var json = System.Text.Json.JsonDocument.Parse(stream);
        Assert.False(json.RootElement.TryGetProperty("glow", out _));
    }

    [Fact]
    public void ButtonTextIsReadable()
    {
        AssertContrast("onAccent", "accent", 4.5);
        AssertContrast("onDanger", "danger", 4.5);
    }

    [Fact]
    public void TextIsLargerThanSystemDefault()
    {
        Assert.True(tokens.Font["body"].Size >= 15);
        Assert.True(tokens.Font["caption"].Size >= 12);
    }
}

