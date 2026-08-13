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
        Assert.Contains("choice", viewModel.CategoryDecisionText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RecoveryPath.PasswordReset, viewModel.SelectedPath?.Path);
        Assert.Contains("password-reset", viewModel.PathSelectionReasonText, StringComparison.OrdinalIgnoreCase);

        await viewModel.BeginCommand.ExecuteAsync();
        var actionId = viewModel.SelectedAction!.DefinitionId;
        var path = viewModel.SelectedPath!.Path;

        fixture.Localization.SetLanguage("de");

        Assert.Equal(actionId, viewModel.SelectedAction.DefinitionId);
        Assert.Equal(path, viewModel.SelectedPath.Path);
        Assert.Contains("Auswahl", viewModel.CategoryDecisionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolvesGoogleAccountToReviewedAutomaticResetWorkflow()
    {
        var fixture = new Fixture(
            providerId: "Google",
            accountUrl: "https://myaccount.google.com/security");
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "reset-password");
        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.True(viewModel.HasWorkflow);
        Assert.Equal("Google", viewModel.ProviderName);
        Assert.Equal(
            "https://accounts.google.com/signin/recovery",
            fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
    }

    [Fact]
    public async Task ResolvesMicrosoftAccountToReviewedAutomaticResetWorkflow()
    {
        var fixture = new Fixture(
            providerId: "Microsoft",
            accountUrl: "https://account.microsoft.com/security");
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "reset-password");
        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.True(viewModel.HasWorkflow);
        Assert.Equal("Microsoft", viewModel.ProviderName);
        Assert.Equal(
            "https://account.live.com/password/reset",
            fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
    }

    [Fact]
    public async Task BrowserNavigationLeavesActionOpenEvenWhenTheProviderPageOpens()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "reset-password");
        var revision = fixture.Execution.State!.Revision;
        var applyCalls = fixture.Execution.ApplyCalls;

        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.Equal(1, fixture.ExternalNavigation.OpenCalls);
        Assert.Equal("https://github.com/password_reset", fixture.ExternalNavigation.LastDestination?.AbsoluteUri);
        Assert.Equal(applyCalls, fixture.Execution.ApplyCalls);
        Assert.Equal(revision, fixture.Execution.State.Revision);
        Assert.Equal(RecoveryActionStatus.Open, fixture.Execution.State.GetAction("reset-password").Status);
        Assert.Contains("remains unchanged", viewModel.NavigationStatus, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("review-api-tokens-reset", "https://github.com/settings/tokens")]
    [InlineData("review-ssh-signing-keys-reset", "https://github.com/settings/keys")]
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
        Assert.Equal(RecoveryActionStatus.InProgress, fixture.Execution.State!.GetAction("identify-account-reset").Status);

        foreach (var criterion in viewModel.CompletionCriteria)
        {
            await criterion.ToggleCommand.ExecuteAsync();
        }
        Assert.True(viewModel.CompleteActionCommand.CanExecute(null));
        await viewModel.CompleteActionCommand.ExecuteAsync();

        Assert.Equal(RecoveryActionStatus.Completed, fixture.Execution.State.GetAction("identify-account-reset").Status);
        Assert.Single(returned);
        Assert.Equal(1, fixture.ConfirmationCalls);
    }

    [Fact]
    public async Task ChecklistConfirmationSurvivesBrowserCloseAndReloadWithoutCompletingAction()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.StartActionCommand.ExecuteAsync();
        var criterion = Assert.Single(viewModel.CompletionCriteria);

        await criterion.ToggleCommand.ExecuteAsync();
        var recordedRevision = fixture.Execution.State!.Revision;
        viewModel.ReportRecoveryBrowserClosed();

        Assert.True(criterion.IsAcknowledged);
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State.GetAction(viewModel.SelectedAction!.DefinitionId).Status);
        Assert.Contains("still open", viewModel.NavigationStatus, StringComparison.Ordinal);

        var resumed = fixture.CreateViewModel();
        await resumed.RefreshCommand.ExecuteAsync();

        Assert.True(Assert.Single(resumed.CompletionCriteria).IsAcknowledged);
        Assert.True(resumed.CompletionCriteriaAcknowledged);
        Assert.Equal(recordedRevision, fixture.Execution.State.Revision);
        Assert.True(resumed.CompleteActionCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedChecklistPersistenceDoesNotDisplayARecordedCheckmark()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.StartActionCommand.ExecuteAsync();
        var criterion = Assert.Single(viewModel.CompletionCriteria);
        fixture.Execution.FailNextApply = true;

        await criterion.ToggleCommand.ExecuteAsync();

        Assert.False(criterion.IsAcknowledged);
        Assert.False(viewModel.CompletionCriteriaAcknowledged);
        Assert.True(viewModel.HasValidationMessage);
        Assert.Empty(fixture.Execution.State!.GetAction(viewModel.SelectedAction!.DefinitionId)
            .AcknowledgedCompletionCriteria);
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
    public async Task ExplicitAccessConfirmationAutomaticallySelectsAuthenticatedChange()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        Assert.Equal(RecoveryPath.PasswordReset, fixture.Execution.State!.SelectedPath);

        await viewModel.SetAccessAvailableCommand.ExecuteAsync();

        Assert.Equal(RecoveryPath.AuthenticatedChange, fixture.Execution.State.SelectedPath);
        Assert.Equal(
            RecoveryPathSelectionReasonCode.ConfirmedAuthenticatedAccess,
            fixture.Execution.State.PathSelectionReason);
        Assert.Contains("explicitly confirmed", viewModel.PathSelectionReasonText, StringComparison.OrdinalIgnoreCase);
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
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "reset-password");
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
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "reset-password");
        var returned = false;
        viewModel.PlanReturnRequested += (_, _) => returned = true;

        await viewModel.StartActionCommand.ExecuteAsync();

        Assert.True(returned);
        Assert.True(viewModel.HasRecordedReason);
        Assert.Contains("Identify the account and reset channel", viewModel.RecordedReasonText, StringComparison.Ordinal);
        Assert.Equal(RecoveryActionStatus.Blocked, fixture.Execution.State!.GetAction("reset-password").Status);
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
    public async Task GuidedRecoveryBrowserStartsButNeverCompletesTheAction()
    {
        var fixture = new Fixture { Confirm = true };
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.SetAccessAvailableCommand.ExecuteAsync();
        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();
        foreach (var criterion in viewModel.CompletionCriteria)
        {
            await criterion.ToggleCommand.ExecuteAsync();
        }
        await viewModel.CompleteActionCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action => action.DefinitionId == "change-password");
        var browserRequests = new List<RecoveryBrowserWorkspaceRequest>();
        viewModel.RecoveryBrowserRequested += (_, request) => browserRequests.Add(request);

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();

        Assert.Equal(0, fixture.ExternalNavigation.OpenCalls);
        var browserRequest = Assert.Single(browserRequests);
        Assert.Equal(
            "https://github.com/settings/security",
            browserRequest.Handoff.Destination.AbsoluteUri);
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State!.GetAction("change-password").Status);
        Assert.False(viewModel.CompleteActionCommand.CanExecute(null));
        Assert.Contains("isolated Recovery Browser", viewModel.NavigationStatus, StringComparison.Ordinal);
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
            fixture.Execution.State!.GetAction("identify-account-reset").Status);
        Assert.True(viewModel.GuidedPrimaryActionCommand.CanExecute(null));

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State.GetAction("identify-account-reset").Status);

        viewModel.ShowProblemReviewCommand.Execute(null);
        viewModel.SelectedProblem = viewModel.ProblemOptions.Single(option =>
            option.Value == GuidedRecoveryProblem.ProviderStepFailed);
        viewModel.Reason = "Synthetic provider rejected the step.";
        await viewModel.ApplyGuidedProblemCommand.ExecuteAsync();

        Assert.Equal(RecoveryPath.ManualRecovery, fixture.Execution.State.SelectedPath);
        Assert.Equal(
            RecoveryPathSelectionReasonCode.ProviderFailureFallback,
            fixture.Execution.State.PathSelectionReason);
        Assert.Equal("identify-account-manual", viewModel.SelectedAction?.DefinitionId);
        Assert.Single(fixture.Execution.State.PreviousPathAttempts);
        Assert.True(viewModel.GuidedPrimaryActionCommand.CanExecute(null));

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();

        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State.GetAction("identify-account-manual").Status);
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
            notApplicableFixture.Execution.State!.GetAction("identify-account-reset").Status);
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

        Assert.True(riskFixture.Execution.State!.GetAction("identify-account-reset").HasUnresolvedRisk);
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
            action.DefinitionId == "reset-password");

        Assert.True(viewModel.CurrentActionFocusRequest > previousRequest);
    }

    [Fact]
    public async Task PasswordStepGeneratesAndAttachesOnlyAnOpaqueCredentialReference()
    {
        var fixture = new Fixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.SetAccessAvailableCommand.ExecuteAsync();
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
    public async Task UnsupportedProviderUsesClearlyLabelledGeneralWorkflow()
    {
        var fixture = new Fixture(
            providerId: "unsupported.example",
            accountUrl: "https://unsupported.example.test/account");
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.True(viewModel.HasWorkflow);
        Assert.True(viewModel.IsGeneralManualWorkflow);
        Assert.False(viewModel.IsReviewedProviderWorkflow);
        Assert.Contains("not provider-specific", viewModel.WorkflowTrustTitle, StringComparison.Ordinal);
        Assert.Contains("may differ", viewModel.WorkflowTrustMessage, StringComparison.Ordinal);
        Assert.Equal(RecoveryPath.PasswordReset, viewModel.SelectedPath?.Path);
        Assert.Contains("password-reset", viewModel.PathSelectionReasonText, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.HasValidationMessage);
    }

    [Fact]
    public async Task GenericPasswordDiscoveryRequiresReviewBeforeOpeningAndNeverCompletesAction()
    {
        var fixture = new Fixture(
            providerId: "unsupported.example",
            accountUrl: "https://unsupported.example.test/account")
        {
            Confirm = true,
        };
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.SetAccessAvailableCommand.ExecuteAsync();
        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();
        foreach (var criterion in viewModel.CompletionCriteria)
        {
            await criterion.ToggleCommand.ExecuteAsync();
        }
        await viewModel.CompleteActionCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "change-password");

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();

        Assert.Equal(1, fixture.LocationDiscovery.Calls);
        Assert.Equal(RecoveryLocationSelectionPolicy.WellKnownFirst,
            fixture.LocationDiscovery.LastRequest?.SelectionPolicy);
        Assert.True(viewModel.HasPreparedNavigation);
        Assert.Contains("/.well-known/change-password", viewModel.OfficialLocationText, StringComparison.Ordinal);
        Assert.Equal(0, fixture.ExternalNavigation.OpenCalls);
        var revision = fixture.Execution.State!.Revision;

        await viewModel.OpenOfficialPageCommand.ExecuteAsync();

        Assert.Equal(1, fixture.ExternalNavigation.OpenCalls);
        Assert.Equal(revision, fixture.Execution.State.Revision);
        Assert.Equal(
            RecoveryActionStatus.InProgress,
            fixture.Execution.State.GetAction("change-password").Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://unsupported.example.test/account")]
    public async Task GenericWorkflowProvidesManualGuidanceWhenSafeNavigationIsUnavailable(
        string accountUrl)
    {
        var fixture = new Fixture("unsupported.example", accountUrl);
        fixture.LocationDiscovery.Result = RecoveryLocationDiscoveryResult.Failure(
            RecoveryLocationDiscoveryFailureCode.InsecureAccountOrigin);
        var viewModel = fixture.CreateViewModel();
        await viewModel.RefreshCommand.ExecuteAsync();
        await viewModel.BeginCommand.ExecuteAsync();
        await viewModel.SetAccessAvailableCommand.ExecuteAsync();
        viewModel.SelectedAction = viewModel.Actions.Single(action =>
            action.DefinitionId == "change-password");

        await viewModel.GuidedPrimaryActionCommand.ExecuteAsync();

        Assert.False(viewModel.HasPreparedNavigation);
        Assert.Equal(0, fixture.ExternalNavigation.OpenCalls);
        Assert.DoesNotContain("unsupported.example.test/account", viewModel.NavigationStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExistingGenericHistoryIsPreservedWhenReviewedWorkflowBecomesAvailable()
    {
        var fixture = new Fixture();
        var generic = RepositoryWorkflowCatalog.CreateGenericManualWorkflow("github.com");
        var state = AccountRecoveryExecutionState.Create(
            fixture.AccountId,
            generic,
            StartedAt)
            .StartAction(generic, "identify-account-reset", StartedAt.AddSeconds(30))
            .FailActionAndSelectFallback(
                generic,
                "identify-account-reset",
                "The reset approach is unavailable for this synthetic account.",
                StartedAt.AddMinutes(1));
        state = state.StartAction(generic, "identify-account-manual", StartedAt.AddMinutes(2));
        fixture.Execution.Seed(state);
        var viewModel = fixture.CreateViewModel();

        await viewModel.RefreshCommand.ExecuteAsync();

        Assert.True(viewModel.IsGeneralManualWorkflow);
        Assert.True(viewModel.HasExecution);
        Assert.Contains("keeps that history", viewModel.WorkflowTrustMessage, StringComparison.Ordinal);
        Assert.Equal(state.Revision, fixture.Execution.State?.Revision);
        Assert.Equal("identify-account-manual", viewModel.SelectedAction?.DefinitionId);
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
            string? accountUrl = "https://github.com/settings/security")
        {
            var account = new AccountInventoryEntry(
                _accountId,
                providerId,
                $"{providerId} recovery account",
                "synthetic-user",
                accountUrl,
                AccountRecoveryCategory.Critical,
                RepositoryAccountClassificationCatalog.CurrentVersion,
                AccountRecoveryCategory.Critical,
                CategoryConfirmedRevision: 1,
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

        public Guid AccountId => _accountId;

        public TestInventoryService Inventory { get; }

        public TestSessionService Session { get; }

        public TestExecutionService Execution { get; } = new();

        public TestExternalNavigationService ExternalNavigation { get; } = new();

        public TestLocationDiscoveryService LocationDiscovery { get; } = new();

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
                LocationDiscovery,
                ExternalNavigation,
                new TestConfirmationDialogService((_, _) =>
                {
                    ConfirmationCalls++;
                    return Task.FromResult(Confirm);
                }),
                Localization,
                Credentials,
                new TestBrowserSessionLifecycle());

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
                RecommendedActionId: null)
            {
                Category = AccountRecoveryCategory.Critical,
            };
    }

    private sealed class TestBrowserSessionLifecycle : IRecoveryBrowserSessionLifecycle
    {
        public event EventHandler<RecoveryBrowserSessionLifecycleSnapshot>? StateChanged
        {
            add { }
            remove { }
        }

        public RecoveryBrowserSessionLifecycleSnapshot Current { get; } = new(
            RecoveryBrowserSessionLifecycleState.Idle,
            null,
            [],
            RecoveryBrowserSessionFailureCode.None);

        public RecoveryBrowserSessionLifecycleSnapshot InspectStartup() => Current;

        public RecoveryBrowserSessionStartResult Start(Guid accountId) =>
            new(null, false, RecoveryBrowserSessionFailureCode.StorageUnavailable);

        public Task<RecoveryBrowserSessionCleanupResult> EndAsync(
            Guid sessionId,
            IRecoveryBrowserSessionResources resources,
            CancellationToken cancellationToken) => Task.FromResult(
                new RecoveryBrowserSessionCleanupResult(
                    false,
                    RecoveryBrowserSessionFailureCode.SessionNotFound));

        public Task<RecoveryBrowserSessionCleanupResult> RetryOrphanCleanupAsync(
            Guid sessionId,
            CancellationToken cancellationToken) => Task.FromResult(
                new RecoveryBrowserSessionCleanupResult(
                    false,
                    RecoveryBrowserSessionFailureCode.SessionNotFound));
    }

    private sealed class TestExecutionService : IAccountRecoveryExecutionService
    {
        private DateTimeOffset _clock = StartedAt.AddMinutes(2);

        public AccountRecoveryExecutionState? State { get; private set; }

        public int ApplyCalls { get; private set; }

        public bool FailNextApply { get; set; }

        public void Seed(AccountRecoveryExecutionState state) => State = state;

        public Task<AccountRecoveryExecutionResult> LoadAsync(
            Guid accountId,
            RecoveryWorkflowDefinition workflow,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (State is null)
            {
                return Task.FromResult(AccountRecoveryExecutionResult.Failure(
                    AccountRecoveryExecutionFailureCode.NotFound));
            }

            try
            {
                State.Validate(workflow);
                return Task.FromResult(AccountRecoveryExecutionResult.Success(State));
            }
            catch (InvalidOperationException)
            {
                return Task.FromResult(AccountRecoveryExecutionResult.Failure(
                    AccountRecoveryExecutionFailureCode.Corrupted));
            }
        }

        public Task<AccountRecoveryExecutionResult> CreateAsync(
            AccountRecoveryExecutionCreateRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = AccountRecoveryExecutionState.Create(
                request.AccountId,
                request.Workflow,
                NextTime());
            return Task.FromResult(AccountRecoveryExecutionResult.Success(State));
        }

        public Task<AccountRecoveryExecutionResult> ApplyAsync(
            AccountRecoveryExecutionTransitionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCalls++;
            if (FailNextApply)
            {
                FailNextApply = false;
                return Task.FromResult(AccountRecoveryExecutionResult.Failure(
                    AccountRecoveryExecutionFailureCode.PersistenceFailure));
            }
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
                    AccountRecoveryExecutionTransitionKind.SetAccessAvailable =>
                        State.SetAccessState(request.Workflow, RecoveryAccessState.Available, null, time),
                    AccountRecoveryExecutionTransitionKind.SetAccessLost =>
                        State.SetAccessState(request.Workflow, RecoveryAccessState.Lost, request.UserReason, time),
                    AccountRecoveryExecutionTransitionKind.SetWaitingForProviderReview =>
                        State.SetAccessState(
                            request.Workflow,
                            RecoveryAccessState.WaitingForProviderReview,
                            request.UserReason,
                            time),
                    AccountRecoveryExecutionTransitionKind.StartAction =>
                        State.StartAction(request.Workflow, request.ActionDefinitionId!, time),
                    AccountRecoveryExecutionTransitionKind.SetCompletionCriteriaAcknowledgements =>
                        State.SetCompletionCriteriaAcknowledgements(
                            request.Workflow,
                            request.ActionDefinitionId!,
                            request.AcknowledgedCompletionCriteria!,
                            time),
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
                        State.FailActionAndSelectFallback(
                            request.Workflow,
                            request.ActionDefinitionId!,
                            request.UserReason!,
                            time),
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
        public int Calls { get; private set; }

        public RecoveryLocationDiscoveryRequest? LastRequest { get; private set; }

        public RecoveryLocationDiscoveryResult? Result { get; set; }

        public Task<RecoveryLocationDiscoveryResult> DiscoverAsync(
            RecoveryLocationDiscoveryRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastRequest = request;
            if (Result is not null)
            {
                return Task.FromResult(Result);
            }

            if (request.ProviderLocationId is null && request.AccountUri is { } accountUri)
            {
                var origin = accountUri.GetLeftPart(UriPartial.Authority);
                return Task.FromResult(RecoveryLocationDiscoveryResult.Success(
                    new RecoveryNavigationHandoff(
                        new Uri($"{origin}/.well-known/change-password"),
                        origin,
                        [origin],
                        RecoveryLocationResolutionSource.WellKnownChangePassword,
                        RequiresVisibleConfirmation: true)));
            }

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

        public AccountInventoryPlan? CurrentPlan => CurrentInventory?.CreatePlan();

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<AccountInventoryOperationResult> UpsertAsync(AccountInventoryUpsertRequest request, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> CategorizeAsync(Guid accountId, AccountRecoveryCategory category, CancellationToken cancellationToken) => Unsupported();

        public Task<AccountInventoryOperationResult> RemoveAccountAsync(Guid accountId, CancellationToken cancellationToken) => Unsupported();

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
