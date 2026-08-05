# Recovery Workflows

## Purpose

A recovery workflow describes how a user restores control over an account after a suspected compromise.

Workflows are the core product capability of unpwn. Automation may assist individual steps, but a workflow remains useful even when every step must be completed manually.

## Credential Recovery Paths

Changing a credential is not a single universal operation. unpwn distinguishes three paths.

### Authenticated password change

The user can access the account or has a valid session and changes the password through account settings.

Possible requirements:

- current password
- recent re-authentication
- MFA challenge
- confirmation through email or another trusted device

This path may be faster, but it is not always available and may depend on credentials the user no longer knows.

### Password reset

The user starts a "forgot password" or equivalent workflow.

A reset may require:

- account email address or username
- access to a primary email account
- SMS, authenticator, passkey, recovery code, or trusted-device confirmation
- identity verification
- opening a time-limited reset link

A password reset is therefore often a multi-account workflow. The primary email account or another recovery channel must be secured before dependent accounts are reset.

### Manual account recovery

The normal change and reset paths are unavailable or fail.

This path may require:

- provider-specific recovery forms
- waiting periods
- proof of ownership
- support interaction
- manual review by the service provider

unpwn may guide and track this process but cannot guarantee that the provider restores access.

## Account Dependencies

Accounts may depend on other accounts or recovery channels.

Examples:

- an online shop sends password-reset links to a primary email account
- a cloud account uses a secondary email address
- a developer account depends on an organization-managed identity
- a financial account requires access to a registered phone number

The Recovery Engine should identify explicit dependencies and recommend an order that secures dependency roots first.

Typical critical roots include:

1. primary email accounts
2. password managers
3. operating-system or platform identities
4. mobile phone and carrier accounts
5. financial accounts

A dependency does not automatically prove that an account is compromised. It only affects recovery order and blocking conditions.

`READY` in a recovery-order plan means that the account can be worked on now. An account whose dependency is only earlier in the topological plan, but has not yet been fully reviewed, remains `WAITING_FOR_DEPENDENCIES`.

## Workflow Structure

A provider workflow contains:

- provider metadata
- supported account types
- recovery locations
- action definitions
- supported recovery paths for each action
- prerequisites and dependencies
- action importance
- automation support level
- completion criteria
- verification date and tests

Example actions:

- confirm access to the account
- change or reset password
- invalidate active sessions
- review trusted devices
- review MFA methods
- regenerate recovery codes
- review recovery email addresses and phone numbers
- revoke app passwords
- review OAuth applications
- review API tokens, access keys, or SSH keys
- record unresolved risks

The canonical workflow and action types live in `Unpwn.Core`. Provider catalogs, contract validation, and runtime action instances must use these same types rather than parallel provider-only and runtime-only models.

An action may support one or more recovery paths. Prerequisites must be executable on the same path. The initial catalog deliberately uses path-specific action definitions where prerequisite chains differ, avoiding ambiguous OR-prerequisites between authenticated change and password reset.

## Action Status

Each recovery action instance has one of these states:

- `OPEN`
- `IN_PROGRESS`
- `BLOCKED`
- `NEEDS_USER_ACTION`
- `COMPLETED`
- `FAILED`
- `NOT_APPLICABLE`

A required action cannot be silently skipped.

`NOT_APPLICABLE` requires both a recorded reason and an explicit disposition:

- **truly not applicable:** the capability does not exist for this account type; the action is excluded from required-action progress
- **unresolved risk:** the security control would be relevant but cannot be completed; it remains in required-action progress and the account is not fully secured

When the user decides not to complete a required action, unpwn records an unresolved risk. The account is not shown as fully secured.

## Completion Rules

An account is **fully reviewed** when every applicable required action is completed and every excluded action is explicitly documented as truly not applicable.

An account is **not fully secured** when:

- a required action is blocked
- a required action failed
- a relevant control was marked unavailable with unresolved risk
- the user accepted an unresolved risk
- access to the account could not be restored

These distinctions must remain visible in the UI and exported recovery report.

## Automation Levels

Actions declare one of these support levels:

- `NONE`: instructions and tracking only
- `NAVIGATION`: open the correct recovery location
- `ASSISTED`: visible browser assistance with user interaction
- `AUTOMATED`: a bounded action can be executed automatically with explicit authorization

The support level describes technical capability, not permission to bypass provider security mechanisms.

unpwn does not bypass CAPTCHA, MFA, identity verification, or account-ownership checks.

## Contribution Model

Recovery workflows are maintained in the unpwn repository.

New workflows and updates are submitted through pull requests and must include:

- a clear description of the provider and supported account type
- documented recovery actions and URLs
- tests for machine-readable definitions or code
- a verification date
- no embedded secrets or personal account data

unpwn does not download or execute third-party provider plugins at runtime.

## Repository Workflow Definitions

Repository-controlled workflow definitions are represented in code as immutable `RecoveryWorkflowDefinition` records until the on-disk workflow package format is introduced. Each definition includes provider metadata, supported account type, workflow version, verification date, official recovery locations, expected origins, and recovery actions.

The validation boundary for these definitions is `RecoveryWorkflowValidator`. It rejects missing required metadata, a verification date later than the supplied or current validation date, duplicate location or action identifiers, invalid HTTPS locations or origins, missing or duplicate recovery paths, missing prerequisite targets, prerequisite cycles, required actions without non-empty completion criteria, and claims of fully automated recovery. Fully automated recovery is intentionally disallowed at this stage because repository workflows may guide or navigate, but they must not bypass user-visible security decisions.

Provider contract scenarios additionally prove that every expected action supports the scenario path, all prerequisites are present earlier on that path, expectations match the workflow definition, and a scenario claiming full security includes every required action for that path.

The shipped provider catalog validates all repository workflows through `RepositoryWorkflowCatalog.ValidateAll()`. Provider workflow changes should add or update catalog entries and regression tests that prove the definition remains structurally and semantically safe.
