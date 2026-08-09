# unpwn User Guide

unpwn helps you work through account recovery after you suspect that credentials, sessions, or account data may have been compromised.

> **Development status:** unpwn is still under active development and does not yet have a supported production release. Do not rely on it as your only source of security advice.

The flow below describes the intended MVP recovery experience. Current development builds may not yet implement every step.

## Before you start

Use a device you reasonably trust. If you think the device itself may still be compromised, do not enter new passwords or recovery information there.

unpwn cannot detect or remove malware and cannot prove that a device is safe.

## Recovery flow

### 1. Confirm the device

unpwn starts by asking whether you trust the device you are using. If you answer **No** or **Unsure**, the sensitive recovery flow stops before a vault is created or unlocked.

### 2. Create or open a Recovery Vault

The Recovery Vault stores your recovery progress locally in encrypted form so you can stop and continue later.

Use a strong, unique vault password. The vault is a temporary recovery workspace, not a replacement for a password manager.

### 3. Record what happened

You can record observations such as lost access, unexpected password or MFA changes, unknown sessions, or a possibly compromised recovery channel.

Do **not** put passwords, reset links, MFA secrets, recovery codes, cookies, or tokens into notes.

### 4. Add your accounts

Add accounts manually or import reviewed account data from CSV.

Old password columns are excluded from import. When the CSV contains duplicate accounts, unpwn keeps the first occurrence by default and skips later duplicates. Accounts that already exist in the vault are also skipped by default.

### 5. Confirm important roles and dependencies

unpwn can suggest that an account is an email mailbox, password manager, identity provider, recovery channel, or another important account. Suggestions only affect recovery planning after you confirm them.

Dependencies matter. For example, an online shop may depend on your email account for password reset. unpwn tries to secure dependency roots first.

### 6. Follow the recommended recovery order

unpwn explains which account is recommended next and why. A recommendation is guidance, not proof that an account is compromised.

For each account, recovery may involve more than changing a password. Depending on the provider, you may also need to review sessions, MFA or passkeys, recovery addresses, trusted devices, connected applications, tokens, or keys.

### 7. Confirm each action explicitly

Opening an official provider page does **not** mark a recovery action as complete. Returning to unpwn does not mark it complete either.

Complete an action only after you have checked its stated completion criteria. Blocked, failed, unavailable, or deliberately accepted risks remain visible.

### 8. Export new credentials

New credentials may be generated and held temporarily in the encrypted vault. Export them to an established password manager when appropriate.

Plaintext exports such as CSV are sensitive. Avoid synchronized folders where possible, import the file promptly, and remove it afterwards if you no longer need it. File deletion does not guarantee forensic erasure.

### 9. Review unresolved work

Before finishing, review critical accounts, blocked or failed actions, lost access, unresolved risks, and any credentials that still need export or cleanup.

A high progress value does not mean that everything is secure.

## Important limits

unpwn does not:

- detect or remove malware;
- bypass MFA, CAPTCHA, identity verification, or ownership checks;
- guarantee that a provider will restore access;
- guarantee that an account is secure merely because a workflow was completed;
- replace a dedicated password manager.

For the security model and known limitations, see [Security Policy](../SECURITY.md) and [Threat Model](THREAT_MODEL.md).
