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

## Recovery Vault

unpwn uses an encrypted local **Recovery Vault** to maintain a recovery session over days or weeks.

The vault uses a user-defined vault password, Argon2id key derivation, and AES-256-GCM authenticated encryption. It stores recovery state, generated credentials, tasks, and export information. It is a recovery workspace, not a replacement for a dedicated password manager.

## Automation Philosophy

unpwn uses automation as assistance, not as an uncontrolled account bot.

Possible assistance layers include:

- official APIs where available
- supported web standards for recovery-location discovery
- visible browser assistance
- manual guidance for sensitive steps

Critical actions remain under user control.

## Contributing Recovery Workflows

Recovery workflows are maintained in this repository. New or updated workflows are contributed through normal pull requests, reviewed, tested, and shipped with unpwn releases. unpwn does not download or execute third-party provider plugins at runtime.

## License

unpwn is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.

See `LICENSE` for details.

## Status

Early design and architecture phase.

See:

- [Project Vision](docs/VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Recovery Workflows](docs/RECOVERY_WORKFLOWS.md)
- [Data Model](docs/DATA_MODEL.md)
- [Vault Security](docs/VAULT_SECURITY.md)
- [Threat Model](docs/THREAT_MODEL.md)
- [Roadmap](docs/ROADMAP.md)
- [Contributing](CONTRIBUTING.md)
