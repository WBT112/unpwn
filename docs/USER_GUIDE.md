# unpwn User Guide

unpwn helps you work through account recovery after you suspect that credentials, sessions, or account data may have been compromised.

> **Development status:** unpwn is still under active development and does not yet have a supported production release. Do not rely on it as your only source of security advice.

## Before you start

Use a device you reasonably trust. If you think the device itself may still be compromised, do not enter new passwords or recovery information there.

unpwn cannot detect or remove malware and cannot prove that a device is safe.

## Recovery flow

### 1. Confirm the device

unpwn first asks whether you trust the device you are using. If you answer **No** or **Unsure**, the sensitive recovery flow stops before a vault is created or unlocked.

### 2. Create or open a Recovery Vault

The Recovery Vault stores recovery progress locally in encrypted form so you can stop and continue later.

Use a strong, unique vault password. The vault is a temporary recovery workspace, not a replacement for a password manager.

### 3. Record what happened

Record observable indicators such as lost access, unexpected password or MFA changes, unknown sessions, or a possibly compromised recovery channel.

Do **not** put passwords, reset links, MFA secrets, recovery codes, cookies, or tokens into notes.

### 4. Add your accounts

Add accounts manually or import reviewed account data from CSV.

Old-password columns are excluded from import. Duplicate candidates are reviewed conservatively; the normal safe path avoids silently creating duplicate recovery accounts.

### 5. Confirm important roles and dependencies

unpwn can suggest that an account is an email mailbox, password manager, identity provider, recovery channel, or another important identity root. Suggestions influence planning only after you confirm them.

Dependencies matter. For example, an online shop may depend on your email account for password reset. unpwn uses confirmed relationships and priorities to recommend a recovery order.

The guided assistant shows the current canonical step and what comes next. Opening a detail/workspace view does not mark a required step complete.

### 6. Follow the recommended recovery order

unpwn explains which account is recommended next and why. A recommendation is guidance, not proof that an account is compromised.

Depending on the provider, recovery may involve more than changing a password: active sessions, MFA/passkeys, recovery addresses, trusted devices, connected applications, tokens, or keys may also require review.

For providers without a reviewed workflow, unpwn clearly labels the guidance as general/manual rather than pretending it has provider-specific knowledge.

### 7. Work through each action in the Recovery Browser

For reviewed navigable actions, unpwn normally opens the provider page inside the managed **Recovery Browser** beside the current instructions and checklist. The external operating-system browser remains an explicit fallback when the embedded host is unavailable or unsuitable.

Opening a page, reaching a URL, navigating, returning, closing the browser, or restarting unpwn does **not** mark a recovery action complete.

Check a completion criterion only after it is actually true. Explicit checkmarks are stored through the encrypted recovery state before they are shown as recorded. The action still requires a separate **Done** confirmation. Blocked, failed, lost-access, waiting, and deliberately accepted risks remain visible.

The Recovery Browser uses a dedicated temporary profile rather than your normal browser profile. Provider login state can remain available while you work through actions for the same recovery account. Switching to another account requires the previous browser session to be cleaned up first. An unclean session after a crash is not silently resumed.

### 8. Handle new credentials in context

For password-change/reset actions, generate the new credential from the current recovery step. The assistant can then provide deliberate **Reveal**, **Copy**, **Hide**, **Mark as used**, and **Confirm credential works** controls without putting the plaintext password into normal recovery state.

Reveal and clipboard access are time-bounded. Closing the Recovery Browser or locking the vault clears materialized reveal state and requests cleanup of an unpwn-owned clipboard value. If automatic clipboard cleanup fails, follow the visible warning and clear it manually.

Automatic field insertion is not generic. It appears only when unpwn has an explicitly reviewed provider/action page contract. Before a credential is read from the vault, unpwn checks the current origin and expected page evidence. Unexpected origin/content, MFA, CAPTCHA, or an email-link handoff stops assistance. The current managed insertion fills only the reviewed fields and does not submit the provider form, confirm that the credential works, or complete the recovery action.

Real providers and generic/manual workflows continue to use Reveal/Copy/manual entry unless a provider/action insertion adapter has been separately reviewed. unpwn never guesses password fields on an arbitrary website.

### 9. Export new credentials

Move newly generated credentials to an established password manager when appropriate.

Plaintext exports such as CSV are sensitive. Avoid synchronized folders where possible, import the file promptly, and remove it afterwards if you no longer need it. File deletion does not guarantee forensic erasure.

### 10. Review unresolved work and finish explicitly

Before finishing, review critical accounts, blocked/failed actions, lost access, unresolved risks, credentials awaiting export/import confirmation, retained temporary credentials, and plaintext-cleanup warnings.

If a previous Recovery Browser session did not end cleanly, unpwn requires explicit cleanup before a new embedded provider session. Cleanup means the dedicated temporary browser data was removed according to the application lifecycle; it is not a forensic-erasure guarantee and it does not complete any provider action.

A high progress value does not mean that everything is secure. Completion runs a current preflight and ends only through an explicit terminal choice and secret-free final report.

## Important limits

unpwn does not:

- detect or remove malware;
- prove that a device is clean;
- bypass MFA, CAPTCHA, identity verification, or account-ownership checks;
- guarantee that a provider will restore access;
- guarantee that an account is secure merely because a workflow was completed;
- interpret browser navigation, field insertion, submission, or browser close as proof of provider success;
- provide generic automatic password-field detection for unsupported providers;
- replace a dedicated password manager or general-purpose web browser.

For the detailed security model and known limitations, see [Security Policy](../SECURITY.md), [Threat Model](THREAT_MODEL.md), and [Recovery Browser Security Boundary](RECOVERY_BROWSER.md).
