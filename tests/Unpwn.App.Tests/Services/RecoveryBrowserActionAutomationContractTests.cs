using Unpwn.App.Services;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryBrowserActionAutomationContractTests
{
    [Fact]
    public void AssistedSyntheticContractIsValid()
    {
        var contract = new RecoveryBrowserActionAutomationContract(
            "synthetic/change-password/credential-insertion-v1",
            "synthetic",
            "change-password",
            AutomationSupport.Assisted,
            RecoveryBrowserContentMode.SyntheticTest,
            ["http://127.0.0.1:49990"],
            RecoveryBrowserAutomationEffect.AssistOnly);

        contract.Validate();
    }

    [Fact]
    public void AutomatedProviderMutationContractCanBeRepresented()
    {
        var contract = new RecoveryBrowserActionAutomationContract(
            "example/invalidate-sessions/v1",
            "example",
            "invalidate-sessions",
            AutomationSupport.Automated,
            RecoveryBrowserContentMode.Recovery,
            ["https://accounts.example.com"],
            RecoveryBrowserAutomationEffect.ProviderMutation);

        contract.Validate();
        Assert.Equal(AutomationSupport.Automated, contract.Support);
        Assert.Equal(RecoveryBrowserAutomationEffect.ProviderMutation, contract.Effect);
    }

    [Theory]
    [InlineData(AutomationSupport.None)]
    [InlineData(AutomationSupport.Navigation)]
    public void NonAutomationSupportCannotCreateBrowserAutomationContract(
        AutomationSupport support)
    {
        var contract = new RecoveryBrowserActionAutomationContract(
            "example/change-password/v1",
            "example",
            "change-password",
            support,
            RecoveryBrowserContentMode.Recovery,
            ["https://example.com"],
            RecoveryBrowserAutomationEffect.AssistOnly);

        Assert.Throws<InvalidOperationException>(contract.Validate);
    }

    [Fact]
    public void AssistedContractCannotAuthorizeProviderMutation()
    {
        var contract = new RecoveryBrowserActionAutomationContract(
            "example/change-password/v1",
            "example",
            "change-password",
            AutomationSupport.Assisted,
            RecoveryBrowserContentMode.Recovery,
            ["https://example.com"],
            RecoveryBrowserAutomationEffect.ProviderMutation);

        Assert.Throws<InvalidOperationException>(contract.Validate);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://user@example.com")]
    public void ProductionAutomationRequiresSafeHttpsOrigin(string origin)
    {
        var contract = new RecoveryBrowserActionAutomationContract(
            "example/change-password/v1",
            "example",
            "change-password",
            AutomationSupport.Automated,
            RecoveryBrowserContentMode.Recovery,
            [origin],
            RecoveryBrowserAutomationEffect.ProviderMutation);

        Assert.Throws<InvalidOperationException>(contract.Validate);
    }

    [Fact]
    public void SyntheticAutomationCannotEscapeLoopback()
    {
        var contract = new RecoveryBrowserActionAutomationContract(
            "synthetic/change-password/v1",
            "synthetic",
            "change-password",
            AutomationSupport.Assisted,
            RecoveryBrowserContentMode.SyntheticTest,
            ["https://example.test"],
            RecoveryBrowserAutomationEffect.AssistOnly);

        Assert.Throws<InvalidOperationException>(contract.Validate);
    }

    [Fact]
    public void CredentialInsertionUsesSharedAssistOnlyAutomationBoundary()
    {
        var insertion = new RecoveryBrowserCredentialInsertionContract(
            "synthetic",
            "change-password",
            RecoveryBrowserContentMode.SyntheticTest,
            ["http://127.0.0.1:49990"],
            "body[data-unpwn-provider='synthetic']",
            "[data-testid='new-password']",
            "[data-testid='confirm-password']",
            "[data-unpwn-stop-reason='mfa']",
            "[data-unpwn-stop-reason='captcha']",
            "[data-unpwn-stop-reason='email-link']");

        var contract = insertion.AsActionAutomationContract();

        contract.Validate();
        Assert.Equal(AutomationSupport.Assisted, contract.Support);
        Assert.Equal(RecoveryBrowserAutomationEffect.AssistOnly, contract.Effect);
        Assert.Contains("credential-insertion", contract.AdapterId, StringComparison.Ordinal);
    }
}
