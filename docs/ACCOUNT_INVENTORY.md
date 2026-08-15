# Account Inventory and Category Triage

The account inventory is the encrypted, language-neutral source of truth for the accounts in one recovery session. The normal review deliberately asks the user to choose one real recovery category when a decision is needed: **Email**, **Critical**, or **Not critical**. `Unknown` is not a user category. It is the system's unresolved state when the offline catalog cannot classify an account with sufficient confidence.

## Stored account state

The encrypted `account-state` record contains:

- opaque account and recovery-session identifiers;
- provider or service identifier;
- recognizable account name, login identifier, and optional HTTP/HTTPS URL without embedded URL credentials;
- the locally suggested category and classification-catalog version;
- the user's optional explicit category and the inventory revision at which it was confirmed;
- inventory revision and timestamps.

An explicit user category always wins over a catalog suggestion. The classifier may store `SuggestedCategory = Unknown`, but `ConfirmedCategory = Unknown` is invalid and fails model/application validation. A user who is not ready to decide leaves the account unresolved instead of manufacturing an `Unknown` confirmation. Passwords, recovery codes, MFA secrets, reset links, and cross-account planning graphs are not part of this model.

Materialized account data is cleared when the vault locks. Only the current schema is accepted; a structurally incompatible record, including obsolete development data with an explicit `Unknown` confirmation, fails closed as corrupted and is not silently reinterpreted.

## Local classification catalog

`RepositoryAccountClassificationCatalog` is versioned, repository-controlled, deterministic, and offline-only. It matches stable provider identifiers and safe HTTP/HTTPS host names already present in imported account data. It does not inspect arbitrary page content and performs no network lookup.

The catalog contains more than 100 common email domains and aliases, plus curated critical services such as financial/payment, commerce, health/insurance, government/identity, communications, password-manager, and identity-provider accounts. It also contains conservative not-critical patterns. Anything not matched remains `Unknown` and therefore needs user review.

The catalog only proposes **when** an account should be handled. Provider workflow definitions independently decide **how** recovery works. A catalog entry cannot select a provider action, change recovery execution state, or prove control of an account.

## Triage flow

Known automatic suggestions are immediately usable for recovery ordering and do not require confirmation. Accounts whose suggestion is `Unknown` are shown as **Needs review / Not automatically recognized**. Their category selector contains only `Email`, `Critical`, and `NonCritical`; no category is preselected until the user makes a real decision.

A valid user choice records the explicit category and confirmation revision. The user can later remove that override with **Use automatic category**. Removing an override restores the current catalog suggestion; if that suggestion is `Unknown`, the account returns to the unresolved Needs-review state.

The user may continue recovery while unresolved accounts remain. Deferring a category decision never writes an `Unknown` confirmation. Account removal requires the normal destructive confirmation.

## Recovery queue boundary

The normal recovery queue is derived automatically from effective categories in exactly this order:

1. `Email`
2. `Critical`
3. `Unknown`
4. `NonCritical`

Within one category, the language-neutral provider identifier and then the opaque account identifier
are deterministic tie-breakers. UI culture, display text, incident warnings, browser state, and
incomplete category review never change this order. An unresolved account keeps its conservative
catalog suggestion, including `Unknown`, until the user explicitly chooses a real category.

The category queue has no parallel cross-account planning authority. Workflow execution, blocked required actions, failed actions, lost access, and unresolved risks remain canonical in the recovery execution model and are never hidden by category triage.

## Persistence and testing

Inventory changes and their dashboard projection are persisted atomically. A failed write is not published as successful state. Tests cover catalog-produced `Unknown`, rejection of explicit `Unknown`, valid user overrides, clearing an override back to the automatic suggestion, exact category ordering across culture changes and restart, incomplete triage, category revision persistence, import integration, localization, incompatible-record failure, and lock clearing.

See [CSV Import](IMPORT.md), [Workspace Persistence](WORKSPACE_PERSISTENCE.md), [Integrated Recovery Flow](RECOVERY_WIZARD.md), and [Testing Strategy](TESTING.md).
