namespace Unpwn.Core;

public sealed record AccountClassificationSuggestion(
    AccountRecoveryCategory Category,
    string CatalogVersion);

/// <summary>
/// Repository-controlled, offline-only account classification suggestions.
/// This catalog determines when an account is recovered; provider workflows
/// independently determine how recovery is performed.
/// </summary>
public static class RepositoryAccountClassificationCatalog
{
    public const string CurrentVersion = "2026.08.1";

    private static readonly string[] EmailDomains =
    [
        "126.com", "139.com", "163.com", "a1.net", "alice.it", "aliyun.com", "aol.com", "aol.de",
        "att.net", "bellsouth.net", "bigpond.com", "bluewin.ch", "bol.com.br", "btinternet.com",
        "centrum.cz", "charter.net", "citromail.hu", "club-internet.fr", "comcast.net", "cox.net",
        "daum.net", "disroot.org", "earthlink.net", "email.cz", "email.it", "ewe.net", "fastmail.com",
        "fastmail.fm", "freenet.de", "frontier.com", "gmx.at", "gmx.ch", "gmx.com", "gmx.de", "gmx.net",
        "googlemail.com", "gmail.com", "hanmail.net", "hey.com", "hushmail.com", "icloud.com", "iinet.net.au",
        "inbox.lv", "interia.pl", "juno.com", "kakao.com", "laposte.net", "libero.it", "live.at", "live.be",
        "live.ca", "live.co.uk", "live.com", "live.com.au", "live.de", "live.dk", "live.fr", "live.ie",
        "live.it", "live.jp", "live.nl", "live.no", "live.se", "lycos.com", "mac.com", "mail.com", "mail.de",
        "mail.ee", "mail.ru", "mailbox.org", "me.com", "msn.com", "naver.com", "netcourrier.com", "netzero.net",
        "ntlworld.com", "o2.pl", "online.de", "orange.fr", "outlook.at", "outlook.be", "outlook.co.uk",
        "outlook.com", "outlook.com.au", "outlook.de", "outlook.dk", "outlook.es", "outlook.fr", "outlook.ie",
        "outlook.it", "outlook.jp", "outlook.nl", "outlook.pt", "outlook.se", "pobox.com", "post.cz",
        "proton.me", "protonmail.ch", "protonmail.com", "qq.com", "rambler.ru", "rediffmail.com", "rocketmail.com",
        "seznam.cz", "shaw.ca", "sina.com", "sky.com", "spectrum.net", "squirrelmail.org", "talktalk.net",
        "t-online.de", "tiscali.co.uk", "tiscali.it", "tuta.com", "tutanota.com", "tutamail.com", "ukr.net",
        "verizon.net", "virginmedia.com", "vodafone.de", "wanadoo.fr", "web.de", "wp.pl", "xs4all.nl",
        "yahoo.ca", "yahoo.co.in", "yahoo.co.jp", "yahoo.co.uk", "yahoo.com", "yahoo.com.au", "yahoo.de",
        "yahoo.es", "yahoo.fr", "yahoo.it", "yandex.com", "yandex.ru", "yeah.net", "zoho.com", "zohomail.com",
    ];

    private static readonly string[] EmailProviderIds =
    [
        "aol", "fastmail", "freenet", "gmail", "gmx", "googlemail", "icloudmail", "mailboxorg",
        "outlook", "protonmail", "tutanota", "webde", "yahoomail", "yandexmail", "zohomail",
    ];

    private static readonly string[] CriticalDomains =
    [
        "1password.com", "amazon.com", "amazon.de", "apple.com", "auth0.com", "bankofamerica.com",
        "barclays.com", "bitwarden.com", "chase.com", "commerzbank.de", "deutsche-bank.de", "discord.com",
        "dropbox.com", "ebay.com", "ebay.de", "etsy.com", "facebook.com", "fidelity.com", "github.com",
        "google.com", "healthcare.gov", "instagram.com", "klarna.com", "lastpass.com", "linkedin.com",
        "mastercard.com", "microsoft.com", "n26.com", "okta.com", "paypal.com", "reddit.com", "revolut.com",
        "stripe.com", "wise.com", "x.com",
    ];

    private static readonly string[] CriticalProviderIds =
    [
        "1password", "amazon", "apple", "auth0", "banking", "bitwarden", "classifieds", "commerce",
        "communications", "discord", "ebay", "etsy", "financial", "github", "google", "government", "health",
        "identityprovider", "insurance", "lastpass", "marketplace", "microsoft", "okta", "passwordmanager",
        "payments", "paypal", "reddit", "socialidentity", "stripe",
    ];

    private static readonly string[] NonCriticalDomains =
    [
        "allrecipes.com", "buzzfeed.com", "duolingo.com", "goodreads.com", "imdb.com", "medium.com",
        "netflix.com", "pinterest.com", "spotify.com", "steampowered.com", "twitch.tv", "weather.com",
    ];

    private static readonly string[] NonCriticalProviderIds =
    [
        "entertainment", "game", "gaming", "newsletter", "news", "recipes", "streaming", "weather",
    ];

    public static int EmailAliasCount => EmailDomains.Length;

    public static AccountClassificationSuggestion Classify(string providerId, string? accountUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var provider = NormalizeProviderId(providerId);
        var providerDomain = NormalizeProviderDomain(providerId);
        var host = GetHost(accountUrl);

        var category = Matches(provider, providerDomain, host, EmailProviderIds, EmailDomains)
            ? AccountRecoveryCategory.Email
            : Matches(provider, providerDomain, host, CriticalProviderIds, CriticalDomains)
                ? AccountRecoveryCategory.Critical
                : Matches(provider, providerDomain, host, NonCriticalProviderIds, NonCriticalDomains)
                    ? AccountRecoveryCategory.NonCritical
                    : AccountRecoveryCategory.Unknown;
        return new AccountClassificationSuggestion(category, CurrentVersion);
    }

    private static bool Matches(
        string provider,
        string? providerDomain,
        string? host,
        IReadOnlyCollection<string> providerIds,
        IReadOnlyCollection<string> domains) =>
        providerIds.Contains(provider, StringComparer.Ordinal) ||
        MatchesDomain(providerDomain, domains) ||
        MatchesDomain(host, domains);

    private static string NormalizeProviderId(string providerId) =>
        string.Concat(providerId.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit));

    private static string? NormalizeProviderDomain(string providerId)
    {
        var value = providerId.Trim().TrimEnd('.');
        return Uri.CheckHostName(value) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? new UriBuilder(Uri.UriSchemeHttps, value).Uri.IdnHost.ToLowerInvariant()
            : null;
    }

    private static bool MatchesDomain(string? host, IReadOnlyCollection<string> domains) =>
        host is not null && domains.Any(domain =>
            string.Equals(host, domain, StringComparison.Ordinal) ||
            host.EndsWith('.' + domain, StringComparison.Ordinal));

    private static string? GetHost(string? accountUrl)
    {
        if (string.IsNullOrWhiteSpace(accountUrl) ||
            !Uri.TryCreate(accountUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
    }
}
