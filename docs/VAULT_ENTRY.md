# Trusted Device and Vault Entry

The desktop application places a trusted-device decision in front of every new or resumed recovery flow. This boundary is advisory rather than diagnostic: unpwn cannot detect malware or prove that a device is safe.

## Entry sequence

1. The application explains its purpose and limitations.
2. The user answers whether the current device is trusted: `Trusted`, `NotTrusted`, or `Unsure`.
3. `NotTrusted` and `Unsure` lead only to non-sensitive safety guidance and a terminal stop.
4. Only `Trusted` permits creation or opening of a recovery vault.
5. After the vault is available, the guided recovery wizard continues at a conservative safe review point.

The lifecycle service enforces this sequence independently of the view model. A direct call to create or open a vault is rejected unless the language-neutral wizard state is at `vault-entry` with a confirmed trusted-device decision.

## Pre-vault state

Before a vault is successfully created or opened:

- the trusted-device answer and current wizard step exist only in memory;
- no incident, account, credential, or recovery-progress data is collected;
- `NotTrusted` and `Unsure` cannot be paused or resumed later;
- no recovery vault file is created or unlocked.

Language selection is available before the safety gate, but localized text is never stored as canonical wizard state.

## Creating a vault

A new vault requires:

- an explicitly selected local file;
- a user-defined password and matching confirmation;
- acknowledgement that a forgotten vault password cannot be recovered;
- a minimum length check without arbitrary character-composition rules.

Pasting from a password manager remains supported. Password fields are concealed by default, and temporary reveal automatically ends. The password is never written to recent-vault metadata, logs, audit events, error messages, or persisted application settings.

Creation uses the existing encrypted SQLite vault implementation with Argon2id key derivation and AES-256-GCM authenticated record encryption. The file is not overwritten when it already exists.

## Opening and unlocking

Opening an existing vault performs authenticated vault unlock before any recovery content is shown. The presentation layer receives stable, secret-safe result codes rather than source exceptions.

The user-facing failure for an authentication or integrity error intentionally does not distinguish among:

- a wrong vault password;
- modified or corrupted authentication metadata;
- an encrypted wizard record that cannot be authenticated or deserialized safely.

This avoids exposing whether particular records exist.

## Wizard persistence and safe resume

After successful vault access, unpwn stores a compact language-neutral wizard state as an encrypted `recovery-session` record with an opaque GUID record identifier.

The record contains only stable state such as:

- wizard identifier;
- current and safe-resume step identifiers;
- lifecycle status;
- revision and update timestamp.

Before an explicit or inactivity lock, the state machine records a conservative resume point. For example, interruption during account recovery returns to the recovery overview, and interruption during final-report work returns to completion preflight. Unlocking never treats an external provider action as completed automatically.

## Locking and inactivity

The vault can be locked explicitly from the global shell. The desktop also tracks keyboard and pointer activity:

- a warning is shown after the configured inactivity threshold;
- the vault locks after the final threshold;
- renewed activity before locking clears the warning;
- locking clears password inputs and temporary reveal state from the presentation layer as far as practical;
- account names, progress, incident details, notes, and generated credentials are not shown while locked.

Managed-memory clearing remains best effort. The underlying vault disposes or clears its in-memory key material according to the vault-security design.

## Vault password changes

Changing the vault password requires the current password and a confirmed new password. The vault re-wraps the existing data key; encrypted records are not exported to plaintext or re-created through the GUI.
Leaving the password-change screen clears the current, new, and confirmation fields and ends any
temporary password reveal before the cached screen can be opened again.

A failed current-password check leaves the vault and the existing password unchanged.

## Recent vault references

Recent-vault metadata is convenience data stored outside the encrypted vault. It is deliberately limited to:

- the local vault path;
- a display name derived from the filename;
- the last-opened timestamp.

It contains no vault password, account name, session name, recovery state, note, or credential value.

Removing a recent reference does not delete the vault file. Deleting a vault file is a separate destructive action with a separate confirmation. The UI does not claim that normal file deletion provides forensic erasure.
Recent-vault paths are compared case-insensitively on Windows and case-sensitively on non-Windows
platforms.

## Error boundary

Expected failures are mapped to stable codes such as:

- invalid input;
- file already exists;
- file not found;
- access denied;
- unsupported vault version;
- authentication or integrity failure;
- general safe I/O failure.

Raw exception messages, database contents, paths embedded in exceptions, stack traces, passwords, and decrypted records are not displayed as operation errors.

## Architecture boundaries

- `Unpwn.Core` owns the language-neutral wizard state machine.
- `Unpwn.Application` exposes the wizard orchestrator.
- `Unpwn.Vault` owns cryptography and encrypted storage.
- `Unpwn.App.Services` coordinates vault lifecycle, safe wizard persistence, recent references, and inactivity policy.
- `Unpwn.App.Presentation` owns localized state, validation, and commands.
- Avalonia code-behind is limited to native file-picker and desktop-input integration.

## Test coverage

Automated tests cover:

- refusal to create a vault without the trusted-device gate;
- `NotTrusted` and `Unsure` terminal paths;
- password validation, clearing, and reveal timeout;
- real vault creation, locking, inactivity warning, inactivity locking, and unlocking;
- conservative wizard resume;
- password re-wrapping and rejection of the old password;
- recent metadata without password values;
- separation of recent-reference removal from file deletion;
- English and German resource parity, pseudo-localization, XAML resource conventions, and synthetic-secret scanning.
