#!/usr/bin/env python3
"""Refresh the vendored offline account-classification domain snapshots.

The source revision is intentionally pinned. Runtime classification never calls the network;
this maintenance script is the only networked part of the data-refresh process.
"""

from __future__ import annotations

import argparse
import ipaddress
import pathlib
import re
import urllib.request

SOURCE_REPOSITORY = "cbuijs/ut1"
SOURCE_REVISION = "1b3eb2de2ccef5e85acb5103f70933b59edc51f9"
RAW_BASE = f"https://raw.githubusercontent.com/{SOURCE_REPOSITORY}/{SOURCE_REVISION}"
OUTPUT_ROOT = pathlib.Path("src/Unpwn.Core/Data/AccountClassification")

# Keep a margin above the product minimum after curated-family de-duplication and
# deterministic cross-category collision resolution.
TARGETS = {
    "webmail": 180,
    "bank": 1250,
    "press": 1250,
}

DOMAIN_RE = re.compile(
    r"^(?=.{1,253}\.?$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.?$"
)


def fetch_lines(category: str, filename: str) -> list[str]:
    url = f"{RAW_BASE}/{category}/{filename}"
    request = urllib.request.Request(
        url,
        headers={"User-Agent": "unpwn-account-catalog-maintenance/1"},
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        if response.status != 200:
            raise RuntimeError(f"Unexpected HTTP status for {category}/{filename}: {response.status}")
        payload = response.read(512 * 1024 + 1)
    if len(payload) > 512 * 1024:
        raise RuntimeError(f"Source file exceeds the maintenance bound: {category}/{filename}")
    return payload.decode("utf-8").splitlines()


def normalize_domain(value: str) -> str | None:
    value = value.strip().rstrip(".").lower()
    if not value or len(value) > 253:
        return None
    try:
        value = value.encode("idna").decode("ascii")
        ipaddress.ip_address(value)
        return None
    except ValueError:
        pass
    except UnicodeError:
        return None
    return value if DOMAIN_RE.fullmatch(value) else None


def select_domains(category: str, target: int) -> list[str]:
    selected: list[str] = []
    seen: set[str] = set()
    for filename in ("domains.top-n", "domains"):
        for raw in fetch_lines(category, filename):
            domain = normalize_domain(raw)
            if domain is None or domain in seen:
                continue
            seen.add(domain)
            selected.append(domain)
            if len(selected) >= target:
                return selected
    raise RuntimeError(
        f"Pinned source has only {len(selected)} valid unique {category} domains; need {target}."
    )


def write_snapshot(category: str, domains: list[str]) -> None:
    OUTPUT_ROOT.mkdir(parents=True, exist_ok=True)
    path = OUTPUT_ROOT / f"ut1-{category}.txt"
    path.write_text("\n".join(domains) + "\n", encoding="utf-8", newline="\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--check",
        action="store_true",
        help="Regenerate in memory and fail if the checked-in snapshot differs.",
    )
    args = parser.parse_args()

    generated: dict[str, list[str]] = {
        category: select_domains(category, target)
        for category, target in TARGETS.items()
    }

    if args.check:
        failures: list[str] = []
        for category, domains in generated.items():
            path = OUTPUT_ROOT / f"ut1-{category}.txt"
            expected = "\n".join(domains) + "\n"
            actual = path.read_text(encoding="utf-8") if path.exists() else ""
            if actual != expected:
                failures.append(str(path))
        if failures:
            raise SystemExit("Catalog snapshots are stale: " + ", ".join(failures))
        return

    for category, domains in generated.items():
        write_snapshot(category, domains)
        print(f"{category}: {len(domains)} records")


if __name__ == "__main__":
    main()
