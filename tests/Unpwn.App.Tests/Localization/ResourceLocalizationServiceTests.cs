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
    public void EnglishSystemCultureUsesEnglishResourcesAndFormattingCulture()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        var localization = new ResourceLocalizationService(culture);

        Assert.Equal("en", localization.CurrentLanguageCode);
        Assert.Equal(culture, localization.CurrentCulture);
        Assert.Equal("Settings and support", localization.GetString("Settings.Title"));
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
    public void StartupUxUsesStartOrResumeAndOneNegativeDeviceChoice()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Begin or resume recovery", localization.GetString("Vault.Welcome.Begin"));
        Assert.Equal("No, or I am not sure", localization.GetString("Vault.Trusted.No"));
        Assert.Equal(
            "Create or unlock a local recovery vault.",
            localization.GetString("Screen.Vault.Description"));
        Assert.Equal(
            "The vault starts locked. Review the recovery overview after unlocking.",
            localization.GetString("Shell.Recovery.Message"));
        Assert.DoesNotContain("Vault.Trusted.Unsure", localization.GetResourceKeys("en"));

        localization.SetLanguage("de");

        Assert.Equal(
            "Wiederherstellung beginnen oder fortsetzen",
            localization.GetString("Vault.Welcome.Begin"));
        Assert.Equal("Nein oder ich bin mir nicht sicher", localization.GetString("Vault.Trusted.No"));
        Assert.Equal(
            "Erstelle oder entsperre einen lokalen Wiederherstellungstresor.",
            localization.GetString("Screen.Vault.Description"));
        Assert.Equal(
            "Der Tresor startet gesperrt. Prüfe nach dem Entsperren die Wiederherstellungsübersicht.",
            localization.GetString("Shell.Recovery.Message"));
        Assert.DoesNotContain("Vault.Trusted.Unsure", localization.GetResourceKeys("de"));
    }

    [Fact]
    public void StartupSafetyGuidanceIsConcreteAndPseudoLocalizable()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var guidance = localization.GetString("Vault.Guidance.Description");

        Assert.Contains("Do not enter account or vault credentials", guidance, StringComparison.Ordinal);
        Assert.Contains("official Windows installation media", guidance, StringComparison.Ordinal);
        Assert.Contains("Linux distribution's official source", guidance, StringComparison.Ordinal);
        Assert.Contains("not proof", guidance, StringComparison.OrdinalIgnoreCase);

        localization.SetLanguage(ResourceLocalizationService.PseudoLanguageCode);
        var pseudo = localization.GetString("Vault.Guidance.Description");

        Assert.StartsWith("⟦", pseudo, StringComparison.Ordinal);
        Assert.EndsWith("···⟧", pseudo, StringComparison.Ordinal);
        Assert.NotEqual(guidance, pseudo);
    }

    [Fact]
    public void PseudoSystemCultureDoesNotSelectPseudoLocalization()
    {
        var localization = new ResourceLocalizationService(
            CultureInfo.GetCultureInfo(ResourceLocalizationService.PseudoLanguageCode));

        Assert.Equal(ResourceLocalizationService.DefaultLanguageCode, localization.CurrentLanguageCode);
        Assert.Equal("Settings and support", localization.GetString("Settings.Title"));
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
