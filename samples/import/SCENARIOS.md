# Synthetic CSV recovery fixtures

All identities, login names, URLs, and credential-like values in this directory are synthetic test data. Email-style identifiers and navigable URLs use the reserved `example.test` namespace. Credential-like values use the repository's `UNPWN_TEST_SECRET_...` convention and must never be reused as credentials.

These files are developer and test fixtures. Do not replace their values with exported personal data.

## Quick manual smoke test

1. Start with a trusted test device choice and create a new temporary recovery vault.
2. Import `generic-recovery-sample.csv` with these mappings:
   - `service` → service/provider
   - `account` → account name
   - `username` → login identifier
   - `url` → account URL
   - leave `scenario` unmapped
3. Review all 16 valid candidates and import them.
4. Configure the roles and dependencies described below; the CSV intentionally does not smuggle unsupported domain state into extra columns.
5. Exercise the selected provider path and verify that returning from a browser never marks an action complete.

`bitwarden-recovery-sample.csv` uses Bitwarden's [official individual-vault CSV format](https://bitwarden.com/help/condition-bitwarden-import/) as documented in August 2026. Map `folder` to service/provider, `name` to account name, `login_username` to login identifier, and `login_uri` to account URL. Explicitly exclude `login_password`. The password values are synthetic markers included only to verify exclusion. Leave notes, fields, reprompt, and TOTP unmapped.

## Provider path matrix

The generic fixture contains one dedicated account for every currently shipped provider and path:

| Provider ID | Authenticated change | Password reset | Manual recovery |
| --- | --- | --- | --- |
| `google.com` | `Google / authenticated-change` | `Google / password-reset` | `Google / manual-recovery` |
| `microsoft.com` | `Microsoft / authenticated-change` | `Microsoft / password-reset` | `Microsoft / manual-recovery` |
| `github.com` | `GitHub / authenticated-change` | `GitHub / password-reset` | `GitHub / manual-recovery` |

The Bitwarden-style fixture supplies a smaller cross-format smoke set: Google authenticated change, Microsoft password reset, GitHub manual recovery, a password-manager root, and a UTF-8 account name.

## Post-import recovery setup

Use these deliberate setup steps to reproduce planning and workflow states:

| Scenario | Setup |
| --- | --- |
| Dependency root | Mark `Primary recovery email` as an email/recovery-channel role. Make selected reset accounts depend on it. It must be reviewed before those dependents become ready. |
| Password manager | Mark `Synthetic password manager` as a password-manager and critical root. Do not store an old password in notes. |
| Critical and ordinary accounts | Mark the primary email, password manager, and one provider account critical. Leave `Marketplace, account` at normal priority. |
| Missing dependency | Add a dependency to a synthetic account, remove that dependency account, and confirm the missing dependency remains visible. |
| Dependency cycle | Make the Google reset account depend on the Microsoft reset account and vice versa. Confirm the cycle is shown, then apply an explicit override with its unresolved risk retained. |
| Lost access | Select a manual-recovery account and record that access cannot currently be restored. |
| Blocked required action | On a password-reset account, block the reset action because its recovery-channel dependency is not secured. |
| Failed action and retry | Record a synthetic provider failure on a required action, verify it remains failed, then use the supported retry path. |
| Waiting/user interaction | Pause an action for MFA, CAPTCHA, email-link handoff, or manual provider review. Do not store codes or links. |
| Not applicable | Mark a capability not applicable only with the required structured disposition and user reason. |
| Accepted unresolved risk | Explicitly accept a remaining required-action risk and confirm the account is not represented as fully secured. |
| Browser return | Open a reviewed destination and return to unpwn without confirming completion. The action must remain incomplete. |
| Plan recalculation | Complete, block, fail, or accept risk on a material action and verify the recommendation is recalculated. |
| Unsupported provider | Use `Manual fallback account`; no reviewed workflow should be guessed for `unsupported.example`. |

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
