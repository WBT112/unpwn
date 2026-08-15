using System.Globalization;
using Unpwn.App.Localization;
using Xunit;

namespace Unpwn.App.Tests.Localization;

public sealed class AccountTriageLocalizationTests
{
    [Fact]
    public void EnglishAndGermanExposeDistinctReviewStates()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));

        Assert.Equal("Needs review", localization.GetString("Accounts.Triage.NeedsReview"));
        Assert.Equal(
            "Automatically categorized",
            localization.GetString("Accounts.Triage.AutomaticallyCategorized"));
        Assert.Equal("Changed by you", localization.GetString("Accounts.Triage.ChangedByYou"));

        localization.SetLanguage("de");

        Assert.Equal("Prüfung erforderlich", localization.GetString("Accounts.Triage.NeedsReview"));
        Assert.Equal(
            "Automatisch kategorisiert",
            localization.GetString("Accounts.Triage.AutomaticallyCategorized"));
        Assert.Equal("Von dir geändert", localization.GetString("Accounts.Triage.ChangedByYou"));
    }

    [Fact]
    public void PseudoLocalizationCoversNewTriageGuidance()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        localization.SetLanguage(ResourceLocalizationService.PseudoLanguageCode);

        var text = localization.GetString("Accounts.Triage.OptionalHelp");

        Assert.StartsWith("⟦", text, StringComparison.Ordinal);
        Assert.EndsWith("···⟧", text, StringComparison.Ordinal);
        Assert.NotEqual(
            "Only accounts marked Needs review require a category decision. Automatic categories are already used for recovery order and can be changed at any time.",
            text);
    }
}
