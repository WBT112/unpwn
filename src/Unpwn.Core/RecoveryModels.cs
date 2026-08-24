namespace Unpwn.Core;

public enum AccountCriticality
{
    Routine = 0,
    Important = 1,
    Critical = 2,
}

public enum AccountRecoveryStatus
{
    Open,
    InProgress,
    FullyReviewed,
    NotFullySecured,
    AccessNotRestored,
}

public enum RecoveryActionStatus
{
    Open,
    InProgress,
    Blocked,
    NeedsUserAction,
    Completed,
    Failed,
    NotApplicable,
}

public enum NotApplicableDisposition
{
    TrulyNotApplicable,
    UnresolvedRisk,
}
