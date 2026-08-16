using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Services;

public sealed class RecoveryBrowserNativeAsyncOperationRegistryTests
{
    [Fact]
    public async Task NormalCompletionReleasesPendingOperationExactlyOnce()
    {
        var registry = new RecoveryBrowserNativeAsyncOperationRegistry();
        IntPtr token = IntPtr.Zero;
        var operation = registry.RunAsync(
            userData => token = userData,
            CancellationToken.None);

        Assert.NotEqual(IntPtr.Zero, token);
        Assert.Equal(1, registry.PendingCount);

        registry.Complete(token);
        await operation;

        Assert.Equal(0, registry.PendingCount);
        registry.Complete(token);
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public async Task CancellationBeforeCallbackReleasesManagedStateImmediately()
    {
        var registry = new RecoveryBrowserNativeAsyncOperationRegistry();
        using var cancellation = new CancellationTokenSource();
        IntPtr token = IntPtr.Zero;
        var operation = registry.RunAsync(
            userData => token = userData,
            cancellation.Token);

        Assert.NotEqual(IntPtr.Zero, token);
        Assert.Equal(1, registry.PendingCount);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public async Task LateCallbackAfterCancellationIsSafeAndCannotResurrectState()
    {
        var registry = new RecoveryBrowserNativeAsyncOperationRegistry();
        using var cancellation = new CancellationTokenSource();
        IntPtr token = IntPtr.Zero;
        var operation = registry.RunAsync(
            userData => token = userData,
            cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(0, registry.PendingCount);

        registry.Complete(token);
        registry.Complete(token, new IOException("synthetic late native failure"));

        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public async Task NativeCompletionFailureReleasesStateAndSurfacesFailure()
    {
        var registry = new RecoveryBrowserNativeAsyncOperationRegistry();
        IntPtr token = IntPtr.Zero;
        var operation = registry.RunAsync(
            userData => token = userData,
            CancellationToken.None);
        var failure = new IOException("synthetic native clear failure");

        registry.Complete(token, failure);

        var thrown = await Assert.ThrowsAsync<IOException>(() => operation);
        Assert.Same(failure, thrown);
        Assert.Equal(0, registry.PendingCount);

        registry.Complete(token, new IOException("duplicate callback"));
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public async Task SynchronousNativeStartFailureDoesNotRetainState()
    {
        var registry = new RecoveryBrowserNativeAsyncOperationRegistry();
        var failure = new DllNotFoundException("synthetic native start failure");

        var operation = registry.RunAsync(
            _ => throw failure,
            CancellationToken.None);

        var thrown = await Assert.ThrowsAsync<DllNotFoundException>(() => operation);
        Assert.Same(failure, thrown);
        Assert.Equal(0, registry.PendingCount);
    }

    [Fact]
    public async Task RepeatedCancellationWithoutNativeCallbacksDoesNotAccumulateManagedState()
    {
        var registry = new RecoveryBrowserNativeAsyncOperationRegistry();

        for (var attempt = 0; attempt < 256; attempt++)
        {
            using var cancellation = new CancellationTokenSource();
            var operation = registry.RunAsync(
                _ => { },
                cancellation.Token);

            Assert.Equal(1, registry.PendingCount);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
            Assert.Equal(0, registry.PendingCount);
        }
    }
}
