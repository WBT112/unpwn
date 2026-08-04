# unpwn

**unpwn** is an open-source emergency account recovery assistant that helps users regain control of their digital identity after a suspected compromise.

unpwn is **not an antivirus** and does not detect malware. It focuses on recovery orchestration after incidents such as infostealers, phishing, or session hijacking.

## Goal

When a user asks:

> "What do I need to do now to secure my accounts again?"

unpwn provides a structured recovery process:

- discover and organize affected accounts
- prioritize critical identities
- guide recovery actions step by step
- invalidate active sessions where possible
- verify MFA and recovery settings
- track progress across many accounts
- maintain an audit history of completed recovery work
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

## Recovery Vault

unpwn uses an encrypted local **Recovery Vault** to maintain a recovery session over days or weeks.

The vault stores recovery state, generated credentials, tasks, and export information. It is a recovery workspace, not intended to replace a dedicated password manager.

## Automation Philosophy

unpwn uses automation as assistance, not as an uncontrolled account bot.

Possible assistance layers include:

- official APIs where available
- supported web standards
- browser assistance
- manual guidance for sensitive steps

Critical actions remain under user control.

## License

unpwn is licensed under the **GNU Affero General Public License v3.0 (AGPL-3.0)**.

See `LICENSE` for details.

## Status

Early design and architecture phase.

See:

- [Project Vision](docs/VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Roadmap](docs/ROADMAP.md)
