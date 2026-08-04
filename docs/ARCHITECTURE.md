# unpwn Architecture

## Overview

unpwn is designed as a modular local-first recovery platform.

The architecture separates recovery logic, storage, automation, imports, exports, and service-specific providers.

## Components

```
Unpwn.Core
 ├── Recovery Engine
 ├── Workflow State Machine
 ├── Prioritization Logic
 └── Recovery Action Model

Unpwn.Vault
 ├── Encrypted Recovery Vault
 ├── Credential Storage
 └── Recovery History

Unpwn.Storage
 └── Local encrypted persistence

Unpwn.Automation
 ├── Web Standard Actions
 ├── Provider Automation
 ├── Playwright Fallback
 └── Manual Guidance

Unpwn.Import
 ├── Browser import
 └── Generic import mapping

Unpwn.Export
 ├── Bitwarden
 ├── KeePass
 ├── 1Password
 └── CSV

Unpwn.Providers
 ├── Google
 ├── Microsoft
 ├── GitHub
 └── Other services
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

This allows unpwn to decide which actions can be automated and which require explicit user interaction.

## Provider System

Services are implemented through providers instead of hard-coded account logic.

Example:

```
IRecoveryProvider
    - GetRecoveryActions()
    - ExecuteRecoveryAction()
```

Providers define service-specific recovery workflows for services such as Google, Microsoft, and GitHub.

## Automation

unpwn does not depend on browser password managers or vendor-specific ecosystems.

Automation layers:

```
Unpwn.Automation

├── Standard Web Actions
│     └── /.well-known/change-password (optional)
│
├── Provider Automation
│
├── Playwright Fallback
│
└── Manual Guidance
```

Browser automation uses visible browser sessions where possible.

Security boundaries:

- no CAPTCHA bypass
- no MFA bypass
- no hidden account changes
- user confirmation for sensitive steps

## Security Assumption

unpwn should be executed on a trusted device.

If the executing system is compromised, local software cannot reliably protect new credentials or user input.
