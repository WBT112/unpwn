# unpwn Architecture

## Overview

unpwn is designed as a modular, local-first account recovery orchestration platform.

The architecture separates recovery planning, workflow execution, storage, vault security, automation assistance, imports, exports, and service-specific recovery workflows.

The architecture is platform-neutral. Windows is the initial target platform, but core components must not depend on Windows-specific functionality. Future support for macOS and Linux should be possible without redesigning the recovery engine.

## Technology Stack

The initial implementation uses:

- C#
- .NET LTS
- Avalonia UI
- SQLite
- Argon2id for vault-password key derivation
- AES-256-GCM for authenticated encryption

The application follows a modular architecture with a platform-independent core.

Platform-specific functionality must remain isolated from recovery logic, workflow logic, provider implementations, and portable vault access.

## Recovery Workflows

Recovery workflows are a first-class concept in unpwn.

A workflow describes the steps required to restore control over a digital account after a suspected security incident.

A workflow may include:

- identifying the account
- assessing priority and risk
- selecting an authenticated password change, password reset, or manual recovery path
- changing credentials
- invalidating sessions
- reviewing MFA settings
- checking recovery options
- reviewing connected applications, tokens, or trusted devices
- documenting completion

Providers define service-specific workflows, while the Recovery Engine manages execution, dependencies, state tracking, prioritization, and user interaction.

See [Recovery Workflows](RECOVERY_WORKFLOWS.md).

## Components

```
Unpwn.App
 └── Avalonia Desktop Application

Unpwn.Application
 └── Application Services and Use Cases

Unpwn.Core
 ├── Recovery Engine
 ├── Workflow State Machine
 ├── Risk Prioritization Logic
 ├── Dependency Planning
 ├── Progress Calculation
 └── Recovery Action Model

Unpwn.Infrastructure
 ├── SQLite Storage
 ├── Cryptography
 └── OS Integration

Unpwn.Vault
 ├── Encrypted Recovery Vault
 ├── Credential Storage
 ├── Key Management
 └── Recovery History

Unpwn.Automation
 ├── Recovery Location Discovery
 ├── Browser Assistance (Playwright)
 └── Manual Guidance

Unpwn.Import
 ├── Browser/password manager imports
 └── Generic import mapping

Unpwn.Export
 ├── Bitwarden
 ├── KeePass
 ├── 1Password
 └── CSV

Unpwn.Providers
 ├── GoogleRecoveryProvider
 ├── MicrosoftRecoveryProvider
 ├── GitHubRecoveryProvider
 └── Other service providers
```

## Recovery Vault

The Recovery Vault is an encrypted local workspace used during the complete recovery process.

It may exist for days or weeks because account recovery is not always completed in one session.

The vault contains:

- accounts and account dependencies
- recovery actions and workflow state
- generated new credentials
- export information
- user notes
- append-only audit events

The vault is a recovery workspace, not a replacement for a dedicated password manager.

### Key hierarchy

The primary protection mechanism is a user-defined vault password.

1. Argon2id derives a key-encryption key from the vault password and a random salt.
2. A cryptographically random vault data key encrypts sensitive records.
3. The derived key encrypts the vault data key.
4. Changing the vault password re-wraps the data key without requiring every record to be re-encrypted.

Sensitive records use AES-256-GCM with a unique nonce for every encryption operation. Record identifiers, record types, and schema versions should be authenticated as associated data.

SQLite is the storage container. It must not be treated as the sole confidentiality boundary.

Operating-system facilities such as Windows Credential Manager, macOS Keychain, or Linux Secret Service may later provide optional unlock convenience. They must not be required for portable vault access.

Old passwords are not stored. Generated new credentials may be retained, encrypted, until they are exported or deliberately deleted by the user.

See [Vault Security](VAULT_SECURITY.md).

## Recovery Actions

Recovery steps are modeled as actions with security and workflow metadata.

Example:

```
RecoveryAction

Type:
  CHANGE_PASSWORD

RecoveryPath:
  AUTHENTICATED_CHANGE | PASSWORD_RESET | MANUAL_RECOVERY

Importance:
  CRITICAL | IMPORTANT | ROUTINE

Status:
  OPEN | IN_PROGRESS | BLOCKED | NEEDS_USER_ACTION | COMPLETED | FAILED

AutomationSupport:
  NONE | NAVIGATION | ASSISTED | AUTOMATED

UserConfirmation:
  REQUIRED
```

Actions may depend on other actions or accounts. For example, a password reset may depend on first securing the primary email account.

## Data and Progress Model

The central domain entities are:

- `RecoverySession`
- `Account`
- `AccountDependency`
- `RecoveryWorkflowDefinition`
- `RecoveryActionInstance`
- `CredentialEntry`
- `AuditEvent`

unpwn reports more than one progress signal to avoid presenting a misleading single percentage:

- critical accounts secured
- accounts fully reviewed
- weighted required actions completed
- blocked actions and unresolved risks

See [Data Model](DATA_MODEL.md).

## Provider System

Services are implemented through providers instead of hard-coded account logic.

Providers define service-specific recovery workflows. They answer:

> What needs to happen for this service?

They do not own vault cryptography or generic browser automation.

Example:

```
GoogleRecoveryProvider
 ├── ChangeOrResetPassword
 ├── RevokeSessions
 ├── CheckMFA
 └── ReviewRecoveryOptions
```

Provider and workflow contributions are made through normal pull requests to this repository. They are reviewed, tested, and shipped with unpwn releases. unpwn does not download or execute third-party provider plugins at runtime.

## Automation Assistance

unpwn does not depend on browser password managers or vendor-specific ecosystems.

Automation layers:

```
Unpwn.Automation

├── Recovery Location Discovery
│     └── /.well-known/change-password (optional)
│
├── Browser Assistance
│     └── Playwright-based visible workflows
│
└── Manual Guidance
```

The `/.well-known/change-password` standard is used only to discover a suitable password change location. It does not provide an automation protocol.

Browser assistance helps users complete workflows when appropriate. It is not intended to be an autonomous account recovery bot.

Automation priority:

1. Official APIs where appropriate and safely available
2. Supported web standards for location discovery
3. Visible browser assistance for supported workflows
4. Manual guidance

Security boundaries:

- no CAPTCHA bypass
- no MFA bypass
- no hidden account changes
- no use of recovery mechanisms to gain access to accounts the user does not own
- user confirmation for sensitive steps

## Security Assumption

unpwn should be executed on a trusted device.

If the executing system is compromised, local software cannot reliably protect new credentials, browser sessions, screen contents, or user input.
