# unpwn Architecture

## Overview

unpwn is designed as a modular local-first recovery orchestration platform.

The architecture separates recovery planning, workflow execution, storage, automation assistance, imports, exports, and service-specific providers.

## Components

```
Unpwn.Core
 ├── Recovery Engine
 ├── Workflow State Machine
 ├── Risk Prioritization Logic
 ├── Recovery Planning
 └── Recovery Action Model

Unpwn.Vault
 ├── Encrypted Recovery Vault
 ├── Credential Storage
 └── Recovery History

Unpwn.Storage
 └── Local encrypted persistence

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

- accounts
- recovery actions
- workflow state
- generated credentials
- export information
- user notes

The vault is a recovery workspace, not a replacement for a dedicated password manager.

## Recovery Actions

Recovery steps are modeled as actions with security metadata.

Example:

```
RecoveryAction

Type:
  CHANGE_PASSWORD

Risk:
  HIGH

AutomationSupport:
  SUPPORTED

UserConfirmation:
  REQUIRED
```

This allows unpwn to decide which actions can be assisted automatically and which require explicit user interaction.

## Provider System

Services are implemented through providers instead of hard-coded account logic.

Providers define service-specific recovery workflows.

They answer:

"What needs to happen for this service?"

They do not define how browser automation works.

Example:

```
GoogleRecoveryProvider
 ├── ChangePassword
 ├── RevokeSessions
 ├── CheckMFA
 └── ReviewRecoveryOptions
```

## Automation Assistance

unpwn does not depend on browser password managers or vendor-specific ecosystems.

Automation layers:

```
Unpwn.Automation

├── Recovery Location Discovery
│     └── /.well-known/change-password (optional)
│
├── Browser Assistance
│     └── Playwright-based assistance
│
└── Manual Guidance
```

The `/.well-known/change-password` standard is used only to discover a suitable password change location. It does not provide an automation protocol.

Browser assistance helps users complete workflows when appropriate. It is not intended to be an autonomous account recovery bot.

Automation priority:

1. Official APIs where available
2. Supported web standards for discovery
3. Browser assistance for supported workflows
4. Manual guidance

Security boundaries:

- no CAPTCHA bypass
- no MFA bypass
- no hidden account changes
- user confirmation for sensitive steps

## Security Assumption

unpwn should be executed on a trusted device.

If the executing system is compromised, local software cannot reliably protect new credentials or user input.
