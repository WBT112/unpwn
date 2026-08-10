using System.Diagnostics.CodeAnalysis;
using Unpwn.Application.Diagnostics;

namespace Unpwn.App.Services;

public sealed record ApplicationRunState(
    bool PreviousExitWasAbnormal,
    bool MarkerUnavailable);

public interface IApplicationRunMarkerStore
{
    bool Exists();

    void Write();

    void Delete();
}

public sealed class ApplicationRunStateService(
    IApplicationRunMarkerStore markerStore,
    SecretSafeDiagnostics diagnostics)
{
    private readonly IApplicationRunMarkerStore _markerStore = markerStore ?? throw new ArgumentNullException(nameof(markerStore));
    private readonly SecretSafeDiagnostics _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    public ApplicationRunState Begin()
    {
        try
        {
            var abnormal = _markerStore.Exists();
            _markerStore.Write();
            if (abnormal)
            {
                _diagnostics.ReportFailure(
                    DiagnosticOperation.StartupRecovery,
                    new PreviousRunInterruptedException());
            }

            return new ApplicationRunState(abnormal, MarkerUnavailable: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _diagnostics.ReportFailure(DiagnosticOperation.StartupRecovery, exception);
            return new ApplicationRunState(
                PreviousExitWasAbnormal: false,
                MarkerUnavailable: true);
        }
    }

    public void Complete()
    {
        try
        {
            _markerStore.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _diagnostics.ReportFailure(DiagnosticOperation.StartupRecovery, exception);
        }
    }

    private sealed class PreviousRunInterruptedException : Exception
    {
    }
}

public sealed class FileApplicationRunMarkerStore : IApplicationRunMarkerStore
{
    private readonly string _markerPath;

    public FileApplicationRunMarkerStore(string markerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerPath);
        _markerPath = Path.GetFullPath(markerPath);
    }

    public bool Exists() => File.Exists(_markerPath);

    public void Write()
    {
        var directory = Path.GetDirectoryName(_markerPath)
            ?? throw new IOException("The application marker directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, "running");
            File.Move(temporaryPath, _markerPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public void Delete() => File.Delete(_markerPath);
}

public interface ISafeCrashLock
{
    void LockAfterApplicationFailure();
}

public sealed class ApplicationCrashBoundary(
    ISafeCrashLock crashLock,
    SecretSafeDiagnostics diagnostics)
{
    private readonly ISafeCrashLock _crashLock = crashLock ?? throw new ArgumentNullException(nameof(crashLock));
    private readonly SecretSafeDiagnostics _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    private int _handled;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The last-chance crash boundary must clear vault keys without exposing source exception details.")]
    public void Handle(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref _handled, 1) != 0)
        {
            return;
        }

        _diagnostics.ReportFailure(DiagnosticOperation.ApplicationCrash, exception);
        try
        {
            _crashLock.LockAfterApplicationFailure();
        }
        catch (Exception lockException)
        {
            _diagnostics.ReportFailure(DiagnosticOperation.VaultLock, lockException);
        }
    }
}
