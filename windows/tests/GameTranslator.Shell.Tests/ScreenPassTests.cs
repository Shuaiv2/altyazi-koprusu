// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Shell.Tests;

public class ScreenPassTests
{
    private sealed class FakeTranslator : ITextTranslator
    {
        public bool Fail { get; set; }
        public Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default) =>
            Fail ? throw new TranslatorException(TranslatorStatus.EngineFailed) : Task.FromResult("TR:" + text);
    }

    private sealed class Harness
    {
        public List<(string Region, IReadOnlyList<ScreenItem> Items, int Ms)> Sent { get; } = [];
        public List<string> Cleared { get; } = [];
        public List<bool> Suppressed { get; } = [];
        public FakeTranslator Translator { get; } = new();
        public bool ObjectiveOn { get; set; } = true;
        public double Now { get; set; } = 100;
        public ScreenPass Pass { get; }

        public Harness() => Pass = new ScreenPass(new ScreenPassHooks
        {
            Reader = () => null,
            Translator = () => Translator,
            Enabled = kind => kind != ScreenKind.Objective || ObjectiveOn,
            Send = (region, items, ms) => { lock (Sent) Sent.Add((region, items, ms)); },
            Clear = region => { lock (Cleared) Cleared.Add(region); },
            SubtitleSuppressed = Suppressed.Add,
            Seconds = () => Now,
        });

        public void Pass1(IReadOnlyList<ScreenLine> lines, ScreenAnalysis? analysis = null)
        {
            var result = analysis ?? ScreenText.Analyze(lines);
            var themes = result.Blocks.ToDictionary(b => b.Slot, _ => "light");
            Pass.Apply(lines, result, themes, 5, 0);
            Now += 0.9;
        }
    }

    private static ScreenLine L(double x0, double y0, double x1, double y1, string text) =>
        new(text, ScreenBox.FromEdges(x0, y0, x1, y1), 1);

    private static readonly ScreenLine Objective = L(0.35, 0.946, 0.65, 0.977, "Take Lucia to the community center");

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    [Fact]
    public async Task StableObjectiveIsTranslatedAndSentWithRegionThemeAndBox()
    {
        var h = new Harness();
        h.Pass1([Objective]);
        Assert.Empty(h.Sent);
        h.Pass1([Objective]);
        await WaitUntil(() => h.Sent.Count == 1);
        var (region, items, ms) = h.Sent[0];
        Assert.Equal("objective", region);
        Assert.Equal(ScreenPass.DisplayMs, ms);
        var item = Assert.Single(items);
        Assert.Equal("over", item.Placement);
        Assert.Equal("light", item.Theme);
        Assert.StartsWith("TR:", item.Text);
        Assert.Equal("Take Lucia to the community center", item.Source);
        Assert.InRange(item.Box.Top, 0.94, 0.95);
    }

    [Fact]
    public async Task ObjectiveVanishesForTwoSecondsClearsRegion()
    {
        var h = new Harness();
        h.Pass1([Objective]); h.Pass1([Objective]);
        await WaitUntil(() => h.Sent.Count == 1);
        h.Pass1([]);
        Assert.DoesNotContain("objective", h.Cleared);
        h.Now += 3;
        h.Pass1([]);
        Assert.Contains("objective", h.Cleared);
    }

    [Fact]
    public async Task TranslationFailureLetsTheSameBlockRetry()
    {
        var h = new Harness { Translator = { Fail = true } };
        h.Pass1([Objective]); h.Pass1([Objective]);
        await Task.Delay(100);
        Assert.Empty(h.Sent);
        h.Translator.Fail = false;
        h.Pass1([Objective]);
        await WaitUntil(() => h.Sent.Count == 1);
    }

    [Fact]
    public void DisabledKindsAreNotTracked()
    {
        var h = new Harness { ObjectiveOn = false };
        h.Pass1([Objective]); h.Pass1([Objective]); h.Pass1([Objective]);
        Assert.Empty(h.Sent);
    }

    [Fact]
    public void MenuModeSuppressesSubtitlesOnceAndGameResumes()
    {
        var h = new Harness();
        var menu = new ScreenAnalysis(ScreenMode.Menu, [], null);
        var game = new ScreenAnalysis(ScreenMode.Game, [], null);
        h.Pass1([], menu);
        Assert.False(h.Pass.SubtitlesSuppressed);
        h.Pass1([], menu);
        Assert.True(h.Pass.SubtitlesSuppressed);
        h.Pass1([], menu);
        Assert.Equal([true], h.Suppressed);
        h.Pass1([], game); h.Pass1([], game);
        Assert.False(h.Pass.SubtitlesSuppressed);
        Assert.Equal([true, false], h.Suppressed);
    }

    [Fact]
    public void GateDropsLinesInsidePhoneAndNotificationAreas()
    {
        var h = new Harness();
        var phone = new ScreenAnalysis(ScreenMode.Game, [], new PhoneInfo(ScreenBox.FromEdges(0.4, 0.7, 0.6, 0.95), false));
        h.Pass1([], phone);
        var area = new TextBox(0.12, 0.72, 0.88, 0.935);
        // Kırpma içinde (0..1): ilk satır telefon kutusunda (x 0.4-0.6 → kırpmada ~0.37-0.63), ikincisi dışında ortalı.
        var read = new Reading([
            new TextLine("Is that you again?", new TextBox(0.40, 0.30, 0.60, 0.45)),
            new TextLine("I have never seen anything like it.", new TextBox(0.20, 0.55, 0.80, 0.70)),
        ]);
        var gated = h.Pass.GateSubtitle(read, area, crossCheck: false);
        Assert.Equal(["I have never seen anything like it."], gated.Lines.Select(l => l.Text));
    }

    [Fact]
    public void CrossCheckPrefersABetterFullFrameReadingAndMovesTheBox()
    {
        var h = new Harness();
        var screenLine = L(0.25, 0.85, 0.75, 0.88, "I have never seen anything like it.");
        h.Pass1([screenLine], new ScreenAnalysis(ScreenMode.Game, [], null));
        var area = new TextBox(0.12, 0.72, 0.88, 0.935);
        var read = new Reading([new TextLine("I have never seen anvthing like it.", new TextBox(0.2, 0.6, 0.8, 0.7))]);
        var gated = h.Pass.GateSubtitle(read, area, crossCheck: true);
        var line = Assert.Single(gated.Lines);
        Assert.Equal("I have never seen anything like it.", line.Text);
        var placed = line.Box!.Value.PlacedIn(area);
        Assert.Equal(0.25, placed.Left, 3);
        Assert.Equal(0.88, placed.Bottom, 3);
        // Kapalıyken okuma olduğu gibi kalır.
        Assert.Equal("I have never seen anvthing like it.", h.Pass.GateSubtitle(read, area, crossCheck: false).Lines[0].Text);
    }

    [Fact]
    public void ResetClearsEveryRegionAndLiftsSuppression()
    {
        var h = new Harness();
        var menu = new ScreenAnalysis(ScreenMode.Menu, [], null);
        h.Pass1([], menu); h.Pass1([], menu);
        h.Pass.Reset();
        Assert.False(h.Pass.SubtitlesSuppressed);
        Assert.Equal(["objective", "info", "notify", "description", "phone"], h.Cleared);
        Assert.Equal([true, false], h.Suppressed);
    }
}

public class ScreenCardsTests
{
    [Fact]
    public void OverFontIsClampedBetween18AndUserFont()
    {
        Assert.Equal(18, ScreenCards.OverFontSize(0.01, 1080, 44));
        Assert.Equal(44, ScreenCards.OverFontSize(0.2, 1080, 44));
        Assert.Equal(Math.Round(0.75 * 0.04 * 1080), ScreenCards.OverFontSize(0.04, 1080, 44));
    }

    [Fact]
    public void OverCardStyleHasThemeColorsAndMinimumOpacity()
    {
        var light = ScreenCards.OverStyle("light", 0);
        Assert.True(light.Light);
        Assert.Equal(0.98, light.Alpha);
        Assert.Equal(((byte)246, (byte)246, (byte)248), light.Background);
        Assert.Equal(((byte)0x15, (byte)0x16, (byte)0x1a), light.Text);
        var dark = ScreenCards.OverStyle(null, 10);
        Assert.False(dark.Light);
        Assert.Equal(0.93, dark.Alpha);
        Assert.Equal(1, ScreenCards.OverStyle("dark", 100).Alpha);
    }

    [Fact]
    public void FirstDescriptionSegmentIsBoldOnlyWithoutSpeaker()
    {
        var item = new ScreenItem("BAŞLIK\nGövde metni", "", new TextBox(0, 0, 0.1, 0.1), 0.03, "over", "dark");
        Assert.True(ScreenCards.Parts(item).TitleBold);
        Assert.Equal(["BAŞLIK", "Gövde metni"], ScreenCards.Parts(item).Segments);
        Assert.False(ScreenCards.Parts(item with { Speaker = "Lucia" }).TitleBold);
    }

    [Fact]
    public void OverCardCoversTheBoxAndGrowsUpwardNearTheBottom()
    {
        var box = new TextBox(0.35, 0.946, 0.65, 0.977);
        var rect = ScreenCards.OverRect(box, 1920, 1080, 48, 0);
        Assert.Equal(Math.Round(0.35 * 1920), rect.Left);
        Assert.Equal(Math.Round(0.946 * 1080), rect.Top);
        var grown = ScreenCards.OverRect(box, 1920, 1080, 48, 100);
        Assert.True(grown.Top < rect.Top);
        Assert.Equal(1.6 * 33, ScreenCards.OverMaxGrow(33, 500), 0);
    }

    [Fact]
    public void PhonePanelGoesLeftOfThePhoneElseRight()
    {
        var phone = new TextBox(0.6, 0.2, 0.8, 0.9);
        var (rect, left) = ScreenCards.BesideRect(phone, 1920, 1080, 48);
        Assert.True(left);
        Assert.Equal(Math.Round(0.6 * 1920 - 24 - 460), rect.Left);
        Assert.Equal(460, rect.Width);
        var (right, isLeft) = ScreenCards.BesideRect(new TextBox(0.05, 0.2, 0.2, 0.9), 1920, 1080, 48);
        Assert.False(isLeft);
        Assert.Equal(Math.Round(0.2 * 1920 + 24), right.Left);
        Assert.Equal(Math.Round(0.24 * 1920) > 460 ? 460 : Math.Round(0.24 * 1920), ScreenCards.BesideRect(phone, 1920, 1080, 48).Rect.Width);
    }

    [Fact]
    public void ChatDropsOldestBubblesFirst()
    {
        Assert.Equal(2, ScreenCards.BesideFirstVisible([50, 50, 50, 50], 6, 120));
        Assert.Equal(0, ScreenCards.BesideFirstVisible([50, 50], 6, 500));
        Assert.Equal(3, ScreenCards.BesideFirstVisible([50, 50, 50, 500], 6, 100));
    }

    [Fact]
    public void OverlappingCardsStackDownwards()
    {
        var stacked = ScreenCards.Stack([new(10, 10, 100, 50, 0), new(10, 30, 100, 50, 0)], 8);
        Assert.Equal(10, stacked[0].Top);
        Assert.Equal(68, stacked[1].Top);
    }
}

public class ScreenThemeAndJsonTests
{
    [Fact]
    public void ThemeFollowsBoxBrightness()
    {
        var white = Frame.Blank(100, 100);
        Array.Fill(white.Pixels, (byte)255);
        Assert.Equal("light", ScreenTheme.Of(white, new ScreenBox(0.1, 0.1, 0.5, 0.5)));
        Assert.Equal("dark", ScreenTheme.Of(Frame.Blank(100, 100), new ScreenBox(0.1, 0.1, 0.5, 0.5)));
    }
}
