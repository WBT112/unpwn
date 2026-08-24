namespace Unpwn.App;

internal static class DesktopE2EScenarioCatalog
{
    public const string Golden = "golden";
    public const string SafetyStopAndRetry = "safety-stop-and-retry";
    public const string VaultWrongPasswordAndResume = "vault-wrong-password-and-resume";
    public const string ImportCorrectionAndRetry = "import-correction-and-retry";
    public const string DeferAccount = "defer-account";
    public const string BrowserClosePreservesRecovery = "browser-close-preserves-recovery";
    public const string ReviewedBrowserStepHierarchy = "reviewed-browser-step-hierarchy";
    public const string BrowserStartupFailureFallback = "browser-startup-failure-fallback";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Golden,
        SafetyStopAndRetry,
        VaultWrongPasswordAndResume,
        ImportCorrectionAndRetry,
        DeferAccount,
        BrowserClosePreservesRecovery,
        ReviewedBrowserStepHierarchy,
        BrowserStartupFailureFallback,
    };

    public static bool IsSupported(string? scenario) =>
        scenario is not null && All.Contains(scenario);
}
