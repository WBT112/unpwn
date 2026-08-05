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

Represents a recovery dependency between accounts or channels.

Suggested fields:

- `Id`
- `SourceAccountId`
- `TargetAccountId`
- `DependencyType`
- `Description`

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

- `Id`
- `ProviderId`
- `Version`
- `AccountType`
- `VerifiedAt`
- `Actions`

Definitions are repository-controlled and shipped with an application release.

### RecoveryActionDefinition

Describes one action in a workflow template.

Suggested fields:

- `Id`
- `Type`
- `Title`
- `Description`
- `Required`
- `Importance`
- `SupportedRecoveryPaths`
- `AutomationSupport`
- `PrerequisiteActionIds`

Importance values:

- `CRITICAL`
- `IMPORTANT`
- `ROUTINE`

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
- `BlockedReason`
- `UnresolvedRiskReason`
- `UserNotes`

Status values:

- `OPEN`
- `IN_PROGRESS`
- `BLOCKED`
- `NEEDS_USER_ACTION`
- `COMPLETED`
- `FAILED`
- `NOT_APPLICABLE`

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
- unresolved-risk count

Critical-account readiness is calculated separately from the overall percentage so blocked critical accounts and accepted unresolved risks remain visible.

### AuditEvent

Records meaningful recovery-state changes without containing secrets.

Suggested fields:

- `Id`
- `RecoverySessionId`
- `AccountId`
- `ActionInstanceId`
- `EventType`
- `OccurredAt`
- `SafeSummary`

Examples:

- account imported
- priority changed
- action started
- action completed
- unresolved risk accepted
- credential exported
- vault locked

Audit events must never contain passwords, vault keys, reset tokens, MFA secrets, or full sensitive browser content.

## Progress Model

A single percentage can create false confidence. unpwn therefore reports several related indicators.

### Critical account readiness

Display:

- number of critical accounts fully reviewed
- total number of critical accounts
- critical accounts that remain blocked or carry unresolved risk

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

Actions marked `NOT_APPLICABLE` with a reason are excluded from the denominator.

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
