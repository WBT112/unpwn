# Generated Credentials and Secure Export Core

## Purpose

unpwn may generate a new credential while guiding an account recovery. The generated credential is temporary recovery data stored in the encrypted Recovery Vault until the user exports it to an established password manager or deliberately deletes it.

This capability never accepts, imports, or stores an old password.

## Generation

`ICredentialPasswordGenerator` returns a temporary UTF-8 byte buffer generated with `RandomNumberGenerator`. The default policy creates 24 ASCII characters and includes lowercase, uppercase, digits, and symbols. Every selected character class is represented before the remaining positions are filled and the complete result is cryptographically shuffled.

The public generated-credential repository API contains no string parameter for entering an existing password. A generated secret is returned only through `CredentialSecretLease`, which zeroes its byte buffer on disposal.

## Encrypted persistence

Each generated credential is stored as an authenticated encrypted `generated-credential` record in the Recovery Vault. The encrypted record contains:

- opaque credential and account identifiers
- lifecycle timestamps and revision
- structured audit events with opaque operation identifiers
- the generated UTF-8 secret while it is retained

The secret is not placed in account notes, workflow labels, audit summaries, logs, notifications, or diagnostics.

## Stable reference

Workflow state refers to a generated credential only through `GeneratedCredentialReference`:

```text
CredentialId + AccountId
```

The reference contains no secret value. A caller must hold an unlocked vault and explicitly request a temporary secret lease to reveal or use the credential.

## Lifecycle

The encrypted metadata records:

- `GeneratedAt`
- `UsedAt`
- `ConfirmedAt`
- `ExportedAt`
- `ExportCount`
- `PasswordManagerImportConfirmedAt`
- whether import confirmation was deliberately postponed
- `PlaintextExportCleanupConfirmedAt`
- `DeletedAt`
- revision
- structured audit events

Lifecycle operations require an opaque operation ID. Repeating the same operation ID for the same event is idempotent and does not create duplicate audit entries or export counts.

Confirmation requires a prior recorded use. Deleted credentials cannot be revealed or changed.

The password-manager handoff is intentionally separate from file creation. A user may confirm or
postpone the import, revoke an incorrect import confirmation, and independently confirm cleanup of
the plaintext export. A deliberate repeat export reopens both handoff and cleanup state.

## Desktop presentation

The desktop credential screen supports generation for a selected account and opaque attachment to
password-change or password-reset workflow actions. Lists always show a concealed placeholder, not
the secret. Reveal lasts 15 seconds and clipboard ownership lasts 30 seconds. Navigation, vault
locking, and language changes clear both presentation states. Clipboard cleanup first verifies a
cryptographic hash of the current clipboard text so later user content is not erased.

Managed UI strings cannot be deterministically zeroed by .NET. They are therefore created only for
an explicit temporary reveal or clipboard operation, are never logged or persisted, and references
are dropped when the presentation state is cleared.

## Deletion boundary

Deleting a generated credential:

- zeroes the current in-memory secret buffer
- replaces the encrypted credential record with metadata that contains no secret
- retains the structured deletion audit event

This is temporary-record cleanup, not a forensic erasure guarantee. SQLite pages, WAL files, filesystem snapshots, storage-device behavior, and backups may retain prior ciphertext. The UI must not claim otherwise.

## Export formats

The core export service supports:

- generic CSV
- Bitwarden-compatible login CSV

Only explicitly selected credential references are loaded and written. Export requires explicit acknowledgement that the destination file is plaintext and may be exposed by synchronized, shared, network, or backed-up locations.

The destination must be selected explicitly, its parent directory must already exist, and an existing destination is never overwritten.

## Export write behavior

Exports use a temporary file in the destination directory:

1. validate all selected metadata and secret leases
2. create a new temporary file
3. write and flush the complete selected export
4. atomically move the temporary file to the final destination without overwrite
5. atomically mark all selected encrypted credential records as exported

If file creation succeeds but the encrypted lifecycle update fails, the result explicitly reports `StateUpdateFailedAfterFileCreation`. It never claims that no plaintext file exists.

A retry with an operation ID that is already recorded as exported does not create another file. A new operation ID represents a deliberate repeated export and increments the export count after success.

## CSV handling

The CSV writer:

- emits deterministic UTF-8 without relying on the selected GUI culture
- quotes commas, quotes, and line breaks
- writes the secret from the temporary byte lease rather than adding it to user notes or diagnostics
- contains only selected credential records

Machine-readable headers are stable and not localized.

## Testing

Tests cover:

- generation policy and selected character classes
- encrypted persistence and reopening
- lifecycle ordering and idempotent operations
- deletion and removal of revealable secret data
- atomic export-state rollback for multiple credentials
- explicit plaintext acknowledgement
- selected-only generic CSV export
- Bitwarden CSV output
- existing destination protection
- missing credentials preventing partial output
- file-created/state-update-failed reporting
- repeated operation handling

The credential UI provides reveal and clipboard timers, destination warnings, password-manager import confirmation, and post-import cleanup prompts. Completion preflight reads only credential metadata and reports separate counts for unexported credentials, unconfirmed password-manager imports, retained vault credentials, and pending plaintext cleanup; it never reads credential secret material.
