# Curated account-classification review — August 2026

This document records the review that produced repository catalog version `2026.08.4`. It is a maintenance and audit artifact, not runtime classification data. Runtime classification continues to use only the explicit records compiled into `Unpwn.Core`.

## Candidate material reviewed

The review reconstructed both broad candidate sets that existed before the conservative catalog introduced by #154:

| Historical artifact | Pinned upstream | Candidate volume | How it was used |
| --- | --- | ---: | --- |
| `ut1-webmail.txt`, `ut1-bank.txt`, `ut1-press.txt` at repository commit `42d7bc7` | `cbuijs/ut1` commit `1b3eb2de2ccef5e85acb5103f70933b59edc51f9`, CC BY-SA 4.0 | 180 + 1,250 + 1,250 rows; 2,679 unique domains | Discovery and negative-review input only |
| Generated catalog at repository commit `f912ffd` | `v2fly/domain-list-community` commit `6f8a5b43db087ae27decef85d80229850bbd40b1` (MIT) and `email-providers` 2.24.0 (ISC) | 2,950 generated records | Provider-family discovery only |

Every candidate went through the same review funnel: discard invalid or non-account entries, collapse infrastructure and alternate domains into real account families, verify current ownership/account relevance, then make an explicit unpwn priority decision. A third-party category never supplied the final category. Historical snapshots, generated records, and updater code are not restored to the runtime or build.

## Result

The review adds 504 canonical provider families to the previous 65, for 569 repository-reviewed families in total. There is deliberately no test or acceptance threshold tied to those counts. The count documents the reviewed change; it is not a minimum that generated data can satisfy.

| Cohort | Families added | Category decision |
| --- | ---: | --- |
| Regional and global consumer email | 55 | `Email` because the mailbox is a recovery root |
| Banks, investments, payments, and crypto | 138 | `Critical` because takeover can affect money and identity |
| Password managers, security, and identity providers | 26 | `Critical` because they control credentials, security services, or connected access |
| Cloud, developer, and work platforms | 64 | `Critical` because they can control data, code, infrastructure, packages, domains, or organizational systems |
| Commerce, marketplaces, and travel | 68 | `Critical` because they commonly retain payment, address, order, booking, loyalty, or seller authority |
| Communications and social identity | 28 | `Critical` because takeover enables impersonation, publishing, or message access |
| Gaming and game marketplaces | 25 | `Critical` where accounts can hold purchases, balances, tradable or valuable inventory, marketplace access, or material social identity |
| Government, health, and insurance | 49 | `Critical` because accounts contain official, identity, medical, policy, claim, or coverage data |
| Clearly lower-impact media, reading, fitness, and leisure | 51 | `NonCritical` where delayed recovery is defensible and no trading or material asset authority was identified |

The canonical records and their domains, aliases, provenance, and category rationale live in `RepositoryAccountClassificationCatalog.cs`, `RepositoryAccountClassificationProviderData.cs`, and `RepositoryAccountClassificationAdditionalProviderData.cs`. Regional and legacy domains are grouped only when they share an account realm—for example Yahoo/ymail, Proton/pm.me, Tuta/tuta.io, Mail.ru/inbox.ru, AT&T/Bellsouth, Virgin Media/ntlworld, ING/ING-DiBa, and X/Twitter.

Review evidence included current provider-owned sign-in, account, and recovery documentation. Representative primary references are:

- [Proton address and alias families](https://proton.me/support/addresses-and-aliases) and [Microsoft's Hotmail/Outlook account guidance](https://support.microsoft.com/en-us/accounts-billing/manage/how-to-sign-in-to-hotmail) for shared mailbox realms;
- [EBA credit-institution register](https://www.eba.europa.eu/risk-and-data-analysis/data/registers-and-other-list-institutions/credit-institutions-register), [EBA payment-institution register](https://www.eba.europa.eu/risk-and-data-analysis/data/registers/payment-institutions-register), [Deutsche Bundesbank bank-code lookup](https://www.bundesbank.de/de/startseite/suche/bankleitzahlen-suche), and [BaFin account comparison](https://kontenvergleich.bafin.de/de) for European financial-provider identity;
- [Dashlane recovery options](https://support.dashlane.com/hc/en-us/articles/11282971791634-Account-recovery-options-for-Dashlane), [Keeper recovery guidance](https://www.keepersecurity.com/en_GB/support.html?t=p), [NordPass reset guidance](https://support.nordpass.com/hc/en-us/articles/5388857973905-How-to-reset-NordPass-account), and [1Password account guidance](https://support.1password.com/explore/get-started/) for password-manager account impact;
- [Steam trading and Community Market restrictions](https://help.steampowered.com/en/faqs/view/451E-96B3-D194-50FC) and [Roblox item trading and resale](https://en.help.roblox.com/hc/en-us/articles/206142306-The-Item-Details-Page-and-Purchasing-Items) for the gaming-account asset boundary;
- [BundID](https://id.bund.de/), [ELSTER](https://www.elster.de/eportal/start?locale=de_DE), and the [FranceConnect account explanation](https://aide.franceconnect.gouv.fr/faq/comprendre-franceconnect/creation-compte/) for the government-identity boundary.

These references support family identity and account relevance. They do not delegate unpwn's recovery-priority decision to the provider or a register.

## Excluded and `Unknown` candidates

All candidates not converted into an explicit reviewed family remain `Unknown`. The repository intentionally does not carry a huge runtime rejection list. The principal rejection classes, with representative candidates from the historical material, are:

| Rejection reason | Representative candidates | Decision |
| --- | --- | --- |
| Category keyword or incidental text, not an account provider | `banking`, `streaming`, `news` | `Unknown`; keywords never classify an account |
| Informational, comparison, charity, regulator, or editorial site | `bankrate.com`, `banquealimentaire.org`, many central-bank and press domains | `Unknown`; appearing in a bank/press list does not establish a recoverable account family |
| Disposable or unsuitable recovery root | `10minutemail.com` | `Unknown`; do not recommend a temporary mailbox as a durable recovery root |
| Obsolete service or domain | `orkut.com`, historical webmail brands with no current account realm | `Unknown`; do not infer a successor account without evidence |
| Shared hosting, ISP infrastructure, school, company, or custom-mail domain | large parts of the broad webmail material | `Unknown`; domain ownership does not identify one consumer mailbox realm |
| Ambiguous local or federated identity | unverified local bank domains; `FranceConnect` itself | `Unknown`; FranceConnect uses another provider's account and does not create its own account |
| Plausible provider but insufficiently reviewed ownership or impact | remaining unmatched candidate domains, including `bild.de` | `Unknown`; omission is preferred to guessing |

Individual Sparkassen, cooperative banks, insurers, regional portals, and country-specific service realms were not merged merely because their branding or sector was similar. A future addition must identify the actual account authority and recovery realm.

## Reproduction and safeguards

Maintainers can reproduce the candidate inputs from the repository commits and pinned revisions above. The reviewed output itself is intentionally hand-maintained so that every included family remains visible in code review.

Tests cover representative German, European, and global additions; aliases; regional and legacy domains; conservative exclusions; culture-independent matching; explicit user overrides; and the strict canonical ID/domain/alias collision boundary. They also demonstrate that a catalog classification can exist without a reviewed workflow or browser-automation capability. Classification says **when** to prioritize an account, never **how** to recover it.
