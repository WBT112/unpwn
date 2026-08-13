namespace Unpwn.Vault.Cryptography;

public sealed record Argon2idParameters(
    int MemorySizeKiB,
    int Iterations,
    int DegreeOfParallelism)
{
    public static Argon2idParameters Interactive { get; } = new(64 * 1024, 3, 2);

    public void Validate()
    {
        if (MemorySizeKiB < 19 * 1024 ||
            MemorySizeKiB > VaultResourceLimits.MaximumArgon2MemorySizeKiB)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MemorySizeKiB),
                $"Argon2id memory must be between 19 MiB and {VaultResourceLimits.MaximumArgon2MemorySizeKiB / 1024} MiB.");
        }

        if (Iterations < 2 || Iterations > VaultResourceLimits.MaximumArgon2Iterations)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Iterations),
                $"Argon2id iterations must be between 2 and {VaultResourceLimits.MaximumArgon2Iterations}.");
        }

        if (DegreeOfParallelism < 1 ||
            DegreeOfParallelism > VaultResourceLimits.MaximumArgon2DegreeOfParallelism)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DegreeOfParallelism),
                $"Argon2id parallelism must be between 1 and {VaultResourceLimits.MaximumArgon2DegreeOfParallelism}.");
        }
    }
}
