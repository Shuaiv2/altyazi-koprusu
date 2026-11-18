// SPDX-License-Identifier: AGPL-3.0-only

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameTranslator.App.Services;
using GameTranslator.Core;
using GameTranslator.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace GameTranslator.App.Views;

/// <summary>
/// "TV Altyazı Düzeni": Mac uygulamasıyla ortak düzenleyici sayfasını (tv/editor, uygulama çıktısında tv-web/) WebView2'de
/// açar. Sayfa Mac'te <c>window.webkit.messageHandlers.altyazi.postMessage</c> kullanır; burada belge oluşurken enjekte
/// edilen küçük bir kabuk bunu <c>window.chrome.webview.postMessage</c>'e bağlar, sayfa değişmez. İleti mantığı
/// <see cref="TvLayoutEditorBridge"/>'dedir (önizleme ≤10/sn, kaydet/vazgeç).
/// </summary>
public sealed class TvLayoutEditorWindow : Window
{
    /// <summary>Mac sayfasının beklediği <c>webkit.messageHandlers.altyazi</c> arayüzünü WebView2'ye bağlar.</summary>
    public const string ShimScript =
        "(function(){window.webkit=window.webkit||{};window.webkit.messageHandlers=window.webkit.messageHandlers||{};" +
        "window.webkit.messageHandlers.altyazi={postMessage:function(m){window.chrome.webview.postMessage(m);}};})();";

    private const string Host = "altyazi.local";
    private static TvLayoutEditorWindow? open;

    private readonly AppController controller;
    private readonly WebView2 browser = new();
    private readonly TvLayoutEditorBridge bridge;
    private readonly Action statusChanged;

    private readonly bool overlay;

    private TvLayoutEditorWindow(AppController controller, bool overlay)
    {
        this.controller = controller;
        this.overlay = overlay;
        Title = overlay ? "Bu Bilgisayarda Altyazı Düzeni" : "TV Altyazı Düzeni";
        Width = 1200;
        Height = 760;
        MinWidth = 800;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (overlay)
        {
            // Bu bilgisayardaki katmanın düzeni: aynı sayfa ve ayar modeli, TV yerine yerel katman ve yerel dosya.
            bridge = new TvLayoutEditorBridge(new OverlayLayoutPort(DesktopSubtitleOverlay.Store, DesktopSubtitleOverlay.SetLayout),
                () => Environment.TickCount64, Schedule, OverlayLayoutPort.ConnectedLabel);
            statusChanged = () => { };
        }
        else
        {
            bridge = new TvLayoutEditorBridge(controller, () => Environment.TickCount64, Schedule);
            statusChanged = () => Dispatcher.BeginInvoke(PushStatus);
            controller.TvLayoutStatusChanged += statusChanged;
        }

        var page = PagePath();
        if (page is null)
        {
            Content = new TextBlock
            {
                Text = "TV altyazı düzenleyicisi bu sürümde bulunamadı.\n\nUygulama klasöründe tv-web\\editor\\editor.html dosyası yok.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                MaxWidth = 520,
                FontSize = 15,
            };
        }
        else
        {
            Content = browser;
            Loaded += async (_, _) => await OpenAsync();
        }
        Closed += OnClosed;
    }

    /// <summary>
    /// Pencereyi açar (zaten açıksa öne getirir). <paramref name="overlay"/> null ise "Bu bilgisayarda" modu açıkken
    /// bilgisayardaki katmanın, değilse TV'nin düzeni düzenlenir.
    /// </summary>
    public static void Show(AppController controller, Window? owner, bool? overlay = null)
    {
        var forOverlay = overlay ?? controller.Settings.PcOverlayMode;
        if (open is not null)
        {
            if (open.overlay == forOverlay) { open.Activate(); return; }
            open.Close();
        }
        open = new TvLayoutEditorWindow(controller, forOverlay) { Owner = owner };
        open.Show();
    }

    private static string? WebRoot()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "tv-web");
        return Directory.Exists(root) ? root : null;
    }

    private static string? PagePath() =>
        WebRoot() is { } root && File.Exists(Path.Combine(root, "editor", "editor.html")) ? Path.Combine(root, "editor", "editor.html") : null;

    private IDisposable Schedule(int delayMs, Action run)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)) };
        timer.Tick += (_, _) => { timer.Stop(); run(); };
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    private async Task OpenAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.Data, "webview-editor"));
            await browser.EnsureCoreWebView2Async(environment);
            var core = browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, WebRoot()!, CoreWebView2HostResourceAccessKind.Allow);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ShimScript);
            core.WebMessageReceived += OnMessage;
            core.Navigate($"https://{Host}/editor/editor.html");
        }
        catch (Exception error)
        {
            SubtitleLog.Write($"TV düzenleyicisi açılamadı: {error.GetType().Name}: {error.Message}");
            Content = new TextBlock
            {
                Text = "Altyazı düzenleyicisi açılamadı. Microsoft Edge WebView2 Runtime yüklü olmalı.",
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 15,
            };
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.WebMessageAsJson; }
        catch { return; }
        foreach (var script in bridge.Handle(json)) _ = Run(script);
    }

    private void PushStatus()
    {
        if (browser.CoreWebView2 is not null) _ = Run(bridge.StatusScript());
    }

    private async Task Run(string script)
    {
        try { if (browser.CoreWebView2 is { } core) await core.ExecuteScriptAsync(script); }
        catch { }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!overlay) controller.TvLayoutStatusChanged -= statusChanged;
        // Kaydedilmeden kapanırsa TV'deki (ya da bilgisayardaki katmandaki) önizleme geri alınır.
        bridge.Closed();
        try { browser.Dispose(); } catch { }
        if (open == this) open = null;
    }
}
