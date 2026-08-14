namespace Unpwn.Core;

public static class RecoveryWizardStateMachine
{
    public static RecoveryWizardState Advance(
        RecoveryWizardState state,
        RecoveryWizardStepId nextStep,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ArgumentNullException.ThrowIfNull(nextStep);

        if (state.CurrentStep == RecoveryWizardStepId.TrustedDeviceCheck)
        {
            throw new InvalidOperationException("Use the trusted-device decision transition at the trusted-device gate.");
        }

        if (state.CurrentStep == RecoveryWizardStepId.VaultEntry)
        {
            throw new InvalidOperationException("Use the vault-ready transition after creating or unlocking a vault.");
        }

        return MoveToAllowedStep(state, nextStep, occurredAt);
    }

    public static RecoveryWizardState GoBack(
        RecoveryWizardState state,
        RecoveryWizardStepId previousStep,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ArgumentNullException.ThrowIfNull(previousStep);
        ValidateTimestamp(state, occurredAt);

        var contract = RecoveryWizardStepCatalog.Get(state.CurrentStep);
        if (!contract.AllowedPreviousSteps.Contains(previousStep))
        {
            throw new InvalidOperationException(
                $"Cannot navigate back from '{state.CurrentStep}' to '{previousStep}'.");
        }

        var trustedDeviceDecision = previousStep == RecoveryWizardStepId.TrustedDeviceCheck
            ? TrustedDeviceDecision.NotAnswered
            : state.TrustedDeviceDecision;

        return state with
        {
            CurrentStep = previousStep,
            ResumeStep = RecoveryWizardStepCatalog.Get(previousStep).SafeResumeStep,
            TrustedDeviceDecision = trustedDeviceDecision,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState RecordTrustedDeviceDecision(
        RecoveryWizardState state,
        TrustedDeviceDecision decision,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ValidateCurrentStep(state, RecoveryWizardStepId.TrustedDeviceCheck);
        ValidateTimestamp(state, occurredAt);

        if (decision == TrustedDeviceDecision.NotAnswered)
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, "A trusted-device decision is required.");
        }

        var nextStep = decision == TrustedDeviceDecision.Trusted
            ? RecoveryWizardStepId.VaultEntry
            : RecoveryWizardStepId.TrustedDeviceGuidance;

        return state with
        {
            CurrentStep = nextStep,
            ResumeStep = RecoveryWizardStepCatalog.Get(nextStep).SafeResumeStep,
            TrustedDeviceDecision = decision,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState AcknowledgeTrustedDeviceGuidance(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ValidateCurrentStep(state, RecoveryWizardStepId.TrustedDeviceGuidance);
        ValidateTimestamp(state, occurredAt);

        if (state.TrustedDeviceDecision != TrustedDeviceDecision.NotTrusted)
        {
            throw new InvalidOperationException("Device-safety guidance requires a not-trusted decision.");
        }

        return state with
        {
            Status = RecoveryWizardLifecycleStatus.StoppedForDeviceSafety,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState ConfirmVaultReady(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ValidateCurrentStep(state, RecoveryWizardStepId.VaultEntry);
        ValidateTimestamp(state, occurredAt);

        if (state.TrustedDeviceDecision != TrustedDeviceDecision.Trusted)
        {
            throw new InvalidOperationException("A vault can be created or unlocked only after the trusted-device gate is accepted.");
        }

        return state with
        {
            CurrentStep = RecoveryWizardStepId.IncidentIntake,
            ResumeStep = RecoveryWizardStepId.IncidentIntake,
            HasVaultContext = true,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState Pause(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ValidateVaultContext(state);
        ValidateTimestamp(state, occurredAt);

        return state with
        {
            ResumeStep = RecoveryWizardStepCatalog.Get(state.CurrentStep).SafeResumeStep,
            Status = RecoveryWizardLifecycleStatus.Paused,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState Lock(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        if (state.Status is not RecoveryWizardLifecycleStatus.Active and not RecoveryWizardLifecycleStatus.Paused)
        {
            throw new InvalidOperationException($"Cannot lock a recovery wizard in the {state.Status} state.");
        }

        ValidateVaultContext(state);
        ValidateTimestamp(state, occurredAt);

        var resumeStep = state.Status == RecoveryWizardLifecycleStatus.Active
            ? RecoveryWizardStepCatalog.Get(state.CurrentStep).SafeResumeStep
            : state.ResumeStep;

        return state with
        {
            ResumeStep = resumeStep,
            Status = RecoveryWizardLifecycleStatus.Locked,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState Resume(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        if (state.Status is not RecoveryWizardLifecycleStatus.Paused and not RecoveryWizardLifecycleStatus.Locked)
        {
            throw new InvalidOperationException($"Cannot resume a recovery wizard in the {state.Status} state.");
        }

        ValidateVaultContext(state);
        ValidateTimestamp(state, occurredAt);

        return state with
        {
            CurrentStep = state.ResumeStep,
            Status = RecoveryWizardLifecycleStatus.Active,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState Cancel(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        if (state.IsTerminal)
        {
            throw new InvalidOperationException($"Cannot cancel a terminal recovery wizard in the {state.Status} state.");
        }

        ValidateTimestamp(state, occurredAt);

        return state with
        {
            Status = RecoveryWizardLifecycleStatus.Cancelled,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState Archive(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Status is not RecoveryWizardLifecycleStatus.Active and not RecoveryWizardLifecycleStatus.Paused)
        {
            throw new InvalidOperationException($"Cannot archive a recovery wizard in the {state.Status} state.");
        }

        ValidateVaultContext(state);
        ValidateTimestamp(state, occurredAt);

        return state with
        {
            Status = RecoveryWizardLifecycleStatus.Archived,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState Finish(
        RecoveryWizardState state,
        RecoveryWizardTerminalOutcome outcome,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ValidateVaultContext(state);
        ValidateCurrentStep(state, RecoveryWizardStepId.FinalReport);
        ValidateTimestamp(state, occurredAt);

        var status = outcome switch
        {
            RecoveryWizardTerminalOutcome.Completed => RecoveryWizardLifecycleStatus.Completed,
            RecoveryWizardTerminalOutcome.Archived => RecoveryWizardLifecycleStatus.Archived,
            RecoveryWizardTerminalOutcome.FollowUpRequired => RecoveryWizardLifecycleStatus.FollowUpRequired,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown recovery wizard outcome."),
        };

        return state with
        {
            Status = status,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    public static RecoveryWizardState BeginCompletionReview(
        RecoveryWizardState state,
        DateTimeOffset occurredAt)
    {
        ValidateActive(state);
        ValidateVaultContext(state);
        ValidateTimestamp(state, occurredAt);
        if (state.CurrentStep == RecoveryWizardStepId.Welcome ||
            state.CurrentStep == RecoveryWizardStepId.TrustedDeviceCheck ||
            state.CurrentStep == RecoveryWizardStepId.TrustedDeviceGuidance ||
            state.CurrentStep == RecoveryWizardStepId.VaultEntry ||
            state.CurrentStep == RecoveryWizardStepId.IncidentIntake)
        {
            throw new InvalidOperationException("Completion review requires a persisted recovery session.");
        }

        return state with
        {
            CurrentStep = RecoveryWizardStepId.CompletionPreflight,
            ResumeStep = RecoveryWizardStepId.CompletionPreflight,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    private static RecoveryWizardState MoveToAllowedStep(
        RecoveryWizardState state,
        RecoveryWizardStepId nextStep,
        DateTimeOffset occurredAt)
    {
        ValidateTimestamp(state, occurredAt);

        var currentContract = RecoveryWizardStepCatalog.Get(state.CurrentStep);
        if (!currentContract.AllowedNextSteps.Contains(nextStep))
        {
            throw new InvalidOperationException(
                $"Cannot advance the recovery wizard from '{state.CurrentStep}' to '{nextStep}'.");
        }

        var nextContract = RecoveryWizardStepCatalog.Get(nextStep);
        if (nextContract.RequiresUnlockedVault && !state.HasVaultContext)
        {
            throw new InvalidOperationException(
                $"The recovery wizard step '{nextStep}' requires an unlocked vault context.");
        }

        return state with
        {
            CurrentStep = nextStep,
            ResumeStep = nextContract.SafeResumeStep,
            Revision = state.Revision + 1,
            UpdatedAt = occurredAt,
        };
    }

    private static void ValidateActive(RecoveryWizardState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status != RecoveryWizardLifecycleStatus.Active)
        {
            throw new InvalidOperationException($"The recovery wizard is not active. Current state: {state.Status}.");
        }
    }

    private static void ValidateCurrentStep(RecoveryWizardState state, RecoveryWizardStepId expectedStep)
    {
        if (state.CurrentStep != expectedStep)
        {
            throw new InvalidOperationException(
                $"Expected recovery wizard step '{expectedStep}', but the current step is '{state.CurrentStep}'.");
        }
    }

    private static void ValidateVaultContext(RecoveryWizardState state)
    {
        if (!state.HasVaultContext)
        {
            throw new InvalidOperationException("The recovery wizard has no unlocked vault context.");
        }
    }

    private static void ValidateTimestamp(RecoveryWizardState state, DateTimeOffset occurredAt)
    {
        if (occurredAt < state.UpdatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                occurredAt,
                "Recovery wizard transitions cannot move backwards in time.");
        }
    }
}
