# Workspace Persistence and Interrupted Work

## Purpose

The recovery workspace contains several encrypted records that represent one user-visible state. A successful UI operation must not persist one part while reporting that the overall operation failed.

This document defines the persistence boundary established before guided provider workflow execution.

## Atomic encrypted record batches

`RecoveryVault.UpsertRecords` encrypts every supplied record and writes the resulting ciphertext rows in one SQLite transaction.

The batch contract:

- requires at least one record
- validates every repository-controlled record descriptor before writing
- rejects duplicate record type and identifier pairs before opening the write transaction
- uses a fresh authenticated-encryption nonce for every record
- commits all records together or rolls the complete batch back
- never returns plaintext or source exception details to presentation code

The single-record API delegates to the same implementation.

## Prepared state updates

Application services prepare immutable next states before persistence. A prepared update contains:

- the proposed language-neutral state
- its encrypted record descriptor
- serialized plaintext held only until the write completes
- the revision expected by the caller

The service follows this order:

1. validate the requested transition
2. calculate the proposed next state
3. serialize it into a temporary buffer
4. perform the atomic encrypted write
5. verify that the in-memory revision still matches
6. publish the new state and notify the UI
7. zero the temporary serialized buffer

An I/O failure therefore leaves both the persisted state and the materialized presentation state unchanged.

## Current atomic groups

The following operations are committed as one encrypted batch:

- recovery-session creation and the matching wizard transition from incident intake to account inventory
- recovery-session pause, resume, and archive together with the matching wizard lifecycle transition
- account-inventory mutation together with the derived recovery-dashboard account projection

Dashboard counters and recommendations are projections. The account inventory remains the canonical source for account metadata, confirmed roles, priorities, and dependencies.

## Concurrency and revisions

A shared `WorkspaceMutationCoordinator` serializes recovery-session and account-inventory mutations. Prepared session and wizard updates carry expected revisions. A prepared update is not published when another mutation changed the state before commit.

Commands remain responsible for rejecting duplicate execution at the presentation boundary. The persistence boundary provides an additional consistency guard; it does not turn an external provider action into an idempotent operation automatically.

## Vault lock and reload

Vault-triggered workspace loads are serialized. A newer lock or unlock event cancels an older pending load. After a lock:

- materialized session and inventory data are cleared
- a previous load is not allowed to republish decrypted data
- the application returns to the locked presentation boundary

An unexpected load failure while the vault remains unlocked is represented as `LoadFailed`. It is not misreported as a locked vault, and the application must not treat the workspace as empty or overwrite the affected encrypted records.

## Recovery after interruption

After process interruption or an unsuccessful write:

- SQLite transaction rollback prevents a partial encrypted batch
- startup reload validates each persisted state before publishing it
- mismatched session identifiers or invalid revisions fail closed
- dashboard projections can be recalculated from the canonical inventory
- no browser handoff or prior in-progress state is interpreted as external action completion

All encrypted workspace-record writes pass through one presentation-facing persistence monitor. It
publishes language-neutral `Saving`, `Saved`, `Retrying`, `SaveFailed`, and `Canceled` states. The shell
renders those states with text and a symbol, and maps access-denied, I/O, version-incompatibility, and
locked/conflict failures to static localized guidance. A failed operation is never reported as saved.
The monitor retains neither plaintext nor an executable retry closure: retry is an explicit repeat of
the original guarded command, so existing operation IDs and revision checks continue to prevent
duplicate state transitions and audit events.

A repository-controlled run marker records only that the process is active. If it remains after an
abnormal exit, the next launch displays a dismissible warning and loads persisted state through the
normal validating services. It does not persist workflow details and does not infer provider success.
A last-chance process boundary records a secret-safe diagnostic and immediately discards the unlocked
vault key where the runtime still permits cleanup. Normal shutdown removes the marker only after the
vault and workspace services have been disposed.

## Testing

Tests cover:

- successful multi-record encrypted writes
- duplicate keys rejected before persistence
- an injected failure on a later record rolling back earlier writes
- staged state not becoming visible before successful persistence
- revision conflicts
- cancellation of superseded reloads
- disk-full, access-denied, locked/conflict, and version-incompatibility status mapping
- interrupted-write cancellation without a false saved state
- explicit retry after a failed write
- abnormal-exit marker detection and crash-boundary locking
- secret-marker scans of test artifacts

Future workflow-state tests must inject failures between every logically grouped state update and verify that reload produces either the complete previous state or the complete next state, never a mixture.
