using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class AutomaticTabRefreshTests
{
    [Fact]
    public void ServiceBackedTabsDoNotExposeManualRefreshCommands()
    {
        var viewsPath = Path.Combine(FindRepositoryRoot(), "src", "Unpwn.App", "Views");
        var viewMarkup = Directory
            .EnumerateFiles(viewsPath, "*.axaml", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText);

        foreach (var xaml in viewMarkup)
        {
            Assert.DoesNotContain("Command=\"{Binding RefreshCommand}\"", xaml, StringComparison.Ordinal);
        }
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
