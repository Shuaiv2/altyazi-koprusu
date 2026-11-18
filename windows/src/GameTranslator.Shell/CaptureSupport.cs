// SPDX-License-Identifier: AGPL-3.0-only

namespace GameTranslator.Shell;

/// <summary>Yakalanabilecek bir Chiaki / Remote Play penceresi (istemci alanı piksel olarak).</summary>
public sealed record CaptureCandidate(nint Handle, string Title, int Width, int Height);

public static class CaptureWindowPicker
{
    public const int MinWidth = 250;
    public const int MinHeight = 150;

    /// <summary>
    /// Görünür pencerelerden en büyüğü seçilir (ilk eşleşme değil): Chiaki'nin küçük yan pencereleri (ayarlar, günlük)
    /// ana akış penceresinden önce sıralanabiliyor. Eşit alanda başlığı "Stream/PlayStation/PS5" geçen kazanır.
    /// </summary>
    public static CaptureCandidate? Best(IEnumerable<CaptureCandidate> candidates) =>
        candidates.Where(c => c.Width >= MinWidth && c.Height >= MinHeight)
            .OrderByDescending(c => (long)c.Width * c.Height)
            .ThenByDescending(c => TitleScore(c.Title))
            .FirstOrDefault();

    private static int TitleScore(string title) =>
        (title.Contains("Stream", StringComparison.OrdinalIgnoreCase) ? 50 : 0) +
        (title.Contains("PlayStation", StringComparison.OrdinalIgnoreCase) ? 30 : 0) +
        (title.Contains("PS5", StringComparison.OrdinalIgnoreCase) ? 30 : 0);
}

/// <summary>
/// Chiaki süreci çalışırken kapanırsa yeniden başlatma kuralı: 5 sn bekle, en çok 5 deneme; bağlantı 30 sn kararlı
/// kaldıysa sayaç sıfırlanır.
/// </summary>
public sealed class StreamReconnectPolicy(Func<long> clockMs)
{
    public const int MaxAttempts = 5;
    public const int DelayMs = 5000;
    public const int StableResetMs = 30_000;

    public readonly record struct Decision(bool Retry, int Attempt, string Message);

    private int attempts;
    private long? streamingSince;

    public int Attempts => attempts;

    /// <summary>Kareler akmaya başladı (ilk kareden sonra bir kez çağrılması yeter).</summary>
    public void MarkStreaming() => streamingSince ??= clockMs();

    /// <summary>Chiaki beklenmedik biçimde kapandı.</summary>
    public Decision OnExited()
    {
        if (streamingSince is { } since && clockMs() - since >= StableResetMs) attempts = 0;
        streamingSince = null;
        attempts++;
        if (attempts > MaxAttempts)
            return new Decision(false, MaxAttempts, "PS5 bağlantısı koptu; yeniden deneme sınırına ulaşıldı. Çeviriyi durdurup yeniden başlatın.");
        return new Decision(true, attempts, $"PS5 bağlantısı koptu, yeniden deneniyor ({attempts}/{MaxAttempts})…");
    }
}
