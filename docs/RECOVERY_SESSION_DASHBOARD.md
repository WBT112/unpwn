# Recovery Session Intake and Dashboard

This document defines the desktop recovery-session boundary implemented for the MVP dashboard.

## Purpose

The recovery session gives the user one encrypted, resumable workspace for an incident. It records structured observations, account-review summaries, lifecycle state, and the next recommended recovery step.

The session does not detect malware, determine whether an account was compromised, or certify that an incident is resolved. Recommendations remain advisory and explainable.

## Storage boundary

The recovery session is stored as an authenticated encrypted `recovery-session` record inside the currently unlocked Recovery Vault.

The encrypted record contains:

- a stable session identifier;
- the user-defined session name;
- optional incident description;
- language-neutral incident-indicator flags;
- active, paused, or archived lifecycle state;
- created and last-updated timestamps;
- revision number;
- language-neutral account dashboard summaries.

The record does not appear in the unencrypted recent-vault list. The shell receives only the current session display name after the encrypted record has been loaded successfully.

Serialized plaintext buffers are cleared after encrypted writes. Locking the vault removes the materialized session and dashboard snapshot from the application service.

## Incident intake

The intake accepts a session name, optional descriptive context, and structured observations:

- lost account access;
- unexpected password changes;
- unexpected MFA or recovery-setting changes;
- unknown active sessions or devices;
- a potentially compromised primary recovery channel;
- a potentially untrusted incident device.

All observations are optional. Skipping them does not block session creation.

The optional description is intentionally limited. It rejects URLs, credential-labelled values, reset links, tokens, MFA secrets, recovery codes, cookies, and long secret-like strings. This validation is a guardrail, not a general secret detector. The interface therefore explicitly instructs users not to enter credentials or browser state and requires acknowledgement before creating a session.

## Advisory emergency priority

A reported compromised recovery channel, or the combination of lost access and an unexpected MFA change, creates a high-impact advisory recommendation. The dashboard directs the user to review the primary recovery channel before dependent accounts.

This recommendation is derived only from user-provided structured observations. The application does not claim that the channel, account, or device was automatically detected as compromised.

## Dashboard semantics

The dashboard deliberately does not reduce recovery status to one percentage.

### Primary readiness signal

Critical-account readiness is shown before aggregate progress. A critical account counts as ready only when it is fully reviewed and has no recorded lost access, required-action blocker, required-action failure, or unresolved risk.

### Supporting metrics

The dashboard also displays:

- fully reviewed accounts compared with total accounts;
- weighted completion of applicable required actions;
- blocked required actions;
- failed required actions;
- unresolved risks;
- accounts with lost access;
- generated credentials awaiting export;
- exported credentials awaiting deletion.

A high weighted-progress value never hides the separate warning groups and is never labelled as a security verdict.

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

The encrypted completion record retains a structured report. Its machine-readable JSON export contains only opaque session/account identifiers, provider identifiers, canonical issue codes, timestamps, and aggregate counters. Account labels, login identifiers, URLs, user notes, credential identifiers, and credential secrets are excluded. Export uses an explicit destination and never overwrites an existing file. Ending or archiving never deletes the vault, credentials, or plaintext export files automatically and never claims forensic erasure.

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

- empty sessions and skipped optional incident details;
- secret-like description rejection before persistence;
- emergency advisory prioritization;
- mixed criticality, blockers, unresolved risks, lost access, and credential cleanup;
- critical readiness kept separate from weighted progress;
- encrypted create and reload behavior;
- corrupted records that are not overwritten;
- pause, resume, archive, and lock-memory boundaries;
- runtime language changes without semantic changes;
- direct warning navigation with account and action targets;
- localization-resource parity and XAML presentation conventions.
