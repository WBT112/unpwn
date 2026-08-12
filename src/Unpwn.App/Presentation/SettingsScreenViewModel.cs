using Unpwn.App.Localization;
using Unpwn.App.Services;

namespace Unpwn.App.Presentation;

public sealed class SettingsScreenViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizationService _localization;
    private readonly IDiagnosticExportService _diagnosticExportService;
    private readonly LanguageOptionViewModel[] _languageOptions;
    private LanguageOptionViewModel _selectedLanguage;
    private DiagnosticReportPreview? _diagnosticPreview;
    private string _diagnosticDestinationPath = string.Empty;
    private bool _diagnosticPreviewApproved;
    private VisualStatusViewModel _status;
    private bool _disposed;

    public SettingsScreenViewModel(
        ILocalizationService localization,
        IDiagnosticExportService diagnosticExportService)
    {
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _diagnosticExportService = diagnosticExportService ??
            throw new ArgumentNullException(nameof(diagnosticExportService));
        _languageOptions = BuildLanguageOptions();
        _selectedLanguage = _languageOptions.Single(option =>
            string.Equals(option.Code, _localization.CurrentLanguageCode, StringComparison.Ordinal));
        _status = VisualStatusViewModel.Create(
            AppVisualState.Normal,
            _localization,
            "Settings.Status.Title",
            "Settings.Status.Message");
        CreateDiagnosticPreviewCommand = new RelayCommand(CreateDiagnosticPreview);
        ExportDiagnosticsCommand = new AsyncCommand(
            ExportDiagnosticsAsync,
            () => _localization.GetString("Vault.Diagnostics.CommandError"),
            CanExportDiagnostics);
        _localization.CultureChanged += Localization_OnCultureChanged;
    }

    public ILocalizationService Localization => _localization;

    public string Title => _localization.GetString("Settings.Title");

    public string Description => _localization.GetString("Settings.Description");

    public string LanguageDescription => _localization.GetString("Settings.Language.Description");

    public IReadOnlyList<LanguageOptionViewModel> LanguageOptions => _languageOptions;

    public LanguageOptionViewModel SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!SetProperty(ref _selectedLanguage, value))
            {
                return;
            }

            if (!string.Equals(
                    value.Code,
                    _localization.CurrentLanguageCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                _localization.SetLanguage(value.Code);
            }
        }
    }

    public VisualStatusViewModel Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public RelayCommand CreateDiagnosticPreviewCommand { get; }

    public AsyncCommand ExportDiagnosticsCommand { get; }

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _localization.CultureChanged -= Localization_OnCultureChanged;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private LanguageOptionViewModel[] BuildLanguageOptions() =>
    [
        .. _localization.SupportedLanguages.Select(language => new LanguageOptionViewModel(
            language.Code,
            _localization.GetString(language.DisplayNameKey))),
    ];

    private void CreateDiagnosticPreview()
    {
        _diagnosticPreview = _diagnosticExportService.CreatePreview();
        DiagnosticPreviewApproved = false;
        OnPropertyChanged(nameof(HasDiagnosticPreview));
        OnPropertyChanged(nameof(DiagnosticPreviewText));
        ExportDiagnosticsCommand.RaiseCanExecuteChanged();
        Status = VisualStatusViewModel.Create(
            AppVisualState.Normal,
            _localization,
            "Vault.Diagnostics.PreviewReady.Title",
            "Vault.Diagnostics.PreviewReady.Message");
    }

    private async Task ExportDiagnosticsAsync(CancellationToken cancellationToken)
    {
        if (_diagnosticPreview is null)
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
            Status = VisualStatusViewModel.Create(
                AppVisualState.Error,
                _localization,
                "Vault.Diagnostics.ExportFailed.Title",
                $"Vault.Diagnostics.Error.{result.FailureCode}");
            return;
        }

        _diagnosticPreview = null;
        DiagnosticPreviewApproved = false;
        DiagnosticDestinationPath = string.Empty;
        OnPropertyChanged(nameof(HasDiagnosticPreview));
        OnPropertyChanged(nameof(DiagnosticPreviewText));
        Status = VisualStatusViewModel.Create(
            AppVisualState.Success,
            _localization,
            "Vault.Diagnostics.Exported.Title",
            "Vault.Diagnostics.Exported.Message");
    }

    private bool CanExportDiagnostics() =>
        _diagnosticPreview is not null &&
        DiagnosticPreviewApproved &&
        !string.IsNullOrWhiteSpace(DiagnosticDestinationPath);

    private void Localization_OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        foreach (var language in _localization.SupportedLanguages)
        {
            var option = _languageOptions.Single(candidate => candidate.Code == language.Code);
            option.UpdateDisplayName(_localization.GetString(language.DisplayNameKey));
        }

        var selectedLanguage = _languageOptions.Single(option =>
            string.Equals(option.Code, _localization.CurrentLanguageCode, StringComparison.Ordinal));
        if (!ReferenceEquals(_selectedLanguage, selectedLanguage))
        {
            _selectedLanguage = selectedLanguage;
            OnPropertyChanged(nameof(SelectedLanguage));
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(LanguageDescription));
        Status = VisualStatusViewModel.Create(
            AppVisualState.Normal,
            _localization,
            "Settings.Status.Title",
            "Settings.Status.Message");
    }
}
