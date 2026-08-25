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
PARAM_KEYWORDS = ["Range"]  # "Range X" - Corrupt X is NOT zero-AbilityDef
# (Corrupt always needs a real trigger + Corrupt effect to do anything -
# see match_corrupt below. It was wrongly listed here in round 1; harmless
# in practice since no real card's text is ever bare "Corrupt N" with no
# trigger phrase, but wrong in principle - a future sheet row shaped like
# that would have silently gotten IsImplemented: true with empty Abilities.)

VALID_ENERGY = {"Fist", "Bolt", "Mask", "Shield"}

BASIC_ACTION_SUBS = {"Basic Action Card", "Basic Action Cards", "Basic Action"}
EPIC_SUBS = {"Epic Basic Action"}
ID_RE = re.compile(r"^[A-Za-z]+\d+$")
# The PROMO tab is not one set but every promo ever printed, and it
# numbers ids digits-first in ~19 different shapes: "1AvXop", "5DC2016",
# "1WKO16DC", "143WF". Deliberately NOT folded into ID_RE - the main sets
# have one consistent shape worth keeping strict, and promos are the ones
# likely to sprout new shapes, so that complexity stays contained here
# (the user's call). Loose on shape, strict on the thing that matters:
# every id must still be unique, which main() already enforces.
PROMO_ID_RE = re.compile(r"^\d+[A-Za-z][A-Za-z0-9]*$")
BURST_ONLY_RE = re.compile(r"^[-*]+(\s[-*]+){2}$")
FACE_RE = re.compile(r"^(\*{0,2})(\d)(\d)(\d)(\*{0,2})$")
# Same shape, one extra digit: "3108" and "3810" are both fielding-cost 3
# plus three digits, but the first is 10A/8D and the second is 8A/10D.
# Nothing in the string says which, so the split cannot be inferred - it
# has to be stated per card. Only the YGO Egyptian God cards need this.
FACE_10_RE = re.compile(r"^(\*{0,2})(\d)(\d{3})(\*{0,2})$")
DOUBLE_DIGIT_STAT = {
    "Slifer the Sky Dragon": "attack",      # 175 286 3108 -> 3/10/8
    "The Winged Dragon of Ra": "defense",   # 157 268 3810 -> 3/8/10
    "White Lantern Dove": "defense",        # 022 033 1410 -> 1/4/10
}
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


# Some set tabs simply have not had their stat-line column filled in yet
# - as of 2026-08-25 that is every character on the MSW tab (136 rows) and
# a scattering elsewhere (Constantine, Ring of Magnetism). Those cards are
# real and belong in the catalog for team-building and for the Orange Ban
# list to match against, so they import with a deliberately absurd 0/0
# placeholder rather than being dropped: a card that cannot be fielded and
# hits for nothing is unmistakably unfinished data, where a guessed
# statline would quietly look legitimate. `statsMissing` flags them so the
# placeholder can be found and replaced once the sheet catches up.
PLACEHOLDER_FACES = [{"fieldingCost": 0, "attack": 0, "defense": 0, "burstStars": None}] * 3


def parse_faces(statline, name=None):
    # A few rows end their stat line with a stray sentence period
    # ("022 123 133." - all three Constantine printings). Trailing '*'
    # is meaningful (burst) and must survive; '.' never is.
    groups = statline.rstrip(".").split()
    if len(groups) != 3:
        return None
    faces = []
    for g in groups:
        m = FACE_RE.match(g)
        if m:
            lead, f, a, d, trail = m.groups()
        else:
            m10 = FACE_10_RE.match(g)
            hint = DOUBLE_DIGIT_STAT.get(name)
            if not m10 or hint is None:
                return None
            lead, f, mid, trail = m10.groups()
            a, d = (mid[:2], mid[2:]) if hint == "attack" else (mid[:1], mid[1:])
        stars = len(lead) + len(trail)
        faces.append({
            "fieldingCost": int(f), "attack": int(a), "defense": int(d),
            "burstStars": stars if stars else None,
        })
    return faces


def parse_energy(raw):
    """"Bolt" -> ["Bolt"]; "Bolt/Mask" -> ["Bolt", "Mask"]; None if any
    part is not a real energy type.

    Dual-energy characters (Crossovers and the like - 76 rows as of
    2026-08-25, e.g. every GAF Barry Allen printing) were previously
    dropped outright because this column was read as a single value.
    v1's CardDef.EnergyTypes was ALREADY a list, and every v1 consumer
    that matters treats it as one (Contains checks, and TurnEngine's
    purchase path takes the whole list), so emitting a list here is
    compatible rather than a v1 change in disguise."""
    parts = [p.strip() for p in (raw or "").split("/") if p.strip()]
    if not parts or any(p not in VALID_ENERGY for p in parts):
        return None
    return parts


# Action cards printed with no energy type of their own (the BFF
# equipment - Magic Helmet, Magic Sword, Limited Wish) show up as
# "Generic", "None" or blank. That is not missing data: it is the same
# "no energy type" Basic Actions already have (rule 1.2.4/1.3.10, and
# SampleCards.cs:182), so it maps to an empty list rather than a skip.
# Generic is an EnergyKind, never an EnergyType - the enum has only the
# four specific types, which is exactly why [] is the right shape.
GENERIC_ENERGY = {"", "None", "Generic"}


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


# Some sheet rows encode a genuinely blank text box as a placeholder
# phrase instead of an actually-empty cell (e.g. "(No Ability Text.)")
# - treated as equivalent to blank (same "vanilla card" convention
# HarleyQuinn/Colossus/etc. already use in SampleCards.cs), not left
# unimplemented. Requires the ENTIRE text (after stripping one layer of
# parens/trailing period) to be exactly one of these phrases - a card
# whose real text merely CONTAINS one of these words (e.g. a card that
# has "(No Ability Text.)" for its base ability but a real printed
# Global on top) must not match, so this is a whole-string check, not
# a substring one.
NO_ABILITY_PHRASES = {"no ability text", "none", "blank", "no ability", "n/a", "no text", "no abilities"}


def is_no_ability_placeholder(ability):
    inner = ability
    if inner.startswith("(") and inner.endswith(")"):
        inner = inner[1:-1].strip()
    return inner.rstrip(".").strip().lower() in NO_ABILITY_PHRASES


def classify_ability(ability):
    ability = norm(ability)
    if not ability:
        return True, []
    keywords = sorted(set(m.group(0).split()[0] if " " not in m.group(0) else m.group(0)
                           for m in LOOSE_KEYWORD_RE.finditer(ability)))
    # Normalize "Range 2" -> "Range" for the Keywords list
    keywords = sorted(set(re.sub(r" \d+$", "", k) for k in keywords))
    is_implemented = bool(STRICT_KEYWORD_RE.match(ability))
    return is_implemented, keywords


# ---- Ability templates: formulaic keywords that DO need an AbilityDef,
# unlike the zero-AbilityDef keywords above. Matched only after
# classify_ability's strict check fails - i.e. these are cards whose
# text isn't blank/pure-keyword, but does turn out to be exactly one of
# these known shapes. Reminder-text parentheticals are stripped before
# comparing (per the user: unofficial, inconsistently-authored
# explanation of the keyword's own already-standardized behavior, not
# part of what makes two cards "the same ability") - only a leading
# trigger-phrase clause (for Corrupt) is treated as functionally real,
# since it picks the TriggerType.
#
# Each matcher returns (abilityTemplate dict, extra keyword names) or
# None. See BulkCardCatalog.cs's BuildTemplatedAbility for the C# side
# that turns "effect" into the actual AbilityDef/EffectNode tree - this
# is the "JSON value that tells us which ability method to call"
# registry; adding a 5th template means one more matcher here plus one
# more case there, nothing else changes.

def strip_parens(text):
    stripped = re.sub(r"\([^()]*\)", "", text)
    return re.sub(r"\s+", " ", stripped).strip().rstrip(".").strip()


def match_call_out(ability, name):
    if strip_parens(ability).lower() == "call out":
        return {"effect": "CallOut", "trigger": "WhenAttacks", "params": {}}, ["Call Out"]
    return None


def match_intimidate(ability, name):
    if strip_parens(ability).lower() == "intimidate":
        return {"effect": "Intimidate", "trigger": "WhenFielded", "params": {}}, ["Intimidate"]
    return None


def match_retaliation(ability, name):
    if strip_parens(ability).lower() == "retaliation":
        return {"effect": "Retaliation", "trigger": "Retaliation", "params": {"amount": 1}}, ["Retaliation"]
    return None


CORRUPT_AMOUNT_RE = re.compile(r"^Corrupt\s+(\d+)$", re.IGNORECASE)


def match_corrupt(ability, name):
    # Corrupt's trigger varies per card and is stated as a real leading
    # clause (not reminder text) - e.g. "When Rogue is fielded, Corrupt
    # 2 (...)". Only WhenFielded/WhenKOd are wired to actually fire in
    # the engine today (see the plan/DESIGN_LOG for WhenBlocks/
    # WhenDamaged/"KOs an opponent" - real engine work, not attempted
    # here) - matched against the card's own real Name, not a generic
    # regex, since that's exactly what the sheet's text uses.
    stripped = strip_parens(ability).replace("’", "'").replace("‘", "'")
    for phrase, trigger in (
        (f"When {name} is fielded,", "WhenFielded"),
        (f"When {name} is KO'd,", "WhenKOd"),
    ):
        if stripped.startswith(phrase):
            rest = stripped[len(phrase):].strip()
            m = CORRUPT_AMOUNT_RE.match(rest)
            if m:
                return {"effect": "Corrupt", "trigger": trigger, "params": {"amount": int(m.group(1))}}, ["Corrupt"]
    return None


ABILITY_TEMPLATE_MATCHERS = [match_call_out, match_intimidate, match_retaliation, match_corrupt]


def match_ability_template(ability, name):
    for matcher in ABILITY_TEMPLATE_MATCHERS:
        result = matcher(ability, name)
        if result:
            return result
    return None


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
    if is_no_ability_placeholder(ability):
        ability = ""
    statline = norm(row[8])

    if not (PROMO_ID_RE if code == "PROMO" else ID_RE).match(card_id):
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
    energy_type = []
    stats_missing = False

    if card_type == "Character":
        energy_type = parse_energy(energy_raw)
        if energy_type is None:
            return None, "character bad/missing energy"
        if not statline:
            levels = PLACEHOLDER_FACES
            stats_missing = True
        else:
            levels = parse_faces(statline, name)
            if levels is None:
                return None, "character unparseable stat line"
        if rarity not in RARITY_TO_DIE_LIMIT:
            return None, "unknown rarity"
        die_limit = RARITY_TO_DIE_LIMIT[rarity]
    elif card_type == "Action":
        if energy_raw in GENERIC_ENERGY:
            energy_type = []
        else:
            energy_type = parse_energy(energy_raw)
            if energy_type is None:
                return None, "action bad/missing energy"
        levels = []
    else:
        levels = []

    if not cost_raw.isdigit():
        return None, "non-numeric cost"
    purchase_cost = int(cost_raw)

    is_implemented, keywords = classify_ability(ability)

    ability_template = None
    if not is_implemented:
        template_match = match_ability_template(ability, name)
        if template_match:
            ability_template, extra_keywords = template_match
            is_implemented = True
            keywords = sorted(set(keywords) | set(extra_keywords))

    if stats_missing:
        # A placeholder statline is missing data, not finished data - the
        # die cannot be fielded, so the card is not playable no matter how
        # simple or well-templated its ability text is. This deliberately
        # runs last, after template matching may have set the flag.
        is_implemented = False

    entry = {
        "id": card_id,
        "name": name,
        "subtitle": subtitle or None,
        "type": card_type,
        "purchaseCost": purchase_cost,
        "energyTypes": energy_type,
        "dieLimit": die_limit,
        "levels": levels,
        "statsMissing": stats_missing,
        "rawText": ability,
        "keywords": keywords,
        "affiliations": parse_affiliations(row[6] if len(row) > 6 else ""),
        "isImplemented": is_implemented,
        "set": code,
        "abilityTemplate": ability_template,
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
    templated = [e for e in entries if e["abilityTemplate"]]
    print(f"\nWrote {len(entries)} cards to {OUTPUT_PATH.relative_to(REPO_ROOT)}")
    print(f"Total sheet rows scanned: {total_rows}")
    print(f"By type: {dict(type_counts)}")
    print(f"Auto-IsImplemented=true (blank/pure-keyword/templated): {implemented_count}")
    print(f"  of which via an ability template ({len(templated)}): " +
          ", ".join(f"{e['id']}={e['abilityTemplate']['effect']}" for e in templated))
    print("\nSkip reasons:")
    for reason, count in reasons.most_common():
        print(f"  {count:5d}  {reason}")


if __name__ == "__main__":
    main()
