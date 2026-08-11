# Account Inventory and Recovery Planning

The account inventory is the encrypted, language-neutral source of truth for the accounts in one recovery session. It connects reviewed account data to roles, dependencies, and deterministic recovery ordering.

## Stored account state

The encrypted `account-state` record contains the recovery-session reference and reviewed account information such as:

- opaque account identifiers;
- provider or service identifiers;
- account name, login identifier, and optional HTTP/HTTPS account URL without embedded URL credentials;
- priority;
- explicit role decisions;
- dependencies and override reasons;
- revision and timestamps.

Old passwords are not part of this model. Materialized account data is cleared when the vault locks. Invalid or mismatched encrypted state fails closed rather than being silently replaced.

## Account management

An account needs a service/provider and either an account name or login identifier. The user can assign low, normal, high, or critical priority.

Removing an account requires confirmation. Dependencies that pointed to the removed account remain visible as missing dependencies so the risk does not disappear silently.

CSV parsing, password-column exclusion, duplicate rules, and import diagnostics are defined only in [CSV Import](IMPORT.md).

## Guided review and advanced details

The normal account-review journey presents the canonical inventory as questions about accounts the user recognizes:

- review imported accounts and add anything missing;
- confirm or reject each inferred identity or recovery role explicitly;
- describe recovery dependencies as “Can this account be recovered using that account?”;
- explain possible duplicates, missing dependencies, cycles, and overrides in user language.

The guided review is a presentation of `AccountInventoryState`; it is not a second inventory or recovery state machine. Role answers and dependency choices use the same `IAccountInventoryService` mutations as the advanced editor. The recovery wizard's `AccountsRequired` and `RoleConfirmationRequired` gates remain authoritative.

Detailed provider metadata, priority, role removal, dependency removal, and cycle overrides remain available behind **Advanced account details**. An override still requires a written reason and continues to appear as an unresolved risk. Switching between guided and advanced views must therefore round-trip immediately through the same encrypted canonical state.

## Roles

Supported recovery and identity roles include:

- email mailbox;
- password manager;
- identity provider;
- recovery email;
- telephone recovery channel;
- organization-managed sign-in.

A suggested role has no planning authority until the user confirms it. A login identifier that looks like an email address is not proof that the corresponding mailbox is controlled by the user.

## Dependencies

An account may depend on another account or recovery channel for:

- password reset;
- MFA;
- identity-provider access;
- recovery contact;
- password-manager access;
- organization-managed sign-in.

Missing targets and dependency cycles are blocking conditions.

A cycle may be overridden only with an explicit reason. The overridden edge can stop blocking scheduling, but the underlying dependency remains recorded as an unresolved risk.

## Recovery plan

The plan is recalculated after persisted inventory changes. For the same state, the result must be deterministic.

Ordering considers:

1. non-overridden dependencies;
2. confirmed recovery and identity roles;
3. relevant incident indicators such as lost access or a compromised recovery channel;
4. user-assigned priority;
5. stable identifiers as tie-breakers.

Plan items use language-neutral statuses and reason codes. Presentation text never controls ordering.

Typical planning states distinguish work that is ready now, planned after dependencies, blocked by a missing dependency, or blocked by a cycle. Material workflow outcomes are fed back through the account-recovery execution boundary and can change the next recommendation.

See [Account Recovery Execution](ACCOUNT_RECOVERY_EXECUTION.md) and [Recovery Session and Dashboard](RECOVERY_SESSION_DASHBOARD.md).

## Persistence rule

Inventory changes and the dashboard projection that depends on them are persisted atomically. A failed write must not be published as successful state.

See [Workspace Persistence](WORKSPACE_PERSISTENCE.md).

Testing for role confirmation, dependencies, ordering, persistence, lock clearing, import integration, and language-independent semantics follows [Testing Strategy](TESTING.md).
