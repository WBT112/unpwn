# CSV Import

`Unpwn.Import` provides a platform-neutral, user-driven CSV import pipeline for account inventory data. It creates a validated preview; persistence remains an application concern.

## Flow

1. Analyze the CSV header and detected delimiter.
2. Show the suggested mapping and the mandatory password warning when password columns are present.
3. Let the user map service name, account name, login identifier, and account URL columns.
4. Require every detected password column to be explicitly excluded.
5. Create a preview containing valid rows, safe diagnostics, and duplicate candidates.
6. Let the user review the preview before passing selected candidates to an application use case.

Malformed rows are reported individually and do not stop later valid rows. Account URLs accept only absolute HTTP or HTTPS URLs. Duplicate detection uses the normalized service host or service name together with the login identifier or account name. It reports matches within the import and against account references supplied by the application.

## Localization and culture boundary

The import engine returns structured codes, row numbers, detected column names, and candidates. It does not return localized diagnostic sentences.

The desktop presentation layer maps import warning, validation, mapping, duplicate, and preview codes to localization resources. Password-column warnings and exclusion confirmations must remain explicit in every shipped language and fall back to reviewed English text when a translation is missing.

Changing the GUI language must not change how the same file is interpreted.

Import parsing therefore uses explicit rules rather than ambient UI culture:

- delimiter and quote handling are format rules
- URLs use invariant URI parsing
- identifiers and source text remain unchanged unless a documented normalization rule applies
- numeric or date fields, if introduced later, require a declared import culture or unambiguous machine format
- localized column-header aliases may suggest mappings but must not alter canonical target field identifiers

Saved mappings contain canonical target field identifiers and source column names only. They do not store translated target labels.

## Secret-handling boundary

Old passwords are never part of the import model. A `CsvColumnMapping` contains column names only, so saved mappings cannot contain source values. Previewing is blocked until all detected password columns are explicitly excluded. During streaming parsing, excluded fields are consumed without appending their content to field buffers. Password values therefore do not enter candidates, diagnostics, duplicate keys, or serialized previews.

The desktop import screen displays a localized warning derived from `CsvImportAnalysis.PasswordWarning` whenever `ContainsPasswordColumns` is true and keeps preview creation disabled until the user confirms exclusion. Diagnostics use stable codes and row numbers and never echo source values.

Localization diagnostics may include the resource key and selected culture only. They must never include imported field values or formatting arguments.

Direct password-manager API imports and automatic browser-password discovery are outside this generic CSV import boundary.

See [Localization and Multilingual GUI](LOCALIZATION.md) for resource, fallback, and formatting requirements.
