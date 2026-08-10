using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class ShellNavigationVisualStateTests
{
    [Fact]
    public void NavigationItemsBindAndVisualizeDisabledStateOnTheContainer()
    {
        var mainWindowPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Unpwn.App",
            "MainWindow.axaml");
        var document = XDocument.Load(mainWindowPath);
        var styles = document.Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .ToArray();

        var itemStyle = Assert.Single(styles, style =>
            string.Equals((string?)style.Attribute("Selector"), "ListBoxItem", StringComparison.Ordinal));
        var isEnabledSetter = Assert.Single(itemStyle.Elements(), element =>
            string.Equals((string?)element.Attribute("Property"), "IsEnabled", StringComparison.Ordinal));
        Assert.Equal("{ReflectionBinding IsEnabled}", (string?)isEnabledSetter.Attribute("Value"));

        var disabledStyle = Assert.Single(styles, style =>
            string.Equals((string?)style.Attribute("Selector"), "ListBoxItem:disabled", StringComparison.Ordinal));
        var opacitySetter = Assert.Single(disabledStyle.Elements(), element =>
            string.Equals((string?)element.Attribute("Property"), "Opacity", StringComparison.Ordinal));
        var opacity = double.Parse(
            Assert.IsType<string>((object?)opacitySetter.Attribute("Value")?.Value),
            CultureInfo.InvariantCulture);

        Assert.InRange(opacity, 0.25, 0.6);
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
}
