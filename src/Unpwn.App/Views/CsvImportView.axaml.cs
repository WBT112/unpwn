using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Import.Csv;

namespace Unpwn.App.Views;

public partial class CsvImportView : AccessibleScreen
{
    private IStorageFile? _selectedFile;
    private CsvImportAnalysis? _analysis;
    private ILocalizationService? _localization;
    private IReadOnlyList<CsvImportDiagnostic> _lastDiagnostics = [];
    private IReadOnlyList<ImportAccountCandidate> _lastCandidates = [];
    private PreviewSummaryState _previewSummaryState = PreviewSummaryState.Initial;
    private int _validCandidateCount;
    private int _duplicateCandidateCount;
    private bool _hasReadFailure;
    private bool _previewCanImport;
    private bool _isImporting;
    private string? _importResultKey;

    public CsvImportView()
    {
        InitializeComponent();
        DataContextChanged += CsvImportView_OnDataContextChanged;
        SetEmptyMappingOptions();
    }

    private ILocalizationService Localization => _localization
        ?? throw new InvalidOperationException("CSV import localization is unavailable.");

    private CsvImportScreenViewModel? ViewModel => DataContext as CsvImportScreenViewModel;

    private async void OpenCsvButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            ShowReadFailure();
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localization.GetString("Import.FilePicker.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localization.GetString("Import.FilePicker.Type"))
                {
                    Patterns = ["*.csv"],
                    MimeTypes = ["text/csv", "text/plain"],
                },
            ],
        });

        _selectedFile = files.SingleOrDefault();
        if (_selectedFile is null)
        {
            return;
        }

        try
        {
            await using var stream = await _selectedFile.OpenReadAsync();
            using var reader = new StreamReader(stream);
            _analysis = CsvAccountImportService.Analyze(reader);
        }
        catch (IOException)
        {
            ShowReadFailure();
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ShowReadFailure();
            return;
        }

        _hasReadFailure = false;
        SelectedFileText.Text = _selectedFile.Name;
        ExcludePasswordsCheckBox.IsChecked = false;
        PasswordWarningBorder.IsVisible = _analysis.ContainsPasswordColumns;
        RefreshPasswordWarning();

        SetMappingOptions(_analysis.Headers);
        SelectMapping(ServiceNameColumnCombo, _analysis.SuggestedMapping.ServiceNameColumn);
        SelectMapping(AccountNameColumnCombo, _analysis.SuggestedMapping.AccountNameColumn);
        SelectMapping(LoginIdentifierColumnCombo, _analysis.SuggestedMapping.LoginIdentifierColumn);
        SelectMapping(AccountUrlColumnCombo, _analysis.SuggestedMapping.AccountUrlColumn);
        _lastDiagnostics = _analysis.Diagnostics;
        _lastCandidates = [];
        _previewCanImport = false;
        _duplicateCandidateCount = 0;
        _importResultKey = null;
        ShowDiagnostics();
        PreviewItems.ItemsSource = null;
        _previewSummaryState = PreviewSummaryState.MappingReview;
        RefreshPreviewSummary();
        ResetDuplicateResolution();
        RefreshPreviewButton();
        RefreshImportControls();
        ServiceNameColumnCombo.Focus(NavigationMethod.Tab);
    }

    private async void CreatePreviewButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (_selectedFile is null || _analysis is null)
        {
            return;
        }

        var excludedPasswordColumns = ExcludePasswordsCheckBox.IsChecked == true
            ? _analysis.DetectedPasswordColumns
            : [];
        var mapping = new CsvColumnMapping(
            GetMapping(ServiceNameColumnCombo),
            GetMapping(AccountNameColumnCombo),
            GetMapping(LoginIdentifierColumnCombo),
            GetMapping(AccountUrlColumnCombo),
            excludedPasswordColumns);

        CsvImportPreview preview;
        try
        {
            await using var stream = await _selectedFile.OpenReadAsync();
            using var reader = new StreamReader(stream);
            preview = CsvAccountImportService.CreatePreview(
                reader,
                mapping,
                ViewModel?.ExistingAccounts ?? [],
                delimiter: _analysis.Delimiter);
        }
        catch (IOException)
        {
            ShowReadFailure();
            return;
        }
        catch (UnauthorizedAccessException)
        {
            ShowReadFailure();
            return;
        }

        _lastDiagnostics = preview.Diagnostics;
        _lastCandidates = preview.Candidates;
        _previewCanImport = preview.CanImport;
        _importResultKey = null;
        ShowDiagnostics();
        ShowCandidates();
        _validCandidateCount = preview.Candidates.Count;
        _duplicateCandidateCount = preview.Candidates.Count(candidate =>
            candidate.DuplicateKind != CsvDuplicateKind.None);
        _previewSummaryState = preview.CanImport
            ? PreviewSummaryState.Valid
            : PreviewSummaryState.NotReady;
        RefreshPreviewSummary();
        ResetDuplicateResolution();
        RefreshImportControls();
        PreviewSummaryText.Focus(NavigationMethod.Tab);
    }

    private async void ImportReviewedButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel is null || !_previewCanImport || _lastCandidates.Count == 0 || _isImporting)
        {
            return;
        }

        ImportDuplicateResolution? resolution = DuplicateResolutionCombo.SelectedIndex switch
        {
            0 => ImportDuplicateResolution.SkipDuplicates,
            1 => ImportDuplicateResolution.ImportAsSeparateAccounts,
            _ => null,
        };
        _isImporting = true;
        RefreshImportControls();
        try
        {
            var result = await ViewModel.ImportAsync(
                _lastCandidates,
                resolution,
                CancellationToken.None);
            _importResultKey = CsvImportScreenViewModel.GetImportResultResourceKey(result);
            if (result.Succeeded)
            {
                _previewCanImport = false;
            }

            ImportResultText.Focus(NavigationMethod.Tab);
        }
        finally
        {
            _isImporting = false;
            RefreshImportControls();
        }
    }

    private void CsvImportView_OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_localization is { } previousLocalization)
        {
            previousLocalization.CultureChanged -= Localization_OnCultureChanged;
        }

        _localization = ViewModel?.Localization;
        if (_localization is null)
        {
            SetEmptyMappingOptions();
            return;
        }

        _localization.CultureChanged += Localization_OnCultureChanged;
        SetMappingOptions(_analysis?.Headers ?? []);
        RefreshLocalizedContent();
    }

    private void Localization_OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        var serviceMapping = GetMapping(ServiceNameColumnCombo);
        var accountMapping = GetMapping(AccountNameColumnCombo);
        var loginMapping = GetMapping(LoginIdentifierColumnCombo);
        var urlMapping = GetMapping(AccountUrlColumnCombo);
        var duplicateResolution = DuplicateResolutionCombo.SelectedIndex;

        SetMappingOptions(_analysis?.Headers ?? []);
        SelectMapping(ServiceNameColumnCombo, serviceMapping);
        SelectMapping(AccountNameColumnCombo, accountMapping);
        SelectMapping(LoginIdentifierColumnCombo, loginMapping);
        SelectMapping(AccountUrlColumnCombo, urlMapping);
        SetDuplicateResolutionOptions();
        DuplicateResolutionCombo.SelectedIndex = duplicateResolution;
        RefreshLocalizedContent();
    }

    private void RefreshLocalizedContent()
    {
        if (_localization is null)
        {
            return;
        }

        if (_selectedFile is null)
        {
            SelectedFileText.Text = Localization.GetString(
                _hasReadFailure ? "Import.ReadFailure" : "Import.NoFile");
        }

        RefreshPasswordWarning();
        ShowDiagnostics();
        ShowCandidates();
        RefreshPreviewSummary();
        RefreshPreviewButton();
        RefreshImportControls();
    }

    private void RefreshPasswordWarning()
    {
        PasswordWarningText.Text = _analysis?.ContainsPasswordColumns == true
            ? Localization.Format(
                "Import.Password.Warning",
                string.Join(", ", _analysis.DetectedPasswordColumns))
            : string.Empty;
    }

    private void ExcludePasswordsCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs eventArgs) =>
        RefreshPreviewButton();

    private void MappingCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) =>
        RefreshPreviewButton();

    private void DuplicateResolutionCombo_OnSelectionChanged(
        object? sender,
        SelectionChangedEventArgs eventArgs) => RefreshImportControls();

    private void SetEmptyMappingOptions()
    {
        var options = Array.Empty<string>();
        ServiceNameColumnCombo.ItemsSource = options;
        AccountNameColumnCombo.ItemsSource = options;
        LoginIdentifierColumnCombo.ItemsSource = options;
        AccountUrlColumnCombo.ItemsSource = options;
        DuplicateResolutionCombo.ItemsSource = options;
    }

    private void SetMappingOptions(IReadOnlyList<string> headers)
    {
        if (_localization is null)
        {
            SetEmptyMappingOptions();
            return;
        }

        var options = new[] { Localization.GetString("Import.Mapping.None") }
            .Concat(headers)
            .ToArray();
        ServiceNameColumnCombo.ItemsSource = options;
        AccountNameColumnCombo.ItemsSource = options;
        LoginIdentifierColumnCombo.ItemsSource = options;
        AccountUrlColumnCombo.ItemsSource = options;

        ServiceNameColumnCombo.SelectedIndex = 0;
        AccountNameColumnCombo.SelectedIndex = 0;
        LoginIdentifierColumnCombo.SelectedIndex = 0;
        AccountUrlColumnCombo.SelectedIndex = 0;
        SetDuplicateResolutionOptions();
    }

    private void SetDuplicateResolutionOptions()
    {
        if (_localization is null)
        {
            DuplicateResolutionCombo.ItemsSource = Array.Empty<string>();
            return;
        }

        DuplicateResolutionCombo.ItemsSource = new[]
        {
            Localization.GetString("Import.Resolution.Skip"),
            Localization.GetString("Import.Resolution.Separate"),
        };
    }

    private void ResetDuplicateResolution()
    {
        SetDuplicateResolutionOptions();
        DuplicateResolutionPanel.IsVisible = _duplicateCandidateCount > 0;
        DuplicateResolutionCombo.SelectedIndex = _duplicateCandidateCount > 0 ? 0 : -1;
    }

    private static void SelectMapping(ComboBox comboBox, string? mapping)
    {
        if (mapping is null)
        {
            comboBox.SelectedIndex = 0;
            return;
        }

        var options = comboBox.ItemsSource?.Cast<string>().ToArray() ?? [];
        var index = Array.FindIndex(options, option =>
            string.Equals(option, mapping, StringComparison.Ordinal));
        comboBox.SelectedIndex = index >= 1 ? index : 0;
    }

    private static string? GetMapping(ComboBox comboBox) =>
        comboBox.SelectedIndex > 0 ? comboBox.SelectedItem as string : null;

    private void RefreshPreviewButton()
    {
        var hasRequiredMapping =
            (GetMapping(ServiceNameColumnCombo) is not null || GetMapping(AccountUrlColumnCombo) is not null) &&
            (GetMapping(LoginIdentifierColumnCombo) is not null || GetMapping(AccountNameColumnCombo) is not null);
        var passwordsConfirmed =
            _analysis?.ContainsPasswordColumns != true || ExcludePasswordsCheckBox.IsChecked == true;
        CreatePreviewButton.IsEnabled = _selectedFile is not null && hasRequiredMapping && passwordsConfirmed;
    }

    private void RefreshImportControls()
    {
        var duplicateResolutionComplete =
            _duplicateCandidateCount == 0 || DuplicateResolutionCombo.SelectedIndex is 0 or 1;
        ImportReviewedButton.IsEnabled = _previewCanImport && duplicateResolutionComplete && !_isImporting;
        ImportResultText.Text = _importResultKey is null
            ? string.Empty
            : Localization.GetString(_importResultKey);
    }

    private void ShowDiagnostics()
    {
        if (_localization is null)
        {
            return;
        }

        DiagnosticsItems.ItemsSource = _lastDiagnostics
            .Select(FormatDiagnostic)
            .ToArray();
    }

    private string FormatDiagnostic(CsvImportDiagnostic diagnostic)
    {
        var severity = Localization.GetString($"Import.Severity.{diagnostic.Severity}");
        var message = diagnostic.Code == "ReadFailure"
            ? Localization.GetString("Import.ReadFailure")
            : Localization.GetString($"Import.Diagnostic.{diagnostic.Code}");
        return diagnostic.RowNumber is { } rowNumber
            ? Localization.Format("Import.Diagnostic.WithRow", severity, rowNumber, message)
            : Localization.Format("Import.Diagnostic.WithoutRow", severity, message);
    }

    private void ShowCandidates()
    {
        if (_localization is null)
        {
            return;
        }

        PreviewItems.ItemsSource = _lastCandidates.Select(FormatCandidate).ToArray();
    }

    private void RefreshPreviewSummary()
    {
        if (_localization is null)
        {
            return;
        }

        PreviewSummaryText.Text = _previewSummaryState switch
        {
            PreviewSummaryState.Initial => Localization.GetString("Import.Preview.Initial"),
            PreviewSummaryState.MappingReview => Localization.GetString("Import.Preview.MappingReview"),
            PreviewSummaryState.Valid => Localization.FormatPlural(
                "Import.Preview.ValidAccounts",
                _validCandidateCount,
                _validCandidateCount,
                _duplicateCandidateCount),
            PreviewSummaryState.NotReady => Localization.GetString("Import.Preview.NotReady"),
            PreviewSummaryState.SelectAnother => Localization.GetString("Import.SelectAnother"),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    private void ShowReadFailure()
    {
        _selectedFile = null;
        _analysis = null;
        _hasReadFailure = true;
        _lastCandidates = [];
        _lastDiagnostics =
        [
            new CsvImportDiagnostic(
                CsvImportDiagnosticSeverity.Error,
                "ReadFailure",
                string.Empty),
        ];
        _previewCanImport = false;
        _duplicateCandidateCount = 0;
        _importResultKey = null;
        SelectedFileText.Text = Localization.GetString("Import.ReadFailure");
        PasswordWarningBorder.IsVisible = false;
        PreviewItems.ItemsSource = null;
        DiagnosticsItems.ItemsSource = new[]
        {
            Localization.Format(
                "Import.Diagnostic.WithoutRow",
                Localization.GetString("Import.Severity.Error"),
                Localization.GetString("Import.ReadFailure")),
        };
        _previewSummaryState = PreviewSummaryState.SelectAnother;
        RefreshPreviewSummary();
        SetMappingOptions([]);
        ResetDuplicateResolution();
        RefreshPreviewButton();
        RefreshImportControls();
        DiagnosticsItems.Focus(NavigationMethod.Tab);
    }

    private string FormatCandidate(ImportAccountCandidate candidate)
    {
        var service = candidate.ServiceName ?? candidate.AccountUrl ??
            Localization.GetString("Import.UnknownService");
        var account = candidate.LoginIdentifier ?? candidate.AccountName ??
            Localization.GetString("Import.UnknownAccount");
        var duplicate = candidate.DuplicateKind == CsvDuplicateKind.None
            ? string.Empty
            : Localization.Format(
                "Import.Candidate.Duplicate",
                Localization.GetString(candidate.DuplicateKind switch
                {
                    CsvDuplicateKind.WithinImport => "Import.Duplicate.WithinImport",
                    CsvDuplicateKind.ExistingAccount => "Import.Duplicate.ExistingAccount",
                    CsvDuplicateKind.WithinImport | CsvDuplicateKind.ExistingAccount =>
                        "Import.Duplicate.WithinAndExisting",
                    _ => throw new ArgumentOutOfRangeException(nameof(candidate)),
                }));
        return Localization.Format(
            "Import.Candidate.Row",
            candidate.RowNumber,
            service,
            account,
            duplicate);
    }

    private enum PreviewSummaryState
    {
        Initial,
        MappingReview,
        Valid,
        NotReady,
        SelectAnother,
    }
}
