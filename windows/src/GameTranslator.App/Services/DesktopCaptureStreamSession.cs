// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;
using GameTranslator.RemotePlay;
using GameTranslator.Shell;

namespace GameTranslator.App.Services;

/// <summary>
/// Masaüstünü, seçili monitörü veya OBS / Pencereyi yakalayarak kareleri altyazı motoruna aktaran IStreamSession.
/// Capture Card kullanıcıları ve OBS Fullscreen Projector kullananlar için PS5 Remote Play gerektirmez.
/// </summary>
public sealed class DesktopCaptureStreamSession : IStreamSession
{
    public event Action<Frame>? FrameReceived;
    public event Action<SessionState>? StateChanged;
#pragma warning disable CS0067
    public event Action<RegisteredConsole>? ConsoleMoved;
#pragma warning restore CS0067

    private readonly string? targetWindowTitle;
    private readonly int screenIndex;
    private readonly SavedWindowSource? savedSource;
    private readonly bool clientArea;
    private readonly WindowSourceResolver resolver = new();
    private readonly WindowCaptureService.ClientCaptureState captureState = new();
    private CancellationTokenSource? cts;
    private Task? loopTask;
    private bool running;

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    /// <param name="savedSource">Kayıtlı süreç + başlık deseni ("Bu bilgisayarda" modu); pencere yeniden açılsa da bulunur.</param>
    /// <param name="clientArea">true: pencerenin yalnız istemci alanı yakalanır (okuma kutusu ona göredir).</param>
    public DesktopCaptureStreamSession(string? targetWindowTitle = null, int screenIndex = 0, SavedWindowSource? savedSource = null, bool clientArea = false)
    {
        this.targetWindowTitle = targetWindowTitle;
        this.screenIndex = screenIndex;
        this.savedSource = savedSource;
        this.clientArea = clientArea;
    }

    public void Start()
    {
        if (running) return;
        running = true;
        cts = new CancellationTokenSource();
        StateChanged?.Invoke(SessionState.Connecting);

        loopTask = Task.Run(() => CaptureLoopAsync(cts.Token));
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        bool emittedStreaming = false;
        TimeBeginPeriod(1);

        try
        {
            while (running && !token.IsCancellationRequested)
            {
                try
                {
                    Frame? frame = null;

                    // 1. Belirli bir fiziksel monitör seçilmişse o monitörün koordinatlarını yakala
                    if (!string.IsNullOrWhiteSpace(targetWindowTitle) && targetWindowTitle.StartsWith("monitor:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(targetWindowTitle["monitor:".Length..], out int mIndex))
                        {
                            var monitors = WindowCaptureService.GetDisplayMonitors();
                            var targetMon = monitors.FirstOrDefault(m => m.Index == mIndex) ?? monitors.FirstOrDefault(m => m.IsPrimary);
                            if (targetMon != null)
                            {
                                frame = WindowCaptureService.CaptureScreen(targetMon.Left, targetMon.Top, targetMon.Width, targetMon.Height);
                            }
                        }
                    }
                    // 1b. "Bu bilgisayarda" modu: seçilen pencerenin istemci alanı (PrintWindow, siyahsa ekran BitBlt)
                    else if (clientArea && (savedSource is not null || !string.IsNullOrWhiteSpace(targetWindowTitle)))
                    {
                        var hWnd = resolver.Resolve(savedSource, targetWindowTitle);
                        if (hWnd != IntPtr.Zero) frame = WindowCaptureService.CaptureClient(hWnd, captureState);
                    }
                    // 2. Özel bir pencere seçilmişse (SADECE o pencere yakalanır, asla tüm ekrana taşmaz)
                    else if (!string.IsNullOrWhiteSpace(targetWindowTitle))
                    {
                        string winTitle = targetWindowTitle.StartsWith("window:", StringComparison.OrdinalIgnoreCase)
                            ? targetWindowTitle["window:".Length..]
                            : targetWindowTitle;

                        var hWnd = WindowCaptureService.FindWindowBySubstring(winTitle);
                        if (hWnd != IntPtr.Zero)
                        {
                            frame = WindowCaptureService.Capture(hWnd);
                        }
                    }
                    // 3. Özel bir pencere veya monitör belirtilmemişse genel masaüstünü yakala
                    else
                    {
                        frame = WindowCaptureService.CaptureScreen();
                    }

                    if (frame != null)
                    {
                        if (!emittedStreaming)
                        {
                            emittedStreaming = true;
                            StateChanged?.Invoke(SessionState.Streaming);
                        }
                        FrameReceived?.Invoke(frame);
                    }
                }
                catch { }

                try
                {
                    await Task.Delay(clientArea ? 50 : 16, token); // pencere modunda ~20 kare/sn yeter (altyazı OCR'ı); masaüstünde ~60
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            TimeEndPeriod(1);
        }
    }

    public void Stop()
    {
        if (!running) return;
        running = false;
        cts?.Cancel();
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
