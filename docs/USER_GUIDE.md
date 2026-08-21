# unpwn User Guide

unpwn helps you work through account recovery after you suspect that credentials, sessions, or account data may have been compromised.

> **Development status:** unpwn is still under active development and does not yet have a supported production release. Do not rely on it as your only source of security advice.

## Before you start

Use a device you reasonably trust. If you think the device itself may still be compromised, do not enter new passwords or recovery information there.

unpwn cannot detect or remove malware and cannot prove that a device is safe.

## Application startup and Settings

unpwn starts in the operating-system UI language when that language is supported. German and English are currently shipped; unsupported system languages fall back to English. Pseudo-localization is a development/test option and is never selected automatically from the operating-system locale.

On first run, the main desktop window opens maximized. Later runs restore the last normal window size and whether the window was normal or maximized. Window position is deliberately not persisted, so stale coordinates from a removed or rearranged monitor cannot place unpwn off-screen.

Use **Settings** in the main header to change the runtime language or create a local support diagnostic report. Diagnostic export is owned only by Settings. The flow requires an explicit preview, destination, and approval before export. Existing destination files are not overwritten.

The application-preferences file is separate from the Recovery Vault and contains only non-sensitive presentation state such as the normal window dimensions and maximized state. It does not store recovery accounts, vault contents, credentials, browser state, or recovery results.

## Recovery flow

### 1. Confirm the device

unpwn first asks whether you trust the device you are using. If you answer **No** or **Unsure**, the sensitive recovery flow stops before a vault is created or unlocked.

If you later choose to reassess the device from an unlocked vault, unpwn locks the vault first and asks the trusted-device question again. Returning to that assessment does not preserve or invent a new trust answer.

### 2. Create or open a Recovery Vault

The Recovery Vault stores recovery progress locally in encrypted form so you can stop and continue later.

If a previously used vault still exists, unpwn presents that vault as the primary **Open last vault** action after the trusted-device check. Other vaults and creation remain available as secondary actions. A missing recent-vault reference is not opened and is never silently recreated at the old path.

If there is no usable previous vault, **Create a new vault** becomes the primary action. unpwn preselects a user-local application-data location and a readable non-existing filename. On Linux the XDG data location is preferred when configured; on Windows the user-local application-data location is used. You can still choose another location before creation, and creation continues to fail rather than overwrite an existing vault file.

Use a strong, unique vault password. The vault is a temporary recovery workspace, not a replacement for a password manager.

After a vault is successfully created, opened, or unlocked, password fields are cleared and unpwn continues directly to the recovery overview. The Vault screen remains available afterwards for vault management such as changing the vault password or repeating the trusted-device assessment.

### 3. Create the recovery session

unpwn suggests a local session name such as **Tobi-Recovery** from the operating-system user name. You
can edit it before creation. If no usable local user name is available, the suggestion is
**Recovery**. The suggestion is created locally; unpwn does not query a directory service or derive it
from imported account or email data.

One optional warning can add immediate guidance: whether a primary email or recovery channel may be
under someone else's control. Confirm account-specific access later while working on that account;
this lets unpwn choose the recovery approach without changing the category order. The warning guides
prioritization and does not prove that an account, device, or credential was compromised. unpwn does
not collect a free-form incident narrative.

After the session is created, unpwn opens CSV import directly. You do not need to choose the next
navigation tab.

### 4. Add your accounts

Add accounts manually or select a CSV file. unpwn detects a safe column mapping and creates the
preview automatically. Mapping choices appear only when a required value is missing or ambiguous, and
the preview updates after that choice without a separate button.

Old-password columns are excluded automatically and cannot be selected as account fields. Review the
preview and explicitly confirm the final import. Duplicate candidates remain conservative: the normal
safe path keeps the first CSV occurrence and lets the existing inventory win.

After at least one reviewed account is stored successfully, unpwn opens account category review
automatically. A failed import stays on the import screen so you can correct or retry it. You can still
return to CSV import later to add more accounts.

### 5. Categorize accounts

unpwn suggests one simple local category for every account: **Email**, **Critical**, **Not critical**, or **Unknown**. The versioned catalog works offline and recognizes common email services and important account types. A suggestion is never your decision: choose a category explicitly and select **Save and review next**.

Select **Continue to recovery** after all categories are reviewed. If useful, you can deliberately
select **Continue to recovery now** while reviews remain; unpwn keeps the remaining count visible and
does not silently confirm any suggestion. If you genuinely have no email account, review the accounts
as such or deliberately continue. On resume, the same workspace shows remaining review and its next
action instead of requiring a separate assistant or guessed navigation tab.

Categories decide when an account is considered; the reviewed provider workflow independently decides how it is recovered. The normal product does not ask you to maintain roles, dependency graphs, cycles, or scheduling overrides. Opening a view or leaving triage does not record an implicit category.

### 6. Follow the recommended recovery order

unpwn automatically orders outstanding accounts as **Email → Critical → Unknown → Not critical** and
explains which account is recommended next. Stable account identifiers resolve ties, so language and
restart do not change the queue. A recommendation is guidance, not proof that an account is
compromised.

The recovery overview shows the current account, its category, why it is next, and the automatically
selected approach. **Start recovery** creates or resumes its canonical execution, starts the current
action, and opens the reviewed destination in the isolated Recovery Browser as one operation. If the
embedded host cannot start safely, unpwn keeps an explicit error and deliberately labelled external
fallback visible. Opening a detail tab does not advance the flow or complete work.

Use **Skip account for now** to move the account behind the other outstanding work for this pass. This
does not complete, secure, or mark the account not applicable. The account remains unresolved, can be
resumed later, and is shown as open work during completion review even after repeated skips.

For each account, unpwn also chooses the safest supported recovery approach. Confirmed usable access
uses an authenticated password change where supported; otherwise unpwn prefers password reset and
then manual recovery. If access is lost or the provider rejects an approach, unpwn records why and
moves to the next safe option. If none remains, the account stays visibly blocked. Browser activity
never selects an approach, and the normal interface does not ask you to choose an internal path value.

Depending on the provider, recovery may involve more than changing a password: active sessions, MFA/passkeys, recovery addresses, trusted devices, connected applications, tokens, or keys may also require review.

For providers without a reviewed workflow, unpwn clearly labels the guidance as general/manual rather than pretending it has provider-specific knowledge.

### 7. Work through each action in the Recovery Browser

For reviewed navigable actions, unpwn normally opens the provider page inside the managed **Recovery Browser** beside the current instructions and checklist. The external operating-system browser remains an explicit fallback when the embedded host is unavailable or unsuitable.

The assistant also explains that a provider may require the current password/session, MFA, a passkey
or security key, a recovery code, CAPTCHA, an email link, or identity verification. Complete those
provider checks yourself. A newly generated replacement password is not the credential for a login
that must happen before the change is accepted.

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
