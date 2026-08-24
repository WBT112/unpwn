using System.Text.Json;

namespace Unpwn.App;

internal sealed record DesktopE2EConfiguration(
    string Scenario,
    string DataRoot,
    string CsvFixturePath,
    Uri ProviderBaseUri,
    string ArtifactDirectory,
    string Phase,
    bool ExternallyDriven)
{
    private const string Option = "--desktop-e2e-config";
    private const string SinglePhase = "single";
    private const string PreparePhase = "prepare";
    private const string ResumePhase = "resume";

    public static DesktopE2EConfiguration? LoadFromArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var optionIndexes = args
            .Select((argument, index) => (argument, index))
            .Where(item => string.Equals(item.argument, Option, StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();
        if (optionIndexes.Length == 0)
        {
            return null;
        }

        if (optionIndexes.Length != 1 || optionIndexes[0] == args.Length - 1)
        {
            throw new InvalidOperationException("The desktop E2E configuration argument is invalid.");
        }

        var path = RequireAbsolutePath(args[optionIndexes[0] + 1], "configuration");
        DesktopE2EConfigurationDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DesktopE2EConfigurationDocument>(
                File.ReadAllText(path));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException(
                "The desktop E2E configuration could not be read.",
                exception);
        }

        if (document is null)
        {
            throw new InvalidOperationException("The desktop E2E configuration is empty.");
        }

        var dataRoot = RequireAbsolutePath(document.DataRoot, "data root");
        var csvFixturePath = RequireAbsolutePath(document.CsvFixturePath, "CSV fixture");
        var artifactDirectory = RequireAbsolutePath(document.ArtifactDirectory, "artifact directory");
        if (!File.Exists(csvFixturePath))
        {
            throw new InvalidOperationException("The desktop E2E CSV fixture does not exist.");
        }

        if (!DesktopE2EScenarioCatalog.IsSupported(document.Scenario))
        {
            throw new InvalidOperationException("The desktop E2E scenario is not supported.");
        }

        var phase = string.IsNullOrWhiteSpace(document.Phase)
            ? SinglePhase
            : document.Phase;
        var isInterruptionScenario = document.Scenario is
            DesktopE2EScenarioCatalog.InterruptionMidRecovery or
            DesktopE2EScenarioCatalog.InterruptionActiveBrowser;
        if (isInterruptionScenario
                ? phase is not (PreparePhase or ResumePhase)
                : phase != SinglePhase)
        {
            throw new InvalidOperationException("The desktop E2E phase is invalid for the selected scenario.");
        }

        var externallyDriven = document.ExternallyDriven;
        if (externallyDriven != string.Equals(
                document.Scenario,
                DesktopE2EScenarioCatalog.ExternalBlackBox,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the external black-box scenario can use an external driver.");
        }

        if (!Uri.TryCreate(document.ProviderBaseUri, UriKind.Absolute, out var providerBaseUri) ||
            providerBaseUri.Scheme != Uri.UriSchemeHttp ||
            !providerBaseUri.IsLoopback ||
            !string.IsNullOrEmpty(providerBaseUri.UserInfo))
        {
            throw new InvalidOperationException(
                "The desktop E2E provider must be an HTTP loopback origin.");
        }

        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(artifactDirectory);
        return new DesktopE2EConfiguration(
            document.Scenario!,
            dataRoot,
            csvFixturePath,
            providerBaseUri,
            artifactDirectory,
            phase,
            externallyDriven);
    }

    public Uri PasswordChangeUri => new(
        ProviderBaseUri,
        "/settings/password?scenario=password-change");

    public bool ForceBrowserStartupFailure => string.Equals(
        Scenario,
        DesktopE2EScenarioCatalog.BrowserStartupFailureFallback,
        StringComparison.Ordinal);

    public string RecentVaultsPath => Path.Combine(DataRoot, "recent-vaults.json");

    public string PreferencesPath => Path.Combine(DataRoot, "preferences.json");

    public string RunMarkerPath => Path.Combine(DataRoot, "run-state", "active.marker");

    public string InterruptionReadyPath => Path.Combine(DataRoot, "desktop-e2e-interruption.ready");

    public bool IsPreparePhase => string.Equals(Phase, PreparePhase, StringComparison.Ordinal);

    public bool IsResumePhase => string.Equals(Phase, ResumePhase, StringComparison.Ordinal);

    private static string RequireAbsolutePath(string? path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"The desktop E2E {name} path must be absolute.");
        }

        return Path.GetFullPath(path);
    }

    private sealed record DesktopE2EConfigurationDocument(
        string? Scenario,
        string? DataRoot,
        string? CsvFixturePath,
        string? ProviderBaseUri,
        string? ArtifactDirectory,
        string? Phase,
        bool ExternallyDriven);
}
