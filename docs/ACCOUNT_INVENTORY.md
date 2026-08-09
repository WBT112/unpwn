# Account Inventory and Recovery Planning

The account inventory is the encrypted, language-neutral source of truth for the accounts that belong to a recovery session. It connects the reviewed CSV import and manual account entry to the guided recovery plan without storing imported old passwords or treating inferred relationships as facts.

## Encrypted persistence boundary

The desktop application stores one authenticated `account-state` record in the currently unlocked recovery vault. The plaintext record contains only:

- an opaque recovery-session identifier,
- account identifiers,
- provider or service identifiers,
- account names, login identifiers, and optional HTTP/HTTPS account URLs,
- priorities,
- explicit role decisions,
- dependency types and opaque account references,
- override reasons,
- revision and update timestamps.

The record does not contain imported old passwords. The materialized inventory is removed from presentation and service state when the vault locks. An unreadable inventory record fails closed and is not silently replaced.

## Manual account entry

An account requires:

- a provider or service name, and
- either an account name or a login identifier.

The account URL is optional and must be an absolute HTTP or HTTPS URL. The user assigns one of four inventory priorities: low, normal, high, or critical. These priorities are presentation-independent persisted enum values.

Removing an account requires confirmation. When another account references it, the confirmation states the dependency impact. The referencing dependency is deliberately retained as missing after removal so the risk cannot disappear silently.

## CSV review and import

The existing generic CSV pipeline remains responsible for parsing, mapping, password-column exclusion, row diagnostics, and duplicate detection. Issue #33 connects its reviewed candidates to encrypted inventory persistence.

The flow is:

1. Select and analyze a CSV file.
2. Explicitly exclude every detected password column.
3. Map service, account, login, and URL columns.
4. Create a preview using the current inventory as the existing-account duplicate set.
5. Review valid rows, row diagnostics, and duplicate candidates.
6. By default, keep the first occurrence in each CSV duplicate group, skip later matching rows, and skip candidates that already match an existing inventory account.
7. Optionally override the default and import duplicate CSV candidates as separate accounts.
8. Persist the reviewed candidates in the encrypted inventory.

Malformed rows remain separate diagnostics and do not prevent later valid rows from being reviewed. The import model has no password property, so excluded password values cannot become account fields, diagnostics, duplicate keys, or persisted inventory data.

## Identity and recovery roles

The inventory supports these roles:

- email mailbox,
- password manager,
- identity provider,
- recovery email,
- telephone recovery channel,
- organization-managed sign-in.

Keyword matching can create a `Suggested` role decision. A suggestion has no planning authority. It becomes canonical only when the user changes it to `Confirmed`. The user may also reject a suggestion, add a role directly, or remove a previously confirmed role.

A login identifier that resembles an email address is not sufficient evidence that the corresponding mailbox is controlled by the user.

## Dependencies

Dependencies state that one account relies on another account for a specific reason:

- password reset,
- MFA,
- identity provider,
- recovery contact,
- password manager,
- organization-managed sign-in.

Missing dependency targets and cycles are blocking issues. The UI does not silently discard them.

When a new dependency would create a cycle, it is rejected unless the user supplies an explicit override reason. An overridden edge is removed from the scheduling constraint so work can continue, but it remains recorded as an unresolved risk. The UI and dashboard must not represent the underlying dependency as resolved.

## Deterministic recovery plan

The plan is recalculated after every persisted inventory change. For the same inventory and incident indicators, the order is deterministic.

The planner considers:

1. non-overridden dependencies,
2. confirmed recovery and identity roles,
3. incident indicators involving compromised recovery channels or lost access,
4. account priority,
5. stable provider and account identifiers as tie-breakers.

Plan items use language-neutral statuses and reason codes. Localized labels are presentation only and never control sorting or transitions.

The initial account-management slice distinguishes:

- ready now,
- planned after dependencies,
- blocked by a missing dependency,
- blocked by a dependency cycle.

Issue #34 will add workflow-action state and feed action outcomes back into the same orchestration boundary.

## Dashboard integration

After each inventory change, the service updates the recovery-session dashboard summaries:

- inventory priority maps to dashboard criticality,
- missing dependencies and cycles remain blocked work,
- dependency overrides remain unresolved risk,
- dependency depth and waiting account identifiers remain language-neutral navigation data.

No account name, login identifier, URL, role, or plan detail is available while the vault is locked.

## Testing expectations

Automated tests cover:

- suggestion versus explicit role confirmation,
- deterministic dependency-root and recovery-channel ordering,
- missing dependencies and cycles,
- override reasons and retained unresolved risk,
- keep-first duplicate handling and explicit separate-account import,
- password-column exclusion and persisted-record scanning,
- encrypted record reload and lock clearing,
- dashboard synchronization,
- filtering, search, and runtime localization without semantic changes.
