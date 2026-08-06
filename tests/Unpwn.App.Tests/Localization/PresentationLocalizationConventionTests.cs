using System.Text.RegularExpressions;
using Unpwn.App.Localization;
using Xunit;

namespace Unpwn.App.Tests.Localization;

public sealed partial class PresentationLocalizationConventionTests
{
    private static readonly HashSet<string> AllowedLiteralValues =
        new(StringComparer.Ordinal)
        {
            "unpwn",
            "×",
        };

    [Fact]
    public void UserFacingXamlAttributesUseBindingsOrDynamicResources()
    {
        var appDirectory = Path.Combine(FindRepositoryRoot(), "src", "Unpwn.App");
        var violations = Directory
            .EnumerateFiles(appDirectory, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => FindLiteralPresentationAttributes(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void EveryDynamicLocalizationResourceExistsInEnglishSourceSet()
    {
        var localization = new ResourceLocalizationService();
        var sourceKeys = localization
            .GetResourceKeys(ResourceLocalizationService.DefaultLanguageCode)
            .ToHashSet(StringComparer.Ordinal);
        var appDirectory = Path.Combine(FindRepositoryRoot(), "src", "Unpwn.App");
        var missingKeys = Directory
            .EnumerateFiles(appDirectory, "*.axaml", SearchOption.AllDirectories)
            .SelectMany(path => DynamicResourceRegex().Matches(File.ReadAllText(path)))
            .Select(match => match.Groups[1].Value)
            .Where(key => key.Contains('.', StringComparison.Ordinal))
            .Where(key => !sourceKeys.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingKeys);
    }

    private static IEnumerable<string> FindLiteralPresentationAttributes(string path)
    {
        var relativePath = Path.GetRelativePath(FindRepositoryRoot(), path);
        foreach (Match match in PresentationAttributeRegex().Matches(File.ReadAllText(path)))
        {
            var value = match.Groups[2].Value.Trim();
            if (value.Length == 0 ||
                value.StartsWith('{') ||
                AllowedLiteralValues.Contains(value))
            {
                continue;
            }

            yield return $"{relativePath}: {match.Groups[1].Value}=\"{value}\"";
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

    [GeneratedRegex("(Text|Content|Title|ToolTip\\.Tip)=\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex PresentationAttributeRegex();

    [GeneratedRegex("\\{DynamicResource\\s+([A-Za-z0-9_.-]+)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex DynamicResourceRegex();
}
