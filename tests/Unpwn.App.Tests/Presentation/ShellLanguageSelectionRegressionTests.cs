using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class ShellLanguageSelectionRegressionTests
{
    [Fact]
    public void LanguageSelectionCanReturnFromPseudoToEnglishWithoutReplacingOptions()
    {
        var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("de-DE"));
        using var vaultLifecycle = new TestVaultLifecycleService();
        var shell = new ShellViewModel(
            new TestScreenFactory(localization),
            vaultLifecycle,
            localization);
        var options = shell.LanguageOptions;
        var pseudo = options.Single(option =>
            option.Code == ResourceLocalizationService.PseudoLanguageCode);
        var english = options.Single(option =>
            option.Code == ResourceLocalizationService.DefaultLanguageCode);

        shell.SelectedLanguage = pseudo;
        shell.SelectedLanguage = english;

        Assert.Same(options, shell.LanguageOptions);
        Assert.Same(english, shell.SelectedLanguage);
        Assert.Equal(ResourceLocalizationService.DefaultLanguageCode, localization.CurrentLanguageCode);
        Assert.Equal("English", english.DisplayName);
        Assert.Equal(
            "Vault",
            shell.NavigationItems.Single(item => item.Route == AppRoute.VaultEntry).Label);
    }

    private sealed class TestScreenFactory(ILocalizationService localization) : IScreenFactory
    {
        public ScreenViewModel Create(AppRoute route) => new PlaceholderScreenViewModel(
            route,
            localization,
            "Screen.Vault.Title",
            "Screen.Vault.Description",
            AppVisualState.Normal,
            "Screen.Vault.StatusTitle",
            "Screen.Vault.StatusMessage");
    }

    private sealed class TestVaultLifecycleService : IVaultLifecycleService
    {
        public event EventHandler? ContextChanged;

        public event EventHandler? VaultStateChanged;

        public ShellContext Current { get; private set; } = ShellContext.Locked;

        public VaultLifecycleSnapshot Snapshot { get; private set; } = VaultLifecycleSnapshot.Empty;

        public IReadOnlyList<RecentVaultReference> RecentVaults { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<VaultOperationResult> CreateAsync(
            string path,
            string vaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task<VaultOperationResult> OpenAsync(
            string path,
            string vaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task<VaultOperationResult> UnlockCurrentAsync(
            string vaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task<VaultOperationResult> ChangePasswordAsync(
            string currentVaultPassword,
            string newVaultPassword,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public Task LockAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Current = ShellContext.Locked;
            Snapshot = VaultLifecycleSnapshot.Empty;
            ContextChanged?.Invoke(this, EventArgs.Empty);
            VaultStateChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task RemoveRecentReferenceAsync(
            string path,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<VaultOperationResult> DeleteVaultFileAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(VaultOperationResult.Success);

        public void RecordUserActivity(DateTimeOffset occurredAt)
        {
        }

        public Task CheckInactivityAsync(
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
