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
- stable localization keys for user-facing guidance where applicable
- an optional reviewed recovery-location identifier on actions that support visible navigation

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

Changing the selected path is allowed only before material action state has been recorded. Once an
action has started, failed, blocked, received notes, or references generated credentials, the user must
resolve or preserve that work instead of silently replacing it with another path.

## Language-neutral semantics

Workflow execution uses only canonical identifiers and structured fields:

- workflow, provider, action, and location identifiers
- recovery paths
- prerequisites
- requirement and importance values
- automation support
- URLs and expected origins
- completion state and structured diagnostic codes

User-facing workflow titles, descriptions, warnings, manual instructions, and completion guidance are presentation resources. They are represented by stable localization keys plus typed formatting arguments rather than one embedded display-language sentence.

Translated text must never:

- select a recovery path
- identify an action or prerequisite
- change an expected origin or provider URL
- determine whether an action is complete
- authorize browser automation
- change a workflow version or `VerifiedAt` date

A translation-only change therefore does not alter workflow semantics or provider verification metadata.

Existing catalog strings that are intended for display should be migrated to resource keys while the provider set is small. Machine-readable completion rules remain canonical and should not require natural-language comparison.

See [Localization and Multilingual GUI](LOCALIZATION.md).

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

Status values remain language-neutral. The UI maps them to reviewed localized labels and descriptions without persisting the translated text.

## Completion Rules

An account is **fully reviewed** when every applicable required action is completed and every excluded action is explicitly documented as truly not applicable.

An account is **not fully secured** when:

- a required action is blocked
- a required action failed
- a relevant control was marked unavailable with unresolved risk
- the user accepted an unresolved risk
- access to the account could not be restored

These distinctions must remain visible in the UI and exported recovery report in every supported language. Missing translation resources fall back to complete English warnings rather than hiding a condition.

## Automation Levels

Actions declare one of these support levels:

- `NONE`: instructions and tracking only
- `NAVIGATION`: open the correct recovery location
- `ASSISTED`: visible browser assistance with user interaction
- `AUTOMATED`: a bounded action can be executed automatically with explicit authorization

The support level describes technical capability, not permission to bypass provider security mechanisms.

unpwn does not bypass CAPTCHA, MFA, identity verification, or account-ownership checks.

Localized button labels and instructions never identify or authorize the automation operation; the structured action and explicit confirmation do.

## Contribution Model

Recovery workflows are maintained in the unpwn repository.

New workflows and updates are submitted through pull requests and must include:

- a clear description of the provider and supported account type
- documented recovery actions and URLs
- stable localization keys for display guidance
- tests for machine-readable definitions or code
- localization key coverage where user-facing resources are introduced
- a verification date
- no embedded secrets or personal account data

unpwn does not download or execute third-party provider plugins or language packs at runtime.

## Repository Workflow Definitions

Repository-controlled workflow definitions are represented in code as immutable `RecoveryWorkflowDefinition` records until the on-disk workflow package format is introduced. Each definition includes provider metadata, supported account type, workflow version, verification date, official recovery locations, expected origins, and recovery actions.

The validation boundary for these definitions is `RecoveryWorkflowValidator`. It rejects missing required metadata, a verification date later than the supplied or current validation date, duplicate location or action identifiers, invalid HTTPS locations or origins, missing or duplicate recovery paths, missing prerequisite targets, prerequisite cycles, required actions without non-empty completion criteria, and claims of fully automated recovery. Fully automated recovery is intentionally disallowed at this stage because repository workflows may guide or navigate, but they must not bypass user-visible security decisions.

Provider contract scenarios additionally prove that every expected action supports the scenario path, all prerequisites are present earlier on that path, expectations match the workflow definition, and a scenario claiming full security includes every required action for that path.

Localization validation separately verifies that referenced display keys exist in the complete English resources and that shipped translations preserve required keys and formatting placeholders. It does not replace structural, semantic, or provider contract validation.

The shipped provider catalog validates all repository workflows through `RepositoryWorkflowCatalog.ValidateAll()`. Provider workflow changes should add or update catalog entries and regression tests that prove the definition remains structurally and semantically safe.

## Generic manual workflow for unsupported providers

Accounts without a matching reviewed provider definition use the repository-controlled
`generic/manual-account-recovery` workflow. The UI labels it as general, non-provider-specific
guidance; it must never be presented with the same confidence as a reviewed provider workflow.
Its stable authenticated-change, password-reset, and manual-recovery paths keep unsupported accounts
inside the normal dependency plan, persisted action state, credential handoff, risk model, and
completion review.

The generic definition contains no provider recovery locations or trusted-origin metadata. Arbitrary
provider IDs and names therefore cannot expand a navigation allowlist. Only an imported absolute HTTPS
account URL may be passed to the existing `/.well-known/change-password` discovery boundary for the
authenticated password-change action. A discovered destination is shown for a separate user decision
before opening; an unsafe URL, unexpected redirect, network failure, or missing URL falls back to the
manual instructions without guessing a destination.

Provider-dependent session, MFA/passkey, recovery-option, application, device, token, and key reviews
are required checklist actions, but the guidance tells users to act only on controls that are visibly
available and relevant. A genuinely absent control uses the existing confirmed truly-not-applicable
transition and a non-secret reason. A relevant control that cannot be completed remains an explicit
unresolved risk. Browser navigation never completes an action, and every completion still requires
explicit acknowledgement of repository-controlled criteria.

## GitHub consumer account workflow

The repository workflow for GitHub.com consumer accounts guides authenticated password changes,
password resets through an already secured email account, and GitHub's provider-controlled recovery
options when 2FA credentials are unavailable. Its reviewed navigation boundaries use only the exact
`github.com` and `docs.github.com` origins for password and authentication settings, active sessions,
verified email addresses, authorized applications, personal access tokens, SSH and signing keys, and
official recovery guidance. These locations were reviewed on 2026-08-10.

Review references:

- [Updating your GitHub access credentials](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/updating-your-github-access-credentials)
- [Preventing unauthorized access](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/preventing-unauthorized-access)
- [Configuring two-factor authentication recovery methods](https://docs.github.com/en/authentication/securing-your-account-with-two-factor-authentication-2fa/configuring-two-factor-authentication-recovery-methods)
- [Recovering your account if you lose your 2FA credentials](https://docs.github.com/en/authentication/securing-your-account-with-two-factor-authentication-2fa/recovering-your-account-if-you-lose-your-2fa-credentials)
- [Reviewing authorized OAuth apps](https://docs.github.com/en/apps/oauth-apps/using-oauth-apps/reviewing-your-authorized-oauth-apps)
- [Managing personal access tokens](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens)

Personal access tokens and SSH or signing keys are separate required critical actions. Their secret
values never enter unpwn. Revocation can interrupt command-line access, integrations, deployments, or
automation, so the user performs each provider action visibly and records any credential that cannot
be safely replaced as unresolved risk.

The workflow covers the user's GitHub.com account. It does not perform organization-wide or
enterprise administration, and it does not assume GitHub Support can restore an account when all
documented 2FA recovery methods are unavailable.

## Google consumer account workflow

The repository workflow for consumer Google accounts uses only two reviewed navigation boundaries:

- `https://myaccount.google.com/security` for authenticated password, device and session, MFA and passkey, recovery-method, and connected-application review;
- `https://accounts.google.com/signin/recovery` for password reset and manual account recovery.

Both locations were reviewed against Google's official compromised-account, device-access,
2-Step Verification, recovery-information, and linked-application guidance on 2026-08-10. The
workflow declares the exact `myaccount.google.com` and `accounts.google.com` origins rather than
trusting arbitrary Google subdomains.

Review references:

- [Secure a hacked or compromised Google Account](https://support.google.com/accounts/answer/6294825)
- [See devices with account access](https://support.google.com/accounts/answer/3067630)
- [Protecting your personal info with 2-Step Verification](https://support.google.com/accounts/answer/10956730)
- [Update your recovery info](https://support.google.com/accounts/answer/17299765)
- [Manage your linked apps](https://support.google.com/accounts/answer/16363505)

A password reset may depend on access to a recovery email account or another recovery channel. The
provider scenarios therefore distinguish a reset through an already secured channel from one blocked
by an unsecured recovery email dependency. That cross-account dependency remains part of the account
inventory and recovery plan; the provider workflow does not assume that receiving a message proves
the channel is under the user's control.

The workflow covers consumer accounts. Organization-managed Google Workspace policies may remove or
alter available controls, in which case the user must keep the limitation visible as blocked work or
unresolved risk and follow their administrator's reviewed process.

## Microsoft personal account workflow

The repository workflow for personal Microsoft accounts uses only reviewed Microsoft account
locations on the exact `account.microsoft.com` and `account.live.com` origins. It covers the
authenticated security dashboard, password reset, the provider-reviewed account recovery form,
recent sign-in activity, advanced security options, registered devices, and privacy or connected-app
controls. The locations and official guidance below were reviewed on 2026-08-10.

Review references:

- [Reset a forgotten Microsoft account password](https://support.microsoft.com/en-us/accounts-billing/manage/reset-a-forgotten-microsoft-account-password)
- [Help with the Microsoft account recovery form](https://support.microsoft.com/en-US/accounts-billing/manage/help-with-the-microsoft-account-recovery-form)
- [Check recent sign-in activity](https://support.microsoft.com/en-us/account-billing/check-the-recent-sign-in-activity-for-your-microsoft-account-5b3cfb8e-70b3-2bd6-9a56-a50177863357)
- [Use two-step verification with a Microsoft account](https://support.microsoft.com/en-US/accounts-billing/security/how-to-use-two-step-verification-with-your-microsoft-account)
- [Get a Microsoft account recovery code](https://support.microsoft.com/en-US/accounts-billing/manage/how-to-get-a-microsoft-account-recovery-code)
- [Add a trusted device](https://support.microsoft.com/en-us/accounts-billing/manage/add-a-trusted-device-to-your-microsoft-account)

Password reset proceeds only through a verification method the user already controls. If every
offered email address, telephone number, authenticator, or other method is unavailable or untrusted,
the reset remains visibly blocked and the user can move to Microsoft's official recovery form.
Submitting that form does not prove ownership or restored access: provider review may remain pending
or be denied.

This workflow explicitly supports personal Microsoft accounts only. Work or school accounts are
organization-managed and can expose different controls and recovery policies; users must follow the
organization's reviewed process or contact its administrator instead of treating the personal-account
workflow as applicable.
