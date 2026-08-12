using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Unpwn.App.Presentation;

namespace Unpwn.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void ChooseDiagnosticPathButton_OnClick(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not SettingsScreenViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = viewModel.Localization.GetString("Vault.Diagnostics.FilePicker.Title"),
            SuggestedFileName = "unpwn-diagnostics.json",
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType(viewModel.Localization.GetString("Vault.Diagnostics.FilePicker.Type"))
                {
                    Patterns = ["*.json"],
                    MimeTypes = ["application/json"],
                },
            ],
        });
        if (file?.Path.IsFile == true)
        {
            viewModel.DiagnosticDestinationPath = file.Path.LocalPath;
        }
    }
}
