// SPDX-License-Identifier: AGPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
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
    private static ScreenRect? _gameRect; // oyunun (yakalanan pencere/monitör) ekran pikseli dikdörtgeni; ekran kartları buna oturur

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
            if (!value)
            {
                Clear();
                ClearScreen(null);
            }
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
        Notice(_enabled ? "Ludora: altyazı katmanı açık (Ctrl+Shift+O ile kapat)" : "");
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

    /// <summary>
    /// Yakalanan oyun alanının (pencere istemci alanı ya da monitör) sanal masaüstü pikseli. Ekran kartlarının
    /// normalize kutuları buraya oturur; verilmezse monitörün tamamı kullanılır.
    /// </summary>
    public static void SetGameRect(ScreenRect? rect)
    {
        if (_gameRect == rect) return;
        _gameRect = rect;
        if (Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() => _instance?.RenderScreen());
    }

    /// <summary>
    /// Ekran metni çevirisini (TV'deki <c>screen</c> olayı) katmanda çizer: "over" kartları İngilizce öğeyi örter, "beside"
    /// telefonun yanında sohbet paneli olur. Aynı bölgenin öncekileri değişir; süre dolunca ya da bölge boşalınca kalkar.
    /// </summary>
    public static void ShowScreen(string region, IReadOnlyList<ScreenItem> items, int durationMs)
    {
        if (!_enabled || Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _instance ??= new DesktopSubtitleOverlay();
                _instance.ShowScreenRegion(region, items, durationMs);
            }
            catch { }
        });
    }

    /// <summary>Bölgeyi temizler; <paramref name="region"/> null ise hepsini.</summary>
    public static void ClearScreen(string? region)
    {
        if (Application.Current == null) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try { _instance?.HideScreenRegion(region); }
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
    private readonly Canvas _screenRoot = new() { ClipToBounds = false, IsHitTestVisible = false };
    private readonly Canvas _screenLayer = new() { ClipToBounds = true };
    private readonly Dictionary<string, ScreenRegion> _regions = new(StringComparer.Ordinal);

    private sealed class ScreenRegion(Canvas host, DispatcherTimer timer, IReadOnlyList<ScreenItem> items)
    {
        public Canvas Host { get; } = host;
        public DispatcherTimer Timer { get; } = timer;
        public IReadOnlyList<ScreenItem> Items { get; } = items;
    }

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
        _screenRoot.Children.Add(_screenLayer);
        Content = new Grid { Background = Brushes.Transparent, Children = { _page, _screenRoot } };
        Visibility = Visibility.Collapsed;

        _timer.Tick += (_, _) => HideText();

        SourceInitialized += (_, _) => ApplyStyles();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(ApplyMonitor, DispatcherPriority.Background);
        SizeChanged += (_, _) =>
        {
            if (Visibility != Visibility.Visible) return;
            Render();
            RenderScreen();
        };
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
            if (Visibility == Visibility.Visible)
            {
                Render();
                RenderScreen();
            }
        }
        catch { }
    }

    private void ShowText(string text, int durationMs)
    {
        _shown = text;
        _box.Visibility = Visibility.Visible;
        ApplyStyles();
        Render();
        EnsureVisible();
        _timer.Stop();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(2500, durationMs));
        _timer.Start();
    }

    private void HideText()
    {
        _timer.Stop();
        _box.Visibility = Visibility.Collapsed;
        CollapseIfIdle();
    }

    private void EnsureVisible()
    {
        if (Visibility == Visibility.Visible) return;
        Visibility = Visibility.Visible;
        ApplyMonitor();
    }

    /// <summary>Altyazı da ekran kartı da yoksa pencere tamamen gizlenir.</summary>
    private void CollapseIfIdle()
    {
        if (_box.Visibility != Visibility.Visible && _regions.Count == 0) Visibility = Visibility.Collapsed;
    }

    // --- Ekran metni kartları (tv/webos/app.js showScreen / showOverItem / showBesideItem'in karşılığı) ---

    private void ShowScreenRegion(string region, IReadOnlyList<ScreenItem> items, int durationMs)
    {
        HideScreenRegion(region, collapse: false);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(2500, durationMs)) };
        var host = new Canvas();
        var state = new ScreenRegion(host, timer, items);
        _regions[region] = state;
        _screenLayer.Children.Add(host);
        timer.Tick += (_, _) => HideScreenRegion(region);
        timer.Start();
        ApplyStyles();
        EnsureVisible();
        PlaceScreenLayer(out var pageWidth, out var pageHeight);
        FillRegion(state, pageWidth, pageHeight, animate: true);
    }

    private void HideScreenRegion(string? region, bool collapse = true)
    {
        var names = region is null ? _regions.Keys.ToList() : [region];
        foreach (var name in names)
        {
            if (!_regions.Remove(name, out var state)) continue;
            state.Timer.Stop();
            _screenLayer.Children.Remove(state.Host);
        }
        if (collapse) CollapseIfIdle();
    }

    /// <summary>
    /// Kartların sayfasını oyunun dikdörtgenine oturtur: sayfa 1920 geniş (TV ile aynı birim), yüksekliği oyunun en-boy
    /// oranından gelir; DIP ölçeğiyle küçültülür. Oyun dikdörtgeni verilmemişse monitörün tamamı.
    /// </summary>
    private void PlaceScreenLayer(out double pageWidth, out double pageHeight)
    {
        var monitor = CurrentMonitor;
        var game = _gameRect ?? monitor;
        var dipPerPixel = ActualWidth > 0 && monitor.Width > 0 ? ActualWidth / monitor.Width : 1;
        double gw = Math.Max(1, game.Width * dipPerPixel), gh = Math.Max(1, game.Height * dipPerPixel);
        var scale = gw / OverlayLayoutSettings.PageWidth;
        pageWidth = OverlayLayoutSettings.PageWidth;
        pageHeight = gh / scale;
        _screenLayer.Width = pageWidth;
        _screenLayer.Height = pageHeight;
        _screenLayer.RenderTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(_screenLayer, (game.Left - monitor.Left) * dipPerPixel);
        Canvas.SetTop(_screenLayer, (game.Top - monitor.Top) * dipPerPixel);
    }

    /// <summary>Pencere/oyun dikdörtgeni değişince açık bölgeler yeniden yerleştirilir.</summary>
    private void RenderScreen()
    {
        if (_regions.Count == 0) return;
        PlaceScreenLayer(out var pageWidth, out var pageHeight);
        foreach (var state in _regions.Values) FillRegion(state, pageWidth, pageHeight, animate: false);
    }

    private void FillRegion(ScreenRegion state, double vw, double vh, bool animate)
    {
        state.Host.Children.Clear();
        var s = Layout.Sanitized();
        var margin = Math.Min(s.Margin, Math.Floor(vw / 2) - 1);
        var placed = new List<(FrameworkElement Element, CardRect Rect)>();
        foreach (var item in state.Items)
        {
            if (item.Box.Right <= item.Box.Left || item.Box.Bottom <= item.Box.Top) continue;
            FrameworkElement? element = null;
            CardRect rect = default;
            if (item.Placement == "beside" && ScreenCards.Messages(item).Count > 0)
            {
                (element, rect) = BuildChatPanel(item, vw, vh, margin);
                state.Host.Children.Add(element);
                Canvas.SetLeft(element, rect.Left);
                Canvas.SetTop(element, rect.Top);
            }
            else if (item.Placement == "over" && item.Text.Length > 0)
            {
                (element, rect) = BuildOverCard(item, vw, vh, margin, s);
                state.Host.Children.Add(element);
                Canvas.SetLeft(element, rect.Left);
                Canvas.SetTop(element, rect.Top);
                placed.Add((element, rect));
            }
            if (element is null) continue;
            if (animate) element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }
        // Aynı bölgedeki kartlar (birden çok bildirim) üst üste binmesin: sırayla aşağı itilir.
        if (placed.Count > 1)
        {
            var stacked = ScreenCards.Stack(placed.Select(p => p.Rect).ToList(), ScreenCards.StackGap);
            for (var i = 0; i < placed.Count; i++) Canvas.SetTop(placed[i].Element, stacked[i].Top);
        }
    }

    private static SolidColorBrush Fill(byte r, byte g, byte b, double alpha) =>
        new(Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), r, g, b));

    private static readonly FontFamily CardFont = new("Segoe UI");

    private static TextBlock CardText(string text, double size, FontWeight weight, Brush foreground, double lineHeightFactor = 1.25) => new()
    {
        Text = text, FontFamily = CardFont, FontSize = size, FontWeight = weight, Foreground = foreground,
        TextWrapping = TextWrapping.Wrap, LineHeight = size * lineHeightFactor, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
    };

    /// <summary>"over": Türkçe kart İngilizce öğeyi tam örter (temalı, neredeyse opak zemin); sığmazsa büyür, sonra yazı küçülür (en az 18).</summary>
    private static (FrameworkElement, CardRect) BuildOverCard(ScreenItem item, double vw, double vh, double margin, OverlayLayoutSettings s)
    {
        var style = ScreenCards.OverStyle(item.Theme, s.BgOpacity);
        var parts = ScreenCards.Parts(item);
        var foreground = new SolidColorBrush(Color.FromRgb(style.Text.R, style.Text.G, style.Text.B));
        var stack = new StackPanel();
        var card = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Fill(style.Background.R, style.Background.G, style.Background.B, style.Alpha),
            Child = stack,
            Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 2, BlurRadius = 10, Opacity = style.Light ? 0.28 : 0.45 },
        };
        var size = ScreenCards.OverFontSize(item.LineHeight, vh, s.FontSize);
        var boxH = Math.Max(0, (item.Box.Bottom - item.Box.Top) * vh);
        var rect = ScreenCards.OverRect(item.Box, vw, vh, margin, 0);
        for (var guard = 0; guard < 60; guard++)
        {
            stack.Children.Clear();
            if (parts.Speaker.Length > 0) stack.Children.Add(CardText(parts.Speaker, size * 1.08, FontWeights.Bold, foreground));
            for (var i = 0; i < parts.Segments.Count; i++)
                stack.Children.Add(CardText(parts.Segments[i], size, i == 0 && parts.TitleBold ? FontWeights.Bold : FontWeights.SemiBold, foreground));
            card.Padding = new Thickness(size * 0.7, size * 0.4, size * 0.7, size * 0.4);
            card.Width = rect.Width;
            card.MinHeight = rect.Height;
            card.Measure(new Size(rect.Width, double.PositiveInfinity));
            var content = card.DesiredSize.Height;
            rect = ScreenCards.OverRect(item.Box, vw, vh, margin, content);
            var limit = Math.Max(rect.Height, ScreenCards.OverMaxGrow(boxH, rect.MaxHeight));
            if (!ScreenCards.ShouldShrink(content, limit, size)) break;
            size -= 2;
        }
        return (card, rect);
    }

    /// <summary>"beside": telefonun yanında sohbet paneli (başlıkta kişi adı; "them" solda, "me" sağda mavi). Taşarsa en eski mesajlar atılır.</summary>
    private static (FrameworkElement, CardRect) BuildChatPanel(ScreenItem item, double vw, double vh, double margin)
    {
        var (rect, _) = ScreenCards.BesideRect(item.Box, vw, vh, margin);
        var fontSize = ScreenCards.BesideFontSize(vh);
        var white = Brushes.White;
        const double padX = 12, padY = 10;
        var stack = new StackPanel();
        var panel = new Border
        {
            Width = rect.Width, MaxHeight = rect.MaxHeight, CornerRadius = new CornerRadius(14), ClipToBounds = true,
            Padding = new Thickness(padX, padY, padX, padY), Background = Fill(12, 12, 16, 0.78), Child = stack,
            Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 4, BlurRadius = 18, Opacity = 0.45 },
        };
        double headHeight = 0;
        var who = item.Speaker?.Trim() ?? "";
        if (who.Length > 0)
        {
            var head = CardText(who, fontSize * 0.85, FontWeights.Bold, Fill(255, 255, 255, 0.7));
            head.Margin = new Thickness(0, 0, 0, 6);
            head.Measure(new Size(rect.Width - (2 * padX), double.PositiveInfinity));
            headHeight = head.DesiredSize.Height + 6;
            stack.Children.Add(head);
        }
        var bubbles = new List<Border>();
        var heights = new List<double>();
        var list = new StackPanel();
        foreach (var message in ScreenCards.Messages(item))
        {
            var me = message.FromMe;
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 6, 10, 6),
                MaxWidth = 0.85 * (rect.Width - (2 * padX)),
                HorizontalAlignment = me ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Background = me ? Fill(10, 132, 255, 0.85) : Fill(255, 255, 255, 0.14),
                Child = CardText(message.Text, fontSize, FontWeights.Normal, white),
            };
            bubble.Measure(new Size(rect.Width - (2 * padX), double.PositiveInfinity));
            heights.Add(bubble.DesiredSize.Height);
            bubbles.Add(bubble);
            list.Children.Add(bubble);
        }
        // Balonlar arası 6 px boşluk (son balonun alt boşluğu yok); yükseklikler boşluksuz ölçüldü.
        for (var i = 0; i < bubbles.Count - 1; i++) bubbles[i].Margin = new Thickness(0, 0, 0, 6);
        var available = rect.MaxHeight - (2 * padY) - headHeight;
        var first = ScreenCards.BesideFirstVisible(heights, 6, available);
        for (var i = 0; i < first; i++) bubbles[i].Visibility = Visibility.Collapsed;
        stack.Children.Add(list);
        return (panel, rect);
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
