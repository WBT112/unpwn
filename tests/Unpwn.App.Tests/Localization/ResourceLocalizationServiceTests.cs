using System.Globalization;
using Unpwn.App.Localization;
using Xunit;

namespace Unpwn.App.Tests.Localization;

public sealed class ResourceLocalizationServiceTests
{
    [Fact]
    public void UnsupportedSystemCultureFallsBackToEnglish()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Equal("en", localization.CurrentLanguageCode);
        Assert.Equal("Vault", localization.GetString("Shell.Navigation.Vault.Label"));
    }

    [Fact]
    public void ExactGermanCultureUsesGermanResourcesAndFormattingCulture()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var localization = new ResourceLocalizationService(culture);

        Assert.Equal("de", localization.CurrentLanguageCode);
        Assert.Equal(culture, localization.CurrentCulture);
        Assert.Equal("Tresor", localization.GetString("Shell.Navigation.Vault.Label"));
        Assert.Contains("1234,5", localization.Format("Import.Candidate.Row", 1234.5m, "Dienst", "Konto", string.Empty));
    }

    [Fact]
    public void MissingKeyUsesVisibleMarker()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("⟦Missing.Resource.Key⟧", localization.GetString("Missing.Resource.Key"));
    }

    [Fact]
    public void ShippedGermanResourcesHaveKeyParityWithEnglishSource()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Equal(
            localization.GetResourceKeys("en"),
            localization.GetResourceKeys("de"));
    }

    [Fact]
    public void PseudoLocalizationExpandsTextAndPreservesPlaceholders()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        localization.SetLanguage(ResourceLocalizationService.PseudoLanguageCode);

        var pseudo = localization.GetString("Import.Candidate.Row");

        Assert.StartsWith("⟦", pseudo, StringComparison.Ordinal);
        Assert.EndsWith("···⟧", pseudo, StringComparison.Ordinal);
        Assert.Contains("{0}", pseudo, StringComparison.Ordinal);
        Assert.Contains("{3}", pseudo, StringComparison.Ordinal);
        Assert.NotEqual("Row {0}: {1} — {2}{3}", pseudo);
    }

    [Theory]
    [InlineData(0, "No valid accounts")]
    [InlineData(1, "1 valid account")]
    [InlineData(2, "2 valid accounts")]
    public void PluralFormattingSelectsExplicitVariant(int count, string expectedPrefix)
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));

        var message = localization.FormatPlural(
            "Import.Preview.ValidAccounts",
            count,
            count,
            0);

        Assert.StartsWith(expectedPrefix, message, StringComparison.Ordinal);
    }

    [Fact]
    public void CultureChangeRaisesOneNotification()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var notifications = 0;
        localization.CultureChanged += (_, _) => notifications++;

        localization.SetLanguage("de");
        localization.SetLanguage("de");

        Assert.Equal(1, notifications);
    }
}
