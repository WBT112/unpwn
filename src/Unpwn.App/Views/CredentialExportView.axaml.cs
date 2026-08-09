using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Unpwn.App.Presentation;

namespace Unpwn.App.Views;

public partial class CredentialExportView : UserControl
{
    public CredentialExportView()
    {
        InitializeComponent();
    }

    private async void ChooseDestinationButton_OnClick(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not CredentialExportScreenViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = viewModel.Localization.GetString("Credentials.Export.FilePicker.Title"),
            SuggestedFileName = "unpwn-generated-credentials.csv",
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType(viewModel.Localization.GetString("Credentials.Export.FilePicker.Type"))
                {
                    Patterns = ["*.csv"],
                    MimeTypes = ["text/csv", "text/plain"],
                },
            ],
        });
        if (file?.Path.IsFile == true)
        {
            viewModel.DestinationPath = file.Path.LocalPath;
        }
    }
}
