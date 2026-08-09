using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Unpwn.App.Services;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The application-scoped clipboard service owns its gate for the process lifetime.")]
public sealed class AvaloniaCredentialClipboardService(Func<TopLevel?> topLevelProvider)
    : ICredentialClipboardService
{
    private readonly Func<TopLevel?> _topLevelProvider =
        topLevelProvider ?? throw new ArgumentNullException(nameof(topLevelProvider));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _ownedHash;

    public async Task<bool> CopyAsync(
        ReadOnlyMemory<byte> secretUtf8,
        CancellationToken cancellationToken)
    {
        if (secretUtf8.IsEmpty || _topLevelProvider()?.Clipboard is not { } clipboard)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var text = Encoding.UTF8.GetString(secretUtf8.Span);
        var hash = SHA256.HashData(secretUtf8.Span);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await clipboard.SetValueAsync(DataFormat.Text, text);
            CryptographicOperations.ZeroMemory(_ownedHash ?? []);
            _ownedHash = hash;
            return true;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(hash);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearOwnedAsync(CancellationToken cancellationToken)
    {
        if (_ownedHash is null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_ownedHash is null)
            {
                return;
            }

            var clipboard = _topLevelProvider()?.Clipboard;
            var text = clipboard is null ? null : await clipboard.TryGetTextAsync();
            if (text is not null)
            {
                var currentBytes = Encoding.UTF8.GetBytes(text);
                var currentHash = SHA256.HashData(currentBytes);
                try
                {
                    if (CryptographicOperations.FixedTimeEquals(currentHash, _ownedHash))
                    {
                        await clipboard!.ClearAsync();
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(currentHash);
                    CryptographicOperations.ZeroMemory(currentBytes);
                }
            }

            CryptographicOperations.ZeroMemory(_ownedHash);
            _ownedHash = null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
