using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public abstract class ScreenViewModel(
    AppRoute route,
    string title,
    string description,
    VisualStatusViewModel status) : ObservableObject
{
    private VisualStatusViewModel _status = status;

    public AppRoute Route { get; } = route;

    public string Title { get; } = title;

    public string Description { get; } = description;

    public VisualStatusViewModel Status
    {
        get => _status;
        protected set => SetProperty(ref _status, value);
    }
}

public sealed class VaultEntryScreenViewModel() : ScreenViewModel(
    AppRoute.VaultEntry,
    "Open your recovery workspace",
    "Create or unlock a local recovery vault to begin. No recovery data is loaded while the vault is locked.",
    VisualStatusViewModel.Create(
        AppVisualState.Warning,
        "Use a trusted device",
        "unpwn cannot detect or remove malware. Begin recovery only after moving to a device you trust."));

public sealed class PlaceholderScreenViewModel(
    AppRoute route,
    string title,
    string description,
    VisualStatusViewModel status) : ScreenViewModel(route, title, description, status);

public sealed class CsvImportScreenViewModel() : ScreenViewModel(
    AppRoute.CsvImport,
    "Import account inventory",
    "Map account fields from a CSV file without importing old passwords.",
    VisualStatusViewModel.Create(
        AppVisualState.Warning,
        "Password columns are excluded",
        "The import preview requires explicit exclusion of every detected password column."));

public sealed class CompletionScreenViewModel : ScreenViewModel
{
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly IShellContextService _shellContext;

    public CompletionScreenViewModel(
        IConfirmationDialogService confirmationDialog,
        IShellContextService shellContext)
        : base(
            AppRoute.Completion,
            "Complete recovery",
            "Review incomplete work, unresolved risks, and remaining credential exports before ending the session.",
            VisualStatusViewModel.Create(
                AppVisualState.UnresolvedRisk,
                "Completion requires review",
                "A session can be completed with unresolved risks only after an explicit confirmation."))
    {
        ArgumentNullException.ThrowIfNull(confirmationDialog);
        ArgumentNullException.ThrowIfNull(shellContext);

        _confirmationDialog = confirmationDialog;
        _shellContext = shellContext;
        ReviewCompletionCommand = new AsyncCommand(
            ReviewCompletionAsync,
            "The completion confirmation could not be opened.",
            () => _shellContext.Current.IsVaultUnlocked);
        _shellContext.ContextChanged += ShellContext_OnContextChanged;
    }

    public AsyncCommand ReviewCompletionCommand { get; }

    public bool HasUnlockedVault => _shellContext.Current.IsVaultUnlocked;

    private async Task ReviewCompletionAsync(CancellationToken cancellationToken)
    {
        var context = _shellContext.Current;
        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                "Complete recovery session",
                context.SessionDisplayName,
                "Blocked work and unresolved risks will remain recorded in the final recovery status.",
                "Continue to completion review",
                isDestructive: false),
            cancellationToken);

        Status = confirmed
            ? VisualStatusViewModel.Create(
                AppVisualState.Success,
                "Confirmation recorded",
                "This placeholder confirms only the UI flow; functional session completion is not implemented yet.")
            : VisualStatusViewModel.Create(
                AppVisualState.Normal,
                "Completion canceled",
                "No recovery-session state was changed.");
    }

    private void ShellContext_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        OnPropertyChanged(nameof(HasUnlockedVault));
        ReviewCompletionCommand.RaiseCanExecuteChanged();
    }
}
