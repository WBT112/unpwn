using Unpwn.Application.Recovery;
using Xunit;

namespace Unpwn.Application.Tests;

public sealed class RecoveryBrowserSecurityBoundaryTests
{
    [Fact]
    public void ReviewedHttpsHandoffAllowsOnlyExactExpectedOrigins()
    {
        var handoff = Handoff(
            new Uri("https://accounts.example.test/recovery/start"),
            "https://accounts.example.test",
            "https://identity.example.test");

        var created = RecoveryBrowserSecurityBoundary.TryCreate(
            handoff,
            RecoveryBrowserContentMode.Recovery,
            out var boundary);

        Assert.True(created);
        Assert.Equal(
            RecoveryBrowserBoundaryDecisionCode.Allowed,
            boundary!.EvaluateTopLevelNavigation(
                new Uri("https://identity.example.test/continue?opaque=value")).Code);
        Assert.Equal(
            RecoveryBrowserBoundaryDecisionCode.UnexpectedOrigin,
            boundary.EvaluateTopLevelNavigation(
                new Uri("https://child.accounts.example.test/recovery")).Code);
    }

    [Theory]
    [InlineData("file:///tmp/provider.html")]
    [InlineData("data:text/html,provider")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:help@example.test")]
    [InlineData("https://user:password@accounts.example.test/recovery")]
    public void UnsafeTopLevelNavigationFailsClosed(string destination)
    {
        Assert.True(RecoveryBrowserSecurityBoundary.TryCreate(
            Handoff(new Uri("https://accounts.example.test/recovery")),
            RecoveryBrowserContentMode.Recovery,
            out var boundary));

        var decision = boundary!.EvaluateTopLevelNavigation(new Uri(destination));

        Assert.False(decision.IsAllowed);
        Assert.Equal(RecoveryBrowserBoundaryDecisionCode.UnsupportedScheme, decision.Code);
        Assert.Null(decision.VisibleOrigin);
    }

    [Fact]
    public void HttpIsLimitedToExplicitSyntheticLoopbackMode()
    {
        var handoff = Handoff(new Uri("http://127.0.0.1:43721/password-change"));

        Assert.False(RecoveryBrowserSecurityBoundary.TryCreate(
            handoff,
            RecoveryBrowserContentMode.Recovery,
            out _));
        Assert.True(RecoveryBrowserSecurityBoundary.TryCreate(
            handoff,
            RecoveryBrowserContentMode.SyntheticTest,
            out var boundary));
        Assert.True(boundary!.EvaluateTopLevelNavigation(
            new Uri("http://127.0.0.1:43721/complete")).IsAllowed);
        Assert.False(RecoveryBrowserSecurityBoundary.TryCreate(
            Handoff(new Uri("http://example.test/recovery")),
            RecoveryBrowserContentMode.SyntheticTest,
            out _));
    }

    [Fact]
    public void InvalidOrUnconfirmedHandoffIsRejected()
    {
        var unconfirmed = Handoff(
            new Uri("https://accounts.example.test/recovery")) with
        {
            RequiresVisibleConfirmation = false,
        };
        var mismatched = Handoff(
            new Uri("https://accounts.example.test/recovery"),
            "https://identity.example.test") with
        {
            ExpectedOrigin = "https://identity.example.test",
        };

        Assert.False(RecoveryBrowserSecurityBoundary.TryCreate(
            unconfirmed,
            RecoveryBrowserContentMode.Recovery,
            out _));
        Assert.False(RecoveryBrowserSecurityBoundary.TryCreate(
            mismatched,
            RecoveryBrowserContentMode.Recovery,
            out _));
    }

    [Fact]
    public void NonNavigationCapabilitiesAreDeniedByDefault()
    {
        Assert.Equal(
            RecoveryBrowserBoundaryDecisionCode.PopupDenied,
            RecoveryBrowserSecurityBoundary.DenyPopup().Code);
        Assert.Equal(
            RecoveryBrowserBoundaryDecisionCode.DownloadDenied,
            RecoveryBrowserSecurityBoundary.DenyDownload().Code);
        Assert.Equal(
            RecoveryBrowserBoundaryDecisionCode.PermissionDenied,
            RecoveryBrowserSecurityBoundary.DenyPermission().Code);
        Assert.Equal(
            RecoveryBrowserBoundaryDecisionCode.ExternalProtocolDenied,
            RecoveryBrowserSecurityBoundary.DenyExternalProtocol().Code);
    }

    private static RecoveryNavigationHandoff Handoff(
        Uri destination,
        params string[] additionalOrigins)
    {
        var origin = destination.GetLeftPart(UriPartial.Authority);
        return new RecoveryNavigationHandoff(
            destination,
            origin,
            [origin, .. additionalOrigins],
            RecoveryLocationResolutionSource.ProviderDefined,
            RequiresVisibleConfirmation: true);
    }
}
