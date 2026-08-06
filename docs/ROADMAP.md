# unpwn Roadmap

The roadmap prioritizes a trustworthy recovery workflow over broad website automation.

## MVP 0.1 - Foundation

- .NET LTS solution and project structure
- Avalonia desktop shell
- platform-neutral core boundaries
- repository documentation and contribution guidance
- automated build and test setup
- initial threat-model checks in development guidelines
- application-wide localization service and versioned resources
- complete English source and fallback resources
- language-neutral domain, workflow, audit, vault, and error codes
- pseudo-localization and resource fallback tests

The localization boundary is established in the foundation phase so later GUI work does not hard-code one language into views, validation, workflows, or persisted state. A complete set of additional translations is not required for the foundation milestone.

See [Localization and Multilingual GUI](LOCALIZATION.md).

## MVP 0.2 - Recovery Domain

- `RecoverySession` model
- `Account` and `AccountDependency` models
- versioned recovery workflow definitions
- recovery action state machine
- account prioritization
- critical-account readiness
- weighted action progress
- unresolved-risk handling
- append-only audit events without secrets or localized summaries
- stable presentation codes for localized status and error mapping

## MVP 0.3 - Encrypted Recovery Vault

- user-defined vault password
- Argon2id key derivation
- random vault data key and key wrapping
- AES-256-GCM encrypted records
- SQLite persistence container
- vault create, unlock, lock, reopen, and password-change flows
- secret-safe logging and crash behavior
- credential lifecycle tracking
- invariant record identifiers, associated data, and serialized values independent of GUI culture

## MVP 0.4 - Account Import and Planning

- generic CSV import
- column mapping workflow
- browser/password-manager export import support
- duplicate detection
- manual account creation and editing
- account priority suggestions
- dependency identification, especially primary-email reset dependencies
- localized mapping and warning UI over culture-explicit parsing rules

## MVP 0.5 - Initial Recovery Workflows

Initial providers:

1. Google
2. Microsoft
3. GitHub

Each provider defines repository-reviewed recovery workflows such as:

- authenticated password change
- password reset
- manual recovery guidance
- session invalidation
- MFA review
- recovery-option review
- connected application, token, or trusted-device review

Provider workflow semantics use canonical identifiers. User-facing titles, instructions, warnings, and completion guidance use reviewed localization resources and English fallback.

Provider changes are contributed through pull requests and shipped with releases. No third-party provider code or language packs are downloaded or executed at runtime.

## MVP 0.6 - Export and Completion

- secure password generation
- encrypted credential retention during recovery
- generic plaintext CSV export with explicit warnings
- at least one password-manager-specific export format
- session completion summary
- unresolved-risk report
- vault credential cleanup options
- localized human-readable reports while machine-readable export schemas remain deterministic

## MVP 0.7 - Automation Assistance

- recovery location discovery
- `/.well-known/change-password` support
- visible Playwright browser assistance for a small set of bounded workflows
- user-assisted handling of email links, MFA, CAPTCHA, and identity verification
- safe failure and manual fallback
- localized guidance that never controls automation selectors or authorization

Automation remains a supporting feature. The primary product value is discovery, prioritization, recovery workflows, dependency handling, and progress management.

## Translation rollout

After the localization foundation is stable:

1. migrate every existing user-facing string to resources
2. add in-application language selection and persistence before vault unlock
3. ship the first reviewed secondary language resource set
4. add terminology guidance for security-sensitive translations
5. expand language coverage through reviewed pull requests
6. add right-to-left verification before shipping the first RTL language

A language is considered supported only when critical workflows, warnings, confirmations, accessibility text, fallback behavior, and minimum-window layout have been verified.

## Future

Possible future work:

- macOS and Linux application packaging
- additional password-manager formats
- additional provider workflows
- additional reviewed GUI languages
- right-to-left language support
- improved dependency suggestions
- advanced recovery recommendations
- carefully bounded provider automation
- professional support or services without making the local core cloud-dependent
