# Integrated Recovery Flow

## Purpose

The recovery flow coordinates the complete session. It does not implement vault cryptography, provider workflows, credential export, or reporting itself; it connects those application services through stable, language-neutral state. There is no separate global wizard UI: each active workspace presents its own explanation and one primary continuation.

For the user-facing walkthrough, see [User Guide](USER_GUIDE.md).

## Canonical steps

```text
welcome
trusted-device-check
trusted-device-guidance
vault-entry
incident-intake
account-inventory
account-triage
recovery-overview
credential-export
completion-preflight
final-report
```

Visible labels are localized at the presentation boundary. Persisted step identifiers and transitions never depend on translated text.

## Trusted-device boundary

The trusted-device decision is the first mandatory security gate:

- `Trusted` may proceed to vault creation or unlock.
- `NotTrusted` and `Unsure` may show non-sensitive guidance and then stop.
- the decision is a user assertion, not proof that the device is malware-free;
- no vault is created or unlocked before the gate is accepted;
- pre-vault wizard state is transient.

## Step contracts

`RecoveryWizardStepCatalog` defines whether each step:

- requires an unlocked vault;
- may handle sensitive recovery data;
- may transition forward or backward;
- has a conservative resume point after pause, lock, restart, or interruption.

Generic navigation must not bypass the trusted-device gate, manufacture a vault context, or silently skip a required security step.

Session creation completes incident intake with a locally suggested, editable display name and only
the optional compromised-recovery-channel warning documented in
[Recovery Session and Dashboard](RECOVERY_SESSION_DASHBOARD.md). The active workspace owns the current
instruction and primary action; persistent shell chrome does not duplicate them. Opening a detail
route does not mark a step complete, and every guided transition is written to the encrypted vault
before the in-memory state changes.

`RecoveryNextUserTask` projects exactly one language-neutral next task from the canonical wizard,
session, inventory, execution, and credential state. The projection distinguishes an available action,
optional work that may continue, a blocked state with a concrete recovery action, and terminal
read-only review. Route changes are outputs of this projection and never inputs to recovery truth.

The post-intake gates are deterministic:

- the account-inventory step opens CSV import as the primary path after session creation;
- inventory cannot advance until at least one imported account exists, after which the import workspace offers **Review account categories**;
- category triage shows the next unreviewed account and remaining count; **Continue to recovery now** deliberately permits early continuation while further review remains optional;
- a user who genuinely has no email account can review all accounts or deliberately continue from the triage workspace; leaving the route never records an implicit category or advances the flow;
- account work is queued automatically as `Email`, `Critical`, `Unknown`, then `NonCritical`, with language-neutral identifiers as stable tie-breakers;
- the recovery approach is selected automatically from explicit account access and the repository workflow; no UI or wizard step asks for an internal recovery-path enum;
- the recovery overview routes to outstanding account work first, then credential handoff, then completion preflight;
- returning from material account work recalculates the next task from the latest persisted projections without a separate account-recovery wizard phase;
- a successful completion preflight advances to final-report review, while the terminal outcome still requires explicit confirmation.

Blocked and failed actions, lost access, and unresolved risks are not hidden by wizard navigation. They remain visible in recovery execution and completion review. The removed account-dependency graph has no wizard gate or parallel planning authority.

## Safe resume

External work is never assumed to have succeeded because a browser opened, the user returned to unpwn, time passed, or the application restarted.

Sensitive interrupted account work resumes through the recovery overview rather than assuming the last external action completed, and final-report work resumes through completion preflight.

Wizard state that belongs to an active recovery session is persisted only through the encrypted workspace boundary. See [Workspace Persistence](WORKSPACE_PERSISTENCE.md).

## Next user task

The application projection exposes stable task codes, task states, workspace targets, and optional
account/action identifiers. The presentation layer explains them in the selected language.
Account-specific ordering is derived from the persisted queue and execution state rather than UI text.
Unknown or obsolete serialized step identifiers fail closed; development states from the removed
`recovery-plan` and `account-recovery` phases are intentionally not migrated.

See [Account Inventory and Recovery Planning](ACCOUNT_INVENTORY.md) and [Account Recovery Execution](ACCOUNT_RECOVERY_EXECUTION.md).

## Terminal outcomes

The wizard distinguishes:

- stopped for device safety;
- cancelled;
- completed;
- archived;
- follow-up required.

A terminal state cannot be resumed as active work. Completion is explicit and may only follow the completion review flow.

Tests for wizard transitions, security gates, resume behavior, and language-neutral identifiers are part of the normal test suite. The authoritative testing rules are in [Testing Strategy](TESTING.md).
