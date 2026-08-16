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

`RepositoryAccountClassificationCatalog` is versioned, repository-controlled, deterministic, and offline-only. Runtime classification reads only embedded repository data and the provider identifier / safe HTTP or HTTPS host already present in the imported account record. It performs no network lookup and sends no account inventory information to a classification service.

The catalog is modeled as canonical provider/service records. Every record has a stable ID, human-reviewable name, recovery category, one or more normalized domains, optional provider-ID aliases, and a provenance ID. Provider counts therefore count records, not domains or regional aliases. Multi-domain families such as Outlook or Yahoo remain one curated record even when they own many domains.

The checked-in catalog enforces at least:

- 100 canonical `Email` provider/service records;
- 1,000 canonical `Critical` provider/service records;
- 1,000 canonical `NonCritical` provider/service records.

Curated records cover important provider families and retain explicit provider-ID aliases used by existing imports. Broader coverage comes from a pinned, repository-vendored subset of the Université Toulouse 1 Capitole web-categorization data via the normalized `cbuijs/ut1` mirror. The mapped source categories are `webmail` → `Email`, `bank` → `Critical`, and `press` → `NonCritical`. Source revision, license/attribution, selection rules, record targets, collision handling, and the update procedure are documented in `src/Unpwn.Core/Data/AccountClassification/README.md`.

Source lists are advisory data and can contain categorization mistakes. Curated records have first claim on their domains; remaining source ingestion uses deterministic precedence `Email` → `Critical` → `NonCritical`. A final catalog domain is owned by exactly one canonical record. Unmatched services remain `Unknown`, and a user's explicit override always wins over any suggestion.

Domain matching is case/culture independent and normalizes internationalized host names to ASCII IDNA form before lookup. The embedded source loader is bounded by file size, line length, and record count. Invalid, duplicate, overlong, or oversized embedded data fails catalog construction instead of silently broadening classification.

The catalog only proposes **when** an account should be handled. Provider workflow definitions independently decide **how** recovery works. A catalog entry cannot select a provider action, change recovery execution state, or prove control of an account. Priority metadata and reviewed provider-navigation/automation metadata remain separate trust boundaries.

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

Inventory changes and their dashboard projection are persisted atomically. A failed write is not published as successful state. Tests cover catalog record minimums and uniqueness, curated aliases versus canonical counts, representative global/German/European providers, culture-independent classification, provenance, catalog-produced `Unknown`, rejection of explicit `Unknown`, valid user overrides, clearing an override back to the automatic suggestion, exact category ordering across culture changes and restart, incomplete triage, category revision persistence, import integration, localization, incompatible-record failure, and lock clearing.

See [CSV Import](IMPORT.md), [Workspace Persistence](WORKSPACE_PERSISTENCE.md), [Integrated Recovery Flow](RECOVERY_WIZARD.md), and [Testing Strategy](TESTING.md).
