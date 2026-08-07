# Canonical Account Recovery Execution

## Purpose

The guided workflow screen needs one persisted source of truth for an account's selected provider workflow, recovery path, access state, action states, timestamps, unresolved risks, user notes, and generated-credential references.

`AccountRecoveryExecutionState` is that canonical execution aggregate. Account inventory remains the source for account identity metadata, confirmed roles, priorities, and dependencies. Dashboard entries and recovery-plan summaries are projections; they are not independent workflow state.

## Identity and versioning

An execution is bound to:

- inventory `AccountId`
- canonical `ProviderId`
- repository `WorkflowId`
- repository `WorkflowVersion`
- selected `RecoveryPath`
- monotonically increasing revision

Loading an execution against a different provider, workflow, version, or path fails closed. Repository workflow changes therefore cannot silently reinterpret persisted action state.

## Action state

Every action instance is identified by its repository definition ID and stores:

- canonical action status
- structured reason code
- stable reason arguments such as prerequisite action IDs
- optional encrypted user-authored reason
- optional encrypted user notes
- start, completion, and update timestamps
- unresolved-risk and not-applicable disposition
- optional opaque `GeneratedCredentialReference`

The aggregate never stores translated labels, provider-page content, browser state, reset links, MFA secrets, recovery codes, cookies, or generated secret values.

## Structured reasons

Prerequisite failures use:

```text
ReasonCode = WaitingForPrerequisite
ReasonArguments = [stable action definition IDs]
```

Core recovery logic does not generate an English sentence for persistence or UI control. The presentation layer maps reason codes and typed arguments to the selected language.

User-authored reasons are required for user blocking, provider failure, not-applicable decisions, and accepted unresolved risk. They remain encrypted user content and never determine transition rules.

## Transition rules

- an action cannot start until every prerequisite is completed or truly not applicable
- attempting to start early records a structured blocked state
- completion requires explicit acknowledgement of the repository-controlled completion criteria
- blocked, failed, needs-user-action, not-applicable, and unresolved-risk transitions require their domain-defined reason data
- completed and not-applicable terminal states cannot be silently reopened
- lost access remains distinct from progress and produces `AccessNotRestored`
- browser navigation and elapsed time never complete an action

## Generated credential reference

Actions refer to generated credentials only through the opaque credential and account IDs introduced by Issue #14. Secret values are obtained only from the unlocked credential repository through a disposable lease.

## Atomic persistence

`AccountRecoveryExecutionService` uses the workspace mutation coordinator and atomic encrypted record batches. Each successful transition writes:

1. the account execution record
2. the derived recovery-session dashboard projection

Both records commit together or remain at the previous revision. The request contains an expected revision and an opaque operation ID:

- stale revisions fail with `Conflict`
- repeating an already applied operation ID returns the existing result without another transition
- failed writes do not publish the new execution or dashboard projection

## Provider guidance resources

Repository workflow definitions contain only stable resource keys for:

- title
- instruction
- warning
- completion message
- completion criteria

The GitHub workflow ships complete English and German resources. CI checks key syntax, workflow-key consistency, English/German parity, and runtime language switching. Translation changes do not alter paths, action IDs, prerequisites, automation support, URLs, expected origins, or completion semantics.

## Integration with Issue #34

The workflow UI should consume this application service rather than mutate `RecoveryActionInstance` or dashboard summaries directly. It should:

- display inventory metadata alongside the canonical execution
- resolve provider guidance through localization resources
- show the structured reason and affected prerequisites
- require explicit completion acknowledgement
- return to the recalculated dashboard/plan after each material transition
- restore focus and announce state changes according to the accessibility baseline in Issue #38

The UI must not infer an external action result from opening or returning from a provider page.
