# Coding Agent Instructions

unpwn is a local-first account-recovery assistant for users who suspect that their digital identity has been compromised. It is not an antivirus, password manager, general-purpose browser, or autonomous recovery bot.

## Read first

Start with the [documentation index](docs/README.md). For any non-trivial change, read the canonical document for the area you are touching rather than relying on summaries copied elsewhere.

At minimum, understand:

- [Vision](docs/VISION.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Threat Model](docs/THREAT_MODEL.md)
- [Testing Strategy](docs/TESTING.md)

Then read the relevant recovery, browser, vault, import, UI, localization, or persistence document.

## Non-negotiable product rules

- Guide recovery; do not claim to detect or remove malware.
- Do not guarantee that a device or account is safe.
- Keep sensitive external actions visible to the user.
- Browser observations are context, not recovery truth: navigation, redirect, form state, browser close/restart, or credential insertion never proves success.
- Required actions cannot be silently skipped.
- Blocked, failed, lost-access, and unresolved-risk states must remain visible.
- Do not bypass CAPTCHA, MFA, identity verification, rate limits, or ownership checks.
- Do not turn the Recovery Vault into a general password manager.
- Do not import normal browser profiles or add generic automatic password-field detection.

## Architecture rules

- `Unpwn.Core` is platform-neutral and must not depend on Avalonia, SQLite, browser engines, OS APIs, localization, or provider infrastructure.
- Use existing canonical domain and application services; do not create parallel recovery state machines in view models, browser adapters, or provider code.
- `Unpwn.App` is the composition root and owns presentation/localization and native Recovery Browser integration.
- Future provider-specific `ASSISTED` or `AUTOMATED` browser behavior must use the scoped `RecoveryBrowserActionAutomationContract`; do not add generic DOM automation or treat an adapter result as canonical recovery success.
- Canonical IDs, states, URLs, serialized values, audit types, and error codes remain language-neutral.
- Logically related security-sensitive state changes must preserve the documented atomic/idempotent persistence rules.

See [Architecture](docs/ARCHITECTURE.md) and [Recovery Browser Security Boundary](docs/RECOVERY_BROWSER.md).

## Security rules

Treat account, vault, credential, reset, token, browser, and recovery data as sensitive.

Never commit, log, expose, or place in test artifacts real credentials, reset links, cookies, MFA secrets, recovery codes, private keys, or personal account data. Use synthetic test data only.

Do not invent cryptographic primitives. Follow [Vault Security](docs/VAULT_SECURITY.md) and [Security Policy](SECURITY.md).

## UI and localization rules

- User-facing text comes from the presentation localization boundary.
- Never use translated text as control data.
- Security meaning must not rely on color alone.
- Layout must tolerate longer and pseudo-localized text.
- Recovery logic does not belong in code-behind; native UI/browser bridging may live there only behind the documented presentation boundary.

See [UI Foundation](docs/UI_FOUNDATION.md) and [Localization](docs/LOCALIZATION.md).

## Testing and completion

Follow [Testing Strategy](docs/TESTING.md). Normal PR tests use synthetic accounts and the local synthetic provider, not destructive live-provider flows.

Before declaring work complete:

- keep the change scoped;
- add or update regression tests;
- test blocked/failure paths where relevant;
- run the applicable full checks;
- verify no secrets or personal data were introduced;
- update the canonical documentation if behavior changed;
- report tests that were actually run and any checks that could not be run.

For contribution workflow and local commands, see [CONTRIBUTING.md](CONTRIBUTING.md).
