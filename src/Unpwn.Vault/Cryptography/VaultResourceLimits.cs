namespace Unpwn.Vault.Cryptography;

public static class VaultResourceLimits
{
    public const int MaximumArgon2MemorySizeKiB = 256 * 1024;
    public const int MaximumArgon2Iterations = 10;
    public const int MaximumArgon2DegreeOfParallelism = 8;
    public const int MaximumRecordBytes = 8 * 1024 * 1024;
    public const int MaximumRecordCount = 4096;
    public const int MaximumRecordTypeUtf8Bytes = 64;
    public const int MaximumRecordIdUtf8Bytes = 64;

    public static void ValidateStoredRecordLength(long length)
    {
        if (length < 0 || length > MaximumRecordBytes)
        {
            throw new InvalidDataException("The recovery vault record exceeds the supported size limit.");
        }
    }

    public static void ValidateStoredFixedLength(long length, int expectedLength)
    {
        if (length != expectedLength)
        {
            throw new InvalidDataException("The recovery vault contains invalid cryptographic metadata.");
        }
    }

    public static void ValidateRecordCount(long count)
    {
        if (count < 0 || count > MaximumRecordCount)
        {
            throw new InvalidDataException("The recovery vault contains too many records.");
        }
    }

    public static void ValidateRecordMetadataLength(long recordTypeUtf8Bytes, long recordIdUtf8Bytes)
    {
        if (recordTypeUtf8Bytes < 0 ||
            recordTypeUtf8Bytes > MaximumRecordTypeUtf8Bytes ||
            recordIdUtf8Bytes < 0 ||
            recordIdUtf8Bytes > MaximumRecordIdUtf8Bytes)
        {
            throw new InvalidDataException("The recovery vault contains invalid record metadata.");
        }
    }
}
