using System.Globalization;
using Unpwn.App.Localization;
using Unpwn.App.Presentation;
using Unpwn.App.Services;
using Unpwn.Application.Credentials;
using Unpwn.Application.Recovery;
using Unpwn.Core;
using Unpwn.Providers.Workflows;
using Xunit;

namespace Unpwn.App.Tests.Presentation;

public sealed class WorkflowExecutionScreenViewModelTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LoadsRecommendedAccountContextAndPreservesCanonicalStateAcrossLanguageChanges()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.True(viewModel.HasAccount);
        Assert.True(viewModel.HasWorkflow);
        Assert.Contains("GitHub", viewModel.ProviderName, StringComparison.Ordinal);
        Assert.Contains("Identity", viewModel.RolesText, StringComparison.Ordinal);
        Assert.Equal(RecoveryPath.AuthenticatedChange, viewModel.SelectedPath?.Path);

        await viewModel.BeginCommand.ExecuteAsync();
        var actionId = viewModel.SelectedAction!.DefinitionId;
        var path = viewModel.SelectedPath!.Path;

        fixture.Localization.SetLanguage("de");

        Assert.Equal(actionId, viewModel.SelectedAction.DefinitionId);
        Assert.Equal(path, viewModel.SelectedPath.Path);
        Assert.Contains("Identitäts", viewModel.RolesText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvesGoogleAccountToReviewedSecurityWorkflow()
    {
        var fixture = new Fixture(
            providerId: "Google",
            accountUrl: "https://myaccount.google.com/security");
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "change-password");
        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.True(viewModel.HasWorkflow);
        Assert.Equal("Google", viewModel.ProviderName);
        Assert.Equal(
            "https://myaccount.google.com/security",
            fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
    }

    [Fact]
    public async Task ResolvesMicrosoftAccountToReviewedPersonalAccountWorkflow()
    {
        var fixture = new Fixture(
            providerId: "Microsoft",
            accountUrl: "https://account.microsoft.com/security");
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "change-password");
        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.True(viewModel.HasWorkflow);
        Assert.Equal("Microsoft", viewModel.ProviderName);
        Assert.Equal(
            "https://account.microsoft.com/security",
            fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
    }

    [Fact]
    public async Task BrowserNavigationLeavesActionOpenEvenWhenTheProviderPageOpens()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "change-password");
        var revision = fixture.Execution.State!.Revision;
        var applyCalls = fixture.Execution.ApplyCalls;

        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.Equal(1, fixture.ExternalNavigation.OpenCalls);
        Assert.Equal("https://github.com/settings/security", fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
        Assert.Equal(applyCalls, fixture.Execution.ApplyCalls);
        Assert.Equal(revision, fixture.Execution.State.Revision);
        Assert.Equal(RecoveryActionStatus.Open, fixture.Execution.State.GetAction("change-password").Status);
        Assert.Contains("remains unchanged", viewModel.NavigationStatus, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("review-api-tokens-auth", "https://github.com/settings/tokens")]
    [InlineData("review-ssh-signing-keys-auth", "https://github.com/settings/keys")]
    public async Task OpensTheReviewedLocationForCriticalDeveloperCredentials(
        string actionId,
        string expectedDestination)
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == actionId);

        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.Equal(expectedDestination, fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
        Assert.Equal(RecoveryActionStatus.Open, fixture.Execution.State!.GetAction(actionId).Status);
    }

    [Fact]
    public async Task CompletionRequiresCriteriaAndConfirmationThenReturnsToRecalculatedPlan()
    {
        var fixture = new Fixture { Confirm = true };
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.StartActionCommand.ExecuteAsync();
        var returned = new List<WorkflowPlanReturnRequest>();
        viewModel.PlanReturnRequested += (_, request) => returned.Add(request);

        Assert.False(viewModel.CompleteActionCommand.CanExecute(null));
        Assert.Equal(RecoveryActionStatus.InProgress, fixture.Execution.State!.GetAction("identify-account-auth").Status);

        viewModel.CompletionCriteriaAcknowledged = true;
        Assert.True(viewModel.CompleteActionCommand.CanExecute(null));
        await viewModel.CompleteActionCommand.ExecuteAsync();

        Assert.Equal(RecoveryActionStatus.Completed, fixture.Execution.State.GetAction("identify-account-auth").Status);
        Assert.Single(returned);
        Assert.Equal(1, fixture.ConfirmationCalls);
    }

    [Fact]
    public async Task ReasonBoundTransitionsRejectEmptyTextAndPersistProviderReviewAcrossReload()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        var calls = fixture.Execution.ApplyCalls;

        await viewModel.SetWaitingCommand.ExecuteAsync();

        Assert.Equal(calls, fixture.Execution.ApplyCalls);
        Assert.True(viewModel.HasValidationMessage);

        viewModel.Reason = "Provider case is awaiting manual review.";
        await viewModel.SetWaitingCommand.ExecuteAsync();
        Assert.Equal(RecoveryAccessState.WaitingForProviderReview, fixture.Execution.State!.AccessState);

        var reloaded = fixture.CreateViewModel();
        await reloaded.RefreshCommand.ExecuteAsync();
        Assert.Equal(RecoveryAccessState.WaitingForProviderReview, fixture.Execution.State.AccessState);
        Assert.Contains("provider review", reloaded.AccessStateText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecoveryPathCanChangeBeforeWorkButNotAfterAnActionStarts()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedPath = viewModel.PathOptions.Single(option => option.Path == RecoveryPath.ManualRecovery);

        Assert.True(viewModel.ChangePathCommand.CanExecute(null));
        await viewModel.ChangePathCommand.ExecuteAsync();
        Assert.Equal(RecoveryPath.ManualRecovery, fixture.Execution.State!.SelectedPath);

        await viewModel.StartActionCommand.ExecuteAsync();
        viewModel.SelectedPath = viewModel.PathOptions.Single(option => option.Path == RecoveryPath.PasswordReset);
        Assert.False(viewModel.ChangePathCommand.CanExecute(null));
    }

    [Fact]
    public async Task NavigationFailureUsesSafePresentationCodeWithoutChangingExecution()
    {
        var fixture = new Fixture
        {
            ExternalNavigationResult = ExternalNavigationResult.Failure(
                ExternalNavigationFailureCode.Rejected),
        };
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "change-password");
        var revision = fixture.Execution.State!.Revision;

        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.Equal(revision, fixture.Execution.State.Revision);
        Assert.Contains("did not open", viewModel.NavigationStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("github.com/settings", viewModel.NavigationStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlockedPrerequisiteDisplaysStructuredReasonAndReturnsToThePlan()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "change-password");
        var returned = false;
        viewModel.PlanReturnRequested += (_, _) => returned = true;

        await viewModel.StartActionCommand.ExecuteAsync();

        Assert.True(returned);
        Assert.True(viewModel.HasRecordedReason);
        Assert.Contains("Identify the affected account", viewModel.RecordedReasonText, StringComparison.Ordinal);
        Assert.Equal(RecoveryActionStatus.Blocked, fixture.Execution.State!.GetAction("change-password").Status);
    }

    [Fact]
    public async Task GuidedActionIsDefaultAndAdvancedStatusUsesTheSameExecution()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        var focusBeforeBegin = viewModel.CurrentActionFocusRequest;
        await viewModel.BeginCommand.ExecuteAsync();
        var revision = fixture.Execution.State!.Revision;

        Assert.True(viewModel.IsGuidedActionVisible);
        Assert.False(viewModel.IsAdvancedStatusVisible);
        Assert.Contains("Why this is next", viewModel.CurrentActionWhyText, StringComparison.Ordinal);
        Assert.True(viewModel.CurrentActionFocusRequest > focusBeforeBegin);

        viewModel.ShowAdvancedStatusCommand.Execute(null);
        Assert.True(viewModel.IsAdvancedStatusVisible);
        Assert.Equal(revision, fixture.Execution.State.Revision);

        viewModel.ShowGuidedActionCommand.Execute(null);
        Assert.True(viewModel.IsGuidedActionVisible);
        Assert.Equal(revision, fixture.Execution.State.Revision);
    }

    [Fact]
    public async Task GuidedOfficialPageStartsButNeverCompletesTheAction()
    {
        var fixture = new Fixture { Confirm = true };
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();
        viewModel.CompletionCriteriaAcknowledged = true;
        await viewModel.CompleteActionCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "change-password");

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, fixture.ExternalNavigation.OpenCalls);
        Assert.Equal(
            "https://github.com/settings/security",
            fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State!.GetAction("change-password").Status);
        Assert.False(viewModel.CompleteActionCommand.CanExecute(null));
        Assert.Contains("remains unchanged", viewModel.NavigationStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GuidedLostAccessAndWaitingAnswersMapToCanonicalAccessStates()
    {
        var lostFixture = new Fixture();
        var lost = lostFixture.CreateViewModel();
        await lost.RefreshCommand.ExecuteAsync();
        await lost.BeginCommand.ExecuteAsync();
        var lostReturns = 0;
        lost.PlanReturnRequested += (_, _) => lostReturns++;
        lost.ShowProblemReviewCommand.Execute(null);
        lost.SelectedProblem = lost.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.LostAccess);
        lost.Reason = "Synthetic access was lost.";

        await lost.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.Equal(RecoveryAccessState.Lost, lostFixture.Execution.State!.AccessState);
        Assert.Equal(1, lostReturns);

        var waitingFixture = new Fixture();
        var waiting = waitingFixture.CreateViewModel();
        await waiting.RefreshCommand.ExecuteAsync();
        await waiting.BeginCommand.ExecuteAsync();
        waiting.ShowProblemReviewCommand.Execute(null);
        waiting.SelectedProblem = waiting.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.WaitingForProvider);
        waiting.Reason = "Synthetic provider review is pending.";

        await waiting.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.Equal(
            RecoveryAccessState.WaitingForProviderReview,
            waitingFixture.Execution.State!.AccessState);
    }

    [Fact]
    public async Task GuidedPrerequisiteAndFailureAnswersRemainRetryable()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.ShowProblemReviewCommand.Execute(null);
        viewModel.SelectedProblem = viewModel.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.MissingPrerequisite);
        viewModel.Reason = "Synthetic prerequisite is unavailable.";

        await viewModel.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.Equal(
            RecoveryActionStatus.Blocked,
            fixture.Execution.State!.GetAction("identify-account-auth").Status);
        Assert.True(viewModel.GuidedPrimaryActionCommand.CanExecute(null));

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State.GetAction("identify-account-auth").Status);

        viewModel.ShowProblemReviewCommand.Execute(null);
        viewModel.SelectedProblem = viewModel.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.ProviderStepFailed);
        viewModel.Reason = "Synthetic provider rejected the step.";
        await viewModel.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.Equal(
            RecoveryActionStatus.Failed,
            fixture.Execution.State.GetAction("identify-account-auth").Status);
        Assert.True(viewModel.GuidedPrimaryActionCommand.CanExecute(null));

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();

        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State.GetAction("identify-account-auth").Status);
    }

    [Fact]
    public async Task GuidedNotApplicableAndRiskAnswersRequireConfirmationAndReasons()
    {
        var notApplicableFixture = new Fixture { Confirm = true };
        var notApplicable = notApplicableFixture.CreateViewModel();
        await notApplicable.RefreshCommand.ExecuteAsync();
        await notApplicable.BeginCommand.ExecuteAsync();
        notApplicable.ShowProblemReviewCommand.Execute(null);
        notApplicable.SelectedProblem = notApplicable.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.TrulyNotApplicable);
        notApplicable.Reason = "Synthetic capability does not exist.";

        await notApplicable.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.Equal(
            RecoveryActionStatus.NotApplicable,
            notApplicableFixture.Execution.State!.GetAction("identify-account-auth").Status);
        Assert.Equal(1, notApplicableFixture.ConfirmationCalls);

        var riskFixture = new Fixture { Confirm = true };
        var risk = riskFixture.CreateViewModel();
        await risk.RefreshCommand.ExecuteAsync();
        await risk.BeginCommand.ExecuteAsync();
        await risk.GuidedPrimaryActionCommand.ExecuteAsync();
        risk.ShowProblemReviewCommand.Execute(null);
        risk.SelectedProblem = risk.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.AcceptUnresolvedRisk);
        risk.Reason = "Synthetic unresolved risk remains.";

        await risk.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.True(riskFixture.Execution.State!.GetAction("identify-account-auth").HasUnresolvedRisk);
        Assert.Equal(AccountRecoveryStatus.NotFullySecured, riskFixture.Execution.State.RecoveryStatus);
        Assert.Equal(1, riskFixture.ConfirmationCalls);
    }

    [Fact]
    public async Task GuidedAdvancedChoiceDoesNotMutateCanonicalExecution()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        var revision = fixture.Execution.State!.Revision;
        var calls = fixture.Execution.ApplyCalls;
        viewModel.ShowProblemReviewCommand.Execute(null);
        viewModel.SelectedProblem = viewModel.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.ReviewAdvancedDetails);

        await viewModel.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.True(viewModel.IsAdvancedStatusVisible);
        Assert.Equal(calls, fixture.Execution.ApplyCalls);
        Assert.Equal(revision, fixture.Execution.State.Revision);
    }

    [Fact]
    public async Task ChangingCurrentActionRequestsAccessibleFocus()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        var previousRequest = viewModel.CurrentActionFocusRequest;

        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "change-password");

        Assert.True(viewModel.CurrentActionFocusRequest > previousRequest);
    }

    [Fact]
    public async Task PasswordStepGeneratesAndAttachesOnlyAnOpaqueCredentialReference()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "change-password");

        Assert.True(viewModel.CanGenerateCredentialForCurrentAction);
        await viewModel.GenerateCredentialCommand.ExecuteAsync();

        var reference = fixture.Execution.State!.GetAction("change-password").CredentialReference;
        Assert.NotNull(reference);
        Assert.Equal(fixture.Credentials.LastMetadata?.Reference, reference);
        Assert.Equal(1, fixture.Credentials.GenerateCalls);
        Assert.True(viewModel.HasCredentialReference);
    }

    [Fact]
    public async Task VaultLockClearsMaterializedAccountAndExecutionState()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        Assert.True(viewModel.HasExecution);

        fixture.Inventory.ClearForLock();

        Assert.False(viewModel.HasAccount);
        Assert.False(viewModel.HasExecution);
        Assert.Null(fixture.Execution.State);
    }

    private sealed class Fixture
    {
        private readonly Guid _accountId = Guid.NewGuid();

        public Fixture(
            string providerId = "github.com",
            string accountUrl = "https://github.com/settings/security")
        {
            var account = new AccountInventoryEntry(
                _accountId,
                providerId,
                $"{providerId} recovery account",
                "synthetic-user",
                accountUrl,
                AccountInventoryPriority.Critical,
                [new AccountRoleState(AccountInventoryRole.IdentityProvider, AccountRoleDecision.Confirmed)],
                [],
                StartedAt);
            var inventory = new AccountInventoryState(
                Guid.NewGuid(),
                Revision: 1,
                StartedAt,
                [account]);
            Inventory = new TestInventoryService(inventory);
            Session = new TestSessionService(RecoverySessionWorkspace.Create(
                inventory.SessionId,
                "Synthetic incident",
                RecoveryIncidentIntake.Empty,
                StartedAt).ReplaceAccounts(
                [DashboardEntry(_accountId, providerId)],
                StartedAt.AddMinutes(1)));
        }

        public ResourceLocalizationService Localization { get; } = new(CultureInfo.GetCultureInfo("en"));

        public TestInventoryService Inventory { get; }

        public TestSessionService Session { get; }

        public TestExecutionService Execution { get; } = new();

        public TestExternalNavigationService ExternalNavigation { get; } = new();

        public TestGeneratedCredentialRepository Credentials { get; } = new();

        public bool Confirm { get; init; }

        public int ConfirmationCalls { get; private set; }

        public ExternalNavigationResult ExternalNavigationResult
        {
            init => ExternalNavigation.Result = value;
        }

        public WorkflowExecutionScreenViewModel CreateViewModel() =>
            new(
                Inventory,
                Session,
                Execution,
                new TestLocationDiscoveryService(),
                ExternalNavigation,
                new TestConfirmationDialogService((_, _) =>
                {
                    ConfirmationCalls++;
                    return Task.FromResult(Confirm);
                }),
                Localization,
                Credentials);

        private static RecoveryAccountDashboardEntry DashboardEntry(
            Guid accountId,
            string providerId) =>
            new(
                accountId,
                providerId,
                AccountCriticality.Critical,
                AccountRecoveryStatus.Open,
                RequiredActionsCompleted: 0,
                RequiredActionsTotal: 0,
                CompletedRequiredWeight: 0,
                TotalRequiredWeight: 0,
                BlockedRequiredActions: 0,
                FailedRequiredActions: 0,
                UnresolvedRisks: 0,
                AccessLost: false,
                CredentialsAwaitingExport: 0,
                CredentialsAwaitingDeletion: 0,
                RecommendedActionId: null,
                DependencyDepth: 0,
                WaitingForAccountIds: []);
    }

    private sealed class TestExecutionService : IAccountRecoveryExecutionService
    {
        private DateTimeOffset _clock = StartedAt.AddMinutes(2);

        public AccountRecoveryExecutionState? State { get; private set; }

        public int ApplyCalls { get; private set; }

        public Task<AccountRecoveryExecutionResult> LoadAsync(
            Guid accountId,
            RecoveryWorkflowDefinition workflow,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(State is null
                ? AccountRecoveryExecutionResult.Failure(AccountRecoveryExecutionFailureCode.NotFound)
                : AccountRecoveryExecutionResult.Success(State));
        }

        public Task<AccountRecoveryExecutionResult> CreateAsync(
            AccountRecoveryExecutionCreateRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = AccountRecoveryExecutionState.Create(
                request.AccountId,
                request.Workflow,
                request.SelectedPath,
                NextTime());
            return Task.FromResult(AccountRecoveryExecutionResult.Success(State));
        }

        public Task<AccountRecoveryExecutionResult> ApplyAsync(
            AccountRecoveryExecutionTransitionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            if (State is null || State.Revision != request.ExpectedRevision)
            {
                return Task.FromResult(AccountRecoveryExecutionResult.Failure(
                    AccountRecoveryExecutionFailureCode.Conflict));
            }

            try
            {
                var time = NextTime();
                State = request.Transition switch
                {
                    AccountRecoveryExecutionTransitionKind.ChangeRecoveryPath =>
                        State.ChangePath(request.Workflow, request.SelectedPath!.Value, time),
                    AccountRecoveryExecutionTransitionKind.SetAccessAvailable =>
                        State.SetAccessState(RecoveryAccessState.Available, null, time),
                    AccountRecoveryExecutionTransitionKind.SetAccessLost =>
                        State.SetAccessState(RecoveryAccessState.Lost, request.UserReason, time),
                    AccountRecoveryExecutionTransitionKind.SetWaitingForProviderReview =>
                        State.SetAccessState(RecoveryAccessState.WaitingForProviderReview, request.UserReason, time),
                    AccountRecoveryExecutionTransitionKind.StartAction =>
                        State.StartAction(request.Workflow, request.ActionDefinitionId!, time),
                    AccountRecoveryExecutionTransitionKind.CompleteAction =>
                        State.CompleteAction(
                            request.Workflow,
                            request.ActionDefinitionId!,
                            request.CompletionCriteriaAcknowledged,
                            time),
                    AccountRecoveryExecutionTransitionKind.RequireUserAction =>
                        State.RequireUserAction(request.Workflow, request.ActionDefinitionId!, request.UserReason!, time),
                    AccountRecoveryExecutionTransitionKind.BlockAction =>
                        State.BlockAction(request.Workflow, request.ActionDefinitionId!, request.UserReason!, time),
                    AccountRecoveryExecutionTransitionKind.FailAction =>
                        State.FailAction(request.Workflow, request.ActionDefinitionId!, request.UserReason!, time),
                    AccountRecoveryExecutionTransitionKind.MarkTrulyNotApplicable =>
                        State.MarkNotApplicable(
                            request.Workflow,
                            request.ActionDefinitionId!,
                            request.UserReason!,
                            NotApplicableDisposition.TrulyNotApplicable,
                            time),
                    AccountRecoveryExecutionTransitionKind.AcceptUnresolvedRisk =>
                        State.AcceptUnresolvedRisk(
                            request.Workflow,
                            request.ActionDefinitionId!,
                            request.UserReason!,
                            time),
                    AccountRecoveryExecutionTransitionKind.SetUserNotes =>
                        State.SetUserNotes(request.ActionDefinitionId!, request.UserNotes, time),
                    AccountRecoveryExecutionTransitionKind.AttachCredentialReference =>
                        State.AttachCredentialReference(request.ActionDefinitionId!, request.CredentialReference!, time),
                    _ => throw new InvalidOperationException(),
                };
                return Task.FromResult(AccountRecoveryExecutionResult.Success(State));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Task.FromResult(AccountRecoveryExecutionResult.Failure(
                    AccountRecoveryExecutionFailureCode.Conflict));
            }
        }

        public void ClearForLock() => State = null;

        private DateTimeOffset NextTime()
        {
            _clock = _clock.AddMinutes(1);
            return _clock;
        }
    }

    private sealed class TestLocationDiscoveryService : IRecoveryLocationDiscoveryService
    {
        public Task<RecoveryLocationDiscoveryResult> DiscoverAsync(
            RecoveryLocationDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = request.Workflow.RecoveryLocations.Single(candidate =>
                candidate.Id == request.ProviderLocationId);
            return Task.FromResult(RecoveryLocationDiscoveryResult.Success(
                new RecoveryNavigationHandoff(
                    location.Url,
                    location.ExpectedOrigins[0],
                    location.ExpectedOrigins,
                    RecoveryLocationResolutionSource.ProviderDefined,
                    RequiresVisibleConfirmation: true)));
        }
    }

    private sealed class TestGeneratedCredentialRepository : IGeneratedCredentialRepository
    {
        public bool IsUnlocked => true;

        public int GenerateCalls { get; private set; }

        public GeneratedCredentialMetadata? LastMetadata { get; private set; }

        public Task<GeneratedCredentialCreationResult> GenerateAsync(
            Guid accountId,
            CredentialGenerationPolicy policy,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GenerateCalls++;
            LastMetadata = GeneratedCredentialMetadata.Create(
                Guid.NewGuid(),
                accountId,
                operationId,
                StartedAt.AddHours(1));
            return Task.FromResult(GeneratedCredentialCreationResult.Success(
                LastMetadata,
                new CredentialSecretLease([65, 66, 67])));
        }

        public Task<IReadOnlyList<GeneratedCredentialMetadata>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GeneratedCredentialMetadata>>(
                LastMetadata is null ? [] : [LastMetadata]);

        public Task<GeneratedCredentialMetadata?> GetMetadataAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken) => Task.FromResult(LastMetadata);

        public Task<CredentialSecretLease?> ReadSecretAsync(
            GeneratedCredentialReference reference,
            CancellationToken cancellationToken) => Task.FromResult<CredentialSecretLease?>(null);

        public Task<GeneratedCredentialOperationResult> MarkUsedAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> ConfirmAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialBatchResult> MarkExportedAsync(
            IReadOnlyCollection<GeneratedCredentialReference> references,
            Guid operationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(GeneratedCredentialBatchResult.Failure(
                GeneratedCredentialFailureCode.InvalidInput));

        public Task<GeneratedCredentialOperationResult> ConfirmPasswordManagerImportAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> RevokePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> PostponePasswordManagerImportConfirmationAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> ConfirmPlaintextExportCleanupAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        public Task<GeneratedCredentialOperationResult> DeleteAsync(
            GeneratedCredentialReference reference,
            Guid operationId,
            CancellationToken cancellationToken) => Unsupported();

        private static Task<GeneratedCredentialOperationResult> Unsupported() =>
            Task.FromResult(GeneratedCredentialOperationResult.Failure(
                GeneratedCredentialFailureCode.InvalidInput));
    }

    private sealed class TestExternalNavigationService : IExternalNavigationService
    {
        public ExternalNavigationResult Result { get; set; } = ExternalNavigationResult.Success;

        public int OpenCalls { get; private set; }

        public Uri? LastDestination { get; private set; }

        public Task<ExternalNavigationResult> OpenAsync(
            Uri destination,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenCalls++;
            LastDestination = destination;
            return Task.FromResult(Result);
        }
    }

    private sealed class TestConfirmationDialogService(
        Func<SensitiveConfirmationRequest, CancellationToken, Task<bool>> confirm)
        : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(
            SensitiveConfirmationRequest request,
            CancellationToken cancellationToken) =>
            confirm(request, cancellationToken);
    }

    private sealed class TestInventoryService(AccountInventoryState inventory) : IAccountInventoryService
    {
        public event EventHandler? InventoryChanged;

        public AccountInventoryLoadState LoadState => AccountInventoryLoadState.Loaded;

        public AccountInventoryState? CurrentInventory { get; private set; } = inventory;

        public AccountInventoryPlan? CurrentPlan => CurrentInventory?.CreatePlan(IncidentIndicator.None);

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(AccountInventoryUpsertRequest request, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> DecideRoleAsync(Guid accountId, AccountInventoryRole role, AccountRoleDecision decision, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> AddDependencyAsync(AccountDependencyRequest request, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveDependencyAsync(Guid accountId, Guid dependsOnAccountId, AccountDependencyKind kind, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(Guid accountId, bool dependencyImpactAcknowledged, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> ImportAsync(IReadOnlyCollection<Unpwn.Import.Csv.ImportAccountCandidate> candidates, ImportDuplicateResolution? duplicateResolution, CancellationToken cancellationToken) => Unsupported();

        public IReadOnlyList<Unpwn.Import.Csv.ExistingAccountReference> GetExistingAccountReferences() => [];

        public void ClearForLock()
        {
            CurrentInventory = null;
            InventoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private static Task<AccountInventoryOperationResult> Unsupported() =>
            Task.FromResult(AccountInventoryOperationResult.Failure(AccountInventoryFailureCode.InvalidInput));
    }

    private sealed class TestSessionService(RecoverySessionWorkspace workspace) : IRecoverySessionService
    {
        public event EventHandler? SessionChanged;

        public RecoverySessionLoadState LoadState => RecoverySessionLoadState.Loaded;

        public RecoverySessionWorkspace? CurrentSession { get; } = workspace;

        public RecoveryDashboardSnapshot? Dashboard => CurrentSession?.CreateDashboardSnapshot();

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<RecoverySessionOperationResult> CreateAsync(RecoverySessionCreateRequest request, CancellationToken cancellationToken) => Unsupported();

        public Task<RecoverySessionOperationResult> PauseAsync(CancellationToken cancellationToken) => Unsupported();

        public Task<RecoverySessionOperationResult> ResumeAsync(CancellationToken cancellationToken) => Unsupported();

        public Task<RecoverySessionOperationResult> ArchiveAsync(CancellationToken cancellationToken) => Unsupported();

        public Task<RecoverySessionOperationResult> ReplaceAccountSummariesAsync(IReadOnlyCollection<RecoveryAccountDashboardEntry> accounts, CancellationToken cancellationToken) => Unsupported();

        public void ClearForLock() => SessionChanged?.Invoke(this, EventArgs.Empty);

        private static Task<RecoverySessionOperationResult> Unsupported() =>
            Task.FromResult(RecoverySessionOperationResult.Failure(RecoverySessionOperationFailureCode.InvalidInput));
    }
}
