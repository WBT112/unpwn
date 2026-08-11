using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Unpwn.App.Presentation;

namespace Unpwn.App.Views;

public partial class VaultEntryView : AccessibleScreen
{
    public VaultEntryView()
    {
        InitializeComponent();
    }

    private async void ChooseCreatePathButton_OnClick(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not VaultEntryScreenViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = viewModel.Localization.GetString("Vault.FilePicker.Create.Title"),
            SuggestedFileName = "unpwn-recovery.db",
            DefaultExtension = "db",
            FileTypeChoices =
            [
                CreateVaultFileType(viewModel),
            ],
        });
        if (file?.Path.IsFile == true)
        {
            viewModel.CreatePath = file.Path.LocalPath;
        }
    }

    private async void ChooseOpenPathButton_OnClick(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not VaultEntryScreenViewModel viewModel ||
            TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = viewModel.Localization.GetString("Vault.FilePicker.Open.Title"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                CreateVaultFileType(viewModel),
            ],
        });
        var file = files.SingleOrDefault();
        if (file?.Path.IsFile == true)
        {
            viewModel.OpenPath = file.Path.LocalPath;
        }
    }

    private async void ChooseDiagnosticPathButton_OnClick(
        object? sender,
        RoutedEventArgs eventArgs)
    {
        if (DataContext is not VaultEntryScreenViewModel viewModel ||
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

    private static FilePickerFileType CreateVaultFileType(
        VaultEntryScreenViewModel viewModel) =>
        new(viewModel.Localization.GetString("Vault.FilePicker.Type"))
        {
            Patterns = ["*.db", "*.unpwn"],
            MimeTypes = ["application/x-sqlite3", "application/octet-stream"],
        };
}
