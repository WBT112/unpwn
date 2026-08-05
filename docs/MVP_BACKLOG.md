# MVP Backlog

This file summarizes the initial implementation sequence. GitHub issues contain the detailed acceptance criteria.

## Foundation

1. [#1](https://github.com/WBT112/unpwn/issues/1) Scaffold the .NET and Avalonia solution.
2. [#2](https://github.com/WBT112/unpwn/issues/2) Add CI, tests, formatting, and security-sensitive logging checks.

## Recovery Domain

3. [#3](https://github.com/WBT112/unpwn/issues/3) Implement the recovery domain model and workflow state machine.
4. [#4](https://github.com/WBT112/unpwn/issues/4) Implement critical-account readiness and progress calculations.
5. [#9](https://github.com/WBT112/unpwn/issues/9) Implement account dependencies and recovery-order planning.

## Recovery Vault

6. [#5](https://github.com/WBT112/unpwn/issues/5) Validate the Argon2id and AES-256-GCM design in a focused cryptographic prototype.
7. [#6](https://github.com/WBT112/unpwn/issues/6) Implement the encrypted SQLite recovery vault.
8. [#7](https://github.com/WBT112/unpwn/issues/7) Implement vault lifecycle, locking, password changes, and secret-safe diagnostics.

## Import and Workflow Catalog

9. [#8](https://github.com/WBT112/unpwn/issues/8) Implement generic CSV import and column mapping.
10. [#10](https://github.com/WBT112/unpwn/issues/10) Define and validate repository-controlled recovery workflow definitions.
11. [#17](https://github.com/WBT112/unpwn/issues/17) Build workflow validation, provider contract tests, and the synthetic-provider test harness.
12. [#11](https://github.com/WBT112/unpwn/issues/11), [#12](https://github.com/WBT112/unpwn/issues/12), and [#13](https://github.com/WBT112/unpwn/issues/13) add the initial Google, Microsoft, and GitHub recovery workflows.
13. [#18](https://github.com/WBT112/unpwn/issues/18) Add scheduled read-only provider smoke checks.

## Credentials, Export, and Assistance

14. [#14](https://github.com/WBT112/unpwn/issues/14) Implement generated-credential lifecycle and secure exports.
15. [#15](https://github.com/WBT112/unpwn/issues/15) Implement recovery-location discovery, including `/.well-known/change-password`.
16. [#16](https://github.com/WBT112/unpwn/issues/16) Prototype visible Playwright browser assistance for one bounded workflow, with headless execution restricted to synthetic CI tests.

Automation is deliberately scheduled after the recovery domain, vault, workflow catalog, and deterministic test harness.

The project-wide testing rules are documented in [`docs/TESTING.md`](TESTING.md).
