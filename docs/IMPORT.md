# CSV Import

`Unpwn.Import` provides a platform-neutral, user-driven CSV import pipeline for
account inventory data. It creates a validated preview; persistence remains an
application concern.

## Flow

1. Analyze the CSV header and detected delimiter.
2. Show the suggested mapping and the mandatory password warning when password
   columns are present.
3. Let the user map service name, account name, login identifier, and account
   URL columns.
4. Require every detected password column to be explicitly excluded.
5. Create a preview containing valid rows, safe diagnostics, and duplicate
   candidates.
6. Let the user review the preview before passing selected candidates to an
   application use case.

Malformed rows are reported individually and do not stop later valid rows.
Account URLs accept only absolute HTTP or HTTPS URLs. Duplicate detection uses
the normalized service host or service name together with the login identifier
or account name. It reports matches within the import and against account
references supplied by the application.

## Secret-handling boundary

Old passwords are never part of the import model. A `CsvColumnMapping` contains
column names only, so saved mappings cannot contain source values. Previewing is
blocked until all detected password columns are explicitly excluded. During
streaming parsing, excluded fields are consumed without appending their content
to field buffers. Password values therefore do not enter candidates,
diagnostics, duplicate keys, or serialized previews.

The desktop import screen displays `CsvImportAnalysis.PasswordWarning` whenever
`ContainsPasswordColumns` is true and keeps preview creation disabled until the
user confirms exclusion. Diagnostics use static messages and row numbers and
never echo source values.

Direct password-manager API imports and automatic browser-password discovery
are outside this generic CSV import boundary.
