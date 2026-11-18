// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using GameTranslator.Core;
using GameTranslator.Shell;

namespace GameTranslator.App.Views;

/// <summary>
/// Bilgisayar ekranında oyunun (ör. tam ekran tarayıcıda Xbox Remote Play) tam üstünde şeffaf, tıklama geçiren ve odağı
/// çalmayan canlı Türkçe altyazı katmanı. Pencere seçilen oyunun bulunduğu monitörü tamamen kaplar; altyazı kutusu TV ile
/// AYNI ayar modeliyle (x, y, genişlik, yazı boyu, kenar boşluğu, renkler, arka plan, kontur, hizalama, kalın; 1920 genişlikli
/// "sayfa pikseli") yerleştirilir ve monitöre ölçeklenir. Yerleşim hesabı <see cref="OverlayLayoutMath"/>'tedir
/// (tv/webos/subtitle-settings.js computeRect'in C# karşılığı). Pencere ekran yakalamadan gizlenir
/// (WDA_EXCLUDEFROMCAPTURE): OCR kendi çevirimizi asla okumaz.
///
/// Neden WebView2 değil de WPF: şeffaf (AllowsTransparency) pencerede alt pencere (HWND) çizen WebView2 güvenilir değil
/// (hava boşluğu sorunu); gerçek Windows'ta denemeden riske girilmedi. Kutu/yazı tek bir WPF öğesi olduğundan hesap aynı kalır.
/// </summary>
public sealed class DesktopSubtitleOverlay : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    /// <summary>Düzen düzenleyicisinde gösterilen örnek yazı.</summary>
    public const string SampleText = "Örnek altyazı: Çağla, ışığı gördüğünde şaşırdı.\nİstanbul'da güneş doğarken öğle yemeğini düşündü.";

    private static DesktopSubtitleOverlay? _instance;
    private static bool _enabled; // PC modu açılınca ya da kullanıcı isteyince açılır
    private static ScreenRect? _monitor;
    private static OverlayLayoutSettings? _layout;
    private static string? _lastText;

    /// <summary>Düzenin saklandığı dosya (%LOCALAPPDATA%\AltyaziKoprusu\overlay-layout.json).</summary>
    public static OverlayLayoutStore Store { get; set; } = new(OverlayLayoutStore.DefaultPath());

    /// <summary>Kayıtlı düzen (önizleme sırasında önizlenen düzen).</summary>
    public static OverlayLayoutSettings Layout => _layout ??= Store.Current;

    private static ScreenRect CurrentMonitor => _monitor ?? PrimaryMonitor();

    private static ScreenRect PrimaryMonitor()
    {
        var monitors = Services.WindowCaptureService.GetDisplayMonitors();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.First();
        return new ScreenRect(primary.Left, primary.Top, primary.Width, primary.Height);
    }

    /// <summary>Katmanın kaplayacağı monitör (sanal masaüstü pikseli). Aynı değerse bir şey yapılmaz.</summary>
    public static void SetTargetMonitor(int left, int top, int width, int height)
    {
        var rect = new ScreenRect(left, top, width, height);
        if (_monitor == rect) return;
        _monitor = rect;
        if (Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() => _instance?.ApplyMonitor());
    }

    public static bool IsOverlayEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Clear();
        }
    }

    public static void Initialize()
    {
        if (Application.Current == null) return;
        Application.Current.Dispatcher.Invoke(() => _instance ??= new DesktopSubtitleOverlay());
    }

    /// <summary>Çeviriyi katmanda gösterir. <paramref name="style"/> ve <paramref name="box"/> yalnız eski çağrılar içindir; yerleşim düzen dosyasından gelir.</summary>
    public static void Display(string text, int durationMs = 4500, SubtitleStyle? style = null, GameTranslator.Core.TextBox? box = null)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(text)) return;
        if (Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _instance ??= new DesktopSubtitleOverlay();
                _lastText = text;
                _instance.ShowText(text, durationMs);
            }
            catch { }
        });
    }

    /// <summary>Kısayolla (Ctrl+Shift+O) göster/gizle; yeni durumu döner. Açılırken kısa bir bilgi yazısı gösterilir.</summary>
    public static bool Toggle()
    {
        IsOverlayEnabled = !_enabled;
        Notice(_enabled ? "Altyazı Köprüsü: altyazı katmanı açık (Ctrl+Shift+O ile kapat)" : "");
        return _enabled;
    }

    /// <summary>Kısa bir bilgi yazısı (katman kapalı olsa bile).</summary>
    public static void Notice(string text)
    {
        if (string.IsNullOrEmpty(text) || Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _instance ??= new DesktopSubtitleOverlay();
                _instance.ShowText(text, 2500);
            }
            catch { }
        });
    }

    /// <summary>
    /// Düzenleyiciden gelen önizleme/kayıt: düzen canlı uygulanır, katman açıksa örnek yazı gösterilir. null: vazgeçildi,
    /// kayıtlı düzene dönülür.
    /// </summary>
    public static void SetLayout(OverlayLayoutSettings? layout)
    {
        if (Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _layout = layout ?? Store.Current;
                _instance ??= new DesktopSubtitleOverlay();
                if (layout is not null && _enabled) _instance.ShowText(_lastText ?? SampleText, 6000);
                else if (_instance.Visibility == Visibility.Visible) _instance.Render();
            }
            catch { }
        });
    }

    public static void Clear()
    {
        if (Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try { _instance?.HideText(); }
            catch { }
        });
    }

    private readonly Canvas _page = new() { ClipToBounds = false };
    private readonly Border _box = new();
    private readonly Border _textBg = new() { CornerRadius = new CornerRadius(10), Padding = new Thickness(26, 10, 26, 10) };
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI") };
    private readonly DispatcherTimer _timer = new();
    private string _shown = "";
    private bool _rendering;

    private DesktopSubtitleOverlay()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Left = 0;
        Top = 0;
        Width = 800;
        Height = 450;

        _textBg.Child = _text;
        _box.Child = _textBg;
        _page.Children.Add(_box);
        Content = new Grid { Background = Brushes.Transparent, Children = { _page } };
        Visibility = Visibility.Collapsed;

        _timer.Tick += (_, _) => HideText();

        SourceInitialized += (_, _) => ApplyStyles();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ApplyMonitor, DispatcherPriority.Background);
        SizeChanged += (_, _) => { if (Visibility == Visibility.Visible) Render(); };
        // Tutamak şimdi oluşsun ki izin/konum ilk gösterimden önce uygulanabilsin.
        new WindowInteropHelper(this).EnsureHandle();
        ApplyMonitor();
    }

    /// <summary>Tıklama geçirme, odak almama, görev çubuğunda görünmeme ve yakalamadan gizlenme.</summary>
    private void ApplyStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            // Ekran kaydından (OBS, BitBlt, Windows Graphics Capture) gizle: OCR kendi altyazımızı okumaz.
            SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    /// <summary>Pencereyi monitörün tam piksel dikdörtgenine oturtur (DPI'dan bağımsız, Win32 pikseliyle).</summary>
    private void ApplyMonitor()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var m = OverlayLayoutMath.OverlayRect(CurrentMonitor);
            SetWindowPos(hwnd, HWND_TOPMOST, m.Left, m.Top, m.Width, m.Height, SWP_NOACTIVATE);
            if (Visibility == Visibility.Visible) Render();
        }
        catch { }
    }

    private void ShowText(string text, int durationMs)
    {
        _shown = text;
        ApplyStyles();
        Render();
        if (Visibility != Visibility.Visible)
        {
            Visibility = Visibility.Visible;
            ApplyMonitor();
        }
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(2500, durationMs));
        _timer.Start();
    }

    private void HideText()
    {
        _timer.Stop();
        Visibility = Visibility.Collapsed;
    }

    /// <summary>Kutuyu ve yazıyı düzene göre yerleştirir (GT_SUBLAYOUT.apply'ın karşılığı).</summary>
    private void Render()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            var s = Layout.Sanitized();
            var widthDip = ActualWidth > 0 ? ActualWidth : Width;
            var heightDip = ActualHeight > 0 ? ActualHeight : Height;
            var scale = OverlayLayoutMath.Scale(widthDip); // DIP -> sayfa pikseli oranı (sayfa 1920 geniş)
            var vw = OverlayLayoutSettings.PageWidth;
            var vh = OverlayLayoutMath.PageHeight(widthDip, heightDip);
            _page.Width = vw;
            _page.Height = vh;
            _page.RenderTransform = new ScaleTransform(scale, scale);

            var (a, r, g, b) = OverlayLayoutSettings.Argb(s.TextColor, 100);
            _text.Foreground = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            var (ba, br, bg, bb) = OverlayLayoutSettings.Argb(s.BgColor, s.BgOpacity);
            _textBg.Background = new SolidColorBrush(Color.FromArgb(ba, br, bg, bb));
            _text.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.SemiBold;
            _text.TextAlignment = s.Align switch { "left" => TextAlignment.Left, "right" => TextAlignment.Right, _ => TextAlignment.Center };
            _textBg.HorizontalAlignment = s.Align switch { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
            _text.Effect = OutlineEffect(s.Outline);
            _text.Text = _shown;

            var margin = Math.Min(s.Margin, Math.Floor(vw / 2) - 1);
            var rect = OverlayLayoutMath.ComputeRect(s, vw, vh, 0);
            _box.Width = rect.Width;
            double size = s.FontSize, contentHeight = 0;
            for (var guard = 0; guard < 60; guard++)
            {
                _text.FontSize = size;
                _text.LineHeight = size * 1.3;
                _textBg.Measure(new Size(rect.Width, double.PositiveInfinity));
                contentHeight = _textBg.DesiredSize.Height;
                if (contentHeight <= vh - 2 * margin || size <= 18) break;
                size -= 2;
            }
            rect = OverlayLayoutMath.ComputeRect(s, vw, vh, contentHeight);
            Canvas.SetLeft(_box, rect.Left);
            Canvas.SetTop(_box, rect.Top);
        }
        finally
        {
            _rendering = false;
        }
    }

    /// <summary>"none" / "shadow" / "outline" / "both" -> WPF etkisi (kontur, gölge ve parlama ile yaklaşık çizilir).</summary>
    private static Effect? OutlineEffect(string kind) => kind switch
    {
        "none" => null,
        "shadow" => new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 4, Opacity = 1 },
        "outline" => new DropShadowEffect { Color = Colors.Black, Direction = 0, ShadowDepth = 0, BlurRadius = 3, Opacity = 1 },
        _ => new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 5, Opacity = 1 },
    };
}
