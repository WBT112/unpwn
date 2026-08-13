# Canonical Account Recovery Execution

## Purpose

The guided workflow screen needs one persisted source of truth for an account's selected provider workflow, recovery path, access state, action states, timestamps, unresolved risks, user notes, and generated-credential references.

`AccountRecoveryExecutionState` is that canonical execution aggregate. It is stored as an authenticated
encrypted `account-execution` record with the opaque inventory account ID as its record identifier.
Account inventory remains the source for account identity metadata, the local catalog suggestion, and
the user's explicit recovery category. Category determines when an account is recommended; this
execution aggregate and its provider workflow determine how the account is recovered. Dashboard
entries and recovery-plan summaries are projections; they are not independent workflow state.

## Identity and versioning

An execution is bound to:

- inventory `AccountId`
- canonical `ProviderId`
- repository `WorkflowId`
- repository `WorkflowVersion`
- automatically selected `RecoveryPath` and structured selection reason
- monotonically increasing revision

Loading an execution against a different provider, workflow, version, or path fails closed. Repository workflow changes therefore cannot silently reinterpret persisted action state.

If a reviewed provider workflow is added after an account has already recorded generic-workflow
history, the application first detects the identity mismatch and then attempts the exact stable
generic definition. A matching generic execution remains active and is visibly labelled as preserved
general history. It is not migrated, replaced, or marked complete automatically; corrupted or
otherwise mismatched state still fails closed.

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
- stable resource keys for explicitly acknowledged completion criteria

The aggregate never stores translated labels, provider-page content, browser state, reset links, MFA secrets, recovery codes, cookies, or generated secret values. Completion acknowledgements reference only repository-controlled criterion keys from the matching action definition.

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
- explicit confirmed access, lost access, or provider failure may trigger the canonical automatic
  selector; users do not directly choose or change the recovery-path enum
- each automatic fallback preserves the prior approach and its structured transition reason before
  materializing the next path's actions
- when no safe supported fallback exists, failed or lost-access work stays visible and the execution
  records `NoSafeSupportedPath` instead of inventing success

Repository actions may reference one reviewed recovery-location identifier from their workflow.
Validation rejects unknown identifiers. The UI therefore does not infer provider URLs from translated
guidance or action names.

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

## Guided workflow UI

The workflow UI consumes this application service rather than mutating action instances or dashboard
summaries directly. It:

- display inventory metadata alongside the canonical execution
- display the automatically selected recovery approach and its reason without a path chooser
- resolve provider guidance through localization resources
- show the structured reason and affected prerequisites
- require explicit completion acknowledgement
- return to the recalculated dashboard/plan after each material transition
- restore focus and announce state changes according to the accessibility baseline in Issue #38

The normal journey makes only the current recommended action visually dominant. It explains why the
account and action are next, shows relevant warnings and prerequisites, and displays the reviewed
destination and expected origin before navigation. Starting or retrying a navigable step may open the
official page, but opening it, returning to unpwn, elapsed time, and restart never complete the action.
Each completion criterion is persisted atomically before its checkmark is shown as recorded. Those
explicit acknowledgements survive browser close and conservative restart, but do not complete the
action. The **Done** action remains disabled until every repository-controlled criterion is visibly
acknowledged and still requires its separate explicit confirmation.

The account-level overview owns **Start recovery** and **Skip account for now**. Start recovery is a
single presentation/application transaction over this canonical aggregate; it does not introduce a
second browser state machine. Skip is persisted as language-neutral session queue metadata, not as an
action transition, `NotApplicable`, completion, or risk acceptance. Deferring keeps the execution and
all required work unchanged and moves the account behind non-deferred work for the current pass.

If the user cannot continue, the presentation asks one understandable follow-up question and maps the
answer to the existing canonical transition model:

- lost access uses `SetAccessLost`;
- provider waiting or review uses `SetWaitingForProviderReview`;
- a missing account or prerequisite uses `BlockAction`;
- provider failure uses `FailAction`;
- a genuinely absent capability uses confirmed `MarkTrulyNotApplicable`;
- deliberately unfinished required work uses confirmed `AcceptUnresolvedRisk`.

All material answers require a non-secret reason and return to the recalculated plan. Technical access,
path, action-state, reason, note, and outcome controls remain inspectable behind **Details / advanced
status**. Guided and advanced controls call the same `IAccountRecoveryExecutionService`; neither owns a
second execution state.

Each material outcome persists the execution and dashboard projection atomically and returns to the
recalculated dashboard recommendation. A visible provider-page
handoff shows the reviewed destination and expected origins before using the operating-system launcher.
Opening the page changes only transient presentation status and never changes an action state.
