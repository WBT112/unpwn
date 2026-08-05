# unpwn Threat Model

## Purpose

This document describes the security assumptions, protected assets, threats, mitigations, and boundaries of unpwn.

## Security Goal

unpwn helps users recover their digital identity after a suspected account compromise. It protects stored recovery data while the vault is locked and helps users avoid missing important recovery actions.

It does not guarantee that an already compromised system is safe.

## Protected Assets

- account inventory
- usernames and email addresses
- account and recovery-channel dependencies
- generated new credentials
- recovery progress and notes
- recovery history
- export data
- vault encryption keys

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

Residual risk:

Deletion does not guarantee forensic erasure on modern storage.

### Secret leakage through logs or crash reports

Risk:

Credentials, reset tokens, or decrypted records appear in diagnostic output.

Mitigations:

- no secret values in logs, exception messages, telemetry, or audit events
- sanitize sensitive object representations
- tests for known secret patterns in diagnostic output
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

### Misleading completion status

Risk:

A user sees a high percentage and assumes all critical accounts are secure.

Mitigations:

- display critical-account readiness separately
- display blocked actions and unresolved risks prominently
- do not count unresolved required actions as complete
- require explicit session completion

## Out of Scope

unpwn does not:

- detect or remove malware
- prove that a device is clean
- bypass MFA
- bypass CAPTCHA
- bypass identity verification or account-ownership checks
- guarantee account recovery
- guarantee that a plaintext export has been securely erased

## Security Principle

Automation should reduce workload while keeping critical security decisions visible to the user. Recovery status must communicate uncertainty and unresolved risk rather than create false assurance.
