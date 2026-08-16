# Account classification data snapshot

This directory contains repository-vendored data used only for **offline recovery-priority suggestions**. It does not define provider recovery workflows and is never queried over the network at runtime.

## Sources and license

The `ut1-*.txt` files are derived from the Université Toulouse 1 Capitole (UT1) web-categorization lists through the normalized `cbuijs/ut1` mirror, pinned at commit:

`1b3eb2de2ccef5e85acb5103f70933b59edc51f9`

Attribution: Université Toulouse 1 Capitole / Fabrice Prigent, with the normalized mirror maintained by `cbuijs`. The mirror points back to the original UT1 source, retains the original data alongside normalized files, and ships the Creative Commons Attribution-ShareAlike 4.0 license (`cc-by-sa-4-0.pdf`). The vendored `ut1-*.txt` snapshot is therefore kept as separately attributed **CC BY-SA 4.0 data**; it is not relicensed as part of unpwn's AGPL source code.

UT1 describes these lists as web categorization rather than a universal blocklist and explicitly notes that categories can contain mistakes. unpwn therefore treats every catalog result as an advisory recovery-priority suggestion. The user's explicit category always wins.

## Mapping and selection

The snapshot maps only three source categories:

| Vendored file | UT1 category | unpwn suggestion | Snapshot target |
| --- | --- | --- | ---: |
| `ut1-webmail.txt` | `webmail` | `Email` | 180 |
| `ut1-bank.txt` | `bank` | `Critical` | 1,250 |
| `ut1-press.txt` | `press` | `NonCritical` | 1,250 |

The maintenance script consumes `domains.top-n` first and then fills any remaining target from the complete normalized `domains` file. Entries are normalized to ASCII IDNA form, checked as DNS names, deduplicated, and bounded before being written. The runtime loader repeats domain validation and enforces independent byte/record limits.

These source-domain records are **canonical domain-scoped service records**, not aliases used to inflate a provider count. Separately curated multi-domain provider families (for example Gmail, Outlook, Yahoo, Proton, Amazon, PayPal, Netflix) remain single canonical records with multiple aliases. When a vendored source domain is already claimed by a curated provider, the curated record wins and the source record is omitted. For cross-source claims the deterministic ingestion precedence is `Email` → `Critical` → `NonCritical`.

## Updating

1. Review the upstream UT1 category semantics and the normalized mirror changes.
2. Change `SOURCE_REVISION` in `scripts/update-account-classification-catalog.py` to a full reviewed commit SHA.
3. Run `python3 scripts/update-account-classification-catalog.py` from the repository root.
4. Review the generated domain diff, especially category changes and unexpected high-impact services.
5. Run the full test suite. The catalog tests enforce minimum canonical-record counts, unique IDs/domain claims, representative classifications, culture independence, provenance, `Unknown` fallback, and user-override precedence.
6. Update `RepositoryAccountClassificationCatalog.Ut1SourceRevision` in the same change.

Do not change this process to a live lookup. Account inventory data must never be sent to UT1, the mirror, or any other classification service.
