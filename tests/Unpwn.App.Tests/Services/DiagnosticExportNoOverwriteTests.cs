using System.Text;
using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class DiagnosticExportNoOverwriteTests
{
    [Fact]
    public async Task ExistingDestinationIsNeverOverwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), $"unpwn-diagnostics-{Guid.NewGuid():N}");
        var destination = Path.Combine(root, "diagnostics.json");
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(destination, "existing-content");
            var writer = new FileDiagnosticFileWriter();

            await Assert.ThrowsAsync<IOException>(() => writer.WriteAtomicallyAsync(
                destination,
                Encoding.UTF8.GetBytes("replacement-content"),
                CancellationToken.None));

            Assert.Equal("existing-content", await File.ReadAllTextAsync(destination));
            Assert.Single(Directory.GetFiles(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
