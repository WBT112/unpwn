# Recovery Vault Security

## Purpose

The Recovery Vault is a local encrypted workspace used while a user restores control over affected accounts. It may remain active for days or weeks.

The vault is not intended to become a general-purpose password manager.

## Security Goals

The vault should protect recovery data when the vault file is copied, lost, stolen, or modified while locked.

The vault cannot protect secrets from an attacker who controls the operating system while unpwn is running.

## Primary Unlock Method

The user creates a dedicated vault password.

The vault password:

- is not stored by unpwn
- is not recoverable by the maintainers
- is the portable unlock mechanism across supported operating systems

Operating-system integrations may later offer optional convenience unlocking, but they must not replace the vault password as the portable security basis.

## Key Hierarchy

1. Generate a random vault data key.
2. Generate a random Argon2id salt.
3. Derive a key-encryption key from the vault password with Argon2id.
4. Encrypt the vault data key with the derived key.
5. Encrypt sensitive vault records with the vault data key.

This design permits a vault-password change by re-encrypting the data key rather than every stored record.

Argon2id parameters are stored as versioned vault metadata so they can be increased in future versions. Concrete parameters must be benchmarked on the minimum supported hardware before release.

## Cryptographic Prototype

The focused Argon2id and AES-256-GCM prototype for Issue #5 is documented in [Cryptographic Prototype](CRYPTO_PROTOTYPE.md). It validates password-derived key wrapping, random vault data keys, per-encryption nonces, AES-GCM authentication tags, and associated-data binding before the encrypted SQLite vault is implemented.

## Record Encryption

Sensitive records use AES-256-GCM authenticated encryption.

Requirements:

- generate a unique nonce for every encryption operation
- never reuse a nonce with the same key
- authenticate record type, opaque record identifier, and schema version as associated data
- reject modified ciphertext or associated data
- use cryptographically secure random generation for keys, salts, and nonces

SQLite is used as a storage container. Sensitive domain data must remain encrypted at the application layer.

Minimal unencrypted vault metadata may include:

- vault format version
- cryptographic algorithm identifiers
- Argon2id parameters and salt
- encrypted vault data key
- non-sensitive migration metadata
- repository-defined generic record categories
- opaque record identifiers

Record categories are selected from a repository-controlled allowlist. They must describe only generic storage categories such as `account-state` or `generated-credential`; they must not contain provider names, account names, usernames, URLs, notes, translated labels, or other user-controlled values.

Record identifiers must be non-empty opaque GUIDs. Email addresses, usernames, provider identifiers, account labels, localized names, URLs, and recovery-state descriptions are not valid plaintext record identifiers.

Account names, usernames, URLs, notes, credentials, and recovery state must not be stored in plaintext.

Even generic record metadata is available only while the vault is unlocked. Listing encrypted record descriptors must fail closed when the vault is locked.

## Localization boundary

Localization is not part of the cryptographic format.

The following must remain invariant and language-neutral:

- vault format versions
- algorithm identifiers
- record categories
- opaque record identifiers
- associated-data encoding
- serialized field names and enum values
- migration identifiers

Translated labels must never be used as record types, identifiers, associated data, lookup keys, or migration conditions. Changing the selected GUI language must not re-encrypt, migrate, rename, or rewrite vault data.

The preferred GUI language may be stored as a separate non-secret application preference available before unlock. It must not be used as key-derivation input or as an authorization signal.

Localized vault warnings and confirmations fall back to complete reviewed English resources when a translation is unavailable. Missing text must never remove the consequence of creating, opening, locking, exporting from, or deleting a vault.

See [Localization and Multilingual GUI](LOCALIZATION.md).

## Vault File Creation and Opening

Creating a vault must fail when a file already exists at the selected path. unpwn must not overwrite an existing vault or unrelated file implicitly.

Opening an existing vault uses a non-creating SQLite mode. A missing file therefore remains a missing-file error rather than silently creating an empty database that could be mistaken for the original vault.

## Stored Credentials

unpwn does not store old passwords.

The vault may store newly generated credentials while recovery is in progress so that they can be entered, resumed, and exported to an established password manager.

A credential record must track whether it was:

- generated
- used in a recovery action
- confirmed by the user
- exported
- deleted

Credential lifecycle values are canonical. The UI localizes their descriptions at display time.

## Secret Handling

- Never write credentials or vault keys to logs, telemetry, exception messages, audit events, or localization diagnostics.
- Disable or sanitize diagnostic output for objects that may contain secrets.
- Keep decrypted secrets in memory only for the shortest practical period.
- Treat memory clearing in managed .NET as best effort rather than a complete guarantee.
- Clipboard use must be explicit. Where supported, unpwn should clear copied credentials after a short user-visible interval.
- Crash reports must exclude vault content and secrets.

A decrypted vault record is exposed through a disposable object rather than a permanently public mutable byte array. Disposing the record overwrites its managed buffer on a best-effort basis and makes further access fail. Callers must use the narrowest possible lifetime, normally with `using`, and must not copy decrypted values into longer-lived strings or view-model state unless the specific user action requires it.

Disposing a managed buffer reduces accidental retention but is not a forensic memory-erasure guarantee. Copies made by callers, runtimes, operating systems, debuggers, or compromised processes remain outside this guarantee.

Resource formatting or missing-key diagnostics must never include a decrypted value or any formatting argument that may contain vault data.

## Export Safety

Plaintext export formats such as CSV are inherently sensitive.

Before export, unpwn must:

- clearly identify that the output contains credentials
- require an explicit destination
- warn about synchronized folders and cloud-backed download locations
- recommend immediate import into a password manager
- offer to delete the plaintext export after confirmed import

These warnings must remain explicit in every shipped language and fall back to the reviewed English resources.

Deletion cannot guarantee forensic erasure on modern filesystems or storage devices. The UI must not claim otherwise.

Machine-readable export formats use deterministic field names and value encodings defined by the target format. They do not change with the selected GUI language unless a format explicitly requires localized human-readable output.

## Locking and Lifecycle

The vault should lock when:

- the user requests it
- the application is inactive for a configurable period
- the operating system session is locked, where detectable
- the application exits normally

After recovery, the user may:

- export credentials and delete the vault
- retain the encrypted vault as a recovery record
- remove credential records while retaining non-secret audit history

## Out of Scope

The vault does not protect against:

- active malware on the device
- keyloggers
- screen capture by a privileged attacker
- browser-session theft while the browser is in use
- compromise of the user's vault password outside unpwn
- misleading third-party or user-supplied translations outside the reviewed application resources
