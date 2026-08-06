using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class ShellViewModelTests
{
    [Fact]
    public void ShellStartsLockedWithoutRecoveryContext()
    {
        var shellContext = new TestShellContextService();
        var shell = CreateShell(shellContext);

        Assert.False(shell.IsVaultUnlocked);
        Assert.Equal("No vault unlocked", shell.VaultContextLabel);
        Assert.Equal("No recovery session", shell.SessionContextLabel);
        Assert.IsType<VaultEntryScreenViewModel>(shell.CurrentScreen);
        Assert.False(shell.LockCommand.CanExecute(null));
    }

    [Fact]
    public void NavigationExposesEveryMvpRouteAndChangesCurrentScreen()
    {
        var shell = CreateShell(new TestShellContextService());
        AppRoute[] expectedRoutes =
        [
            AppRoute.VaultEntry,
            AppRoute.Dashboard,
            AppRoute.Accounts,
            AppRoute.Workflow,
            AppRoute.CredentialsExport,
            AppRoute.Completion,
            AppRoute.CsvImport,
        ];

        Assert.Equal(expectedRoutes, shell.NavigationItems.Select(item => item.Route));

        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);

        Assert.Equal(AppRoute.Accounts, shell.CurrentScreen.Route);
        Assert.Equal("Accounts", shell.CurrentScreen.Title);
    }

    [Fact]
    public void LanguageChangeRefreshesShellNavigationAndCurrentScreen()
    {
        var localization = CreateLocalization();
        var shell = CreateShell(new TestShellContextService(), localization);
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Accounts);

        shell.SelectedLanguage = shell.LanguageOptions.Single(option => option.Code == "de");

        Assert.Equal("de", localization.CurrentLanguageCode);
        Assert.Equal("Kein Tresor entsperrt", shell.VaultContextLabel);
        Assert.Equal("Konten", shell.SelectedNavigation.Label);
        Assert.Equal("Konten", shell.CurrentScreen.Title);
    }

    [Fact]
    public async Task GlobalLockIsAvailableOnlyForUnlockedVaultAndReturnsToVaultEntry()
    {
        var shellContext = new TestShellContextService();
        var shell = CreateShell(shellContext);
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        shell.SelectedNavigation = shell.NavigationItems.Single(item => item.Route == AppRoute.Dashboard);

        Assert.True(shell.IsVaultUnlocked);
        Assert.True(shell.LockCommand.CanExecute(null));
        Assert.Equal("Synthetic vault", shell.VaultContextLabel);

        var outcome = await shell.LockCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.False(shell.IsVaultUnlocked);
        Assert.Equal(AppRoute.VaultEntry, shell.CurrentScreen.Route);
        Assert.Equal(AppVisualState.Success, shell.CurrentStatus.State);
    }

    [Fact]
    public async Task CompletionCommandCanBeCanceledAndRejectsRepeatedExecution()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmationCalls = 0;
        var confirmation = new TestConfirmationDialogService(async (_, cancellationToken) =>
        {
            confirmationCalls++;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return true;
        });
        var shellContext = new TestShellContextService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var viewModel = new CompletionScreenViewModel(
            confirmation,
            shellContext,
            CreateLocalization());

        var firstExecution = viewModel.ReviewCompletionCommand.ExecuteAsync();
        await started.Task;
        var repeatedOutcome = await viewModel.ReviewCompletionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Skipped, repeatedOutcome);
        Assert.True(viewModel.ReviewCompletionCommand.IsRunning);
        Assert.True(viewModel.ReviewCompletionCommand.CanBeCanceled);

        viewModel.ReviewCompletionCommand.Cancel();
        var firstOutcome = await firstExecution;

        Assert.Equal(AsyncCommandOutcome.Canceled, firstOutcome);
        Assert.Equal(1, confirmationCalls);
        Assert.False(viewModel.ReviewCompletionCommand.IsRunning);
    }

    [Fact]
    public async Task FailedCompletionCommandUsesCurrentLanguageAndExposesNoSourceMessage()
    {
        const string sourceError = "UNPWN_TEST_SECRET_dialog-failure";
        var confirmation = new TestConfirmationDialogService((_, _) =>
            Task.FromException<bool>(new InvalidOperationException(sourceError)));
        var shellContext = new TestShellContextService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var localization = CreateLocalization();
        var viewModel = new CompletionScreenViewModel(confirmation, shellContext, localization);
        localization.SetLanguage("de");

        var outcome = await viewModel.ReviewCompletionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Failed, outcome);
        Assert.Equal(
            "Die Abschlussbestätigung konnte nicht geöffnet werden.",
            viewModel.ReviewCompletionCommand.LastErrorMessage);
        Assert.DoesNotContain(sourceError, viewModel.ReviewCompletionCommand.LastErrorMessage, StringComparison.Ordinal);
        Assert.True(viewModel.ReviewCompletionCommand.HasError);
        Assert.False(viewModel.ReviewCompletionCommand.IsRunning);
    }

    [Fact]
    public async Task ConfirmationNamesExactLocalizedActionAndAffectedItem()
    {
        SensitiveConfirmationRequest? observedRequest = null;
        var confirmation = new TestConfirmationDialogService((request, _) =>
        {
            observedRequest = request;
            return Task.FromResult(false);
        });
        var shellContext = new TestShellContextService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var localization = CreateLocalization();
        localization.SetLanguage("de");
        var viewModel = new CompletionScreenViewModel(confirmation, shellContext, localization);

        var outcome = await viewModel.ReviewCompletionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.NotNull(observedRequest);
        Assert.Equal("Wiederherstellungssitzung abschließen", observedRequest.Action);
        Assert.Equal("Synthetic recovery session", observedRequest.AffectedItem);
        Assert.Equal("SICHERHEITSRELEVANTE AKTION", observedRequest.RiskLabel);
        Assert.Equal(AppVisualState.Normal, viewModel.Status.State);
    }

    [Fact]
    public void RiskStatesHaveLocalizedTextAndDistinctSymbols()
    {
        var localization = CreateLocalization();
        var states = Enum.GetValues<AppVisualState>()
            .Select(state => VisualStatusViewModel.Create(
                state,
                localization,
                "Screen.Vault.StatusTitle",
                "Screen.Vault.StatusMessage"))
            .ToArray();

        Assert.Equal(states.Length, states.Select(state => state.KindLabel).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(states.Length, states.Select(state => state.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(states, state => state.State == AppVisualState.Blocked && state.KindLabel == "Blocked");
        Assert.Contains(states, state => state.State == AppVisualState.Error && state.KindLabel == "Failed");
        Assert.Contains(
            states,
            state => state.State == AppVisualState.UnresolvedRisk && state.KindLabel == "Unresolved risk");
    }

    private static ResourceLocalizationService CreateLocalization() =>
        new(CultureInfo.GetCultureInfo("en"));

    private static ShellViewModel CreateShell(
        TestShellContextService shellContext,
        ResourceLocalizationService? localization = null)
    {
        localization ??= CreateLocalization();
        var confirmation = new TestConfirmationDialogService((_, _) => Task.FromResult(false));
        return new ShellViewModel(
            new AppScreenFactory(confirmation, shellContext, localization),
            shellContext,
            localization);
    }

    private sealed class TestConfirmationDialogService(
        Func<SensitiveConfirmationRequest, CancellationToken, Task<bool>> confirm)
        : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) => confirm(request, cancellationToken);
    }

    private sealed class TestShellContextService : IShellContextService
    {
        public event EventHandler? ContextChanged;

        public ShellContext Current { get; private set; } = ShellContext.Locked;

        public Task LockAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Current = ShellContext.Locked;
            ContextChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void Unlock(string vaultDisplayName, string sessionDisplayName)
        {
            Current = ShellContext.Unlocked(vaultDisplayName, sessionDisplayName);
            ContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
