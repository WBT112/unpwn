# unpwn Documentation

This directory contains the detailed project documentation. Each topic should have one canonical home; other documents should link to it instead of repeating the same rules.

## For users

- [User Guide](USER_GUIDE.md) — short recovery walkthrough and important limits
- [Security Policy](../SECURITY.md) — project status, limitations, and vulnerability reporting

## Product

- [Vision](VISION.md) — user problem, product scope, and principles
- [Roadmap](ROADMAP.md) — current development direction

## Architecture and UI

- [Architecture](ARCHITECTURE.md) — modules and dependency direction
- [Data Model](DATA_MODEL.md) — canonical recovery domain and progress semantics
- [Application Shell and UI Foundation](UI_FOUNDATION.md) — MVVM shell, accessibility, and presentation rules
- [Desktop Accessibility Acceptance](ACCESSIBILITY_ACCEPTANCE.md) — automated baseline and Windows/Ubuntu release checklist
- [Localization](LOCALIZATION.md) — language boundary and resource rules

## Recovery flow

- [Guided Recovery Wizard](RECOVERY_WIZARD.md) — end-to-end wizard state and resume rules
- [Recovery Session and Dashboard](RECOVERY_SESSION_DASHBOARD.md) — incident intake, dashboard, and session lifecycle
- [Account Inventory and Recovery Planning](ACCOUNT_INVENTORY.md) — accounts, roles, dependencies, and ordering
- [CSV Import](IMPORT.md) — import parsing, password exclusion, and duplicate handling
- [Recovery Workflows](RECOVERY_WORKFLOWS.md) — provider workflow semantics and validation
- [Account Recovery Execution](ACCOUNT_RECOVERY_EXECUTION.md) — canonical per-account/action execution state
- [Recovery Location Discovery](RECOVERY_LOCATION_DISCOVERY.md) — safe provider navigation handoff
- [Browser Assistance Prototype](BROWSER_ASSISTANCE_PROTOTYPE.md) — bounded Playwright workflow and rollout decision

## Vault and credentials

- [Trusted Device and Vault Entry](VAULT_ENTRY.md) — trusted-device gate and vault lifecycle UI
- [Vault Security](VAULT_SECURITY.md) — cryptographic and secret-handling design
- [Workspace Persistence](WORKSPACE_PERSISTENCE.md) — atomic persistence and interrupted work
- [Generated Credentials](GENERATED_CREDENTIALS.md) — generation, encrypted lifecycle, and export core
- [Cryptographic Prototype](CRYPTO_PROTOTYPE.md) — focused Argon2id/AES-GCM prototype

## Engineering

- [Threat Model](THREAT_MODEL.md) — threats, mitigations, and residual risks
- [Testing Strategy](TESTING.md) — authoritative testing and CI rules
- [Contributing](../CONTRIBUTING.md) — contribution workflow
- [Coding Agent Instructions](../AGENTS.md) — concise repository rules for automated contributors

Detailed implementation work and acceptance criteria live in GitHub issues rather than being duplicated in documentation.
