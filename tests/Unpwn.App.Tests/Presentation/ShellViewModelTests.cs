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
        var viewModel = new CompletionScreenViewModel(confirmation, shellContext);

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
    public async Task FailedCompletionCommandExposesOnlyStaticSafeMessage()
    {
        const string sourceError = "UNPWN_TEST_SECRET_dialog-failure";
        var confirmation = new TestConfirmationDialogService((_, _) =>
            Task.FromException<bool>(new InvalidOperationException(sourceError)));
        var shellContext = new TestShellContextService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var viewModel = new CompletionScreenViewModel(confirmation, shellContext);

        var outcome = await viewModel.ReviewCompletionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Failed, outcome);
        Assert.Equal(
            "The completion confirmation could not be opened.",
            viewModel.ReviewCompletionCommand.LastErrorMessage);
        Assert.DoesNotContain(sourceError, viewModel.ReviewCompletionCommand.LastErrorMessage, StringComparison.Ordinal);
        Assert.True(viewModel.ReviewCompletionCommand.HasError);
        Assert.False(viewModel.ReviewCompletionCommand.IsRunning);
    }

    [Fact]
    public async Task ConfirmationNamesExactActionAndAffectedItem()
    {
        SensitiveConfirmationRequest? observedRequest = null;
        var confirmation = new TestConfirmationDialogService((request, _) =>
        {
            observedRequest = request;
            return Task.FromResult(false);
        });
        var shellContext = new TestShellContextService();
        shellContext.Unlock("Synthetic vault", "Synthetic recovery session");
        var viewModel = new CompletionScreenViewModel(confirmation, shellContext);

        var outcome = await viewModel.ReviewCompletionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.NotNull(observedRequest);
        Assert.Equal("Complete recovery session", observedRequest.Action);
        Assert.Equal("Synthetic recovery session", observedRequest.AffectedItem);
        Assert.Equal(AppVisualState.Normal, viewModel.Status.State);
    }

    [Fact]
    public void RiskStatesHaveTextAndSymbolsThatDoNotDependOnColor()
    {
        var states = Enum.GetValues<AppVisualState>()
            .Select(state => VisualStatusViewModel.Create(state, "Synthetic title", "Synthetic message"))
            .ToArray();

        Assert.Equal(states.Length, states.Select(state => state.KindLabel).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(states.Length, states.Select(state => state.Symbol).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(states, state => state.State == AppVisualState.Blocked && state.KindLabel == "Blocked");
        Assert.Contains(states, state => state.State == AppVisualState.Error && state.KindLabel == "Failed");
        Assert.Contains(
            states,
            state => state.State == AppVisualState.UnresolvedRisk && state.KindLabel == "Unresolved risk");
    }

    private static ShellViewModel CreateShell(TestShellContextService shellContext)
    {
        var confirmation = new TestConfirmationDialogService((_, _) => Task.FromResult(false));
        return new ShellViewModel(new AppScreenFactory(confirmation, shellContext), shellContext);
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
