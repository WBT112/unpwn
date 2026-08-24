using System.Xml.Linq;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class TaskFirstUxRegressionTests
{
    [Fact]
    public void ApplicationDefinesReusableActionHierarchy()
    {
        var document = Load("src", "Unpwn.App", "App.axaml");
        var selectors = document.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => (string?)element.Attribute("Selector"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Button.primary", selectors);
        Assert.Contains("Button.secondary", selectors);
        Assert.Contains("Button.tertiary", selectors);
        Assert.Contains("Button.destructive", selectors);
        Assert.Contains("Border.actionable-warning", selectors);
        Assert.Contains("Border.passive-information", selectors);
        Assert.Contains("Expander.details", selectors);
    }

    [Fact]
    public void MajorWorkspacesKeepAdministrativeDetailBehindDisclosure()
    {
        var dashboard = Load("src", "Unpwn.App", "Views", "DashboardView.axaml");
        var accounts = Load("src", "Unpwn.App", "Views", "AccountsView.axaml");
        var credentials = Load("src", "Unpwn.App", "Views", "CredentialExportView.axaml");
        var completion = Load("src", "Unpwn.App", "Views", "CompletionScreenView.axaml");
        var vault = Load("src", "Unpwn.App", "Views", "VaultEntryView.axaml");

        AssertExpander(dashboard, "dashboard-session-details");
        AssertExpander(dashboard, "dashboard-recovery-details");
        AssertExpander(accounts, "accounts-inventory-management");
        AssertExpander(credentials, "credentials-advanced");
        AssertExpander(completion, "completion-details");
        AssertExpander(vault, "vault-manage-recent");
        AssertExpander(vault, "vault-create-location-details");
    }

    [Fact]
    public void NormalTaskSurfacesExposeStablePrimaryActionsWithoutLegacyCeremony()
    {
        var dashboard = Load("src", "Unpwn.App", "Views", "DashboardView.axaml");
        var accounts = Load("src", "Unpwn.App", "Views", "AccountsView.axaml");
        var credentials = Load("src", "Unpwn.App", "Views", "CredentialExportView.axaml");
        var completion = Load("src", "Unpwn.App", "Views", "CompletionScreenView.axaml");

        AssertPrimary(dashboard, "dashboard-create-session");
        AssertPrimary(dashboard, "dashboard-recommendation-open");
        AssertPrimary(accounts, "accounts-category-save");
        AssertPrimary(accounts, "accounts-continue-recovery");
        AssertPrimary(credentials, "credentials-continue-completion");
        AssertPrimary(completion, "completion-complete");

        Assert.Null(Find(dashboard, "dashboard-security-acknowledge"));
        Assert.Null(Find(completion, "completion-review"));
    }

    [Fact]
    public void ImportReviewStartsCollapsedAndCompletionReviewIsAutomatic()
    {
        var import = Load("src", "Unpwn.App", "Views", "CsvImportView.axaml");
        var review = import.Descendants().Single(element =>
            element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Name" && attribute.Value == "ReviewPanel"));
        Assert.Equal("False", (string?)review.Attribute("IsVisible"));

        var completionViewModel = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "Unpwn.App", "Presentation", "CompletionScreenViewModel.cs"));
        Assert.Contains("ReviewCompletionCommand.Execute(null)", completionViewModel, StringComparison.Ordinal);
    }

    private static void AssertExpander(XDocument document, string automationId) =>
        Assert.Equal("Expander", Find(document, automationId)?.Name.LocalName);

    private static void AssertPrimary(XDocument document, string automationId)
    {
        var control = Assert.IsType<XElement>(Find(document, automationId));
        Assert.Contains(
            "primary",
            ((string?)control.Attribute("Classes") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static XElement? Find(XDocument document, string automationId) =>
        document.Descendants().SingleOrDefault(element => element.Attributes().Any(attribute =>
            attribute.Name.LocalName.EndsWith(".AutomationId", StringComparison.Ordinal) &&
            attribute.Value == automationId));

    private static XDocument Load(params string[] path) =>
        XDocument.Load(Path.Combine([FindRepositoryRoot(), .. path]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "unpwn.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("The repository root containing unpwn.slnx was not found.");
    }
}
