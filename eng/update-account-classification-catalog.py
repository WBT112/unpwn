#!/usr/bin/env python3
"""Generate the repository-controlled offline account classification catalog.

The generated TSV is committed to the repository and used offline at runtime.
This updater intentionally performs network access only when a maintainer runs it.
Upstream inputs are pinned below so generation is reproducible.
"""

from __future__ import annotations

import json
import re
import shutil
import subprocess
import tarfile
import tempfile
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_DIR = ROOT / "src" / "Unpwn.Core" / "Data"
CATALOG_PATH = DATA_DIR / "account-classification-catalog.tsv"
META_PATH = DATA_DIR / "account-classification-catalog.meta.json"

CATALOG_VERSION = "2026.08.2"
DLC_REPOSITORY = "https://github.com/v2fly/domain-list-community.git"
DLC_COMMIT = "6f8a5b43db087ae27decef85d80229850bbd40b1"
DLC_LICENSE = "MIT"
EMAIL_PACKAGE = "email-providers"
EMAIL_PACKAGE_VERSION = "2.24.0"
EMAIL_PACKAGE_LICENSE = "ISC"

MINIMUM_COUNTS = {"Email": 100, "Critical": 1000, "NonCritical": 1000}
MAXIMUM_COUNTS = {"Email": 500, "Critical": 1600, "NonCritical": 1600}
CATEGORY_PRIORITY = {"Email": 0, "Critical": 1, "NonCritical": 2}

CRITICAL_PRIMARY_PATTERNS = (
    "category-bank-*",
    "category-finance*",
    "category-ecommerce*",
    "category-social-media*",
    "category-communication*",
    "category-cloud*",
    "category-vpnservices*",
    "category-cryptocurrency*",
    "category-health*",
    "category-government*",
    "category-identity*",
    "category-password*",
)
CRITICAL_SUPPLEMENTAL_PATTERNS = ("category-dev*",)
NONCRITICAL_PATTERNS = (
    "category-entertainment*",
    "category-games*",
    "category-media*",
    "category-news*",
    "category-sports*",
    "category-scholar*",
    "category-education*",
    "category-anime*",
    "category-comics*",
    "category-music*",
    "category-weather*",
    "category-reading*",
)

# Known mailbox families whose regional/legacy domains must not inflate provider counts.
EMAIL_FAMILIES: dict[str, tuple[str, tuple[str, ...]]] = {
    "gmail": ("Gmail", ("gmail.com", "googlemail.com")),
    "microsoft-outlook": (
        "Microsoft Outlook",
        (
            "outlook.com", "hotmail.com", "live.com", "msn.com", "outlook.de", "outlook.fr",
            "outlook.co.uk", "live.de", "live.co.uk", "hotmail.de", "hotmail.co.uk",
        ),
    ),
    "yahoo-mail": (
        "Yahoo Mail",
        (
            "yahoo.com", "yahoo.de", "yahoo.fr", "yahoo.it", "yahoo.es", "yahoo.co.uk",
            "yahoo.co.jp", "yahoo.ca", "yahoo.com.au", "yahoo.co.in", "ymail.com", "rocketmail.com",
        ),
    ),
    "apple-icloud-mail": ("Apple iCloud Mail", ("icloud.com", "me.com", "mac.com")),
    "gmx": ("GMX", ("gmx.de", "gmx.net", "gmx.com", "gmx.at", "gmx.ch")),
    "proton-mail": ("Proton Mail", ("proton.me", "protonmail.com", "protonmail.ch")),
    "tuta-mail": ("Tuta Mail", ("tuta.com", "tutanota.com", "tutamail.com", "tuta.io")),
    "zoho-mail": ("Zoho Mail", ("zoho.com", "zohomail.com")),
    "aol-mail": ("AOL Mail", ("aol.com", "aol.de")),
    "mailru": ("Mail.ru", ("mail.ru", "inbox.ru", "list.ru", "bk.ru")),
    "yandex-mail": ("Yandex Mail", ("yandex.com", "yandex.ru", "ya.ru")),
}

# Reviewed continuity records retained from the original unpwn catalog. They also make
# representative global/German/European behavior stable even if an upstream category moves.
CURATED_RECORDS = (
    ("webde", "WEB.DE", "Email", ("web.de",), ("webde",), "unpwn-curated"),
    ("mailboxorg", "mailbox.org", "Email", ("mailbox.org",), ("mailboxorg",), "unpwn-curated"),
    ("freenet", "Freenet Mail", "Email", ("freenet.de",), ("freenet",), "unpwn-curated"),
    ("tonline", "T-Online Mail", "Email", ("t-online.de",), ("tonline",), "unpwn-curated"),
    ("fastmail", "Fastmail", "Email", ("fastmail.com", "fastmail.fm"), ("fastmail",), "unpwn-curated"),
    ("deutschebank", "Deutsche Bank", "Critical", ("deutsche-bank.de",), ("deutschebank",), "unpwn-curated"),
    ("commerzbank", "Commerzbank", "Critical", ("commerzbank.de",), ("commerzbank",), "unpwn-curated"),
    ("n26", "N26", "Critical", ("n26.com",), ("n26",), "unpwn-curated"),
    ("paypal", "PayPal", "Critical", ("paypal.com",), ("paypal", "payments"), "unpwn-curated"),
    ("amazon", "Amazon", "Critical", ("amazon.com", "amazon.de"), ("amazon", "commerce"), "unpwn-curated"),
    ("ebay", "eBay", "Critical", ("ebay.com", "ebay.de"), ("ebay", "marketplace"), "unpwn-curated"),
    ("github", "GitHub", "Critical", ("github.com",), ("github",), "unpwn-curated"),
    ("google", "Google", "Critical", ("google.com",), ("google",), "unpwn-curated"),
    ("microsoft", "Microsoft", "Critical", ("microsoft.com",), ("microsoft",), "unpwn-curated"),
    ("apple", "Apple", "Critical", ("apple.com",), ("apple",), "unpwn-curated"),
    ("bitwarden", "Bitwarden", "Critical", ("bitwarden.com",), ("bitwarden", "passwordmanager"), "unpwn-curated"),
    ("1password", "1Password", "Critical", ("1password.com",), ("1password",), "unpwn-curated"),
    ("discord", "Discord", "Critical", ("discord.com",), ("discord", "communications"), "unpwn-curated"),
    ("reddit", "Reddit", "Critical", ("reddit.com",), ("reddit",), "unpwn-curated"),
    ("netflix", "Netflix", "NonCritical", ("netflix.com",), ("netflix", "streaming"), "unpwn-curated"),
    ("spotify", "Spotify", "NonCritical", ("spotify.com",), ("spotify",), "unpwn-curated"),
    ("duolingo", "Duolingo", "NonCritical", ("duolingo.com",), ("duolingo",), "unpwn-curated"),
    ("goodreads", "Goodreads", "NonCritical", ("goodreads.com",), ("goodreads",), "unpwn-curated"),
    ("imdb", "IMDb", "NonCritical", ("imdb.com",), ("imdb",), "unpwn-curated"),
    ("medium", "Medium", "NonCritical", ("medium.com",), ("medium",), "unpwn-curated"),
    ("pinterest", "Pinterest", "NonCritical", ("pinterest.com",), ("pinterest",), "unpwn-curated"),
    ("twitch", "Twitch", "NonCritical", ("twitch.tv",), ("twitch",), "unpwn-curated"),
    ("allrecipes", "Allrecipes", "NonCritical", ("allrecipes.com",), ("allrecipes", "recipes"), "unpwn-curated"),
)

DNS_RE = re.compile(r"^(?=.{1,253}\.?$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.?$", re.I)


@dataclass
class ProviderRecord:
    provider_id: str
    display_name: str
    category: str
    domains: set[str] = field(default_factory=set)
    aliases: set[str] = field(default_factory=set)
    sources: set[str] = field(default_factory=set)


def run(*args: str, cwd: Path | None = None) -> str:
    completed = subprocess.run(args, cwd=cwd, check=True, text=True, capture_output=True)
    return completed.stdout.strip()


def normalize_provider_id(value: str) -> str:
    return "".join(ch for ch in value.strip().lower() if ch.isalnum())


def stable_id(value: str) -> str:
    value = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    return value or "provider"


def humanize(value: str) -> str:
    words = re.split(r"[-_]+", value)
    return " ".join(word.upper() if len(word) <= 3 and word.isalpha() else word[:1].upper() + word[1:] for word in words)


def normalize_domain(value: str) -> str | None:
    value = value.strip().lower().rstrip(".")
    if not value or "://" in value or "/" in value or "*" in value:
        return None
    try:
        ascii_value = value.encode("idna").decode("ascii")
    except UnicodeError:
        return None
    if not DNS_RE.fullmatch(ascii_value):
        return None
    return ascii_value


def clone_dlc(work: Path) -> Path:
    checkout = work / "domain-list-community"
    run("git", "clone", "--filter=blob:none", "--no-checkout", DLC_REPOSITORY, str(checkout))
    run("git", "checkout", DLC_COMMIT, cwd=checkout)
    return checkout / "data"


def load_dlc_files(data_dir: Path) -> dict[str, list[str]]:
    result: dict[str, list[str]] = defaultdict(list)
    for path in sorted(data_dir.rglob("*")):
        if path.is_file():
            result[path.name].extend(path.read_text(encoding="utf-8").splitlines())
    return dict(result)


def clean_line(raw: str) -> list[str]:
    line = raw.split("#", 1)[0].strip()
    return line.split() if line else []


def direct_domains(lines: list[str]) -> set[str]:
    result: set[str] = set()
    for raw in lines:
        tokens = clean_line(raw)
        if not tokens:
            continue
        token = tokens[0]
        if token.startswith(("include:", "keyword:", "regexp:")):
            continue
        if "@ads" in tokens:
            continue
        if token.startswith("domain:"):
            token = token.removeprefix("domain:")
        elif token.startswith("full:"):
            token = token.removeprefix("full:")
        domain = normalize_domain(token)
        if domain:
            result.add(domain)
    return result


def includes(lines: list[str]) -> set[str]:
    result: set[str] = set()
    for raw in lines:
        tokens = clean_line(raw)
        if tokens and tokens[0].startswith("include:"):
            target = tokens[0].removeprefix("include:").strip()
            if target:
                result.add(target)
    return result


def category_names(files: dict[str, list[str]], patterns: tuple[str, ...]) -> list[str]:
    from fnmatch import fnmatch
    return sorted(name for name in files if any(fnmatch(name, pattern) for pattern in patterns))


def category_provider_candidates(
    files: dict[str, list[str]],
    root_names: list[str],
    category: str,
) -> dict[str, ProviderRecord]:
    records: dict[str, ProviderRecord] = {}
    visited_categories: set[str] = set()

    def provider_for_file(name: str, source_root: str) -> None:
        if name not in files:
            return
        provider_id = stable_id(name)
        record = records.setdefault(
            provider_id,
            ProviderRecord(provider_id, humanize(name), category),
        )
        record.domains.update(direct_domains(files[name]))
        record.aliases.add(name)
        record.sources.add(f"dlc:{source_root}/{name}")
        # Service/owner files may include helper lists. Treat those domains as aliases of
        # the same canonical service rather than counting helper files as extra providers.
        pending = list(includes(files[name]))
        seen_helpers: set[str] = set()
        while pending:
            helper = pending.pop()
            if helper in seen_helpers or helper.startswith("category-"):
                continue
            seen_helpers.add(helper)
            helper_lines = files.get(helper)
            if helper_lines is None:
                continue
            record.domains.update(direct_domains(helper_lines))
            pending.extend(includes(helper_lines))

    def walk_category(name: str, root_name: str) -> None:
        if name in visited_categories or name not in files:
            return
        visited_categories.add(name)
        lines = files[name]
        for included in sorted(includes(lines)):
            if included.startswith("category-"):
                walk_category(included, root_name)
            else:
                provider_for_file(included, root_name)
        for domain in sorted(direct_domains(lines)):
            provider_id = "domain-" + stable_id(domain)
            record = records.setdefault(provider_id, ProviderRecord(provider_id, domain, category))
            record.domains.add(domain)
            record.sources.add(f"dlc:{root_name}/{name}")

    for root_name in root_names:
        walk_category(root_name, root_name)

    # Affiliations add a rule to a category without an include. If any selected category
    # is referenced by affiliation, keep the source service as one provider record.
    selected = visited_categories | set(root_names)
    for name, lines in files.items():
        if name.startswith("category-"):
            continue
        matched_roots: set[str] = set()
        affiliated_domains: set[str] = set()
        for raw in lines:
            tokens = clean_line(raw)
            if not tokens:
                continue
            affiliations = {token[1:] for token in tokens[1:] if token.startswith("&")}
            if not affiliations.intersection(selected):
                continue
            token = tokens[0]
            if token.startswith("domain:"):
                token = token.removeprefix("domain:")
            elif token.startswith("full:"):
                token = token.removeprefix("full:")
            elif token.startswith(("include:", "keyword:", "regexp:")):
                continue
            domain = normalize_domain(token)
            if domain:
                affiliated_domains.add(domain)
                matched_roots.update(affiliations.intersection(selected))
        if affiliated_domains:
            provider_id = stable_id(name)
            record = records.setdefault(provider_id, ProviderRecord(provider_id, humanize(name), category))
            record.domains.update(affiliated_domains)
            record.aliases.add(name)
            record.sources.update(f"dlc:{root}/{name}" for root in sorted(matched_roots))

    return {key: value for key, value in records.items() if value.domains}


def load_common_email_domains(work: Path) -> list[str]:
    npm_dir = work / "npm"
    npm_dir.mkdir()
    tarball_name = run("npm", "pack", f"{EMAIL_PACKAGE}@{EMAIL_PACKAGE_VERSION}", "--silent", cwd=npm_dir)
    tarball = npm_dir / tarball_name.splitlines()[-1]
    with tarfile.open(tarball, "r:gz") as archive:
        member = archive.extractfile("package/common.json")
        if member is None:
            raise RuntimeError("email-providers package does not contain common.json")
        values = json.load(member)
    domains = sorted({domain for value in values if (domain := normalize_domain(str(value)))})
    return domains


def email_candidates(domains: list[str]) -> dict[str, ProviderRecord]:
    records: dict[str, ProviderRecord] = {}
    consumed: set[str] = set()
    available = set(domains)
    for provider_id, (name, family_domains) in EMAIL_FAMILIES.items():
        matched = {domain for domain in family_domains if domain in available}
        # Retain reviewed family aliases even when a package revision temporarily omits one.
        matched.update(domain for domain in family_domains if normalize_domain(domain))
        record = ProviderRecord(provider_id, name, "Email", matched, {provider_id}, {"email-providers:common", "unpwn-family-map"})
        records[provider_id] = record
        consumed.update(matched)

    for domain in domains:
        if domain in consumed:
            continue
        provider_id = "mail-" + stable_id(domain)
        records[provider_id] = ProviderRecord(
            provider_id,
            domain,
            "Email",
            {domain},
            set(),
            {"email-providers:common"},
        )
    return records


def apply_curated(records: dict[str, ProviderRecord]) -> None:
    for provider_id, name, category, domains, aliases, source in CURATED_RECORDS:
        existing = records.get(provider_id)
        if existing is None or CATEGORY_PRIORITY[category] < CATEGORY_PRIORITY[existing.category]:
            existing = ProviderRecord(provider_id, name, category)
            records[provider_id] = existing
        elif existing.category != category:
            continue
        existing.display_name = name
        existing.domains.update(filter(None, (normalize_domain(domain) for domain in domains)))
        existing.aliases.update(aliases)
        existing.sources.add(source)


def merge_candidates(*groups: dict[str, ProviderRecord]) -> dict[str, ProviderRecord]:
    merged: dict[str, ProviderRecord] = {}
    for group in groups:
        for provider_id, incoming in group.items():
            current = merged.get(provider_id)
            if current is None:
                merged[provider_id] = ProviderRecord(
                    incoming.provider_id,
                    incoming.display_name,
                    incoming.category,
                    set(incoming.domains),
                    set(incoming.aliases),
                    set(incoming.sources),
                )
                continue
            if CATEGORY_PRIORITY[incoming.category] < CATEGORY_PRIORITY[current.category]:
                current.category = incoming.category
                current.display_name = incoming.display_name
            current.domains.update(incoming.domains)
            current.aliases.update(incoming.aliases)
            current.sources.update(incoming.sources)
    return merged


def overlaps(a: str, b: str) -> bool:
    return a == b or a.endswith("." + b) or b.endswith("." + a)


def resolve_alias_collisions(records: dict[str, ProviderRecord]) -> list[ProviderRecord]:
    ordered = sorted(records.values(), key=lambda item: (CATEGORY_PRIORITY[item.category], item.provider_id))
    accepted_domains: list[tuple[str, str]] = []
    accepted_aliases: dict[str, str] = {}
    result: list[ProviderRecord] = []

    for record in ordered:
        clean_domains: set[str] = set()
        for domain in sorted(record.domains, key=lambda item: (item.count("."), len(item), item)):
            if any(owner != record.provider_id and overlaps(domain, existing) for existing, owner in accepted_domains):
                continue
            clean_domains.add(domain)
        if not clean_domains:
            continue

        clean_aliases: set[str] = set()
        for alias in sorted(record.aliases | {record.provider_id}):
            normalized = normalize_provider_id(alias)
            if not normalized:
                continue
            owner = accepted_aliases.get(normalized)
            if owner is not None and owner != record.provider_id:
                continue
            accepted_aliases[normalized] = record.provider_id
            clean_aliases.add(alias)

        record.domains = clean_domains
        record.aliases = clean_aliases
        for domain in clean_domains:
            accepted_domains.append((domain, record.provider_id))
        result.append(record)

    return result


def cap_and_validate(records: list[ProviderRecord]) -> list[ProviderRecord]:
    curated_ids = {item[0] for item in CURATED_RECORDS}
    final: list[ProviderRecord] = []
    for category in ("Email", "Critical", "NonCritical"):
        candidates = [record for record in records if record.category == category]
        candidates.sort(key=lambda record: (record.provider_id not in curated_ids, record.provider_id))
        limit = MAXIMUM_COUNTS[category]
        selected = candidates[:limit]
        if len(selected) < MINIMUM_COUNTS[category]:
            raise RuntimeError(
                f"Catalog generation produced only {len(selected)} {category} providers; "
                f"minimum is {MINIMUM_COUNTS[category]}."
            )
        final.extend(selected)
    return sorted(final, key=lambda record: (CATEGORY_PRIORITY[record.category], record.provider_id))


def write_catalog(records: list[ProviderRecord], critical_roots: list[str], noncritical_roots: list[str]) -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    lines = ["provider_id\tdisplay_name\tcategory\tdomains\tprovider_aliases\tprovenance"]
    for record in records:
        fields = (
            record.provider_id,
            record.display_name.replace("\t", " "),
            record.category,
            "|".join(sorted(record.domains)),
            "|".join(sorted(record.aliases)),
            "|".join(sorted(record.sources)),
        )
        if any("\t" in field or "\n" in field or "\r" in field for field in fields):
            raise RuntimeError(f"Invalid TSV field in {record.provider_id}")
        lines.append("\t".join(fields))
    CATALOG_PATH.write_text("\n".join(lines) + "\n", encoding="utf-8")

    counts = {category: sum(record.category == category for record in records) for category in MINIMUM_COUNTS}
    metadata = {
        "catalogVersion": CATALOG_VERSION,
        "counts": counts,
        "countingRule": "One canonical provider/service record counts once; domains and provider aliases never count as providers.",
        "precedence": ["Email", "Critical", "NonCritical", "Unknown"],
        "sources": [
            {
                "id": "v2fly/domain-list-community",
                "commit": DLC_COMMIT,
                "license": DLC_LICENSE,
                "purpose": "Service/owner domain grouping and category source material for Critical/NonCritical suggestions.",
                "criticalRoots": critical_roots,
                "nonCriticalRoots": noncritical_roots,
            },
            {
                "id": EMAIL_PACKAGE,
                "version": EMAIL_PACKAGE_VERSION,
                "license": EMAIL_PACKAGE_LICENSE,
                "purpose": "Common mailbox-provider domains used to seed Email provider records.",
            },
            {
                "id": "unpwn-curated",
                "purpose": "Reviewed continuity records and explicit mailbox-family alias grouping maintained in the generator.",
            },
        ],
    }
    META_PATH.write_text(json.dumps(metadata, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(metadata["counts"], sort_keys=True))


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="unpwn-account-catalog-") as temp:
        work = Path(temp)
        dlc_data = clone_dlc(work)
        files = load_dlc_files(dlc_data)
        critical_primary_roots = category_names(files, CRITICAL_PRIMARY_PATTERNS)
        critical_supplemental_roots = category_names(files, CRITICAL_SUPPLEMENTAL_PATTERNS)
        noncritical_roots = category_names(files, NONCRITICAL_PATTERNS)
        if not critical_primary_roots or not noncritical_roots:
            raise RuntimeError("Pinned domain-list-community revision lacks required category roots.")

        critical = category_provider_candidates(files, critical_primary_roots, "Critical")
        # Add developer services only when the higher-confidence categories cannot satisfy the
        # required breadth. This keeps the recovery-priority policy conservative by default.
        if len(critical) < MINIMUM_COUNTS["Critical"]:
            supplemental = category_provider_candidates(files, critical_supplemental_roots, "Critical")
            critical = merge_candidates(critical, supplemental)
            critical_roots = critical_primary_roots + critical_supplemental_roots
        else:
            critical_roots = critical_primary_roots

        noncritical = category_provider_candidates(files, noncritical_roots, "NonCritical")
        email = email_candidates(load_common_email_domains(work))
        merged = merge_candidates(email, critical, noncritical)
        apply_curated(merged)
        resolved = resolve_alias_collisions(merged)
        final = cap_and_validate(resolved)
        write_catalog(final, critical_roots, noncritical_roots)


if __name__ == "__main__":
    main()
