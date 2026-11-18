// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using GameTranslator.Core;

namespace GameTranslator.Shell.Tests;

public class WindowRankingTests
{
    private static WindowEntry W(string title, string process, int w = 1920, int h = 1080, nint handle = 1) => new(handle, title, process, w, h);

    [Fact]
    public void XboxBrowserWindowsComeFirst()
    {
        var ranked = WindowRanking.Rank([
            W("Notlar", "notepad", 3000, 2000),
            W("Haberler - Google Chrome", "chrome", 1800, 1000),
            W("Xbox Remote Play - Microsoft Edge", "msedge", 1000, 700),
        ]);
        Assert.Equal(["msedge", "chrome", "notepad"], ranked.Select(r => r.Process));
    }

    [Theory]
    [InlineData("Xbox Uzaktan Oynatma", true)]
    [InlineData("Forza - Xbox Cloud Gaming", true)]
    [InlineData("Remote Play - Edge", true)]
    [InlineData("YouTube", false)]
    public void RecognisesRemotePlayTitles(string title, bool expected) =>
        Assert.Equal(expected, WindowRanking.IsXboxRemotePlay(W(title, "msedge")));

    [Fact]
    public void XboxTitleInNonBrowserIsNotPromoted()
    {
        Assert.False(WindowRanking.IsXboxRemotePlay(W("Xbox", "explorer")));
        var ranked = WindowRanking.Rank([W("Xbox Uygulaması", "XboxPcApp", 800, 600), W("Sayfa - Firefox", "firefox", 500, 400)]);
        Assert.Equal("firefox", ranked[0].Process);
    }

    [Fact]
    public void TinyAndUntitledWindowsAreDropped()
    {
        var ranked = WindowRanking.Rank([W("", "chrome"), W("Küçük", "chrome", 100, 80), W("Tamam", "chrome")]);
        Assert.Single(ranked);
        Assert.Equal("Tamam", ranked[0].Title);
    }

    [Fact]
    public void LargerWindowWinsWithinGroup()
    {
        var ranked = WindowRanking.Rank([W("a", "calc", 400, 300), W("b", "notepad", 900, 700)]);
        Assert.Equal("b", ranked[0].Title);
    }
}

public class SavedWindowSourceTests
{
    private static WindowEntry W(string title, string process, int w = 1920, int h = 1080, nint handle = 1) => new(handle, title, process, w, h);

    [Fact]
    public void PatternIsTheStableHintNotTheGameName()
    {
        var saved = SavedWindowSource.From(W("Forza Horizon 5 - Xbox Cloud Gaming - Microsoft Edge", "msedge"));
        Assert.Equal("msedge", saved.Process);
        Assert.Equal("Xbox", saved.TitlePattern);
    }

    [Fact]
    public void PatternFallsBackToWholeTitle() =>
        Assert.Equal("Oyun Penceresi", SavedWindowSource.From(W("  Oyun Penceresi ", "game")).TitlePattern);

    [Fact]
    public void ReselectsAfterRestartWhenGameTitleChanged()
    {
        var saved = SavedWindowSource.From(W("Halo - Xbox Cloud Gaming", "chrome", handle: 5));
        var open = new[] { W("Gmail", "chrome", handle: 6), W("Starfield - Xbox Cloud Gaming", "chrome", handle: 9), W("Xbox", "firefox", handle: 7) };
        Assert.Equal((nint)9, saved.Find(open)?.Handle);
    }

    [Fact]
    public void DoesNotPickSameProcessWithUnrelatedTitle()
    {
        var saved = new SavedWindowSource("chrome", "Xbox");
        Assert.Null(saved.Find([W("Gmail", "chrome")]));
    }

    [Fact]
    public void ProcessMatchIsCaseInsensitiveAndPicksLargest()
    {
        var saved = new SavedWindowSource("MSEdge", "xbox");
        var found = saved.Find([W("Xbox 1", "msedge", 800, 600, 1), W("Xbox 2", "msedge", 1920, 1080, 2)]);
        Assert.Equal((nint)2, found?.Handle);
    }

    [Fact]
    public void SettingsRoundTripKeepsOverlayAndSourceFields()
    {
        var settings = new UserSettings().WithPcOverlayMode(true) with { SourceProcess = "msedge", SourceTitlePattern = "Xbox", DesktopWindowName = "window:Xbox" };
        var back = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(back.PcOverlayMode);
        Assert.Equal(new SavedWindowSource("msedge", "Xbox"), back.SavedSource);
        Assert.Equal(VideoSourceType.DesktopScreen, back.VideoSource);
    }

    [Fact]
    public void OldSettingsFileLoadsWithOverlayOff()
    {
        var back = JsonSerializer.Deserialize<UserSettings>("{\"AutoRegion\":true}")!;
        Assert.False(back.PcOverlayMode);
        Assert.Null(back.SavedSource);
    }

    [Fact]
    public void EnablingModeDefaultsToBottomCenterOnceThenKeepsUserRegion()
    {
        var on = new UserSettings().WithPcOverlayMode(true);
        Assert.Equal(UserSettings.BottomCenterRegion, on.Region);
        Assert.False(on.AutoRegion);
        var custom = on with { Region = new CropRegion(0.5, 0.6, 0.1, 0.9) };
        Assert.Equal(custom.Region, custom.WithPcOverlayMode(true).Region);
        Assert.False(custom.WithPcOverlayMode(false).PcOverlayMode);
    }
}

public class MonitorPickerTests
{
    private static readonly ScreenRect Left = new(0, 0, 1920, 1080);
    private static readonly ScreenRect Right = new(1920, 0, 2560, 1440);

    [Fact]
    public void PicksMonitorWithMostOverlap() =>
        Assert.Equal(Right, MonitorPicker.For(new ScreenRect(1800, 100, 1000, 800), [Left, Right]));

    [Fact]
    public void FullscreenWindowMapsToItsMonitor() =>
        Assert.Equal(Left, MonitorPicker.For(new ScreenRect(0, 0, 1920, 1080), [Left, Right]));

    [Fact]
    public void OffScreenWindowFallsBackToNearest() =>
        Assert.Equal(Right, MonitorPicker.For(new ScreenRect(5000, 100, 300, 300), [Left, Right]));

    [Fact]
    public void NoMonitorsIsNull() => Assert.Null(MonitorPicker.For(new ScreenRect(0, 0, 10, 10), []));
}

public class OverlayLayoutTests
{
    [Fact]
    public void DefaultsMatchTvDefaults()
    {
        var d = OverlayLayoutSettings.Default;
        Assert.Equal((960, 1004, 1400, 44, 48), (d.X, d.Y, d.Width, d.FontSize, d.Margin));
        Assert.Equal("shadow", d.Outline);
        Assert.True(d.Bold);
    }

    [Fact]
    public void ParseSanitizesAndRejectsGarbage()
    {
        Assert.Null(OverlayLayoutSettings.Parse("not json"));
        Assert.Null(OverlayLayoutSettings.Parse("[1]"));
        var s = OverlayLayoutSettings.Parse("""{"x":99999,"y":-5,"width":50,"fontSize":500,"margin":-3,"textColor":"red","bgColor":"#1e1e1e","bgOpacity":150,"outline":"x","align":"right","bold":false}""")!;
        Assert.Equal(1920, s.X);
        Assert.Equal(0, s.Y);
        Assert.Equal(300, s.Width);
        Assert.Equal(120, s.FontSize);
        Assert.Equal(0, s.Margin);
        Assert.Equal("#FFFFFF", s.TextColor);
        Assert.Equal("#1E1E1E", s.BgColor);
        Assert.Equal(100, s.BgOpacity);
        Assert.Equal("shadow", s.Outline);
        Assert.Equal("right", s.Align);
        Assert.False(s.Bold);
    }

    [Fact]
    public void JsonUsesTvKeys()
    {
        using var doc = JsonDocument.Parse(OverlayLayoutSettings.Default.ToJson());
        foreach (var key in new[] { "x", "y", "width", "fontSize", "margin", "textColor", "bgColor", "bgOpacity", "outline", "align", "bold", "version" })
            Assert.True(doc.RootElement.TryGetProperty(key, out _), key);
        Assert.Equal(OverlayLayoutSettings.Default, OverlayLayoutSettings.Parse(OverlayLayoutSettings.Default.ToJson()));
    }

    [Fact]
    public void ScalesPageToMonitor()
    {
        Assert.Equal(1.0, OverlayLayoutMath.Scale(1920));
        Assert.Equal(2.0, OverlayLayoutMath.Scale(3840));
        Assert.Equal(1080, OverlayLayoutMath.PageHeight(1920, 1080));
        Assert.Equal(1080, OverlayLayoutMath.PageHeight(3840, 2160));
        // 21:9 monitör: sayfa daha kısa.
        Assert.Equal(Math.Round(1440 / (2560 / 1920.0)), Math.Round(OverlayLayoutMath.PageHeight(2560, 1440)));
        Assert.True(OverlayLayoutMath.PageHeight(3440, 1440) < 1080);
        Assert.Equal(1080, OverlayLayoutMath.PageHeight(0, 0));
    }

    [Fact]
    public void RectBottomCenterDefault()
    {
        var r = OverlayLayoutMath.ComputeRect(OverlayLayoutSettings.Default, 1920, 1080, 100);
        Assert.Equal(new PageRect(260, 904, 1400, 1004), r);
    }

    [Fact]
    public void RectIsClampedInsideMargins()
    {
        var s = OverlayLayoutSettings.Default with { X = 0, Y = 5000, Width = 1400, Margin = 48 };
        var r = OverlayLayoutMath.ComputeRect(s, 1920, 1080, 100);
        Assert.Equal(48, r.Left);
        Assert.Equal(1032, r.Bottom);
        Assert.Equal(932, r.Top);
        var top = OverlayLayoutMath.ComputeRect(OverlayLayoutSettings.Default with { Y = 0 }, 1920, 1080, 100);
        Assert.Equal(148, top.Bottom);
        Assert.Equal(48, top.Top);
    }

    [Fact]
    public void WidthNeverExceedsPageMinusMargins()
    {
        var r = OverlayLayoutMath.ComputeRect(OverlayLayoutSettings.Default with { Width = 1920, Margin = 100 }, 1920, 1080, 50);
        Assert.Equal(1720, r.Width);
        Assert.Equal(100, r.Left);
    }

    [Fact]
    public void TooTallContentSticksToTopMargin()
    {
        var r = OverlayLayoutMath.ComputeRect(OverlayLayoutSettings.Default, 1920, 1080, 2000);
        Assert.Equal(48, r.Top);
        Assert.Equal(2048, r.Bottom);
    }

    [Fact]
    public void ArgbFromHexAndOpacity()
    {
        Assert.Equal(((byte)153, (byte)0, (byte)0, (byte)0), OverlayLayoutSettings.Argb("#000000", 60));
        Assert.Equal(((byte)255, (byte)0xFF, (byte)0xE6, (byte)0), OverlayLayoutSettings.Argb("#FFE600", 100));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), OverlayLayoutSettings.Argb("bad", 0));
    }

    [Fact]
    public void OverlayCoversWholeMonitor()
    {
        var monitor = new ScreenRect(-1920, 0, 1920, 1080);
        Assert.Equal(monitor, OverlayLayoutMath.OverlayRect(monitor));
    }
}

public class OverlayLayoutStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "overlay-" + Guid.NewGuid().ToString("N"));
    private string File_ => Path.Combine(dir, "sub", "overlay-layout.json");

    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [Fact]
    public void MissingOrCorruptFileGivesDefaults()
    {
        var store = new OverlayLayoutStore(File_);
        Assert.Null(store.Load());
        Assert.Equal(OverlayLayoutSettings.Default, store.Current);
        Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
        File.WriteAllText(File_, "{ bozuk");
        Assert.Null(store.Load());
        Assert.Equal(OverlayLayoutSettings.Default, store.Current);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        var store = new OverlayLayoutStore(File_);
        var layout = OverlayLayoutSettings.Default with { X = 400, Y = 300, FontSize = 60, Align = "left", TextColor = "#FFE600", Bold = false };
        var when = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        store.Save(layout, when);
        var loaded = new OverlayLayoutStore(File_).Load();
        Assert.NotNull(loaded);
        Assert.Equal(layout, loaded!.Value.Settings);
        Assert.Equal(when, loaded.Value.At);
    }

    [Fact]
    public void NullPathNeverTouchesDisk()
    {
        var store = new OverlayLayoutStore(null);
        store.Save(OverlayLayoutSettings.Default);
        Assert.Null(store.Load());
    }
}

public class OverlayLayoutPortTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "overlayport-" + Guid.NewGuid().ToString("N"));
    private readonly List<OverlayLayoutSettings?> applied = [];

    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [Fact]
    public void PreviewAppliesLiveWithoutSaving_SaveWritesFile_CancelReverts()
    {
        var store = new OverlayLayoutStore(Path.Combine(dir, "o.json"));
        var port = new OverlayLayoutPort(store, applied.Add);
        var json = (OverlayLayoutSettings.Default with { X = 700 }).ToJson();

        port.SendLayout("preview", json);
        Assert.Equal(700, applied[^1]!.X);
        Assert.Null(store.Load());

        port.SendLayout("save", json);
        Assert.Equal(700, store.Current.X);
        Assert.Equal(700, applied[^1]!.X);
        Assert.NotNull(port.LastTvLayout);

        port.SendLayout("cancel", "{}");
        Assert.Null(applied[^1]);
    }

    [Fact]
    public void GarbagePreviewIsIgnored()
    {
        var port = new OverlayLayoutPort(new OverlayLayoutStore(null), applied.Add);
        port.SendLayout("preview", "oops");
        Assert.Empty(applied);
        Assert.True(port.TvConnected);
    }

    [Fact]
    public void EditorBridgeDrivesOverlayPort()
    {
        var store = new OverlayLayoutStore(Path.Combine(dir, "o.json"));
        var port = new OverlayLayoutPort(store, applied.Add);
        var bridge = new TvLayoutEditorBridge(port, () => 1_000_000, (_, _) => new NoopTimer(), OverlayLayoutPort.ConnectedLabel);

        var status = bridge.StatusScript();
        Assert.Contains(JsonSerializer.Serialize(OverlayLayoutPort.ConnectedLabel), status);
        Assert.Contains("__editorStatus(true", status);

        bridge.Handle("""{"type":"save","settings":{"x":500,"y":400,"width":1000,"fontSize":50,"margin":40,"textColor":"#FFFFFF","bgColor":"#000000","bgOpacity":50,"outline":"both","align":"center","bold":true}}""");
        Assert.Equal(500, store.Current.X);
        bridge.Closed();
        Assert.Equal(500, applied[^1]!.X); // kaydedildiği için kapanışta geri alınmaz
    }

    private sealed class NoopTimer : IDisposable { public void Dispose() { } }
}
