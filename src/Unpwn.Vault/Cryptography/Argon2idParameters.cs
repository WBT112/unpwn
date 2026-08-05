namespace Unpwn.Vault.Cryptography;

public sealed record Argon2idParameters(
    int MemorySizeKiB,
    int Iterations,
    int DegreeOfParallelism)
{
    public static Argon2idParameters Interactive { get; } = new(64 * 1024, 3, 2);

    public void Validate()
    {
        if (MemorySizeKiB < 19 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MemorySizeKiB), "Argon2id memory must be at least 19 MiB.");
        }

        if (Iterations < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Iterations), "Argon2id iterations must be at least 2.");
        }

        if (DegreeOfParallelism < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(DegreeOfParallelism), "Argon2id parallelism must be positive.");
        }
    }
}
