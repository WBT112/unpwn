# Guided Recovery Wizard

## Purpose

The wizard coordinates the complete recovery session. It does not implement vault cryptography, provider workflows, credential export, or reporting itself; it connects those application services through stable, language-neutral state.

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
recovery-plan
account-recovery
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

The post-intake gates are deterministic:

- the account-inventory step opens CSV import as the primary path after session creation;
- inventory cannot advance until at least one imported account exists, after which the assistant opens account-category triage;
- category triage shows the next unreviewed account and remaining count; after an explicitly confirmed email account the user may continue early because further review is optional but improves ordering;
- a user who genuinely has no email account can review all accounts or deliberately use the assistant's Continue action; leaving the step never records an implicit category;
- account work is queued automatically as `Email`, `Critical`, `Unknown`, then `NonCritical`, with language-neutral identifiers as stable tie-breakers;
- the recovery approach is selected automatically from explicit account access and the repository workflow; no UI or wizard step asks for an internal recovery-path enum;
- recovery planning routes to outstanding account work first, then credential handoff, then completion preflight;
- returning from material account work recalculates the plan from the latest persisted projections;
- a successful completion preflight advances to final-report review, while the terminal outcome still requires explicit confirmation.

Blocked and failed actions, lost access, and unresolved risks are not hidden by wizard navigation. They remain visible in recovery execution and completion review. The removed account-dependency graph has no wizard gate or parallel planning authority.

## Safe resume

External work is never assumed to have succeeded because a browser opened, the user returned to unpwn, time passed, or the application restarted.

Sensitive interrupted work resumes at a safe review point. In particular, account recovery resumes through the recovery plan rather than assuming the last external action completed, and final-report work resumes through completion preflight.

Wizard state that belongs to an active recovery session is persisted only through the encrypted workspace boundary. See [Workspace Persistence](WORKSPACE_PERSISTENCE.md).

## Recommendations

The orchestrator exposes stable recommendation codes. The presentation layer explains them in the selected language. Account-specific ordering is derived from the recovery plan rather than from UI text.

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
