using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Unpwn.Providers.Workflows;

namespace Unpwn.App.Presentation;

internal sealed record RecoveryCredentialHandoffServices(
    IGeneratedCredentialRepository Credentials,
    ICredentialClipboardService Clipboard,
    IVaultLifecycleService VaultLifecycle,
    IAccountInventoryService Inventory,
    IAccountRecoveryExecutionService Execution,
    IConfirmationDialogService Confirmation,
    IRecoveryBrowserCredentialAssistanceCatalog AssistanceCatalog);

internal interface IRecoveryCredentialPresentationDelay
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

internal sealed class SystemRecoveryCredentialPresentationDelay : IRecoveryCredentialPresentationDelay
{
    public static SystemRecoveryCredentialPresentationDelay Instance { get; } = new();

    private SystemRecoveryCredentialPresentationDelay()
    {
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Cancellation sources are synchronously canceled/disposed by ClearSensitivePresentation and Dispose.")]
[SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Background clipboard cleanup must fail closed without surfacing exception text that could contain platform clipboard details.")]
public sealed class RecoveryCredentialHandoffViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RevealDuration = TimeSpan.FromSeconds(15);
    private const int ClipboardSeconds = 30;

    private readonly WorkflowExecutionScreenViewModel _workflow;
    private readonly RecoveryCredentialHandoffServices _services;
    private readonly RecoveryBrowserWorkspaceRequest _browserRequest;
    private readonly string _actionDefinitionId;
    private readonly Func<RecoveryBrowserCredentialInsertionContract, CancellationToken,
        Task<RecoveryBrowserCredentialAssistanceResult>> _inspect;
    private readonly Func<RecoveryBrowserCredentialInsertionContract, ReadOnlyMemory<byte>, CancellationToken,
        Task<RecoveryBrowserCredentialAssistanceResult>> _insert;
    private readonly IRecoveryCredentialPresentationDelay _delay;
    private GeneratedCredentialReference? _reference;
    private GeneratedCredentialMetadata? _metadata;
    private RecoveryBrowserCredentialInsertionContract? _assistanceContract;
    private string _revealedSecret = string.Empty;
    private bool _isSecretRevealed;
    private int _clipboardSecondsRemaining;
    private string? _statusKey;
    private CancellationTokenSource? _revealCancellation;
    private CancellationTokenSource? _clipboardCancellation;
    private bool _browserOpen = true;
    private bool _disposed;

    internal RecoveryCredentialHandoffViewModel(
        WorkflowExecutionScreenViewModel workflow,
        RecoveryCredentialHandoffServices services,
        RecoveryBrowserWorkspaceRequest browserRequest,
        string actionDefinitionId,
        Func<RecoveryBrowserCredentialInsertionContract, CancellationToken,
            Task<RecoveryBrowserCredentialAssistanceResult>> inspect,
        Func<RecoveryBrowserCredentialInsertionContract, ReadOnlyMemory<byte>, CancellationToken,
            Task<RecoveryBrowserCredentialAssistanceResult>> insert,
        IRecoveryCredentialPresentationDelay? delay = null)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _browserRequest = browserRequest ?? throw new ArgumentNullException(nameof(browserRequest));
        _actionDefinitionId = string.IsNullOrWhiteSpace(actionDefinitionId)
            ? throw new ArgumentException("An action definition ID is required.", nameof(actionDefinitionId))
            : actionDefinitionId;
        _inspect = inspect ?? throw new ArgumentNullException(nameof(inspect));
        _insert = insert ?? throw new ArgumentNullException(nameof(insert));
        _delay = delay ?? SystemRecoveryCredentialPresentationDelay.Instance;

        RevealCommand = new AsyncCommand(
            RevealAsync,
            () => Localization.GetString("Credentials.Error.Command"),
            CanAccessSecret);
        HideCommand = new RelayCommand(ClearReveal, () => IsSecretRevealed);
        CopyCommand = new AsyncCommand(
            CopyAsync,
            () => Localization.GetString("Credentials.Error.Command"),
            CanAccessSecret);
        MarkUsedCommand = new AsyncCommand(
            MarkUsedAsync,
            () => Localization.GetString("Credentials.Error.Command"),
            CanMarkUsed);
        ConfirmCredentialCommand = new AsyncCommand(
            ConfirmCredentialAsync,
            () => Localization.GetString("Credentials.Error.Command"),
            CanConfirmCredential);
        AssistInsertionCommand = new AsyncCommand(
            AssistInsertionAsync,
            () => Localization.GetString("Credentials.Error.Command"),
            () => CanUseProviderReviewedAssistance);

        _workflow.PropertyChanged += Workflow_OnPropertyChanged;
        _services.VaultLifecycle.ContextChanged += VaultLifecycle_OnContextChanged;
        Localization.CultureChanged += Localization_OnCultureChanged;
        ResolveAssistanceContract();
    }

    private ILocalizationService Localization => _workflow.Localization;

    public AsyncCommand RevealCommand { get; }

    public RelayCommand HideCommand { get; }

    public AsyncCommand CopyCommand { get; }

    public AsyncCommand MarkUsedCommand { get; }

    public AsyncCommand ConfirmCredentialCommand { get; }

    public AsyncCommand AssistInsertionCommand { get; }

    public bool HasCredential => _reference is not null && _metadata is { IsDeleted: false };

    public string CredentialStageText => _metadata is null
        ? string.Empty
        : Localization.GetString($"Credentials.Stage.{_metadata.Stage}");

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

    public bool HasStatus => _statusKey is not null;

    public string StatusMessage => _statusKey is null
        ? string.Empty
        : Localization.GetString(_statusKey);

    public bool CanUseProviderReviewedAssistance =>
        _browserOpen && HasCredential && _assistanceContract is not null &&
        _services.Credentials.IsUnlocked;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await RefreshCredentialAsync(cancellationToken);
    }

    public async Task OnBrowserClosedAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        _browserOpen = false;
        ClearReveal();
        OnPropertyChanged(nameof(CanUseProviderReviewedAssistance));
        AssistInsertionCommand.RaiseCanExecuteChanged();
        await ClearClipboardAsync(showFailure: true, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _workflow.PropertyChanged -= Workflow_OnPropertyChanged;
        _services.VaultLifecycle.ContextChanged -= VaultLifecycle_OnContextChanged;
        Localization.CultureChanged -= Localization_OnCultureChanged;
        ClearReveal();
        _clipboardCancellation?.Cancel();
        _clipboardCancellation?.Dispose();
        _clipboardCancellation = null;
        ClipboardSecondsRemaining = 0;
        _ = ClearOwnedClipboardBestEffortAsync();
        _reference = null;
        _metadata = null;
        GC.SuppressFinalize(this);
    }

    private async Task RefreshCredentialAsync(CancellationToken cancellationToken)
    {
        if (!_workflow.HasCredentialReference || !_services.Credentials.IsUnlocked)
        {
            SetCredential(null, null);
            return;
        }

        var account = _services.Inventory.CurrentInventory?.Accounts.SingleOrDefault(candidate =>
            candidate.Id == _browserRequest.AccountId);
        if (account is null)
        {
            SetCredential(null, null);
            return;
        }

        var reviewedWorkflow = ResolveReviewedWorkflow(account);
        var definition = reviewedWorkflow ??
            RepositoryWorkflowCatalog.CreateGenericManualWorkflow(account.ProviderId);
        var loaded = await _services.Execution.LoadAsync(
            account.Id,
            definition,
            cancellationToken);
        if (reviewedWorkflow is not null &&
            loaded.FailureCode == AccountRecoveryExecutionFailureCode.Corrupted)
        {
            definition = RepositoryWorkflowCatalog.CreateGenericManualWorkflow(account.ProviderId);
            loaded = await _services.Execution.LoadAsync(account.Id, definition, cancellationToken);
        }

        if (!loaded.Succeeded || loaded.State is null)
        {
            SetCredential(null, null);
            return;
        }

        var reference = loaded.State.Actions.SingleOrDefault(action =>
            string.Equals(action.DefinitionId, _actionDefinitionId, StringComparison.Ordinal))
            ?.CredentialReference;
        if (reference is null)
        {
            SetCredential(null, null);
            return;
        }

        var metadata = await _services.Credentials.GetMetadataAsync(reference, cancellationToken);
        SetCredential(reference, metadata);
    }

    private void ResolveAssistanceContract()
    {
        var account = _services.Inventory.CurrentInventory?.Accounts.SingleOrDefault(candidate =>
            candidate.Id == _browserRequest.AccountId);
        _assistanceContract = account is not null &&
            _services.AssistanceCatalog.TryResolve(
                account.ProviderId,
                _actionDefinitionId,
                _workflow.IsReviewedProviderWorkflow,
                _browserRequest.Handoff,
                _browserRequest.ContentMode,
                out var resolved)
            ? resolved
            : null;
        OnPropertyChanged(nameof(CanUseProviderReviewedAssistance));
        AssistInsertionCommand.RaiseCanExecuteChanged();
    }

    private async Task RevealAsync(CancellationToken cancellationToken)
    {
        if (_reference is null)
        {
            return;
        }

        using var lease = await _services.Credentials.ReadSecretAsync(_reference, cancellationToken);
        if (lease is null)
        {
            SetStatus("Credentials.Error.Repository.NotFound");
            return;
        }

        ClearReveal();
        RevealedSecret = Encoding.UTF8.GetString(lease.SecretUtf8.Span);
        IsSecretRevealed = true;
        SetStatus("Credentials.Secret.RevealedAnnouncement");
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
        if (_reference is null)
        {
            return;
        }

        using var lease = await _services.Credentials.ReadSecretAsync(_reference, cancellationToken);
        if (lease is null)
        {
            SetStatus("Credentials.Error.Repository.NotFound");
            return;
        }

        if (!await _services.Clipboard.CopyAsync(lease.SecretUtf8, cancellationToken))
        {
            SetStatus("Credentials.Error.ClipboardUnavailable");
            return;
        }

        _clipboardCancellation?.Cancel();
        _clipboardCancellation?.Dispose();
        _clipboardCancellation = new CancellationTokenSource();
        ClipboardSecondsRemaining = ClipboardSeconds;
        SetStatus("Credentials.Result.Copied");
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

            await _services.Clipboard.ClearOwnedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ClipboardSecondsRemaining = 0;
            SetStatus("Credentials.Error.ClipboardClearFailed");
        }
    }

    private async Task MarkUsedAsync(CancellationToken cancellationToken)
    {
        if (_reference is null)
        {
            return;
        }

        var result = await _services.Credentials.MarkUsedAsync(
            _reference,
            Guid.NewGuid(),
            cancellationToken);
        ApplyCredentialMutation(result, "Credentials.Result.Used");
    }

    private async Task ConfirmCredentialAsync(CancellationToken cancellationToken)
    {
        if (_reference is null)
        {
            return;
        }

        var result = await _services.Credentials.ConfirmAsync(
            _reference,
            Guid.NewGuid(),
            cancellationToken);
        ApplyCredentialMutation(result, "Credentials.Result.Confirmed");
    }

    private async Task AssistInsertionAsync(CancellationToken cancellationToken)
    {
        var contract = _assistanceContract;
        var reference = _reference;
        if (contract is null || reference is null || !_browserOpen)
        {
            SetStatus("Credentials.Assistance.Unavailable");
            return;
        }

        var coordinator = new RecoveryBrowserCredentialInsertionCoordinator(_services.Credentials);
        var outcome = await coordinator.ExecuteAsync(
            reference,
            contract,
            token => _services.Confirmation.ConfirmAsync(
                new SensitiveConfirmationRequest(
                    Localization.GetString("Credentials.Assistance.Confirmation.Action"),
                    _workflow.AccountName,
                    Localization.GetString("Credentials.Assistance.Confirmation.Consequence"),
                    Localization.GetString("Credentials.Assistance.Confirmation.Confirm"),
                    Localization.GetString("Confirmation.Risk.Sensitive"),
                    isDestructive: false),
                token),
            _inspect,
            _insert,
            cancellationToken);

        switch (outcome.Code)
        {
            case RecoveryBrowserCredentialInsertionOutcomeCode.AuthorizationDenied:
                SetStatus("Credentials.Assistance.Denied");
                break;
            case RecoveryBrowserCredentialInsertionOutcomeCode.InspectionStopped:
            case RecoveryBrowserCredentialInsertionOutcomeCode.InsertionStopped:
                SetAssistanceStatus(outcome.BrowserResult!);
                break;
            case RecoveryBrowserCredentialInsertionOutcomeCode.CredentialUnavailable:
                SetStatus("Credentials.Error.Repository.NotFound");
                break;
            case RecoveryBrowserCredentialInsertionOutcomeCode.InsertedStateSaveFailed:
                SetStatus("Credentials.Assistance.InsertedStateSaveFailed");
                break;
            case RecoveryBrowserCredentialInsertionOutcomeCode.InsertedAndRecordedUsed:
                _metadata = outcome.Metadata;
                NotifyCredentialState();
                SetStatus("Credentials.Assistance.Inserted");
                break;
            default:
                SetStatus("Credentials.Assistance.Unavailable");
                break;
        }
    }

    private async Task ClearClipboardAsync(bool showFailure, CancellationToken cancellationToken)
    {
        _clipboardCancellation?.Cancel();
        _clipboardCancellation?.Dispose();
        _clipboardCancellation = null;
        ClipboardSecondsRemaining = 0;
        try
        {
            await _services.Clipboard.ClearOwnedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            if (showFailure)
            {
                SetStatus("Credentials.Error.ClipboardClearFailed");
            }
        }
    }

    private async Task ClearOwnedClipboardBestEffortAsync()
    {
        try
        {
            await _services.Clipboard.ClearOwnedAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }
    }

    private void ApplyCredentialMutation(
        GeneratedCredentialOperationResult result,
        string successKey)
    {
        if (!result.Succeeded || result.Metadata is null)
        {
            SetStatus($"Credentials.Error.Repository.{result.FailureCode}");
            return;
        }

        _metadata = result.Metadata;
        NotifyCredentialState();
        SetStatus(successKey);
    }

    private void SetAssistanceStatus(RecoveryBrowserCredentialAssistanceResult result)
    {
        var key = result.FailureCode == RecoveryBrowserCredentialAssistanceFailureCode.WrongOrigin
            ? "Credentials.Assistance.WrongOrigin"
            : result.State switch
            {
                RecoveryBrowserCredentialAssistanceState.PausedForMfa =>
                    "Credentials.Assistance.PausedForMfa",
                RecoveryBrowserCredentialAssistanceState.PausedForCaptcha =>
                    "Credentials.Assistance.PausedForCaptcha",
                RecoveryBrowserCredentialAssistanceState.PausedForEmailLink =>
                    "Credentials.Assistance.PausedForEmailLink",
                RecoveryBrowserCredentialAssistanceState.ManualGuidanceRequired =>
                    "Credentials.Assistance.ManualGuidanceRequired",
                _ => "Credentials.Assistance.Unavailable",
            };
        SetStatus(key);
    }

    private void SetCredential(
        GeneratedCredentialReference? reference,
        GeneratedCredentialMetadata? metadata)
    {
        if (reference is null || metadata is null || metadata.IsDeleted)
        {
            ClearReveal();
            _reference = null;
            _metadata = null;
        }
        else
        {
            _reference = reference;
            _metadata = metadata;
        }

        NotifyCredentialState();
    }

    private void NotifyCredentialState()
    {
        OnPropertyChanged(nameof(HasCredential));
        OnPropertyChanged(nameof(CredentialStageText));
        OnPropertyChanged(nameof(CanUseProviderReviewedAssistance));
        RaiseCommandStates();
    }

    private void RaiseCommandStates()
    {
        RevealCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        MarkUsedCommand.RaiseCanExecuteChanged();
        ConfirmCredentialCommand.RaiseCanExecuteChanged();
        AssistInsertionCommand.RaiseCanExecuteChanged();
        HideCommand.RaiseCanExecuteChanged();
    }

    private bool CanAccessSecret() =>
        HasCredential && _services.Credentials.IsUnlocked;

    private bool CanMarkUsed() =>
        HasCredential && _services.Credentials.IsUnlocked && _metadata?.UsedAt is null;

    private bool CanConfirmCredential() =>
        HasCredential && _services.Credentials.IsUnlocked &&
        _metadata is { UsedAt: not null, ConfirmedAt: null };

    private void ClearReveal()
    {
        _revealCancellation?.Cancel();
        _revealCancellation?.Dispose();
        _revealCancellation = null;
        RevealedSecret = string.Empty;
        IsSecretRevealed = false;
    }

    private void SetStatus(string key)
    {
        _statusKey = key;
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void Workflow_OnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(WorkflowExecutionScreenViewModel.HasCredentialReference))
        {
            _ = RefreshCredentialSafelyAsync();
        }
    }

    private async Task RefreshCredentialSafelyAsync()
    {
        try
        {
            await RefreshCredentialAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            SetStatus("Credentials.Error.Command");
        }
    }

    private void VaultLifecycle_OnContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_services.VaultLifecycle.Current.IsVaultUnlocked)
        {
            RaiseCommandStates();
            return;
        }

        ClearReveal();
        _reference = null;
        _metadata = null;
        NotifyCredentialState();
        _ = ClearClipboardAfterLockAsync();
    }

    private async Task ClearClipboardAfterLockAsync()
    {
        await ClearClipboardAsync(showFailure: true, CancellationToken.None);
    }

    private void Localization_OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        ClearReveal();
        OnPropertyChanged(nameof(CredentialStageText));
        OnPropertyChanged(nameof(ClipboardCountdownText));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private static RecoveryWorkflowDefinition? ResolveReviewedWorkflow(AccountInventoryEntry account)
    {
        var accountHost = Uri.TryCreate(account.AccountUrl, UriKind.Absolute, out var accountUri)
            ? accountUri.Host
            : null;
        return RepositoryWorkflowCatalog.Workflows.SingleOrDefault(workflow =>
            string.Equals(workflow.ProviderId, account.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(workflow.ProviderName, account.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(workflow.ProviderId, accountHost, StringComparison.OrdinalIgnoreCase));
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
