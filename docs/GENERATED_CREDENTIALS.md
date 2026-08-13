# Generated Credentials and Secure Export

## Purpose

unpwn may generate a new credential while guiding account recovery. Generated credentials are temporary recovery data held in the encrypted Recovery Vault until they are moved to an established password manager or deliberately deleted.

This capability never accepts, imports, or stores an old password.

## Generation and secret lifetime

`ICredentialPasswordGenerator` produces a temporary UTF-8 buffer using cryptographically secure randomness. The default policy creates 24 ASCII characters and includes the selected lowercase, uppercase, digit, and symbol classes before cryptographically shuffling the complete result.

The generated-credential repository exposes no API for storing an existing password. Plaintext is returned only through a disposable `CredentialSecretLease`, whose byte buffer is zeroed on disposal.

Managed UI strings cannot be deterministically zeroed by .NET. Therefore plaintext strings are created only for deliberate short-lived reveal/clipboard operations, are never logged or persisted, and references are dropped when presentation state is cleared.

## Encrypted persistence and stable references

Each generated credential is stored as an authenticated encrypted `generated-credential` record. The record contains opaque credential/account identifiers, lifecycle metadata, structured audit events, and the generated UTF-8 secret while it is retained.

Recovery execution refers to the secret only through `GeneratedCredentialReference`:

```text
CredentialId + AccountId
```

The reference contains no secret. Reading plaintext requires an unlocked vault and an explicit temporary lease.

The Recovery Browser resolves the reference attached to the current canonical recovery action. It must not select a credential merely because it is the newest record for an account.

## Lifecycle

Credential metadata tracks generation, use, confirmation, export, password-manager import confirmation/postponement, plaintext-export cleanup confirmation, deletion, revision, and structured audit events.

Lifecycle mutations use opaque operation IDs and are idempotent where specified. Confirmation requires prior recorded use. Deleted credentials cannot be revealed or mutated as active credentials.

File creation, password-manager import confirmation, and plaintext cleanup are deliberately separate states. A repeated deliberate export reopens the relevant handoff/cleanup state rather than pretending prior cleanup still applies.

## Desktop and Recovery Browser presentation

The normal credential UI keeps secrets concealed and offers explicit short-lived reveal and owned clipboard copy. Reveal lasts 15 seconds and clipboard ownership lasts 30 seconds. Vault lock and relevant navigation/session boundaries clear materialized presentation state. Clipboard cleanup verifies ownership before removing content so later user clipboard data is not erased accidentally.

When an active password-change/reset action has an attached credential, the Recovery Browser assistant reuses the same repository/lifecycle services for Reveal, Hide, Copy, Mark used, and Confirm working. Browser close clears materialized reveal state and requests owned-clipboard cleanup. Browser close itself does not mark the credential used or confirmed.

The in-browser guidance distinguishes the user's current provider credential from the generated
replacement. A generated password is not presented as valid for a required pre-change login and does
not become confirmed merely because it was revealed, copied, inserted, or submitted. Only the
existing explicit lifecycle confirmation after the provider change may confirm it.

Detailed browser/session/origin rules live in [Recovery Browser Security Boundary](RECOVERY_BROWSER.md) rather than being duplicated here.

## Provider-reviewed insertion

Automatic field insertion is not a generic password-form feature. Manual Reveal/Copy remains the safe default.

A repository-controlled insertion contract must match the exact provider, recovery action, content mode, expected origins, page evidence, and exact new-password/confirmation selectors. Unsupported/generic providers do not receive DOM insertion merely because a page contains password-like inputs.

Before any secret lease is opened, the current origin/page contract is inspected. MFA, CAPTCHA, email-link handoff, unexpected origin, missing/duplicated fields, or changed content stops assistance. A ready attempt then obtains a short-lived secret lease, immediately re-checks the exact contract, and fills only the reviewed fields.

The managed insertion path does not submit the provider form. Successful insertion may record the credential as `Used`; it never records it as `Confirmed` and never completes the recovery action.

The repository currently exposes automatic insertion only for the explicit synthetic-test contract. Real-provider and generic/manual workflows remain Reveal/Copy/manual entry unless a separate provider/action adapter is reviewed.

## Export boundary

The export core supports generic CSV and Bitwarden-compatible login CSV for explicitly selected generated-credential references.

Plaintext export requires an explicit acknowledgement and destination. The parent directory must exist and an existing destination is not overwritten.

The write lifecycle is:

1. validate selected metadata and secret leases;
2. create a temporary file in the destination directory;
3. write and flush the complete export;
4. atomically move it to the final destination without overwrite;
5. update encrypted credential lifecycle state.

The final move is the plaintext-file commit boundary. If lifecycle persistence fails after that move, unpwn reports that a plaintext file exists and still requires cleanup; it must not claim the export never happened.

CSV output is deterministic UTF-8, culture-independent, correctly quoted, and writes plaintext only from temporary secret leases. Machine-readable headers are stable and not localized.

## Deletion boundary

Deleting a generated credential removes revealable plaintext from the active encrypted record and retains only non-secret lifecycle/audit metadata as designed. This is application-level cleanup, not a forensic-erasure guarantee: prior ciphertext may remain in SQLite pages/WAL, filesystem snapshots, backups, or storage-device history.

## Testing

Tests cover generation policy, encrypted persistence/reopen, lifecycle ordering/idempotency, deletion, reveal/clipboard timers, lock/session clearing, clipboard cleanup failure, export commit boundaries, password-manager handoff, selected-only CSV output, existing-destination protection, missing-credential failure, synthetic reviewed browser insertion, late vault retrieval, browser stop conditions, no automatic submission/completion, and secret-artifact scanning.

See [Vault Security](VAULT_SECURITY.md), [Recovery Browser Security Boundary](RECOVERY_BROWSER.md), and [Testing Strategy](TESTING.md) for their canonical boundaries.
