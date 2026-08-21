using System.Text;
using System.Text.Json;
using Unpwn.Application.Recovery;

namespace Unpwn.App.Services;

public enum RecoveryBrowserCredentialAssistanceState
{
    Unavailable,
    ReadyForAuthorization,
    PausedForMfa,
    PausedForCaptcha,
    PausedForEmailLink,
    ManualGuidanceRequired,
    Inserted,
}

public enum RecoveryBrowserCredentialAssistanceFailureCode
{
    None,
    BrowserUnavailable,
    WrongOrigin,
    UnexpectedContent,
    InvocationFailed,
}

public sealed record RecoveryBrowserCredentialAssistanceResult(
    RecoveryBrowserCredentialAssistanceState State,
    RecoveryBrowserCredentialAssistanceFailureCode FailureCode)
{
    public bool Succeeded =>
        State == RecoveryBrowserCredentialAssistanceState.Inserted &&
        FailureCode == RecoveryBrowserCredentialAssistanceFailureCode.None;

    public static RecoveryBrowserCredentialAssistanceResult Ready { get; } =
        new(
            RecoveryBrowserCredentialAssistanceState.ReadyForAuthorization,
            RecoveryBrowserCredentialAssistanceFailureCode.None);

    public static RecoveryBrowserCredentialAssistanceResult Inserted { get; } =
        new(
            RecoveryBrowserCredentialAssistanceState.Inserted,
            RecoveryBrowserCredentialAssistanceFailureCode.None);

    public static RecoveryBrowserCredentialAssistanceResult Pause(
        RecoveryBrowserCredentialAssistanceState state) =>
        new(state, RecoveryBrowserCredentialAssistanceFailureCode.None);

    public static RecoveryBrowserCredentialAssistanceResult Failure(
        RecoveryBrowserCredentialAssistanceState state,
        RecoveryBrowserCredentialAssistanceFailureCode failureCode) =>
        new(state, failureCode);
}

public sealed record RecoveryBrowserCredentialInsertionContract(
    string ProviderId,
    string ActionDefinitionId,
    RecoveryBrowserContentMode ContentMode,
    string[] ExpectedOrigins,
    string PageEvidenceSelector,
    string NewPasswordSelector,
    string ConfirmPasswordSelector,
    string MfaSelector,
    string CaptchaSelector,
    string EmailLinkSelector)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderId) ||
            string.IsNullOrWhiteSpace(ActionDefinitionId) ||
            ExpectedOrigins.Length == 0 ||
            ExpectedOrigins.Any(string.IsNullOrWhiteSpace) ||
            string.IsNullOrWhiteSpace(PageEvidenceSelector) ||
            string.IsNullOrWhiteSpace(NewPasswordSelector) ||
            string.IsNullOrWhiteSpace(ConfirmPasswordSelector) ||
            string.IsNullOrWhiteSpace(MfaSelector) ||
            string.IsNullOrWhiteSpace(CaptchaSelector) ||
            string.IsNullOrWhiteSpace(EmailLinkSelector))
        {
            throw new InvalidOperationException("A credential-insertion contract requires explicit provider, action, origins, and selectors.");
        }

        if (ContentMode == RecoveryBrowserContentMode.Recovery &&
            ExpectedOrigins.Any(origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var parsed) ||
                parsed.Scheme != Uri.UriSchemeHttps ||
                parsed.UserInfo.Length != 0))
        {
            throw new InvalidOperationException("Production credential-insertion origins must be absolute HTTPS origins without user information.");
        }
    }
}

public interface IRecoveryBrowserCredentialAssistanceCatalog
{
    bool TryResolve(
        string providerId,
        string actionDefinitionId,
        bool isReviewedProviderWorkflow,
        RecoveryNavigationHandoff handoff,
        RecoveryBrowserContentMode contentMode,
        out RecoveryBrowserCredentialInsertionContract? contract);
}

public sealed class RepositoryRecoveryBrowserCredentialAssistanceCatalog
    : IRecoveryBrowserCredentialAssistanceCatalog
{
    public static RepositoryRecoveryBrowserCredentialAssistanceCatalog Instance { get; } = new();

    private RepositoryRecoveryBrowserCredentialAssistanceCatalog()
    {
    }

    public bool TryResolve(
        string providerId,
        string actionDefinitionId,
        bool isReviewedProviderWorkflow,
        RecoveryNavigationHandoff handoff,
        RecoveryBrowserContentMode contentMode,
        out RecoveryBrowserCredentialInsertionContract? contract)
    {
        ArgumentNullException.ThrowIfNull(handoff);
        contract = null;

        // The first adapter is deliberately synthetic-only. Production provider assistance must be
        // added as an explicit repository-reviewed adapter rather than inferred from arbitrary DOM.
        if (contentMode != RecoveryBrowserContentMode.SyntheticTest ||
            !string.Equals(providerId, "synthetic", StringComparison.OrdinalIgnoreCase) ||
            actionDefinitionId is not ("change-password" or "reset-password") ||
            !handoff.Destination.IsLoopback)
        {
            return false;
        }

        contract = new RecoveryBrowserCredentialInsertionContract(
            "synthetic",
            actionDefinitionId,
            RecoveryBrowserContentMode.SyntheticTest,
            [.. handoff.ExpectedOrigins],
            "body[data-unpwn-provider='synthetic'][data-unpwn-workflow='password-change']",
            "[data-testid='new-password']",
            "[data-testid='confirm-password']",
            "[data-unpwn-stop-reason='mfa']",
            "[data-unpwn-stop-reason='captcha']",
            "[data-unpwn-stop-reason='email-link']");
        contract.Validate();
        return true;
    }
}

internal static class RecoveryBrowserCredentialScript
{
    private const string Ready = "unpwn-ready";
    private const string Inserted = "unpwn-inserted";
    private const string Mfa = "unpwn-paused-mfa";
    private const string Captcha = "unpwn-paused-captcha";
    private const string EmailLink = "unpwn-paused-email-link";
    private const string Manual = "unpwn-manual";

    public static string BuildInspection(RecoveryBrowserCredentialInsertionContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Validate();
        return BuildBody(contract, secretLiteral: null);
    }

    public static string BuildInsertion(
        RecoveryBrowserCredentialInsertionContract contract,
        ReadOnlyMemory<byte> secretUtf8)
    {
        ArgumentNullException.ThrowIfNull(contract);
        contract.Validate();
        if (secretUtf8.IsEmpty)
        {
            throw new ArgumentException("The credential secret cannot be empty.", nameof(secretUtf8));
        }

        var secret = Encoding.UTF8.GetString(secretUtf8.Span);
        var secretLiteral = JsonSerializer.Serialize(secret);
        return BuildBody(contract, secretLiteral);
    }

    public static RecoveryBrowserCredentialAssistanceResult Parse(string? raw)
    {
        var value = Normalize(raw);
        return value switch
        {
            Ready => RecoveryBrowserCredentialAssistanceResult.Ready,
            Inserted => RecoveryBrowserCredentialAssistanceResult.Inserted,
            Mfa => RecoveryBrowserCredentialAssistanceResult.Pause(
                RecoveryBrowserCredentialAssistanceState.PausedForMfa),
            Captcha => RecoveryBrowserCredentialAssistanceResult.Pause(
                RecoveryBrowserCredentialAssistanceState.PausedForCaptcha),
            EmailLink => RecoveryBrowserCredentialAssistanceResult.Pause(
                RecoveryBrowserCredentialAssistanceState.PausedForEmailLink),
            Manual => RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                RecoveryBrowserCredentialAssistanceFailureCode.UnexpectedContent),
            _ => RecoveryBrowserCredentialAssistanceResult.Failure(
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired,
                RecoveryBrowserCredentialAssistanceFailureCode.InvocationFailed),
        };
    }

    private static string BuildBody(
        RecoveryBrowserCredentialInsertionContract contract,
        string? secretLiteral)
    {
        var page = JsonSerializer.Serialize(contract.PageEvidenceSelector);
        var password = JsonSerializer.Serialize(contract.NewPasswordSelector);
        var confirmation = JsonSerializer.Serialize(contract.ConfirmPasswordSelector);
        var mfa = JsonSerializer.Serialize(contract.MfaSelector);
        var captcha = JsonSerializer.Serialize(contract.CaptchaSelector);
        var email = JsonSerializer.Serialize(contract.EmailLinkSelector);
        var insertion = secretLiteral is null
            ? $"return '{Ready}';"
            : $$"""
                const value = {{secretLiteral}};
                const first = document.querySelector({{password}});
                const second = document.querySelector({{confirmation}});
                first.value = value;
                second.value = value;
                for (const element of [first, second]) {
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                }
                return '{{Inserted}}';
                """;

        return $$"""
            (() => {
                const count = selector => document.querySelectorAll(selector).length;
                if (count({{mfa}}) > 0) return '{{Mfa}}';
                if (count({{captcha}}) > 0) return '{{Captcha}}';
                if (count({{email}}) > 0) return '{{EmailLink}}';
                if (count({{page}}) !== 1 || count({{password}}) !== 1 || count({{confirmation}}) !== 1) {
                    return '{{Manual}}';
                }
                {{insertion}}
            })()
            """;
    }

    private static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        return trimmed;
    }
}
