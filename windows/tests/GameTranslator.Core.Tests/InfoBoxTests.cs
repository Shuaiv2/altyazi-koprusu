// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// <summary>mac/tests/InfoBoxTests.swift'in karşılığı.</summary>
public class InfoBoxTests
{
    private static readonly InfoBox.Region Region = new(0.01, 0.24, 0.27, 0.44);

    /// <summary>Kırpma içinde (y yukarı) satır: kare y (üst) → Vision y (alt başlangıç).</summary>
    private static OcrTextLine VLine(string text, double frameTop, double h = 0.026, double left = 0.02, double w = 0.18)
    {
        var x = (left - Region.Left) / (Region.Right - Region.Left);
        var top = (frameTop - Region.Top) / (Region.Bottom - Region.Top);
        var hh = h / (Region.Bottom - Region.Top);
        return new OcrTextLine(text, new OcrRect(x, 1 - top - hh, w / (Region.Right - Region.Left), hh));
    }

    private static readonly InfoBox.Region PRegion = new(0.66, 0.20, 0.99, 0.90);

    private static OcrTextLine PLine(string text, double top, double h = 0.025, double left = 0.70, double w = 0.2)
    {
        var x = (left - PRegion.Left) / (PRegion.Right - PRegion.Left);
        var t = (top - PRegion.Top) / (PRegion.Bottom - PRegion.Top);
        var hh = h / (PRegion.Bottom - PRegion.Top);
        return new OcrTextLine(text, new OcrRect(x, 1 - t - hh, w / (PRegion.Right - PRegion.Left), hh));
    }

    [Fact]
    public void TrailingGlyphAndHyphenJoin()
    {
        Assert.Equal("In the bedroom, you can open the wardrobe", InfoBox.StripTrailingGlyph("In the bedroom, you can open the wardrobe &"));
        Assert.Equal("You can sleep to advance time", InfoBox.StripTrailingGlyph("You can sleep to advance time ="));
        Assert.Equal("Collect cash from your mailbox.", InfoBox.StripTrailingGlyph("Collect cash from your mailbox."));
        Assert.Equal("Any maintenance is your problem.", InfoBox.Join(["Any main-", "tenance is your problem."]));
    }

    [Fact]
    public void BestBlock_GroupsAndFilters()
    {
        var block = InfoBox.BestBlock([
            VLine("This is your house. Brian lets you stay", 0.28),
            VLine("here but any maintenance is your problem. &", 0.308),
            VLine("Menu", 0.40, left: 0.10, w: 0.05),
        ], Region);
        Assert.Equal("This is your house. Brian lets you stay here but any maintenance is your problem.", block?.Text);
        Assert.True(Math.Abs(block!.Box.MinX - 0.02) < 0.002 && Math.Abs(block.Box.MinY - 0.28) < 0.002);
        Assert.Null(InfoBox.BestBlock([VLine("Save game now", 0.3)], Region));
        Assert.Null(InfoBox.BestBlock([VLine("WEAPON WHEEL OPTIONS MENU", 0.3)], Region));
        Assert.Equal("This is your house and it is nice.", InfoBox.BestBlock([VLine("This is your house and it is nice.", 0.28), VLine("Another unrelated sentence here too.", 0.40)], Region)?.Text);
        Assert.Equal("Hold L1 + R1 to leave trails when your weapon is unequipped while driving.",
            InfoBox.BestBlock([VLine("to leave trails when your weapon is unequipped while driving.", 0.308), VLine("Hold LI + RI", 0.28)], Region)?.Text);
        Assert.Equal("Press ✕ to skip this tutorial text now", InfoBox.BestBlock([VLine("Press ⓧ to skip this tutorial text now", 0.3)], Region)?.Text);
        Assert.Equal("You can open the map any time by pressing ✕", InfoBox.BestBlock([VLine("You can open the map any time by pressing ⓧ", 0.3)], Region)?.Text);
    }

    [Fact]
    public void InfoBoxTracker_Behaviour()
    {
        var b1 = new InfoBlock("This is your house. Brian lets you stay here", ScreenBox.Zero, 0.026);
        var b2 = new InfoBlock("This is your house. Brian lets you stay here.", ScreenBox.Zero, 0.026);
        var t = new InfoBoxTracker();
        Assert.Equal(InfoBoxAction.None, t.Feed(b1, 0));
        Assert.Equal(InfoBoxAction.Translate(b2), t.Feed(b2, 0.5));
        Assert.Equal(InfoBoxAction.None, t.Feed(b1, 1));
        Assert.Equal(InfoBoxAction.None, t.Feed(null, 1.5));
        Assert.Equal(InfoBoxAction.None, t.Feed(null, 2.0));
        Assert.Equal(InfoBoxAction.None, t.Feed(null, 2.5));
        Assert.Equal(InfoBoxAction.None, t.Feed(b1, 3.0));
        Assert.Equal(InfoBoxAction.None, t.Feed(b2, 3.5));
        Assert.Equal(InfoBoxAction.None, t.Feed(null, 10));
        Assert.Equal(InfoBoxAction.None, t.Feed(null, 11.5));
        Assert.Equal(InfoBoxAction.Clear, t.Feed(null, 12.1));
        Assert.Equal(InfoBoxAction.None, t.Feed(null, 13));
        Assert.Equal(InfoBoxAction.None, t.Feed(b1, 20));
        Assert.Equal(InfoBoxAction.Translate(b2), t.Feed(b2, 20.5));
        var resent = 0;
        for (var i = 0; i < 120; i++) if (t.Feed(i % 2 == 0 ? b1 : b2, 21 + i * 0.5).Kind == InfoBoxActionKind.Translate) resent++;
        Assert.Equal(0, resent);
        t.ForgetShown();
        Assert.True(t.Feed(b1, 100) == InfoBoxAction.Translate(b1) || t.Feed(b2, 100.5) == InfoBoxAction.Translate(b2));
        var b3 = new InfoBlock("Press the button to open the weapon wheel now", ScreenBox.Zero, 0.026);
        Assert.Equal(InfoBoxAction.None, t.Feed(b3, 101));
        Assert.Equal(InfoBoxAction.Translate(b3), t.Feed(b3, 101.5));
    }

    [Theory]
    [InlineData("Hold L1 + R1 to leave trails when your weapon is unequipped while driving.", "Hold L1 + R1 to leave trails when your weapon is unequipped while driving.")]
    [InlineData("Hold LI + RI to leave trails", "Hold L1 + R1 to leave trails")]
    [InlineData("Hold Ll+Rl to leave trails", "Hold L1 + R1 to leave trails")]
    [InlineData("Hold L| + R| to go", "Hold L1 + R1 to go")]
    [InlineData("Hold [L1] + [R1] to leave trails", "Hold L1 + R1 to leave trails")]
    [InlineData("Hold (L2) and (R2) to aim", "Hold L2 and R2 to aim")]
    [InlineData("Press ⓧ to jump", "Press ✕ to jump")]
    [InlineData("Press (X) to jump", "Press ✕ to jump")]
    [InlineData("Press X to jump", "Press ✕ to jump")]
    [InlineData("Tap O or X to dodge", "Tap ○ or ✕ to dodge")]
    [InlineData("Hold X + O to dodge", "Hold ✕ + ○ to dodge")]
    [InlineData("Press oa to open the map", "Press [button] to open the map")]
    [InlineData("Hold L1 + @ to aim", "Hold L1 + [button] to aim")]
    [InlineData("Press ⊗ or ⭕ to confirm", "Press ✕ or ○ to confirm")]
    [InlineData("Press ◻ to reload and ▵ to swap", "Press □ to reload and △ to swap")]
    [InlineData("Press the OPTIONS button to pause", "Press the OPTIONS button to pause")]
    [InlineData("Press the options button to pause", "Press the OPTIONS button to pause")]
    [InlineData("Press [Options] to pause", "Press OPTIONS to pause")]
    [InlineData("Press the touchpad button to look", "Press the TOUCHPAD button to look")]
    [InlineData("Malcolm X was a leader.", "Malcolm X was a leader.")]
    [InlineData("Check your options before you go.", "Check your options before you go.")]
    [InlineData("Hold on, I am coming.", "Hold on, I am coming.")]
    [InlineData("Take the O train to X street.", "Take the O train to X street.")]
    public void GlyphNormalizer_Normalize(string input, string expected) => Assert.Equal(expected, GlyphNormalizer.Normalize(input));

    [Fact]
    public void GlyphNormalizer_TokensAndGameEnglish()
    {
        Assert.Equal(["L1", "R1", "✕"], GlyphNormalizer.Tokens("Hold L1 + R1 ve ✕ bas"));
        Assert.Equal("Hold L1 + R1 to leave trails", GameEnglish.Plain("Hold L1 + R1 to leave trails"));
    }

    [Fact]
    public void PhoneMessages_Blocks()
    {
        var one = InfoBox.Blocks([PLine("Hey, where are you? I have been", 0.40), PLine("calling you all morning.", 0.43)], PRegion);
        Assert.Single(one);
        Assert.Equal("Hey, where are you? I have been calling you all morning.", one[0].Text);
        Assert.True(Math.Abs(one[0].Box.MinX - 0.70) < 0.002 && Math.Abs(one[0].Box.MinY - 0.40) < 0.002);
        var two = InfoBox.Blocks([PLine("Meet me at the marina.", 0.30), PLine("Ok, on my way now.", 0.50), PLine("Bring the cash.", 0.53)], PRegion);
        Assert.Equal(["Meet me at the marina.", "Ok, on my way now. Bring the cash."], two.Select(b => b.Text));
        Assert.Equal(["First bubble here.", "Second bubble here."],
            InfoBox.Blocks([PLine("Second bubble here.", 0.60), PLine("First bubble here.", 0.30)], PRegion).Select(b => b.Text));
        var chrome = InfoBox.Blocks([PLine("12:45", 0.22), PLine("MESSAGES", 0.25), PLine("87%", 0.23), PLine("Lucia", 0.28), PLine("Call me when you land.", 0.45), PLine("9.05 PM", 0.60)], PRegion);
        Assert.Equal(["Call me when you land."], chrome.Select(b => b.Text));
        Assert.Empty(InfoBox.Blocks([PLine("OK", 0.4)], PRegion));
        Assert.Equal(["On my way"], InfoBox.Blocks([PLine("On my way", 0.4)], PRegion).Select(b => b.Text));
        Assert.Equal("Press L1 to reply now", InfoBox.Blocks([PLine("Press LI to reply now", 0.4)], PRegion)[0].Text);
        Assert.True(InfoBox.IsPhoneChrome("12:45") && InfoBox.IsPhoneChrome("9.05 pm") && InfoBox.IsPhoneChrome("LOCKED") && !InfoBox.IsPhoneChrome("LOL") && !InfoBox.IsPhoneChrome("Hello"));
        var comb = InfoBox.Combined(two);
        Assert.Equal("Meet me at the marina.\nOk, on my way now. Bring the cash.", comb?.Text);
        Assert.True(comb!.Box.MinY < 0.31 && comb.Box.MaxY > 0.55);
        Assert.Null(InfoBox.Combined([]));
    }

    [Fact]
    public void ResendGuard_Window()
    {
        var g = new ResendGuard();
        Assert.True(g.ShouldSend(["Bu ses nedir?", "What is that sound?"], 0));
        Assert.False(g.ShouldSend(["Bu ses nedir?", "x"], 3));
        Assert.False(g.ShouldSend(["Başka", "What is that sound"], 4));
        Assert.False(g.ShouldSend(["Bu ses nedir ?!", ""], 5));
        Assert.True(g.ShouldSend(["Tamamen farklı bir cümle", "Totally different"], 6));
        Assert.True(g.ShouldSend(["Bu ses nedir?", "What is that sound?"], 9.5));
    }

    [Theory]
    [InlineData("Keep cash safe by storing it in your stash a or by depositing it into BuckMe at an ATM IP,", "Keep cash safe by storing it in your stash or by depositing it into BuckMe at an ATM")]
    [InlineData("Incapacitations will increase your focus bar o depending on your criminal profile.", "Incapacitations will increase your focus bar depending on your criminal profile.")]
    [InlineData("Press © to open @ the • map", "Press to open the map")]
    [InlineData("Grab a car and go", "Grab a car and go")]
    [InlineData("I am at the Plan A.", "I am at the Plan A.")]
    [InlineData("Meet at Gate B", "Meet at Gate B")]
    [InlineData("Use the ATM.", "Use the ATM.")]
    [InlineData("Buy a gun, a car", "Buy a gun, a car")]
    [InlineData("Plan A.", "Plan A.")]
    [InlineData("Rob the store a", "Rob the store")]
    public void StripIconRemnants(string input, string expected) => Assert.Equal(expected, GlyphNormalizer.StripIconRemnants(input));
}
