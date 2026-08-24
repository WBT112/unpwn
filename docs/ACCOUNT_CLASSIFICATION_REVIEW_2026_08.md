# Account-classification catalog review — August 2026

This maintenance audit records the review history and current provenance for catalog version `2026.08.6`. It is not runtime classification data. Runtime uses only explicit repository-controlled records compiled into `Unpwn.Core`.

## Current result

The catalog meets the repository coverage goal in every recovery category:

| Category | Version `2026.08.4` | Added later | Version `2026.08.6` |
| --- | ---: | ---: | ---: |
| `Email` | 79 | 921 | 1,000 |
| `Critical` | 429 | 571 | 1,000 |
| `NonCritical` | 61 | 939 | 1,000 |

The target is a coverage guard, not classification authority. A third-party category, domain keyword, or numeric target never replaces an explicit unpwn category decision. Ambiguous or unsupported services remain `Unknown`, and a user's explicit category always wins.

## Review history

| Catalog version | Review outcome |
| --- | --- |
| `2026.08.4` | Rebuilt the conservative catalog from historical candidate sets and added 504 canonical provider families to the previous 65, for 569 reviewed families. No numeric target existed during this pass. |
| `2026.08.5` | Introduced the current 1,000-per-category coverage goal and added 939 ordinary publisher, news, and media account realms to complete `NonCritical`. |
| `2026.08.6` | Added 921 public-mailbox and 571 financial account realms to complete `Email` and `Critical`. |

Later rows supersede earlier count and policy snapshots. This single audit intentionally replaces the former version-specific follow-up documents so that historical statements cannot be mistaken for the current catalog state.

## Pinned candidate material and attribution

| Candidate material | Pinned revision and license | Volume | Use |
| --- | --- | ---: | --- |
| Historical `ut1-webmail.txt`, `ut1-bank.txt`, and `ut1-press.txt` at unpwn commit `42d7bc7` | `cbuijs/ut1@1b3eb2de2ccef5e85acb5103f70933b59edc51f9`, CC BY-SA 4.0 | 2,679 unique domains | Initial family discovery and negative review |
| Historical generated catalog at unpwn commit `f912ffd` | `v2fly/domain-list-community@6f8a5b43db087ae27decef85d80229850bbd40b1` (MIT) and `email-providers` 2.24.0 (ISC) | 2,950 generated records | Initial provider-family discovery |
| [UT1 press candidates](https://github.com/cbuijs/ut1/tree/2ddb46bdb691721cadc8e1521abc780396e7aeb3/press) | `cbuijs/ut1@2ddb46bdb691721cadc8e1521abc780396e7aeb3`, CC BY-SA 4.0 | 1,584 rows | `NonCritical` follow-up; 939 selected realms |
| [UT1 bank candidates](https://github.com/cbuijs/ut1/tree/2ddb46bdb691721cadc8e1521abc780396e7aeb3/bank) | `cbuijs/ut1@2ddb46bdb691721cadc8e1521abc780396e7aeb3`, CC BY-SA 4.0 | 875 rows | `Critical` follow-up; 571 selected realms |
| [Public-mailbox candidates](https://github.com/Validemailchecker/free-email-provider-domains/tree/8e7fa38d2228a6eed81226c63776d56d1b53c242) | `Validemailchecker/free-email-provider-domains@8e7fa38d2228a6eed81226c63776d56d1b53c242`, MIT | 13,716 rows | `Email` follow-up; 921 selected realms |
| [Disposable-domain exclusion](https://github.com/Validemailchecker/disposable-email-domains/tree/3af5c60a1934d98a7a66d52fe5f2d12260027b34) | `Validemailchecker/disposable-email-domains@3af5c60a1934d98a7a66d52fe5f2d12260027b34`, MIT | 212,970 rows | Complete exclusion set for the mailbox pass |

The upstream lists were discovery material only. Their snapshots and updater code are not loaded or executed at runtime. Selected derivative data retains the recorded attribution and license; the final recovery category remains an explicit unpwn product decision.

## Review rules

Each included record must:

1. identify a concrete provider or account realm rather than a keyword or infrastructure host;
2. use normalized registrable domains and avoid parent/subdomain ownership conflicts;
3. group regional, legacy, and alternate domains when evidence shows one shared account/recovery realm;
4. justify `Email`, `Critical`, or `NonCritical` from recovery impact rather than an upstream web-filter category;
5. remain independent from provider workflow, browser-navigation, and automation trust;
6. retain stable provenance and review basis in repository metadata.

The `NonCritical` follow-up accepts an ordinary private reader, listener, or viewer account only where delayed recovery is a defensible default. Elevated publishing, newsroom, advertising, administrator, payment, identity, or social authority requires a different classification or a user override. Gaming accounts remain `Critical` when they can hold purchases, balance, tradable or valuable inventory, marketplace access, or material social identity.

The financial pass accepts bank, credit-union, card, payment, and financial account realms where takeover can materially affect money, identity, or account control. Informational publications, trade groups, regulators or central banks without a normal customer recovery realm, and unrelated businesses are excluded.

The mailbox pass excludes the complete pinned disposable-domain set, malformed or overlapping names, numeric churn indicators, ambiguous shared hosts, and high-abuse dynamic domain shapes. Surviving public mailbox domains are retained only as explicit repository-reviewed account realms. Evidence that several domains share one canonical recovery authority should consolidate them later; the numeric target must never prevent a correctness fix.

Representative primary evidence retained from the family review includes:

- [Proton address/alias families](https://proton.me/support/addresses-and-aliases) and [Microsoft Hotmail/Outlook sign-in guidance](https://support.microsoft.com/en-us/accounts-billing/manage/how-to-sign-in-to-hotmail) for shared mailbox realms;
- the [EBA credit-institution register](https://www.eba.europa.eu/risk-and-data-analysis/data/registers-and-other-list-institutions/credit-institutions-register), [EBA payment-institution register](https://www.eba.europa.eu/risk-and-data-analysis/data/registers/payment-institutions-register), [Deutsche Bundesbank bank-code lookup](https://www.bundesbank.de/de/startseite/suche/bankleitzahlen-suche), and [BaFin account comparison](https://kontenvergleich.bafin.de/de) for European financial-provider identity;
- [Dashlane recovery options](https://support.dashlane.com/hc/en-us/articles/11282971791634-Account-recovery-options-for-Dashlane), [Keeper recovery guidance](https://www.keepersecurity.com/en_GB/support.html?t=p), [NordPass reset guidance](https://support.nordpass.com/hc/en-us/articles/5388857973905-How-to-reset-NordPass-account), and [1Password account guidance](https://support.1password.com/explore/get-started/) for password-manager impact;
- [Steam trading/Community Market restrictions](https://help.steampowered.com/en/faqs/view/451E-96B3-D194-50FC) and [Roblox item trading/resale](https://en.help.roblox.com/hc/en-us/articles/206142306-The-Item-Details-Page-and-Purchasing-Items) for the gaming-asset boundary;
- [BundID](https://id.bund.de/), [ELSTER](https://www.elster.de/eportal/start?locale=de_DE), and the [FranceConnect account explanation](https://aide.franceconnect.gouv.fr/faq/comprendre-franceconnect/creation-compte/) for the government-identity boundary.

These references support family identity and account relevance. They do not delegate unpwn's
recovery-priority decision to a provider, register, or candidate list.

## Representative exclusions

| Reason | Examples | Result |
| --- | --- | --- |
| Generic category hint | `banking`, `streaming`, `news` | `Unknown`; a keyword does not identify an account provider |
| Informational, charity, regulator, or unrelated site | `bankrate.com`, `banquealimentaire.org`, `safeway.com`, `tcmb.gov.tr` | `Unknown`; upstream category membership is insufficient |
| Disposable or dynamic mailbox | `10minutemail.com`, `202608221.xyz`, `midnd.help` | `Unknown`; not accepted as a durable recovery root |
| Obsolete service without a current account realm | `orkut.com` | `Unknown`; no successor is inferred |
| Federated or ambiguous identity | `FranceConnect` itself | `Unknown`; another provider owns the actual login account |

The repository intentionally does not carry a large runtime rejection list. Omission means the classifier returns `Unknown`; it is not evidence that the provider is harmless.

## Runtime boundary and maintenance

The current records live in:

- `RepositoryAccountClassificationCatalog.cs`;
- `RepositoryAccountClassificationProviderData.cs` and `RepositoryAccountClassificationAdditionalProviderData.cs`;
- the category-specific `Email`, `Critical`, and `NonCritical` provider-data files.

Runtime classification is deterministic and offline. It does not download candidate data, query DNS, validate a mailbox, inspect browser state, or send inventory data to a third party. Classification decides only **when** an account is prioritized; provider workflows decide **how** recovery proceeds.

Tests enforce the per-category target, unique IDs/domains/aliases, parent/subdomain ownership, representative positive matches and exclusions, pinned provenance/licenses, culture-independent matching, conservative `Unknown`, workflow/automation independence, and explicit user-override precedence.
