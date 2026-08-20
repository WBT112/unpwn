using Unpwn.Core;

namespace Unpwn.Providers.Workflows;

public sealed record ReviewedRecoveryBrowserEntry(
    string ProviderId,
    DateOnly VerifiedAt,
    RecoveryLocationDefinition Location);

/// <summary>
/// Repository-reviewed initial browser destinations for providers that do not yet have a
/// provider-specific recovery workflow. These entries validate navigation only; they do not
/// upgrade general guidance to provider-reviewed guidance or prove recovery success.
/// </summary>
public static class RepositoryRecoveryBrowserEntryCatalog
{
    public static IReadOnlyList<ReviewedRecoveryBrowserEntry> Entries { get; } =
    [
        new(
            "bitwarden",
            new DateOnly(2026, 8, 20),
            new RecoveryLocationDefinition(
                "bitwarden-web-vault",
                new Uri("https://vault.bitwarden.com/"),
                ["https://vault.bitwarden.com", "https://vault.bitwarden.eu"])),
    ];

    public static ReviewedRecoveryBrowserEntry? Resolve(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var normalized = providerId.Trim();
        return Entries.SingleOrDefault(entry =>
            string.Equals(entry.ProviderId, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals($"{entry.ProviderId}.com", normalized, StringComparison.OrdinalIgnoreCase));
    }
}
