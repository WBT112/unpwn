# NonCritical account-classification review — August 2026

This document records the follow-up review that produced catalog version `2026.08.5`. It supersedes the previous absence of a numeric coverage goal: the repository now aims for at least 1,000 canonical provider/account realms in each recovery category. The target does not grant classification authority to an external list.

## Result and current progress

The review adds 939 publisher, news, and media consumer account realms to the previous 61 `NonCritical` records. The resulting catalog has:

| Category | Reviewed records | Goal |
| --- | ---: | ---: |
| `Email` | 79 | 1,000 |
| `Critical` | 429 | 1,000 |
| `NonCritical` | 1,000 | 1,000 |

This change deliberately addresses `NonCritical` first. The other category gaps remain visible rather than being filled with disposable mailboxes, custom corporate mail domains, generic bank keywords, or unreviewed domain claims.

## Candidate source and review

Discovery used the `press/domains.top-n` material from the `cbuijs/ut1` mirror pinned at commit `2ddb46bdb691721cadc8e1521abc780396e7aeb3`. UT1 is web-categorization data and remains only a candidate source. The selected derivative data retains its CC BY-SA 4.0 attribution; the unpwn category decision is independent.

The pinned source contained 1,584 prioritized candidates. Review processing:

1. accepted only normalized provider-root shapes, including recognized country-code account roots;
2. rejected subdomains and shared-hosting shapes that did not identify an independent account realm;
3. rejected canonical parent/subdomain overlaps and all domains already owned by a reviewed record;
4. retained 1,524 structurally eligible publisher/media roots;
5. explicitly selected 939 ordinary private reader, listener, or viewer account realms as `NonCritical`.

The checked-in result is an explicit repository-controlled source list. Runtime performs no network lookup and never loads UT1 data. The upstream ordering helps make selection reproducible but carries no recovery-risk meaning.

## Category boundary

`NonCritical` means delayed recovery is a defensible normal default for an ordinary private consumer account. It does not mean that the provider, content, or user is unimportant, and it does not prove that every account at that provider has identical impact.

The following remain outside this cohort:

- gaming accounts with purchases, balance, valuable or tradable inventory, marketplace access, or material social identity;
- social, messaging, creator, and community identities where takeover enables impersonation or publishing;
- work, administrator, newsroom, advertising, or other elevated publisher authority;
- accounts with material payment, identity, health, government, security, or recovery-root control;
- ambiguous shared hosts and candidates that do not identify one account realm.

If a private user's real account has elevated authority or unusual impact, their explicit category override remains canonical and wins over the catalog suggestion.

## Maintenance evidence

Tests enforce the 1,000-record `NonCritical` milestone, representative global publisher classification, strict canonical ownership, conservative unknown fallback, culture-independent matching, and user-override precedence. Future changes must preserve the offline boundary and may exceed the goal only through reviewed canonical additions.

Source: [cbuijs/ut1 pinned review revision](https://github.com/cbuijs/ut1/tree/2ddb46bdb691721cadc8e1521abc780396e7aeb3/press), derived from the Université Toulouse Capitole categorization data under CC BY-SA 4.0.
