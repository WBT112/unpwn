# Security Policy

unpwn handles sensitive recovery state and newly generated credentials. Security and transparent failure are more important than convenience.

## Project status

unpwn is under active development and does not yet have a supported production release. Do not infer production security guarantees from unreleased builds.

## Important limits

Run unpwn on a device you reasonably trust. If the operating system is compromised, local software cannot reliably protect new passwords, browser sessions, screen contents, or decrypted recovery data.

unpwn does not:

- detect or remove malware;
- bypass MFA, CAPTCHA, identity verification, or account-ownership checks;
- guarantee that a provider restores access;
- guarantee that a completed workflow proves an account is secure;
- treat browser navigation, redirects, form state, credential insertion, or browser close as proof that a recovery action succeeded;
- guarantee forensic erasure of plaintext exports or temporary browser-profile data.

The detailed security assumptions and mitigations are documented in [Threat Model](docs/THREAT_MODEL.md). Vault cryptography and secret handling are documented in [Vault Security](docs/VAULT_SECURITY.md). The embedded provider-session/origin boundary is documented in [Recovery Browser Security Boundary](docs/RECOVERY_BROWSER.md).

## Sensitive data

Recovery data is local-first. Old passwords are not stored. Credentials, vault keys, reset data, MFA secrets, cookies, browser state, and other sensitive values must not appear in logs, telemetry, crash reports, audit summaries, localization diagnostics, or public test artifacts.

Plaintext generated-credential exports are an explicit escape from encrypted vault storage. On Unix-like platforms unpwn creates the temporary export with owner read/write permissions only (`0600`) from the initial exclusive open, verifies that mode before writing plaintext, and atomically moves the same file to its final name without overwrite. Parent-directory permissions are not modified. On Windows the export uses the normal ACL semantics inherited from the user-selected destination directory; unpwn does not claim a custom owner-only Windows ACL for arbitrary destinations.

## Known pre-release hardening gaps

The current source tree is not a supported security release. Remaining hardening work includes:

- owner-only creation permissions for Linux Recovery Browser profile data;
- cancellation/resource-safety hardening for the Linux WPE browsing-data cleanup callback boundary;
- dedicated code-security analysis, stricter dependency gating, and review guards around native interop.

These are local-confidentiality, native-resource-lifetime, and regression-detection risks. Existing encryption, plaintext-export permissions, no-overwrite behavior, public-network-only recovery discovery, exact-origin validation, secret-safe diagnostics, and normal CI remain useful controls, but they must not be presented as covering the remaining boundaries.

## Reporting a vulnerability

1. Prefer GitHub Private Vulnerability Reporting for this repository when available.
2. Include the affected component, reproduction steps, impact, and proposed mitigation if known.
3. Remove credentials, personal account data, reset links, cookies, tokens, and other live secrets from the report.

Do not open a public issue containing exploit details or sensitive user data. If no private channel is available, open only a minimal public issue requesting a private security contact.

A broken provider workflow that is not itself a vulnerability can be reported through a normal issue or pull request. Use private reporting when a workflow could expose credentials, send users to an attacker-controlled location, perform an unsafe action, weaken vault/browser isolation, or leak recovery data.

Please allow maintainers a reasonable opportunity to investigate before public disclosure.
