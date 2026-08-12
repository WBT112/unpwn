using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class SettingsScreenViewModelTests
{
    [Fact]
    public void LanguageSelectionChangesSharedLocalizationAtRuntime()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en-US"));
        using var viewModel = new SettingsScreenViewModel(
            localization,
            new TestDiagnosticExportService());
        var german = viewModel.LanguageOptions.Single(option => option.Code == "de");

        viewModel.SelectedLanguage = german;

        Assert.Equal("de", localization.CurrentLanguageCode);
        Assert.Equal("Einstellungen und Support", viewModel.Title);
        Assert.Same(german, viewModel.SelectedLanguage);
    }

    [Fact]
    public async Task DiagnosticExportRequiresPreviewApprovalAndDestination()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en-US"));
        var diagnostics = new TestDiagnosticExportService();
        using var viewModel = new SettingsScreenViewModel(localization, diagnostics);

        Assert.False(viewModel.ExportDiagnosticsCommand.CanExecute(null));
        viewModel.CreateDiagnosticPreviewCommand.Execute(null);
        Assert.True(viewModel.HasDiagnosticPreview);
        Assert.False(viewModel.ExportDiagnosticsCommand.CanExecute(null));

        viewModel.DiagnosticDestinationPath = "/tmp/unpwn-diagnostics.json";
        viewModel.DiagnosticPreviewApproved = true;
        Assert.True(viewModel.ExportDiagnosticsCommand.CanExecute(null));

        await viewModel.ExportDiagnosticsCommand.ExecuteAsync();

        Assert.Equal(1, diagnostics.ExportCount);
        Assert.False(viewModel.HasDiagnosticPreview);
        Assert.False(viewModel.DiagnosticPreviewApproved);
        Assert.Equal(string.Empty, viewModel.DiagnosticDestinationPath);
    }

    private sealed class TestDiagnosticExportService : IDiagnosticExportService
    {
        private DiagnosticReportPreview? _preview;

        public int ExportCount { get; private set; }

        public DiagnosticReportPreview CreatePreview()
        {
            _preview = new DiagnosticReportPreview(
                Guid.NewGuid(),
                "{\"safe\":true}",
                0,
                DateTimeOffset.UtcNow);
            return _preview;
        }

        public Task<DiagnosticExportResult> ExportAsync(
            DiagnosticReportPreview preview,
            string destinationPath,
            bool previewApproved,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Same(_preview, preview);
            Assert.True(previewApproved);
            Assert.False(string.IsNullOrWhiteSpace(destinationPath));
            ExportCount++;
            return Task.FromResult(DiagnosticExportResult.Success);
        }
    }
}
