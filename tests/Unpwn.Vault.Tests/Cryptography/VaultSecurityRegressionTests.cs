using Unpwn.Vault.Cryptography;
using Xunit;

namespace Unpwn.Vault.Tests.Cryptography;

public sealed class VaultSecurityRegressionTests
{
    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void StoredRecordAndCountLimitsRejectValuesAboveTheirBounds()
    {
        Assert.Throws<VaultFormatException>(() =>
            VaultResourceLimits.ValidateStoredRecordLength(
                VaultResourceLimits.MaximumRecordBytes + 1L));
        Assert.Throws<VaultFormatException>(() =>
            VaultResourceLimits.ValidateRecordCount(
                VaultResourceLimits.MaximumRecordCount + 1L));
    }

    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void Argon2ResourceLimitsRejectOversizedParameters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Argon2idParameters(
                VaultResourceLimits.MaximumArgon2MemorySizeKiB + 1,
                3,
                2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Argon2idParameters(
                64 * 1024,
                VaultResourceLimits.MaximumArgon2Iterations + 1,
                2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Argon2idParameters(
                64 * 1024,
                3,
                VaultResourceLimits.MaximumArgon2DegreeOfParallelism + 1).Validate());
    }

    [Fact]
    [Trait("Category", "SecurityRegression")]
    public void FixedSeedOutOfRangeVaultLengthsAlwaysFailClosed()
    {
        var random = new Random(0x134);

        for (var attempt = 0; attempt < 64; attempt++)
        {
            var recordOverflow = random.Next(1, 4096);
            var countOverflow = random.Next(1, 4096);

            Assert.Throws<VaultFormatException>(() =>
                VaultResourceLimits.ValidateStoredRecordLength(
                    VaultResourceLimits.MaximumRecordBytes + (long)recordOverflow));
            Assert.Throws<VaultFormatException>(() =>
                VaultResourceLimits.ValidateRecordCount(
                    VaultResourceLimits.MaximumRecordCount + (long)countOverflow));
        }
    }
}
