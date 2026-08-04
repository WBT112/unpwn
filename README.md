# unpwn

**unpwn** is an open-source emergency account recovery assistant that helps users regain control of their digital identity after a suspected compromise.

unpwn is **not an antivirus** and does not detect malware. It focuses on the recovery workflow after incidents such as infostealers, phishing, or session hijacking.

## Goal

When a user asks:

> "What do I need to do now to secure my accounts again?"

unpwn provides a structured recovery process:

- prioritize critical accounts
- guide password changes
- invalidate active sessions
- verify MFA and recovery settings
- track progress across many accounts
- export recovered credentials into established password managers

## Principles

- Open source
- Local-first
- No cloud dependency in MVP
- No password collection service
- Transparent security model
- Human in the loop where required

## Recovery Vault

unpwn uses an encrypted local **Recovery Vault** to maintain a recovery session over days or weeks.

The vault stores recovery state, generated credentials, tasks, and export information. It is a recovery workspace, not intended to replace a dedicated password manager.

## Status

Early design and architecture phase.

See:

- [Project Vision](docs/VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
