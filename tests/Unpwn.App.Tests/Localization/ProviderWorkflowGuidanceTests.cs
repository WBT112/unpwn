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

    [Fact]
    public void RuntimeLanguageSwitchChangesGuidanceWithoutChangingWorkflowSemantics()
    {
        var workflow = Assert.Single(RepositoryWorkflowCatalog.Workflows);
        var action = workflow.Actions.Single(candidate => candidate.Id == "change-password");
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
