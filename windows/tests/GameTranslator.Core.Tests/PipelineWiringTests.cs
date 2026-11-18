// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// <summary>Altyazı süzgeci, cümle birleştirme, dolgu bekletme ve GameEnglish'in hatta bağlanması.</summary>
public class PipelineWiringTests
{
    private static readonly Frame Image = Frame.Blank(64, 64);

    private static async Task Run(SubtitlePipeline pipeline, int frames, TestClock clock)
    {
        for (var i = 0; i < frames; i++)
        {
            await pipeline.ProcessAsync(Image, new CropRegion());
            await pipeline.WaitForTranslationsAsync();
            clock.Now += 50;
        }
    }

    private static string[] Repeat(params (string Text, int Count)[] runs) =>
        [.. runs.SelectMany(run => Enumerable.Repeat(run.Text, run.Count))];

    [Fact]
    public async Task SubtitleOnlyDropsSignsButKeepsDialogue()
    {
        var clock = new TestClock();
        var translator = new FakeTranslator();
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader(Repeat(("EXIT\nHold the gate.", 6))), translator, sender, clock: clock.Read) { SubtitleOnly = true };
        await Run(pipeline, 6, clock);
        Assert.Equal(["Hold the gate."], translator.Requested);
        Assert.Equal(["TR: Hold the gate."], sender.Sent);
    }

    [Fact]
    public async Task SubtitleOnlyOffKeepsEverything()
    {
        var clock = new TestClock();
        var translator = new FakeTranslator();
        var pipeline = new SubtitlePipeline(new FakeReader(Repeat(("EXIT\nHold the gate.", 6))), translator, new FakeSender(), clock: clock.Read) { SubtitleOnly = false };
        await Run(pipeline, 6, clock);
        Assert.Contains("EXIT", translator.Requested.Single());
    }

    [Fact]
    public async Task SubtitleOnlyUsesGeometryWhenLinesHaveBoxes()
    {
        var clock = new TestClock();
        // Altyazı ortada; sağ üstteki gösterge ("Press R2 to jump") ve uzaktaki yazı elenir.
        var reading = new Reading([
            new TextLine("Hold the gate and wait for me.", new TextBox(0.3, 0.6, 0.7, 0.7)),
            new TextLine("Quest: Go now", new TextBox(0.8, 0.0, 1.0, 0.1)),
        ]);
        var pipeline = new SubtitlePipeline(new LinesReader(reading), new FakeTranslator(), new FakeSender(), clock: clock.Read) { SubtitleOnly = true };
        var filtered = pipeline.WithSubtitleFilter(reading);
        Assert.Equal(["Hold the gate and wait for me."], filtered.Lines.Select(line => line.Text));
    }

    [Fact]
    public async Task SplitSentenceIsTranslatedWhole()
    {
        var clock = new TestClock();
        var translator = new FakeTranslator();
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader(Repeat(("Hold the gate and", 6), ("wait for me.", 6))), translator, sender, clock: clock.Read) { JoinSentences = true };
        await Run(pipeline, 12, clock);
        Assert.Contains("Hold the gate and wait for me.", translator.Requested);
        Assert.Equal("TR: Hold the gate and wait for me.", sender.Sent[^1]);
    }

    [Fact]
    public async Task LoneFillerWaitsThenIsSentAlone()
    {
        var clock = new TestClock();
        var translator = new FakeTranslator();
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader(Repeat(("Well...", 8))), translator, sender, clock: clock.Read) { JoinSentences = true, FillerHoldMs = 60 };
        await Run(pipeline, 8, clock);
        Assert.Empty(sender.Sent);
        await Task.Delay(400);
        Assert.Equal(["TR: Well..."], sender.Sent);
    }

    [Fact]
    public async Task FillerIsTranslatedWithItsContinuationInstead()
    {
        var clock = new TestClock();
        var translator = new FakeTranslator();
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader(Repeat(("Well...", 6), ("I told you so.", 6))), translator, sender, clock: clock.Read) { JoinSentences = true, FillerHoldMs = 300 };
        await Run(pipeline, 12, clock);
        await Task.Delay(600);
        Assert.Equal(["TR: Well, I told you so."], sender.Sent);
    }

    [Fact]
    public async Task GameEnglishIsAppliedBeforeTranslation()
    {
        var clock = new TestClock();
        var translator = new FakeTranslator();
        var pipeline = new SubtitlePipeline(new FakeReader(Repeat(("Check your six.", 6))), translator, new FakeSender(), clock: clock.Read);
        await Run(pipeline, 6, clock);
        Assert.Equal(["Watch behind you."], translator.Requested);
    }

    /// Çeviri yoksa (model eksik) İngilizce TV'ye gitmez; satır gönderildi sayılmaz, model gelince aynı satır çevrilir.
    [Fact]
    public async Task FailedTranslationSendsNothingAndLineIsNotMarkedSent()
    {
        var clock = new TestClock();
        var translator = new FakeTranslator { Error = new TranslatorException(TranslatorStatus.NeedsDownload) };
        var sender = new FakeSender();
        var pipeline = new SubtitlePipeline(new FakeReader(Repeat(("Hold the gate.", 60))), translator, sender, clock: clock.Read);
        await Run(pipeline, 10, clock);
        Assert.Empty(sender.Sent);
        translator.Error = null;
        clock.Now += 1500;
        await Run(pipeline, 20, clock);
        Assert.Equal(["TR: Hold the gate."], sender.Sent);
    }
}
