using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Core;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class DashboardScreenViewModelTests
{
    [Fact]
    public void ActivationReloadsThePersistedSessionEveryTimeTheTabOpens()
    {
        var sessionService = new TestRecoverySessionService();
        var viewModel = CreateViewModel(sessionService);

        viewModel.Activate();
        viewModel.Activate();

        Assert.Equal(2, sessionService.InitializeCalls);
    }

    [Fact]
    public void SessionCreationRemainsDisabledUntilRequiredOverviewFieldsAreComplete()
    {
        var viewModel = CreateViewModel(new TestRecoverySessionService());

        Assert.False(viewModel.CreateSessionCommand.CanExecute(null));
        Assert.Equal("SyntheticUser-Recovery", viewModel.SessionName);

        viewModel.SecurityWarningAcknowledged = true;
        Assert.True(viewModel.CreateSessionCommand.CanExecute(null));

        viewModel.SessionName = string.Empty;
        Assert.False(viewModel.CreateSessionCommand.CanExecute(null));
    }

    [Fact]
    public void SessionNameSuggestionIsSanitizedAndRemainsEditable()
    {
        var viewModel = CreateViewModel(
            new TestRecoverySessionService(),
            localUserName: () => "  DOMAIN\\Tobi  ");

        Assert.Equal("DOMAIN-Tobi-Recovery", viewModel.SessionName);

        viewModel.SessionName = "Edited recovery";

        Assert.Equal("Edited recovery", viewModel.SessionName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" /\\:*? ")]
    public void MissingOrInvalidLocalUserNameUsesNeutralFallback(string? localUserName)
    {
        Assert.Equal("Recovery", RecoverySessionNameSuggestion.Create(localUserName));
    }

    [Fact]
    public void SessionNameSuggestionStaysWithinCanonicalLengthLimit()
    {
        var suggestion = RecoverySessionNameSuggestion.Create(new string('a', 200));

        Assert.Equal(120, suggestion.Length);
        Assert.EndsWith("-Recovery", suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OptionalIncidentChoicesCanBeSkippedWhenCreatingSession()
    {
        var sessionService = new TestRecoverySessionService();
        var viewModel = CreateViewModel(sessionService);
        viewModel.SessionName = "Minimal recovery";
        viewModel.SecurityWarningAcknowledged = true;

        var outcome = await viewModel.CreateSessionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.NotNull(sessionService.LastCreateRequest);
        Assert.Equal(IncidentIndicator.None, sessionService.LastCreateRequest.Indicators);
        Assert.True(viewModel.IsDashboardState);
        Assert.False(viewModel.HasValidationMessage);
        Assert.Equal(StatusPresentation.TransientResult, viewModel.Status.Presentation);
    }

    [Fact]
    public async Task RetainedIncidentChoicesReachCanonicalSessionState()
    {
        var sessionService = new TestRecoverySessionService();
        var viewModel = CreateViewModel(sessionService);
        viewModel.LostAccess = true;
        viewModel.CompromisedRecoveryChannel = true;
        viewModel.SecurityWarningAcknowledged = true;

        var outcome = await viewModel.CreateSessionCommand.ExecuteAsync();

        Assert.Equal(AsyncCommandOutcome.Completed, outcome);
        Assert.Equal(
            IncidentIndicator.LostAccess | IncidentIndicator.CompromisedRecoveryChannel,
            sessionService.CurrentSession?.Incident.Indicators);
        Assert.Equal(
            RecoveryDashboardRecommendationCode.SecureRecoveryChannel,
            sessionService.Dashboard?.Recommendation.Code);
    }

    [Fact]
    public void EmergencyRecommendationRemainsSemanticAcrossLanguageChange()
    {
        var localization = CreateLocalization();
        var session = RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Recovery channel review",
            new RecoveryIncidentIntake(IncidentIndicator.CompromisedRecoveryChannel),
            DateTimeOffset.UnixEpoch);
        var sessionService = new TestRecoverySessionService(session);
        var viewModel = CreateViewModel(sessionService, localization);

        Assert.True(viewModel.HasEmergencyAdvisory);
        Assert.Contains("primary email", viewModel.RecommendationText, StringComparison.OrdinalIgnoreCase);

        localization.SetLanguage("de");

        Assert.True(viewModel.HasEmergencyAdvisory);
        Assert.Equal(
            IncidentIndicator.CompromisedRecoveryChannel,
            sessionService.CurrentSession?.Incident.Indicators);
        Assert.Contains("primäre E-Mail-Adresse", viewModel.RecommendationText, StringComparison.Ordinal);
    }

    [Fact]
    public void RecommendationNamesReviewedProviderAndCurrentActionInSelectedLanguage()
    {
        var accountId = Guid.NewGuid();
        var session = RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                "Provider recovery",
                RecoveryIncidentIntake.Empty,
                DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [
                new RecoveryAccountDashboardEntry(
                    accountId,
                    "github.com",
                    AccountCriticality.Important,
                    AccountRecoveryStatus.Open,
                    RequiredActionsCompleted: 0,
                    RequiredActionsTotal: 1,
                    CompletedRequiredWeight: 0,
                    TotalRequiredWeight: 1,
                    BlockedRequiredActions: 0,
                    FailedRequiredActions: 0,
                    UnresolvedRisks: 0,
                    AccessLost: false,
                    CredentialsAwaitingExport: 0,
                    CredentialsAwaitingDeletion: 0,
                    RecommendedActionId: "change-password",
                    DependencyDepth: 0,
                    WaitingForAccountIds: []),
            ],
            DateTimeOffset.UnixEpoch.AddMinutes(1));
        var localization = CreateLocalization();
        var viewModel = CreateViewModel(new TestRecoverySessionService(session), localization);

        Assert.True(viewModel.HasRecommendationTarget);
        Assert.True(viewModel.HasRecommendationAction);
        Assert.Equal("Recommended account or service: GitHub", viewModel.RecommendationTargetText);
        Assert.Equal("Current action: Change the password", viewModel.RecommendationActionText);
        Assert.Equal("0 of 0 critical accounts handled.", viewModel.CriticalReadinessText);
        Assert.Equal("Progress: 0%", viewModel.WeightedProgressText);

        localization.SetLanguage("de");

        Assert.Equal("Empfohlenes Konto oder Dienst: GitHub", viewModel.RecommendationTargetText);
        Assert.Equal("Aktuelle Aktion: Passwort ändern", viewModel.RecommendationActionText);
        Assert.Equal("0 von 0 kritischen Konten bearbeitet.", viewModel.CriticalReadinessText);
        Assert.Equal("Fortschritt: 0 %", viewModel.WeightedProgressText);
    }

    [Fact]
    public void BlockedSummaryNavigatesToAffectedAccountAndAction()
    {
        var accountId = Guid.NewGuid();
        var session = RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Blocked work",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch)
            .ReplaceAccounts(
            [
                new RecoveryAccountDashboardEntry(
                    accountId,
                    "primary-email",
                    AccountCriticality.Critical,
                    AccountRecoveryStatus.NotFullySecured,
                    RequiredActionsCompleted: 0,
                    RequiredActionsTotal: 1,
                    CompletedRequiredWeight: 0,
                    TotalRequiredWeight: 5,
                    BlockedRequiredActions: 1,
                    FailedRequiredActions: 0,
                    UnresolvedRisks: 0,
                    AccessLost: false,
                    CredentialsAwaitingExport: 0,
                    CredentialsAwaitingDeletion: 0,
                    RecommendedActionId: "reset-password",
                    DependencyDepth: 0,
                    WaitingForAccountIds: []),
            ],
            DateTimeOffset.UnixEpoch.AddMinutes(1));
        var viewModel = CreateViewModel(new TestRecoverySessionService(session));
        DashboardNavigationRequest? request = null;
        viewModel.NavigationRequested += (_, eventArgs) => request = eventArgs;

        viewModel.OpenBlockedCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(AppRoute.Workflow, request.Route);
        Assert.Equal(accountId, request.AccountId);
        Assert.Equal("reset-password", request.ActionId);
    }

    [Fact]
    public void CompletionEntryIsExplicitNavigationAndDoesNotMutateSession()
    {
        var session = RecoverySessionWorkspace.Create(
            Guid.NewGuid(),
            "Completion review",
            RecoveryIncidentIntake.Empty,
            DateTimeOffset.UnixEpoch);
        var sessionService = new TestRecoverySessionService(session);
        var viewModel = CreateViewModel(sessionService);
        DashboardNavigationRequest? request = null;
        viewModel.NavigationRequested += (_, eventArgs) => request = eventArgs;

        viewModel.OpenCompletionCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(AppRoute.Completion, request.Route);
        Assert.Equal(RecoveryWorkspaceLifecycleStatus.Active, sessionService.CurrentSession?.Status);
        Assert.Equal(0, sessionService.ArchiveCalls);
    }

    private static ResourceLocalizationService CreateLocalization() =>
        new(CultureInfo.GetCultureInfo("en"));

    private static DashboardScreenViewModel CreateViewModel(
        TestRecoverySessionService sessionService,
        ResourceLocalizationService? localization = null,
        Func<string?>? localUserName = null)
    {
        localization ??= CreateLocalization();
        return new DashboardScreenViewModel(
            sessionService,
            new TestVaultLifecycleService(),
            new RecoveryWizardSessionService(DateTimeOffset.UnixEpoch),
            new TestConfirmationDialogService(),
            localization,
            localUserName ?? (() => "SyntheticUser"));
    }

    private sealed class TestConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }
    }

    private sealed class TestRecoverySessionService(RecoverySessionWorkspace? session = null)
        : IRecoverySessionService
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState { get; private set; } = session is null
            ? RecoverySessionLoadState.Empty
            : RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; private set; } = session;

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public RecoverySessionCreateRequest? LastCreateRequest { get; private set; }

        public int ArchiveCalls { get; private set; }

        public int InitializeCalls { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InitializeCalls++;
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task<RecoverySessionOperationResult> CreateAsync(
            RecoverySessionCreateRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCreateRequest = request;
            CurrentSession = RecoverySessionWorkspace.Create(
                Guid.NewGuid(),
                request.Name,
                new RecoveryIncidentIntake(request.Indicators),
                DateTimeOffset.UnixEpoch);
            LoadState = RecoverySessionLoadState.Loaded;
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentSession = CurrentSession?.Pause(CurrentSession.UpdatedAt.AddMinutes(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentSession = CurrentSession?.Resume(CurrentSession.UpdatedAt.AddMinutes(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveCalls++;
            CurrentSession = CurrentSession?.Archive(CurrentSession.UpdatedAt.AddMinutes(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(
            IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CurrentSession = CurrentSession?.ReplaceAccounts(
                accounts,
                CurrentSession.UpdatedAt.AddMinutes(1));
            SessionChanged?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(RecoverySessionOperationResult.Success);
        }

        public void ClearForLock()
        {
            LoadState = RecoverySessionLoadState.Locked;
            CurrentSession = null;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestVaultLifecycleService : IVaultLifecycleService
    {
        public event EventHandler? ContextChanged;

        public event EventHandler? VaultStateChanged;

        public ShellContext Current { get; private set; } =
            ShellContext.Unlocked("Synthetic vault", "Synthetic session");

        public VaultLifecycleSnapshot Snapshot { get; private set; } = new(
            VaultLifecycleStatus.Unlocked,
            "synthetic.db",
            "Synthetic vault",
            VaultLockReason.None,
            IsInactivityWarningVisible: false,
            InactivityLocksAt: null);

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
            Snapshot = Snapshot with { Status = VaultLifecycleStatus.Locked };
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
