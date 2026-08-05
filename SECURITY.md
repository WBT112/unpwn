# Security Policy

## Security Philosophy

unpwn handles security-sensitive recovery workflows and newly generated credentials. Security and transparency have priority over convenience.

## Project Status

unpwn is currently in an early design and development phase. There are no supported production releases yet.

Security guarantees must not be inferred from unreleased designs or prototypes.

## Important Limitations

unpwn cannot guarantee account security if the device running it is compromised.

Users should run unpwn from a trusted environment whenever possible.

unpwn does not:

- detect or remove malware
- bypass MFA, CAPTCHA, identity verification, or account-ownership checks
- guarantee that a service provider restores account access
- guarantee forensic deletion of exported plaintext files

## Sensitive Data Requirements

unpwn must:

- keep recovery data local in the MVP
- encrypt sensitive vault records with AES-256-GCM
- derive vault-password protection with Argon2id
- avoid storing old passwords
- avoid logging credentials, vault keys, reset tokens, MFA secrets, or sensitive browser content
- avoid collecting user credentials remotely
- keep telemetry disabled unless a future design is explicitly reviewed and documented

See:

- [Vault Security](docs/VAULT_SECURITY.md)
- [Threat Model](docs/THREAT_MODEL.md)

## Reporting a Vulnerability

Preferred reporting channel:

1. Use GitHub Private Vulnerability Reporting for this repository when the feature is available.
2. Include the affected component, reproduction steps, impact, and any proposed mitigation.
3. Remove credentials, personal account data, reset links, cookies, tokens, and other live secrets from the report.

Do not open a public issue containing exploit details or sensitive user data.

When private vulnerability reporting is unavailable, create a minimal public issue stating that you need a private security contact, without disclosing the vulnerability details.

Maintainers will assess reports according to project capacity and may request additional non-sensitive reproduction information.

## Provider and Workflow Reports

A changed or broken provider workflow is normally reported as a regular issue or pull request when it does not expose a security vulnerability.

Use private vulnerability reporting when a workflow could:

- expose credentials
- navigate users to an attacker-controlled location
- perform an unsafe account action
- weaken vault protection
- leak recovery data

## Responsible Disclosure

Do not publicly disclose a vulnerability before maintainers have had a reasonable opportunity to investigate and prepare a fix or mitigation.
