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
    private Func<Task<Stream>>? _openSelectedStream;
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
    private bool _isUpdatingMapping;
    private int _previewGeneration;
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

        var selectedFile = files.SingleOrDefault();
        if (selectedFile is null)
        {
            return;
        }

        await LoadCsvAsync(
            selectedFile.Name,
            async () => await selectedFile.OpenReadAsync());
    }

    internal async Task LoadCsvAsync(string fileName, Func<Task<Stream>> openReadAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(openReadAsync);
        _openSelectedStream = openReadAsync;
        var generation = Interlocked.Increment(ref _previewGeneration);

        try
        {
            await using var stream = await openReadAsync();
            using var reader = new StreamReader(stream);
            _analysis = CsvAccountImportService.Analyze(reader);
        }
        catch (IOException)
        {
            if (IsCurrentPreview(generation))
            {
                ShowReadFailure();
            }
            return;
        }
        catch (UnauthorizedAccessException)
        {
            if (IsCurrentPreview(generation))
            {
                ShowReadFailure();
            }
            return;
        }

        if (!IsCurrentPreview(generation))
        {
            return;
        }

        _hasReadFailure = false;
        SelectedFileText.Text = fileName;
        PasswordWarningBorder.IsVisible = _analysis.ContainsPasswordColumns;
        RefreshPasswordWarning();

        _isUpdatingMapping = true;
        try
        {
            SetMappingOptions(_analysis.Headers);
            SelectMapping(ServiceNameColumnCombo, _analysis.SuggestedMapping.ServiceNameColumn);
            SelectMapping(AccountNameColumnCombo, _analysis.SuggestedMapping.AccountNameColumn);
            SelectMapping(LoginIdentifierColumnCombo, _analysis.SuggestedMapping.LoginIdentifierColumn);
            SelectMapping(AccountUrlColumnCombo, _analysis.SuggestedMapping.AccountUrlColumn);
        }
        finally
        {
            _isUpdatingMapping = false;
        }

        _lastDiagnostics = _analysis.Diagnostics;
        _lastCandidates = [];
        _previewCanImport = false;
        _duplicateCandidateCount = 0;
        _importResultKey = null;
        ShowDiagnostics();
        PreviewItems.ItemsSource = null;
        ResetDuplicateResolution();
        RefreshImportControls();
        await EvaluateMappingAndPreviewAsync();
    }

    internal async Task EvaluateMappingAndPreviewAsync()
    {
        if (_openSelectedStream is null || _analysis is null ||
            _localization is null || ViewModel is null)
        {
            return;
        }

        var mapping = CurrentMapping();
        var assessment = CsvAccountImportService.AssessMapping(_analysis, mapping);
        MappingPanel.IsVisible = !assessment.IsComplete;
        RefreshMappingIssues(assessment);
        if (!assessment.IsComplete)
        {
            _ = Interlocked.Increment(ref _previewGeneration);
            _lastDiagnostics = _analysis.Diagnostics;
            _lastCandidates = [];
            _previewCanImport = false;
            _validCandidateCount = 0;
            _duplicateCandidateCount = 0;
            _importResultKey = null;
            _previewSummaryState = PreviewSummaryState.MappingReview;
            ShowDiagnostics();
            ShowCandidates();
            RefreshPreviewSummary();
            ResetDuplicateResolution();
            RefreshImportControls();
            MappingIssuesText.Focus(NavigationMethod.Tab);
            return;
        }

        var generation = Interlocked.Increment(ref _previewGeneration);

        CsvImportPreview preview;
        try
        {
            await using var stream = await _openSelectedStream();
            using var reader = new StreamReader(stream);
            preview = CsvAccountImportService.CreatePreview(
                reader,
                mapping,
                ViewModel?.ExistingAccounts ?? [],
                delimiter: _analysis.Delimiter);
        }
        catch (IOException)
        {
            if (IsCurrentPreview(generation))
            {
                ShowReadFailure();
            }
            return;
        }
        catch (UnauthorizedAccessException)
        {
            if (IsCurrentPreview(generation))
            {
                ShowReadFailure();
            }
            return;
        }

        if (!IsCurrentPreview(generation))
        {
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
        var viewModel = ViewModel;
        if (viewModel is null || !_previewCanImport || _lastCandidates.Count == 0 || _isImporting)
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
            var result = await viewModel.ImportAsync(
                _lastCandidates,
                resolution,
                CancellationToken.None);
            if (!ReferenceEquals(ViewModel, viewModel))
            {
                return;
            }

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
            if (ReferenceEquals(ViewModel, viewModel))
            {
                RefreshImportControls();
            }
        }
    }

    private void CsvImportView_OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        _ = Interlocked.Increment(ref _previewGeneration);
        if (_localization is { } previousLocalization)
        {
            previousLocalization.CultureChanged -= Localization_OnCultureChanged;
        }

        _localization = ViewModel?.Localization;
        _isUpdatingMapping = true;
        try
        {
            if (_localization is null)
            {
                SetEmptyMappingOptions();
                return;
            }

            _localization.CultureChanged += Localization_OnCultureChanged;
            SetMappingOptions(_analysis?.Headers ?? []);
            RefreshLocalizedContent();
        }
        finally
        {
            _isUpdatingMapping = false;
        }
    }

    private void Localization_OnCultureChanged(object? sender, EventArgs eventArgs)
    {
        var serviceMapping = GetMapping(ServiceNameColumnCombo);
        var accountMapping = GetMapping(AccountNameColumnCombo);
        var loginMapping = GetMapping(LoginIdentifierColumnCombo);
        var urlMapping = GetMapping(AccountUrlColumnCombo);
        var duplicateResolution = DuplicateResolutionCombo.SelectedIndex;

        _isUpdatingMapping = true;
        try
        {
            SetMappingOptions(_analysis?.Headers ?? []);
            SelectMapping(ServiceNameColumnCombo, serviceMapping);
            SelectMapping(AccountNameColumnCombo, accountMapping);
            SelectMapping(LoginIdentifierColumnCombo, loginMapping);
            SelectMapping(AccountUrlColumnCombo, urlMapping);
            SetDuplicateResolutionOptions();
            DuplicateResolutionCombo.SelectedIndex = duplicateResolution;
        }
        finally
        {
            _isUpdatingMapping = false;
        }

        RefreshLocalizedContent();
    }

    private void RefreshLocalizedContent()
    {
        if (_localization is null)
        {
            return;
        }

        if (_openSelectedStream is null)
        {
            SelectedFileText.Text = Localization.GetString(
                _hasReadFailure ? "Import.ReadFailure" : "Import.NoFile");
        }

        RefreshPasswordWarning();
        ShowDiagnostics();
        ShowCandidates();
        RefreshPreviewSummary();
        if (_analysis is not null)
        {
            RefreshMappingIssues(CsvAccountImportService.AssessMapping(_analysis, CurrentMapping()));
        }

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

    private async void MappingCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (!_isUpdatingMapping && _localization is not null && ViewModel is not null)
        {
            await EvaluateMappingAndPreviewAsync();
        }
    }

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

        var passwordColumns = _analysis?.DetectedPasswordColumns ?? [];
        var options = new[] { Localization.GetString("Import.Mapping.None") }
            .Concat(headers.Where(header =>
                !passwordColumns.Contains(header, StringComparer.OrdinalIgnoreCase)))
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
        _ = Interlocked.Increment(ref _previewGeneration);
        _openSelectedStream = null;
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
        MappingPanel.IsVisible = false;
        MappingIssuesText.Text = string.Empty;
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

    private CsvColumnMapping CurrentMapping() => new(
        GetMapping(ServiceNameColumnCombo),
        GetMapping(AccountNameColumnCombo),
        GetMapping(LoginIdentifierColumnCombo),
        GetMapping(AccountUrlColumnCombo),
        _analysis?.DetectedPasswordColumns ?? []);

    private bool IsCurrentPreview(int generation) =>
        generation == Volatile.Read(ref _previewGeneration) &&
        _localization is not null &&
        ViewModel is not null;

    private void RefreshMappingIssues(CsvMappingAssessment assessment)
    {
        MappingIssuesText.Text = assessment.IsComplete
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                assessment.Issues.Select(issue =>
                    Localization.GetString($"Import.Mapping.Issue.{issue}")));
        var showAll = assessment.Issues.Any(issue => issue is
            CsvMappingIssue.MissingMappedColumn or
            CsvMappingIssue.RepeatedMappedColumn or
            CsvMappingIssue.PasswordColumnMapped or
            CsvMappingIssue.PasswordColumnNotExcluded);
        ServiceNameMappingField.IsVisible = showAll || assessment.Issues.Any(issue => issue is
            CsvMappingIssue.MissingServiceIdentity or CsvMappingIssue.AmbiguousServiceName);
        AccountUrlMappingField.IsVisible = showAll || assessment.Issues.Any(issue => issue is
            CsvMappingIssue.MissingServiceIdentity or CsvMappingIssue.AmbiguousAccountUrl);
        AccountNameMappingField.IsVisible = showAll || assessment.Issues.Any(issue => issue is
            CsvMappingIssue.MissingAccountIdentity or CsvMappingIssue.AmbiguousAccountName);
        LoginIdentifierMappingField.IsVisible = showAll || assessment.Issues.Any(issue => issue is
            CsvMappingIssue.MissingAccountIdentity or CsvMappingIssue.AmbiguousLoginIdentifier);
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
