# Current Vault Format Limits

The current vault format accepts finite values for fields that control processing cost and stored record size.

- Argon2id memory: up to 256 MiB.
- Argon2id iterations: up to 10.
- Argon2id parallelism: up to 8.
- Record payload: up to 8 MiB per record.
- Record descriptors: up to 4096 per vault.

New vaults continue to use 64 MiB, three iterations, and parallelism two. Values outside the current format are rejected rather than migrated.

Tests cover these boundaries with small deterministic fixtures rather than high-memory stress runs.
