namespace Unpwn.App.Services;

public sealed class LockedShellContextService : IShellContextService
{
    public event EventHandler? ContextChanged;

    public ShellContext Current { get; private set; } = ShellContext.Locked;

    public Task LockAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current = ShellContext.Locked;
        ContextChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
