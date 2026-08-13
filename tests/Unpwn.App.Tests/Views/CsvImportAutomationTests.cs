using System.Globalization;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Views;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class CsvImportAutomationTests
{
    [Fact]
    public async Task CompleteMappingCreatesSafePreviewWithoutPreparatoryConfirmation()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            const string secret = "UNPWN_TEST_SECRET_automatic-import";
            const string csv =
                "service,username,password\n" +
                "Mail,person@example.invalid," + secret + "\n";
            var view = CreateView();
            var window = new Window { Content = view };
            window.Show();

            await view.LoadCsvAsync("synthetic.csv", StreamFactory(csv));
            Dispatcher.UIThread.RunJobs();

            Assert.False(Find<Control>(view, "import-mapping-panel").IsVisible);
            Assert.True(Find<Button>(view, "import-reviewed").IsEnabled);
            Assert.DoesNotContain(view.GetLogicalDescendants().OfType<Control>(), control =>
                AutomationProperties.GetAutomationId(control) is
                    "import-create-preview" or "import-exclude-passwords");
            Assert.All(
                Find<ListBox>(view, "import-preview-items").Items.Cast<string>(),
                item => Assert.DoesNotContain(secret, item, StringComparison.Ordinal));
            var warning = Find<Control>(view, "import-password-warning");
            Assert.True(warning.IsVisible);
            Assert.DoesNotContain(
                secret,
                AutomationProperties.GetName(warning),
                StringComparison.Ordinal);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AmbiguousMappingShowsOnlyRequiredTaskAndRefreshesWhenResolved()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(async () =>
        {
            const string csv =
                "service,username,email,password\n" +
                "Mail,person@example.invalid,person@example.invalid,discarded\n";
            var view = CreateView();
            var window = new Window { Content = view };
            window.Show();

            await view.LoadCsvAsync("ambiguous.csv", StreamFactory(csv));
            Dispatcher.UIThread.RunJobs();

            Assert.True(Find<Control>(view, "import-mapping-panel").IsVisible);
            Assert.False(Find<Button>(view, "import-reviewed").IsEnabled);
            Assert.False(Find<Control>(view, "import-service-mapping-field").IsVisible);
            Assert.False(Find<Control>(view, "import-account-mapping-field").IsVisible);
            Assert.True(Find<Control>(view, "import-login-mapping-field").IsVisible);
            Assert.False(Find<Control>(view, "import-url-mapping-field").IsVisible);
            Assert.Contains(
                "Choose which possible username or email column to use.",
                Find<TextBlock>(view, "import-mapping-issues").Text,
                StringComparison.Ordinal);
            var loginMapping = Find<ComboBox>(view, "import-login-column");
            Assert.DoesNotContain("password", loginMapping.Items.Cast<string>());

            loginMapping.SelectedItem = "email";
            await view.EvaluateMappingAndPreviewAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.False(Find<Control>(view, "import-mapping-panel").IsVisible);
            Assert.True(Find<Button>(view, "import-reviewed").IsEnabled);
            window.Close();
        }, CancellationToken.None);
    }

    private static CsvImportView CreateView() => new()
    {
        DataContext = new CsvImportScreenViewModel(
            new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"))),
    };

    private static Func<Task<Stream>> StreamFactory(string csv)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        return () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private static T Find<T>(Control root, string automationId)
        where T : Control => Assert.IsType<T>(
            root.GetLogicalDescendants()
                .OfType<Control>()
                .Single(control => string.Equals(
                    AutomationProperties.GetAutomationId(control),
                    automationId,
                    StringComparison.Ordinal)),
            exactMatch: false);
}
