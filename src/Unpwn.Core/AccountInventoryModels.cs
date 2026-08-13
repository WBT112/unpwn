namespace Unpwn.Core;

/// <summary>
/// Describes when an account should be recovered. Provider workflows remain the
/// independent source of truth for how the recovery is performed.
/// </summary>
public enum AccountRecoveryCategory
{
    Email = 0,
    Critical = 1,
    NonCritical = 2,
    Unknown = 3,
}

public enum AccountInventoryPlanReasonCode
{
    EmailCategory,
    CriticalCategory,
    UnknownCategory,
    NonCriticalCategory,
}

public sealed record AccountInventoryEntry(
    Guid Id,
    string ProviderId,
    string? AccountName,
    string? LoginIdentifier,
    string? AccountUrl,
    AccountRecoveryCategory SuggestedCategory,
    string ClassificationCatalogVersion,
    AccountRecoveryCategory? ConfirmedCategory,
    long? CategoryConfirmedRevision,
    DateTimeOffset UpdatedAt)
{
    public AccountRecoveryCategory EffectiveCategory => ConfirmedCategory ?? SuggestedCategory;

    public bool IsCategorized => ConfirmedCategory.HasValue;

    public AccountCriticality DashboardCriticality => EffectiveCategory switch
    {
        AccountRecoveryCategory.Critical => AccountCriticality.Critical,
        AccountRecoveryCategory.Email => AccountCriticality.Important,
        AccountRecoveryCategory.Unknown or AccountRecoveryCategory.NonCritical => AccountCriticality.Routine,
        _ => throw new ArgumentOutOfRangeException(nameof(EffectiveCategory)),
    };

    public void Validate()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("An inventory account requires a non-empty identifier.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ClassificationCatalogVersion);
        if (ProviderId.Trim().Length > 160 || AccountName?.Trim().Length > 200 ||
            LoginIdentifier?.Trim().Length > 320 || AccountUrl?.Trim().Length > 2048 ||
            ClassificationCatalogVersion.Trim().Length > 80)
        {
            throw new InvalidOperationException("An inventory account contains an overlong field.");
        }

        if (string.IsNullOrWhiteSpace(AccountName) && string.IsNullOrWhiteSpace(LoginIdentifier))
        {
            throw new InvalidOperationException("An inventory account requires a name or login identifier.");
        }

        if (!Enum.IsDefined(SuggestedCategory) ||
            (ConfirmedCategory.HasValue && !Enum.IsDefined(ConfirmedCategory.Value)))
        {
            throw new InvalidOperationException("An inventory account contains an unknown recovery category.");
        }

        if (!string.IsNullOrWhiteSpace(AccountUrl) &&
            (!Uri.TryCreate(AccountUrl, UriKind.Absolute, out var uri) ||
             uri.Scheme is not ("https" or "http") ||
             string.IsNullOrWhiteSpace(uri.Host) ||
             !string.IsNullOrEmpty(uri.UserInfo)))
        {
            throw new InvalidOperationException(
                "An account URL must be an absolute HTTP or HTTPS URL without embedded credentials.");
        }

        if (ConfirmedCategory.HasValue != CategoryConfirmedRevision.HasValue ||
            CategoryConfirmedRevision is < 1)
        {
            throw new InvalidOperationException(
                "An explicit account category requires the inventory revision at which it was confirmed.");
        }
    }

    public AccountInventoryEntry NormalizeAndClassify(DateTimeOffset occurredAt)
    {
        var providerId = ProviderId.Trim();
        var accountUrl = Normalize(AccountUrl);
        var classification = RepositoryAccountClassificationCatalog.Classify(providerId, accountUrl);
        var normalized = this with
        {
            ProviderId = providerId,
            AccountName = Normalize(AccountName),
            LoginIdentifier = Normalize(LoginIdentifier),
            AccountUrl = accountUrl,
            SuggestedCategory = classification.Category,
            ClassificationCatalogVersion = classification.CatalogVersion,
            UpdatedAt = occurredAt,
        };
        normalized.Validate();
        return normalized;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AccountInventoryPlanItem(
    Guid AccountId,
    string ProviderId,
    AccountRecoveryCategory Category,
    AccountInventoryPlanReasonCode ReasonCode,
    int Order);

public sealed record AccountInventoryPlan(AccountInventoryPlanItem[] Items)
{
    public AccountInventoryPlanItem? Recommended => Items.OrderBy(item => item.Order).FirstOrDefault();
}

public sealed record AccountInventoryState(
    Guid SessionId,
    long Revision,
    DateTimeOffset UpdatedAt,
    AccountInventoryEntry[] Accounts)
{
    public static AccountInventoryState Empty(Guid sessionId, DateTimeOffset occurredAt)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("An account inventory requires a recovery session.", nameof(sessionId));
        }

        return new AccountInventoryState(sessionId, 0, occurredAt, []);
    }

    public AccountInventoryState ReplaceAccounts(
        IEnumerable<AccountInventoryEntry> accounts,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentOutOfRangeException.ThrowIfLessThan(occurredAt, UpdatedAt);

        AccountInventoryEntry[] materialized =
        [
            .. accounts.Select(account => account.NormalizeAndClassify(occurredAt)),
        ];
        if (materialized.Select(account => account.Id).Distinct().Count() != materialized.Length)
        {
            throw new InvalidOperationException("Inventory account identifiers must be unique.");
        }

        return this with
        {
            Revision = Revision + 1,
            UpdatedAt = occurredAt,
            Accounts = materialized,
        };
    }

    public AccountInventoryPlan CreatePlan(IncidentIndicator incidentIndicators = IncidentIndicator.None)
    {
        Validate();
        return AccountInventoryPlanner.Create(Accounts, incidentIndicators);
    }

    public void Validate()
    {
        if (SessionId == Guid.Empty || Revision < 0)
        {
            throw new InvalidOperationException("The persisted account inventory is invalid.");
        }

        ArgumentNullException.ThrowIfNull(Accounts);
        foreach (var account in Accounts)
        {
            account.Validate();
            if (account.CategoryConfirmedRevision > Revision)
            {
                throw new InvalidOperationException(
                    "An explicit account category cannot reference a future inventory revision.");
            }
        }

        if (Accounts.Select(account => account.Id).Distinct().Count() != Accounts.Length)
        {
            throw new InvalidOperationException("The persisted account inventory contains duplicate accounts.");
        }
    }
}

public static class AccountInventoryPlanner
{
    public static AccountInventoryPlan Create(
        IReadOnlyCollection<AccountInventoryEntry> accounts,
        IncidentIndicator incidentIndicators)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        var items = accounts
            .OrderBy(account => SortOrder(account.EffectiveCategory, incidentIndicators))
            .ThenBy(account => account.ProviderId, StringComparer.Ordinal)
            .ThenBy(account => account.Id)
            .Select((account, index) => new AccountInventoryPlanItem(
                account.Id,
                account.ProviderId,
                account.EffectiveCategory,
                Reason(account.EffectiveCategory),
                index + 1))
            .ToArray();
        return new AccountInventoryPlan(items);
    }

    private static int SortOrder(
        AccountRecoveryCategory category,
        IncidentIndicator incidentIndicators)
    {
        var recoveryChannelNeedsAttention =
            incidentIndicators.HasFlag(IncidentIndicator.LostAccess) ||
            incidentIndicators.HasFlag(IncidentIndicator.CompromisedRecoveryChannel);
        return category switch
        {
            AccountRecoveryCategory.Email when recoveryChannelNeedsAttention => 0,
            AccountRecoveryCategory.Critical => recoveryChannelNeedsAttention ? 1 : 0,
            AccountRecoveryCategory.Email => 1,
            AccountRecoveryCategory.Unknown => 2,
            AccountRecoveryCategory.NonCritical => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
    }

    private static AccountInventoryPlanReasonCode Reason(AccountRecoveryCategory category) => category switch
    {
        AccountRecoveryCategory.Email => AccountInventoryPlanReasonCode.EmailCategory,
        AccountRecoveryCategory.Critical => AccountInventoryPlanReasonCode.CriticalCategory,
        AccountRecoveryCategory.Unknown => AccountInventoryPlanReasonCode.UnknownCategory,
        AccountRecoveryCategory.NonCritical => AccountInventoryPlanReasonCode.NonCriticalCategory,
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };
}
