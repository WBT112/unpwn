# Localization and Multilingual GUI

## Purpose

unpwn must support additional GUI languages without coupling translated text to recovery logic, workflow execution, vault storage, or machine-readable data.

Localization is a presentation concern. Canonical identifiers and security decisions remain stable regardless of the selected language.

## Scope

The localization boundary covers all user-facing desktop content, including:

- window titles and navigation labels
- buttons, menus, dialogs, and confirmations
- validation messages and safe error messages
- warnings, blocked states, unresolved-risk explanations, and completion summaries
- tooltips, empty states, progress text, and accessibility names
- provider workflow guidance and completion instructions
- user-visible dates, times, numbers, and percentages

Logs, audit event types, workflow identifiers, action identifiers, database values, cryptographic metadata, error codes, and import/export schemas are not localized canonical data.

## Source Language and Fallback

The default source and fallback language is English (`en`).

Culture resolution is deterministic:

1. use the explicit in-application language override when configured
2. otherwise use the operating-system UI culture when supported
3. try the exact culture, such as `de-DE`
4. try its neutral parent culture, such as `de`
5. fall back to English
6. if the English resource key is missing, display a visible key marker such as `⟦Vault.Unlock.Title⟧` rather than an empty or misleading message

Missing keys in the default resource set are test and release failures. Runtime fallback must never suppress a security warning or confirmation consequence.

## Resource Layout

Use version-controlled .NET resource files owned by the desktop presentation project.

Current layout:

```text
src/Unpwn.App/
└── Localization/
    ├── Strings.resx / Strings.de.resx
    ├── AccountStrings.resx / AccountStrings.de.resx
    ├── DashboardStrings.resx / DashboardStrings.de.resx
    ├── RecoveryExecutionStrings.resx / RecoveryExecutionStrings.de.resx
    ├── CredentialStrings.resx / CredentialStrings.de.resx
    ├── VaultStrings.resx / VaultStrings.de.resx
    └── other feature-owned English/German pairs
```

The English `.resx` file in each feature pair is its complete source set. Every shipped German pair contains the same keys and formatting placeholders. Pseudo-localization is generated at runtime for layout/testing and is not a shipped translation resource. A new feature should extend the owning resource pair instead of creating a second generic source of the same text.

Resource keys are stable, descriptive, and grouped by feature, for example:

```text
Shell.Navigation.Accounts
Vault.Unlock.Title
Recovery.Status.Blocked
Recovery.Progress.CriticalAccounts
Import.Password.Warning
Import.Mapping.Issue.MissingAccountIdentity
Export.Plaintext.Warning
```

Keys are part of the presentation contract. Renaming or deleting a key requires updating every reference and every shipped resource set.

## Application Boundary

The desktop application provides one application-wide localization service. View models and views consume that service rather than generated static resource properties or ambient global culture directly.

The service must support:

- lookup by stable resource key
- explicit UI culture selection
- deterministic fallback
- parameterized formatting with the selected UI culture
- a culture-changed notification so visible screens can refresh
- safe handling of missing keys without including user data in diagnostics

The localization service belongs to the presentation layer. `Unpwn.Core` and security-sensitive feature modules must not depend on Avalonia, resource managers, translated strings, or the selected UI culture.

## Language-Neutral Domain Data

The following remain language-neutral:

- enum and status values
- workflow and provider identifiers
- recovery action identifiers and types
- audit event types
- structured error and diagnostic codes
- vault record categories and opaque identifiers
- persisted recovery state
- import/export schema fields

The presentation layer maps structured values to resource keys.

Do not persist translated labels or error messages as canonical state. A language change must not require a domain migration, vault rewrite, or workflow-version change.

User-authored notes remain exactly as entered and are never machine-translated by unpwn.

## Workflow Guidance

Provider workflow execution must never depend on visible text.

User-facing workflow titles, descriptions, completion guidance, warnings, and manual instructions should be represented by stable localization keys plus typed formatting arguments. Workflow validation, prerequisites, recovery paths, automation support, and completion state use canonical identifiers and structured data.

Provider display text uses stable localization keys. Translation changes alone must not alter workflow semantics or verification metadata.

## Parameterized and Plural-Sensitive Messages

Do not build translated sentences through string concatenation.

Use complete parameterized resources, for example:

```text
Recovery.Progress.CriticalAccounts = {0} of {1} critical accounts fully reviewed
```

Formatting uses the selected UI culture explicitly.

Plural-sensitive messages use explicit semantic variants such as `.Zero`, `.One`, and `.Other`, selected by presentation logic. The localization abstraction may later adopt an ICU-compatible formatter without changing domain or view-model contracts.

Formatting arguments must not contain secrets unless the screen is explicitly designed to reveal that specific secret to the user. Resource lookup failures and formatting diagnostics must never log the argument values.

## Culture Handling

UI culture and data-processing culture are separate concerns.

The selected UI culture controls:

- translated resources
- displayed dates and times
- displayed numbers and percentages
- accessibility text

Invariant or explicitly specified culture controls:

- GUIDs and opaque identifiers
- URLs and origins
- cryptographic associated data
- workflow versions
- JSON and other machine-readable formats
- security-sensitive parsing

CSV and other user imports must define their parsing rules explicitly. They must not silently change meaning because the process UI culture changed.

The language override is available before vault unlock and can be stored as a non-secret application preference. Changing it must not expose, rewrite, or unlock vault data.

## Layout and Accessibility

Translated text may be substantially longer than English.

GUI implementation must:

- avoid fixed widths and heights based on English labels
- allow wrapping or scrolling where appropriate
- preserve keyboard navigation and visible focus
- localize accessibility names, descriptions, and access keys where supported
- combine status text and symbols so meaning does not depend on color
- tolerate pseudo-localized text with expansion and accented characters
- preserve an architectural path for right-to-left layout and mirrored navigation

A language is not considered supported merely because resource files compile. Critical workflows must remain understandable and operable at the documented minimum window size.

## Security Requirements

Localization must not weaken security communication.

- Security warnings, consequences, and unresolved risks must remain explicit in every shipped language.
- Missing translations fall back to reviewed English text, never to an empty label.
- Translation resources are repository-controlled and shipped with releases.
- unpwn does not download runtime language packs or use runtime machine translation for security-critical content.
- Translations must not change provider URLs, origins, workflow identifiers, action ordering, or authorization boundaries.
- Resource diagnostics may include a culture name and resource key only; they must not include user data or formatting values.

Security-sensitive translations should be reviewed for meaning, not only grammar.

## Testing and CI

Tests cover:

- English resource lookup
- German as the shipped secondary culture
- exact-culture and parent-culture fallback
- fallback to English
- missing default keys
- parameter substitution
- plural variants
- runtime culture switching and view-model refresh
- explicit formatting of dates, numbers, and percentages
- resource-key parity for shipped translations
- pseudo-localization and long-string layout behavior
- absence of direct user-facing string literals in presentation code where a practical analyzer or convention can enforce it

Pseudo-localization should expand text, add visible delimiters, and preserve formatting placeholders so clipping and concatenation defects are detectable without finished translations.

Localization checks are part of normal pull-request CI. Missing security-critical keys are release-blocking.

## Translation Contributions

Translation pull requests must:

- identify the target culture
- preserve resource keys and formatting placeholders
- avoid modifying workflow semantics or canonical identifiers
- use terminology consistent with the security glossary for that language
- include or update resource parity and formatting tests
- state whether a fluent or native reviewer checked security-sensitive wording

A translation may be shipped as incomplete only when fallback behavior is intentional, visible in review, and accepted for that release. The application must still provide complete English fallback content.

## Non-Goals

The localization architecture does not:

- translate user-authored notes
- localize logs, identifiers, audit event codes, or cryptographic metadata
- infer parsing rules from the selected GUI language
- download unreviewed runtime translations
- use translated display text as workflow control data
- require every planned language to ship in the same release
