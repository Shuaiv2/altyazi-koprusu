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

    internal static string Join(IReadOnlyList<string> parts)
    {
        var o = StripDash(parts[0]).Trim(' ', '\t');
        foreach (var p in parts.Skip(1))
        {
            var next = p.Trim(' ', '\t').TrimStart('.', '…').Trim(' ', '\t');
            var sep = " ";
            if (o.EndsWith("...", StringComparison.Ordinal) || o.EndsWith('…'))
            {
                o = o[..^(o.EndsWith('…') ? 1 : 3)].Trim(' ', '\t');
                if (next.Length > 0 && char.IsUpper(next[0])) sep = ", ";
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
