using System.Diagnostics;
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
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(3);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(string[] args)
    {
        if (!TryReadOption(args, "--app", out var appPath) ||
            !Path.IsPathFullyQualified(appPath) ||
            !File.Exists(appPath))
        {
            Console.Error.WriteLine("Usage: Unpwn.DesktopE2E --app <absolute Unpwn.App.dll path> [--artifacts <absolute directory>]");
            return 2;
        }

        var artifacts = TryReadOption(args, "--artifacts", out var requestedArtifacts)
            ? requestedArtifacts
            : Path.Combine(Path.GetTempPath(), "unpwn-desktop-e2e-artifacts");
        if (!Path.IsPathFullyQualified(artifacts))
        {
            Console.Error.WriteLine("The artifact directory must be absolute.");
            return 2;
        }

        artifacts = Path.GetFullPath(artifacts);
        Directory.CreateDirectory(artifacts);
        var runRoot = Path.Combine(
            Path.GetTempPath(),
            $"unpwn-desktop-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runRoot);
        using var runRootCleanup = new TemporaryDirectory(runRoot);

        await using var provider = await SyntheticProvider.StartAsync();
        var csvPath = Path.Combine(runRoot, "synthetic-accounts.csv");
        await File.WriteAllTextAsync(
            csvPath,
            "service,username,url,password\n" +
            $"synthetic,user@example.invalid,{provider.PasswordChangeUri},synthetic-ignored-value\n");
        var configPath = Path.Combine(runRoot, "desktop-e2e-config.json");
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(
                new
                {
                    DataRoot = Path.Combine(runRoot, "app-data"),
                    CsvFixturePath = csvPath,
                    ProviderBaseUri = provider.BaseAddress.ToString(),
                    ArtifactDirectory = artifacts,
                },
                JsonOptions));

        using var process = new Process
        {
            StartInfo = CreateStartInfo(Path.GetFullPath(appPath), configPath),
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        using (var timeout = new CancellationTokenSource(ProcessTimeout))
        {
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var processLog = new
        {
            LogicalStep = timedOut ? "process-timeout" : "process-exit",
            ExitCode = timedOut ? -1 : process.ExitCode,
            TimedOut = timedOut,
            StandardOutput = Sanitize(await stdout),
            StandardError = Sanitize(await stderr),
        };
        await File.WriteAllTextAsync(
            Path.Combine(artifacts, "desktop-process.json"),
            JsonSerializer.Serialize(processLog, JsonOptions));

        var resultPath = Path.Combine(artifacts, "desktop-e2e-result.json");
        var succeeded = !timedOut && process.ExitCode == 0 &&
            File.Exists(resultPath) &&
            JsonDocument.Parse(await File.ReadAllTextAsync(resultPath))
                .RootElement.GetProperty("Succeeded").GetBoolean();
        Console.WriteLine(
            succeeded
                ? $"Desktop E2E passed. Artifacts: {artifacts}"
                : $"Desktop E2E failed. Artifacts: {artifacts}");
        return succeeded ? 0 : 1;
    }

    private static ProcessStartInfo CreateStartInfo(string appPath, string configPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(appPath);
        startInfo.ArgumentList.Add("--desktop-e2e-config");
        startInfo.ArgumentList.Add(configPath);
        return startInfo;
    }

    private static bool TryReadOption(
        string[] args,
        string name,
        out string value)
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
        .Replace("synthetic-ignored-value", "[redacted]", StringComparison.Ordinal);
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
            string.Equals(
                request.Query["scenario"],
                "password-change",
                StringComparison.Ordinal)
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
            // The test result has already been written. Never retain the temporary browser profile
            // as an uploaded artifact; CI runners are ephemeral if native teardown still owns it.
        }
    }
}
