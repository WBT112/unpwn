# MVP Backlog

This file summarizes the initial implementation sequence. GitHub issues contain the detailed acceptance criteria.

## Foundation

1. [#1](https://github.com/WBT112/unpwn/issues/1) Scaffold the .NET and Avalonia solution.
2. [#2](https://github.com/WBT112/unpwn/issues/2) Add CI, tests, formatting, and security-sensitive logging checks.
3. [#49](https://github.com/WBT112/unpwn/issues/49) Establish the GUI localization boundary before additional user-facing surfaces are implemented.

Issue #49 provides the application-wide localization service, complete English source and fallback resources, language-neutral presentation codes, runtime culture support, resource tests, and pseudo-localization. Complete translations are delivered later through reviewed resource sets.

See [`docs/LOCALIZATION.md`](LOCALIZATION.md).

## Recovery Domain

4. [#3](https://github.com/WBT112/unpwn/issues/3) Implement the recovery domain model and workflow state machine.
5. [#4](https://github.com/WBT112/unpwn/issues/4) Implement critical-account readiness and progress calculations.
6. [#9](https://github.com/WBT112/unpwn/issues/9) Implement account dependencies and recovery-order planning.

Domain values, audit event types, workflow IDs, action IDs, progress data, and diagnostic codes remain language-neutral. The desktop presentation layer maps them to localized resources.

## Recovery Vault

7. [#5](https://github.com/WBT112/unpwn/issues/5) Validate the Argon2id and AES-256-GCM design in a focused cryptographic prototype.
8. [#6](https://github.com/WBT112/unpwn/issues/6) Implement the encrypted SQLite recovery vault.
9. [#7](https://github.com/WBT112/unpwn/issues/7) Implement vault lifecycle, locking, password changes, and secret-safe diagnostics.

Vault formats, record identifiers, associated data, and serialized values are invariant and never derived from translated labels or the selected UI culture.

## Import and Workflow Catalog

10. [#8](https://github.com/WBT112/unpwn/issues/8) Implement generic CSV import and column mapping.
11. [#10](https://github.com/WBT112/unpwn/issues/10) Define and validate repository-controlled recovery workflow definitions.
12. [#17](https://github.com/WBT112/unpwn/issues/17) Build workflow validation, provider contract tests, and the synthetic-provider test harness.
13. [#11](https://github.com/WBT112/unpwn/issues/11), [#12](https://github.com/WBT112/unpwn/issues/12), and [#13](https://github.com/WBT112/unpwn/issues/13) add the initial Google, Microsoft, and GitHub recovery workflows.
14. [#18](https://github.com/WBT112/unpwn/issues/18) Add scheduled read-only provider smoke checks.

Import parsing is explicit and culture-safe. Provider workflow execution uses canonical identifiers; user-facing guidance uses stable localization keys and reviewed resources.

## Credentials, Export, and Assistance

15. [#14](https://github.com/WBT112/unpwn/issues/14) Implement the generated-credential lifecycle and secure export core before workflow actions require credential references.
16. [#15](https://github.com/WBT112/unpwn/issues/15) Implement recovery-location discovery, including `/.well-known/change-password`.
17. [#16](https://github.com/WBT112/unpwn/issues/16) Prototype visible Playwright browser assistance for one bounded workflow, with headless execution restricted to synthetic CI tests.

Machine-readable export formats remain deterministic. Human-readable warnings and reports use the selected UI culture and complete English fallback resources.

## MVP Desktop UI

The UI work is tracked by [Epic #29](https://github.com/WBT112/unpwn/issues/29).

Current sequence:

1. [#52](https://github.com/WBT112/unpwn/issues/52) defines the guided wizard and remains the end-to-end orchestration issue.
2. [#30](https://github.com/WBT112/unpwn/issues/30) establishes the application shell and security-oriented visual foundation.
3. [#49](https://github.com/WBT112/unpwn/issues/49) establishes localization before feature screens.
4. [#31](https://github.com/WBT112/unpwn/issues/31) adds the trusted-device gate and vault lifecycle UI.
5. [#32](https://github.com/WBT112/unpwn/issues/32) adds incident intake, the recovery session, safe resume, and dashboard.
6. [#33](https://github.com/WBT112/unpwn/issues/33) adds account inventory, import review, priorities, confirmed roles, and dependencies.
7. [#15](https://github.com/WBT112/unpwn/issues/15) establishes safe browser-location discovery.
8. The blocking [#37](https://github.com/WBT112/unpwn/issues/37) slice adds atomic workspace persistence, revisions, serialized reloads, and safe interrupted-write behavior before action execution.
9. [#14](https://github.com/WBT112/unpwn/issues/14) supplies generated credentials and stable encrypted references used by recovery actions.
10. Canonical account/action state and provider localization keys are stabilized before [#34](https://github.com/WBT112/unpwn/issues/34).
11. [#34](https://github.com/WBT112/unpwn/issues/34) adds recovery workflow execution with the [#38](https://github.com/WBT112/unpwn/issues/38) accessibility baseline integrated from the start.
12. [#35](https://github.com/WBT112/unpwn/issues/35) adds export, password-manager import confirmation, and cleanup UX.
13. [#36](https://github.com/WBT112/unpwn/issues/36) adds session completion and the final report.
14. Remaining #37 and #38 acceptance criteria are completed before MVP release.

UI issues may begin only when their required application, domain, vault, credential, and persistence interfaces are stable. Do not build views against speculative APIs from unmerged branches.

Every UI issue must use localization resources for labels, warnings, validation, accessibility text, and formatted values. Pseudo-localization and long-string behavior are part of relevant UI acceptance tests.

## Sequencing Principles

Localization architecture is deliberately scheduled near the foundation so user-facing strings, validation messages, workflow guidance, and accessibility labels do not become coupled to one display language.

Atomic persistence and accessibility are cross-cutting requirements, not final cleanup phases. Each later workflow, export, and completion change must add interruption and accessibility coverage while its contracts are introduced.

Automation is deliberately scheduled after the recovery domain, vault, workflow catalog, and deterministic test harness.

Security-sensitive changes follow one issue, one branch, one pull request, successful CI, review, and merge. Direct feature commits to `main` should be avoided.

The project-wide architecture and test rules are documented in:

- [`docs/ARCHITECTURE.md`](ARCHITECTURE.md)
- [`docs/UI_FOUNDATION.md`](UI_FOUNDATION.md)
- [`docs/WORKSPACE_PERSISTENCE.md`](WORKSPACE_PERSISTENCE.md)
- [`docs/LOCALIZATION.md`](LOCALIZATION.md)
- [`docs/TESTING.md`](TESTING.md)
