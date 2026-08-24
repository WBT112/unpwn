# Email and Critical Catalog Completion Review — August 2026

## Outcome

Catalog version `2026.08.6` completes the repository target of at least 1,000 account realms per recovery category:

| Category | Before | Added | Current |
| --- | ---: | ---: | ---: |
| Email | 79 | 921 | 1,000 |
| Critical | 429 | 571 | 1,000 |
| NonCritical | 1,000 | 0 | 1,000 |

The target is a coverage guard, not a classification shortcut. Every checked-in entry records an explicit unpwn category and review basis. Unknown or ambiguous candidates remain absent and therefore classify as `Unknown`.

## Pinned candidate material and attribution

The financial discovery pass used `bank/domains.top-n` from `cbuijs/ut1` pinned at commit `2ddb46bdb691721cadc8e1521abc780396e7aeb3` (875 source rows). The selected derivative list retains the source's CC BY-SA 4.0 attribution. UT1's web category is only candidate material; it is not recovery-risk truth.

The mailbox discovery pass used `Validemailchecker/free-email-provider-domains` pinned at commit `8e7fa38d2228a6eed81226c63776d56d1b53c242` (13,716 source rows) and excluded every domain present in `Validemailchecker/disposable-email-domains` pinned at commit `3af5c60a1934d98a7a66d52fe5f2d12260027b34` (212,970 source rows). Both repositories are MIT licensed. Their membership is candidate evidence only; the checked-in `Email` decision is explicit repository metadata.

## Review and normalization

The common structural pass accepted normalized DNS names representing a registrable account realm, rejected malformed values and parent/subdomain collisions, and removed domains already owned by a canonical provider record. The resulting source files are deterministic and human-reviewable.

The `Critical` pass retained bank, credit-union, card, payment, and financial account realms where takeover can materially affect money, identity, or account control. It explicitly rejected informational publications, trade groups, regulators and central banks without a normal customer recovery realm, unrelated businesses, and obvious upstream false positives. Representative exclusions include `bankrate.com`, `banquealimentaire.org`, `safeway.com`, and `tcmb.gov.tr`.

The `Email` pass first removed the complete pinned disposable-domain set, then limited candidates to registrable classic generic or country-code domains without numeric churn indicators. Temporary, dynamically named, structurally ambiguous, and high-abuse new-TLD candidates were rejected. Representative exclusions include `10minutemail.com`, `202608221.xyz`, and `midnd.help`.

Some public mailbox services historically offered multiple selectable domains. A checked-in domain is treated as an account realm only where the candidate material identifies it as a public mailbox domain and it survives the conservative exclusion pass. Future evidence that domains share one canonical recovery realm should consolidate them; the numeric target must never block a correctness fix.

## Runtime and trust boundary

Runtime classification remains completely offline. unpwn does not download either source, query DNS, validate a mailbox, inspect browser state, or send inventory data to a third party. The catalog suggests only recovery ordering. It cannot choose a provider workflow or turn browser observations into recovery truth, and an explicit user category continues to win.

Regression tests cover the per-category target, catalog uniqueness and hierarchy invariants, representative positive matches, the named false-positive exclusions, pinned provenance and licenses, culture-independent matching, and explicit user overrides.
