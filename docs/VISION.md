# unpwn Vision

## Purpose

unpwn helps a private user regain control of their digital identity after a suspected account compromise.

The hard part after phishing, an infostealer, session theft, or a similar incident is often not knowing **what to do first and what is still unresolved**. unpwn provides that structure without asking the user to model a technical account graph.

## Product promise

unpwn should guide the user through one understandable recovery process:

1. start on a trusted device;
2. create or resume an encrypted local recovery workspace;
3. record affected accounts and relevant observations;
4. categorize accounts as email, critical, unknown, or non-critical;
5. recommend a deterministic recovery order and explain why;
6. guide provider recovery actions one step at a time;
7. keep blocked, failed, lost-access, and unresolved-risk states visible;
8. export newly generated credentials to an established password manager;
9. finish with an explicit review instead of a misleading "all secure" claim.

The user should understand what unpwn proposes, why it matters, and what still requires their action.

## Product boundaries

unpwn is not:

- an antivirus or malware scanner;
- a tool that proves a device or account is safe;
- a replacement for a password manager;
- an autonomous account-recovery bot;
- a CAPTCHA, MFA, identity-verification, or ownership-check bypass;
- a general enterprise incident-response platform in the MVP.

Automation may assist recovery, but security-relevant decisions and external provider actions remain visible to the user.

## Principles

- **Local-first:** recovery data stays on the user's device in the MVP.
- **Human control:** sensitive actions require visible user participation and confirmation.
- **No false assurance:** progress never hides blocked work or unresolved risk.
- **Explainable ordering:** email and critical accounts are prioritized for understandable reasons.
- **Open source:** security-sensitive behavior is reviewable.
- **Platform-neutral core:** Windows is the first target, but recovery logic remains portable.
- **Language-neutral semantics:** changing the UI language must not change recovery logic, security decisions, or persisted state.
- **Maintainable automation:** stable recovery workflows are more important than brittle website automation.

For the user-facing flow see [User Guide](USER_GUIDE.md). Technical boundaries are documented in [Architecture](ARCHITECTURE.md), [Threat Model](THREAT_MODEL.md), and [Recovery Workflows](RECOVERY_WORKFLOWS.md).
