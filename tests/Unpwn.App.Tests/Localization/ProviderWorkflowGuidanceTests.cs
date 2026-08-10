using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.App.Tests.Localization;

public sealed class ProviderWorkflowGuidanceTests
{
    [Fact]
    public void EveryRepositoryWorkflowGuidanceKeyExistsInEnglishAndGerman()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var english = localization.GetResourceKeys("en").ToHashSet(StringComparer.Ordinal);
        var german = localization.GetResourceKeys("de").ToHashSet(StringComparer.Ordinal);
        var required = RepositoryWorkflowCatalog.Workflows
            .SelectMany(workflow => workflow.Actions)
            .SelectMany(action => EnumerateKeys(action.Guidance))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(required);
        Assert.Empty(required.Except(english, StringComparer.Ordinal));
        Assert.Empty(required.Except(german, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("github.com", "change-password")]
    [InlineData("google.com", "change-password")]
    [InlineData("microsoft.com", "change-password")]
    public void RuntimeLanguageSwitchChangesGuidanceWithoutChangingWorkflowSemantics(
        string providerId,
        string actionId)
    {
        var workflow = RepositoryWorkflowCatalog.Workflows.Single(candidate =>
            candidate.ProviderId == providerId);
        var action = workflow.Actions.Single(candidate => candidate.Id == actionId);
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
        var english = localization.GetString(action.Guidance.InstructionKey);
        var semanticSnapshot = (
            workflow.WorkflowId,
            workflow.WorkflowVersion,
            action.Id,
            action.Type,
            action.RecoveryPaths.ToArray(),
            action.Prerequisites.ToArray(),
            action.CompletionCriteria.ToArray());

        localization.SetLanguage("de");
        var german = localization.GetString(action.Guidance.InstructionKey);

        Assert.NotEqual(english, german);
        Assert.DoesNotContain("⟦", english, StringComparison.Ordinal);
        Assert.DoesNotContain("⟦", german, StringComparison.Ordinal);
        Assert.Equal(semanticSnapshot.WorkflowId, workflow.WorkflowId);
        Assert.Equal(semanticSnapshot.WorkflowVersion, workflow.WorkflowVersion);
        Assert.Equal(semanticSnapshot.Id, action.Id);
        Assert.Equal(semanticSnapshot.Type, action.Type);
        Assert.Equal(semanticSnapshot.Item5, action.RecoveryPaths);
        Assert.Equal(semanticSnapshot.Item6, action.Prerequisites);
        Assert.Equal(semanticSnapshot.Item7, action.CompletionCriteria);
    }

    private static IEnumerable<string> EnumerateKeys(
        global::Unpwn.Core.RecoveryActionGuidanceKeys guidance)
    {
        yield return guidance.TitleKey;
        yield return guidance.InstructionKey;
        if (guidance.WarningKey is not null)
        {
            yield return guidance.WarningKey;
        }

        yield return guidance.CompletionKey;
        foreach (var criterion in guidance.CompletionCriteriaKeys)
        {
            yield return criterion;
        }
    }
}
