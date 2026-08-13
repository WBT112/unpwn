# Account Inventory and Category Triage

The account inventory is the encrypted, language-neutral source of truth for the accounts in one recovery session. The normal review deliberately asks one simple question per account: is it **Email**, **Critical**, **Not critical**, or **Unknown**?

## Stored account state

The encrypted `account-state` record contains:

- opaque account and recovery-session identifiers;
- provider or service identifier;
- recognizable account name, login identifier, and optional HTTP/HTTPS URL without embedded URL credentials;
- the locally suggested category and classification-catalog version;
- the user's optional explicit category and the inventory revision at which it was confirmed;
- inventory revision and timestamps.

An explicit user category always wins over a catalog suggestion. Explicit `Unknown` is distinct from an account that has not been reviewed. Passwords, recovery codes, MFA secrets, reset links, and cross-account planning graphs are not part of this model.

Materialized account data is cleared when the vault locks. Only the current schema is accepted; a structurally incompatible record fails closed as corrupted and is not overwritten.

## Local classification catalog

`RepositoryAccountClassificationCatalog` is versioned, repository-controlled, deterministic, and offline-only. It matches stable provider identifiers and safe HTTP/HTTPS host names already present in imported account data. It does not inspect arbitrary page content and performs no network lookup.

The catalog contains more than 100 common email domains and aliases, plus curated critical services such as financial/payment, commerce, health/insurance, government/identity, communications, password-manager, and identity-provider accounts. It also contains conservative not-critical patterns. Anything not matched remains `Unknown`.

The catalog only proposes **when** an account should be handled. Provider workflow definitions independently decide **how** recovery works. A catalog entry cannot select a provider action, change recovery execution state, or prove control of an account.

## Triage flow

The account screen shows a recognizable identity, current suggestion or explicit choice, one category selector, and **Save and review next**. Saving advances to the next unreviewed account automatically.

Once at least one account is explicitly categorized as `Email`, the user can return to the assistant and continue immediately; reviewing remaining accounts is optional but improves ordering. On resume, the screen shows the remaining count and keeps the next unreviewed account selected. If the user genuinely has no email account, reviewing all accounts permits continuation without one. The assistant also remains a deliberate exit from category triage; navigation itself never silently records a category.

Account removal requires the normal destructive confirmation.

## Recovery queue boundary

The normal recovery queue is derived automatically from effective categories in exactly this order:

1. `Email`
2. `Critical`
3. `Unknown`
4. `NonCritical`

Within one category, the language-neutral provider identifier and then the opaque account identifier
are deterministic tie-breakers. UI culture, display text, incident warnings, browser state, and
incomplete category review never change this order. An unreviewed account keeps its conservative
catalog suggestion, including `Unknown`, until the user explicitly overrides it.

The category queue has no parallel cross-account planning authority. Workflow execution, blocked required actions, failed actions, lost access, and unresolved risks remain canonical in the recovery execution model and are never hidden by category triage.

## Persistence and testing

Inventory changes and their dashboard projection are persisted atomically. A failed write is not published as successful state. Tests cover catalog aliases and types, unknown fallback, explicit override precedence, exact category ordering across culture changes and restart, incomplete triage, category revision persistence, early exit/resume guidance, incompatible-record failure, lock clearing, import integration, and language-independent semantics.

See [CSV Import](IMPORT.md), [Workspace Persistence](WORKSPACE_PERSISTENCE.md), [Integrated Recovery Flow](RECOVERY_WIZARD.md), and [Testing Strategy](TESTING.md).
