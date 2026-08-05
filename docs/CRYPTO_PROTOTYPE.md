# Cryptographic Prototype

Issue #5 validates the Recovery Vault key hierarchy before the SQLite vault is implemented.
The prototype is intentionally small and lives in `Unpwn.Vault` so the platform-neutral recovery core remains independent of cryptography and storage adapters.

## Design validated

- A user-provided vault password is converted to UTF-8 bytes only for Argon2id derivation and is cleared afterwards on a best-effort basis.
- Argon2id derives a 256-bit key-encryption key from the vault password and a random 128-bit salt.
- A random 256-bit vault data key is generated separately from the password-derived key.
- AES-256-GCM wraps the vault data key with a fresh 96-bit nonce and a 128-bit authentication tag.
- Sensitive vault records are encrypted with the vault data key using AES-256-GCM, a fresh 96-bit nonce per record encryption, and a 128-bit authentication tag.
- Record type, record identifier, and schema version are bound as AES-GCM associated data so encrypted bytes cannot be moved between records without detection.
- Failed unwrap or decrypt operations surface `CryptographicException`; callers must route such failures through the application's secret-safe diagnostics boundary before exposing them to logs or UI.

## Prototype parameters

`Argon2idParameters.Interactive` currently uses 64 MiB of memory, 3 iterations, and parallelism 2. The validator rejects settings below 19 MiB, fewer than 2 iterations, or non-positive parallelism so tests cannot accidentally normalize weaker settings.

These values are prototype defaults, not a final product promise. Before production vault release, the team should benchmark unlock latency on the supported device class and version persisted parameter metadata for future migration.

## Validated behavior

The unit tests cover:

- vault creation and data-key unwrap with the correct vault password
- rejection of a wrong vault password
- record encryption and decryption round trips
- ciphertext tamper detection
- associated-data binding for record identity metadata
- fresh nonce generation for repeated record encryptions
- rejection of unsafe Argon2id parameter choices

The prototype does not implement persistence, vault lifecycle, password changes, credential export, or operating-system unlock convenience. Those behaviors remain separate follow-up issues.
