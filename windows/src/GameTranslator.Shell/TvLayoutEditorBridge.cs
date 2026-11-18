// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace GameTranslator.Shell;

/// <summary>TV altyazı düzeni düzenleyicisinin konuştuğu uç (AppController ya da test sahtesi).</summary>
public interface ITvLayoutPort
{
    bool TvConnected { get; }
    (string Json, DateTimeOffset? At)? LastTvLayout { get; }
    void SendLayout(string mode, string settingsJson);
}

/// <summary>
/// "TV Altyazı Düzeni" penceresinin mantığı (Mac: TVLayoutEditor, mac/NativeApp.swift). Sayfa
/// <c>{type: ready|preview|save|cancel, settings}</c> iletileri yollar; önizleme saniyede en çok 10 kez TV'ye gider
/// (aradaki en son önizleme zamanlayıcıyla gönderilir), kaydetmeden kapanırsa TV'ye "cancel" gider.
/// WebView2'ye bağımlı değil: iletiyi alır, sayfada çalıştırılacak betikleri döndürür.
/// </summary>
public sealed class TvLayoutEditorBridge(ITvLayoutPort port, Func<long> clockMs, Func<int, Action, IDisposable> schedule)
{
    /// <summary>Önizlemeler arasındaki en küçük süre (10/sn).</summary>
    public const int PreviewIntervalMs = 100;

    private readonly object gate = new();
    private long lastPreviewAt = long.MinValue / 2;
    private string? pendingPreview;
    private IDisposable? previewTimer;
    private bool saved;

    /// <summary>true ise "save" gönderilmiş; kapanışta "cancel" gitmez.</summary>
    public bool Saved { get { lock (gate) return saved; } }

    /// <summary>Sayfaya ilk durum: kayıtlı düzen ve TV bağlantısı (sayfadaki <c>__editorLoad</c>).</summary>
    public string LoadScript()
    {
        var json = port.LastTvLayout?.Json ?? "null";
        return $"window.__editorLoad && window.__editorLoad({json}, {Bool(port.TvConnected)})";
    }

    /// <summary>TV bağlantısı ya da bildirilen düzen değişince sayfadaki <c>__editorStatus</c> çağrılır.</summary>
    public string StatusScript()
    {
        var at = port.LastTvLayout?.At is { } time ? JsonSerializer.Serialize(time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")) : "null";
        return $"window.__editorStatus && window.__editorStatus({Bool(port.TvConnected)}, {at})";
    }

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Sayfadan gelen ileti (WebView2 <c>WebMessageAsJson</c>). Sayfada çalıştırılacak betikler döner.</summary>
    public IReadOnlyList<string> Handle(string messageJson)
    {
        string? type;
        string? settings = null;
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            type = doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (doc.RootElement.TryGetProperty("settings", out var s) && s.ValueKind == JsonValueKind.Object) settings = s.GetRawText();
        }
        catch (JsonException) { return []; }

        switch (type)
        {
            case "ready":
                return [LoadScript(), StatusScript()];
            case "preview":
                if (settings is not null) Preview(settings);
                return [];
            case "save":
                if (settings is null) return [];
                lock (gate) { CancelPending(); saved = true; }
                port.SendLayout("save", settings);
                return [];
            case "cancel":
                lock (gate) CancelPending();
                port.SendLayout("cancel", "{}");
                return [];
            default:
                return [];
        }
    }

    private void Preview(string settings)
    {
        bool sendNow;
        lock (gate)
        {
            var since = clockMs() - lastPreviewAt;
            sendNow = since >= PreviewIntervalMs;
            if (sendNow)
            {
                lastPreviewAt = clockMs();
                pendingPreview = null;
            }
            else
            {
                pendingPreview = settings;
                previewTimer ??= schedule((int)(PreviewIntervalMs - since), FlushPreview);
            }
        }
        if (sendNow) port.SendLayout("preview", settings);
    }

    private void FlushPreview()
    {
        string? pending;
        lock (gate)
        {
            previewTimer?.Dispose();
            previewTimer = null;
            pending = pendingPreview;
            pendingPreview = null;
            if (pending is not null) lastPreviewAt = clockMs();
        }
        if (pending is not null) port.SendLayout("preview", pending);
    }

    private void CancelPending()
    {
        previewTimer?.Dispose();
        previewTimer = null;
        pendingPreview = null;
    }

    /// <summary>Pencere kapandı: kaydedilmediyse TV'deki önizleme geri alınır.</summary>
    public void Closed()
    {
        bool wasSaved;
        lock (gate) { CancelPending(); wasSaved = saved; }
        if (!wasSaved) port.SendLayout("cancel", "{}");
    }
}
