#!/usr/bin/env python3
"""Bulk-imports the reference Google Sheet's full card catalog into
src/DiceFight.Engine/Data/BulkCards.json.

Re-run this whenever new sets get added to the sheet - it always
re-fetches all 49 tabs fresh and regenerates the JSON file from
scratch (no incremental state to get out of sync). See DESIGN_LOG.md's
"bulk-import the full reference sheet" status update for the full
methodology and the numbers this produced the first time it ran.

Usage: python3 scripts/import_bulk_cards.py
Requires network access to docs.google.com (no auth - the sheet is
publicly viewable). No third-party dependencies - stdlib only.
"""
import csv
import io
import json
import re
import urllib.request
from collections import Counter
from pathlib import Path

SHEET_ID = "19_XaXj37QQSoCvyVdiUFBml4cnUqk3H5YtrO_Di871A"
REPO_ROOT = Path(__file__).resolve().parent.parent
OUTPUT_PATH = REPO_ROOT / "src" / "DiceFight.Engine" / "Data" / "BulkCards.json"
SAMPLE_CARDS_PATH = REPO_ROOT / "src" / "DiceFight.Engine" / "Data" / "SampleCards.cs"

# All known set tab names (== short set codes) per the SetInfo tab -
# see the dicefight2026-stats-spreadsheet memory.
SET_CODES = [
    "PROMO", "AvX", "UXM", "YGO", "BFF", "JL", "AOU", "WOL", "ASM", "FUS", "WF", "CW",
    "TMNT", "GAF", "DRS", "DP", "HHS", "IMW", "DEF", "BAT", "SWW", "SMC", "GOTG", "XFC",
    "TOA", "THOR", "AI", "HQ", "KI", "JLL", "BFU", "ORK", "SW", "DOOM", "JUS", "MYST",
    "XMF", "XFO", "DXM", "AIW", "TIW", "ZHN", "WWE", "TAG", "BIT", "IG", "DPS", "SKC",
    "MSW",
]

# Keyword names that need zero AbilityDef in this engine - see
# DieStats.HasKeyword's callers and each matching hand-curated card's
# own "no AbilityDef needed" comment in SampleCards.cs. A card's
# ability text auto-qualifies for IsImplemented: true only if, after
# whitespace normalization, the ENTIRE text is one or two of these
# back to back (each with its own optional parenthetical reminder
# text) - not a prefix match. Grow this list as more keywords become
# fully engine-built.
PURE_KEYWORDS = [
    "Overcrush", "Deadly", "Regenerate", "Swarm", "Fast", "Energy Drain",
    "Infiltrate", "Obscure", "Tag Out", "Strike", "Ally", "Experience",
]
PARAM_KEYWORDS = ["Range", "Corrupt"]  # "Range X" / "Corrupt X"

VALID_ENERGY = {"Fist", "Bolt", "Mask", "Shield"}
BASIC_ACTION_SUBS = {"Basic Action Card", "Basic Action Cards", "Basic Action"}
EPIC_SUBS = {"Epic Basic Action"}
ID_RE = re.compile(r"^[A-Za-z]+\d+$")
BURST_ONLY_RE = re.compile(r"^[-*]+(\s[-*]+){2}$")
FACE_RE = re.compile(r"^(\*{0,2})(\d)(\d)(\d)(\*{0,2})$")
RARITY_TO_DIE_LIMIT = {"Common": 4, "Uncommon": 3, "Rare": 2, "Super": 1, "Super-Rare": 1, "Chase": 1}

_single_kw_alts = [re.escape(k) for k in PURE_KEYWORDS] + [re.escape(k) + r" \d+" for k in PARAM_KEYWORDS]
_one_kw = r"(?:" + "|".join(_single_kw_alts) + r")(?:\s*\([^()]*\)\.?)?"
STRICT_KEYWORD_RE = re.compile(r"^" + _one_kw + r"(?:\s+" + _one_kw + r")*\.?$")
LOOSE_KEYWORD_RE = re.compile(r"(?:" + "|".join(_single_kw_alts) + r")")


def norm(s):
    return re.sub(r"\s+", " ", s or "").strip()


def fetch_set_csv(code):
    url = f"https://docs.google.com/spreadsheets/d/{SHEET_ID}/gviz/tq?tqx=out:csv&sheet={code}"
    with urllib.request.urlopen(url, timeout=30) as resp:
        return resp.read().decode("utf-8")


def existing_hand_curated_ids():
    text = SAMPLE_CARDS_PATH.read_text(encoding="utf-8")
    return set(re.findall(r'^\s*"([A-Z]+\d+)",', text, re.MULTILINE))


def parse_faces(statline):
    groups = statline.split()
    if len(groups) != 3:
        return None
    faces = []
    for g in groups:
        m = FACE_RE.match(g)
        if not m:
            return None
        lead, f, a, d, trail = m.groups()
        stars = len(lead) + len(trail)
        faces.append({
            "fieldingCost": int(f), "attack": int(a), "defense": int(d),
            "burstStars": stars if stars else None,
        })
    return faces


def parse_affiliations(raw):
    raw = norm(raw)
    if not raw or raw == "None":
        return []
    if "\n" in raw:
        # "a: Batman Family\nb: Villains" - one entry per lettered line
        parts = []
        for line in raw.split("\n"):
            line = re.sub(r"^[a-z]:\s*", "", line.strip())
            if line:
                parts.append(line)
        return parts
    if "/" in raw:
        return [p.strip() for p in raw.split("/") if p.strip()]
    return [raw]


def classify_ability(ability):
    ability = norm(ability)
    if not ability:
        return True, []
    keywords = sorted(set(m.group(0).split()[0] if " " not in m.group(0) else m.group(0)
                           for m in LOOSE_KEYWORD_RE.finditer(ability)))
    # Normalize "Range 2" -> "Range", "Corrupt 2" -> "Corrupt" for the Keywords list
    keywords = sorted(set(re.sub(r" \d+$", "", k) for k in keywords))
    is_implemented = bool(STRICT_KEYWORD_RE.match(ability))
    return is_implemented, keywords


def classify_row(code, row):
    if len(row) < 9 or not row[0].strip():
        return None, "empty row"
    card_id = row[0].strip()
    name = norm(row[1])
    subtitle = norm(row[2])
    cost_raw = row[3].strip()
    energy_raw = norm(row[4])
    rarity = norm(row[5])
    ability = norm(row[7])
    statline = norm(row[8])

    if not ID_RE.match(card_id):
        return None, "bad id format"

    if subtitle in BASIC_ACTION_SUBS:
        card_type = "BasicAction"
    elif subtitle in EPIC_SUBS:
        card_type = "EpicBasicAction"
    elif BURST_ONLY_RE.match(statline):
        card_type = "Action"
    else:
        card_type = "Character"

    levels = None
    die_limit = 3
    energy_type = None

    if card_type == "Character":
        if energy_raw not in VALID_ENERGY:
            return None, "character bad/multi/missing energy"
        levels = parse_faces(statline)
        if levels is None:
            return None, "character unparseable stat line"
        if rarity not in RARITY_TO_DIE_LIMIT:
            return None, "unknown rarity"
        die_limit = RARITY_TO_DIE_LIMIT[rarity]
        energy_type = energy_raw
    elif card_type == "Action":
        if energy_raw not in VALID_ENERGY:
            return None, "action bad/multi/missing energy"
        energy_type = energy_raw
        levels = []
    else:
        levels = []

    if not cost_raw.isdigit():
        return None, "non-numeric cost"
    purchase_cost = int(cost_raw)

    is_implemented, keywords = classify_ability(ability)

    entry = {
        "id": card_id,
        "name": name,
        "subtitle": subtitle or None,
        "type": card_type,
        "purchaseCost": purchase_cost,
        "energyType": energy_type,
        "dieLimit": die_limit,
        "levels": levels,
        "rawText": ability,
        "keywords": keywords,
        "affiliations": parse_affiliations(row[6] if len(row) > 6 else ""),
        "isImplemented": is_implemented,
        "set": code,
    }
    return entry, None


def main():
    existing_ids = existing_hand_curated_ids()
    print(f"Excluding {len(existing_ids)} already hand-curated ids from bulk import.")

    entries = []
    seen_ids = set()
    reasons = Counter()
    total_rows = 0
    type_counts = Counter()

    for code in SET_CODES:
        print(f"Fetching {code}...")
        csv_text = fetch_set_csv(code)
        reader = csv.reader(io.StringIO(csv_text))
        next(reader, None)  # header
        for row in reader:
            if not row or not row[0].strip():
                continue
            total_rows += 1
            card_id = row[0].strip()
            if card_id in existing_ids:
                reasons["already hand-curated (skipped)"] += 1
                continue
            if card_id in seen_ids:
                reasons["duplicate id within sheet"] += 1
                continue
            entry, skip_reason = classify_row(code, row)
            if entry is None:
                reasons[skip_reason] += 1
                continue
            seen_ids.add(card_id)
            entries.append(entry)
            type_counts[entry["type"]] += 1

    entries.sort(key=lambda e: e["id"])
    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(json.dumps(entries, indent=2) + "\n", encoding="utf-8")

    implemented_count = sum(1 for e in entries if e["isImplemented"])
    print(f"\nWrote {len(entries)} cards to {OUTPUT_PATH.relative_to(REPO_ROOT)}")
    print(f"Total sheet rows scanned: {total_rows}")
    print(f"By type: {dict(type_counts)}")
    print(f"Auto-IsImplemented=true (blank or pure-keyword-only text): {implemented_count}")
    print("\nSkip reasons:")
    for reason, count in reasons.most_common():
        print(f"  {count:5d}  {reason}")


if __name__ == "__main__":
    main()
