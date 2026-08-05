namespace Unpwn.App.Services;

public interface IShellContextService
{
    event EventHandler? ContextChanged;

    ShellContext Current { get; }

    Task LockAsync(CancellationToken cancellationToken);
}
