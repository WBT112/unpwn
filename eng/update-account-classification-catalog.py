#!/usr/bin/env python3
"""Generate unpwn's repository-controlled offline account classification catalog.

Network access happens only when a maintainer runs this updater. Runtime
classification reads the committed TSV and never contacts either upstream.
"""

from __future__ import annotations

import json
import re
import subprocess
import tarfile
import tempfile
from dataclasses import dataclass, field
from fnmatch import fnmatch
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA_DIR = ROOT / "src" / "Unpwn.Core" / "Data"
CATALOG_PATH = DATA_DIR / "account-classification-catalog.tsv"
META_PATH = DATA_DIR / "account-classification-catalog.meta.json"

CATALOG_VERSION = "2026.08.2"
DLC_REPOSITORY = "https://github.com/v2fly/domain-list-community.git"
DLC_COMMIT = "6f8a5b43db087ae27decef85d80229850bbd40b1"
EMAIL_PACKAGE = "email-providers"
EMAIL_PACKAGE_VERSION = "2.24.0"
MINIMUM_COUNTS = {"Email": 100, "Critical": 1000, "NonCritical": 1000}
MAXIMUM_COUNTS = {"Email": 500, "Critical": 1600, "NonCritical": 1600}
CATEGORY_PRIORITY = {"Email": 0, "Critical": 1, "NonCritical": 2}

# These are explicit unpwn recovery-priority policy inputs, not risk labels asserted
# by domain-list-community. When uncertain, services with identity, communications,
# work/education or payment impact are conservatively recovered earlier.
CRITICAL_PATTERNS = (
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
    "category-dev*",
    "category-forums*",
    "category-ai*",
    "category-education*",
    "category-scholar*",
    "category-travel*",
)
NONCRITICAL_PATTERNS = (
    "category-entertainment*",
    "category-games*",
    "category-media*",
    "category-news*",
    "category-sports*",
    "category-anime*",
    "category-comics*",
    "category-music*",
    "category-weather*",
    "category-reading*",
)

EMAIL_FAMILIES: dict[str, tuple[str, tuple[str, ...]]] = {
    "gmail": ("Gmail", ("gmail.com", "googlemail.com")),
    "microsoft-outlook": (
        "Microsoft Outlook",
        ("outlook.com", "hotmail.com", "live.com", "msn.com", "outlook.de", "outlook.fr",
         "outlook.co.uk", "live.de", "live.co.uk", "hotmail.de", "hotmail.co.uk"),
    ),
    "yahoo-mail": (
        "Yahoo Mail",
        ("yahoo.com", "yahoo.de", "yahoo.fr", "yahoo.it", "yahoo.es", "yahoo.co.uk",
         "yahoo.co.jp", "yahoo.ca", "yahoo.com.au", "yahoo.co.in", "ymail.com", "rocketmail.com"),
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

CURATED_RECORDS = (
    ("webde", "WEB.DE", "Email", ("web.de",), ("webde",)),
    ("mailboxorg", "mailbox.org", "Email", ("mailbox.org",), ("mailboxorg",)),
    ("freenet", "Freenet Mail", "Email", ("freenet.de",), ("freenet",)),
    ("tonline", "T-Online Mail", "Email", ("t-online.de",), ("tonline",)),
    ("fastmail", "Fastmail", "Email", ("fastmail.com", "fastmail.fm"), ("fastmail",)),
    ("deutschebank", "Deutsche Bank", "Critical", ("deutsche-bank.de",), ("deutschebank",)),
    ("commerzbank", "Commerzbank", "Critical", ("commerzbank.de",), ("commerzbank",)),
    ("n26", "N26", "Critical", ("n26.com",), ("n26",)),
    ("paypal", "PayPal", "Critical", ("paypal.com",), ("paypal", "payments")),
    ("amazon", "Amazon", "Critical", ("amazon.com", "amazon.de"), ("amazon", "commerce")),
    ("ebay", "eBay", "Critical", ("ebay.com", "ebay.de"), ("ebay", "marketplace")),
    ("github", "GitHub", "Critical", ("github.com",), ("github",)),
    ("google", "Google", "Critical", ("google.com",), ("google",)),
    ("microsoft", "Microsoft", "Critical", ("microsoft.com",), ("microsoft",)),
    ("apple", "Apple", "Critical", ("apple.com",), ("apple",)),
    ("bitwarden", "Bitwarden", "Critical", ("bitwarden.com",), ("bitwarden", "passwordmanager")),
    ("1password", "1Password", "Critical", ("1password.com",), ("1password",)),
    ("discord", "Discord", "Critical", ("discord.com",), ("discord", "communications")),
    ("reddit", "Reddit", "Critical", ("reddit.com",), ("reddit",)),
    ("netflix", "Netflix", "NonCritical", ("netflix.com",), ("netflix", "streaming")),
    ("spotify", "Spotify", "NonCritical", ("spotify.com",), ("spotify",)),
    ("duolingo", "Duolingo", "NonCritical", ("duolingo.com",), ("duolingo",)),
    ("goodreads", "Goodreads", "NonCritical", ("goodreads.com",), ("goodreads",)),
    ("imdb", "IMDb", "NonCritical", ("imdb.com",), ("imdb",)),
    ("medium", "Medium", "NonCritical", ("medium.com",), ("medium",)),
    ("pinterest", "Pinterest", "NonCritical", ("pinterest.com",), ("pinterest",)),
    ("twitch", "Twitch", "NonCritical", ("twitch.tv",), ("twitch",)),
    ("allrecipes", "Allrecipes", "NonCritical", ("allrecipes.com",), ("allrecipes", "recipes")),
)

DNS_RE = re.compile(
    r"^(?=.{1,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+"
    r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$",
    re.I,
)


@dataclass
class ProviderRecord:
    provider_id: str
    display_name: str
    category: str
    domains: set[str] = field(default_factory=set)
    aliases: set[str] = field(default_factory=set)
    sources: set[str] = field(default_factory=set)


def run(*args: str, cwd: Path | None = None) -> str:
    return subprocess.run(args, cwd=cwd, check=True, text=True, capture_output=True).stdout.strip()


def provider_alias(value: str) -> str:
    return "".join(ch for ch in value.strip().lower() if ch.isalnum())


def stable_id(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-") or "provider"


def humanize(value: str) -> str:
    return " ".join(part[:1].upper() + part[1:] for part in re.split(r"[-_]+", value) if part)


def domain(value: str) -> str | None:
    value = value.strip().lower().rstrip(".")
    if not value or "://" in value or "/" in value or "*" in value:
        return None
    try:
        value = value.encode("idna").decode("ascii")
    except UnicodeError:
        return None
    return value if DNS_RE.fullmatch(value) else None


def tokens(raw: str) -> list[str]:
    value = raw.split("#", 1)[0].strip()
    return value.split() if value else []


def direct_domains(lines: list[str]) -> set[str]:
    result: set[str] = set()
    for raw in lines:
        values = tokens(raw)
        if not values or "@ads" in values:
            continue
        value = values[0]
        if value.startswith(("include:", "keyword:", "regexp:")):
            continue
        value = value.removeprefix("domain:").removeprefix("full:")
        normalized = domain(value)
        if normalized:
            result.add(normalized)
    return result


def includes(lines: list[str]) -> set[str]:
    return {
        values[0].removeprefix("include:")
        for raw in lines
        if (values := tokens(raw)) and values[0].startswith("include:")
    }


def clone_domain_lists(work: Path) -> dict[str, list[str]]:
    checkout = work / "domain-list-community"
    run("git", "clone", "--filter=blob:none", "--no-checkout", DLC_REPOSITORY, str(checkout))
    run("git", "checkout", DLC_COMMIT, cwd=checkout)
    data_dir = checkout / "data"
    files: dict[str, list[str]] = {}
    for path in sorted(data_dir.iterdir()):
        if not path.is_file():
            continue
        if path.name in files:
            raise RuntimeError(f"Duplicate domain-list filename: {path.name}")
        files[path.name] = path.read_text(encoding="utf-8").splitlines()
    return files


def category_roots(files: dict[str, list[str]], patterns: tuple[str, ...]) -> list[str]:
    return sorted(name for name in files if any(fnmatch(name, pattern) for pattern in patterns))


def category_records(
    files: dict[str, list[str]], roots: list[str], category: str
) -> dict[str, ProviderRecord]:
    records: dict[str, ProviderRecord] = {}
    selected_categories: set[str] = set()

    def add_service(name: str, root: str) -> None:
        service_lines = files.get(name)
        if service_lines is None:
            return
        key = stable_id(name)
        record = records.setdefault(key, ProviderRecord(key, humanize(name), category))
        record.domains.update(direct_domains(service_lines))
        record.aliases.add(name)
        record.sources.add(f"dlc:{root}/{name}")
        pending = list(includes(service_lines))
        seen: set[str] = set()
        while pending:
            helper = pending.pop()
            if helper in seen or helper.startswith("category-"):
                continue
            seen.add(helper)
            helper_lines = files.get(helper)
            if helper_lines is not None:
                record.domains.update(direct_domains(helper_lines))
                pending.extend(includes(helper_lines))

    def walk(name: str, root: str, visited: set[str]) -> None:
        if name in visited or name not in files:
            return
        visited.add(name)
        selected_categories.add(name)
        for child in sorted(includes(files[name])):
            if child.startswith("category-"):
                walk(child, root, visited)
            else:
                add_service(child, root)
        for item in sorted(direct_domains(files[name])):
            key = "domain-" + stable_id(item)
            record = records.setdefault(key, ProviderRecord(key, item, category))
            record.domains.add(item)
            record.sources.add(f"dlc:{root}/{name}")

    for root in roots:
        walk(root, root, set())

    # Affiliations are domain-list-community's mechanism for adding a service rule
    # to a category without a literal include line.
    for name, lines in files.items():
        if name.startswith("category-"):
            continue
        matched_domains: set[str] = set()
        matched_categories: set[str] = set()
        for raw in lines:
            values = tokens(raw)
            if not values:
                continue
            affiliations = {value[1:] for value in values[1:] if value.startswith("&")}
            matched = affiliations.intersection(selected_categories)
            if not matched:
                continue
            value = values[0]
            if value.startswith(("include:", "keyword:", "regexp:")):
                continue
            normalized = domain(value.removeprefix("domain:").removeprefix("full:"))
            if normalized:
                matched_domains.add(normalized)
                matched_categories.update(matched)
        if matched_domains:
            key = stable_id(name)
            record = records.setdefault(key, ProviderRecord(key, humanize(name), category))
            record.domains.update(matched_domains)
            record.aliases.add(name)
            record.sources.update(f"dlc:{root}/{name}" for root in sorted(matched_categories))

    return {key: record for key, record in records.items() if record.domains}


def email_records(work: Path) -> dict[str, ProviderRecord]:
    npm_dir = work / "npm"
    npm_dir.mkdir()
    packed = run("npm", "pack", f"{EMAIL_PACKAGE}@{EMAIL_PACKAGE_VERSION}", "--silent", cwd=npm_dir)
    with tarfile.open(npm_dir / packed.splitlines()[-1], "r:gz") as archive:
        member = archive.extractfile("package/common.json")
        if member is None:
            raise RuntimeError("email-providers package lacks common.json")
        raw_domains = json.load(member)
    available = {normalized for value in raw_domains if (normalized := domain(str(value)))}
    records: dict[str, ProviderRecord] = {}
    consumed: set[str] = set()
    for key, (name, family) in EMAIL_FAMILIES.items():
        family_domains = {normalized for value in family if (normalized := domain(value))}
        records[key] = ProviderRecord(
            key, name, "Email", family_domains, {key}, {"email-providers:common", "unpwn-family-map"}
        )
        consumed.update(family_domains)
    for item in sorted(available - consumed):
        key = "mail-" + stable_id(item)
        records[key] = ProviderRecord(key, item, "Email", {item}, set(), {"email-providers:common"})
    return records


def merge(*groups: dict[str, ProviderRecord]) -> dict[str, ProviderRecord]:
    result: dict[str, ProviderRecord] = {}
    for group in groups:
        for key, incoming in group.items():
            current = result.get(key)
            if current is None:
                result[key] = ProviderRecord(
                    incoming.provider_id, incoming.display_name, incoming.category,
                    set(incoming.domains), set(incoming.aliases), set(incoming.sources)
                )
                continue
            if CATEGORY_PRIORITY[incoming.category] < CATEGORY_PRIORITY[current.category]:
                current.category = incoming.category
                current.display_name = incoming.display_name
            current.domains.update(incoming.domains)
            current.aliases.update(incoming.aliases)
            current.sources.update(incoming.sources)
    return result


def add_curated(records: dict[str, ProviderRecord]) -> None:
    for key, name, category, domains, aliases in CURATED_RECORDS:
        current = records.get(key)
        if current is None or CATEGORY_PRIORITY[category] < CATEGORY_PRIORITY[current.category]:
            current = ProviderRecord(key, name, category)
            records[key] = current
        elif current.category != category:
            continue
        current.display_name = name
        current.domains.update(normalized for value in domains if (normalized := domain(value)))
        current.aliases.update(aliases)
        current.sources.add("unpwn-curated")


def overlaps(first: str, second: str) -> bool:
    return first == second or first.endswith("." + second) or second.endswith("." + first)


def resolve(records: dict[str, ProviderRecord]) -> list[ProviderRecord]:
    accepted_domains: list[tuple[str, str]] = []
    accepted_aliases: dict[str, str] = {}
    result: list[ProviderRecord] = []
    for record in sorted(records.values(), key=lambda item: (CATEGORY_PRIORITY[item.category], item.provider_id)):
        domains: set[str] = set()
        for item in sorted(record.domains, key=lambda value: (value.count("."), len(value), value)):
            if any(owner != record.provider_id and overlaps(item, existing) for existing, owner in accepted_domains):
                continue
            domains.add(item)
        if not domains:
            continue
        aliases: set[str] = set()
        for item in sorted(record.aliases | {record.provider_id}):
            normalized = provider_alias(item)
            owner = accepted_aliases.get(normalized)
            if not normalized or (owner is not None and owner != record.provider_id):
                continue
            accepted_aliases[normalized] = record.provider_id
            aliases.add(item)
        record.domains = domains
        record.aliases = aliases
        accepted_domains.extend((item, record.provider_id) for item in domains)
        result.append(record)
    return result


def select(records: list[ProviderRecord]) -> list[ProviderRecord]:
    curated = {item[0] for item in CURATED_RECORDS}
    result: list[ProviderRecord] = []
    for category in ("Email", "Critical", "NonCritical"):
        candidates = sorted(
            (record for record in records if record.category == category),
            key=lambda record: (record.provider_id not in curated, record.provider_id),
        )
        if len(candidates) < MINIMUM_COUNTS[category]:
            raise RuntimeError(
                f"Catalog generation produced only {len(candidates)} {category} providers; "
                f"minimum is {MINIMUM_COUNTS[category]}."
            )
        result.extend(candidates[: MAXIMUM_COUNTS[category]])
    return sorted(result, key=lambda record: (CATEGORY_PRIORITY[record.category], record.provider_id))


def write(records: list[ProviderRecord], critical_roots: list[str], noncritical_roots: list[str]) -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    rows = ["provider_id\tdisplay_name\tcategory\tdomains\tprovider_aliases\tprovenance"]
    for record in records:
        fields = (
            record.provider_id,
            record.display_name.replace("\t", " "),
            record.category,
            "|".join(sorted(record.domains)),
            "|".join(sorted(record.aliases)),
            "|".join(sorted(record.sources)),
        )
        if any(any(character in field for character in "\t\r\n") for field in fields):
            raise RuntimeError(f"Unsafe TSV field in {record.provider_id}")
        rows.append("\t".join(fields))
    CATALOG_PATH.write_text("\n".join(rows) + "\n", encoding="utf-8")

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
                "license": "MIT",
                "purpose": "Service/owner domain grouping used by unpwn's reviewed Critical/NonCritical mapping.",
                "criticalRoots": critical_roots,
                "nonCriticalRoots": noncritical_roots,
            },
            {
                "id": EMAIL_PACKAGE,
                "version": EMAIL_PACKAGE_VERSION,
                "license": "ISC",
                "purpose": "Common mailbox domains used to seed canonical Email provider records.",
            },
            {
                "id": "unpwn-curated",
                "purpose": "Reviewed continuity records and explicit mailbox-family alias grouping.",
            },
        ],
    }
    META_PATH.write_text(json.dumps(metadata, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(json.dumps(counts, sort_keys=True))


def main() -> None:
    with tempfile.TemporaryDirectory(prefix="unpwn-account-catalog-") as temporary:
        work = Path(temporary)
        files = clone_domain_lists(work)
        critical_roots = category_roots(files, CRITICAL_PATTERNS)
        noncritical_roots = category_roots(files, NONCRITICAL_PATTERNS)
        if not critical_roots or not noncritical_roots:
            raise RuntimeError("Pinned domain-list-community revision lacks required category roots.")
        records = merge(
            email_records(work),
            category_records(files, critical_roots, "Critical"),
            category_records(files, noncritical_roots, "NonCritical"),
        )
        add_curated(records)
        write(select(resolve(records)), critical_roots, noncritical_roots)


if __name__ == "__main__":
    main()
