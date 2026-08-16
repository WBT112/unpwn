using System.Globalization;

namespace Unpwn.Core;

public sealed record AccountClassificationSuggestion(
    AccountRecoveryCategory Category,
    string CatalogVersion);

public sealed record AccountClassificationProviderRecord(
    string Id,
    string Name,
    AccountRecoveryCategory Category,
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> ProviderIdAliases,
    string ProvenanceId,
    string ReviewBasis);

public sealed record AccountClassificationCatalogProvenance(
    string Id,
    string SourceName,
    string SourceRevision,
    string LicenseId,
    string SourceCategory);

/// <summary>
/// Repository-controlled, offline-only account classification suggestions.
/// This catalog determines when an account is recovered; provider workflows
/// independently determine how recovery is performed.
/// </summary>
public static class RepositoryAccountClassificationCatalog
{
    public const string CurrentVersion = "2026.08.3";

    private const string CuratedProvenanceId = "unpwn-curated-2026.08.3";

    private static readonly IdnMapping Idn = new();
    private static readonly CatalogState State = BuildState();

    public static IReadOnlyList<AccountClassificationProviderRecord> Providers => State.Providers;

    public static IReadOnlyList<AccountClassificationCatalogProvenance> Provenance => State.Provenance;

    public static int EmailAliasCount => State.Providers
        .Where(record => record.Category == AccountRecoveryCategory.Email)
        .Sum(record => record.Domains.Count);

    public static int GetProviderCount(AccountRecoveryCategory category) =>
        State.Providers.Count(record => record.Category == category);

    public static AccountClassificationSuggestion Classify(string providerId, string? accountUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var normalizedProvider = NormalizeProviderId(providerId);
        var providerDomain = NormalizeDomain(providerId);
        var host = GetHost(accountUrl);

        var category = HighestPriority(
            FindProviderAliasCategory(normalizedProvider),
            FindDomainCategory(providerDomain),
            FindDomainCategory(host));
        return new AccountClassificationSuggestion(category, CurrentVersion);
    }

    private static CatalogState BuildState()
    {
        var provenance = Array.AsReadOnly(
        [
            new AccountClassificationCatalogProvenance(
                CuratedProvenanceId,
                "unpwn repository-reviewed provider metadata",
                CurrentVersion,
                "AGPL-3.0-or-later",
                "curated-manual"),
        ]);

        var records = new List<AccountClassificationProviderRecord>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var domains = new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal);
        var aliases = new Dictionary<string, AccountClassificationProviderRecord>(StringComparer.Ordinal);

        foreach (var record in CreateCuratedRecords())
        {
            AddRecord(record, records, ids, domains, aliases);
        }

        return new CatalogState(
            Array.AsReadOnly(records.ToArray()),
            provenance,
            domains,
            aliases);
    }

    private static IReadOnlyList<AccountClassificationProviderRecord> CreateCuratedRecords() =>
    [
        Record("email-gmail", "Gmail", AccountRecoveryCategory.Email,
            ["gmail.com", "googlemail.com"], "gmail", "googlemail"),
        Record("email-microsoft", "Microsoft Outlook / Hotmail", AccountRecoveryCategory.Email,
            ["outlook.com", "outlook.de", "outlook.co.uk", "outlook.fr", "outlook.it", "outlook.es",
             "live.com", "live.de", "live.co.uk", "hotmail.com", "hotmail.de", "hotmail.fr", "msn.com"],
            "outlook"),
        Record("email-yahoo", "Yahoo Mail", AccountRecoveryCategory.Email,
            ["yahoo.com", "yahoo.de", "yahoo.fr", "yahoo.it", "yahoo.es", "yahoo.co.uk", "yahoo.co.jp",
             "yahoo.co.in", "yahoo.com.au", "yahoo.ca", "rocketmail.com"], "yahoomail"),
        Record("email-proton", "Proton Mail", AccountRecoveryCategory.Email,
            ["proton.me", "protonmail.com", "protonmail.ch"], "protonmail"),
        Record("email-tuta", "Tuta", AccountRecoveryCategory.Email,
            ["tuta.com", "tutanota.com", "tutamail.com"], "tutanota"),
        Record("email-gmx", "GMX", AccountRecoveryCategory.Email,
            ["gmx.de", "gmx.net", "gmx.com", "gmx.at", "gmx.ch"], "gmx"),
        Record("email-webde", "WEB.DE", AccountRecoveryCategory.Email, ["web.de"], "webde"),
        Record("email-fastmail", "Fastmail", AccountRecoveryCategory.Email,
            ["fastmail.com", "fastmail.fm", "fastmail.ca"], "fastmail"),
        Record("email-icloud", "Apple iCloud Mail", AccountRecoveryCategory.Email,
            ["icloud.com", "me.com", "mac.com"], "icloudmail"),
        Record("email-freenet", "Freenet Mail", AccountRecoveryCategory.Email, ["freenet.de"], "freenet"),
        Record("email-mailboxorg", "mailbox.org", AccountRecoveryCategory.Email, ["mailbox.org"], "mailboxorg"),
        Record("email-yandex", "Yandex Mail", AccountRecoveryCategory.Email,
            ["yandex.com", "yandex.ru"], "yandexmail"),
        Record("email-zoho", "Zoho Mail", AccountRecoveryCategory.Email,
            ["zoho.com", "zohomail.com"], "zohomail"),
        Record("email-aol", "AOL Mail", AccountRecoveryCategory.Email,
            ["aol.com", "aol.de"], "aol"),
        Record("email-t-online", "T-Online Mail", AccountRecoveryCategory.Email, ["t-online.de"], "tonline"),
        Record("email-mailru", "Mail.ru", AccountRecoveryCategory.Email, ["mail.ru"], "mailru"),
        Record("email-seznam", "Seznam Email", AccountRecoveryCategory.Email, ["seznam.cz"], "seznam"),
        Record("email-orange", "Orange Mail", AccountRecoveryCategory.Email, ["orange.fr", "wanadoo.fr"], "orangemail"),
        Record("email-libero", "Libero Mail", AccountRecoveryCategory.Email, ["libero.it"], "liberomail"),
        Record("email-qq", "QQ Mail", AccountRecoveryCategory.Email, ["qq.com"], "qqmail"),
        Record("email-163", "NetEase 163 Mail", AccountRecoveryCategory.Email, ["163.com"], "163mail"),
        Record("email-126", "NetEase 126 Mail", AccountRecoveryCategory.Email, ["126.com"], "126mail"),
        Record("email-naver", "Naver Mail", AccountRecoveryCategory.Email, ["naver.com"], "navermail"),
        Record("email-mailcom", "mail.com", AccountRecoveryCategory.Email, ["mail.com"], "mailcom"),

        Record("critical-1password", "1Password", AccountRecoveryCategory.Critical, ["1password.com"], "1password"),
        Record("critical-amazon", "Amazon", AccountRecoveryCategory.Critical,
            ["amazon.com", "amazon.de"], "amazon"),
        Record("critical-apple", "Apple", AccountRecoveryCategory.Critical, ["apple.com"], "apple"),
        Record("critical-auth0", "Auth0", AccountRecoveryCategory.Critical, ["auth0.com"], "auth0"),
        Record("critical-bitwarden", "Bitwarden", AccountRecoveryCategory.Critical, ["bitwarden.com"], "bitwarden"),
        Record("critical-discord", "Discord", AccountRecoveryCategory.Critical, ["discord.com"], "discord"),
        Record("critical-dropbox", "Dropbox", AccountRecoveryCategory.Critical, ["dropbox.com"], "dropbox"),
        Record("critical-ebay", "eBay", AccountRecoveryCategory.Critical, ["ebay.com", "ebay.de"], "ebay"),
        Record("critical-etsy", "Etsy", AccountRecoveryCategory.Critical, ["etsy.com"], "etsy"),
        Record("critical-facebook", "Facebook", AccountRecoveryCategory.Critical, ["facebook.com"], "facebook"),
        Record("critical-fidelity", "Fidelity", AccountRecoveryCategory.Critical, ["fidelity.com"], "fidelity"),
        Record("critical-github", "GitHub", AccountRecoveryCategory.Critical, ["github.com"], "github"),
        Record("critical-google", "Google Account", AccountRecoveryCategory.Critical, ["google.com"], "google"),
        Record("critical-healthcaregov", "HealthCare.gov", AccountRecoveryCategory.Critical, ["healthcare.gov"], "healthcaregov"),
        Record("critical-instagram", "Instagram", AccountRecoveryCategory.Critical, ["instagram.com"], "instagram"),
        Record("critical-klarna", "Klarna", AccountRecoveryCategory.Critical, ["klarna.com"], "klarna"),
        Record("critical-lastpass", "LastPass", AccountRecoveryCategory.Critical, ["lastpass.com"], "lastpass"),
        Record("critical-linkedin", "LinkedIn", AccountRecoveryCategory.Critical, ["linkedin.com"], "linkedin"),
        Record("critical-microsoft", "Microsoft Account", AccountRecoveryCategory.Critical, ["microsoft.com"], "microsoft"),
        Record("critical-n26", "N26", AccountRecoveryCategory.Critical, ["n26.com"], "n26"),
        Record("critical-okta", "Okta", AccountRecoveryCategory.Critical, ["okta.com"], "okta"),
        Record("critical-paypal", "PayPal", AccountRecoveryCategory.Critical, ["paypal.com", "paypal.de"], "paypal"),
        Record("critical-reddit", "Reddit", AccountRecoveryCategory.Critical, ["reddit.com"], "reddit"),
        Record("critical-revolut", "Revolut", AccountRecoveryCategory.Critical, ["revolut.com"], "revolut"),
        Record("critical-stripe", "Stripe", AccountRecoveryCategory.Critical, ["stripe.com"], "stripe"),
        Record("critical-wise", "Wise", AccountRecoveryCategory.Critical, ["wise.com"], "wise"),
        Record("critical-x", "X", AccountRecoveryCategory.Critical, ["x.com"], "x"),
        Record("critical-deutsche-bank", "Deutsche Bank", AccountRecoveryCategory.Critical, ["deutsche-bank.de"], "deutschebank"),
        Record("critical-commerzbank", "Commerzbank", AccountRecoveryCategory.Critical, ["commerzbank.de"], "commerzbank"),
        Record("critical-chase", "Chase", AccountRecoveryCategory.Critical, ["chase.com"], "chase"),
        Record("critical-bankofamerica", "Bank of America", AccountRecoveryCategory.Critical, ["bankofamerica.com"], "bankofamerica"),

        Record("noncritical-allrecipes", "Allrecipes", AccountRecoveryCategory.NonCritical, ["allrecipes.com"], "allrecipes"),
        Record("noncritical-buzzfeed", "BuzzFeed", AccountRecoveryCategory.NonCritical, ["buzzfeed.com"], "buzzfeed"),
        Record("noncritical-duolingo", "Duolingo", AccountRecoveryCategory.NonCritical, ["duolingo.com"], "duolingo"),
        Record("noncritical-goodreads", "Goodreads", AccountRecoveryCategory.NonCritical, ["goodreads.com"], "goodreads"),
        Record("noncritical-imdb", "IMDb", AccountRecoveryCategory.NonCritical, ["imdb.com"], "imdb"),
        Record("noncritical-medium", "Medium", AccountRecoveryCategory.NonCritical, ["medium.com"], "medium"),
        Record("noncritical-netflix", "Netflix", AccountRecoveryCategory.NonCritical, ["netflix.com"], "netflix"),
        Record("noncritical-pinterest", "Pinterest", AccountRecoveryCategory.NonCritical, ["pinterest.com"], "pinterest"),
        Record("noncritical-spotify", "Spotify", AccountRecoveryCategory.NonCritical, ["spotify.com"], "spotify"),
        Record("noncritical-weather", "The Weather Channel", AccountRecoveryCategory.NonCritical, ["weather.com"], "weatherchannel"),
    ];

    private static AccountClassificationProviderRecord Record(
        string id,
        string name,
        AccountRecoveryCategory category,
        string[] domains,
        params string[] providerIdAliases) =>
        new(
            id,
            name,
            category,
            Array.AsReadOnly(domains),
            Array.AsReadOnly(providerIdAliases),
            CuratedProvenanceId,
            category switch
            {
                AccountRecoveryCategory.Email => "Repository-reviewed mailbox provider family.",
                AccountRecoveryCategory.Critical => "Repository-reviewed provider with material identity, money, communications, recovery, or account-control impact.",
                AccountRecoveryCategory.NonCritical => "Repository-reviewed lower-impact consumer service; uncertain services remain Unknown.",
                _ => throw new InvalidOperationException("Unknown cannot be a curated provider category."),
            });

    private static void AddRecord(
        AccountClassificationProviderRecord record,
        List<AccountClassificationProviderRecord> records,
        HashSet<string> ids,
        Dictionary<string, AccountClassificationProviderRecord> domains,
        Dictionary<string, AccountClassificationProviderRecord> aliases)
    {
        if (string.IsNullOrWhiteSpace(record.Id) ||
            string.IsNullOrWhiteSpace(record.Name) ||
            record.Category == AccountRecoveryCategory.Unknown ||
            !Enum.IsDefined(record.Category) ||
            string.IsNullOrWhiteSpace(record.ProvenanceId) ||
            string.IsNullOrWhiteSpace(record.ReviewBasis) ||
            record.Domains.Count == 0)
        {
            throw new InvalidOperationException("Account classification provider metadata is invalid.");
        }

        if (!ids.Add(record.Id))
        {
            throw new InvalidOperationException("Account classification provider IDs must be unique.");
        }

        var normalizedDomains = record.Domains
            .Select(domain => NormalizeDomain(domain) ?? throw new InvalidOperationException(
                "Account classification provider metadata contains an invalid domain."))
            .ToArray();
        if (normalizedDomains.Distinct(StringComparer.Ordinal).Count() != normalizedDomains.Length)
        {
            throw new InvalidOperationException("Account classification provider metadata contains duplicate domains.");
        }

        foreach (var domain in normalizedDomains)
        {
            if (domains.Keys.Any(existing => DomainsOverlap(existing, domain)))
            {
                throw new InvalidOperationException("Account classification domains must have one unambiguous canonical owner.");
            }
        }

        var normalizedAliases = record.ProviderIdAliases
            .Select(NormalizeProviderId)
            .ToArray();
        if (normalizedAliases.Any(string.IsNullOrEmpty) ||
            normalizedAliases.Distinct(StringComparer.Ordinal).Count() != normalizedAliases.Length)
        {
            throw new InvalidOperationException("Account classification provider aliases are invalid or duplicated.");
        }

        var normalizedRecord = record with
        {
            Domains = Array.AsReadOnly(normalizedDomains),
            ProviderIdAliases = Array.AsReadOnly(normalizedAliases),
        };
        records.Add(normalizedRecord);

        foreach (var domain in normalizedDomains)
        {
            domains.Add(domain, normalizedRecord);
        }

        foreach (var alias in normalizedAliases)
        {
            if (!aliases.TryAdd(alias, normalizedRecord))
            {
                throw new InvalidOperationException("Account classification provider aliases must be unique.");
            }
        }
    }

    private static bool DomainsOverlap(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal) ||
        left.EndsWith('.' + right, StringComparison.Ordinal) ||
        right.EndsWith('.' + left, StringComparison.Ordinal);

    private static AccountRecoveryCategory FindProviderAliasCategory(string provider) =>
        State.ProviderAliasIndex.TryGetValue(provider, out var record)
            ? record.Category
            : AccountRecoveryCategory.Unknown;

    private static AccountRecoveryCategory FindDomainCategory(string? domain)
    {
        var candidate = domain;
        while (!string.IsNullOrEmpty(candidate))
        {
            if (State.DomainIndex.TryGetValue(candidate, out var record))
            {
                return record.Category;
            }

            var separator = candidate.IndexOf('.', StringComparison.Ordinal);
            candidate = separator < 0 ? null : candidate[(separator + 1)..];
        }

        return AccountRecoveryCategory.Unknown;
    }

    private static AccountRecoveryCategory HighestPriority(params AccountRecoveryCategory[] categories)
    {
        if (categories.Contains(AccountRecoveryCategory.Email))
        {
            return AccountRecoveryCategory.Email;
        }

        if (categories.Contains(AccountRecoveryCategory.Critical))
        {
            return AccountRecoveryCategory.Critical;
        }

        return categories.Contains(AccountRecoveryCategory.NonCritical)
            ? AccountRecoveryCategory.NonCritical
            : AccountRecoveryCategory.Unknown;
    }

    private static string NormalizeProviderId(string providerId) =>
        string.Concat(providerId.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));

    private static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var ascii = Idn.GetAscii(value.Trim().TrimEnd('.')).ToLowerInvariant();
            return Uri.CheckHostName(ascii) == UriHostNameType.Dns ? ascii : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? GetHost(string? accountUrl)
    {
        if (string.IsNullOrWhiteSpace(accountUrl) ||
            !Uri.TryCreate(accountUrl.Trim(), UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return NormalizeDomain(uri.IdnHost);
    }

    private sealed record CatalogState(
        IReadOnlyList<AccountClassificationProviderRecord> Providers,
        IReadOnlyList<AccountClassificationCatalogProvenance> Provenance,
        IReadOnlyDictionary<string, AccountClassificationProviderRecord> DomainIndex,
        IReadOnlyDictionary<string, AccountClassificationProviderRecord> ProviderAliasIndex);
}
