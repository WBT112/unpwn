using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Unpwn.App.Tests.Views;
using Unpwn.App.Views;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class VaultEntryAccessibilityTests
{
    [Fact]
    public async Task VaultEntryExposesPrimarySecondaryAndTrustAssessmentActions()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(() =>
        {
            var view = new VaultEntryView();

            Assert.NotNull(FindByAutomationId(view, "vault-primary-action"));
            Assert.NotNull(FindByAutomationId(view, "vault-open-another"));
            Assert.NotNull(FindByAutomationId(view, "vault-create-new"));
            Assert.NotNull(FindByAutomationId(view, "vault-back-trusted-assessment"));
            Assert.NotNull(FindByAutomationId(view, "vault-reassess-trusted-device"));
        }, CancellationToken.None);
    }

    private static StyledElement FindByAutomationId(Control root, string automationId)
    {
        var descendants = root.GetLogicalDescendants().OfType<StyledElement>();
        return Assert.Single(descendants, element =>
            AutomationProperties.GetAutomationId(element) == automationId);
    }
}
