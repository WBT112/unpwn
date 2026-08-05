namespace Unpwn.Vault.Cryptography;

public sealed record VaultRecordDescriptor(
    string RecordType,
    string RecordId,
    int SchemaVersion)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RecordType))
        {
            throw new ArgumentException("Record type is required.", nameof(RecordType));
        }

        if (string.IsNullOrWhiteSpace(RecordId))
        {
            throw new ArgumentException("Record identifier is required.", nameof(RecordId));
        }

        if (SchemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(SchemaVersion), "Schema version must be positive.");
        }
    }
}
