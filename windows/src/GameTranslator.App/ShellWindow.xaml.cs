// SPDX-License-Identifier: AGPL-3.0-only

using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using GameTranslator.App.Services;
using GameTranslator.App.Views;
using GameTranslator.Shell;
using Microsoft.Win32;

namespace GameTranslator.App;

/// <summary>Uygulamanın tek penceresi: kurulum bitmediyse sihirbaz, bittiyse ana ekran.</summary>
public partial class ShellWindow : Window
{
    private readonly AppController controller;
    private bool showingMain;
    private GlobalHotkeys? hotkeys;
    private PcOverlayTracker? overlayTracker;

    public ShellWindow(AppController controller)
    {
        this.controller = controller;
        InitializeComponent();
        controller.PropertyChanged += OnChanged;
        // Ctrl+Shift+L: altyazı düzeni (Mac: ⌘⇧L); "Bu bilgisayarda" modunda genel kısayol olarak da çalışır (aşağıda).
        InputBindings.Add(new System.Windows.Input.KeyBinding(
            new RelayCommand(() => TvLayoutEditorWindow.Show(controller, this)),
            System.Windows.Input.Key.L, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));
        // Bilgisayar uykudan uyanınca bağlantılar hemen yenilenir.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        // Num 0, 1, 2, 3, 5, 8, 9 kısayolları yalnız çeviri sürerken tutulur.
        SourceInitialized += (_, _) =>
        {
            try
            {
                hotkeys = new GlobalHotkeys(new WindowInteropHelper(this).Handle, OnHotkey);
                hotkeys.Enabled = controller.IsRunning;
                hotkeys.ChordsEnabled = controller.Settings.PcOverlayMode;
            }
            catch { }
        };
        // "Bu bilgisayarda" modu: katman seçilen pencerenin monitörünü izler; Ctrl+Shift+O / Ctrl+Shift+L oyun tarayıcıda tam
        // ekranken de çalışsın diye genel kısayoldur.
        overlayTracker = new PcOverlayTracker(controller);
        overlayTracker.ModeChanged += mode => { if (hotkeys is not null) hotkeys.ChordsEnabled = mode; };
        // Bilgisayarda gösterilecek çeviriler küçük, üstte duran bir kutuda çıkar.
        controller.PcResultReady += PcResultWindow.Display;
        // Canlı oyun içi Türkçe altyazıyı doğrudan bilgisayar ekranında (HUD overlay) göster
        Action<string, int, GameTranslator.Core.TextBox?> overlayHandler = (text, ms, box) => DesktopSubtitleOverlay.Display(text, ms, controller.Settings.Style, box);
        controller.LiveSubtitleShown += overlayHandler;
        controller.SubtitlesCleared += DesktopSubtitleOverlay.Clear;
        DesktopSubtitleOverlay.Initialize();
        Closed += (_, _) =>
        {
            controller.PcResultReady -= PcResultWindow.Display;
            controller.LiveSubtitleShown -= overlayHandler;
            controller.SubtitlesCleared -= DesktopSubtitleOverlay.Clear;
            DesktopSubtitleOverlay.Clear();
            overlayTracker?.Dispose();
            hotkeys?.Dispose();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            controller.Dispose();
        };
        ShowScreen(force: true);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppController.Step)) ShowScreen();
        if (e.PropertyName == nameof(AppController.IsRunning) && hotkeys is not null) hotkeys.Enabled = controller.IsRunning;
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ExtraRegion1: _ = controller.TranslateExtraRegionAsync(1); break;
            case HotkeyAction.ExtraRegion2: _ = controller.TranslateExtraRegionAsync(2); break;
            case HotkeyAction.ExtraRegion3: _ = controller.TranslateExtraRegionAsync(3); break;
            case HotkeyAction.PauseSubtitles: controller.ToggleSubtitles(); break;
            case HotkeyAction.Snapshot: controller.RequestSnapshot(); break;
            case HotkeyAction.FreeArea: FreeAreaWindow.Open(controller); break;
            case HotkeyAction.Screen: _ = controller.TranslateScreenAsync(); break;
            case HotkeyAction.OverlayToggle: DesktopSubtitleOverlay.Toggle(); break;
            case HotkeyAction.LayoutEditor: TvLayoutEditorWindow.Show(controller, this); break;
        }
    }

    private void ShowScreen(bool force = false)
    {
        var main = controller.Step == OnboardingStep.Done;
        if (!force && main == showingMain) return;
        showingMain = main;
        Host.Content = main ? new MainView(controller) : new OnboardingView(controller);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.Invoke(controller.SystemResumed);
    }
}

/// <summary>Kısayol bağlaması için en küçük komut.</summary>
internal sealed class RelayCommand(Action run) : System.Windows.Input.ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
