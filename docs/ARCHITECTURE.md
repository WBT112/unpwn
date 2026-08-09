# unpwn Architecture

## Goals

unpwn is a modular, local-first desktop application for account-recovery orchestration. The architecture keeps the recovery domain independent from UI, storage, operating-system APIs, localization, providers, and browser assistance.

Windows is the first target platform. Core recovery logic must remain portable to macOS and Linux.

## Technology

- C# / .NET 10 (LTS)
- Avalonia UI
- SQLite
- Argon2id and AES-256-GCM in the Recovery Vault
- .NET resource files for presentation localization
- Playwright for bounded browser assistance where implemented

Detailed cryptographic rules live in [Vault Security](VAULT_SECURITY.md); localization rules live in [Localization](LOCALIZATION.md).

## Projects

```text
Unpwn.App
  Desktop UI, MVVM presentation, navigation, localization, composition root

Unpwn.Application
  Application services and use cases

Unpwn.Core
  Recovery domain, state machines, priorities, dependencies, progress

Unpwn.Infrastructure
  General infrastructure and OS integration

Unpwn.Vault
  Encrypted Recovery Vault, keys, records, generated credentials

Unpwn.Automation
  Recovery-location discovery and bounded browser assistance

Unpwn.Import
  Platform-neutral import parsing and mapping

Unpwn.Export
  Credential export formats

Unpwn.Providers
  Repository-controlled provider workflow definitions
```

## Dependency direction

Dependencies point inward:

```text
Unpwn.App
 ├── Infrastructure ─┐
 ├── Vault ──────────┤
 ├── Automation ─────┤
 ├── Import ─────────┼──> Unpwn.Application ──> Unpwn.Core
 ├── Export ─────────┤
 └── Providers ──────┘
```

`Unpwn.Core` must not depend on Avalonia, SQLite, Playwright, operating-system APIs, localization resources, or provider-specific infrastructure. Architecture tests enforce the project-reference boundary.

`Unpwn.App` is the composition root. View models and UI-facing services use constructor injection; code-behind is limited to Avalonia-specific bridging such as native file pickers, dialog results, and focus hooks. See [UI Foundation](UI_FOUNDATION.md).

## Canonical boundaries

### Recovery domain

Canonical accounts, dependencies, actions, statuses, progress, and audit semantics live in the platform-neutral domain. Presentation code must not create a parallel recovery state machine.

See [Data Model](DATA_MODEL.md) and [Account Recovery Execution](ACCOUNT_RECOVERY_EXECUTION.md).

### Recovery workflows

Providers describe what must be done for a service. Generic orchestration owns execution state, ordering, dependencies, and progress. Provider code does not own vault cryptography, localization, or generic browser automation.

See [Recovery Workflows](RECOVERY_WORKFLOWS.md).

### Persistence and vault

Sensitive recovery state is stored in the encrypted local Recovery Vault. Logically related workspace changes are persisted atomically where required. Storage failure must not be represented as saved work.

See [Vault Security](VAULT_SECURITY.md) and [Workspace Persistence](WORKSPACE_PERSISTENCE.md).

### Localization

Localization is a presentation concern. Workflow IDs, action IDs, error codes, URLs, record identifiers, serialized values, and security decisions remain language-neutral.

See [Localization](LOCALIZATION.md).

### Automation

Automation assists bounded recovery tasks. Opening or returning from an external provider page never proves that an action succeeded. CAPTCHA, MFA, identity verification, and ownership checks are not bypassed.

See [Recovery Location Discovery](RECOVERY_LOCATION_DISCOVERY.md) and [Recovery Workflows](RECOVERY_WORKFLOWS.md).

## Documentation ownership

Detailed rules should live in the specialized documents listed in the [documentation index](README.md). This architecture document defines module boundaries and dependency direction rather than repeating the complete vault, workflow, localization, or testing specifications.
