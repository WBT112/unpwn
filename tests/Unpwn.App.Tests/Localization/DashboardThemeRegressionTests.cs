using System.Text.RegularExpressions;
using Xunit;

namespace Unpwn.App.Tests.Localization;

public sealed partial class DashboardThemeRegressionTests
{
    [Fact]
    public void DashboardBackgroundsUseThemeResourcesInsteadOfLiteralLightColors()
    {
        var dashboardPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Unpwn.App",
            "Views",
            "DashboardView.axaml");
        var xaml = File.ReadAllText(dashboardPath);

        Assert.DoesNotMatch(LiteralBackgroundRegex(), xaml);
        Assert.Contains("SystemControlBackgroundChromeMediumBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("SystemControlBackgroundChromeMediumLowBrush", xaml, StringComparison.Ordinal);
    }

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

    [GeneratedRegex("Background=\"#[0-9A-Fa-f]{6,8}\"", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralBackgroundRegex();
}
