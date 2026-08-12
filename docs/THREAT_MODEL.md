# unpwn Threat Model

## Purpose

This document describes the security assumptions, protected assets, threats, mitigations, and boundaries of unpwn.

## Security Goal

unpwn helps users recover their digital identity after a suspected account compromise. It protects stored recovery data while the vault is locked and helps users avoid missing important recovery actions.

It does not guarantee that an already compromised system is safe.

Security-critical guidance must remain complete and understandable in every shipped GUI language. Localization must not alter recovery semantics, hide unresolved risks, or influence canonical security decisions.

## Protected Assets

- account inventory
- usernames and email addresses
- account and recovery-channel dependencies
- generated new credentials
- recovery progress and notes
- recovery history
- export data
- vault encryption keys
- the integrity of security warnings and recovery guidance

## Trust Boundary

unpwn should be executed on a trusted device.

If malware or an attacker controls the operating system, they may be able to observe or manipulate:

- keyboard input
- clipboard contents
- browser sessions
- screen contents
- new credentials
- application memory
- network traffic after it leaves the application
- displayed UI text or language preferences

No local recovery application can fully prevent this.

## Cryptographic Boundary

The Recovery Vault uses:

- a user-defined vault password
- Argon2id to derive a key-encryption key
- a random vault data key
- AES-256-GCM authenticated encryption for sensitive records
- unique nonces for every encryption operation

The vault design aims to protect confidentiality and integrity when an attacker obtains a locked vault file.

Cryptography does not protect secrets while they are legitimately decrypted on a compromised device.

Localization resources and selected UI culture are outside the cryptographic format. Translated labels must not become record identifiers, associated data, key-derivation input, or authorization data.

## Threat Scenarios

### Active device compromise

Risk:

An attacker can observe new credentials, manipulate browser actions, or access decrypted recovery data.

Mitigations:

- clear security warning before recovery starts
- recommend a trusted device
- no claim that unpwn cleans or validates the device
- visible browser assistance
- user confirmation for sensitive actions

Residual risk:

This threat cannot be reliably mitigated by unpwn while the operating system is under attacker control.

### Vault-file theft

Risk:

An attacker obtains the locked recovery vault file and attempts offline recovery of its contents.

Mitigations:

- Argon2id password-based key derivation
- AES-256-GCM authenticated encryption
- random salt and versioned KDF parameters
- random vault data key
- no plaintext account or credential records

Residual risk:

A weak vault password remains vulnerable to guessing. unpwn must encourage a strong, unique vault password without claiming that strength can be guaranteed.

### Vault tampering

Risk:

An attacker modifies encrypted records, metadata, or workflow state.

Mitigations:

- authenticated encryption
- associated data binding record type, identifier, and schema version
- rejection of records that fail authentication
- append-only audit semantics at the domain level

### Nonce reuse or cryptographic implementation error

Risk:

Incorrect AES-GCM nonce handling or key management can undermine confidentiality and integrity.

Mitigations:

- central cryptographic service rather than provider-specific cryptography
- cryptographically secure nonce generation
- tests that detect accidental nonce reuse within a vault
- versioned vault format
- focused security review before production release

### Unsafe exports

Risk:

Plaintext export files containing credentials are copied, synchronized, backed up, or forgotten.

Mitigations:

- explicit export confirmation
- warning before creating plaintext formats
- explicit destination selection
- warning about synchronized folders
- recommend immediate import into an established password manager
- offer deletion after confirmed import
- complete reviewed warning text in every shipped language with English fallback

Residual risk:

Deletion does not guarantee forensic erasure on modern storage.

### Secret leakage through logs or crash reports

Risk:

Credentials, reset tokens, decrypted records, or localization formatting arguments appear in diagnostic output.

Mitigations:

- no secret values in logs, exception messages, telemetry, audit events, or localization diagnostics
- sanitize sensitive object representations
- tests for known secret patterns in diagnostic output
- localization diagnostics include resource keys and cultures only
- no automatic upload of crash data containing application state

### Malicious or compromised recovery workflow

Risk:

A workflow directs users to an attacker-controlled site or performs an unsafe action.

Mitigations:

- workflows are shipped from the main repository
- all changes arrive through pull requests
- no runtime download or execution of third-party provider plugins
- review of recovery URLs, permissions, and automation behavior
- tests and verification metadata for provider workflows
- workflow execution uses canonical identifiers rather than translated display text

### Embedded provider content escapes the Recovery Browser boundary

Risk:

A provider page, redirect, popup, download, permission request, external protocol, or invalid TLS
connection reaches a capability or destination that the current reviewed recovery action did not
authorize. Browser state could also be confused with canonical recovery evidence.

Mitigations:

- provider entry goes through the existing validated recovery-location handoff
- top-level navigation is limited to exact expected origins and HTTPS; HTTP is accepted only for an
  explicit loopback synthetic-test mode
- file, data, JavaScript, custom, and external application schemes fail closed
- popups/new windows, downloads, website permissions, client certificates, and TLS exceptions are
  denied by default
- platform browser controls are configured behind WebView2 and WPE WebKit adapters before navigation
- the browser uses an opaque unpwn-owned data path instead of a normal user browser profile
- WebView2 password autosave, general autofill, OS-account SSO, developer tools, browser accelerator
  keys, and default context menus are disabled
- WPE WebKit uses a dedicated data/cache location, disables persistent credential storage, developer
  tools, permissions, downloads, and TLS exceptions where its native API exposes those controls
- current origin and denied capabilities are visible in localized application chrome
- browser events do not call canonical recovery execution services
- one active opaque profile is bound to one account only in process memory; account switching is
  blocked until cleanup succeeds
- clean close clears engine-managed browsing data, waits for native resources to release, and then
  deletes the entire dedicated profile
- orphaned or cleanup-failed profiles remain visible and retryable at startup and are never resumed
  automatically

Residual risk:

Provider content remains untrusted and can contain deceptive UI. Platform engines differ, and WPE
WebKit does not expose every WebView2 autofill control through the maintained Avalonia surface.
Dedicated profile storage and conservative cleanup limit cross-profile reuse, but an operating-system
or engine crash may leave data until the explicit retry succeeds. Filesystem snapshots, backups, and
storage-device behavior can retain deleted bytes; the UI must not claim forensic erasure. The guided
assistant uses this lifecycle through the composition root and never constructs or switches profiles
from browser observations.

### Credential leakage or unsafe automatic browser insertion

Risk:

A malicious or changed provider page could trick generic automation into putting a newly generated
credential into the wrong field or origin. Browser script failures could also leak a credential if
script text, page content, screenshots, traces, or exception messages were retained. Treating a field
insertion or form submission as proof of successful recovery would create false assurance.

Mitigations:

- manual Reveal/Copy remains the safe default and uses the existing short-lived credential lease and
  owned-clipboard safeguards
- generic and unsupported-provider workflows never receive automatic DOM password-field discovery
- field insertion requires a repository-controlled provider/action adapter with explicit content mode,
  exact expected origins, page evidence, and exact selectors
- Issue #95 initially exposes the insertion adapter only in loopback `SyntheticTest` mode; real
  providers remain manual until a separate adapter is reviewed
- every attempt requires a fresh visible user authorization
- the browser inspects origin/page evidence before the credential is read from the vault
- MFA, CAPTCHA, email-link handoff, wrong origin, missing/duplicated fields, or changed content stops
  before secret retrieval
- after inspection, the credential is obtained only through a short-lived `CredentialSecretLease`
- the browser re-checks the exact contract immediately before insertion rather than trusting an old
  inspection result
- the insertion code sets only the reviewed new-password and confirmation fields and never submits
  the form
- browser script results contain only stable non-secret status codes; script exception details are
  not copied to diagnostics because the insertion script contains the transient credential
- screenshots and tracing containing real credentials remain prohibited
- successful insertion may explicitly mark the credential lifecycle as `Used`, but never as
  `Confirmed`, and never completes the recovery action
- provider success remains a user/provider verification decision under the canonical execution model
- browser close and vault lock clear materialized reveal state and request cleanup of an unpwn-owned
  clipboard value

Residual risk:

Provider markup can change between repository review and use, and script execution is part of the
untrusted browser interaction surface. The fail-closed contract reduces but cannot eliminate that
risk. If expected evidence changes, unpwn must fall back to manual Reveal/Copy instead of guessing.
A malicious provider page necessarily receives a credential when the user deliberately enters or
inserts it; unpwn cannot make a hostile provider trustworthy.

### Malicious, incorrect, or incomplete translation

Risk:

A translation changes the meaning of a security warning, removes a consequence, understates unresolved risk, alters a provider instruction, or causes the user to authorize the wrong action.

Mitigations:

- translation resources are repository-controlled and reviewed through pull requests
- English is the complete source and fallback language
- missing translations never produce empty security text
- resource-key and formatting-placeholder parity checks
- security-sensitive terminology review by a fluent or native reviewer where practical
- translated text never controls workflow paths, URLs, prerequisites, completion, or authorization
- no runtime machine translation for security-critical content
- confirmation dialogs execute structured canonical actions rather than button text

Residual risk:

A grammatically valid translation may still communicate security meaning poorly. Review must assess meaning and consequences, not only linguistic correctness.

### Text clipping or inaccessible localized UI

Risk:

Longer translated strings, plural forms, or right-to-left presentation hide warnings, controls, status text, or confirmation consequences.

Mitigations:

- pseudo-localization and long-string tests
- layouts that wrap or scroll rather than assume English dimensions
- minimum-window verification
- localized accessibility names and descriptions
- status symbols and text in addition to color
- an architectural path for right-to-left flow direction

### Culture-sensitive parsing changes meaning

Risk:

Changing the GUI language changes interpretation of imported values, URLs, dates, numbers, identifiers, or serialized security data.

Mitigations:

- explicit separation of UI culture and data-processing culture
- invariant parsing for identifiers, URLs, workflow versions, and cryptographic data
- declared culture or unambiguous format for imported date and numeric fields
- deterministic machine-readable export formats
- tests that run parsing under multiple UI cultures

### Misleading completion status

Risk:

A user sees a high percentage and assumes all critical accounts are secure.

Mitigations:

- display critical-account readiness separately
- display blocked actions and unresolved risks prominently
- do not count unresolved required actions as complete
- require explicit session completion
- verify that every shipped language preserves these distinctions

## Out of Scope

unpwn does not:

- detect or remove malware
- prove that a device is clean
- bypass MFA
- bypass CAPTCHA
- bypass identity verification or account-ownership checks
- guarantee account recovery
- guarantee that a plaintext export has been securely erased
- provide generic automatic password-field detection for arbitrary providers
- interpret browser navigation, field insertion, or submission as proof that a recovery action succeeded
- guarantee the quality of unofficial modified translations outside released repository resources

## Security Principle

Automation should reduce workload while keeping critical security decisions visible to the user.
Browser observations are context, not recovery truth. Recovery status must communicate uncertainty
and unresolved risk rather than create false assurance.

Localization must preserve that same security meaning. A missing or unclear translation is a security defect, not merely a cosmetic issue.

See [Localization and Multilingual GUI](LOCALIZATION.md).
