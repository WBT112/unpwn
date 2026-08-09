namespace Unpwn.App.Services;

public interface ICredentialClipboardService
{
    Task<bool> CopyAsync(ReadOnlyMemory<byte> secretUtf8, CancellationToken cancellationToken);

    Task ClearOwnedAsync(CancellationToken cancellationToken);
}
