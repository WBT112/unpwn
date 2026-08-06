namespace Unpwn.App.Services;

public interface IPresentationDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemPresentationDelay : IPresentationDelay
{
    public static SystemPresentationDelay Instance { get; } = new();

    private SystemPresentationDelay()
    {
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
