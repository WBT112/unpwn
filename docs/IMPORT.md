# CSV Import

`Unpwn.Import` turns a user-selected CSV file into reviewed account candidates. It does not persist recovery data itself; the desktop account-inventory service writes approved candidates to the encrypted vault.

## User-visible flow

1. Select a CSV file.
2. unpwn analyzes the header, automatically excludes detected password columns, and evaluates the
   suggested mapping.
3. A complete, unambiguous mapping immediately creates the preview; mapping controls stay hidden.
4. If a required service/account identity is missing or ambiguous, resolve only the affected mapping
   choices. The preview updates automatically as soon as the mapping is valid.
5. Review valid rows, row-level diagnostics, and duplicates.
6. Explicitly confirm the reviewed candidates for import into the encrypted account inventory.

A successful preview is not a successful import. Persistence must complete before the UI reports imported work.

## Password handling

Old passwords are never part of the import model.

Detected password columns are excluded automatically and are not offered as mapping choices. Their
column names are shown in one concise notice; there is no additional exclusion confirmation. Excluded
password values must not enter account candidates, diagnostics, duplicate keys, role inference,
serialized previews, UI strings, logs, test artifacts, or encrypted inventory state. The stream parser
does not append excluded field characters to its field buffers.

Diagnostics use structured codes and row numbers rather than echoing imported values.

## Duplicate handling

The safe default is:

- keep the **first** occurrence of an account within the CSV;
- skip later matching rows;
- skip a CSV candidate when the same account already exists in the encrypted inventory.

The user may explicitly override the default and import duplicate CSV candidates as separate accounts.

Automatic merging is intentionally not part of the generic importer because similarly named accounts can still represent different provider identities. The user reviews duplicate candidates explicitly.

Duplicate matching uses normalized service/provider identity together with the available login identifier or account name.

## Parsing boundary

The importer is platform-neutral and language-neutral. Changing the GUI language must not change the meaning of the same CSV file.

- delimiter and quote handling follow explicit format rules;
- account URLs must be absolute HTTP or HTTPS URLs and must not contain embedded URL credentials;
- URLs and identifiers use invariant parsing rules;
- localized column aliases may suggest mappings but never change canonical target field identifiers;
- future numeric or date fields require an explicit import culture or unambiguous machine format.

Saved mappings contain column names and canonical target identifiers only, never source values or translated target labels.

## Mapping quality

`CsvImportAnalysis.MappingAssessment` is the language-neutral source of truth for progressive
disclosure. It reports `Complete`, `NeedsReview`, or `Incomplete` together with structured issue codes.
The view does not infer validity from control state. Common aliases are accepted only when exactly one
header matches a semantic field; multiple plausible required headers are left unmapped for explicit
review rather than guessed. User-selected mappings are evaluated through the same import boundary.

Mapping quality controls preview preparation only. It never imports accounts or changes recovery
state. Final import remains an explicit user action after reviewing candidates and diagnostics.

## Application boundary

The desktop application supplies non-secret existing-account references for duplicate detection, assigns opaque account IDs, persists reviewed accounts, and recalculates the recovery queue and overview projection.

For the resulting account model and queue rules, see [Account Inventory and Recovery Queue](ACCOUNT_INVENTORY.md). Localization requirements are defined in [Localization](LOCALIZATION.md).

Repository-controlled developer fixtures and their expected mapping, diagnostics, provider paths, and post-import setup live in [`samples/import`](../samples/import/SCENARIOS.md). They contain synthetic data only and are the canonical manual import smoke-test input described by the [Testing Strategy](TESTING.md).
