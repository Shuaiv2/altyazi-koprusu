// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Shell;

/// <summary>Ekran metni geçişinin dış dünyayla bağlantıları (uygulama TV sunucusunu ve bilgisayar katmanını bağlar).</summary>
public sealed class ScreenPassHooks
{
    /// <summary>Tam kareyi okuyan okuyucu (altyazı okuyucusundan ayrı örnek); yoksa geçiş yapılmaz.</summary>
    public required Func<ITextReader?> Reader { get; init; }
    /// <summary>Seçili çevirmen (yeniden kurulduğunda <see cref="ScreenPass.Invalidate"/> çağrılır).</summary>
    public required Func<ITextTranslator> Translator { get; init; }
    /// <summary>Bu türün çevirisi açık mı (Görev hedefi, Bilgi kutuları, Telefon/bildirimler, Harita/menü açıklamaları).</summary>
    public required Func<ScreenKind, bool> Enabled { get; init; }
    /// <summary>Bölgenin gösterilen bütün öğeleri değişti (TV <c>screen</c> olayı ve bilgisayar katmanı).</summary>
    public required Action<string, IReadOnlyList<ScreenItem>, int> Send { get; init; }
    /// <summary>Bölge boşaldı (TV <c>screen-clear</c> olayı ve bilgisayar katmanı).</summary>
    public required Action<string> Clear { get; init; }
    /// <summary>Altyazı hattı susturuldu (true: menü/harita/telefon) ya da yeniden açıldı; ilk durumda bir kez altyazı temizlenir.</summary>
    public required Action<bool> SubtitleSuppressed { get; init; }
    public Func<double>? Seconds { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Ekran metni geçişi (Mac: NativeApp.runScreenPass): karenin tamamı okunur, <see cref="ScreenText.Analyze"/> yazıları
/// görev hedefi / bilgi kutusu / bildirim / açıklama / telefon konuşmasına ayırır, <see cref="ScreenTracker"/> kararlılığı
/// ölçer, kararlı blok çevrilip bölge adıyla gönderilir. Aynı okuma altyazı hattına da yardım eder: telefon ve bildirim
/// kutularının içindeki satırlar altyazıdan elenir (<see cref="DialogueGate"/>), kırpma okuması tam kare okumasıyla
/// çapraz doğrulanır ve ekran menü/harita/telefondayken altyazı hattı susar.
/// </summary>
public sealed class ScreenPass(ScreenPassHooks hooks)
{
    /// <summary>İki geçiş arası en az süre (Mac: her 3. tik).</summary>
    public const int MinIntervalMs = 800;
    /// <summary>Çapraz doğrulamada kullanılan tam kare okumasının en çok yaşı (sn).</summary>
    public const double CrossCheckFresh = 1.5;
    /// <summary>Dışlanacak alanların en çok yaşı (sn).</summary>
    public const double ExclusionFresh = 3.0;
    /// <summary>TV'de kalma süresi (ms); kutu ekrandan kalkınca izleyici temizler.</summary>
    public const int DisplayMs = 15000;

    private readonly object gate = new();
    private readonly ScreenTracker tracker = new();
    private readonly Func<double> seconds = hooks.Seconds ?? (() => Environment.TickCount64 / 1000.0);
    private int busy;
    private long lastStart = long.MinValue / 2;
    private int seq;
    private ITextTranslator? translator;
    private IReadOnlyList<ScreenLine> lastLines = [];
    private IReadOnlyList<ScreenBox> lastExclusions = [];
    private double lastAt = -1000;
    private ScreenBox? lastPhoneBox;
    private bool suppressed;
    private readonly Dictionary<string, ScreenItem> items = [];
    private readonly Dictionary<string, ScreenKind> kinds = [];
    private readonly Dictionary<string, int> generations = [];

    /// <summary>Ekran menü/harita/telefondayken altyazı hattı okunmaz.</summary>
    public bool SubtitlesSuppressed { get { lock (gate) return suppressed; } }

    public ScreenMode CurrentMode { get { lock (gate) return tracker.CurrentMode; } }

    private bool AnyEnabled => Enum.GetValues<ScreenKind>().Any(hooks.Enabled);

    /// <summary>Çevirmen yeniden kuruldu (dil, motor değişti): sonraki çeviri yenisini alır.</summary>
    public void Invalidate() { lock (gate) translator = null; }

    /// <summary>Durdurulunca ya da kaynak değişince: izleyici sıfırlanır, bölgeler temizlenir, susturma kalkar.</summary>
    public void Reset()
    {
        bool wasSuppressed;
        lock (gate)
        {
            tracker.Reset();
            seq++;
            items.Clear(); kinds.Clear(); generations.Clear();
            lastLines = []; lastExclusions = []; lastAt = -1000; lastPhoneBox = null;
            wasSuppressed = suppressed;
            suppressed = false;
        }
        foreach (var region in new[] { "objective", "info", "notify", "description", "phone" }) hooks.Clear(region);
        if (wasSuppressed) hooks.SubtitleSuppressed(false);
    }

    /// <summary>Kare geldi: uygunsa arka planda ekran geçişi başlatır (çağıranı bekletmez).</summary>
    public void Feed(Frame frame)
    {
        if (!AnyEnabled) { ClearIfAllOff(); return; }
        var at = Environment.TickCount64;
        if (at - Volatile.Read(ref lastStart) < MinIntervalMs || Interlocked.Exchange(ref busy, 1) == 1) return;
        lastStart = at;
        int mine;
        lock (gate) mine = seq;
        _ = Task.Run(() =>
        {
            try { Run(frame, mine); }
            catch (Exception error) { hooks.Log?.Invoke($"ekran geçişi başarısız: {error.GetType().Name}: {error.Message}"); }
            finally { Volatile.Write(ref busy, 0); }
        });
    }

    /// <summary>Dört anahtar da kapandıysa bir kez her şey temizlenir ve altyazı hattı serbest kalır.</summary>
    private void ClearIfAllOff()
    {
        bool needed;
        lock (gate) needed = items.Count > 0 || suppressed || tracker.CurrentMode != ScreenMode.Game;
        if (needed) Reset();
    }

    /// <summary>TV bölge adı (Mac: kind.rawValue).</summary>
    private static string WireRegion(ScreenKind kind) => kind switch
    {
        ScreenKind.Objective => "objective",
        ScreenKind.Info => "info",
        ScreenKind.Notify => "notify",
        ScreenKind.Description => "description",
        _ => "phone",
    };

    internal void Run(Frame frame, int mySeq)
    {
        var reader = hooks.Reader();
        if (reader is null) return;
        var started = Environment.TickCount64;
        var reading = reader.ReadLines(frame, deep: false);
        var lines = reading.Lines.Where(line => line.Box is not null && line.Text.Trim().Length > 0)
            .Select(line =>
            {
                var b = line.Box!.Value;
                return new ScreenLine(line.Text.Trim(), ScreenBox.FromEdges(b.Left, b.Top, b.Right, b.Bottom), (float)(line.Confidence ?? 1));
            }).ToList();
        var ms = Environment.TickCount64 - started;
        var result = ScreenText.Analyze(lines, BubbleColor.ForFrame(frame.Pixels, frame.Stride, frame.Width, frame.Height));
        // Kart/kutu bölgesinin açık ya da koyu zemini (TV'deki Türkçe kartın temasını seçer).
        var themes = new Dictionary<string, string>();
        foreach (var block in result.Blocks) themes[block.Slot] = ScreenTheme.Of(frame, block.Box);
        Apply(lines, result, themes, ms, mySeq);
    }

    internal void Apply(IReadOnlyList<ScreenLine> lines, ScreenAnalysis result, IReadOnlyDictionary<string, string> themes, long ms, int mySeq)
    {
        var actions = new List<(ScreenAction Action, int Generation)>();
        bool? suppressChange = null;
        lock (gate)
        {
            if (mySeq != seq) return;
            // Altyazı hattı için: çapraz doğrulama satırları ve dışlanacak alanlar (telefon, bildirim kartları).
            lastLines = lines;
            lastAt = seconds();
            var exclusions = result.Blocks.Where(b => b.Kind is ScreenKind.Notify or ScreenKind.Phone).Select(b => b.Box.Inset(-0.01, -0.01)).ToList();
            if (result.Phone is { } phone) { exclusions.Add(phone.Box); lastPhoneBox = phone.Box; }
            lastExclusions = exclusions;
            hooks.Log?.Invoke($"ekran: OCR {ms} ms, {lines.Count} satır, mod={result.Mode}");
            foreach (var block in result.Blocks)
                hooks.Log?.Invoke($"ekran: blok [{block.Kind}] {(block.Speaker is { } s ? $"({s}) " : "")}{(block.Title is { } t ? $"\"{t}\" " : "")}\"{block.Text}\"");
            var blocks = result.Blocks.Where(b => hooks.Enabled(b.Kind)).ToList();
            var tracked = tracker.Feed(blocks, result.Mode, seconds());
            var suppress = tracker.CurrentMode != ScreenMode.Game;
            if (suppress != suppressed)
            {
                suppressed = suppress;
                suppressChange = suppress;
            }
            foreach (var action in tracked)
            {
                var slot = action is ScreenAction.Clear c ? c.Slot : ((ScreenAction.Translate)action).Block.Slot;
                generations[slot] = generations.GetValueOrDefault(slot) + 1;
                actions.Add((action, generations[slot]));
            }
        }
        if (suppressChange is { } now)
        {
            hooks.Log?.Invoke(now ? $"ekran: mod {CurrentMode} -> altyazı hattı susturuldu" : "ekran: altyazı hattı yeniden açıldı");
            hooks.SubtitleSuppressed(now);
        }
        foreach (var (action, generation) in actions)
        {
            if (action is ScreenAction.Clear clear)
            {
                hooks.Log?.Invoke($"ekran: izleyici temizle [{clear.Slot}]");
                lock (gate) { items.Remove(clear.Slot); kinds.Remove(clear.Slot); }
                Publish(clear.Kind);
            }
            else if (action is ScreenAction.Translate translate)
            {
                hooks.Log?.Invoke($"ekran: izleyici çevir [{translate.Block.Slot}] \"{translate.Block.Text}\"");
                var theme = themes.GetValueOrDefault(translate.Block.Slot, "dark");
                _ = Task.Run(() => TranslateBlock(translate.Block, generation, theme, mySeq));
            }
        }
    }

    private ITextTranslator Translator()
    {
        lock (gate) return translator ??= hooks.Translator();
    }

    /// <summary>Çeviri yok (çevirmen hazır değil/başarısız): null; blok yeniden denensin diye izleyici unutturulur.</summary>
    private async Task<string?> TranslateText(string text)
    {
        try
        {
            var translated = (await Translator().TranslateAsync(GameEnglish.Plain(text)).ConfigureAwait(false)).Trim();
            return translated.Length == 0 ? null : translated;
        }
        catch (Exception error)
        {
            hooks.Log?.Invoke($"ekran: çeviri yapılamadı ({error.GetType().Name})");
            return null;
        }
    }

    private bool Current(string slot, int generation, int mySeq)
    {
        lock (gate) return mySeq == seq && generations.GetValueOrDefault(slot) == generation;
    }

    private void Forget(string slot) { lock (gate) tracker.ForgetShown(slot); }

    internal async Task TranslateBlock(ScreenBlock block, int generation, string theme, int mySeq)
    {
        if (block.Messages.Count > 0) { await TranslatePhone(block, generation, theme, mySeq).ConfigureAwait(false); return; }
        // Gövde "\n" ile ayrılmış parçalardan oluşabilir (başlık/gövde); her parça ayrı çevrilir.
        var parts = new List<string>();
        foreach (var segment in block.Text.Split('\n').Where(s => s.Length > 0))
        {
            if (await TranslateText(segment).ConfigureAwait(false) is not { } translated) { Forget(block.Slot); return; }
            parts.Add(translated);
        }
        if (parts.Count == 0) { Forget(block.Slot); return; }
        var turkish = string.Join('\n', parts);
        var source = block.Text;
        if (block.Title is { } title)
        {
            var trTitle = (await TranslateText(title).ConfigureAwait(false) ?? title).ToUpper(new System.Globalization.CultureInfo("tr"));
            turkish = $"{trTitle}: {turkish}";
            source = $"{title}: {block.Text}";
        }
        if (!Current(block.Slot, generation, mySeq)) return;
        lock (gate)
        {
            items[block.Slot] = ScreenItem.Over(block, turkish, source, theme);
            kinds[block.Slot] = block.Kind;
        }
        Publish(block.Kind);
    }

    /// <summary>Açık telefon konuşması: her balon ayrı çevrilir; tek "beside" sohbet paneli telefonun yanına gider.</summary>
    private async Task TranslatePhone(ScreenBlock block, int generation, string theme, int mySeq)
    {
        var messages = new List<ScreenMessage>();
        foreach (var message in block.Messages)
        {
            if (await TranslateText(message.Text).ConfigureAwait(false) is not { } translated) { Forget(block.Slot); return; }
            messages.Add(new ScreenMessage(message.FromMe, translated, message.Text));
        }
        if (!Current(block.Slot, generation, mySeq)) return;
        lock (gate)
        {
            var phone = lastPhoneBox ?? block.Box;
            items[block.Slot] = new ScreenItem("", block.Text, ScreenItem.ToTextBox(phone), block.LineHeight, "beside", theme, block.Speaker, messages);
            kinds[block.Slot] = block.Kind;
        }
        Publish(block.Kind);
    }

    /// <summary>Bir türün şu an görünen bütün yuvalarını gönderir; hiç yoksa o türü temizler.</summary>
    private void Publish(ScreenKind kind)
    {
        List<ScreenItem> shown;
        lock (gate)
            shown = kinds.Where(pair => pair.Value == kind).Select(pair => pair.Key).Order(StringComparer.Ordinal)
                .Select(slot => items[slot]).ToList();
        if (shown.Count == 0) hooks.Clear(WireRegion(kind));
        else hooks.Send(WireRegion(kind), shown, DisplayMs);
    }

    // --- Altyazı hattına yardım ---

    /// <summary>
    /// Altyazı kırpmasındaki satırlara diyalog denetimi: kesik önizleme, telefon/bildirim içindeki, ortalanmamış satırlar
    /// elenir. <paramref name="crossCheck"/> açıksa ve yeni tam kare okuması varsa, alt bantta kırpma metnine benzeyen
    /// satır kümesi (daha iyi okunmuş olabilir) onun yerine geçer; kutusu da tam karedeki yerinden alınır.
    /// </summary>
    /// <param name="area">Kırpmanın karedeki yeri; satır kutuları kırpmaya göredir.</param>
    public Reading GateSubtitle(Reading read, TextBox area, bool crossCheck)
    {
        IReadOnlyList<ScreenLine> screen;
        IReadOnlyList<ScreenBox> exclude;
        lock (gate)
        {
            var age = seconds() - lastAt;
            screen = age <= CrossCheckFresh ? lastLines : [];
            exclude = age <= ExclusionFresh ? lastExclusions : [];
        }
        var lines = read.Lines;
        var boxed = new List<(int Index, ScreenLine Line)>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Box is not { } box) continue;
            var framed = box.PlacedIn(area);
            boxed.Add((i, new ScreenLine(lines[i].Text, ScreenBox.FromEdges(framed.Left, framed.Top, framed.Right, framed.Bottom), (float)(lines[i].Confidence ?? 1))));
        }
        var verdict = DialogueGate.Evaluate(boxed.Select(b => b.Line).ToList(), exclude);
        var dropped = verdict.Dropped.Select(d => boxed[d.Index].Index).ToHashSet();
        foreach (var d in verdict.Dropped) hooks.Log?.Invoke($"elendi [{d.Reason}] {lines[boxed[d.Index].Index].Text}");
        var kept = lines.Where((_, i) => !dropped.Contains(i)).ToList();
        var result = dropped.Count == 0 ? read : new Reading(kept);
        if (!crossCheck || screen.Count == 0 || result.Lines.Count == 0) return result;
        var cropText = string.Join(' ', result.Lines.Select(l => l.Text)).Trim();
        if (DialogueGate.CrossCheck(cropText, screen, exclude) is not { } hit) return result;
        if (TextMatch.Normalize(hit.Text) == TextMatch.Normalize(cropText)) return result;
        hooks.Log?.Invoke($"tam kare okuması kullanıldı: \"{hit.Text}\" (kırpma: \"{cropText}\")");
        double aw = Math.Max(1e-9, area.Right - area.Left), ah = Math.Max(1e-9, area.Bottom - area.Top);
        var rel = new TextBox((hit.Rect.MinX - area.Left) / aw, (hit.Rect.MinY - area.Top) / ah, (hit.Rect.MaxX - area.Left) / aw, (hit.Rect.MaxY - area.Top) / ah);
        return new Reading([new TextLine(hit.Text, rel)]);
    }
}
