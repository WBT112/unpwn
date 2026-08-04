# unpwn Threat Model

## Purpose

This document describes the security assumptions, assets, threats, and boundaries of unpwn.

## Security Goal

unpwn helps users recover their digital identity after a suspected compromise. It does not guarantee that an already compromised system is safe.

## Protected Assets

- account inventory
- usernames and email addresses
- generated credentials
- recovery progress
- recovery history
- export data

## Trust Boundary

unpwn should be executed on a trusted device.

If malware or an attacker controls the operating system, they may be able to observe:

- keyboard input
- browser sessions
- screen contents
- new credentials

No local recovery application can fully prevent this.

## Threat Scenarios

### Device compromise

Risk:

An attacker can access recovery data or new credentials.

Mitigation:

- clear security warning before recovery starts
- encrypted local vault
- no cloud storage in MVP

### Vault theft

Risk:

An attacker obtains the recovery vault file.

Mitigation:

- strong encryption
- secure key handling
- no plaintext secrets in logs

### Unsafe exports

Risk:

Export files containing credentials are copied or leaked.

Mitigation:

- explicit export confirmation
- warnings before creating plaintext formats
- recommend importing into established password managers

## Out of Scope

unpwn does not:

- detect malware
- remove infostealers
- bypass MFA
- bypass CAPTCHA
- guarantee account recovery

## Security Principle

Automation should reduce workload while keeping critical security decisions visible to the user.
