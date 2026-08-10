# CSV Import

`Unpwn.Import` turns a user-selected CSV file into reviewed account candidates. It does not persist recovery data itself; the desktop account-inventory service writes approved candidates to the encrypted vault.

## User-visible flow

1. Select a CSV file.
2. Review the detected delimiter and suggested column mapping.
3. Explicitly exclude every detected password column.
4. Map service, account name, login identifier, and optional account URL.
5. Review valid rows, row-level diagnostics, and duplicates.
6. Import the reviewed candidates into the encrypted account inventory.

A successful preview is not a successful import. Persistence must complete before the UI reports imported work.

## Password handling

Old passwords are never part of the import model.

Preview creation is blocked until detected password columns are excluded. Excluded password values must not enter account candidates, diagnostics, duplicate keys, role inference, serialized previews, or encrypted inventory state.

Diagnostics use structured codes and row numbers rather than echoing imported values.

## Duplicate handling

The safe default is:

- keep the **first** occurrence of an account within the CSV;
- skip later matching rows;
- skip a CSV candidate when the same account already exists in the encrypted inventory.

The user may explicitly override the default and import duplicate CSV candidates as separate accounts.

Automatic merging is intentionally not part of the generic importer because existing roles, dependencies, and recovery state require account-specific review.

Duplicate matching uses normalized service/provider identity together with the available login identifier or account name.

## Parsing boundary

The importer is platform-neutral and language-neutral. Changing the GUI language must not change the meaning of the same CSV file.

- delimiter and quote handling follow explicit format rules;
- account URLs must be absolute HTTP or HTTPS URLs;
- URLs and identifiers use invariant parsing rules;
- localized column aliases may suggest mappings but never change canonical target field identifiers;
- future numeric or date fields require an explicit import culture or unambiguous machine format.

Saved mappings contain column names and canonical target identifiers only, never source values or translated target labels.

## Application boundary

The desktop application supplies non-secret existing-account references for duplicate detection, assigns opaque account IDs, persists reviewed accounts, and recalculates the recovery plan and dashboard projection.

For the resulting account model and planning rules, see [Account Inventory and Recovery Planning](ACCOUNT_INVENTORY.md). Localization requirements are defined in [Localization](LOCALIZATION.md).

Repository-controlled developer fixtures and their expected mapping, diagnostics, provider paths, and post-import setup live in [`samples/import`](../samples/import/SCENARIOS.md). They contain synthetic data only and are the canonical manual import smoke-test input described by the [Testing Strategy](TESTING.md).
