// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// Metinler Mac uygulamasının Localizable.xcstrings dosyasından gelir; iki uygulama aynı çevirileri kullanır.
public class LocalizerTests
{
    [Fact]
    public void TurkishTextComesFromSharedCatalog()
    {
        var tr = Localizer.ForLanguage("tr");
        Assert.Equal("Çeviriyi başlat", tr["Start translating"]);
        Assert.Equal("Start translating", Localizer.ForLanguage("en")["Start translating"]);
    }

    [Fact]
    public void ProductNameIsLudoraEverywhere()
    {
        Assert.Equal("Ludora Hakkında", Localizer.ForLanguage("tr")["About GameTranslator"]);
        Assert.Equal("About Ludora", Localizer.ForLanguage("en")["About GameTranslator"]);
        Assert.Equal("Ludora'yı aç", Localizer.Brand("GameTranslator'ı aç", "tr"));
        Assert.Equal("Ludora'ya", Localizer.Brand("GameTranslator'a", "tr"));
        Assert.Equal("Ludora'da", Localizer.Brand("GameTranslator'da", "tr"));
        Assert.Equal("Ludora'dan", Localizer.Brand("GameTranslator'dan", "tr"));
        Assert.Equal("Ludora'nın", Localizer.Brand("GameTranslator'ın", "tr"));
        Assert.Equal("Ludora'yla", Localizer.Brand("GameTranslator'la", "tr"));
        // Dosya adları değişmez.
        Assert.Equal("GameTranslator-windows-x64.zip", Localizer.Brand("GameTranslator-windows-x64.zip", "tr"));
    }

    [Fact]
    public void UnknownLanguageFallsBackToEnglish() =>
        Assert.Equal("Start translating", Localizer.ForLanguage("de")["Start translating"]);

    [Fact]
    public void MissingKeyShowsTheKey() =>
        Assert.Equal("No such text", Localizer.ForLanguage("tr")["No such text"]);

    [Fact]
    public void PositionalFormatArgumentsAreFilled()
    {
        var tr = Localizer.ForLanguage("tr");
        Assert.Equal("Adım 2 / 6", tr.Format("Step %lld of %lld", 2, 6));
        Assert.Contains("İngilizce", tr.Format("The %@–%@ language pack is not downloaded yet.", "İngilizce", "Türkçe"));
    }

    [Fact]
    public void EveryEntryHasTurkish()
    {
        var missing = Localizer.MissingTranslations("tr");
        Assert.True(missing.Count == 0, string.Join(", ", missing));
    }
}

