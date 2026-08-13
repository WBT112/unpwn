# Recovery Session Intake and Dashboard

This document defines the desktop recovery-session boundary implemented for the MVP dashboard.

## Purpose

The recovery session gives the user one encrypted, resumable workspace for an incident. It records structured observations, account-review summaries, lifecycle state, and the next recommended recovery step.

The session does not detect malware, determine whether an account was compromised, or certify that an incident is resolved. Recommendations remain advisory and explainable.

## Storage boundary

The recovery session is stored as an authenticated encrypted `recovery-session` record inside the currently unlocked Recovery Vault.

The encrypted record contains:

- a stable session identifier;
- the editable, locally suggested session name;
- language-neutral incident-indicator flags;
- active, paused, or archived lifecycle state;
- created and last-updated timestamps;
- revision number;
- language-neutral account dashboard summaries.

The record does not appear in the unencrypted recent-vault list. The shell receives only the current session display name after the encrypted record has been loaded successfully.

Serialized plaintext buffers are cleared after encrypted writes. Locking the vault removes the materialized session and dashboard snapshot from the application service.

## Session creation and guidance inputs

The session name is prefilled from the local operating-system user name as
`<username>-Recovery`. Unsuitable characters are normalized locally, the result is limited to the
existing 120-character session-name boundary, and a missing or unusable user name falls back to
`Recovery`. The suggestion remains editable. No directory or network service is queried, and no
additional identity metadata is persisted.

The only optional structured guidance inputs are:

- lost account access;
- possible third-party control of a primary email or recovery channel.

Both inputs have a direct canonical consumer. Lost access moves confirmed recovery-channel accounts
earlier in the account-inventory plan. Possible control of a primary recovery channel produces the
immediate `SecureRecoveryChannel` dashboard recommendation and also prioritizes confirmed recovery
channels in the plan. Both choices are optional; skipping them uses the normal recovery order.

Free-form incident narrative and structured choices without a recovery consumer are not collected.
The interface explains the effect before creation and requires acknowledgement that the answers guide
prioritization but do not prove compromise.

## Advisory emergency priority

A reported possibly controlled recovery channel creates a high-impact advisory recommendation. The dashboard directs the user to review the primary recovery channel before dependent accounts.

This recommendation is derived only from user-provided structured observations. The application does not claim that the channel, account, or device was automatically detected as compromised.

## Dashboard semantics

The dashboard deliberately does not reduce recovery status to one percentage.

### Primary account signal

The normal UI shows `x of n critical accounts handled` before aggregate progress. A critical account
counts as handled only when it is fully reviewed and has no recorded lost access, required-action
blocker, required-action failure, or unresolved risk. Internal readiness terminology is not exposed.

### Supporting metrics

The dashboard also displays:

- fully reviewed accounts compared with total accounts;
- simple recovery progress;
- blocked required actions;
- failed required actions;
- unresolved risks;
- accounts with lost access;
- generated credentials awaiting export;
- exported credentials awaiting deletion.

The simple percentage retains the deterministic weighted required-action calculation internally. The
normal UI does not expose that implementation detail. A high progress value never hides the separate
warning groups and is never labelled as a security verdict.

### Recommended next step

The recommendation uses stable reason codes and persisted recovery-order information. The current ordering favors:

1. a reported high-impact recovery-channel problem;
2. critical accounts with lost access;
3. critical blockers or failures;
4. unresolved risks;
5. critical accounts still needing review;
6. other accounts allowed by the recovery order;
7. generated-credential export and cleanup.

Each warning summary carries an optional account identifier and action identifier into the shell navigation context. Account and workflow screens can use this target without parsing localized text.

## Lifecycle

### Active

The session accepts normal recovery-state changes. Pause, archive, and completion review are explicit commands.

### Paused

The encrypted state remains available, but the dashboard requires an explicit resume before normal recovery-state changes. Locking a paused session preserves its conservative wizard resume step.

### Archived

Archiving requires a confirmation that names the session and explains the consequence. Archiving does not mark accounts as secure or complete. The encrypted session remains readable while state-changing recovery commands are disabled.

Completion remains a separate review flow. Opening completion review from the dashboard does not complete the session.

### Completed and follow-up required

Completion reloads the encrypted session and inventory projections and the generated-credential metadata before it builds the preflight. The preflight is revision-bound; if persisted state changes before confirmation, the user must review the refreshed result. Reviewing or cancelling does not mutate the session.

A clean explicit completion stores `Completed`. Open required work, lost access, unresolved roles or dependencies, incomplete credential handoff, and cleanup risk require a separate acknowledgement and store `FollowUpRequired`. A credential that has completed export, password-manager confirmation, and plaintext cleanup but remains encrypted in the vault is shown as a non-blocking retention warning; the terminal confirmation still states that it will not be deleted automatically. Archiving from the same review stores `Archived`. The session record and wizard terminal state are written atomically, after which account, workflow, import, and credential-mutation navigation is read-only.

The encrypted completion record retains a structured report. Its machine-readable JSON export contains only opaque session/account identifiers, provider identifiers, canonical issue codes, timestamps, and aggregate counters. Account labels, login identifiers, URLs, user notes, credential identifiers, and credential secrets are excluded. Export uses an explicit destination, writes through a same-directory temporary file, atomically publishes the completed report, cleans up cancellation remnants, and never overwrites an existing file. Ending or archiving never deletes the vault, credentials, or plaintext export files automatically and never claims forensic erasure.

## Unlock and restart behavior

After a vault is created or unlocked, the application loads the encrypted session record:

- no record produces the empty intake state;
- a valid record produces the dashboard;
- a malformed or structurally invalid record produces a corrupted state and is not overwritten;
- locking returns the service to the locked state and clears the materialized session;
- unlocking reloads the persisted session and the wizard resumes only at its recorded safe review point.

No external provider action is marked complete during reload, resume, navigation, or application restart.

## Localization boundary

Incident flags, lifecycle states, recommendation codes, alert kinds, account identifiers, action identifiers, and persisted wizard steps remain language-neutral.

English and German presentation resources translate labels and explanations at runtime. Changing the interface language does not change incident semantics, recovery ordering, stored state, or progress calculations.

## Test coverage

The test suite covers:

- safe local session-name suggestions, sanitization, editing, and neutral fallback;
- empty sessions and skipped optional guidance inputs;
- removed narrative and obsolete indicator fields rejected as incompatible current-schema data;
- a documented planning or dashboard effect for each retained guidance input;
- emergency advisory prioritization;
- mixed criticality, blockers, unresolved risks, lost access, and credential cleanup;
- critical accounts handled kept separate from the simple progress presentation;
- encrypted create and reload behavior;
- corrupted records that are not overwritten;
- pause, resume, archive, and lock-memory boundaries;
- runtime language changes without semantic changes;
- direct warning navigation with account and action targets;
- localization-resource parity and XAML presentation conventions.
