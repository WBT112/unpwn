# Synthetic CSV recovery fixtures

All identities, login names, URLs, and credential-like values in this directory are synthetic test data. Email-style identifiers and navigable URLs use the reserved `example.test` namespace. Credential-like values use the repository's `UNPWN_TEST_SECRET_...` convention and must never be reused as credentials.

These files are developer and test fixtures. Do not replace their values with exported personal data.

## Quick manual smoke test

1. Start with a trusted test device choice and create a new temporary recovery vault.
2. Select `generic-recovery-sample.csv`. Verify that unpwn automatically detects these mappings and
   opens the preview without an additional action:
   - `service` → service/provider
   - `account` → account name
   - `username` → login identifier
   - `url` → account URL
   - leave `scenario` unmapped
3. Review all 16 valid candidates and import them.
4. Select **Review account categories**. Explicitly categorize `Primary recovery email` as **Email**,
   `Synthetic password manager` and one provider account as **Critical**, `Marketplace, account` as
   **Not critical**, and at least one account as **Unknown**.
5. Confirm that the queue is `Email → Critical → Unknown → NonCritical` and that remaining category
   reviews stay visible if you deliberately continue early.
6. Select **Start recovery** for the recommended account. Exercise the selected provider approach in
   the managed Recovery Browser and verify that navigation, returning, and closing the browser never
   mark an action complete.

`bitwarden-recovery-sample.csv` uses Bitwarden's [official individual-vault CSV format](https://bitwarden.com/help/condition-bitwarden-import/) as documented in August 2026. Verify that unpwn maps `folder` to service/provider, `name` to account name, `login_username` to login identifier, and `login_uri` to account URL. `login_password` must be excluded automatically and unavailable as a mapping option. The password values are synthetic markers included only to verify exclusion. Notes, fields, reprompt, and TOTP remain unmapped.

## Provider path matrix

The generic fixture contains one dedicated account for every currently shipped provider and path:

| Provider ID | Authenticated change | Password reset | Manual recovery |
| --- | --- | --- | --- |
| `google.com` | `Google / authenticated-change` | `Google / password-reset` | `Google / manual-recovery` |
| `microsoft.com` | `Microsoft / authenticated-change` | `Microsoft / password-reset` | `Microsoft / manual-recovery` |
| `github.com` | `GitHub / authenticated-change` | `GitHub / password-reset` | `GitHub / manual-recovery` |

The Bitwarden-style fixture supplies a smaller cross-format smoke set: Google authenticated change, Microsoft password reset, GitHub manual recovery, a password-manager account, and a UTF-8 account name.

## Post-import recovery setup

Use these deliberate setup steps to reproduce current category, queue, workflow, and browser states:

| Scenario | Setup |
| --- | --- |
| Category order | Categorize the primary recovery email as **Email**, the password manager and one provider account as **Critical**, one account as **Unknown**, and the marketplace as **Not critical**. Confirm the fixed queue order and stable ordering after restart. |
| Incomplete triage | Leave at least one suggestion unconfirmed, choose the deliberate early-continuation action, then return to Accounts. The remaining count and next unreviewed account must still be visible. |
| Password manager | Categorize `Synthetic password manager` as **Critical**. Do not store an old password in notes or any account field. |
| Authenticated change | Use an `authenticated-change` fixture and explicitly confirm that usable account access exists. The reviewed workflow should select authenticated password change without a manual path chooser. |
| Password reset | Use a `password-reset` fixture and record the explicit access condition requested by the guided workflow. The selector should prefer the reviewed reset approach when authenticated change is unavailable. |
| Manual recovery | Use a `manual-recovery` fixture and follow the guided access/failure choices until no safer reviewed automated-navigation path remains. Manual recovery must stay visible rather than being represented as success. |
| Lost access | Select a manual-recovery account and record that access cannot currently be restored. |
| Blocked required action | Start an action before one of its workflow-defined prerequisite actions is complete, or use the guided cannot-continue choice. The blocker and required reason must remain visible. |
| Failed action and retry | Record a synthetic provider failure on a required action, verify it remains failed, then use the supported retry path. |
| Waiting/user interaction | Pause an action for MFA, CAPTCHA, email-link handoff, or manual provider review. Do not store codes or links. |
| Not applicable | Mark a capability not applicable only with the required structured disposition and user reason. |
| Accepted unresolved risk | Explicitly accept a remaining required-action risk and confirm the account is not represented as fully secured. |
| Browser return | Open a reviewed destination in the managed Recovery Browser, navigate or close it, and return without confirming completion. The action must remain incomplete. |
| Queue recalculation | Complete, block, fail, defer, or accept risk on material work and verify that the recovery-overview recommendation is recalculated from persisted state. |
| Unsupported provider | Use `Manual fallback account`; the visibly labelled generic manual workflow should remain in the normal queue, and no provider URL or control should be guessed for `unsupported.example`. |

## Import edge-case fixture

`import-edge-cases.csv` intentionally contains:

- an exact normalized duplicate pair; the first remains importable and the second is marked as a duplicate;
- a quoted comma and an unknown `extra` column;
- blank optional URL or account-name fields;
- an invalid `javascript:` URL;
- a row missing both account name and login identifier;
- a blank line and a short malformed row;
- valid rows after the invalid rows;
- an unknown provider, a URL-only service identity, and a login-only account identity;
- an excluded password column containing only synthetic secret markers.

Importing the fixture a second time exercises existing-inventory duplicate detection. The default path skips duplicates; the explicit alternative imports them as separate accounts without silently merging recovery state.
