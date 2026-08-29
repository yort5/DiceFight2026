#!/usr/bin/env python3
"""Extracts per-card data from the old Teambuilder that our own sources
do not have: max dice and split-energy symbols (-> scripts/maxdice.json,
read by import_bulk_cards.py), each card's old-Teambuilder code
(-> src/DiceFight.Engine/Data/OldTeamBuilderCodes.json) and its
affiliation icons (-> src/DiceFight.Engine/Data/AffiliationIcons.json),
both embedded and served on the API for the web client.

WHY THIS EXISTS: the reference Google Sheet has no die-limit column, and
die limit is NOT derivable from rarity - the previous importer inferred
it from the rarity/"Stripe" column, which the user confirmed is simply
wrong (rarity has no direct bearing on max dice). The old Teambuilder
(~/DiceMasters/Teambuilder/cards.php) does carry a real per-card value,
so it is the best source available until the sheet grows a column of its
own.

The old tool packs each card into one string: a fixed-width header
followed by "Name|Subtitle|Ability|...". Per index.php:
    set[i][0] rarity, [1] cost (hex), [2] energy, [3] affiliation,
    [4] MAX DICE, and  p_src = set[i].substring(is_dnd ? 7 : 5)
so the header is 5 characters wide, except in the D&D sets where it is
7, and max dice is always character 4.

SPLIT ENERGY: the old tool writes a card's printed symbols as bracketed
tokens ([B], [PAWN], [2]), and among them are the six two-colour "either
energy" symbols - [BM] is one Bolt OR one Mask, which is a different cost
from [B][M], one Bolt AND one Mask. The reference sheet flattens both to
the same words ("pay Bolt Mask"), so the distinction is unrecoverable
from the sheet alone. Four cards in the whole game use one, and this is
the only source that knows which; each is emitted as the pair of energy
names to rewrite as "Bolt/Mask" in the sheet text.

AFFILIATION ICONS: the sheet gives affiliations as free text, and its
123 distinct spellings do not map cleanly onto logos - "Villains" alone
is drawn with two different logos depending on the universe, and a card
with two affiliations is often drawn with ONE combined logo (Doctor
Octopus is a single Sinister-Six-and-Villain mark, not two). The old tool
stores the icon per card instead, as character [3] of the header passed
through the set's own affiliation map, so that is what we take.

Output is keyed by normalized name+subtitle rather than by set code,
because the promo arrays (avxop, m_op2019, wd_op2018, ...) do not
correspond to sheet set codes at all. Entries whose printings disagree
about max dice are recorded per-set so the importer can disambiguate.

Usage: python3 scripts/extract_maxdice.py
No network and no dependencies; reads a local checkout of the old tool.
"""
import json
import re
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TEAMBUILDER = Path.home() / "DiceMasters" / "Teambuilder"
CARDS_PHP = TEAMBUILDER / "cards.php"
INDEX_PHP = TEAMBUILDER / "index.php"
OUTPUT_PATH = REPO_ROOT / "scripts" / "maxdice.json"
OLD_CODES_PATH = REPO_ROOT / "src" / "DiceFight.Engine" / "Data" / "OldTeamBuilderCodes.json"
AFFILIATION_PATH = REPO_ROOT / "src" / "DiceFight.Engine" / "Data" / "AffiliationIcons.json"

# set_names order in cards.php, used only to resolve which arrays use the
# 7-character D&D header (dndsets = [2,8,23,38,39,40] in index.php).
SET_NAMES = [
    "avx", "uxm", "bff", "ygo", "jl", "aou", "wol", "asm", "fus", "wf", "tmnt", "cw",
    "gaf", "drs", "dp", "hhs", "imw", "bat", "def", "sww", "smc", "gotg", "xfc", "toa",
    "thor", "ai", "ki", "jll", "hq", "bfu", "ork", "sw", "jus", "doom", "myst", "xmf",
    "xfo", "dxm", "tiw", "aiw", "zhn", "wwe", "bit", "tag", "ig", "dps", "skc", "msw",
]
DND_SETS = {SET_NAMES[i] for i in (2, 8, 23, 38, 39, 40)}
# The promo arrays that belong to those same D&D lines - named after the
# product rather than the set, so they cannot be looked up in SET_NAMES.
DND_PROMO_ARRAYS = {"bff_op", "bff_promo", "wd_op2018"}

# Arrays in cards.php that are not card lists (colour tables, affiliation
# maps, per-set die-face data).
NOT_CARD_ARRAYS = re.compile(r"(_aff|_dice)$|^(raritycolor|affiliation_properites)$")

ENTRY_RE = re.compile(r"'((?:[^'\\]|\\.)*)'")

# The old tool's icon tokens for the six split "either energy" symbols,
# in its own letter order (B)olt (F)ist (M)ask (S)hield.
SPLIT_ENERGY = {
    "BF": ("Bolt", "Fist"), "BM": ("Bolt", "Mask"), "BS": ("Bolt", "Shield"),
    "FM": ("Fist", "Mask"), "FS": ("Fist", "Shield"), "MS": ("Mask", "Shield"),
}
SPLIT_ENERGY_RE = re.compile(r"\[(" + "|".join(SPLIT_ENERGY) + r")\]")


def normalize(s):
    return re.sub(r"[^a-z0-9]", "", (s or "").lower())


# The two sources spell the Basic Action subtitle three ways between them
# ("Basic Action", "Basic Action Card", "Basic Action Cards"), which would
# otherwise cost 62 cards their code. Collapsed to one form on both sides
# - this must stay identical to BulkCardCatalog.NormalizeCardKey in C#.
def normalize_subtitle(s):
    v = normalize(s)
    if v.startswith("epicbasicaction"):
        return "epicbasicaction"
    if v.startswith("basicaction"):
        return "basicaction"
    return v


def header_width(array_name):
    return 7 if (array_name in DND_SETS or array_name in DND_PROMO_ARRAYS) else 5


def affiliation_maps():
    """arrayVar -> {header char: icon code}, from index.php's init() calls.

    Most sets pass a map as init()'s 6th argument; the earliest ones pass
    none and use the header character as the icon code directly."""
    text = INDEX_PHP.read_text(encoding="utf-8", errors="replace")
    inits = re.findall(
        r"init\((\d+),([a-z0-9_]+),'([^']+)','([a-z0-9_]+)'(?:,([^,)]+))?(?:,([a-z0-9_]+))?\)", text)
    cards = CARDS_PHP.read_text(encoding="utf-8", errors="replace")
    named = {}
    for m in re.finditer(r"var ([a-z0-9_]+_aff) = \{(.*?)\};", cards, re.S):
        named[m.group(1)] = dict(re.findall(r"(\w+)\s*:\s*['\"]([^'\"]*)['\"]", m.group(2)))
    return {arr: (named.get(aff) if aff else None) for _, arr, _, _, _, aff in inits}


def affiliation_icons(entry, aff_map):
    """The icon codes on one card entry, or [] for "no affiliation"."""
    value = aff_map[entry[3]] if aff_map is not None else entry[3]
    if value is None:
        return []
    # "X/Y" is a card drawn with two separate logos; "0" is none.
    return [code for code in value.split("/") if code and code != "0"]


def array_to_set_name():
    """arrayVar -> the set name the old tool uses in team codes.

    From its index.php's init(setid, arrayVar, setName, trsName) calls.
    Note these are NOT our set codes: the old tool splits promos into
    their own sets (avxop, dc2016, wko16dc, m2019, ...), which is exactly
    why the codes have to be read rather than derived."""
    text = INDEX_PHP.read_text(encoding="utf-8", errors="replace")
    return {m[1]: m[2].lower() for m in re.findall(r"init\((\d+),([a-z0-9_]+),'([^']+)'", text)}


def main():
    if not CARDS_PHP.exists():
        raise SystemExit(f"Old Teambuilder not found at {CARDS_PHP}")
    text = CARDS_PHP.read_text(encoding="utf-8", errors="replace")
    blocks = re.findall(r"^\s*var ([a-z0-9_]+) = \[(.*?)^\s*\];", text, re.M | re.S)

    set_names = array_to_set_name()
    aff_maps = affiliation_maps()

    # (name, subtitle) -> {maxdice: [array names]}
    seen = defaultdict(lambda: defaultdict(list))
    old_codes = {}
    old_codes_by_set = {}
    split_energy = {}
    affiliations = {}
    affiliations_by_set = {}
    total = 0
    for array_name, body in blocks:
        if NOT_CARD_ARRAYS.search(array_name):
            continue
        width = header_width(array_name)
        for position, raw in enumerate(ENTRY_RE.findall(body)):
            entry = raw.replace("\\'", "'").replace('\\"', '"').replace("\\\\", "\\")
            if len(entry) <= width or not entry[4].isdigit():
                continue
            parts = entry[width:].split("|")
            if len(parts) < 2:
                continue
            key = f"{normalize(parts[0])}|{normalize_subtitle(parts[1])}"
            seen[key][int(entry[4])].append(array_name)
            pairs = [SPLIT_ENERGY[m] for m in SPLIT_ENERGY_RE.findall(entry)]
            if pairs:
                split_energy[key] = pairs
            if array_name in aff_maps:
                icons = affiliation_icons(entry, aff_maps[array_name])
                affiliations.setdefault(key, icons)
                if array_name in set_names:
                    affiliations_by_set.setdefault(f"{set_names[array_name]}|{key}", icons)
            # The team-link code is "<position in this set><set name>",
            # per the old tool's num2cardname(): a%1000 + setnames[a/1000].
            if array_name in set_names:
                code = f"{position + 1}{set_names[array_name]}"
                # Two maps. The set-qualified one is preferred at lookup
                # time so a card resolves to ITS OWN printing: several
                # Basic Actions are reprinted verbatim across sets, and
                # picking an arbitrary one would show the wrong set's art.
                # The bare key is the fallback, and is what makes a promo
                # reprint resolve to the original set that has it.
                old_codes_by_set[f"{set_names[array_name]}|{key}"] = code
                old_codes[key] = code
            total += 1

    lookup, conflicts = {}, {}
    for key, by_value in seen.items():
        if len(by_value) == 1:
            lookup[key] = next(iter(by_value))
        else:
            # Same name+subtitle printed with different limits - record the
            # options rather than silently picking one.
            conflicts[key] = {str(v): sorted(set(a)) for v, a in by_value.items()}

    OUTPUT_PATH.write_text(
        json.dumps({"maxDice": lookup, "conflicts": conflicts,
                    "splitEnergy": split_energy}, indent=1, sort_keys=True) + "\n",
        encoding="utf-8")
    AFFILIATION_PATH.write_text(
        json.dumps({"bySetAndCard": dict(sorted(affiliations_by_set.items())),
                    "byCard": dict(sorted(affiliations.items()))}, indent=1) + "\n", encoding="utf-8")
    OLD_CODES_PATH.write_text(
        json.dumps({"bySetAndCard": dict(sorted(old_codes_by_set.items())),
                    "byCard": dict(sorted(old_codes.items()))}, indent=1) + "\n", encoding="utf-8")

    print(f"Scanned {len(blocks)} arrays, {total} card entries.")
    print(f"Wrote {len(old_codes)} old-Teambuilder codes "
          f"({len(old_codes_by_set)} set-qualified) to "
          f"{OLD_CODES_PATH.relative_to(REPO_ROOT)}")
    print(f"Wrote {len(lookup)} unambiguous name+subtitle -> max dice entries "
          f"to {OUTPUT_PATH.relative_to(REPO_ROOT)}")
    with_icon = sum(1 for v in affiliations.values() if v)
    print(f"Wrote affiliation icons for {len(affiliations)} cards "
          f"({with_icon} with at least one) to {AFFILIATION_PATH.relative_to(REPO_ROOT)}")
    used = sorted({c for v in affiliations.values() for c in v})
    print(f"    {len(used)} distinct icons: {' '.join(used)}")
    print(f"Cards printing a split \"either energy\" symbol: {len(split_energy)}")
    for key, pairs in sorted(split_energy.items()):
        print(f"    {key}: {['/'.join(p) for p in pairs]}")
    print(f"Ambiguous (same name+subtitle, differing limits): {len(conflicts)}")
    for key, opts in list(conflicts.items())[:10]:
        print(f"    {key}: {opts}")


if __name__ == "__main__":
    main()
