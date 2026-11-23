// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace GameTranslator.Core;

/// <summary>
/// Oyunların birkaç ekrana böldüğü cümleleri ("...very" / "fun.") çeviri öncesi birleştirir.
/// Saf mantık: zamanı dışarıdan alır, test edilebilir. Mac: mac/SentenceJoiner.swift (kurallar birebir aynı).
/// </summary>
public sealed partial class SentenceJoiner
{
    /// <param name="Source">Çevrilecek metin (önceki açık parçalar + bu satır, birleştirilmiş).</param>
    /// <param name="IsContinuation">Önceki bir parçanın devamı mı?</param>
    /// <param name="Hold">Tek başına dolgu sözcüğü ("well...", "uh..."): sonraki parçayla çevrilmek üzere bekletilmeli.</param>
    public readonly record struct Result(string Source, bool IsContinuation, bool Hold);

    public const double MaxGap = 6;
    public const int MaxFragments = 3;
    public const int MaxChars = 220;

    private readonly List<string> fragments = [];
    private double lastTime = double.NegativeInfinity;

    public void Reset() { fragments.Clear(); lastTime = double.NegativeInfinity; }

    /// <param name="time">Saniye (tek yönlü artan herhangi bir saat).</param>
    public Result Feed(string raw, double time)
    {
        var line = raw.Trim();
        if (fragments.Count > 0 && time - lastTime <= MaxGap)
        {
            var acc = Norm(Join(fragments));
            var n = Norm(line);
            // Yeni okuma zaten birikmiş cümlenin içinde: yeni bir şey yok, tekrar eklenmez.
            if (Significant(n) && Contained(n, acc))
            {
                lastTime = time;
                var src = fragments.Count > 1 ? Join(fragments) : StripDash(fragments[0]);
                return new Result(src, fragments.Count > 1, false);
            }
            // Yeni okuma bir öncekinin üst kümesi (aynı satırın daha eksiksiz okunuşu): eklemek yerine değiştir.
            var last = Norm(fragments[^1]);
            if (last.Length > 0 && n.Length > last.Length
                && (Contained(last, n) || TextMatch.IsSimilar(n[..last.Length], last, 0.85)))
            {
                fragments[^1] = line;
                lastTime = time;
                var src = fragments.Count > 1 ? Join(fragments) : StripDash(line);
                return new Result(src, fragments.Count > 1, false);
            }
        }
        var continues = fragments.Count > 0
            && time - lastTime <= MaxGap
            && IsOpen(fragments[^1])
            && Continues(line, fragments[^1]);
        if (!continues) fragments.Clear();
        fragments.Add(line);
        lastTime = time;
        while (fragments.Count > MaxFragments || (fragments.Count > 1 && Join(fragments).Length > MaxChars))
            fragments.RemoveAt(0);
        var isCont = fragments.Count > 1;
        var source = isCont ? Join(fragments) : StripDash(line);
        return new Result(source, isCont, !isCont && IsFiller(line));
    }

    // --- Kurallar ---

    internal static bool IsOpen(string s)
    {
        var t = s.Trim(' ', '\t');
        if (t.EndsWith("...", StringComparison.Ordinal)) return true;
        if (t.Length == 0) return false;
        var last = t[^1];
        if (",;:—-…".Contains(last)) return true;
        return !".!?\"”’)".Contains(last);
    }

    private static bool EndsEllipsisOrComma(string s)
    {
        var t = s.Trim(' ', '\t');
        return t.EndsWith("...", StringComparison.Ordinal) || t.EndsWith('…') || t.EndsWith(',');
    }

    private static bool Continues(string line, string prev)
    {
        if (line.Length == 0) return false;
        if (line.StartsWith("- ", StringComparison.Ordinal) || (line.StartsWith('-') && line.Length > 1 && char.IsLetter(line[1])))
            return false;
        if (SpeakerPrefix().IsMatch(line)) return false;
        var rest = line.TrimStart('.', '…', ' ');
        var s = rest.Length > 0 ? rest[0] : line[0];
        return char.IsLower(s) || EndsEllipsisOrComma(prev);
    }

    [GeneratedRegex(@"^[A-ZÇĞİÖŞÜ][A-Za-zÇĞİÖŞÜçğıöşü0-9 ]{0,20}:", RegexOptions.CultureInvariant)]
    private static partial Regex SpeakerPrefix();

    private static readonly HashSet<string> Fillers =
        ["well", "uh", "um", "uhm", "er", "erm", "so", "hmm", "ah", "oh", "okay", "ok", "like"];

    internal static bool IsFiller(string s) =>
        Fillers.Contains(StripDash(s).ToLowerInvariant().Trim('.', '…', ',', '!', '?', ' '));

    internal static string StripDash(string s) => s.StartsWith("- ", StringComparison.Ordinal) ? s[2..] : s;

    /// <summary>Tekrar denetimine girecek kadar uzun mu (≥3 sözcük): kısa parçalar ('well', 'oh') başka yerde geçse de tekrar sayılmaz.</summary>
    internal static bool Significant(string n) => n.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3;

    internal static string Norm(string s) => TextMatch.Normalize(s);

    /// <summary><paramref name="a"/> (sadeleştirilmiş), <paramref name="b"/> içinde geçiyor mu: tam geçiş ya da sözcük penceresinde ≥0.85 benzerlik.</summary>
    internal static bool Contained(string a, string b)
    {
        if (a.Length == 0) return true;
        if (b.Contains(a, StringComparison.Ordinal)) return true;
        var aw = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bw = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (aw.Length < 2 || bw.Length < aw.Length) return false;
        for (var start = 0; start <= bw.Length - aw.Length; start++)
        {
            var win = string.Join(' ', bw.Skip(start).Take(aw.Length));
            if (TextMatch.IsSimilar(win, a, 0.85)) return true;
        }
        return false;
    }

    internal static string Join(IReadOnlyList<string> parts)
    {
        var o = StripDash(parts[0]).Trim(' ', '\t');
        foreach (var p in parts.Skip(1))
        {
            var next = p.Trim(' ', '\t');
            // Zaten birikmiş cümlede geçen parça tekrar eklenmez.
            if (Significant(Norm(next)) && Contained(Norm(next), Norm(o))) continue;
            next = next.TrimStart('.', '…').Trim(' ', '\t');
            // Ekranda önceki satırın son sözcükleri yeni parçanın başında yeniden okunduysa (≥2 sözcük) çakışma atılır.
            var outTok = o.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Norm).Where(t => t.Length > 0).ToList();
            var nextTok = next.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var nn = nextTok.Select(Norm).ToList();
            var k = Math.Min(outTok.Count, nn.Count);
            while (k >= 2)
            {
                if (outTok.Skip(outTok.Count - k).SequenceEqual(nn.Take(k)))
                {
                    nextTok.RemoveRange(0, k);
                    next = string.Join(' ', nextTok);
                    break;
                }
                k--;
            }
            if (next.Length == 0) continue;
            var sep = " ";
            if (o.EndsWith("...", StringComparison.Ordinal) || o.EndsWith('…'))
            {
                o = o[..^(o.EndsWith('…') ? 1 : 3)].Trim(' ', '\t');
                if (char.IsUpper(next[0])) sep = ", ";
            }
            o += sep + next;
        }
        // Cümle ortasındaki "uh/um" dolgularını at.
        var cleaned = Fill().Replace(o, "");
        return cleaned.Length == 0 ? o : cleaned;
    }

    [GeneratedRegex(@"\s+\b(uh|um|uhm|erm)\b[.,…]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Fill();
}
