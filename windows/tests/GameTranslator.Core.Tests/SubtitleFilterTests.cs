// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// <summary>mac/tests/SubtitleFilterTests.swift'in birebir karşılığı.</summary>
public class SubtitleFilterTests
{
    private static OcrTextLine Line(string text, double x = 0.2, double w = 0.6, double y = 0.5, double h = 0.2, float conf = 0.9f) =>
        new(text, new OcrRect(x, y, w, h), conf);

    private static string[] Texts(params OcrTextLine[] lines) => [.. SubtitleFilter.Filter(lines).Select(l => l.Text)];

    // (Swift dosyasındaki yinelenen "Geri" satırı xunit'te aynı kimlik olduğundan bir kez yazıldı.)
    // Plaka, HUD, sayılar, tabela, tuş ipuçları, altyazılar
    [Theory]
    [InlineData("34 ABC 123", false)]
    [InlineData("AB-1234-XY", false)]
    [InlineData("00:12", false)]
    [InlineData("1/3", false)]
    [InlineData("$500", false)]
    [InlineData("100", false)]
    [InlineData("ABC 1234", false)]
    [InlineData("EXIT", false)]
    [InlineData("POLICE", false)]
    [InlineData("GAS STATION", false)]
    [InlineData("ROUTE 66", false)]
    [InlineData("X Atla", false)]
    [InlineData("Press ⓧ to continue", false)]
    [InlineData("Hold R2", false)]
    [InlineData("Press X to continue", false)]
    [InlineData("Hold R2 to aim", false)]
    [InlineData("Basılı tut R2", false)]
    [InlineData("Bas ⓧ", false)]
    [InlineData("Press any button to continue", false)]
    [InlineData("Tap to continue", false)]
    [InlineData("OPTIONS Atla", false)]
    [InlineData("Skip", false)]
    [InlineData("Atla", false)]
    [InlineData("Geri", false)]
    [InlineData("Select", false)]
    [InlineData("Devam", false)]
    [InlineData("△ Back", false)]
    [InlineData("We need to get out of here before they find us.", true)]
    [InlineData("MARCUS: We need to move.", true)]
    [InlineData("Marcus: Where is everybody?", true)]
    [InlineData("Run!", true)]
    [InlineData("What?", true)]
    [InlineData("STOP!", true)]
    [InlineData("WHAT?!", true)]
    [InlineData("Hold on! I'm coming.", true)]
    [InlineData("Hold on!", true)]
    [InlineData("Press the attack, now!", true)]
    [InlineData("Malcolm X", true)]
    [InlineData("Bunu yapmak zorunda değildin, Çağrı.", true)]
    [InlineData("Işığı kapat, hemen şimdi.", true)]
    [InlineData("ÖĞRETMEN: Şimdi gidelim.", true)]
    [InlineData("I'm at 3rd and Main, send backup.", true)]
    [InlineData("THE ENEMY IS NEAR US", true)]
    [InlineData("Back off!", true)]
    [InlineData("WAIT.", true)]
    [InlineData("NO.", true)]
    [InlineData("Back.", true)]
    [InlineData("\"WAIT.\"", true)]
    [InlineData("Back", false)]
    [InlineData("Back here in ten minutes.", true)]
    [InlineData("It's over, Joel.", true)]
    public void SingleLineIsKeptOrDropped(string text, bool keep) =>
        Assert.Equal(keep, SubtitleFilter.Filter([Line(text)]).Count > 0);

    [Fact]
    public void SubtitleAndPrompt() =>
        Assert.Equal(["We need to get out of here"],
            Texts(Line("We need to get out of here", y: 0.6), Line("Press ⓧ to continue", y: 0.2)));

    [Fact]
    public void TwoLineSubtitle() =>
        Assert.Equal(["I told you not to come back here,", "and you did it anyway."],
            Texts(Line("I told you not to come back here,", y: 0.6), Line("and you did it anyway.", y: 0.4)));

    [Fact]
    public void OffCenterSmallSign() =>
        Assert.Equal(["Where were you last night?"],
            Texts(Line("Where were you last night?", x: 0.2, w: 0.6, h: 0.2), Line("Joe's diner", x: 0.02, w: 0.15, h: 0.08)));

    [Fact]
    public void CenteredButSmallSign() =>
        Assert.Equal(["Where were you last night?"],
            Texts(Line("Where were you last night?", x: 0.2, w: 0.6, h: 0.2), Line("Joe's diner open", x: 0.4, w: 0.2, h: 0.07)));

    [Fact]
    public void LeftAlignedSubtitleIsKept() =>
        Assert.Equal(["Where were you last night? I called twice", "and nobody answered."],
            Texts(Line("Where were you last night? I called twice", x: 0.05, w: 0.8), Line("and nobody answered.", x: 0.05, w: 0.35)));

    [Fact]
    public void LowConfidenceGarbage() =>
        Assert.Equal(["Nereye gidiyorsun?"],
            Texts(Line("Nereye gidiyorsun?", conf: 0.9f), Line("Nereye gdyrsn", y: 0.3, conf: 0.36f)));

    [Fact]
    public void OnlyWorldText() => Assert.Empty(Texts(Line("34 ABC 123"), Line("EXIT")));

    [Fact]
    public void OptionsOffKeepsEverything()
    {
        var off = new SubtitleFilter.Options { DropShortCaps = false, DropDigitHeavy = false, DropFewLetters = false, DropButtonPrompts = false };
        Assert.Single(SubtitleFilter.Filter([Line("EXIT")], off));
    }

    [Fact]
    public void DropReasonIsReported() =>
        Assert.Equal(SubtitleFilter.Reason.ShortCaps, SubtitleFilter.Evaluate([Line("EXIT")]).Dropped.First().Reason);

    // --- Kararlılık hattı (OcrNoise -> filtre -> SubtitleStabilizer) ---

    private static List<StabilizerAction> Pipeline(IEnumerable<OcrTextLine[]> frames)
    {
        long now = 0;
        var stabilizer = new SubtitleStabilizer(clock: () => now);
        var actions = new List<StabilizerAction>();
        foreach (var lines in frames)
        {
            now += 500;
            var cleaned = lines.Select(l => l with { Text = OcrNoise.Clean(l.Text) });
            var text = string.Join(" ", SubtitleFilter.Filter(cleaned).Select(l => l.Text));
            actions.Add(stabilizer.Feed(text));
        }
        return actions;
    }

    private static List<StabilizerAction> RunStability()
    {
        var prompt = Line("Press ⓧ to continue", y: 0.2);
        var sub = Line("We should head to the old harbor", y: 0.6);
        return Pipeline(
        [
            [prompt],
            [Line("The ship")],
            [Line("The shipment arrives at")],
            [Line("The shipment arrives at midnight and the guards")],
            [sub, prompt], [sub, prompt], [sub], [sub],
            [], [], [], // Windows kararlayıcısı kalkmayı zamanla (800 ms) anlar: Mac'tekinden bir boş kare fazla
        ]);
    }

    [Fact]
    public void StabilitySendsOnce()
    {
        var actions = RunStability();
        var translates = actions.Where(a => a.Kind == StabilizerKind.Translate).ToList();
        Assert.Single(translates);
        Assert.Equal("We should head to the old harbor", translates[0].Text);
    }

    [Fact]
    public void StabilityClears()
    {
        var actions = RunStability();
        Assert.Single(actions, a => a.Kind == StabilizerKind.Clear);
        Assert.Equal(StabilizerKind.Clear, actions[^1].Kind);
    }

    [Fact]
    public void TypewriterIsNotSent()
    {
        var actions = RunStability();
        Assert.All(actions.Take(4), a => Assert.Equal(StabilizerKind.None, a.Kind));
    }
}
