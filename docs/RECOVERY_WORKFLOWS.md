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

## Workflow Structure

A provider workflow contains:

- provider metadata
- supported account types
- recovery locations
- action definitions
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

When the user decides not to complete a required action, unpwn records an unresolved risk. The account is not shown as fully secured.

## Completion Rules

An account is **fully reviewed** when every applicable required action is either:

- completed, or
- marked not applicable with a recorded reason

An account is **not fully secured** when:

- a required action is blocked
- a required action failed
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

Repository-controlled workflow definitions are represented in code as immutable `RecoveryWorkflowDefinition` records until the on-disk workflow package format is introduced. Each definition includes provider metadata, supported account type, workflow version, verification date, official recovery locations, expected origins, and ordered recovery actions.

The validation boundary for these definitions is `RecoveryWorkflowValidator`. It currently rejects missing required metadata, future verification dates, duplicate location or action identifiers, non-HTTPS recovery locations, recovery-location origin mismatches, missing expected origins, missing prerequisite targets, prerequisite cycles, required actions without completion criteria, and claims of fully automated recovery. Fully automated recovery is intentionally disallowed at this stage because repository workflows may guide or navigate, but they must not bypass user-visible security decisions.

The shipped provider catalog validates all repository workflows through `RepositoryWorkflowCatalog.ValidateAll()`. Provider workflow changes should add or update catalog entries and regression tests that prove the definition remains structurally and semantically safe.
