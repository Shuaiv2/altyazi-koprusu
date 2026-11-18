// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Windows.Threading;
using GameTranslator.App.Views;
using GameTranslator.Shell;

namespace GameTranslator.App.Services;

/// <summary>
/// Seçilen pencereyi (süreç adı + başlık deseni) açık pencereler arasında bulur ve tutamağı önbelleğe alır; pencere kapanıp
/// yeniden açılırsa (ör. tarayıcı yeniden başlatıldı) kendiliğinden yeniden bulur. Tarama pahalı olduğundan
/// kayıp tutamak için en çok 1,5 sn'de bir yapılır.
/// </summary>
public sealed class WindowSourceResolver
{
    private const long RescanEveryMs = 1500;
    private IntPtr handle;
    private long lastScan = long.MinValue / 2;

    /// <param name="saved">Kayıtlı süreç + başlık deseni (önce bu).</param>
    /// <param name="windowName">Eski ayar: "window:Başlık" ya da yalnız başlık.</param>
    public IntPtr Resolve(SavedWindowSource? saved, string? windowName)
    {
        if (WindowCaptureService.IsUsableWindow(handle) && StillMatches(saved)) return handle;
        var now = Environment.TickCount64;
        if (now - lastScan < RescanEveryMs) return IntPtr.Zero;
        lastScan = now;
        handle = IntPtr.Zero;

        if (saved is not null)
        {
            if (saved.Find(WindowCaptureService.EnumerateWindows()) is { } found) handle = (IntPtr)found.Handle;
        }
        else if (!string.IsNullOrWhiteSpace(windowName))
        {
            var title = windowName.StartsWith("window:", StringComparison.OrdinalIgnoreCase) ? windowName["window:".Length..] : windowName;
            handle = WindowCaptureService.FindWindowBySubstring(title);
        }
        return handle;
    }

    private bool StillMatches(SavedWindowSource? saved) =>
        saved is null || string.IsNullOrWhiteSpace(saved.TitlePattern)
        || WindowCaptureService.GetTitle(handle).Contains(saved.TitlePattern, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// "Bu bilgisayarda" modunun canlı bekçisi: mod açıkken katmanı etkinleştirir, seçilen pencerenin bulunduğu monitörü izler
/// (pencere başka monitöre geçerse katman da gider) ve Ctrl+Shift+O / Ctrl+Shift+L kısayollarının ne zaman tutulacağını söyler.
/// </summary>
public sealed class PcOverlayTracker : IDisposable
{
    private readonly AppController controller;
    private readonly DispatcherTimer timer;
    private readonly WindowSourceResolver resolver = new();
    private bool lastMode;

    /// <summary>Mod açılıp kapanınca (kısayolları kaydetmek için).</summary>
    public event Action<bool>? ModeChanged;

    public PcOverlayTracker(AppController controller)
    {
        this.controller = controller;
        controller.PropertyChanged += OnChanged;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) => Update();
        timer.Start();
        Apply();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppController.Settings)) Apply();
    }

    private void Apply()
    {
        var mode = controller.Settings.PcOverlayMode;
        if (mode != lastMode)
        {
            lastMode = mode;
            // Mod açılınca katman da açılır; kapanınca eski hâline (kapalı) döner.
            DesktopSubtitleOverlay.IsOverlayEnabled = mode;
            ModeChanged?.Invoke(mode);
        }
        Update();
    }

    private void Update()
    {
        if (!lastMode) return;
        try
        {
            var s = controller.Settings;
            var monitors = WindowCaptureService.GetDisplayMonitors();
            if (monitors.Count == 0) return;
            var rects = monitors.Select(m => new ScreenRect(m.Left, m.Top, m.Width, m.Height)).ToList();

            ScreenRect? target = null;
            var name = s.DesktopWindowName;
            if (!string.IsNullOrWhiteSpace(name) && name.StartsWith("monitor:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name["monitor:".Length..], out var index))
            {
                var monitor = monitors.FirstOrDefault(m => m.Index == index);
                if (monitor is not null) target = new ScreenRect(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
            }
            else
            {
                var handle = resolver.Resolve(s.SavedSource, name);
                if (handle != IntPtr.Zero && WindowCaptureService.GetClientScreenRect(handle) is { } client)
                    target = MonitorPicker.For(client, rects);
            }

            // Pencere bulunamazsa katman son monitörde kalır (ya da ana monitörde başlar).
            if (target is { } t) DesktopSubtitleOverlay.SetTargetMonitor(t.Left, t.Top, t.Width, t.Height);
        }
        catch { }
    }

    public void Dispose()
    {
        timer.Stop();
        controller.PropertyChanged -= OnChanged;
    }
}
