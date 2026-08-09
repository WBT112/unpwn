# Security Policy

unpwn handles sensitive recovery state and newly generated credentials. Security and transparent failure are more important than convenience.

## Project status

unpwn is under active development and does not yet have a supported production release. Do not infer production security guarantees from unreleased builds or prototypes.

## Important limits

Run unpwn on a device you reasonably trust. If the operating system is compromised, local software cannot reliably protect new passwords, browser sessions, screen contents, or decrypted recovery data.

unpwn does not:

- detect or remove malware;
- bypass MFA, CAPTCHA, identity verification, or account-ownership checks;
- guarantee that a provider restores access;
- guarantee that a completed workflow proves an account is secure;
- guarantee forensic erasure of plaintext exports.

The detailed security assumptions and mitigations are documented in [Threat Model](docs/THREAT_MODEL.md). Vault cryptography and secret handling are documented in [Vault Security](docs/VAULT_SECURITY.md).

## Sensitive data

Recovery data is local-first in the MVP. Old passwords are not stored. Credentials, vault keys, reset data, MFA secrets, cookies, and other sensitive values must not appear in logs, telemetry, crash reports, audit summaries, localization diagnostics, or public test artifacts.

## Reporting a vulnerability

1. Prefer GitHub Private Vulnerability Reporting for this repository when available.
2. Include the affected component, reproduction steps, impact, and proposed mitigation if known.
3. Remove credentials, personal account data, reset links, cookies, tokens, and other live secrets from the report.

Do not open a public issue containing exploit details or sensitive user data. If no private channel is available, open only a minimal public issue requesting a private security contact.

A broken provider workflow that is not itself a vulnerability can be reported through a normal issue or pull request. Use private reporting when a workflow could expose credentials, send users to an attacker-controlled location, perform an unsafe action, weaken vault protection, or leak recovery data.

Please allow maintainers a reasonable opportunity to investigate before public disclosure.
