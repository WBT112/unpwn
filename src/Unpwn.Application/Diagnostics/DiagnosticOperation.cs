namespace Unpwn.Application.Diagnostics;

/// <summary>
/// Operations that may emit secret-safe diagnostics.
/// </summary>
public enum DiagnosticOperation
{
    VaultUnlock,
    RecoverySessionLoad,
    RecoveryAction,
    CredentialExport,
}
