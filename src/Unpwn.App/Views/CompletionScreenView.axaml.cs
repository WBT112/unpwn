using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Unpwn.App.Presentation;

namespace Unpwn.App.Views;

public partial class CompletionScreenView : UserControl
{
    public CompletionScreenView()
    {
        InitializeComponent();
    }

    private async void ChooseReportDestinationButton_OnClick(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not CompletionScreenViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = viewModel.Localization.GetString("Completion.Export.FilePicker.Title"),
            SuggestedFileName = "unpwn-recovery-report.json",
            DefaultExtension = "json",
            ShowOverwritePrompt = false,
            FileTypeChoices =
            [
                new FilePickerFileType(viewModel.Localization.GetString("Completion.Export.FilePicker.Type"))
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json", "text/plain"],
                },
            ],
        });
        if (file?.Path.IsFile == true)
        {
            viewModel.DestinationPath = file.Path.LocalPath;
        }
    }
}
