// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;
using GameTranslator.RemotePlay;
using GameTranslator.Shell;

namespace GameTranslator.App.Services;

/// <summary>
/// Windows'ta Chiaki veya PlayStation Remote Play penceresini periyodik olarak yakalayan IStreamSession uygulaması.
/// </summary>
public sealed class WindowCaptureStreamSession : IStreamSession
{
    public event Action<Frame>? FrameReceived;
    public event Action<SessionState>? StateChanged;
#pragma warning disable CS0067
    public event Action<RegisteredConsole>? ConsoleMoved;
#pragma warning restore CS0067

    private readonly RegisteredConsole console;
    private readonly byte[]? accountId;
    private CancellationTokenSource? cts;
    private Task? loopTask;
    private bool running;

    public WindowCaptureStreamSession(RegisteredConsole console, byte[]? accountId = null)
    {
        this.console = console;
        this.accountId = accountId;
    }

    public void Start()
    {
        if (running) return;
        running = true;
        cts = new CancellationTokenSource();
        StateChanged?.Invoke(SessionState.Connecting);

        if (console != null && !string.IsNullOrWhiteSpace(console.Host))
        {
            ChiakiLauncher.SyncConfig(console.Host, accountId);
            ChiakiLauncher.LaunchAndStream(console.Host, console.Name);
        }

        loopTask = Task.Run(() => CaptureLoopAsync(cts.Token));
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        var emittedStreaming = false;
        var retry = new StreamReconnectPolicy(() => Environment.TickCount64);
        var missingSince = 0L;
        var launchedAt = Environment.TickCount64;

        while (running && !token.IsCancellationRequested)
        {
            try
            {
                var hWnd = WindowCaptureService.FindTargetWindow(out _);
                if (hWnd != IntPtr.Zero)
                {
                    missingSince = 0;
                    // Chiaki'yi ekrana fırlatmak yerine arka planda görünmez tut
                    WindowCaptureService.MakeInvisibleBackground(hWnd);

                    var frame = WindowCaptureService.Capture(hWnd);
                    if (frame != null)
                    {
                        if (!emittedStreaming)
                        {
                            emittedStreaming = true;
                            retry.MarkStreaming();
                            StateChanged?.Invoke(SessionState.Streaming);
                        }
                        FrameReceived?.Invoke(frame);
                    }
                }
                else
                {
                    // Akış penceresi kayboldu. Chiaki süreci de yoksa (çöktü / kapandı) yeniden başlatılır; süreç varsa
                    // pencere yeniden oluşuyor olabilir, birkaç saniye beklenir.
                    var now = Environment.TickCount64;
                    if (missingSince == 0) missingSince = now;
                    // Akış hiç başlamadıysa Chiaki'ye açılması için 10 sn tanınır.
                    var grace = emittedStreaming ? 1500 : 10_000;
                    if (!ChiakiLauncher.IsRunning() && now - missingSince >= grace && (emittedStreaming || now - launchedAt >= grace))
                    {
                        missingSince = 0;
                        emittedStreaming = false;
                        var decision = retry.OnExited();
                        SubtitleLog.Write($"chiaki kapandı: {decision.Message}");
                        StateChanged?.Invoke(SessionState.Disconnected(DisconnectKind.Network, decision.Message));
                        if (!decision.Retry) { running = false; break; }
                        await Task.Delay(StreamReconnectPolicy.DelayMs, token);
                        StateChanged?.Invoke(SessionState.Connecting);
                        if (console != null && !string.IsNullOrWhiteSpace(console.Host))
                            ChiakiLauncher.LaunchAndStream(console.Host, console.Name);
                        launchedAt = Environment.TickCount64;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }

            try
            {
                await Task.Delay(150, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Stop()
    {
        if (!running) return;
        running = false;
        cts?.Cancel();
        ChiakiLauncher.Stop();
        StateChanged?.Invoke(SessionState.Disconnected(DisconnectKind.Stopped));
    }

    public void ReconnectNow()
    {
        if (!running) return;
        Stop();
        Start();
    }

    public void Dispose()
    {
        Stop();
        cts?.Dispose();
    }
}
