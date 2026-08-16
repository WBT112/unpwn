# unpwn Documentation

This directory contains the canonical project documentation. Each topic has one primary home; other documents link to it instead of maintaining parallel specifications. GitHub issues define bounded implementation work, not durable product or architecture truth.

## Current product flow

The normal journey is one integrated workspace:

1. confirm a reasonably trusted device;
2. create or unlock the encrypted Recovery Vault and create or resume a session;
3. import accounts, then explicitly review their simple categories;
4. follow the automatic `Email → Critical → Unknown → NonCritical` queue;
5. perform provider work in the isolated Recovery Browser while unpwn shows one instruction and checklist;
6. handle generated credentials and explicitly review unresolved work before finishing.

Only canonical user confirmations advance recovery state. Browser navigation, redirects, form state, credential insertion, closing a view, and application restart never prove success. The [User Guide](USER_GUIDE.md) is the canonical user-facing walkthrough; the documents below own the corresponding technical contracts.

## Documentation ownership

| Concern | Canonical document |
| --- | --- |
| Product purpose and non-goals | [Vision](VISION.md) |
| User-visible journey and terminology | [User Guide](USER_GUIDE.md) |
| Module boundaries and dependency direction | [Architecture](ARCHITECTURE.md) |
| Canonical entities, progress, and completion semantics | [Data Model](DATA_MODEL.md) |
| Threats, mitigations, and residual risk | [Threat Model](THREAT_MODEL.md) |
| Test layers, CI, fixtures, and release verification | [Testing Strategy](TESTING.md) |
| Blocking security-analysis gates and exceptions | [Security CI Gates](SECURITY_GATES.md) |

## For users

- [User Guide](USER_GUIDE.md) — short recovery walkthrough and important limits
- [Security Policy](../SECURITY.md) — project status, limitations, and vulnerability reporting

## Product

- [Vision](VISION.md) — user problem, product scope, and principles
- [Roadmap](ROADMAP.md) — current development and release-readiness direction

## Architecture and UI

- [Architecture](ARCHITECTURE.md) — modules and dependency direction
- [Data Model](DATA_MODEL.md) — canonical recovery domain and progress semantics
- [Application Shell and UI Foundation](UI_FOUNDATION.md) — MVVM shell, accessibility, and presentation rules
- [Desktop Accessibility Acceptance](ACCESSIBILITY_ACCEPTANCE.md) — automated baseline and Windows/Ubuntu release checklist
- [Localization](LOCALIZATION.md) — language boundary and resource rules

## Recovery flow

- [Integrated Recovery Flow](RECOVERY_WIZARD.md) — canonical next-task state and resume rules; there is no separate wizard UI
- [Recovery Session and Overview](RECOVERY_SESSION_DASHBOARD.md) — session input, overview projections, lifecycle, and completion
- [Account Inventory and Recovery Queue](ACCOUNT_INVENTORY.md) — account categories, local suggestions, and ordering
- [CSV Import](IMPORT.md) — import parsing, password exclusion, and duplicate handling
- [Recovery Workflows](RECOVERY_WORKFLOWS.md) — provider workflow semantics and validation
- [Account Recovery Execution](ACCOUNT_RECOVERY_EXECUTION.md) — canonical per-account/action execution state
- [Recovery Location Discovery](RECOVERY_LOCATION_DISCOVERY.md) — safe provider navigation handoff
- [Recovery Browser Security Boundary](RECOVERY_BROWSER.md) — embedded provider workspace, browser-session lifecycle, origin policy, and bounded credential assistance

## Vault and credentials

- [Trusted Device and Vault Entry](VAULT_ENTRY.md) — trusted-device gate and vault lifecycle UI
- [Vault Security](VAULT_SECURITY.md) — cryptographic and secret-handling design
- [Workspace Persistence](WORKSPACE_PERSISTENCE.md) — atomic persistence and interrupted work
- [Generated Credentials](GENERATED_CREDENTIALS.md) — generation, encrypted lifecycle, in-context handoff, and export

## Engineering

- [Threat Model](THREAT_MODEL.md) — threats, mitigations, and residual risks
- [Testing Strategy](TESTING.md) — authoritative testing and CI rules
- [Security CI Gates](SECURITY_GATES.md) — CodeQL, analyzer, dependency, native-boundary, security-regression, and exception policy
- [Contributing](../CONTRIBUTING.md) — contribution workflow
- [Coding Agent Instructions](../AGENTS.md) — concise repository rules for automated contributors

Detailed implementation tasks and acceptance criteria live in GitHub issues rather than being duplicated in documentation.
