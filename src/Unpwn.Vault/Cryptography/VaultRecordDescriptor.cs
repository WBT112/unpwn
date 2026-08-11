namespace Unpwn.Vault.Cryptography;

public sealed record VaultRecordDescriptor(
    string RecordType,
    string RecordId,
    int SchemaVersion)
{
    private static readonly HashSet<string> AllowedRecordTypes =
        new(StringComparer.Ordinal)
        {
            "account-execution",
            "account-state",
            "audit-events",
            "generated-credential",
            "note",
            "provider-workflow",
            "recovery-session",
        };

    public void Validate()
    {
        if (!AllowedRecordTypes.Contains(RecordType))
        {
            throw new ArgumentException("Record type must be a repository-defined non-sensitive metadata category.", nameof(RecordType));
        }

        if (!Guid.TryParse(RecordId, out var recordId) || recordId == Guid.Empty)
        {
            throw new ArgumentException("Record identifier must be a non-empty opaque GUID and must not contain account data.", nameof(RecordId));
        }

        if (SchemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), "Schema version must be positive.");
        }
    }
}
