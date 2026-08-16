using System.Globalization;
using System.Text;

namespace Unpwn.Core;

public sealed record AccountClassificationSuggestion(
    AccountRecoveryCategory Category,
    string CatalogVersion);

public sealed record AccountClassificationProviderRecord(
    string ProviderId,
    string DisplayName,
    AccountRecoveryCategory Category,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> ProviderIdAliases,
    IReadOnlyList<string> Provenance);

/// <summary>
/// Repository-controlled, offline-only account classification suggestions.
/// This catalog determines when an account is recovered; provider workflows
/// independently determine how recovery is performed.
/// </summary>
public static class RepositoryAccountClassificationCatalog
{
    public const string CurrentVersion = "2026.08.2";

    private static readonly Lazy<AccountClassificationCatalogData> Catalog =
        new(LoadEmbeddedCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<AccountClassificationProviderRecord> ProviderRecords =>
        Array.AsReadOnly(Catalog.Value.Records);

    public static int EmailAliasCount => Catalog.Value.Records
        .Where(record => record.Category == AccountRecoveryCategory.Email)
        .Sum(record => record.Domains.Count);

    public static AccountClassificationSuggestion Classify(string providerId, string? accountUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var data = Catalog.Value;
        var categories = new List<AccountRecoveryCategory>(3);
        var canonicalProvider = providerId.Trim();
        if (data.CanonicalProviderCategories.TryGetValue(canonicalProvider, out var canonicalCategory))
        {
            categories.Add(canonicalCategory);
        }
        else
        {
            var providerAlias = NormalizeProviderId(providerId);
            if (data.ProviderAliasCategories.TryGetValue(providerAlias, out var aliasCategory))
            {
                categories.Add(aliasCategory);
            }
        }

        var providerDomain = NormalizeDomain(providerId);
        if (MatchDomain(providerDomain, data.DomainCategories) is { } providerDomainCategory)
        {
            categories.Add(providerDomainCategory);
        }

        var host = GetHost(accountUrl);
        if (MatchDomain(host, data.DomainCategories) is { } hostCategory)
        {
            categories.Add(hostCategory);
        }

        var category = categories.Count == 0
            ? AccountRecoveryCategory.Unknown
            : categories.Order().First();
        return new AccountClassificationSuggestion(category, CurrentVersion);
    }

    internal static AccountClassificationCatalogData Load(TextReader reader) =>
        AccountClassificationCatalogLoader.Load(reader);

    internal static string NormalizeProviderId(string providerId) =>
        string.Concat(providerId.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));

    internal static string? NormalizeDomain(string value)
    {
        var candidate = value.Trim().TrimEnd('.');
        if (candidate.Length is 0 or > 253)
        {
            return null;
        }

        try
        {
            candidate = new IdnMapping().GetAscii(candidate).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        return Uri.CheckHostName(candidate) == UriHostNameType.Dns ? candidate : null;
    }

    private static AccountClassificationCatalogData LoadEmbeddedCatalog()
    {
        const string resourceName = "Unpwn.Core.Data.account-classification-catalog.tsv";
        var assembly = typeof(RepositoryAccountClassificationCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded account classification catalog '{resourceName}' is missing.");
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return AccountClassificationCatalogLoader.Load(reader);
    }

    private static AccountRecoveryCategory? MatchDomain(
        string? host,
        IReadOnlyDictionary<string, AccountRecoveryCategory> domains)
    {
        if (host is null)
        {
            return null;
        }

        var candidate = host;
        while (true)
        {
            if (domains.TryGetValue(candidate, out var category))
            {
                return category;
            }

            var separator = candidate.IndexOf('.');
            if (separator < 0 || separator == candidate.Length - 1)
            {
                return null;
            }

            candidate = candidate[(separator + 1)..];
        }
    }

    private static string? GetHost(string? accountUrl)
    {
        if (string.IsNullOrWhiteSpace(accountUrl) ||
            !Uri.TryCreate(accountUrl.Trim(), UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return NormalizeDomain(uri.IdnHost);
    }
}

internal sealed record AccountClassificationCatalogData(
    AccountClassificationProviderRecord[] Records,
    IReadOnlyDictionary<string, AccountRecoveryCategory> CanonicalProviderCategories,
    IReadOnlyDictionary<string, AccountRecoveryCategory> ProviderAliasCategories,
    IReadOnlyDictionary<string, AccountRecoveryCategory> DomainCategories);

internal static class AccountClassificationCatalogLoader
{
    private const string Header =
        "provider_id\tdisplay_name\tcategory\tdomains\tprovider_aliases\tprovenance";

    internal const int MaximumProviderRecords = 4000;
    internal const int MaximumLineCharacters = 65536;
    internal const int MaximumDomainsPerProvider = 512;
    internal const int MaximumAliasesPerProvider = 128;
    internal const int MaximumProvenanceEntriesPerProvider = 128;
    internal const int MaximumTotalDomains = 50000;
    internal const int MaximumTotalProviderAliases = 20000;

    internal static AccountClassificationCatalogData Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var firstLine = ReadBoundedLine(reader)
            ?? throw new InvalidOperationException("The account classification catalog is empty.");
        if (!string.Equals(firstLine, Header, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The account classification catalog has an unsupported header.");
        }

        var records = new List<AccountClassificationProviderRecord>();
        var canonicalProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalDomains = 0;
        var totalAliases = 0;
        string? line;
        while ((line = ReadBoundedLine(reader)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            if (records.Count >= MaximumProviderRecords)
            {
                throw new InvalidOperationException("The account classification catalog contains too many providers.");
            }

            var fields = line.Split('\t');
            if (fields.Length != 6)
            {
                throw new InvalidOperationException("The account classification catalog contains a malformed record.");
            }

            var providerId = fields[0].Trim();
            if (!IsValidCanonicalProviderId(providerId) || !canonicalProviderIds.Add(providerId))
            {
                throw new InvalidOperationException(
                    "The account classification catalog contains an invalid or duplicate canonical provider ID.");
            }

            var displayName = fields[1].Trim();
            if (displayName.Length is 0 or > 240)
            {
                throw new InvalidOperationException(
                    "The account classification catalog contains an invalid provider name.");
            }

            if (!Enum.TryParse<AccountRecoveryCategory>(fields[2], ignoreCase: false, out var category) ||
                !AccountRecoveryCategoryRules.IsUserSelectable(category))
            {
                throw new InvalidOperationException(
                    "The account classification catalog contains an invalid recovery category.");
            }

            var domains = ParseDomains(fields[3], providerId);
            var aliases = ParseAliases(fields[4]);
            var provenance = ParseValues(
                fields[5], MaximumProvenanceEntriesPerProvider, 300, "provenance");
            if (domains.Length == 0 || provenance.Length == 0)
            {
                throw new InvalidOperationException(
                    "Every account classification provider requires domains and provenance.");
            }

            totalDomains += domains.Length;
            totalAliases += aliases.Length;
            if (totalDomains > MaximumTotalDomains || totalAliases > MaximumTotalProviderAliases)
            {
                throw new InvalidOperationException(
                    "The account classification catalog exceeds its aggregate resource limits.");
            }

            records.Add(new AccountClassificationProviderRecord(
                providerId,
                displayName,
                category,
                domains,
                aliases,
                provenance));
        }

        if (records.Count == 0)
        {
            throw new InvalidOperationException("The account classification catalog contains no providers.");
        }

        return BuildIndexes([.. records]);
    }

    private static AccountClassificationCatalogData BuildIndexes(
        AccountClassificationProviderRecord[] records)
    {
        var canonicalCategories = records.ToDictionary(
            record => record.ProviderId,
            record => record.Category,
            StringComparer.OrdinalIgnoreCase);

        var canonicalNormalizedOwners = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var normalized = RepositoryAccountClassificationCatalog.NormalizeProviderId(record.ProviderId);
            if (canonicalNormalizedOwners.TryGetValue(normalized, out var owner) &&
                !string.Equals(owner, record.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                canonicalNormalizedOwners[normalized] = null;
            }
            else
            {
                canonicalNormalizedOwners[normalized] = record.ProviderId;
            }
        }

        var aliasCategories = new Dictionary<string, AccountRecoveryCategory>(StringComparer.Ordinal);
        var aliasOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            foreach (var alias in record.ProviderIdAliases)
            {
                var normalized = RepositoryAccountClassificationCatalog.NormalizeProviderId(alias);
                if (canonicalNormalizedOwners.TryGetValue(normalized, out var canonicalOwner) &&
                    !string.Equals(canonicalOwner, record.ProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (aliasOwners.TryGetValue(normalized, out var aliasOwner) &&
                    !string.Equals(aliasOwner, record.ProviderId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The account classification catalog contains a provider alias collision.");
                }

                aliasOwners[normalized] = record.ProviderId;
                aliasCategories[normalized] = record.Category;
            }
        }

        var domainEntries = records
            .SelectMany(record => record.Domains.Select(domain =>
                (Domain: domain, record.ProviderId, record.Category)))
            .OrderBy(entry => entry.Domain.Count(character => character == '.'))
            .ThenBy(entry => entry.Domain, StringComparer.Ordinal)
            .ToArray();
        var domainCategories = new Dictionary<string, AccountRecoveryCategory>(StringComparer.Ordinal);
        var domainOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (domain, providerId, category) in domainEntries)
        {
            if (domainOwners.TryGetValue(domain, out var exactOwner) &&
                !string.Equals(exactOwner, providerId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The account classification catalog contains a duplicate domain alias.");
            }

            var parent = domain;
            while (true)
            {
                var separator = parent.IndexOf('.');
                if (separator < 0 || separator == parent.Length - 1)
                {
                    break;
                }

                parent = parent[(separator + 1)..];
                if (domainOwners.TryGetValue(parent, out var parentOwner) &&
                    !string.Equals(parentOwner, providerId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The account classification catalog contains overlapping domain aliases.");
                }
            }

            domainOwners[domain] = providerId;
            domainCategories[domain] = category;
        }

        return new AccountClassificationCatalogData(
            records,
            canonicalCategories,
            aliasCategories,
            domainCategories);
    }

    private static bool IsValidCanonicalProviderId(string providerId) =>
        providerId.Length is > 0 and <= 160 &&
        providerId.All(character =>
            char.IsAsciiLetterOrDigit(character) || character == '-') &&
        string.Equals(providerId, providerId.ToLowerInvariant(), StringComparison.Ordinal) &&
        providerId[0] != '-' &&
        providerId[^1] != '-';

    private static string[] ParseDomains(string field, string providerId)
    {
        var rawValues = ParseValues(field, MaximumDomainsPerProvider, 253, "domain");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in rawValues)
        {
            var normalized = RepositoryAccountClassificationCatalog.NormalizeDomain(raw)
                ?? throw new InvalidOperationException(
                    $"The account classification catalog contains invalid domain alias '{raw}' for provider '{providerId}'.");
            if (!result.Add(normalized))
            {
                throw new InvalidOperationException(
                    "The account classification catalog contains a duplicate domain within one provider.");
            }
        }

        return [.. result.Order(StringComparer.Ordinal)];
    }

    private static string[] ParseAliases(string field)
    {
        var rawValues = ParseValues(field, MaximumAliasesPerProvider, 160, "provider alias");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in rawValues)
        {
            var normalized = RepositoryAccountClassificationCatalog.NormalizeProviderId(raw);
            if (normalized.Length == 0)
            {
                throw new InvalidOperationException(
                    "The account classification catalog contains an invalid provider alias.");
            }

            result.TryAdd(normalized, raw);
        }

        return [.. result.Values.Order(StringComparer.Ordinal)];
    }

    private static string[] ParseValues(
        string field,
        int maximumCount,
        int maximumLength,
        string label)
    {
        if (field.Length == 0)
        {
            return [];
        }

        var values = field.Split('|');
        if (values.Length > maximumCount)
        {
            throw new InvalidOperationException(
                $"The account classification catalog contains too many {label} entries.");
        }

        if (values.Any(value => value.Length == 0 || value.Length > maximumLength))
        {
            throw new InvalidOperationException(
                $"The account classification catalog contains an invalid {label} entry.");
        }

        return values;
    }

    private static string? ReadBoundedLine(TextReader reader)
    {
        var builder = new StringBuilder(Math.Min(256, MaximumLineCharacters));
        while (true)
        {
            var character = reader.Read();
            if (character < 0)
            {
                return builder.Length == 0 ? null : builder.ToString();
            }

            if (character == '\n')
            {
                return builder.ToString();
            }

            if (character == '\r')
            {
                if (reader.Peek() == '\n')
                {
                    _ = reader.Read();
                }

                return builder.ToString();
            }

            if (builder.Length >= MaximumLineCharacters)
            {
                throw new InvalidOperationException(
                    "The account classification catalog contains an overlong line.");
            }

            builder.Append((char)character);
        }
    }
}
