using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Core;

namespace Unpwn.App.Presentation;

public sealed record CredentialAccountOption(Guid AccountId, string Label);

public sealed record CredentialExportFormatOption(CredentialExportFormatId Format, string Label);

public sealed class GeneratedCredentialListItemViewModel(
    GeneratedCredentialMetadata metadata,
    string accountLabel,
    string stageLabel) : ObservableObject
{
    private bool _isSelectedForExport;

    public GeneratedCredentialMetadata Metadata { get; } = metadata ?? throw new ArgumentNullException(nameof(metadata));

    public GeneratedCredentialReference Reference => Metadata.Reference;

    public string AccountLabel { get; } = accountLabel;

    public string StageLabel { get; } = stageLabel;

    public bool IsDeleted => Metadata.IsDeleted;

    public bool CanExport => !IsDeleted && Metadata.ConfirmedAt is not null;

    public bool IsSelectedForExport
    {
        get => _isSelectedForExport;
        set => SetProperty(ref _isSelectedForExport, value);
    }
}

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Sensitive cancellation sources are disposed whenever the cached screen deactivates and when timers are replaced.")]
public sealed class CredentialExportScreenViewModel : LocalizedScreenViewModel
{
    private static readonly TimeSpan RevealDuration = TimeSpan.FromSeconds(15);
    private const int ClipboardSeconds = 30;

    private readonly IGeneratedCredentialRepository _repository;
    private readonly IGeneratedCredentialExportService _exportService;
    private readonly IAccountInventoryService _inventory;
    private readonly IShellContextService _shellContext;
    private readonly ICredentialClipboardService _clipboard;
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly IPresentationDelay _delay;
    private IReadOnlyList<CredentialAccountOption> _accounts = [];
    private IReadOnlyList<GeneratedCredentialListItemViewModel> _credentials = [];
    private IReadOnlyList<CredentialExportFormatOption> _formats = [];
    private CredentialAccountOption? _selectedAccount;
    private GeneratedCredentialListItemViewModel? _selectedCredential;
    private CredentialExportFormatOption? _selectedFormat;
    private int _passwordLength = CredentialGenerationPolicy.Default.Length;
    private bool _includeLowercase = true;
    private bool _includeUppercase = true;
    private bool _includeDigits = true;
    private bool _includeSymbols = true;
    private bool _plaintextRiskAcknowledged;
    private string _destinationPath = string.Empty;
    private string _revealedSecret = string.Empty;
    private bool _isSecretRevealed;
    private int _clipboardSecondsRemaining;
    private string? _resultKey;
    private object?[] _resultArguments = [];
    private CancellationTokenSource? _revealCancellation;
    private CancellationTokenSource? _clipboardCancellation;

    public CredentialExportScreenViewModel(
        IGeneratedCredentialRepository repository,
        IGeneratedCredentialExportService exportService,
        IAccountInventoryService inventory,
        IShellContextService shellContext,
        ICredentialClipboardService clipboard,
        IConfirmationDialogService confirmationDialog,
        ILocalizationService localization,
        IPresentationDelay? delay = null)
        : base(
            AppRoute.CredentialsExport,
            localization,
            "Screen.Credentials.Title",
            "Screen.Credentials.Description",
            AppVisualState.Warning,
            "Screen.Credentials.StatusTitle",
            "Screen.Credentials.StatusMessage")
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        _shellContext = shellContext ?? throw new ArgumentNullException(nameof(shellContext));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _confirmationDialog = confirmationDialog ?? throw new ArgumentNullException(nameof(confirmationDialog));
        _delay = delay ?? SystemPresentationDelay.Instance;

        RefreshCommand = Command(RefreshAsync, () => _repository.IsUnlocked);
        GenerateCommand = Command(GenerateAsync, CanGenerate);
        RevealCommand = Command(RevealAsync, CanAccessSelectedSecret);
        HideCommand = new RelayCommand(ClearReveal, () => IsSecretRevealed);
        CopyCommand = Command(CopyAsync, CanAccessSelectedSecret);
        MarkUsedCommand = Command(
            token => MutateSelectedAsync(
                (reference, operationId) => _repository.MarkUsedAsync(reference, operationId, token),
                "Credentials.Result.Used",
                token),
            CanMarkUsed);
        ConfirmCredentialCommand = Command(
            token => MutateSelectedAsync(
                (reference, operationId) => _repository.ConfirmAsync(reference, operationId, token),
                "Credentials.Result.Confirmed",
                token),
            CanConfirmCredential);
        ExportCommand = Command(ExportAsync, CanExport);
        ConfirmImportCommand = Command(ConfirmImportAsync, CanConfirmImport);
        RevokeImportConfirmationCommand = Command(
            token => MutateSelectedAsync(
                (reference, operationId) => _repository.RevokePasswordManagerImportConfirmationAsync(reference, operationId, token),
                "Credentials.Result.ImportRevoked",
                token),
            CanRevokeImport);
        PostponeImportConfirmationCommand = Command(
            token => MutateSelectedAsync(
                (reference, operationId) => _repository.PostponePasswordManagerImportConfirmationAsync(reference, operationId, token),
                "Credentials.Result.ImportPostponed",
                token),
            CanPostponeImport);
        ConfirmCleanupCommand = Command(
            token => MutateSelectedAsync(
                (reference, operationId) => _repository.ConfirmPlaintextExportCleanupAsync(reference, operationId, token),
                "Credentials.Result.CleanupConfirmed",
                token),
            CanConfirmCleanup);
        DeleteCommand = Command(DeleteAsync, CanDelete);

        _inventory.InventoryChanged += Inventory_OnInventoryChanged;
        _shellContext.ContextChanged += ShellContext_OnContextChanged;
        BuildLocalizedOptions();
    }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand GenerateCommand { get; }

    public AsyncCommand RevealCommand { get; }

    public RelayCommand HideCommand { get; }

    public AsyncCommand CopyCommand { get; }

    public AsyncCommand MarkUsedCommand { get; }

    public AsyncCommand ConfirmCredentialCommand { get; }

    public AsyncCommand ExportCommand { get; }

    public AsyncCommand ConfirmImportCommand { get; }

    public AsyncCommand RevokeImportConfirmationCommand { get; }

    public AsyncCommand PostponeImportConfirmationCommand { get; }

    public AsyncCommand ConfirmCleanupCommand { get; }

    public AsyncCommand DeleteCommand { get; }

    public IReadOnlyList<CredentialAccountOption> Accounts
    {
        get => _accounts;
        private set => SetProperty(ref _accounts, value);
    }

    public IReadOnlyList<GeneratedCredentialListItemViewModel> Credentials
    {
        get => _credentials;
        private set
        {
            foreach (var item in _credentials)
            {
                item.PropertyChanged -= CredentialItem_OnPropertyChanged;
            }

            if (SetProperty(ref _credentials, value))
            {
                foreach (var item in _credentials)
                {
                    item.PropertyChanged += CredentialItem_OnPropertyChanged;
                }

                OnPropertyChanged(nameof(HasCredentials));
            }
        }
    }

    public IReadOnlyList<CredentialExportFormatOption> Formats
    {
        get => _formats;
        private set => SetProperty(ref _formats, value);
    }

    public CredentialAccountOption? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (SetProperty(ref _selectedAccount, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public GeneratedCredentialListItemViewModel? SelectedCredential
    {
        get => _selectedCredential;
        set
        {
            if (!SetProperty(ref _selectedCredential, value))
            {
                return;
            }

            ClearReveal();
            NotifySelectedCredentialState();
            RaiseCommandStates();
        }
    }

    public CredentialExportFormatOption? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int PasswordLength
    {
        get => _passwordLength;
        set
        {
            if (SetProperty(ref _passwordLength, value))
            {
                GenerateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IncludeLowercase
    {
        get => _includeLowercase;
        set => SetGenerationOption(ref _includeLowercase, value);
    }

    public bool IncludeUppercase
    {
        get => _includeUppercase;
        set => SetGenerationOption(ref _includeUppercase, value);
    }

    public bool IncludeDigits
    {
        get => _includeDigits;
        set => SetGenerationOption(ref _includeDigits, value);
    }

    public bool IncludeSymbols
    {
        get => _includeSymbols;
        set => SetGenerationOption(ref _includeSymbols, value);
    }

    public bool PlaintextRiskAcknowledged
    {
        get => _plaintextRiskAcknowledged;
        set
        {
            if (SetProperty(ref _plaintextRiskAcknowledged, value))
            {
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value ?? string.Empty))
            {
                ExportCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RevealedSecret
    {
        get => _revealedSecret;
        private set => SetProperty(ref _revealedSecret, value);
    }

    public bool IsSecretRevealed
    {
        get => _isSecretRevealed;
        private set
        {
            if (SetProperty(ref _isSecretRevealed, value))
            {
                HideCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int ClipboardSecondsRemaining
    {
        get => _clipboardSecondsRemaining;
        private set
        {
            if (SetProperty(ref _clipboardSecondsRemaining, value))
            {
                OnPropertyChanged(nameof(IsClipboardCountdownVisible));
                OnPropertyChanged(nameof(ClipboardCountdownText));
            }
        }
    }

    public bool IsClipboardCountdownVisible => ClipboardSecondsRemaining > 0;

    public string ClipboardCountdownText => Localization.Format(
        "Credentials.Clipboard.Countdown",
        ClipboardSecondsRemaining);

    public bool HasCredentials => Credentials.Count > 0;

    public bool HasSelectedCredential => SelectedCredential is not null;

    public bool HasResult => _resultKey is not null;

    public string ResultMessage => _resultKey is null
        ? string.Empty
        : Localization.Format(_resultKey, _resultArguments);

    public string SelectedCredentialStage => SelectedCredential?.StageLabel ?? string.Empty;

    public bool IsImportConfirmationPostponed =>
        SelectedCredential?.Metadata.IsPasswordManagerImportConfirmationPostponed == true;

    public bool IsCleanupPending =>
        SelectedCredential?.Metadata.IsPlaintextExportCleanupPending == true;

    public bool IsImportConfirmed =>
        SelectedCredential?.Metadata.PasswordManagerImportConfirmedAt is not null;

    public override void Activate() => _ = RefreshCommand.ExecuteAsync();

    public override void Deactivate()
    {
        ClearSensitivePresentation();
        base.Deactivate();
    }

    protected override void RefreshLocalization()
    {
        ClearSensitivePresentation();
        base.RefreshLocalization();
        BuildLocalizedOptions();
        RebuildCredentialLabels();
        OnPropertyChanged(nameof(ClipboardCountdownText));
        OnPropertyChanged(nameof(ResultMessage));
        NotifySelectedCredentialState();
    }

    private AsyncCommand Command(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null) =>
        new(execute, () => Localization.GetString("Credentials.Error.Command"), canExecute);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var selectedId = SelectedCredential?.Reference.CredentialId;
        var selectedAccountId = SelectedAccount?.AccountId;
        var inventoryAccounts = _inventory.CurrentInventory?.Accounts ?? [];
        Accounts = [.. inventoryAccounts
            .Select(account => new CredentialAccountOption(account.Id, AccountLabel(account)))
            .OrderBy(account => account.Label, StringComparer.Create(Localization.CurrentCulture, ignoreCase: true))];
        SelectedAccount = Accounts.SingleOrDefault(account => account.AccountId == selectedAccountId) ??
            (Accounts.Count > 0 ? Accounts[0] : null);

        var accountLabels = inventoryAccounts.ToDictionary(account => account.Id, AccountLabel);
        var metadata = await _repository.ListAsync(cancellationToken);
        Credentials = [.. metadata
            .OrderBy(item => item.IsDeleted)
            .ThenByDescending(item => item.GeneratedAt)
            .Select(item => new GeneratedCredentialListItemViewModel(
                item,
                accountLabels.GetValueOrDefault(
                    item.AccountId,
                    Localization.GetString("Credentials.Account.Unavailable")),
                StageLabel(item.Stage)))];
        SelectedCredential = Credentials.SingleOrDefault(item => item.Reference.CredentialId == selectedId) ??
            Credentials.FirstOrDefault(item => !item.IsDeleted) ??
            (Credentials.Count > 0 ? Credentials[0] : null);
        RaiseCommandStates();
    }

    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        if (SelectedAccount is null)
        {
            return;
        }

        ClearResult();
        using var result = await _repository.GenerateAsync(
            SelectedAccount.AccountId,
            new CredentialGenerationPolicy(
                PasswordLength,
                IncludeLowercase,
                IncludeUppercase,
                IncludeDigits,
                IncludeSymbols),
            Guid.NewGuid(),
            cancellationToken);
        if (!result.Succeeded || result.Metadata is null)
        {
            SetFailure(result.FailureCode);
            return;
        }

        var credentialId = result.Metadata.CredentialId;
        await RefreshAsync(cancellationToken);
        SelectedCredential = Credentials.Single(item => item.Reference.CredentialId == credentialId);
        SetResult("Credentials.Result.Generated");
    }

    private async Task RevealAsync(CancellationToken cancellationToken)
    {
        if (SelectedCredential is null)
        {
            return;
        }

        using var lease = await _repository.ReadSecretAsync(
            SelectedCredential.Reference,
            cancellationToken);
        if (lease is null)
        {
            SetFailure(GeneratedCredentialFailureCode.NotFound);
            return;
        }

        ClearReveal();
        RevealedSecret = Encoding.UTF8.GetString(lease.SecretUtf8.Span);
        IsSecretRevealed = true;
        _revealCancellation = new CancellationTokenSource();
        _ = ExpireRevealAsync(_revealCancellation.Token);
    }

    private async Task ExpireRevealAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _delay.DelayAsync(RevealDuration, cancellationToken);
            ClearReveal();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        if (SelectedCredential is null)
        {
            return;
        }

        using var lease = await _repository.ReadSecretAsync(
            SelectedCredential.Reference,
            cancellationToken);
        if (lease is null || !await _clipboard.CopyAsync(lease.SecretUtf8, cancellationToken))
        {
            SetResult("Credentials.Error.ClipboardUnavailable");
            return;
        }

        _clipboardCancellation?.Cancel();
        _clipboardCancellation?.Dispose();
        _clipboardCancellation = new CancellationTokenSource();
        ClipboardSecondsRemaining = ClipboardSeconds;
        SetResult("Credentials.Result.Copied");
        _ = RunClipboardCountdownAsync(_clipboardCancellation.Token);
    }

    private async Task RunClipboardCountdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (ClipboardSecondsRemaining > 0)
            {
                await _delay.DelayAsync(TimeSpan.FromSeconds(1), cancellationToken);
                ClipboardSecondsRemaining--;
            }

            await _clipboard.ClearOwnedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (SelectedFormat is null)
        {
            return;
        }

        ClearResult();
        var accounts = _inventory.CurrentInventory?.Accounts.ToDictionary(account => account.Id) ?? [];
        var selections = Credentials
            .Where(item => item.IsSelectedForExport && !item.IsDeleted)
            .Select(item =>
            {
                accounts.TryGetValue(item.Reference.AccountId, out var account);
                return new CredentialExportSelection(
                    item.Reference,
                    item.AccountLabel,
                    account?.LoginIdentifier,
                    account?.AccountUrl);
            })
            .ToArray();
        var result = await _exportService.ExportAsync(
            new CredentialExportRequest(
                Guid.NewGuid(),
                SelectedFormat.Format,
                DestinationPath,
                selections,
                PlaintextRiskAcknowledged),
            cancellationToken);
        if (!result.Succeeded)
        {
            SetResult(
                result.FileCreated
                    ? "Credentials.Error.ExportStateAfterFile"
                    : $"Credentials.Error.Export.{result.FailureCode}",
                result.DestinationPath ?? string.Empty);
            return;
        }

        await RefreshAsync(cancellationToken);
        SetResult("Credentials.Result.Exported", result.ExportedCredentials, result.DestinationPath ?? string.Empty);
        PlaintextRiskAcknowledged = false;
    }

    private async Task MutateSelectedAsync(
        Func<GeneratedCredentialReference, Guid, Task<GeneratedCredentialOperationResult>> mutation,
        string successKey,
        CancellationToken cancellationToken)
    {
        if (SelectedCredential is null)
        {
            return;
        }

        var credentialId = SelectedCredential.Reference.CredentialId;
        var result = await mutation(SelectedCredential.Reference, Guid.NewGuid());
        if (!result.Succeeded)
        {
            SetFailure(result.FailureCode);
            return;
        }

        await RefreshAsync(cancellationToken);
        SelectedCredential = Credentials.Single(item => item.Reference.CredentialId == credentialId);
        SetResult(successKey);
    }

    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedCredential is null)
        {
            return;
        }

        var item = SelectedCredential;
        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Credentials.Delete.Confirmation.Action"),
                item.AccountLabel,
                Localization.GetString("Credentials.Delete.Confirmation.Consequence"),
                Localization.GetString("Credentials.Delete.Confirmation.Confirm"),
                Localization.GetString("Credentials.Delete.Confirmation.Risk"),
                isDestructive: true),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        ClearSensitivePresentation();
        var result = await _repository.DeleteAsync(item.Reference, Guid.NewGuid(), cancellationToken);
        if (!result.Succeeded)
        {
            SetFailure(result.FailureCode);
            return;
        }

        await RefreshAsync(cancellationToken);
        SetResult("Credentials.Result.Deleted");
    }

    private async Task ConfirmImportAsync(CancellationToken cancellationToken)
    {
        if (SelectedCredential is null)
        {
            return;
        }

        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Credentials.Import.Confirmation.Action"),
                SelectedCredential.AccountLabel,
                Localization.GetString("Credentials.Import.Confirmation.Consequence"),
                Localization.GetString("Credentials.Import.Confirmation.Confirm"),
                Localization.GetString("Confirmation.Risk.Sensitive"),
                isDestructive: false),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await MutateSelectedAsync(
            (reference, operationId) => _repository.ConfirmPasswordManagerImportAsync(
                reference, operationId, cancellationToken),
            "Credentials.Result.ImportConfirmed",
            cancellationToken);
    }

    private bool CanGenerate() =>
        _repository.IsUnlocked && SelectedAccount is not null && PasswordLength is >= 12 and <= 128 &&
        (IncludeLowercase || IncludeUppercase || IncludeDigits || IncludeSymbols);

    private bool CanAccessSelectedSecret() =>
        _repository.IsUnlocked && SelectedCredential is { IsDeleted: false };

    private bool CanMarkUsed() =>
        SelectedCredential is { IsDeleted: false, Metadata.UsedAt: null };

    private bool CanConfirmCredential() =>
        SelectedCredential is { IsDeleted: false, Metadata.UsedAt: not null, Metadata.ConfirmedAt: null };

    private bool CanExport() =>
        _repository.IsUnlocked && SelectedFormat is not null &&
        !string.IsNullOrWhiteSpace(DestinationPath) && PlaintextRiskAcknowledged &&
        Credentials.Any(item => item.IsSelectedForExport) &&
        Credentials.Where(item => item.IsSelectedForExport).All(item => item.CanExport);

    private bool CanConfirmImport() =>
        SelectedCredential is { IsDeleted: false, Metadata.ExportedAt: not null } && !IsImportConfirmed;

    private bool CanRevokeImport() =>
        SelectedCredential is { IsDeleted: false } && IsImportConfirmed;

    private bool CanPostponeImport() =>
        SelectedCredential is { IsDeleted: false, Metadata.ExportedAt: not null } && !IsImportConfirmed;

    private bool CanConfirmCleanup() =>
        SelectedCredential is { IsDeleted: false } && IsCleanupPending;

    private bool CanDelete() => SelectedCredential is { IsDeleted: false };

    private void SetGenerationOption(ref bool field, bool value)
    {
        if (SetProperty(ref field, value))
        {
            GenerateCommand.RaiseCanExecuteChanged();
        }
    }

    private void BuildLocalizedOptions()
    {
        var selectedFormat = SelectedFormat?.Format ?? CredentialExportFormatId.BitwardenCsv;
        Formats = [.. Enum.GetValues<CredentialExportFormatId>()
            .Select(format => new CredentialExportFormatOption(
                format,
                Localization.GetString($"Credentials.Export.Format.{format}")))];
        SelectedFormat = Formats.Single(format => format.Format == selectedFormat);
    }

    private void RebuildCredentialLabels()
    {
        if (Credentials.Count == 0)
        {
            return;
        }

        var selectedId = SelectedCredential?.Reference.CredentialId;
        Credentials = [.. Credentials.Select(item =>
        {
            var replacement = new GeneratedCredentialListItemViewModel(
                item.Metadata,
                item.AccountLabel,
                StageLabel(item.Metadata.Stage))
            {
                IsSelectedForExport = item.IsSelectedForExport,
            };
            return replacement;
        })];
        SelectedCredential = Credentials.SingleOrDefault(item => item.Reference.CredentialId == selectedId);
    }

    private string AccountLabel(AccountInventoryEntry account) =>
        account.AccountName ?? account.LoginIdentifier ?? account.ProviderId;

    private string StageLabel(GeneratedCredentialStage stage) =>
        Localization.GetString($"Credentials.Stage.{stage}");

    private void ClearReveal()
    {
        _revealCancellation?.Cancel();
        _revealCancellation?.Dispose();
        _revealCancellation = null;
        RevealedSecret = string.Empty;
        IsSecretRevealed = false;
    }

    private void ClearSensitivePresentation()
    {
        ClearReveal();
        _clipboardCancellation?.Cancel();
        _clipboardCancellation?.Dispose();
        _clipboardCancellation = null;
        ClipboardSecondsRemaining = 0;
        _ = ClearOwnedClipboardBestEffortAsync();
    }

    private async Task ClearOwnedClipboardBestEffortAsync()
    {
        try
        {
            await _clipboard.ClearOwnedAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void SetFailure(GeneratedCredentialFailureCode code) =>
        SetResult($"Credentials.Error.Repository.{code}");

    private void SetResult(string key, params object?[] arguments)
    {
        _resultKey = key;
        _resultArguments = arguments;
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultMessage));
    }

    private void ClearResult()
    {
        _resultKey = null;
        _resultArguments = [];
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ResultMessage));
    }

    private void Inventory_OnInventoryChanged(object? sender, EventArgs eventArgs) =>
        _ = RefreshCommand.ExecuteAsync();

    private void CredentialItem_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(GeneratedCredentialListItemViewModel.IsSelectedForExport))
        {
            ExportCommand.RaiseCanExecuteChanged();
        }
    }

    private void ShellContext_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        if (!_shellContext.Current.IsVaultUnlocked)
        {
            ClearSensitivePresentation();
            Credentials = [];
            SelectedCredential = null;
        }

        RaiseCommandStates();
    }

    private void NotifySelectedCredentialState()
    {
        OnPropertyChanged(nameof(HasSelectedCredential));
        OnPropertyChanged(nameof(SelectedCredentialStage));
        OnPropertyChanged(nameof(IsImportConfirmationPostponed));
        OnPropertyChanged(nameof(IsCleanupPending));
        OnPropertyChanged(nameof(IsImportConfirmed));
    }

    private void RaiseCommandStates()
    {
        GenerateCommand.RaiseCanExecuteChanged();
        RevealCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        MarkUsedCommand.RaiseCanExecuteChanged();
        ConfirmCredentialCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        ConfirmImportCommand.RaiseCanExecuteChanged();
        RevokeImportConfirmationCommand.RaiseCanExecuteChanged();
        PostponeImportConfirmationCommand.RaiseCanExecuteChanged();
        ConfirmCleanupCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }
}
