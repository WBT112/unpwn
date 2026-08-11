using System.Text.Json;

namespace Unpwn.App.Services;

public interface IRecentVaultStore
{
    Task<IReadOnlyList<RecentVaultReference>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        IReadOnlyList<RecentVaultReference> references,
        CancellationToken cancellationToken);
}

public sealed class JsonRecentVaultStore(string? path = null) : IRecentVaultStore
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
    };

    private readonly string _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "unpwn",
        "recent-vaults.json");

    public async Task<IReadOnlyList<RecentVaultReference>> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var references = await JsonSerializer.DeserializeAsync<RecentVaultReference[]>(
                stream,
                SerializerOptions,
                cancellationToken);
            return references?
                .Where(reference =>
                    !string.IsNullOrWhiteSpace(reference.Path) &&
                    !string.IsNullOrWhiteSpace(reference.DisplayName))
                .Select(reference => reference with { Path = Path.GetFullPath(reference.Path) })
                .DistinctBy(reference => reference.Path, PathComparer)
                .OrderByDescending(reference => reference.LastOpenedAt)
                .Take(8)
                .ToArray() ?? [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<RecentVaultReference> references,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(references);

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The recent-vault store has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                references,
                SerializerOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }
}
