# Guided Recovery Wizard

## Purpose

The recovery wizard is the end-to-end orchestration layer for unpwn. It guides a user from the initial trusted-device check through vault entry, incident intake, account inventory, dependency-aware recovery, credential export, completion review, and the final report.

The wizard does not implement vault cryptography, account workflows, credential export, or final reporting itself. Those capabilities remain owned by their dedicated application services and issues. The wizard coordinates them through stable, language-neutral step and status contracts.

## Security boundary

The trusted-device gate is the first mandatory branch.

- `Trusted` proceeds to vault creation or unlock.
- `NotTrusted` and `Unsure` proceed only to non-sensitive safety guidance and then stop.
- Selecting `Trusted` records a user assertion; it is not proof that the device is free of malware.
- A new or existing vault is not created or unlocked before the gate is accepted.
- Pre-vault wizard state is intentionally transient and cannot be paused or locked for later persistence.

The first persistent wizard context begins only after a vault has been created or unlocked successfully. `RecoveryWizardState.HasVaultContext` marks that boundary for later persistence integration.

## Stable step identifiers

Wizard state uses stable English-like machine identifiers, not translated labels:

```text
welcome
trusted-device-check
trusted-device-guidance
vault-entry
incident-intake
account-inventory
identity-review
recovery-plan
account-recovery
credential-export
completion-preflight
final-report
```

The presentation layer maps these identifiers and recommendation codes to localized resources. Persisted state and transition rules do not depend on the selected language.

## Step contracts

`RecoveryWizardStepCatalog` defines for each step:

- whether an unlocked vault is required
- whether the step may handle sensitive recovery data
- the safe resume step after pause, lock, restart, or interruption
- allowed forward transitions
- allowed backward transitions

All steps that may handle sensitive recovery data require an unlocked vault context.

The state machine has dedicated transitions for security-sensitive boundaries:

- trusted-device decision
- acknowledgement and stop after safety guidance
- successful vault creation or unlock
- pause
- lock
- resume
- cancellation
- final completion outcome

Generic forward navigation cannot bypass the trusted-device gate or claim that a vault is ready.

## Safe resume behavior

External provider work is never assumed to have completed because a browser was opened, the user returned to unpwn, or the application restarted.

Examples:

- pausing or locking during `account-recovery` resumes at `recovery-plan`
- pausing or locking during `final-report` resumes at `completion-preflight`
- other steps resume at their explicitly declared safe review point

Concrete state persistence and interrupted-write recovery are integrated later through the vault and resilience work. The current foundation supplies the deterministic state and transition contracts those services must store.

## Recommendation codes

`RecoveryWizardOrchestrator` exposes a stable `RecoveryWizardRecommendationCode` for the current state. These codes explain the category of the next user action without embedding display text in domain or application state.

Examples include:

- `ConfirmTrustedDevice`
- `CreateOrUnlockVault`
- `CaptureIncidentContext`
- `ReviewRecoveryPlan`
- `RecoverRecommendedAccount`
- `RunCompletionPreflight`
- `UnlockVault`

Account-specific ordering and detailed recommendation reasons are added when account roles, incident indicators, and dependency orchestration are integrated through the later wizard steps.

## Terminal outcomes

The foundation distinguishes these terminal states:

- stopped for device safety
- cancelled
- completed
- archived
- follow-up required

A terminal state cannot be resumed or advanced. Completion outcomes can be selected only from the final-report step with an unlocked vault context.

## Testing

The foundation tests verify:

- stable and unique step identifiers
- no sensitive step without an unlocked vault requirement
- prevention of trusted-device-gate bypass
- safe termination for `NotTrusted` and `Unsure`
- no pre-vault pause or lock
- the complete synthetic happy-path transition sequence
- deterministic recovery-plan loops
- safe resume after account recovery and final-report interruption
- monotonic transition timestamps
- stable recommendation codes instead of translated display strings

End-to-end GUI tests, persistence tests, real feature integration, and synthetic-provider workflow execution are added incrementally with Issues #31–#38.
