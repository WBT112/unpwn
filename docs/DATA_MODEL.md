# Data Model

## Goals

The data model must support:

- recovery sessions that last days or weeks
- many accounts per session
- service-specific workflows
- dependencies between accounts and actions
- encrypted generated credentials
- reliable progress reporting
- an audit history without storing secrets in audit events

## Core Entities

### RecoverySession

Represents one recovery effort after a suspected incident.

Suggested fields:

- `Id`
- `Name`
- `Status`
- `CreatedAt`
- `UpdatedAt`
- `CompletedAt`
- `SecurityWarningAcknowledgedAt`

Session status values:

- `ACTIVE`
- `PAUSED`
- `COMPLETED`
- `ARCHIVED`

### Account

Represents one user account or digital identity to review.

Suggested fields:

- `Id`
- `RecoverySessionId`
- `ProviderId`
- `DisplayName`
- `LoginIdentifier`
- `AccountUrl`
- `Priority`
- `Status`
- `CreatedAt`
- `UpdatedAt`

Account priority values:

- `CRITICAL`
- `HIGH`
- `NORMAL`
- `LOW`

Account status values:

- `OPEN`
- `IN_PROGRESS`
- `BLOCKED`
- `FULLY_REVIEWED`
- `REVIEWED_WITH_UNRESOLVED_RISK`
- `ACCESS_LOST`

### AccountDependency

Represents a recovery dependency between accounts or channels. A dependency means the source account should wait for the target account or recovery channel to be secured first, for example when password-reset links for a shopping account are sent to a primary email account.

Suggested fields:

- `Id`
- `SourceAccountId`
- `TargetAccountId`
- `DependencyType`
- `Reason`
- `Description`

Recovery-order planning treats dependency roots as earlier work, keeps critical accounts ahead of lower-priority accounts when dependencies permit it, and surfaces imported dependencies that reference unknown accounts. Dependency cycles are blocking issues because the user must decide which account or channel can be recovered manually before the dependent chain can continue.

Topological order and current readiness are separate. A dependent account may appear later in the recommended plan but remains `WAITING_FOR_DEPENDENCIES` until every target account is fully reviewed. `READY` means the account can be worked on now.

Examples:

- password reset depends on primary email
- MFA depends on mobile phone access
- organization account depends on an identity provider

Dependency type values may include:

- `PASSWORD_RESET_CHANNEL`
- `MFA_CHANNEL`
- `IDENTITY_PROVIDER`
- `RECOVERY_CONTACT`
- `OTHER`

### RecoveryWorkflowDefinition

A versioned provider-defined workflow template.

Suggested fields:

- `WorkflowId`
- `ProviderId`
- `ProviderName`
- `WorkflowVersion`
- `SupportedAccountType`
- `VerifiedAt`
- `RecoveryLocations`
- `Actions`

Definitions are repository-controlled and shipped with an application release. Provider validation and runtime action instances use the same canonical types from `Unpwn.Core`.

### RecoveryActionDefinition

Describes one action in a workflow template.

Suggested fields:

- `Id`
- `Type`
- `Requirement`
- `Importance`
- `RecoveryPaths`
- `AutomationSupport`
- `Prerequisites`
- `CompletionCriteria`

Importance values and their progress weights are:

- `CRITICAL`: `5`
- `IMPORTANT`: `3`
- `ROUTINE`: `1`

### RecoveryActionInstance

The mutable state of an action for one account.

Suggested fields:

- `Id`
- `AccountId`
- `DefinitionId`
- `Status`
- `SelectedRecoveryPath`
- `StartedAt`
- `CompletedAt`
- `StatusReason`
- `NotApplicableDisposition`
- `HasUnresolvedRisk`
- `UserNotes`

Status values:

- `OPEN`
- `IN_PROGRESS`
- `BLOCKED`
- `NEEDS_USER_ACTION`
- `COMPLETED`
- `FAILED`
- `NOT_APPLICABLE`

`NOT_APPLICABLE` always requires a reason and one explicit disposition:

- `TRULY_NOT_APPLICABLE`: the capability is absent for the account type and the action is excluded from required progress
- `UNRESOLVED_RISK`: the control is relevant but unavailable or declined; the action remains in required progress and prevents a fully secured result

### CredentialEntry

Stores a newly generated credential during recovery.

Suggested fields:

- `Id`
- `AccountId`
- `EncryptedSecret`
- `GeneratedAt`
- `UsedAt`
- `ConfirmedAt`
- `ExportedAt`
- `DeletedAt`

Old credentials are never stored.

### RecoveryProgress

Reports recovery status without implying that unresolved risks are secured. It includes:

- critical accounts secured versus total critical accounts
- overall accounts fully reviewed versus total accounts
- weighted required-action completion using action importance
- blocked required-action count
- failed required-action count
- unresolved-risk count

Critical-account readiness is calculated separately from the overall percentage so blocked critical accounts and accepted unresolved risks remain visible.

### AuditEvent

Records meaningful recovery-state changes without containing user-controlled free text.

Implemented structured fields:

- `OccurredAt`
- `EventType`
- `AccountId` when relevant
- `ActionType` when relevant

Examples:

- account imported
- priority changed
- action started
- action completed
- unresolved risk accepted
- credential exported
- vault locked

Human notes and detailed reasons belong to encrypted domain records, not audit event summaries. Audit events must never contain passwords, vault keys, reset tokens, MFA secrets, recovery codes, account notes, or browser content.

## Progress Model

A single percentage can create false confidence. unpwn therefore reports several related indicators.

### Critical account readiness

Display:

- number of critical accounts fully reviewed
- total number of critical accounts
- critical accounts that remain blocked, failed, or carry unresolved risk

This is the primary emergency indicator.

### Account coverage

Formula:

```text
fully reviewed accounts / all included accounts
```

Accounts reviewed with unresolved risk are shown separately and do not count as fully reviewed.

### Weighted action progress

Required applicable actions receive fixed weights:

- critical action: `5`
- important action: `3`
- routine action: `1`

Formula:

```text
sum(weights of completed required actions)
/
sum(weights of all applicable required actions)
```

Only actions marked `NOT_APPLICABLE` with a reason and the `TRULY_NOT_APPLICABLE` disposition are excluded from the denominator.

Blocked, failed, and unresolved required actions remain in the denominator.

Optional actions may be displayed separately but do not affect the required-action percentage.

### Blocked and unresolved work

The UI must always show:

- blocked actions
- failed actions
- accounts with unresolved risks
- accounts for which access could not be restored

A high action-progress percentage must not hide these conditions.

## Completion

A recovery session can be marked completed only through an explicit user action.

Before completion, unpwn summarizes:

- critical accounts not fully reviewed
- required actions not completed
- unresolved risks
- credentials not exported or deliberately deleted
- plaintext export files that may still require cleanup

The user may complete a session with unresolved risks, but the final report must preserve those risks and must not describe the session as fully secured.
