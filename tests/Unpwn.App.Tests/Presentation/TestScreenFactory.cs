using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application;
using Unpwn.Core;
using Unpwn.Import.Csv;

namespace Unpwn.App.Tests.Presentation;

internal sealed class TestScreenFactory : IScreenFactory
{
    private readonly Dictionary<AppRoute, ScreenViewModel> _screens;

    public TestScreenFactory(
        IConfirmationDialogService confirmationDialog,
        IVaultLifecycleService vaultLifecycle,
        RecoveryWizardSessionService wizard,
        IRecoverySessionService recoverySession,
        IAccountInventoryService accountInventory,
        ILocalizationService localization)
    {
        _screens = new Dictionary<AppRoute, ScreenViewModel>
        {
            [AppRoute.VaultEntry] = new VaultEntryScreenViewModel(
                vaultLifecycle,
                wizard,
                confirmationDialog,
                localization),
            [AppRoute.Dashboard] = new DashboardScreenViewModel(
                recoverySession,
                vaultLifecycle,
                confirmationDialog,
                localization),
            [AppRoute.CsvImport] = new CsvImportScreenViewModel(accountInventory, localization),
            [AppRoute.Accounts] = new AccountInventoryScreenViewModel(
                accountInventory,
                confirmationDialog,
                localization),
            [AppRoute.Workflow] = CreatePlaceholder(
                AppRoute.Workflow,
                localization,
                "Screen.Workflow"),
            [AppRoute.CredentialsExport] = CreatePlaceholder(
                AppRoute.CredentialsExport,
                localization,
                "Screen.Credentials"),
            [AppRoute.Completion] = CreatePlaceholder(
                AppRoute.Completion,
                localization,
                "Screen.Completion"),
        };
    }

    public ScreenViewModel Create(AppRoute route) => _screens[route];

    private static TestPlaceholderScreenViewModel CreatePlaceholder(
        AppRoute route,
        ILocalizationService localization,
        string resourcePrefix) => new(
            route,
            localization,
            $"{resourcePrefix}.Title",
            $"{resourcePrefix}.Description",
            AppVisualState.Normal,
            $"{resourcePrefix}.StatusTitle",
            $"{resourcePrefix}.StatusMessage");
}

internal sealed class TestPlaceholderScreenViewModel(
    AppRoute route,
    ILocalizationService localization,
    string titleResourceKey,
    string descriptionResourceKey,
    AppVisualState statusState,
    string statusTitleResourceKey,
    string statusMessageResourceKey)
    : LocalizedScreenViewModel(
        route,
        localization,
        titleResourceKey,
        descriptionResourceKey,
        statusState,
        statusTitleResourceKey,
        statusMessageResourceKey);

internal sealed class EmptyAccountInventoryService : IAccountInventoryService
{
    public event EventHandler? InventoryChanged;

    public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Locked;

    public AccountInventoryState? CurrentInventory => null;

    public AccountRecoveryOrder? CurrentRecoveryOrder => null;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InventoryChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<AccountInventoryOperationResult> UpsertAsync(
        AccountInventoryUpsertRequest request,
        CancellationToken cancellationToken) => Locked(cancellationToken);

    public Task<AccountInventoryOperationResult> CategorizeAsync(
        Guid accountId,
        AccountRecoveryCategory category,
        CancellationToken cancellationToken) => Locked(cancellationToken);

    public Task<AccountInventoryOperationResult> RemoveAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken) => Locked(cancellationToken);

    public Task<AccountInventoryOperationResult> ImportAsync(
        IReadOnlyCollection<ImportAccountCandidate> candidates,
        ImportDuplicateResolution? duplicateResolution,
        CancellationToken cancellationToken) => Locked(cancellationToken);

    public IReadOnlyList<ExistingAccountReference> GetExistingAccountReferences() => [];

    public void ClearForLock() => InventoryChanged?.Invoke(this, EventArgs.Empty);

    private static Task<AccountInventoryOperationResult> Locked(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AccountInventoryOperationResult.Failure(
            AccountInventoryFailureCode.Locked));
    }
}
