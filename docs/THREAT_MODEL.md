# unpwn Threat Model

## Purpose and security goal

unpwn helps a user regain control of digital accounts after suspected compromise. It protects stored recovery data while the Recovery Vault is locked and helps the user execute and track recovery work without hiding unresolved risk.

unpwn does **not** prove that the current device is clean, remove malware, guarantee provider recovery, or make a compromised operating system trustworthy.

## Protected assets

- account inventory, identifiers, suggested categories, and explicit category choices;
- incident context, recovery progress, notes, and history;
- generated new credentials and vault encryption keys;
- export data and credential lifecycle state;
- integrity of recovery guidance, warnings, provider destinations, and completion state.

## Trust boundary: the host device

unpwn should be run on a device the user reasonably trusts. The trusted-device decision is a user assertion, not malware detection.

If malware or an attacker controls the operating system, they may be able to observe or manipulate keyboard input, clipboard content, screen content, browser sessions, application memory, new credentials, or network traffic after it leaves the application's protected components. A local recovery application cannot reliably eliminate this risk while the host OS is controlled by an attacker.

Mitigation is therefore procedural as well as technical: the sensitive flow is gated behind the trusted-device acknowledgement and unpwn never claims to clean or validate the host.

## Cryptographic boundary

The Recovery Vault uses a user-defined vault password, Argon2id key derivation, a random vault data key, and AES-256-GCM authenticated encryption with unique nonces and associated data that binds records to their type/identifier/schema.

The goal is confidentiality and integrity when an attacker obtains a **locked** vault file. Cryptography cannot protect plaintext while it is legitimately decrypted on a compromised host.

Detailed cryptographic design and parameter rules live in [Vault Security](VAULT_SECURITY.md).

## Threats and mitigations

### Vault theft and offline guessing

**Risk:** an attacker obtains a locked vault and attempts password guessing.

**Mitigations:** Argon2id password-based derivation, random salt/data key, authenticated encryption, no plaintext recovery records, and guidance to use a strong unique vault password.

**Residual risk:** weak vault passwords remain guessable; unpwn cannot guarantee user-selected password strength.

### Vault/state tampering

**Risk:** encrypted records, metadata, workflow state, or revisions are modified/replayed.

**Mitigations:** authenticated encryption, associated-data binding, fail-closed record authentication, canonical revision/operation-ID checks, atomic persistence for related state, and explicit conflict handling.

### Cryptographic implementation error or nonce misuse

**Risk:** key/nonce mistakes undermine confidentiality/integrity.

**Mitigations:** centralized reviewed cryptographic services, secure nonce generation, versioned formats, tests for crypto invariants, and focused security review before production release.

### Unsafe plaintext exports

**Risk:** exported credentials are synchronized, backed up, shared, or forgotten.

**Mitigations:** explicit plaintext warning/destination choice, no overwrite, synchronized-location warning, prompt password-manager import, separate import confirmation and cleanup state, and clear reporting when a plaintext file may exist after a later persistence failure.

**Residual risk:** deleting a file is not forensic erasure on modern storage.

### Secret leakage through diagnostics or presentation

**Risk:** credentials, reset tokens, decrypted data, or imported sensitive values enter logs, exception messages, audit events, accessibility text, screenshots/traces, or crash artifacts.

**Mitigations:** structured secret-safe diagnostics, no secret-bearing object summaries, short-lived credential leases, bounded reveal/clipboard handling, synthetic-secret scans in CI, and no automatic crash upload containing application state.

### Malicious or incorrect recovery workflow

**Risk:** repository workflow data directs the user to an attacker-controlled destination or incorrectly describes a sensitive action.

**Mitigations:** repository-controlled workflows, pull-request review, no runtime third-party workflow code/plugins, exact reviewed recovery origins, semantic validation, stable canonical identifiers, and provider verification metadata.

**Residual risk:** provider behavior can change after review; scheduled read-only smoke checks are observations, not proof of safety.

### Recovery Browser escape or cross-account state reuse

**Risk:** provider content, redirects, popups, permissions, downloads, external protocols, TLS exceptions, or stale authenticated browser state escape the intended recovery boundary or leak between accounts.

**Mitigations:** exact-origin validated handoff, HTTPS-only production navigation, unsafe-scheme rejection, default-denied popup/download/permission/external-protocol behavior, dedicated unpwn-owned browser profiles, same-account reuse only, cross-account cleanup enforcement, clear→release→delete session cleanup, explicit orphan handling, and no automatic resume after an unclean exit.

Browser events have no path to canonical completion/risk transitions. Detailed platform and lifecycle rules live in [Recovery Browser Security Boundary](RECOVERY_BROWSER.md).

**Residual risk:** provider content is untrusted; browser engines/platform controls differ; crashes, backups, filesystem snapshots, or storage behavior can leave temporary data until cleanup/retry and may retain deleted bytes.

### Credential insertion into the wrong provider field/page

**Risk:** changed or malicious page content causes a newly generated credential to be inserted into the wrong origin or control, or insertion is mistaken for successful recovery.

**Mitigations:** manual Reveal/Copy by default; no generic DOM password-field discovery; insertion only through repository-reviewed provider/action contracts; fresh visible authorization; origin/page evidence inspection before vault access; stop on MFA/CAPTCHA/email-link/unexpected page state; immediate contract re-check before insertion; no automatic form submission; insertion can mark only `Used`, never `Confirmed` or recovery completion.

The repository currently enables automatic insertion only for the explicit synthetic-test contract. Detailed secret and lifecycle rules live in [Generated Credentials](GENERATED_CREDENTIALS.md).

### Misleading completion/progress

**Risk:** a user assumes a high percentage or browser outcome means critical accounts are secure.

**Mitigations:** critical readiness separated from aggregate progress, blocked/failed/lost-access/unresolved-risk states remain visible, completion preflight reads current persisted state, explicit completion is required, and browser observations never manufacture completion.

### Localization changes security meaning

**Risk:** translation changes warnings, authorization consequences, workflow meaning, parsing, or control decisions.

**Mitigations:** repository-controlled translations, complete English source/fallback, resource/placeholder parity tests, presentation-only localization, canonical language-neutral IDs/state, invariant parsing for machine data, and no runtime machine translation for security-critical content.

**Residual risk:** grammatically correct text can still communicate security meaning poorly; human review remains necessary for security-sensitive translations.

### Localized/inaccessible UI hides critical information

**Risk:** long text, layout constraints, color-only status, or assistive-technology problems hide warnings or actions.

**Mitigations:** wrapping/scrolling layouts, pseudo-localization, minimum-window checks, keyboard/focus tests, localized accessibility text, status text/symbols in addition to color, plus Windows/NVDA and Ubuntu/Orca release acceptance.

## Explicitly out of scope

unpwn does not:

- detect/remove malware or prove a host clean;
- bypass MFA, CAPTCHA, identity verification, ownership checks, or provider rate limits;
- guarantee account recovery or provider correctness;
- guarantee forensic erasure of browser/profile/export/vault storage;
- provide generic automatic password-field detection for arbitrary providers;
- interpret navigation, redirects, browser close, credential insertion, or form submission as proof of recovery success;
- provide unattended/autonomous account recovery.

## Security principle

Automation should reduce user workload while preserving explicit human security decisions. Recovery status must communicate uncertainty and unresolved risk rather than create false assurance.

**Browser observations are context, not recovery truth.**

See [Vault Security](VAULT_SECURITY.md), [Recovery Browser Security Boundary](RECOVERY_BROWSER.md), [Generated Credentials](GENERATED_CREDENTIALS.md), [Localization](LOCALIZATION.md), and [Testing Strategy](TESTING.md) for the detailed canonical rules behind those boundaries.
