using System.Collections.Concurrent;

namespace Unpwn.App.Services;

internal sealed class RecoveryBrowserNativeAsyncOperationRegistry
{
    private readonly ConcurrentDictionary<nint, TaskCompletionSource> _pending = new();
    private int _nextToken;

    internal int PendingCount => _pending.Count;

    internal async Task RunAsync(
        Action<IntPtr> startNativeOperation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startNativeOperation);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var token = Add(completion);
        using var registration = cancellationToken.Register(
            static state =>
            {
                var cancellation = (CancellationState)state!;
                cancellation.Registry.Cancel(cancellation.Token, cancellation.CancellationToken);
            },
            new CancellationState(this, token, cancellationToken));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            startNativeOperation((IntPtr)token);
        }
        catch
        {
            _pending.TryRemove(token, out _);
            throw;
        }

        await completion.Task.ConfigureAwait(false);
    }

    internal void Complete(IntPtr userData, Exception? failure = null)
    {
        var token = (nint)userData;
        if (!_pending.TryRemove(token, out var completion))
        {
            return;
        }

        if (failure is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(failure);
        }
    }

    private nint Add(TaskCompletionSource completion)
    {
        while (true)
        {
            var candidate = Interlocked.Increment(ref _nextToken);
            if (candidate == 0)
            {
                continue;
            }

            var token = (nint)candidate;
            if (_pending.TryAdd(token, completion))
            {
                return token;
            }
        }
    }

    private void Cancel(nint token, CancellationToken cancellationToken)
    {
        if (_pending.TryRemove(token, out var completion))
        {
            completion.TrySetCanceled(cancellationToken);
        }
    }

    private sealed record CancellationState(
        RecoveryBrowserNativeAsyncOperationRegistry Registry,
        nint Token,
        CancellationToken CancellationToken);
}
