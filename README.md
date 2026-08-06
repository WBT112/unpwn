# unpwn

**unpwn** is an open-source emergency account recovery assistant that helps users regain control of their digital identity after a suspected compromise.

unpwn is **not an antivirus** and does not detect malware. It focuses on account recovery orchestration after incidents such as infostealers, phishing, or session hijacking.

## Goal

When a user asks:

> "What do I need to do now to secure my accounts again?"

unpwn provides a structured recovery process:

- discover and organize affected accounts
- prioritize critical identities
- guide recovery actions step by step
- distinguish between authenticated password changes, password resets, and manual recovery
- invalidate active sessions where possible
- verify MFA and recovery settings
- track dependencies between accounts, such as password resets that rely on a primary email account
- maintain progress and an audit history across many accounts
- export recovered credentials into established password managers

## What unpwn is not

unpwn does not replace a password manager.

A password manager answers:

> "Which credentials do I have?"

unpwn answers:

> "What do I need to do after a security incident to recover my digital identity?"

## Principles

- Open source
- Local-first
- No cloud dependency
- No password collection service
- Transparent security model
- Human in the loop where required
- Platform-neutral core with Windows as the first target
- Multilingual presentation without localized domain or vault data

## Recovery Vault

unpwn uses an encrypted local **Recovery Vault** to maintain a recovery session over days or weeks.

The vault uses a user-defined vault password, Argon2id key derivation, and AES-256-GCM authenticated encryption. It stores recovery state, generated credentials, tasks, and export information. It is a recovery workspace, not a replacement for a dedicated password manager.

Every new or resumed flow begins with an explicit trusted-device decision. Declining or expressing uncertainty ends before a vault is created or unlocked. Explicit and inactivity locking preserve a conservative encrypted wizard-resume point, and recent-vault metadata contains only local file references rather than recovery content or passwords.

See [Trusted Device and Vault Entry](docs/VAULT_ENTRY.md) and [Vault Security](docs/VAULT_SECURITY.md).

## Guided Recovery Wizard

The desktop application guides users through one resumable recovery process instead of exposing unrelated feature screens.

The wizard begins with a trusted-device gate before a vault is created or unlocked. It then coordinates incident intake, account inventory, identity and recovery dependencies, the recommended recovery plan, guided account actions, generated-credential export, completion review, and the final report.

Wizard steps, lifecycle states, and recommendation reasons use stable language-neutral codes. Display text is localized only in the presentation layer. Opening an external provider page or returning to the application never marks an action complete automatically.

See [Guided Recovery Wizard](docs/RECOVERY_WIZARD.md).

## Automation Philosophy

unpwn uses automation as assistance, not as an uncontrolled account bot.

Possible assistance layers include:

- official APIs where available
- supported web standards for recovery-location discovery
- visible browser assistance
- manual guidance for sensitive steps

Critical actions remain under user control.

## Multilingual GUI

The GUI is designed so additional languages can be added without changing recovery logic, workflow semantics, encrypted vault data, or machine-readable formats.

English is the complete source and fallback language. User-facing labels, warnings, validation messages, workflow guidance, accessibility text, and formatted values are obtained through the presentation localization boundary. Canonical status values, workflow identifiers, audit event types, URLs, error codes, and persisted records remain language-neutral.

Translation resources are repository-controlled and shipped with releases. Runtime machine translation and downloaded language packs are not used for security-critical content.

See [Localization and Multilingual GUI](docs/LOCALIZATION.md).

## Contributing Recovery Workflows

Recovery workflows are maintained in this repository. New or updated workflows are contributed through normal pull requests, reviewed, tested, and shipped with unpwn releases. unpwn does not download or execute third-party provider plugins at runtime.

## License

unpwn is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.

See `LICENSE` for details.

## Status

The initial .NET 10 and Avalonia desktop solution is scaffolded. Domain and recovery functionality are still under active development.

## Building

Install the .NET 10 SDK, then run:

```shell
dotnet restore unpwn.slnx
dotnet build unpwn.slnx --no-restore
dotnet test unpwn.slnx --no-build
dotnet format unpwn.slnx --no-restore --verify-no-changes --severity info
```

Tests can produce local Cobertura coverage output with:

```shell
dotnet test unpwn.slnx --collect:"XPlat Code Coverage" --results-directory artifacts/test-results
```

GitHub Actions performs full Release builds and tests on Windows and Linux. Formatting/analyzer verification, Cobertura coverage collection, secret-marker scanning, and the normal successful artifact upload run on Linux; failed Windows test artifacts are uploaded only when needed. Retained test artifacts use the configured short retention period.

Start the desktop shell with:

```shell
dotnet run --project src/Unpwn.App/Unpwn.App.csproj
```

See:

- [Project Vision](docs/VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Application Shell and UI Foundation](docs/UI_FOUNDATION.md)
- [Guided Recovery Wizard](docs/RECOVERY_WIZARD.md)
- [Trusted Device and Vault Entry](docs/VAULT_ENTRY.md)
- [Localization and Multilingual GUI](docs/LOCALIZATION.md)
- [CSV Import](docs/IMPORT.md)
- [Recovery Workflows](docs/RECOVERY_WORKFLOWS.md)
- [Data Model](docs/DATA_MODEL.md)
- [Vault Security](docs/VAULT_SECURITY.md)
- [Threat Model](docs/THREAT_MODEL.md)
- [Testing Strategy](docs/TESTING.md)
- [Roadmap](docs/ROADMAP.md)
- [Contributing](CONTRIBUTING.md)
