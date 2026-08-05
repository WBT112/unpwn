using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Unpwn.Import.Csv;

namespace Unpwn.App;

public partial class MainWindow : Window
{
    private const string NoMapping = "(not mapped)";
    private IStorageFile? _selectedFile;
    private CsvImportAnalysis? _analysis;

    public MainWindow()
    {
        InitializeComponent();
        SetMappingOptions([]);
    }

    private async void OpenCsvButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose account CSV file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV files")
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

        SelectedFileText.Text = _selectedFile.Name;
        ExcludePasswordsCheckBox.IsChecked = false;
        PasswordWarningBorder.IsVisible = _analysis.ContainsPasswordColumns;
        PasswordWarningText.Text = _analysis.ContainsPasswordColumns
            ? $"{CsvImportAnalysis.PasswordWarning} Detected: {string.Join(", ", _analysis.DetectedPasswordColumns)}"
            : string.Empty;

        SetMappingOptions(_analysis.Headers);
        SelectMapping(ServiceNameColumnCombo, _analysis.SuggestedMapping.ServiceNameColumn);
        SelectMapping(AccountNameColumnCombo, _analysis.SuggestedMapping.AccountNameColumn);
        SelectMapping(LoginIdentifierColumnCombo, _analysis.SuggestedMapping.LoginIdentifierColumn);
        SelectMapping(AccountUrlColumnCombo, _analysis.SuggestedMapping.AccountUrlColumn);
        ShowDiagnostics(_analysis.Diagnostics);
        PreviewItems.ItemsSource = null;
        PreviewSummaryText.Text = "Review the mapping, then create the import preview.";
        RefreshPreviewButton();
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
            preview = CsvAccountImportService.CreatePreview(reader, mapping, delimiter: _analysis.Delimiter);
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

        ShowDiagnostics(preview.Diagnostics);
        PreviewItems.ItemsSource = preview.Candidates.Select(FormatCandidate).ToArray();
        var duplicateCount = preview.Candidates.Count(candidate => candidate.DuplicateKind != CsvDuplicateKind.None);
        PreviewSummaryText.Text = preview.CanImport
            ? $"{preview.Candidates.Count} valid account(s); {duplicateCount} duplicate candidate(s). No old passwords will be imported."
            : "The preview is not ready to import. Review the diagnostics and mapping.";
    }

    private void ExcludePasswordsCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs eventArgs) =>
        RefreshPreviewButton();

    private void MappingCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) =>
        RefreshPreviewButton();

    private void SetMappingOptions(IReadOnlyList<string> headers)
    {
        var options = new[] { NoMapping }.Concat(headers).ToArray();
        ServiceNameColumnCombo.ItemsSource = options;
        AccountNameColumnCombo.ItemsSource = options;
        LoginIdentifierColumnCombo.ItemsSource = options;
        AccountUrlColumnCombo.ItemsSource = options;

        ServiceNameColumnCombo.SelectedIndex = 0;
        AccountNameColumnCombo.SelectedIndex = 0;
        LoginIdentifierColumnCombo.SelectedIndex = 0;
        AccountUrlColumnCombo.SelectedIndex = 0;
    }

    private static void SelectMapping(ComboBox comboBox, string? mapping) =>
        comboBox.SelectedItem = mapping ?? NoMapping;

    private static string? GetMapping(ComboBox comboBox) =>
        comboBox.SelectedItem as string is { } selected && selected != NoMapping ? selected : null;

    private void RefreshPreviewButton()
    {
        var hasRequiredMapping =
            (GetMapping(ServiceNameColumnCombo) is not null || GetMapping(AccountUrlColumnCombo) is not null) &&
            (GetMapping(LoginIdentifierColumnCombo) is not null || GetMapping(AccountNameColumnCombo) is not null);
        var passwordsConfirmed =
            _analysis?.ContainsPasswordColumns != true || ExcludePasswordsCheckBox.IsChecked == true;
        CreatePreviewButton.IsEnabled = _selectedFile is not null && hasRequiredMapping && passwordsConfirmed;
    }

    private void ShowDiagnostics(IReadOnlyList<CsvImportDiagnostic> diagnostics)
    {
        DiagnosticsItems.ItemsSource = diagnostics
            .Select(diagnostic => diagnostic.RowNumber is { } rowNumber
                ? $"{diagnostic.Severity}: row {rowNumber}: {diagnostic.Message}"
                : $"{diagnostic.Severity}: {diagnostic.Message}")
            .ToArray();
    }

    private void ShowReadFailure()
    {
        _selectedFile = null;
        _analysis = null;
        SelectedFileText.Text = "The selected CSV file could not be read.";
        PasswordWarningBorder.IsVisible = false;
        PreviewItems.ItemsSource = null;
        DiagnosticsItems.ItemsSource = new[] { "Error: The selected CSV file could not be read." };
        PreviewSummaryText.Text = "Select another CSV file.";
        RefreshPreviewButton();
    }

    private static string FormatCandidate(ImportAccountCandidate candidate)
    {
        var service = candidate.ServiceName ?? candidate.AccountUrl ?? "(unknown service)";
        var account = candidate.LoginIdentifier ?? candidate.AccountName ?? "(unknown account)";
        var duplicate = candidate.DuplicateKind == CsvDuplicateKind.None
            ? string.Empty
            : $" — possible duplicate ({candidate.DuplicateKind})";
        return $"Row {candidate.RowNumber}: {service} — {account}{duplicate}";
    }
}
