using System.Text.Json;
using Unpwn.App;
using Xunit;

namespace Unpwn.App.Tests;

public sealed class DesktopE2EConfigurationTests
{
    [Fact]
    public void ScenarioCatalogUsesStableUniqueLanguageNeutralIdentifiers()
    {
        Assert.Equal(6, DesktopE2EScenarioCatalog.All.Count);
        Assert.All(DesktopE2EScenarioCatalog.All, scenario =>
        {
            Assert.Matches("^[a-z]+(?:-[a-z]+)*$", scenario);
            Assert.Equal(scenario.Trim(), scenario);
        });
    }

    [Fact]
    public void MissingOptionKeepsProductionComposition()
    {
        Assert.Null(DesktopE2EConfiguration.LoadFromArguments([]));
    }

    [Fact]
    public void ValidConfigurationRequiresExplicitAbsoluteIsolatedInputs()
    {
        using var temporary = new TestDirectory();
        var csv = Path.Combine(temporary.Path, "accounts.csv");
        File.WriteAllText(csv, "service,username\nsynthetic,user@example.invalid\n");
        var artifacts = Path.Combine(temporary.Path, "artifacts");
        var data = Path.Combine(temporary.Path, "data");
        var configuration = WriteConfiguration(
            temporary.Path,
            data,
            csv,
            "http://127.0.0.1:41823",
            artifacts);

        var loaded = DesktopE2EConfiguration.LoadFromArguments(
            ["--desktop-e2e-config", configuration]);

        Assert.NotNull(loaded);
        Assert.Equal(DesktopE2EScenarioCatalog.Golden, loaded.Scenario);
        Assert.Equal(Path.GetFullPath(data), loaded.DataRoot);
        Assert.Equal(new Uri("http://127.0.0.1:41823"), loaded.ProviderBaseUri);
        Assert.True(Directory.Exists(artifacts));
    }

    [Fact]
    public void UnknownScenarioIsRejected()
    {
        using var temporary = new TestDirectory();
        var csv = Path.Combine(temporary.Path, "accounts.csv");
        File.WriteAllText(csv, "service,username\nsynthetic,user@example.invalid\n");
        var configuration = WriteConfiguration(
            temporary.Path,
            Path.Combine(temporary.Path, "data"),
            csv,
            "http://127.0.0.1:41823",
            Path.Combine(temporary.Path, "artifacts"),
            "not-a-scenario");

        Assert.Throws<InvalidOperationException>(() =>
            DesktopE2EConfiguration.LoadFromArguments(["--desktop-e2e-config", configuration]));
    }

    [Theory]
    [InlineData("https://127.0.0.1:41823")]
    [InlineData("http://example.invalid:41823")]
    [InlineData("http://user@127.0.0.1:41823")]
    public void ProviderOutsideExplicitHttpLoopbackBoundaryIsRejected(string provider)
    {
        using var temporary = new TestDirectory();
        var csv = Path.Combine(temporary.Path, "accounts.csv");
        File.WriteAllText(csv, "service,username\nsynthetic,user@example.invalid\n");
        var configuration = WriteConfiguration(
            temporary.Path,
            Path.Combine(temporary.Path, "data"),
            csv,
            provider,
            Path.Combine(temporary.Path, "artifacts"));

        Assert.Throws<InvalidOperationException>(() =>
            DesktopE2EConfiguration.LoadFromArguments(
                ["--desktop-e2e-config", configuration]));
    }

    private static string WriteConfiguration(
        string root,
        string dataRoot,
        string csv,
        string provider,
        string artifacts,
        string scenario = DesktopE2EScenarioCatalog.Golden)
    {
        var path = Path.Combine(root, "desktop-e2e.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Scenario = scenario,
            DataRoot = dataRoot,
            CsvFixturePath = csv,
            ProviderBaseUri = provider,
            ArtifactDirectory = artifacts,
        }));
        return path;
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = Directory.CreateTempSubdirectory("unpwn-desktop-e2e-tests-").FullName;
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
