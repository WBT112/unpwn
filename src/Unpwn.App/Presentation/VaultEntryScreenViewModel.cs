using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using Unpwn.App.Localization;
using Unpwn.App.Services;
using Unpwn.Core;

namespace Unpwn.App.Presentation;

public enum VaultEntryStage
{
    Welcome,
    TrustedDeviceCheck,
    TrustedDeviceGuidance,
    SafetyStopped,
    VaultChoice,
    CreateVault,
    OpenVault,
    LockedVault,
    UnlockedVault,
    ChangePassword,
}

public sealed record RecentVaultReferenceViewModel(
    string Path,
    string DisplayName,
    string DisplayText);

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Reveal timers are cancelled whenever inputs are cleared and the screen lives for the application lifetime.")]
public sealed class VaultEntryScreenViewModel : LocalizedScreenViewModel
{
    private const int MinimumNewPasswordLength = 12;
    private readonly IVaultLifecycleService _vaultLifecycle;
    private readonly RecoveryWizardSessionService _wizard;
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly TimeSpan _passwordRevealDuration;
    private readonly IPresentationDelay _passwordRevealDelay;
    private readonly IDiagnosticExportService? _diagnosticExportService;
    private VaultEntryStage _stage;
    private string _createPath = string.Empty;
    private string _openPath = string.Empty;
    private string _createPassword = string.Empty;
    private string _confirmCreatePassword = string.Empty;
    private string _openPassword = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmNewPassword = string.Empty;
    private bool _acknowledgesNonRecoverability;
    private bool _isCreatePasswordRevealed;
    private bool _isOpenPasswordRevealed;
    private bool _isChangePasswordRevealed;
    private string? _validationKey;
    private RecentVaultReferenceViewModel[] _recentVaults = [];
    private RecentVaultReferenceViewModel? _selectedRecentVault;
    private CancellationTokenSource? _createRevealCancellation;
    private CancellationTokenSource? _openRevealCancellation;
    private CancellationTokenSource? _changeRevealCancellation;
    private DiagnosticReportPreview? _diagnosticPreview;
    private string _diagnosticDestinationPath = string.Empty;
    private bool _diagnosticPreviewApproved;

    public VaultEntryScreenViewModel(
        IVaultLifecycleService vaultLifecycle,
        RecoveryWizardSessionService wizard,
        IConfirmationDialogService confirmationDialog,
        ILocalizationService localization,
        TimeSpan? passwordRevealDuration = null,
        IPresentationDelay? passwordRevealDelay = null,
        IDiagnosticExportService? diagnosticExportService = null)
        : base(
            AppRoute.VaultEntry,
            localization,
            "Screen.Vault.Title",
            "Screen.Vault.Description",
            AppVisualState.Warning,
            "Screen.Vault.StatusTitle",
            "Screen.Vault.StatusMessage")
    {
        _vaultLifecycle = vaultLifecycle ?? throw new ArgumentNullException(nameof(vaultLifecycle));
        _wizard = wizard ?? throw new ArgumentNullException(nameof(wizard));
        _confirmationDialog = confirmationDialog ?? throw new ArgumentNullException(nameof(confirmationDialog));
        _passwordRevealDuration = passwordRevealDuration ?? TimeSpan.FromSeconds(15);
        _passwordRevealDelay = passwordRevealDelay ?? SystemPresentationDelay.Instance;
        _diagnosticExportService = diagnosticExportService;
        if (_passwordRevealDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(passwordRevealDuration));
        }

        _stage = GetStage(_vaultLifecycle.Snapshot);
        BeginCommand = new RelayCommand(Begin);
        TrustedDeviceYesCommand = new RelayCommand(() => RecordTrustedDeviceDecision(TrustedDeviceDecision.Trusted));
        TrustedDeviceNoCommand = new RelayCommand(() => RecordTrustedDeviceDecision(TrustedDeviceDecision.NotTrusted));
        TrustedDeviceUnsureCommand = new RelayCommand(() => RecordTrustedDeviceDecision(TrustedDeviceDecision.Unsure));
        BackToWelcomeCommand = new RelayCommand(() => SetStage(VaultEntryStage.Welcome));
        BackToTrustedDeviceCommand = new RelayCommand(ReturnToTrustedDeviceCheck);
        EndForDeviceSafetyCommand = new RelayCommand(EndForDeviceSafety);
        RestartSafetyCheckCommand = new RelayCommand(RestartSafetyCheck);
        ShowCreateVaultCommand = new RelayCommand(() => SetStage(VaultEntryStage.CreateVault));
        ShowOpenVaultCommand = new RelayCommand(() => SetStage(VaultEntryStage.OpenVault));
        BackToVaultChoiceCommand = new RelayCommand(() => SetStage(VaultEntryStage.VaultChoice));
        UseRecentVaultCommand = new RelayCommand(UseRecentVault, () => SelectedRecentVault is not null);
        CreateVaultCommand = new AsyncCommand(
            CreateVaultAsync,
            () => Localization.GetString("Vault.Command.Error"),
            CanCreateVault);
        OpenVaultCommand = new AsyncCommand(
            OpenVaultAsync,
            () => Localization.GetString("Vault.Command.Error"),
            CanOpenVault);
        UnlockCurrentVaultCommand = new AsyncCommand(
            UnlockCurrentVaultAsync,
            () => Localization.GetString("Vault.Command.Error"),
            () => !string.IsNullOrEmpty(OpenPassword) && _vaultLifecycle.Snapshot.CanUnlockCurrent);
        RemoveRecentReferenceCommand = new AsyncCommand(
            RemoveRecentReferenceAsync,
            () => Localization.GetString("Vault.Command.Error"),
            () => SelectedRecentVault is not null);
        DeleteRecentVaultCommand = new AsyncCommand(
            DeleteRecentVaultAsync,
            () => Localization.GetString("Vault.Command.Error"),
            () => SelectedRecentVault is not null);
        ShowChangePasswordCommand = new RelayCommand(
            () => SetStage(VaultEntryStage.ChangePassword),
            () => _vaultLifecycle.Snapshot.IsUnlocked);
        CancelChangePasswordCommand = new RelayCommand(CancelChangePassword);
        ChangePasswordCommand = new AsyncCommand(
            ChangePasswordAsync,
            () => Localization.GetString("Vault.Command.Error"),
            CanChangePassword);
        ContinueCommand = new RelayCommand(
            () => ContinueRequested?.Invoke(this, EventArgs.Empty),
            () => _vaultLifecycle.Snapshot.IsUnlocked);
        CreateDiagnosticPreviewCommand = new RelayCommand(
            CreateDiagnosticPreview,
            () => _diagnosticExportService is not null);
        ExportDiagnosticsCommand = new AsyncCommand(
            ExportDiagnosticsAsync,
            () => Localization.GetString("Vault.Diagnostics.CommandError"),
            CanExportDiagnostics);

        _vaultLifecycle.VaultStateChanged += VaultLifecycle_OnStateChanged;
        RefreshRecentVaults();
    }

    public event EventHandler? ContinueRequested;

    public VaultEntryStage Stage
    {
        get => _stage;
        private set
        {
            if (SetProperty(ref _stage, value))
            {
                NotifyStageProperties();
            }
        }
    }

    public bool IsWelcomeVisible => Stage == VaultEntryStage.Welcome;

    public bool IsTrustedDeviceCheckVisible => Stage == VaultEntryStage.TrustedDeviceCheck;

    public bool IsTrustedDeviceGuidanceVisible => Stage == VaultEntryStage.TrustedDeviceGuidance;

    public bool IsSafetyStoppedVisible => Stage == VaultEntryStage.SafetyStopped;

    public bool IsVaultChoiceVisible => Stage == VaultEntryStage.VaultChoice;

    public bool IsCreateVaultVisible => Stage == VaultEntryStage.CreateVault;

    public bool IsOpenVaultVisible => Stage == VaultEntryStage.OpenVault;

    public bool IsLockedVaultVisible => Stage == VaultEntryStage.LockedVault;

    public bool IsUnlockedVaultVisible => Stage == VaultEntryStage.UnlockedVault;

    public bool IsChangePasswordVisible => Stage == VaultEntryStage.ChangePassword;

    public string CreatePath
    {
        get => _createPath;
        set
        {
            if (SetProperty(ref _createPath, value ?? string.Empty))
            {
                ClearValidation();
                CreateVaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string OpenPath
    {
        get => _openPath;
        set
        {
            if (SetProperty(ref _openPath, value ?? string.Empty))
            {
                ClearValidation();
                OpenVaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CreatePassword
    {
        get => _createPassword;
        set
        {
            if (SetProperty(ref _createPassword, value ?? string.Empty))
            {
                ClearValidation();
                OnPropertyChanged(nameof(CreatePasswordGuidance));
                CreateVaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfirmCreatePassword
    {
        get => _confirmCreatePassword;
        set
        {
            if (SetProperty(ref _confirmCreatePassword, value ?? string.Empty))
            {
                ClearValidation();
                CreateVaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string OpenPassword
    {
        get => _openPassword;
        set
        {
            if (SetProperty(ref _openPassword, value ?? string.Empty))
            {
                ClearValidation();
                OpenVaultCommand.RaiseCanExecuteChanged();
                UnlockCurrentVaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set
        {
            if (SetProperty(ref _currentPassword, value ?? string.Empty))
            {
                ClearValidation();
                ChangePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetProperty(ref _newPassword, value ?? string.Empty))
            {
                ClearValidation();
                OnPropertyChanged(nameof(NewPasswordGuidance));
                ChangePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfirmNewPassword
    {
        get => _confirmNewPassword;
        set
        {
            if (SetProperty(ref _confirmNewPassword, value ?? string.Empty))
            {
                ClearValidation();
                ChangePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool AcknowledgesNonRecoverability
    {
        get => _acknowledgesNonRecoverability;
        set
        {
            if (SetProperty(ref _acknowledgesNonRecoverability, value))
            {
                ClearValidation();
                CreateVaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCreatePasswordRevealed
    {
        get => _isCreatePasswordRevealed;
        set => SetRevealState(
            ref _isCreatePasswordRevealed,
            value,
            ref _createRevealCancellation,
            nameof(IsCreatePasswordRevealed),
            nameof(CreatePasswordCharacter));
    }

    public bool IsOpenPasswordRevealed
    {
        get => _isOpenPasswordRevealed;
        set => SetRevealState(
            ref _isOpenPasswordRevealed,
            value,
            ref _openRevealCancellation,
            nameof(IsOpenPasswordRevealed),
            nameof(OpenPasswordCharacter));
    }

    public bool IsChangePasswordRevealed
    {
        get => _isChangePasswordRevealed;
        set => SetRevealState(
            ref _isChangePasswordRevealed,
            value,
            ref _changeRevealCancellation,
            nameof(IsChangePasswordRevealed),
            nameof(ChangePasswordCharacter));
    }

    public char CreatePasswordCharacter => IsCreatePasswordRevealed ? '\0' : '●';

    public char OpenPasswordCharacter => IsOpenPasswordRevealed ? '\0' : '●';

    public char ChangePasswordCharacter => IsChangePasswordRevealed ? '\0' : '●';

    public string CreatePasswordGuidance => GetPasswordGuidance(CreatePassword);

    public string NewPasswordGuidance => GetPasswordGuidance(NewPassword);

    public string? ValidationMessage => _validationKey is null
        ? null
        : Localization.GetString(_validationKey);

    public bool HasValidationMessage => _validationKey is not null;

    public IReadOnlyList<RecentVaultReferenceViewModel> RecentVaults => _recentVaults;

    public bool HasRecentVaults => _recentVaults.Length > 0;

    public RecentVaultReferenceViewModel? SelectedRecentVault
    {
        get => _selectedRecentVault;
        set
        {
            if (SetProperty(ref _selectedRecentVault, value))
            {
                UseRecentVaultCommand.RaiseCanExecuteChanged();
                RemoveRecentReferenceCommand.RaiseCanExecuteChanged();
                DeleteRecentVaultCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentVaultDisplayName => _vaultLifecycle.Snapshot.CurrentDisplayName ?? string.Empty;

    public string CurrentVaultPath => _vaultLifecycle.Snapshot.CurrentPath ?? string.Empty;

    public RelayCommand BeginCommand { get; }

    public RelayCommand TrustedDeviceYesCommand { get; }

    public RelayCommand TrustedDeviceNoCommand { get; }

    public RelayCommand TrustedDeviceUnsureCommand { get; }

    public RelayCommand BackToWelcomeCommand { get; }

    public RelayCommand BackToTrustedDeviceCommand { get; }

    public RelayCommand EndForDeviceSafetyCommand { get; }

    public RelayCommand RestartSafetyCheckCommand { get; }

    public RelayCommand ShowCreateVaultCommand { get; }

    public RelayCommand ShowOpenVaultCommand { get; }

    public RelayCommand BackToVaultChoiceCommand { get; }

    public RelayCommand UseRecentVaultCommand { get; }

    public AsyncCommand CreateVaultCommand { get; }

    public AsyncCommand OpenVaultCommand { get; }

    public AsyncCommand UnlockCurrentVaultCommand { get; }

    public AsyncCommand RemoveRecentReferenceCommand { get; }

    public AsyncCommand DeleteRecentVaultCommand { get; }

    public RelayCommand ShowChangePasswordCommand { get; }

    public RelayCommand CancelChangePasswordCommand { get; }

    public AsyncCommand ChangePasswordCommand { get; }

    public RelayCommand ContinueCommand { get; }

    public RelayCommand CreateDiagnosticPreviewCommand { get; }

    public AsyncCommand ExportDiagnosticsCommand { get; }

    public bool IsDiagnosticExportAvailable => _diagnosticExportService is not null;

    public bool HasDiagnosticPreview => _diagnosticPreview is not null;

    public string DiagnosticPreviewText => _diagnosticPreview?.Content ?? string.Empty;

    public string DiagnosticDestinationPath
    {
        get => _diagnosticDestinationPath;
        set
        {
            if (SetProperty(ref _diagnosticDestinationPath, value ?? string.Empty))
            {
                ExportDiagnosticsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool DiagnosticPreviewApproved
    {
        get => _diagnosticPreviewApproved;
        set
        {
            if (SetProperty(ref _diagnosticPreviewApproved, value))
            {
                ExportDiagnosticsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        RefreshRecentVaults();
        OnPropertyChanged(nameof(CreatePasswordGuidance));
        OnPropertyChanged(nameof(NewPasswordGuidance));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void Begin()
    {
        _wizard.BeginTrustedDeviceCheck(DateTimeOffset.UtcNow);
        SetStage(VaultEntryStage.TrustedDeviceCheck);
    }

    private void RecordTrustedDeviceDecision(TrustedDeviceDecision decision)
    {
        _wizard.RecordTrustedDeviceDecision(decision, DateTimeOffset.UtcNow);
        SetStage(decision == TrustedDeviceDecision.Trusted
            ? VaultEntryStage.VaultChoice
            : VaultEntryStage.TrustedDeviceGuidance);
    }

    private void ReturnToTrustedDeviceCheck()
    {
        _wizard.ReturnToTrustedDeviceCheck(DateTimeOffset.UtcNow);
        SetStage(VaultEntryStage.TrustedDeviceCheck);
    }

    private void EndForDeviceSafety()
    {
        _wizard.StopAfterTrustedDeviceGuidance(DateTimeOffset.UtcNow);
        ClearSensitiveInputs();
        SetStage(VaultEntryStage.SafetyStopped);
    }

    private void RestartSafetyCheck()
    {
        _wizard.BeginTrustedDeviceCheck(DateTimeOffset.UtcNow);
        SetStage(VaultEntryStage.TrustedDeviceCheck);
    }

    private void UseRecentVault()
    {
        if (SelectedRecentVault is null)
        {
            return;
        }

        OpenPath = SelectedRecentVault.Path;
        SetStage(VaultEntryStage.OpenVault);
    }

    private async Task CreateVaultAsync(CancellationToken cancellationToken)
    {
        if (!ValidateNewVault(CreatePath, CreatePassword, ConfirmCreatePassword, AcknowledgesNonRecoverability))
        {
            return;
        }

        var result = await _vaultLifecycle.CreateAsync(CreatePath, CreatePassword, cancellationToken);
        ClearSensitiveInputs();
        if (!result.Succeeded)
        {
            ShowFailure(result.FailureCode);
            return;
        }

        SetStage(VaultEntryStage.UnlockedVault);
        SetLocalizedStatus(
            AppVisualState.Success,
            "Vault.Result.Created.Title",
            "Vault.Result.Created.Message");
    }

    private async Task OpenVaultAsync(CancellationToken cancellationToken)
    {
        if (!ValidateOpenVault(OpenPath, OpenPassword))
        {
            return;
        }

        var result = await _vaultLifecycle.OpenAsync(OpenPath, OpenPassword, cancellationToken);
        ClearSensitiveInputs();
        if (!result.Succeeded)
        {
            ShowFailure(result.FailureCode);
            return;
        }

        SetStage(VaultEntryStage.UnlockedVault);
        SetLocalizedStatus(
            AppVisualState.Success,
            "Vault.Result.Opened.Title",
            "Vault.Result.Opened.Message");
    }

    private async Task UnlockCurrentVaultAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(OpenPassword))
        {
            SetValidation("Vault.Validation.PasswordRequired");
            return;
        }

        var result = await _vaultLifecycle.UnlockCurrentAsync(OpenPassword, cancellationToken);
        ClearSensitiveInputs();
        if (!result.Succeeded)
        {
            ShowFailure(result.FailureCode);
            return;
        }

        SetStage(VaultEntryStage.UnlockedVault);
        SetLocalizedStatus(
            AppVisualState.Success,
            "Vault.Result.Opened.Title",
            "Vault.Result.Opened.Message");
    }

    private async Task ChangePasswordAsync(CancellationToken cancellationToken)
    {
        if (!ValidatePasswordChange())
        {
            return;
        }

        var result = await _vaultLifecycle.ChangePasswordAsync(
            CurrentPassword,
            NewPassword,
            cancellationToken);
        ClearSensitiveInputs();
        if (!result.Succeeded)
        {
            ShowFailure(result.FailureCode);
            return;
        }

        SetStage(VaultEntryStage.UnlockedVault);
        SetLocalizedStatus(
            AppVisualState.Success,
            "Vault.Result.PasswordChanged.Title",
            "Vault.Result.PasswordChanged.Message");
    }

    private async Task RemoveRecentReferenceAsync(CancellationToken cancellationToken)
    {
        if (SelectedRecentVault is not { } selected)
        {
            return;
        }

        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Vault.Confirm.Remove.Action"),
                selected.Path,
                Localization.GetString("Vault.Confirm.Remove.Consequence"),
                Localization.GetString("Vault.Confirm.Remove.Button"),
                Localization.GetString("Confirmation.Risk.Sensitive"),
                isDestructive: false),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        await _vaultLifecycle.RemoveRecentReferenceAsync(selected.Path, cancellationToken);
        SelectedRecentVault = null;
        SetLocalizedStatus(
            AppVisualState.Success,
            "Vault.Result.ReferenceRemoved.Title",
            "Vault.Result.ReferenceRemoved.Message");
    }

    private async Task DeleteRecentVaultAsync(CancellationToken cancellationToken)
    {
        if (SelectedRecentVault is not { } selected)
        {
            return;
        }

        var confirmed = await _confirmationDialog.ConfirmAsync(
            new SensitiveConfirmationRequest(
                Localization.GetString("Vault.Confirm.Delete.Action"),
                selected.Path,
                Localization.GetString("Vault.Confirm.Delete.Consequence"),
                Localization.GetString("Vault.Confirm.Delete.Button"),
                Localization.GetString("Confirmation.Risk.Destructive"),
                isDestructive: true),
            cancellationToken);
        if (!confirmed)
        {
            return;
        }

        var result = await _vaultLifecycle.DeleteVaultFileAsync(selected.Path, cancellationToken);
        if (!result.Succeeded)
        {
            ShowFailure(result.FailureCode);
            return;
        }

        SelectedRecentVault = null;
        SetLocalizedStatus(
            AppVisualState.Success,
            "Vault.Result.FileDeleted.Title",
            "Vault.Result.FileDeleted.Message");
    }

    private void CancelChangePassword()
    {
        ClearSensitiveInputs();
        SetStage(VaultEntryStage.UnlockedVault);
    }

    private void CreateDiagnosticPreview()
    {
        if (_diagnosticExportService is null)
        {
            return;
        }

        _diagnosticPreview = _diagnosticExportService.CreatePreview();
        DiagnosticPreviewApproved = false;
        OnPropertyChanged(nameof(HasDiagnosticPreview));
        OnPropertyChanged(nameof(DiagnosticPreviewText));
        ExportDiagnosticsCommand.RaiseCanExecuteChanged();
        SetLocalizedStatus(
            AppVisualState.Normal,
            "Vault.Diagnostics.PreviewReady.Title",
            "Vault.Diagnostics.PreviewReady.Message");
    }

    private async Task ExportDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_diagnosticExportService is null || _diagnosticPreview is null)
        {
            return;
        }

        var result = await _diagnosticExportService.ExportAsync(
            _diagnosticPreview,
            DiagnosticDestinationPath,
            DiagnosticPreviewApproved,
            cancellationToken);
        if (!result.Succeeded)
        {
            SetLocalizedStatus(
                AppVisualState.Error,
                "Vault.Diagnostics.ExportFailed.Title",
                $"Vault.Diagnostics.Error.{result.FailureCode}");
            return;
        }

        _diagnosticPreview = null;
        DiagnosticPreviewApproved = false;
        DiagnosticDestinationPath = string.Empty;
        OnPropertyChanged(nameof(HasDiagnosticPreview));
        OnPropertyChanged(nameof(DiagnosticPreviewText));
        SetLocalizedStatus(
            AppVisualState.Success,
            "Vault.Diagnostics.Exported.Title",
            "Vault.Diagnostics.Exported.Message");
    }

    private bool CanExportDiagnostics() =>
        _diagnosticExportService is not null &&
        _diagnosticPreview is not null &&
        DiagnosticPreviewApproved &&
        !string.IsNullOrWhiteSpace(DiagnosticDestinationPath);

    private bool ValidateNewVault(
        string path,
        string password,
        string confirmation,
        bool acknowledged)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetValidation("Vault.Validation.PathRequired");
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetValidation("Vault.Validation.PasswordRequired");
            return false;
        }

        if (password.Length < MinimumNewPasswordLength)
        {
            SetValidation("Vault.Validation.PasswordTooShort");
            return false;
        }

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            SetValidation("Vault.Validation.PasswordMismatch");
            return false;
        }

        if (!acknowledged)
        {
            SetValidation("Vault.Validation.AcknowledgementRequired");
            return false;
        }

        return true;
    }

    private bool ValidateOpenVault(string path, string password)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            SetValidation("Vault.Validation.PathRequired");
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetValidation("Vault.Validation.PasswordRequired");
            return false;
        }

        return true;
    }

    private bool ValidatePasswordChange()
    {
        if (string.IsNullOrEmpty(CurrentPassword))
        {
            SetValidation("Vault.Validation.PasswordRequired");
            return false;
        }

        if (NewPassword.Length < MinimumNewPasswordLength)
        {
            SetValidation("Vault.Validation.PasswordTooShort");
            return false;
        }

        if (!string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal))
        {
            SetValidation("Vault.Validation.PasswordMismatch");
            return false;
        }

        return true;
    }

    private bool CanCreateVault() =>
        !string.IsNullOrWhiteSpace(CreatePath) &&
        CreatePassword.Length >= MinimumNewPasswordLength &&
        string.Equals(CreatePassword, ConfirmCreatePassword, StringComparison.Ordinal) &&
        AcknowledgesNonRecoverability;

    private bool CanOpenVault() =>
        !string.IsNullOrWhiteSpace(OpenPath) &&
        !string.IsNullOrEmpty(OpenPassword);

    private bool CanChangePassword() =>
        _vaultLifecycle.Snapshot.IsUnlocked &&
        !string.IsNullOrEmpty(CurrentPassword) &&
        NewPassword.Length >= MinimumNewPasswordLength &&
        string.Equals(NewPassword, ConfirmNewPassword, StringComparison.Ordinal);

    private void ShowFailure(VaultOperationFailureCode failureCode)
    {
        _validationKey = $"Vault.Failure.{failureCode}";
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        SetLocalizedStatus(
            AppVisualState.Error,
            "Vault.Failure.Title",
            _validationKey);
    }

    private void SetValidation(string key)
    {
        _validationKey = key;
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
    }

    private void ClearValidation()
    {
        if (_validationKey is null)
        {
            return;
        }

        _validationKey = null;
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
    }

    private string GetPasswordGuidance(string password) => password.Length switch
    {
        0 => Localization.GetString("Vault.Password.Guidance.Empty"),
        < MinimumNewPasswordLength => Localization.GetString("Vault.Password.Guidance.Short"),
        _ => Localization.GetString("Vault.Password.Guidance.Good"),
    };

    private void ClearSensitiveInputs()
    {
        CreatePassword = string.Empty;
        ConfirmCreatePassword = string.Empty;
        OpenPassword = string.Empty;
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmNewPassword = string.Empty;
        IsCreatePasswordRevealed = false;
        IsOpenPasswordRevealed = false;
        IsChangePasswordRevealed = false;
    }

    private void SetRevealState(
        ref bool field,
        bool value,
        ref CancellationTokenSource? cancellation,
        string propertyName,
        string passwordCharacterPropertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(passwordCharacterPropertyName);
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;

        if (value)
        {
            cancellation = new CancellationTokenSource();
            _ = HidePasswordAfterDelayAsync(propertyName, cancellation.Token);
        }
    }

    private async Task HidePasswordAfterDelayAsync(
        string propertyName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _passwordRevealDelay.DelayAsync(_passwordRevealDuration, cancellationToken);
            if (propertyName == nameof(IsCreatePasswordRevealed))
            {
                IsCreatePasswordRevealed = false;
            }
            else if (propertyName == nameof(IsOpenPasswordRevealed))
            {
                IsOpenPasswordRevealed = false;
            }
            else
            {
                IsChangePasswordRevealed = false;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void VaultLifecycle_OnStateChanged(object? sender, EventArgs eventArgs)
    {
        RefreshRecentVaults();
        OnPropertyChanged(nameof(CurrentVaultDisplayName));
        OnPropertyChanged(nameof(CurrentVaultPath));
        ShowChangePasswordCommand.RaiseCanExecuteChanged();
        ContinueCommand.RaiseCanExecuteChanged();
        UnlockCurrentVaultCommand.RaiseCanExecuteChanged();

        var snapshot = _vaultLifecycle.Snapshot;
        if (snapshot.Status == VaultLifecycleStatus.Locked)
        {
            ClearSensitiveInputs();
            SetStage(VaultEntryStage.LockedVault);
            SetLocalizedStatus(
                snapshot.LastLockReason == VaultLockReason.Inactivity
                    ? AppVisualState.Warning
                    : AppVisualState.Success,
                snapshot.LastLockReason == VaultLockReason.Inactivity
                    ? "Vault.Inactivity.Locked.Title"
                    : "Vault.Locked.Title",
                snapshot.LastLockReason == VaultLockReason.Inactivity
                    ? "Vault.Inactivity.Locked.Message"
                    : "Vault.Locked.Description");
        }
        else if (snapshot.IsInactivityWarningVisible && snapshot.InactivityLocksAt is { } locksAt)
        {
            Status = new VisualStatusViewModel(
                AppVisualState.Warning,
                Localization.GetString("Status.Warning"),
                "!",
                Localization.GetString("Vault.Inactivity.Warning.Title"),
                Localization.Format("Vault.Inactivity.Warning.Message", locksAt));
        }
    }

    private void RefreshRecentVaults()
    {
        var selectedPath = SelectedRecentVault?.Path;
        _recentVaults =
        [
            .. _vaultLifecycle.RecentVaults.Select(reference => new RecentVaultReferenceViewModel(
                reference.Path,
                reference.DisplayName,
                Localization.Format(
                    "Vault.Recent.Item",
                    reference.DisplayName,
                    reference.LastOpenedAt))),
        ];
        OnPropertyChanged(nameof(RecentVaults));
        OnPropertyChanged(nameof(HasRecentVaults));
        SelectedRecentVault = selectedPath is null
            ? null
            : _recentVaults.SingleOrDefault(reference =>
                string.Equals(reference.Path, selectedPath, StringComparison.Ordinal));
    }

    private void SetStage(VaultEntryStage stage)
    {
        ClearValidation();
        Stage = stage;
    }

    private static VaultEntryStage GetStage(VaultLifecycleSnapshot snapshot) => snapshot.Status switch
    {
        VaultLifecycleStatus.Unlocked => VaultEntryStage.UnlockedVault,
        VaultLifecycleStatus.Locked => VaultEntryStage.LockedVault,
        _ => VaultEntryStage.Welcome,
    };

    private void NotifyStageProperties()
    {
        OnPropertyChanged(nameof(IsWelcomeVisible));
        OnPropertyChanged(nameof(IsTrustedDeviceCheckVisible));
        OnPropertyChanged(nameof(IsTrustedDeviceGuidanceVisible));
        OnPropertyChanged(nameof(IsSafetyStoppedVisible));
        OnPropertyChanged(nameof(IsVaultChoiceVisible));
        OnPropertyChanged(nameof(IsCreateVaultVisible));
        OnPropertyChanged(nameof(IsOpenVaultVisible));
        OnPropertyChanged(nameof(IsLockedVaultVisible));
        OnPropertyChanged(nameof(IsUnlockedVaultVisible));
        OnPropertyChanged(nameof(IsChangePasswordVisible));
    }
}
