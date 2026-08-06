namespace Unpwn.Infrastructure.Recovery;

internal static class RecoveryLocationUriNormalizer
{
    public static bool TryNormalizeHttps(Uri? candidate, out Uri normalized)
    {
        normalized = null!;
        if (candidate is null ||
            !candidate.IsAbsoluteUri ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(candidate.Host) ||
            !string.IsNullOrEmpty(candidate.UserInfo))
        {
            return false;
        }

        var builder = new UriBuilder(candidate)
        {
            Scheme = Uri.UriSchemeHttps,
            Host = candidate.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty,
        };
        if (builder.Port == 443)
        {
            builder.Port = -1;
        }

        normalized = builder.Uri;
        return true;
    }

    public static bool TryNormalizeOrigin(string? value, out string normalizedOrigin)
    {
        normalizedOrigin = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
            !string.IsNullOrEmpty(origin.Fragment) ||
            !TryNormalizeHttps(origin, out var normalized) ||
            normalized.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(normalized.Query))
        {
            return false;
        }

        normalizedOrigin = GetOrigin(normalized);
        return true;
    }

    public static string GetOrigin(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    public static Uri GetOriginUri(Uri uri) => new($"{GetOrigin(uri)}/", UriKind.Absolute);

    public static Uri GetWellKnownChangePasswordUri(Uri uri) =>
        new(GetOriginUri(uri), ".well-known/change-password");
}
