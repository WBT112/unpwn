namespace Unpwn.Application.Diagnostics;

/// <summary>
/// Operations that may emit secret-safe diagnostics.
/// </summary>
public enum DiagnosticOperation
{
    VaultUnlock,
    VaultLock,
    VaultPasswordChange,
    RecoverySessionLoad,
    RecoveryAction,
    CredentialExport,
}
