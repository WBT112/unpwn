# Contributing to unpwn

Thank you for helping improve unpwn.

unpwn is security-sensitive software. Small, reviewable changes with tests and clear reasoning are preferred over large feature dumps.

Coding agents and automated contributors must also follow [`AGENTS.md`](AGENTS.md).

## Ways to Contribute

- improve documentation
- report broken recovery workflows
- add or update provider recovery workflows
- add or review translations
- improve tests
- review security and privacy assumptions
- implement scoped roadmap issues

## Before Starting

For non-trivial changes, open or reference an issue that explains:

- the problem
- the proposed scope
- security or privacy impact
- localization impact for user-facing changes
- alternatives considered

Do not include credentials, cookies, reset links, personal account data, or other live secrets in issues, commits, tests, screenshots, translations, or pull requests.

## Pull Requests

Pull requests should:

- address one coherent problem
- include a clear description and test plan
- preserve platform-neutral core boundaries
- avoid adding cloud dependencies to the local MVP
- avoid secret values in logs or exceptions
- update documentation when behavior or architecture changes
- include tests for new domain logic, vault behavior, workflow definitions, or localization behavior
- keep user-facing text localizable and canonical data language-neutral

## Localization and Translation Contributions

Follow [`docs/LOCALIZATION.md`](docs/LOCALIZATION.md).

User-facing text belongs in version-controlled presentation resources. Do not hard-code labels, warnings, validation messages, dialog text, accessibility names, or workflow guidance in XAML, code-behind, view models, or domain services.

Translation pull requests should include:

- the target BCP 47 culture, such as `de` or `de-DE`
- unchanged stable resource keys
- preserved formatting placeholders and plural variants
- resource parity and fallback test results
- consistent terminology for accounts, credentials, sessions, MFA, unresolved risks, vault operations, and exports
- confirmation that security-sensitive wording was reviewed for meaning, not only grammar

Do not translate or change:

- workflow and action identifiers
- provider URLs and expected origins
- status and error codes
- audit event types
- vault record categories or cryptographic metadata
- machine-readable import/export field names unless the format explicitly defines localized headers

Translation-only changes must not alter workflow semantics, prerequisites, completion state, or provider verification metadata. unpwn does not use runtime machine translation for security-critical content.

## Recovery Workflow Contributions

Recovery workflows and provider updates are contributed through pull requests to this repository.

unpwn does not use a runtime provider marketplace and does not download or execute third-party provider plugins.

A recovery workflow pull request should include:

- provider name and supported account type
- workflow version
- verification date
- recovery locations and URLs
- required and optional actions
- supported credential recovery paths
- dependencies and blocking conditions
- automation support level
- completion criteria
- localization keys for user-facing guidance where applicable
- structural validation
- semantic validation
- provider contract scenarios

Workflow URLs must point to the legitimate provider domain or a documented official endpoint.

Do not add automation that bypasses CAPTCHA, MFA, identity verification, rate limits, or account-ownership checks.

Workflow execution and validation must use canonical identifiers and structured data. Never branch on translated display text.

## Cryptography and Vault Changes

Changes to vault cryptography, key management, secret handling, export behavior, or authentication require focused review.

Do not introduce custom cryptographic primitives.

The current design uses:

- Argon2id for vault-password key derivation
- AES-256-GCM for authenticated record encryption
- a random vault data key protected by a password-derived key

Cryptographic changes must include:

- threat and migration analysis
- tests for failure and tampering cases
- documentation updates
- backward-compatibility considerations for the vault format

Localization must not alter opaque record identifiers, associated data, algorithm names, serialized values, or cryptographic parsing.

## Testing

Follow [`docs/TESTING.md`](docs/TESTING.md).

Normal pull-request tests must be deterministic and must not require real provider accounts or changing live websites.

Expected coverage includes, where applicable:

- unit and state-machine tests
- workflow schema validation
- semantic workflow validation
- provider contract scenarios
- critical-account readiness and progress calculations
- vault tampering and secret-leakage tests
- localization lookup, fallback, formatting, and resource-parity tests
- pseudo-localization or long-string UI coverage
- integration tests against the local synthetic provider
- Playwright tests against the synthetic provider

Production browser assistance must remain visible. Headless browser execution is allowed only in explicit test mode against the local synthetic provider.

Read-only live-provider checks belong in scheduled or manually triggered smoke workflows. They must not submit forms, use credentials, trigger reset emails, or upload browser state and sensitive traces.

Tests and CI artifacts may contain synthetic data only. Use recognizable synthetic secret markers and verify that they do not leak into logs, exceptions, database files, traces, screenshots, localization diagnostics, or uploaded artifacts.

Do not claim a test passed unless it was actually run. Document checks that could not be executed.

## Security Reports

Do not report exploitable vulnerabilities publicly.

Follow [SECURITY.md](SECURITY.md) for private reporting guidance.

## License

By contributing, you agree that your contribution is licensed under the repository's GNU Affero General Public License v3.0.
