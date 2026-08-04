# unpwn Architecture

## Overview

unpwn is designed as a modular local-first recovery platform.

The architecture separates recovery logic, storage, automation, imports, exports, and service-specific providers.

## Components

```
Unpwn.Core
 ├── Recovery Engine
 ├── Workflow State Machine
 └── Prioritization Logic

Unpwn.Vault
 ├── Encrypted Recovery Vault
 ├── Credential Storage
 └── Recovery History

Unpwn.Storage
 └── Local encrypted persistence

Unpwn.Automation
 └── Playwright browser automation

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
- recovery tasks
- workflow state
- generated credentials
- export information
- user notes

The vault is not intended to replace a password manager.

## Provider System

Services are implemented through providers instead of hard-coded account logic.

Example:

```
IRecoveryProvider
    - GetRecoveryTasks()
    - ExecuteRecoveryStep()
```

This allows independent support for Google, Microsoft, GitHub, and others.

## Automation

Browser automation uses Playwright with a visible browser.

The goal is maximum assistance while keeping users aware of actions.

Security boundaries:

- no CAPTCHA bypass
- no MFA bypass
- no hidden account changes
- user confirmation for sensitive steps

## Security Assumption

unpwn should be executed on a trusted device.

If the executing system is compromised, local software cannot reliably protect new credentials or user input.
