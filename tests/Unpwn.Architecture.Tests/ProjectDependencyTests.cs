using System.Xml.Linq;
using Xunit;

namespace Unpwn.Architecture.Tests;

public sealed class ProjectDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Unpwn.App"] =
            [
                "Unpwn.Application",
                "Unpwn.Automation",
                "Unpwn.Export",
                "Unpwn.Import",
                "Unpwn.Infrastructure",
                "Unpwn.Providers",
                "Unpwn.Vault",
            ],
            ["Unpwn.Application"] = ["Unpwn.Core"],
            ["Unpwn.Automation"] = ["Unpwn.Application", "Unpwn.Core"],
            ["Unpwn.Core"] = [],
            ["Unpwn.Export"] = ["Unpwn.Application", "Unpwn.Core"],
            ["Unpwn.Import"] = ["Unpwn.Application", "Unpwn.Core"],
            ["Unpwn.Infrastructure"] = ["Unpwn.Application", "Unpwn.Core"],
            ["Unpwn.Providers"] = ["Unpwn.Application", "Unpwn.Core"],
            ["Unpwn.Vault"] = ["Unpwn.Application", "Unpwn.Core"],
        };

    [Fact]
    public void SolutionContainsEveryArchitectureProject()
    {
        var solution = XDocument.Load(Path.Combine(RepositoryRoot, "unpwn.slnx"));
        var actualProjects = solution
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => path is not null)
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Where(name => name.StartsWith("Unpwn.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expectedProjects = AllowedReferences.Keys
            .Append("Unpwn.App.Tests")
            .Append("Unpwn.Architecture.Tests")
            .Append("Unpwn.Application.Tests")
            .Append("Unpwn.Automation.Tests")
            .Append("Unpwn.Core.Tests")
            .Append("Unpwn.DesktopE2E")
            .Append("Unpwn.Export.Tests")
            .Append("Unpwn.Import.Tests")
            .Append("Unpwn.ProviderSmokeChecks")
            .Append("Unpwn.SyntheticProvider.Tests")
            .Append("Unpwn.Vault.Tests")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedProjects, actualProjects);
    }

    [Fact]
    public void ProjectReferencesFollowTheDocumentedDependencyDirection()
    {
        foreach (var (projectName, expectedReferences) in AllowedReferences)
        {
            var project = LoadProject(projectName);
            var actualReferences = project
                .Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")?.Value))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    [Fact]
    public void CoreIsPlatformIndependent()
    {
        var project = LoadProject("Unpwn.Core");

        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("FrameworkReference"));
        Assert.Empty(project.Descendants("ProjectReference"));

        var coreDirectory = Path.Combine(RepositoryRoot, "src", "Unpwn.Core");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));
        var forbiddenTerms = new[] { "Avalonia", "SQLite", "Playwright", "Microsoft.Win32" };

        foreach (var forbiddenTerm in forbiddenTerms)
        {
            Assert.DoesNotContain(forbiddenTerm, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProviderSmokeCheckToolDependsOnlyOnAutomationAndProviderCatalog()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot,
            "tools",
            "Unpwn.ProviderSmokeChecks",
            "Unpwn.ProviderSmokeChecks.csproj"));
        var actualReferences = project
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")?.Value))
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Unpwn.Automation", "Unpwn.Providers"], actualReferences);
    }

    [Fact]
    public void DesktopE2EHarnessDoesNotBypassTheUiThroughProductReferences()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot,
            "tools",
            "Unpwn.DesktopE2E",
            "Unpwn.DesktopE2E.csproj"));

        Assert.Empty(project.Descendants("ProjectReference"));
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static XDocument LoadProject(string projectName) => XDocument.Load(
        Path.Combine(RepositoryRoot, "src", projectName, $"{projectName}.csproj"));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "unpwn.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
