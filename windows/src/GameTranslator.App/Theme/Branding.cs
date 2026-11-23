// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace GameTranslator.App.Theme;

/// <summary>
/// Kullanıcıya görünen ürün adı: Ludora. Ortak metin kataloğundan (Core) gelen eski adlar ("Altyazı Köprüsü",
/// "GameTranslator") ekranda Ludora olarak gösterilir; Türkçe ekler ünlü uyumuna göre düzeltilir.
/// </summary>
internal static class Branding
{
    public const string Name = "Ludora";

    private static readonly Regex Old = new(
        @"(?:Altyazı Köprüsü|GameTranslator)('[\p{L}]+)?(?![\w.\-])", RegexOptions.Compiled);

    public static string Fix(string value)
    {
        if (string.IsNullOrEmpty(value) || (!value.Contains("Köprüsü", StringComparison.Ordinal) && !value.Contains("GameTranslator", StringComparison.Ordinal)))
            return value;
        return Old.Replace(value, match =>
        {
            var suffix = match.Groups[1].Value;
            return Name + suffix switch
            {
                "'nü" or "'ı" or "'i" => "'yı",
                "'ne" or "'a" or "'e" => "'ya",
                "'nde" or "'da" or "'de" => "'da",
                "'nden" or "'dan" or "'den" => "'dan",
                "'nün" or "'nın" or "'nin" or "'ın" or "'in" => "'nın",
                "'yle" or "'la" or "'le" => "'yla",
                _ => suffix,
            };
        });
    }
}

/// <summary>Localizer.Current gibi okunur, ancak sonuç Ludora adıyla düzeltilir.</summary>
internal sealed class BrandedText
{
    public static BrandedText Current { get; } = new();
    public string this[string key] => Branding.Fix(GameTranslator.Core.Localizer.Current[key]);
    public string Format(string key, params object[] args) => Branding.Fix(GameTranslator.Core.Localizer.Current.Format(key, args));
}
