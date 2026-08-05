using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unpwn.SyntheticProvider.Tests;

public sealed class SyntheticRecoveryProviderTests
{
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
        MapScenario(app, "/settings/password", "password-change");
        MapScenario(app, "/forgot-password", "password-reset");
        MapScenario(app, "/reset-link", "email-link-handoff");
        MapScenario(app, "/mfa", "mfa-pause");
        MapScenario(app, "/captcha", "captcha-pause");
        MapScenario(app, "/reset-link/expired", "expired-link");
        MapScenario(app, "/error", "provider-error");
        MapScenario(app, "/unexpected", "unexpected-content");
        MapScenario(app, "/manual-recovery", "manual-recovery");

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
}
