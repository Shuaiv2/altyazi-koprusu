// SPDX-License-Identifier: AGPL-3.0-only

namespace GameTranslator.Shell.Tests;

public class TvLayoutEditorBridgeTests
{
    private sealed class Port : ITvLayoutPort
    {
        public bool TvConnected { get; set; } = true;
        public (string Json, DateTimeOffset? At)? LastTvLayout { get; set; }
        public List<(string Mode, string Json)> Sent { get; } = [];
        public void SendLayout(string mode, string settingsJson) => Sent.Add((mode, settingsJson));
    }

    private sealed class Scheduler
    {
        public List<(int Delay, Action Run, bool Disposed)> Items { get; } = [];
        public IDisposable Schedule(int delay, Action run)
        {
            var index = Items.Count;
            Items.Add((delay, run, false));
            return new Handle(() => Items[index] = Items[index] with { Disposed = true });
        }
        private sealed class Handle(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }

    private static (TvLayoutEditorBridge Bridge, Port Port, Scheduler Scheduler, long[] Clock) Make()
    {
        var port = new Port();
        var scheduler = new Scheduler();
        var clock = new long[] { 10_000 };
        return (new TvLayoutEditorBridge(port, () => clock[0], scheduler.Schedule), port, scheduler, clock);
    }

    [Fact]
    public void ReadyLoadsSavedLayoutAndStatus()
    {
        var (bridge, port, _, _) = Make();
        port.LastTvLayout = ("""{"fontSize":60}""", new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var scripts = bridge.Handle("""{"type":"ready"}""");
        Assert.Equal("""window.__editorLoad && window.__editorLoad({"fontSize":60}, true)""", scripts[0]);
        Assert.Equal("""window.__editorStatus && window.__editorStatus(true, "2026-03-01T12:00:00Z")""", scripts[1]);
        port.LastTvLayout = null;
        port.TvConnected = false;
        Assert.Equal("window.__editorLoad && window.__editorLoad(null, false)", bridge.LoadScript());
        Assert.Equal("window.__editorStatus && window.__editorStatus(false, null)", bridge.StatusScript());
    }

    [Fact]
    public void PreviewsAreThrottledToTenPerSecondAndTheLastOneWins()
    {
        var (bridge, port, scheduler, clock) = Make();
        bridge.Handle("""{"type":"preview","settings":{"x":1}}""");
        clock[0] += 30;
        bridge.Handle("""{"type":"preview","settings":{"x":2}}""");
        clock[0] += 30;
        bridge.Handle("""{"type":"preview","settings":{"x":3}}""");
        Assert.Equal([("preview", """{"x":1}""")], port.Sent);
        Assert.Single(scheduler.Items);
        Assert.Equal(70, scheduler.Items[0].Delay);
        clock[0] += 70;
        scheduler.Items[0].Run();
        Assert.Equal([("preview", """{"x":1}"""), ("preview", """{"x":3}""")], port.Sent);
        // Aralık geçtikten sonraki önizleme hemen gider.
        clock[0] += 150;
        bridge.Handle("""{"type":"preview","settings":{"x":4}}""");
        Assert.Equal(("preview", """{"x":4}"""), port.Sent[^1]);
    }

    [Fact]
    public void SaveDropsPendingPreviewAndSkipsCancelOnClose()
    {
        var (bridge, port, scheduler, clock) = Make();
        bridge.Handle("""{"type":"preview","settings":{"x":1}}""");
        clock[0] += 10;
        bridge.Handle("""{"type":"preview","settings":{"x":2}}""");
        bridge.Handle("""{"type":"save","settings":{"x":9}}""");
        Assert.True(scheduler.Items[0].Disposed);
        Assert.Equal(("save", """{"x":9}"""), port.Sent[^1]);
        Assert.DoesNotContain(port.Sent, sent => sent.Json == """{"x":2}""");
        bridge.Closed();
        Assert.Equal(2, port.Sent.Count);
    }

    [Fact]
    public void ClosingWithoutSavingCancelsThePreviewOnTv()
    {
        var (bridge, port, _, _) = Make();
        bridge.Handle("""{"type":"preview","settings":{"x":1}}""");
        bridge.Closed();
        Assert.Equal(("cancel", "{}"), port.Sent[^1]);
    }

    [Fact]
    public void CancelMessageAndGarbageAreHandled()
    {
        var (bridge, port, _, _) = Make();
        Assert.Empty(bridge.Handle("not json"));
        Assert.Empty(bridge.Handle("""{"type":"unknown"}"""));
        Assert.Empty(bridge.Handle("""{"type":"save"}"""));
        Assert.Empty(port.Sent);
        bridge.Handle("""{"type":"cancel"}""");
        Assert.Equal([("cancel", "{}")], port.Sent);
    }
}

public class CaptureSupportTests
{
    [Fact]
    public void LargestVisibleWindowWinsNotTheFirst()
    {
        var best = CaptureWindowPicker.Best([
            new CaptureCandidate(1, "chiaki Settings", 640, 480),
            new CaptureCandidate(2, "Stream", 1280, 720),
            new CaptureCandidate(3, "tiny", 100, 100),
        ]);
        Assert.Equal(2, best!.Handle);
        Assert.Null(CaptureWindowPicker.Best([new CaptureCandidate(1, "x", 200, 100)]));
    }

    [Fact]
    public void EqualAreaPrefersStreamTitle()
    {
        var best = CaptureWindowPicker.Best([new CaptureCandidate(1, "other", 800, 600), new CaptureCandidate(2, "PS5 Stream", 800, 600)]);
        Assert.Equal(2, best!.Handle);
    }

    [Fact]
    public void RetriesFiveTimesThenGivesUp()
    {
        long now = 0;
        var policy = new StreamReconnectPolicy(() => now);
        for (var n = 1; n <= 5; n++)
        {
            var decision = policy.OnExited();
            Assert.True(decision.Retry);
            Assert.Equal($"PS5 bağlantısı koptu, yeniden deneniyor ({n}/5)…", decision.Message);
            now += 1000;
        }
        Assert.False(policy.OnExited().Retry);
    }

    [Fact]
    public void ThirtySecondsOfStreamingResetsTheCounter()
    {
        long now = 0;
        var policy = new StreamReconnectPolicy(() => now);
        policy.OnExited();
        policy.OnExited();
        policy.MarkStreaming();
        now += StreamReconnectPolicy.StableResetMs;
        Assert.Equal(1, policy.OnExited().Attempt);
        // Kısa süren akış sayacı sıfırlamaz.
        policy.MarkStreaming();
        now += 5000;
        Assert.Equal(2, policy.OnExited().Attempt);
    }

    [Fact]
    public void ReconnectMessageShowsAsStatus()
    {
        var state = RemotePlay.SessionState.Disconnected(RemotePlay.DisconnectKind.Network, "PS5 bağlantısı koptu, yeniden deneniyor (2/5)…");
        Assert.Equal("PS5 bağlantısı koptu, yeniden deneniyor (2/5)…", AppController.StatusText(state, true, GameTranslator.Core.Localizer.ForLanguage("en")));
    }
}
