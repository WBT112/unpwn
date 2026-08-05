# MVP Backlog

This file summarizes the initial implementation sequence. GitHub issues contain the detailed acceptance criteria.

## Foundation

1. Scaffold the .NET and Avalonia solution.
2. Add CI, tests, formatting, and security-sensitive logging checks.

## Recovery Domain

3. Implement the recovery domain model and workflow state machine.
4. Implement critical-account readiness and progress calculations.
5. Implement account dependencies and recovery-order planning.

## Recovery Vault

6. Validate the Argon2id and AES-256-GCM design in a focused cryptographic prototype.
7. Implement the encrypted SQLite recovery vault.
8. Implement vault lifecycle, locking, password changes, and secret-safe diagnostics.

## Import and Workflow Catalog

9. Implement generic CSV import and column mapping.
10. Define and validate repository-controlled recovery workflow definitions.
11. Add initial Google, Microsoft, and GitHub recovery workflows.

## Credentials, Export, and Assistance

12. Implement generated-credential lifecycle and secure exports.
13. Implement recovery-location discovery, including `/.well-known/change-password`.
14. Prototype visible Playwright browser assistance for one bounded workflow.

Automation is deliberately scheduled after the recovery domain, vault, and workflow catalog.
