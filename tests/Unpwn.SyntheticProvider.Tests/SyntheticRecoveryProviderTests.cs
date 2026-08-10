using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Unpwn.Application.Credentials;
using Unpwn.Application.Recovery;
using Unpwn.Automation.Recovery;
using Unpwn.Core;
using Xunit;

namespace Unpwn.SyntheticProvider.Tests;

public sealed class SyntheticRecoveryProviderTests
{
    private static readonly GeneratedCredentialReference CredentialReference =
        new(Guid.Parse("14f7988d-76e5-465a-a9ee-624aeb87131c"), Guid.Parse("8744c111-29a7-40f1-826b-7556e7d2fa88"));

    [Theory]
    [InlineData("login", "/login")]
    [InlineData("reauth", "/reauth")]
    [InlineData("password-change", "/settings/password")]
    [InlineData("password-reset", "/forgot-password")]
    [InlineData("email-link-handoff", "/reset-link")]
    [InlineData("mfa-pause", "/mfa")]
    [InlineData("captcha-pause", "/captcha")]
    [InlineData("expired-link", "/reset-link/expired")]
    [InlineData("provider-error", "/error")]
    [InlineData("unexpected-content", "/unexpected")]
    [InlineData("manual-recovery", "/manual-recovery")]
    public async Task SyntheticProviderExposesDeterministicRecoveryScenario(string scenario, string path)
    {
        await using SyntheticRecoveryProvider provider = await SyntheticRecoveryProvider.StartAsync();
        using HttpClient client = new() { BaseAddress = provider.BaseAddress };

        using HttpResponseMessage response = await client.GetAsync($"{path}?scenario={scenario}");
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, body);
        Assert.Contains("UNPWN_SYNTHETIC_PROVIDER", body, StringComparison.Ordinal);
        Assert.Contains(scenario, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionModeRejectsHeadlessBeforeLaunchingBrowser()
    {
        var credentials = new CountingCredentialRepository();
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(credentials);

        BrowserAssistanceResult result = await assistance.StartAsync(
            new BrowserAssistanceLaunchOptions(
                new Uri("http://127.0.0.1:1/settings/password"),
                BrowserAssistanceExecutionMode.Production,
                Headless: true),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BrowserAssistanceFailureCode.InvalidConfiguration, result.FailureCode);
        Assert.Equal(0, credentials.ReadCount);
    }

    [Fact]
    public async Task SyntheticTestModeRejectsNonLoopbackTarget()
    {
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(new CountingCredentialRepository());

        BrowserAssistanceResult result = await assistance.StartAsync(
            new BrowserAssistanceLaunchOptions(
                new Uri("https://example.test/settings/password"),
                BrowserAssistanceExecutionMode.SyntheticTest,
                Headless: true,
                UsesSyntheticCredentials: true),
            CancellationToken.None);

        Assert.Equal(BrowserAssistanceFailureCode.InvalidConfiguration, result.FailureCode);
    }

    [Fact]
    public async Task SyntheticTestModeRejectsNonHttpLoopbackTarget()
    {
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(new CountingCredentialRepository());

        BrowserAssistanceResult result = await assistance.StartAsync(
            new BrowserAssistanceLaunchOptions(
                new Uri("file://localhost/tmp/synthetic-provider.html"),
                BrowserAssistanceExecutionMode.SyntheticTest,
                Headless: true,
                UsesSyntheticCredentials: true),
            CancellationToken.None);

        Assert.Equal(BrowserAssistanceFailureCode.InvalidConfiguration, result.FailureCode);
    }

    [Fact]
    public async Task SyntheticTestModeRequiresSyntheticCredentialDeclaration()
    {
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(new CountingCredentialRepository());

        BrowserAssistanceResult result = await assistance.StartAsync(
            new BrowserAssistanceLaunchOptions(
                new Uri("http://127.0.0.1:1/settings/password"),
                BrowserAssistanceExecutionMode.SyntheticTest,
                Headless: true),
            CancellationToken.None);

        Assert.Equal(BrowserAssistanceFailureCode.InvalidConfiguration, result.FailureCode);
    }

    [Theory]
    [InlineData(BrowserAssistanceExecutionMode.Production, false, true)]
    [InlineData(BrowserAssistanceExecutionMode.SyntheticTest, true, false)]
    public async Task ArtifactCaptureRequiresSyntheticTestModeAndSyntheticCredentials(
        BrowserAssistanceExecutionMode mode,
        bool headless,
        bool usesSyntheticCredentials)
    {
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(new CountingCredentialRepository());

        BrowserAssistanceResult result = await assistance.StartAsync(
            new BrowserAssistanceLaunchOptions(
                new Uri("http://127.0.0.1:1/settings/password"),
                mode,
                headless,
                CaptureArtifacts: true,
                UsesSyntheticCredentials: usesSyntheticCredentials),
            CancellationToken.None);

        Assert.Equal(BrowserAssistanceFailureCode.InvalidConfiguration, result.FailureCode);
    }

    [Fact]
    public async Task AuthorizedPasswordChangeRetrievesCredentialOnlyAtSubmission()
    {
        await using SyntheticRecoveryProvider provider = await SyntheticRecoveryProvider.StartAsync();
        var credentials = new CountingCredentialRepository("UNPWN_TEST_SECRET_BROWSER_16");
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(credentials);

        BrowserAssistanceResult prepared = await assistance.StartAsync(
            TestOptions(provider, "/settings/password?scenario=password-change"),
            CancellationToken.None);

        Assert.True(prepared.Succeeded);
        Assert.Equal(BrowserAssistanceState.ReadyForAuthorization, prepared.State);
        Assert.Equal(0, credentials.ReadCount);

        BrowserAssistanceResult submitted = await assistance.SubmitPasswordChangeAsync(
            CredentialReference,
            new SensitiveBrowserSubmissionAuthorization(Guid.NewGuid(), Approved: true),
            CancellationToken.None);

        Assert.True(submitted.Succeeded);
        Assert.Equal(BrowserAssistanceState.Submitted, submitted.State);
        Assert.Equal(1, credentials.ReadCount);
        Assert.DoesNotContain("UNPWN_TEST_SECRET_", submitted.ToString(), StringComparison.Ordinal);
        Assert.Throws<ObjectDisposedException>(() => _ = credentials.LastLease!.SecretUtf8);
    }

    [Fact]
    public async Task SubmissionWithoutExplicitAuthorizationDoesNotReadCredential()
    {
        await using SyntheticRecoveryProvider provider = await SyntheticRecoveryProvider.StartAsync();
        var credentials = new CountingCredentialRepository();
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(credentials);
        await assistance.StartAsync(
            TestOptions(provider, "/settings/password?scenario=password-change"),
            CancellationToken.None);

        BrowserAssistanceResult result = await assistance.SubmitPasswordChangeAsync(
            CredentialReference,
            new SensitiveBrowserSubmissionAuthorization(Guid.NewGuid(), Approved: false),
            CancellationToken.None);

        Assert.Equal(BrowserAssistanceFailureCode.AuthorizationRequired, result.FailureCode);
        Assert.Equal(0, credentials.ReadCount);
    }

    [Theory]
    [InlineData("/mfa?scenario=mfa-pause", BrowserAssistanceState.PausedForMfa)]
    [InlineData("/captcha?scenario=captcha-pause", BrowserAssistanceState.PausedForCaptcha)]
    [InlineData("/reset-link?scenario=email-link-handoff", BrowserAssistanceState.PausedForEmailLink)]
    public async Task BlockingPagePausesWithoutReadingCredential(
        string path,
        BrowserAssistanceState expectedState)
    {
        await using SyntheticRecoveryProvider provider = await SyntheticRecoveryProvider.StartAsync();
        var credentials = new CountingCredentialRepository();
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(credentials);

        BrowserAssistanceResult result = await assistance.StartAsync(
            TestOptions(provider, path),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedState, result.State);
        Assert.Equal(BrowserAssistanceFailureCode.Paused, result.FailureCode);
        Assert.False(result.RequiresManualGuidance);
        Assert.Equal(0, credentials.ReadCount);
    }

    [Fact]
    public async Task UnexpectedContentStopsWithManualGuidance()
    {
        await using SyntheticRecoveryProvider provider = await SyntheticRecoveryProvider.StartAsync();
        var credentials = new CountingCredentialRepository();
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(credentials);

        BrowserAssistanceResult result = await assistance.StartAsync(
            TestOptions(provider, "/unexpected?scenario=unexpected-content"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BrowserAssistanceState.ManualGuidanceRequired, result.State);
        Assert.Equal(BrowserAssistanceFailureCode.UnexpectedContent, result.FailureCode);
        Assert.True(result.RequiresManualGuidance);
        Assert.Equal(0, credentials.ReadCount);
    }

    [Fact]
    public async Task RedirectAwayFromLoopbackIsBlockedBeforeCredentialRead()
    {
        await using SyntheticRecoveryProvider provider = await SyntheticRecoveryProvider.StartAsync();
        var credentials = new CountingCredentialRepository();
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(credentials);

        BrowserAssistanceResult result = await assistance.StartAsync(
            TestOptions(provider, "/external-redirect"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(BrowserAssistanceState.ManualGuidanceRequired, result.State);
        Assert.Equal(BrowserAssistanceFailureCode.NavigationFailed, result.FailureCode);
        Assert.Equal(0, credentials.ReadCount);
    }

    [Fact]
    public async Task UserCanPauseResumeAndAbortWithoutSubmitting()
    {
        await using SyntheticRecoveryProvider provider = await SyntheticRecoveryProvider.StartAsync();
        var credentials = new CountingCredentialRepository();
        await using var assistance = new PlaywrightRecoveryBrowserAssistance(credentials);
        await assistance.StartAsync(
            TestOptions(provider, "/settings/password?scenario=password-change"),
            CancellationToken.None);

        BrowserAssistanceResult paused = await assistance.PauseAsync(CancellationToken.None);
        BrowserAssistanceResult resume = await assistance.ResumeAsync(CancellationToken.None);
        BrowserAssistanceResult aborted = await assistance.AbortAsync(CancellationToken.None);

        Assert.Equal(BrowserAssistanceState.PausedByUser, paused.State);
        Assert.Equal(BrowserAssistanceState.ReadyForAuthorization, resume.State);
        Assert.Equal(BrowserAssistanceState.Aborted, aborted.State);
        Assert.Equal(0, credentials.ReadCount);
    }

    private static BrowserAssistanceLaunchOptions TestOptions(
        SyntheticRecoveryProvider provider,
        string path) =>
        new(
            new Uri(provider.BaseAddress, path),
            BrowserAssistanceExecutionMode.SyntheticTest,
            Headless: true,
            CaptureArtifacts: false,
            UsesSyntheticCredentials: true);
}

internal sealed class SyntheticRecoveryProvider : IAsyncDisposable
{
    private readonly WebApplication app;

    private SyntheticRecoveryProvider(WebApplication app, Uri baseAddress)
    {
        this.app = app;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public static async Task<SyntheticRecoveryProvider> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();

        MapScenario(app, "/login", "login");
        MapScenario(app, "/reauth", "reauth");
        MapPasswordChangeScenario(app);
        MapScenario(app, "/forgot-password", "password-reset");
        MapStopScenario(app, "/reset-link", "email-link-handoff", "email-link");
        MapStopScenario(app, "/mfa", "mfa-pause", "mfa");
        MapStopScenario(app, "/captcha", "captcha-pause", "captcha");
        MapScenario(app, "/reset-link/expired", "expired-link");
        MapScenario(app, "/error", "provider-error");
        MapScenario(app, "/unexpected", "unexpected-content");
        MapScenario(app, "/manual-recovery", "manual-recovery");
        app.MapGet("/external-redirect", () => Results.Redirect("https://example.test/password-change"));

        await app.StartAsync();
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.Single()
            ?? throw new InvalidOperationException("The synthetic provider did not publish a loopback address.");
        return new SyntheticRecoveryProvider(app, new Uri(address));
    }

    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    private static void MapScenario(WebApplication app, string path, string expectedScenario) =>
        app.MapGet(path, (HttpRequest request) =>
        {
            string scenario = request.Query["scenario"].ToString();
            return string.Equals(scenario, expectedScenario, StringComparison.Ordinal)
                ? Results.Text($"UNPWN_SYNTHETIC_PROVIDER scenario={expectedScenario}", "text/plain")
                : Results.BadRequest("Scenario query parameter did not match the deterministic route.");
        });

    private static void MapPasswordChangeScenario(WebApplication app) =>
        app.MapGet("/settings/password", (HttpRequest request) =>
            string.Equals(request.Query["scenario"], "password-change", StringComparison.Ordinal)
                ? Results.Content(
                    """
                    <!doctype html>
                    <html lang="en">
                    <body data-unpwn-provider="synthetic" data-unpwn-workflow="password-change">
                      <p>UNPWN_SYNTHETIC_PROVIDER scenario=password-change</p>
                      <label>New password <input type="password" data-testid="new-password"></label>
                      <label>Confirm password <input type="password" data-testid="confirm-password"></label>
                      <button type="button" data-testid="submit-password-change">Change password</button>
                      <script>
                        document.querySelector('[data-testid="submit-password-change"]').addEventListener('click', () => {
                          const password = document.querySelector('[data-testid="new-password"]').value;
                          const confirmation = document.querySelector('[data-testid="confirm-password"]').value;
                          if (password.length > 0 && password === confirmation) {
                            const outcome = document.createElement('p');
                            outcome.setAttribute('data-unpwn-outcome', 'submitted');
                            outcome.textContent = 'Synthetic password change submitted.';
                            document.body.appendChild(outcome);
                          }
                        });
                      </script>
                    </body>
                    </html>
                    """,
                    "text/html")
                : Results.BadRequest("Scenario query parameter did not match the deterministic route."));

    private static void MapStopScenario(
        WebApplication app,
        string path,
        string expectedScenario,
        string stopReason) =>
        app.MapGet(path, (HttpRequest request) =>
            string.Equals(request.Query["scenario"], expectedScenario, StringComparison.Ordinal)
                ? Results.Content(
                    $"""
                    <!doctype html>
                    <html lang="en">
                    <body data-unpwn-stop-reason="{stopReason}">
                      UNPWN_SYNTHETIC_PROVIDER scenario={expectedScenario}
                    </body>
                    </html>
                    """,
                    "text/html")
                : Results.BadRequest("Scenario query parameter did not match the deterministic route."));
}

internal sealed class CountingCredentialRepository(string secret = "synthetic-browser-password")
    : IGeneratedCredentialRepository
{
    private readonly byte[] _secret = System.Text.Encoding.UTF8.GetBytes(secret);

    public bool IsUnlocked => true;

    public int ReadCount { get; private set; }

    public CredentialSecretLease? LastLease { get; private set; }

    public Task<CredentialSecretLease?> ReadSecretAsync(
        GeneratedCredentialReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        LastLease = new CredentialSecretLease([.. _secret]);
        return Task.FromResult<CredentialSecretLease?>(LastLease);
    }

    public Task<GeneratedCredentialCreationResult> GenerateAsync(Guid accountId, CredentialGenerationPolicy policy, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialMetadata?> GetMetadataAsync(GeneratedCredentialReference reference, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialOperationResult> MarkUsedAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialOperationResult> ConfirmAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialBatchResult> MarkExportedAsync(IReadOnlyCollection<GeneratedCredentialReference> references, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialOperationResult> ConfirmPasswordManagerImportAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialOperationResult> RevokePasswordManagerImportConfirmationAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialOperationResult> PostponePasswordManagerImportConfirmationAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialOperationResult> ConfirmPlaintextExportCleanupAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<GeneratedCredentialOperationResult> DeleteAsync(GeneratedCredentialReference reference, Guid operationId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
