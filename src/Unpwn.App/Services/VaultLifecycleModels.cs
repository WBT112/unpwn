namespace Unpwn.App.Services;

public enum VaultLifecycleStatus
{
    NoVault,
    Unlocked,
    Locked,
}

public enum VaultLockReason
{
    None,
    User,
    Inactivity,
    ApplicationFailure,
}

public enum VaultOperationFailureCode
{
    None,
    InvalidInput,
    AlreadyExists,
    NotFound,
    AccessDenied,
    UnsupportedVersion,
    AuthenticationOrIntegrity,
    IoFailure,
    CurrentVaultInUse,
}

public sealed record VaultOperationResult(
    bool Succeeded,
    VaultOperationFailureCode FailureCode)
{
    public static VaultOperationResult Success { get; } = new(true, VaultOperationFailureCode.None);

    public static VaultOperationResult Failure(VaultOperationFailureCode failureCode)
    {
        if (failureCode == VaultOperationFailureCode.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failureCode));
        }

        return new VaultOperationResult(false, failureCode);
    }
}

public sealed record RecentVaultReference(
    string Path,
    string DisplayName,
    DateTimeOffset LastOpenedAt);

public sealed record VaultLifecycleSnapshot(
    VaultLifecycleStatus Status,
    string? CurrentPath,
    string? CurrentDisplayName,
    VaultLockReason LastLockReason,
    bool IsInactivityWarningVisible,
    DateTimeOffset? InactivityLocksAt)
{
    public static VaultLifecycleSnapshot Empty { get; } = new(
        VaultLifecycleStatus.NoVault,
        null,
        null,
        VaultLockReason.None,
        IsInactivityWarningVisible: false,
        InactivityLocksAt: null);

    public bool IsUnlocked => Status == VaultLifecycleStatus.Unlocked;

    public bool CanUnlockCurrent => Status == VaultLifecycleStatus.Locked && CurrentPath is not null;
}

public sealed record VaultInactivityPolicy(
    TimeSpan WarningAfter,
    TimeSpan LockAfter)
{
    public static VaultInactivityPolicy Default { get; } = new(
        TimeSpan.FromMinutes(14),
        TimeSpan.FromMinutes(15));

    public void Validate()
    {
        if (WarningAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(WarningAfter));
        }

        if (LockAfter <= WarningAfter)
        {
            throw new ArgumentOutOfRangeException(nameof(LockAfter));
        }
    }
}
