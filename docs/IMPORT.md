# CSV Import

`Unpwn.Import` provides a platform-neutral, user-driven CSV import pipeline for account inventory data. It creates a validated preview; the desktop application persists only explicitly reviewed candidates in the encrypted account inventory.

## Flow

1. Analyze the CSV header and detected delimiter.
2. Show the suggested mapping and the mandatory password warning when password columns are present.
3. Let the user map service name, account name, login identifier, and account URL columns.
4. Require every detected password column to be explicitly excluded.
5. Create a preview containing valid rows, safe diagnostics, and duplicate candidates.
6. Compare the preview with the current encrypted inventory.
7. Apply the safe duplicate default: keep the first occurrence within the CSV, skip later matching rows, and skip candidates that already match an existing inventory account.
8. Allow the user to override that default and import duplicate CSV candidates as separate accounts.
9. Persist the reviewed candidates through the account-inventory application service.

Malformed rows are reported individually and do not stop later valid rows. Account URLs accept only absolute HTTP or HTTPS URLs. Duplicate detection uses the normalized service host or service name together with the login identifier or account name. It reports matches within the import and against account references supplied by the application.

The desktop import flow supports two duplicate dispositions:

- **default:** import the first occurrence in each CSV duplicate group and skip later duplicates; candidates that already match the encrypted inventory are skipped, or
- explicitly import duplicate candidates as separate accounts.

The default avoids creating duplicate account records without forcing the user through a confirmation for the common case. Merging account records remains outside the generic importer because it requires account-specific review of roles, dependencies, and existing recovery state.

## Application persistence boundary

The import project does not know about the recovery vault. It returns candidates and structured diagnostics only.

The desktop account-inventory service:

- supplies non-secret existing-account references for duplicate detection,
- converts reviewed candidates to new opaque account identifiers,
- assigns normal priority initially,
- creates only suggested identity/recovery roles,
- persists the resulting account state in the encrypted `account-state` vault record,
- recalculates the dependency-aware recovery plan and dashboard summaries.

A successful preview is not a successful import. File parsing, preview review, duplicate resolution, and encrypted persistence remain separate visible states. A failed write must not be presented as imported work.

## Localization and culture boundary

The import engine returns structured codes, row numbers, detected column names, and candidates. It does not return localized diagnostic sentences.

The desktop presentation layer maps import warning, validation, mapping, duplicate, preview, resolution, and persistence-result codes to localization resources. Password-column warnings and exclusion confirmations must remain explicit in every shipped language and fall back to reviewed English text when a translation is missing.

Changing the GUI language must not change how the same file is interpreted or how duplicate and persistence decisions are stored.

Import parsing therefore uses explicit rules rather than ambient UI culture:

- delimiter and quote handling are format rules,
- URLs use invariant URI parsing,
- identifiers and source text remain unchanged unless a documented normalization rule applies,
- numeric or date fields, if introduced later, require a declared import culture or unambiguous machine format,
- localized column-header aliases may suggest mappings but must not alter canonical target field identifiers.

Saved mappings contain canonical target field identifiers and source column names only. They do not store translated target labels.

## Secret-handling boundary

Old passwords are never part of the import model. A `CsvColumnMapping` contains column names only, so saved mappings cannot contain source values. Previewing is blocked until all detected password columns are explicitly excluded. During streaming parsing, excluded fields are consumed without appending their content to field buffers. Password values therefore do not enter candidates, diagnostics, duplicate keys, serialized previews, role inference, or the encrypted inventory.

The desktop import screen displays a localized warning derived from `CsvImportAnalysis.PasswordWarning` whenever `ContainsPasswordColumns` is true and keeps preview creation disabled until the user confirms exclusion. Diagnostics use stable codes and row numbers and never echo source values.

Localization diagnostics may include the resource key and selected culture only. They must never include imported field values or formatting arguments.

Direct password-manager API imports, automatic browser-password discovery, and automatic account merging are outside this generic CSV import boundary.

See [Account Inventory and Recovery Planning](ACCOUNT_INVENTORY.md) and [Localization and Multilingual GUI](LOCALIZATION.md).
