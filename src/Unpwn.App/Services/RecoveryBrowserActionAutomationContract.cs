using Unpwn.Core;

namespace Unpwn.App.Services;

public enum RecoveryBrowserAutomationEffect
{
    AssistOnly,
    ProviderMutation,
}

public sealed record RecoveryBrowserActionAutomationContract(
    string AdapterId,
    string ProviderId,
    string ActionDefinitionId,
    AutomationSupport Support,
    RecoveryBrowserContentMode ContentMode,
    IReadOnlyList<string> ExpectedOrigins,
    RecoveryBrowserAutomationEffect Effect)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AdapterId) ||
            string.IsNullOrWhiteSpace(ProviderId) ||
            string.IsNullOrWhiteSpace(ActionDefinitionId) ||
            ExpectedOrigins.Count == 0 ||
            ExpectedOrigins.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "A browser automation contract requires an adapter, provider, action, and explicit expected origins.");
        }

        if (Support is not AutomationSupport.Assisted and not AutomationSupport.Automated)
        {
            throw new InvalidOperationException(
                "A browser automation contract must represent an ASSISTED or AUTOMATED action capability.");
        }

        if (Effect == RecoveryBrowserAutomationEffect.ProviderMutation &&
            Support != AutomationSupport.Automated)
        {
            throw new InvalidOperationException(
                "Provider-mutating browser automation requires AUTOMATED support.");
        }

        foreach (var origin in ExpectedOrigins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed) || parsed.UserInfo.Length != 0)
            {
                throw new InvalidOperationException(
                    "Browser automation origins must be absolute and must not contain user information.");
            }

            if (ContentMode == RecoveryBrowserContentMode.Recovery && parsed.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException(
                    "Production browser automation origins must use HTTPS.");
            }

            if (ContentMode == RecoveryBrowserContentMode.SyntheticTest &&
                (!parsed.IsLoopback || parsed.Scheme is not (Uri.UriSchemeHttp or Uri.UriSchemeHttps)))
            {
                throw new InvalidOperationException(
                    "Synthetic browser automation origins must remain on HTTP(S) loopback.");
            }
        }
    }
}

public static class RecoveryBrowserActionAutomationContractExtensions
{
    public static RecoveryBrowserActionAutomationContract AsActionAutomationContract(
        this RecoveryBrowserCredentialInsertionContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return new RecoveryBrowserActionAutomationContract(
            $"{contract.ProviderId}/{contract.ActionDefinitionId}/credential-insertion-v1",
            contract.ProviderId,
            contract.ActionDefinitionId,
            AutomationSupport.Assisted,
            contract.ContentMode,
            contract.ExpectedOrigins,
            RecoveryBrowserAutomationEffect.AssistOnly);
    }
}
