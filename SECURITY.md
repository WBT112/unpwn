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

Linux Recovery Browser profiles use an application-owned filesystem boundary: unpwn-created profile/data/cache directories are restricted to `0700`, owned marker files to `0600`, and redirected or unprotectable storage fails closed.

## Security CI

Security regressions are first-class CI failures. The repository-maintained gates include:

- built-in .NET `Security` analyzer diagnostics promoted to errors;
- NuGet audit for direct and transitive dependencies at `moderate` severity or higher;
- a native/unsafe boundary guard that allowlists the reviewed Recovery Browser interop location;
- a deterministic `SecurityRegression` test subset for high-risk invariants;
- CodeQL C# analysis on pull requests, `main`, and a weekly schedule;
- the existing synthetic-secret artifact scan before CI artifacts are uploaded.

The exact gates, allowlists, local commands, and exception process are documented in [Security CI Gates](docs/SECURITY_GATES.md). A green automated scan is additional evidence, not proof that a build is secure.

## Known pre-release hardening gaps

The current source tree is still not a supported security release. Remaining release work includes real desktop validation of the Linux Recovery Browser fallback and a complete native desktop end-to-end recovery journey on Windows and Linux. These gaps concern production-runtime integration and release validation; they do not change the existing rule that browser activity is never canonical recovery truth.

Existing encryption, resource limits, owner-only Linux browser/export permissions, public-network-only recovery discovery, exact-origin validation, cancellation-safe native cleanup, secret-safe diagnostics, and security CI are useful controls, but must not be presented as a guarantee against compromise of the host operating system or provider-side failures.

## Reporting a vulnerability

1. Prefer GitHub Private Vulnerability Reporting for this repository when available.
2. Include the affected component, reproduction steps, impact, and proposed mitigation if known.
3. Remove credentials, personal account data, reset links, cookies, tokens, and other live secrets from the report.

Do not open a public issue containing exploit details or sensitive user data. If no private channel is available, open only a minimal public issue requesting a private security contact.

A broken provider workflow that is not itself a vulnerability can be reported through a normal issue or pull request. Use private reporting when a workflow could expose credentials, send users to an attacker-controlled location, perform an unsafe action, weaken vault/browser isolation, or leak recovery data.

Please allow maintainers a reasonable opportunity to investigate before public disclosure.
