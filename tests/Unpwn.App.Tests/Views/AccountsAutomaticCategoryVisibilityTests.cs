using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.App.Tests.Presentation;
using Unpwn.App.Views;
using Unpwn.Application;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Views;

public sealed class AccountsAutomaticCategoryVisibilityTests
{
    [Fact]
    public async Task KnownAutomaticSuggestionKeepsResetActionVisibleForAnOverride()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(() =>
        {
            var inventory = new ShellViewModelTests.TestAccountInventoryService();
            inventory.SetInventory(CreateInventory(
                AccountRecoveryCategory.Email,
                AccountRecoveryCategory.Critical));
            var viewModel = CreateViewModel(inventory);
            var view = new AccountsView { DataContext = viewModel };
            var window = new Window { Content = view };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var reset = Assert.IsType<Button>(
                FindByAutomationId(view, "accounts-category-reset"),
                exactMatch: false);
            Assert.True(viewModel.HasCategoryOverride);
            Assert.True(viewModel.CanUseAutomaticCategory);
            Assert.True(reset.IsVisible);

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UnknownAutomaticSuggestionHidesResetActionEvenWithAnOverrideAcrossLanguages()
    {
        await AccessibilityHeadlessTests.Session.Dispatch(() =>
        {
            var inventory = new ShellViewModelTests.TestAccountInventoryService();
            inventory.SetInventory(CreateInventory(
                AccountRecoveryCategory.Unknown,
                AccountRecoveryCategory.Critical));
            var localization = new ResourceLocalizationService(CultureInfo.GetCultureInfo("en"));
            var viewModel = CreateViewModel(inventory, localization);
            var view = new AccountsView { DataContext = viewModel };
            var window = new Window { Content = view };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            var reset = Assert.IsType<Button>(
                FindByAutomationId(view, "accounts-category-reset"),
                exactMatch: false);
            Assert.True(viewModel.HasCategoryOverride);
            Assert.False(viewModel.CanUseAutomaticCategory);
            Assert.False(reset.IsVisible);

            foreach (var language in new[]
                     {
                         "de",
                         ResourceLocalizationService.PseudoLanguageCode,
                         "en",
                     })
            {
                localization.SetLanguage(language);
                Dispatcher.UIThread.RunJobs();
                Assert.False(viewModel.CanUseAutomaticCategory);
                Assert.False(reset.IsVisible);
            }

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void UnknownWithoutOverrideStillNeedsReviewAndHasNoAutomaticResetAction()
    {
        var inventory = new ShellViewModelTests.TestAccountInventoryService();
        inventory.SetInventory(CreateInventory(
            AccountRecoveryCategory.Unknown,
            confirmedCategory: null));
        var viewModel = CreateViewModel(inventory);

        var item = Assert.Single(viewModel.Accounts);
        Assert.True(item.Account.RequiresCategoryReview);
        Assert.False(viewModel.HasCategoryOverride);
        Assert.False(viewModel.CanUseAutomaticCategory);
        Assert.Null(viewModel.SelectedCategory);
    }

    private static AccountInventoryScreenViewModel CreateViewModel(
        IAccountInventoryService inventory,
        ILocalizationService? localization = null) => new(
            inventory,
            new ConfirmationDialogService(),
            localization ?? new ResourceLocalizationService(CultureInfo.GetCultureInfo("en")));

    private static AccountInventoryState CreateInventory(
        AccountRecoveryCategory suggestedCategory,
        AccountRecoveryCategory? confirmedCategory)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1);
        var account = new AccountInventoryEntry(
            Guid.NewGuid(),
            "synthetic-provider.example",
            "Synthetic account",
            "person@example.invalid",
            "https://synthetic-provider.example/account",
            suggestedCategory,
            RepositoryAccountClassificationCatalog.CurrentVersion,
            confirmedCategory,
            confirmedCategory.HasValue ? 1 : null,
            timestamp);
        var state = new AccountInventoryState(
            Guid.NewGuid(),
            Revision: 1,
            timestamp,
            [account]);
        state.Validate();
        return state;
    }

    private static StyledElement? FindByAutomationId(Control root, string automationId) =>
        root.GetLogicalDescendants()
            .OfType<StyledElement>()
            .Prepend(root)
            .FirstOrDefault(element =>
                AutomationProperties.GetAutomationId(element) == automationId);

    private sealed class ConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
