using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

return await DesktopE2EHarness.RunAsync(args);

internal static class DesktopE2EHarness
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan CheckpointTimeout = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> InterruptionScenarios = new(StringComparer.Ordinal)
    {
        "interruption-mid-recovery",
        "interruption-active-browser",
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryReadOption(args, "--app", out var appPath) ||
            !Path.IsPathFullyQualified(appPath) || !File.Exists(appPath))
        {
            Console.Error.WriteLine(
                "Usage: Unpwn.DesktopE2E --app <absolute app entry path> " +
                "[--scenario <id>] [--artifacts <absolute directory>] " +
                "[--external-driver <absolute path>] [--published-root <absolute directory>]");
            return 2;
        }

        var scenario = TryReadOption(args, "--scenario", out var requestedScenario)
            ? requestedScenario
            : "golden";
        var artifacts = TryReadOption(args, "--artifacts", out var requestedArtifacts)
            ? requestedArtifacts
            : Path.Combine(Path.GetTempPath(), "unpwn-desktop-e2e-artifacts");
        if (!Path.IsPathFullyQualified(artifacts))
        {
            Console.Error.WriteLine("The artifact directory must be absolute.");
            return 2;
        }

        var externalDriver = TryReadOption(args, "--external-driver", out var requestedDriver)
            ? Path.GetFullPath(requestedDriver)
            : null;
        if (string.Equals(scenario, "external-black-box", StringComparison.Ordinal) !=
            (externalDriver is not null))
        {
            Console.Error.WriteLine("The external black-box scenario requires exactly one external driver.");
            return 2;
        }
        if (externalDriver is not null &&
            (!Path.IsPathFullyQualified(requestedDriver) || !File.Exists(externalDriver)))
        {
            Console.Error.WriteLine("The external driver path must name an existing absolute file.");
            return 2;
        }

        artifacts = Path.GetFullPath(artifacts);
        Directory.CreateDirectory(artifacts);
        appPath = Path.GetFullPath(appPath);
        if (TryReadOption(args, "--published-root", out var publishedRoot))
        {
            if (!Path.IsPathFullyQualified(publishedRoot) || !Directory.Exists(publishedRoot))
            {
                Console.Error.WriteLine("The published root must be an existing absolute directory.");
                return 2;
            }
            try
            {
                await VerifyPublishedArtifactAsync(Path.GetFullPath(publishedRoot), appPath, artifacts);
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        var runRoot = Path.Combine(Path.GetTempPath(), $"unpwn-desktop-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runRoot);
        using var runRootCleanup = new TemporaryDirectory(runRoot);
        await using var provider = await SyntheticProvider.StartAsync();
        var csvPath = Path.Combine(runRoot, "synthetic-accounts.csv");
        await File.WriteAllTextAsync(csvPath, BuildCsvFixture(scenario, provider.PasswordChangeUri));
        var dataRoot = Path.Combine(runRoot, "app-data");
        var configPath = Path.Combine(runRoot, "desktop-e2e-config.json");
        var processLogs = new List<ProcessPhaseLog>();

        bool succeeded;
        if (InterruptionScenarios.Contains(scenario))
        {
            succeeded = await RunInterruptionAsync(
                appPath, scenario, artifacts, runRoot, dataRoot, csvPath,
                provider.BaseAddress, configPath, processLogs);
        }
        else if (externalDriver is not null)
        {
            await WriteConfigurationAsync(
                configPath, scenario, dataRoot, csvPath, provider.BaseAddress,
                artifacts, "single", externallyDriven: true);
            succeeded = await RunExternallyDrivenAsync(
                appPath, externalDriver, configPath, runRoot, processLogs);
            await WriteExternalResultAsync(artifacts, scenario, succeeded);
        }
        else
        {
            await WriteConfigurationAsync(
                configPath, scenario, dataRoot, csvPath, provider.BaseAddress,
                artifacts, "single", externallyDriven: false);
            var phase = await RunAppProcessAsync(appPath, configPath, runRoot, "single");
            processLogs.Add(phase.Log);
            succeeded = phase.CompletedNormally && IsSuccessfulAppResult(artifacts);
        }

        await File.WriteAllTextAsync(
            Path.Combine(artifacts, "desktop-process.json"),
            JsonSerializer.Serialize(processLogs, JsonOptions));
        await WriteManifestAsync(artifacts, scenario);
        Console.WriteLine(succeeded
            ? $"Desktop E2E passed. Artifacts: {artifacts}"
            : $"Desktop E2E failed. Artifacts: {artifacts}");
        return succeeded ? 0 : 1;
    }

    private static async Task<bool> RunInterruptionAsync(
        string appPath,
        string scenario,
        string artifacts,
        string runRoot,
        string dataRoot,
        string csvPath,
        Uri providerBaseUri,
        string configPath,
        List<ProcessPhaseLog> processLogs)
    {
        await WriteConfigurationAsync(
            configPath, scenario, dataRoot, csvPath, providerBaseUri,
            artifacts, "prepare", externallyDriven: false);
        var checkpointPath = Path.Combine(dataRoot, "desktop-e2e-interruption.ready");
        using (var process = StartAppProcess(appPath, configPath, runRoot))
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            var reachedCheckpoint = await WaitForCheckpointAsync(process, checkpointPath);
            if (reachedCheckpoint)
            {
                process.Kill(entireProcessTree: true);
            }
            if (!process.HasExited)
            {
                await process.WaitForExitAsync();
            }
            processLogs.Add(await CreateLogAsync(
                "prepare-abrupt-termination", process, stdout, stderr,
                timedOut: !reachedCheckpoint, expectedTermination: reachedCheckpoint));
            if (!reachedCheckpoint)
            {
                return false;
            }
        }

        await WriteConfigurationAsync(
            configPath, scenario, dataRoot, csvPath, providerBaseUri,
            artifacts, "resume", externallyDriven: false);
        var resume = await RunAppProcessAsync(appPath, configPath, runRoot, "resume");
        processLogs.Add(resume.Log);
        return resume.CompletedNormally && IsSuccessfulAppResult(artifacts);
    }

    private static async Task<bool> RunExternallyDrivenAsync(
        string appPath,
        string externalDriver,
        string configPath,
        string runRoot,
        List<ProcessPhaseLog> processLogs)
    {
        using var app = StartAppProcess(appPath, configPath, runRoot);
        var appStdout = app.StandardOutput.ReadToEndAsync();
        var appStderr = app.StandardError.ReadToEndAsync();
        using var driver = StartExternalDriver(externalDriver, app.Id, runRoot);
        var driverStdout = driver.StandardOutput.ReadToEndAsync();
        var driverStderr = driver.StandardError.ReadToEndAsync();
        var driverTimedOut = !await WaitForExitAsync(driver, ProcessTimeout);
        if (driverTimedOut)
        {
            driver.Kill(entireProcessTree: true);
            await driver.WaitForExitAsync();
        }

        var driverFailed = driverTimedOut || driver.ExitCode != 0;
        var appStoppedAfterDriverFailure = false;
        var appTimedOut = false;
        if (driverFailed && !app.HasExited)
        {
            app.Kill(entireProcessTree: true);
            await app.WaitForExitAsync();
            appStoppedAfterDriverFailure = true;
        }
        else if (!driverFailed)
        {
            appTimedOut = !await WaitForExitAsync(app, TimeSpan.FromSeconds(30));
        }
        if (appTimedOut && !app.HasExited)
        {
            app.Kill(entireProcessTree: true);
            await app.WaitForExitAsync();
        }
        processLogs.Add(await CreateLogAsync(
            "external-driver", driver, driverStdout, driverStderr,
            driverTimedOut, expectedTermination: false));
        processLogs.Add(await CreateLogAsync(
            "externally-driven-app", app, appStdout, appStderr,
            appTimedOut, expectedTermination: appStoppedAfterDriverFailure));
        return !driverTimedOut && !appTimedOut && driver.ExitCode == 0 && app.ExitCode == 0;
    }

    private static Process StartExternalDriver(string path, int processId, string workingDirectory)
    {
        var isPython = path.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isPython && OperatingSystem.IsLinux() ? "/usr/bin/python3" :
                isPython ? "python3" : path,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (isPython)
        {
            startInfo.ArgumentList.Add(path);
        }
        startInfo.ArgumentList.Add("--process-id");
        startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The external desktop driver did not start.");
    }

    private static async Task<(bool CompletedNormally, ProcessPhaseLog Log)> RunAppProcessAsync(
        string appPath,
        string configPath,
        string workingDirectory,
        string phase)
    {
        using var process = StartAppProcess(appPath, configPath, workingDirectory);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var timedOut = !await WaitForExitAsync(process, ProcessTimeout);
        if (timedOut)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        var log = await CreateLogAsync(
            phase, process, stdout, stderr, timedOut, expectedTermination: false);
        return (!timedOut && process.ExitCode == 0, log);
    }

    private static Process StartAppProcess(string appPath, string configPath, string workingDirectory)
    {
        var isDll = appPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        var startInfo = new ProcessStartInfo
        {
            FileName = isDll ? "dotnet" : appPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (isDll)
        {
            startInfo.ArgumentList.Add(appPath);
        }
        startInfo.ArgumentList.Add("--desktop-e2e-config");
        startInfo.ArgumentList.Add(configPath);
        return Process.Start(startInfo) ??
            throw new InvalidOperationException("The desktop application did not start.");
    }

    private static async Task<bool> WaitForCheckpointAsync(Process process, string path)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < CheckpointTimeout && !process.HasExited)
        {
            if (File.Exists(path))
            {
                return true;
            }
            await Task.Delay(50);
        }
        return File.Exists(path);
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task<ProcessPhaseLog> CreateLogAsync(
        string phase,
        Process process,
        Task<string> stdout,
        Task<string> stderr,
        bool timedOut,
        bool expectedTermination) => new(
            phase,
            Environment.OSVersion.VersionString,
            ReadLinuxDistribution(),
            Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ??
                Environment.GetEnvironmentVariable("DISPLAY") ??
                (OperatingSystem.IsWindows() ? "windows-desktop" : "unknown"),
            process.ExitCode,
            timedOut,
            expectedTermination,
            Sanitize(await stdout),
            Sanitize(await stderr));

    private static async Task WriteConfigurationAsync(
        string path,
        string scenario,
        string dataRoot,
        string csvPath,
        Uri providerBaseUri,
        string artifacts,
        string phase,
        bool externallyDriven) => await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(new
            {
                Scenario = scenario,
                DataRoot = dataRoot,
                CsvFixturePath = csvPath,
                ProviderBaseUri = providerBaseUri.ToString(),
                ArtifactDirectory = artifacts,
                Phase = phase,
                ExternallyDriven = externallyDriven,
            }, JsonOptions));

    private static string BuildCsvFixture(string scenario, Uri passwordChangeUri)
    {
        if (string.Equals(scenario, "import-correction-and-retry", StringComparison.Ordinal))
        {
            return "unrelated,columns\nvalue,only\n";
        }
        if (string.Equals(scenario, "multi-account-transition", StringComparison.Ordinal))
        {
            return "service,username,url,password\n" +
                $"gmail.com,mailbox@example.invalid,{passwordChangeUri},synthetic-ignored-value\n" +
                $"github.com,developer@example.invalid,{passwordChangeUri},synthetic-ignored-value\n" +
                $"synthetic,review@example.invalid,{passwordChangeUri},synthetic-ignored-value\n";
        }

        var service = scenario switch
        {
            "reviewed-browser-step-hierarchy" => "github.com",
            "browser-startup-failure-fallback" => "bitwarden",
            _ => "synthetic",
        };
        return "service,username,url,password\n" +
            $"{service},user@example.invalid,{passwordChangeUri},synthetic-ignored-value\n";
    }

    private static bool IsSuccessfulAppResult(string artifacts)
    {
        var path = Path.Combine(artifacts, "desktop-e2e-result.json");
        return File.Exists(path) &&
            JsonDocument.Parse(File.ReadAllText(path))
                .RootElement.GetProperty("Succeeded").GetBoolean();
    }

    private static async Task WriteExternalResultAsync(
        string artifacts,
        string scenario,
        bool succeeded) => await File.WriteAllTextAsync(
            Path.Combine(artifacts, "desktop-e2e-result.json"),
            JsonSerializer.Serialize(new
            {
                Succeeded = succeeded,
                FailureCode = succeeded ? null : "external-black-box-failed",
                Scenario = scenario,
                Driver = "external-at-spi",
            }, JsonOptions));

    private static async Task VerifyPublishedArtifactAsync(
        string publishedRoot,
        string appPath,
        string artifacts)
    {
        var relativeApp = Path.GetRelativePath(publishedRoot, appPath);
        if (Path.IsPathFullyQualified(relativeApp) || relativeApp == ".." ||
            relativeApp.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The app entry point is outside the published artifact.");
        }

        var repositoryRoot = Path.GetFullPath(Directory.GetCurrentDirectory());
        var relativeToRepository = Path.GetRelativePath(repositoryRoot, publishedRoot);
        if (!Path.IsPathFullyQualified(relativeToRepository) && relativeToRepository != ".." &&
            !relativeToRepository.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Release smoke must run a clean artifact outside the repository tree.");
        }

        var files = Directory.EnumerateFiles(publishedRoot, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException("The published artifact is empty.");
        }

        string[] forbiddenSegments =
        [
            "browser-profiles",
            "test-results",
            "samples",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        ];
        string[] forbiddenExtensions = [".unpwn", ".csv", ".db", ".sqlite", ".marker", ".pdb"];
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(publishedRoot, file);
            if (forbiddenSegments.Any(segment =>
                    relative.Contains(segment, StringComparison.OrdinalIgnoreCase)) ||
                forbiddenExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The published artifact contains a build/test/data file: {relative}");
            }
            if (IsReviewableTextFile(file))
            {
                var content = await File.ReadAllTextAsync(file);
                if (content.Contains("UNPWN_TEST_SECRET_", StringComparison.Ordinal) ||
                    content.Contains("desktop-e2e-only-482!", StringComparison.Ordinal) ||
                    content.Contains("synthetic-ignored-value", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"The published artifact contains synthetic secret data: {relative}");
                }
            }
        }

        var inventoryFiles = new List<object>();
        foreach (var file in files)
        {
            await using var stream = File.OpenRead(file);
            inventoryFiles.Add(new
            {
                Path = Path.GetRelativePath(publishedRoot, file).Replace('\\', '/'),
                Length = new FileInfo(file).Length,
                Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream)),
            });
        }
        var inventory = new { EntryPoint = relativeApp, Files = inventoryFiles };
        await File.WriteAllTextAsync(
            Path.Combine(artifacts, "release-artifact-inventory.json"),
            JsonSerializer.Serialize(inventory, JsonOptions));
    }

    private static bool IsReviewableTextFile(string path) => Path.GetExtension(path) is
        ".json" or ".config" or ".xml" or ".txt" or ".deps" or ".runtimeconfig";

    private static async Task WriteManifestAsync(string artifacts, string scenario)
    {
        var manifest = new
        {
            Scenario = scenario,
            Files = Directory.EnumerateFiles(artifacts)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray(),
        };
        await File.WriteAllTextAsync(
            Path.Combine(artifacts, "desktop-e2e-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static bool TryReadOption(string[] args, string name, out string value)
    {
        value = string.Empty;
        var indexes = args
            .Select((argument, index) => (argument, index))
            .Where(item => string.Equals(item.argument, name, StringComparison.Ordinal))
            .Select(item => item.index)
            .ToArray();
        if (indexes.Length != 1 || indexes[0] == args.Length - 1)
        {
            return false;
        }
        value = args[indexes[0] + 1];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string Sanitize(string text) => text
        .Replace("desktop-e2e-only-482!", "[redacted]", StringComparison.Ordinal)
        .Replace("synthetic-wrong-password", "[redacted]", StringComparison.Ordinal)
        .Replace("synthetic-ignored-value", "[redacted]", StringComparison.Ordinal);

    private static string ReadLinuxDistribution()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/etc/os-release"))
        {
            return OperatingSystem.IsWindows() ? "windows" : "unknown";
        }
        var values = File.ReadLines("/etc/os-release")
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1].Trim('"'), StringComparer.Ordinal);
        return values.TryGetValue("PRETTY_NAME", out var name) ? name : "linux-unknown";
    }

    private sealed record ProcessPhaseLog(
        string Phase,
        string OperatingSystem,
        string Distribution,
        string Display,
        int ExitCode,
        bool TimedOut,
        bool ExpectedTermination,
        string StandardOutput,
        string StandardError);
}

internal sealed class SyntheticProvider : IAsyncDisposable
{
    private readonly WebApplication _application;

    private SyntheticProvider(WebApplication application, Uri baseAddress)
    {
        _application = application;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public Uri PasswordChangeUri => new(
        BaseAddress,
        "/settings/password?scenario=password-change");

    public static async Task<SyntheticProvider> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var application = builder.Build();
        application.MapGet("/settings/password", (HttpRequest request) =>
            string.Equals(request.Query["scenario"], "password-change", StringComparison.Ordinal)
                ? Results.Content(
                    """
                    <!doctype html>
                    <html lang="en">
                    <body data-unpwn-provider="synthetic" data-unpwn-workflow="password-change">
                      <h1>UNPWN_SYNTHETIC_PROVIDER</h1>
                      <label>New password <input type="password" data-testid="new-password"></label>
                      <label>Confirm password <input type="password" data-testid="confirm-password"></label>
                    </body>
                    </html>
                    """,
                    "text/html")
                : Results.BadRequest());
        await application.StartAsync();
        var address = application.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses.Single() ??
            throw new InvalidOperationException("The synthetic provider did not bind a loopback address.");
        return new SyntheticProvider(application, new Uri(address));
    }

    public async ValueTask DisposeAsync() => await _application.DisposeAsync();
}

internal sealed class TemporaryDirectory(string path) : IDisposable
{
    private readonly string _path = Path.GetFullPath(path);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Never retain the temporary browser profile as an uploaded artifact.
        }
    }
}
