#!/usr/bin/env python3
"""Rank Dice Masters characters by how easily they'd fit Dice Kingdom's engine.

One line per character not yet used by a Dice Kingdom card: its easiest
printing, a score (lower = easier) and why. A screen for picking import
batches by hand, not a decision - the text still has to be read.

    python3 scripts/dk_import_candidates.py [--top N] [--cards dkcards.json]

--cards is the live catalog (GET /api/v2/games/cards); without it, used
characters come from the source ids in the C# card files.
"""
import argparse, collections, json, re, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
BULK = json.loads((ROOT / "src/DiceFight.Engine/Data/BulkCards.json").read_text())

# Keywords the V2 engine runs on its own (KeywordAbilities.cs, CombatEngine).
ENGINE_KEYWORDS = {
    "Overcrush", "Fast", "Deadly", "Intimidate", "Infiltrate", "Attune", "Obscure", "Energize",
    "Energy Drain", "Tag Out", "Sacrifice", "Range", "Aftershock", "Breath Weapon", "Ally",
    "Boomerang", "Impulse", "Awaken", "Amplify", "Elder", "Teamwatch",
}
# Dice Masters keywords V2 doesn't run yet (CombatEngine.cs header) - each is engine work.
UNBUILT_KEYWORDS = ["Regenerate", "Swarm", "Call Out", "Corrupt", "Retaliation"]
# Mechanics Dice Kingdom has no equivalent for - a card leaning on one is a rework, not an import.
BLOCKERS = [
    r"\bdraft\b", r"\bloyalty\b", r"\bcrossover\b", r"\bfusion\b", r"\btokens?\b", r"\bequip",
    r"\btrap\b", r"\bgate\b", r"\britual\b", r"\bfabricate\b", r"\bflip\b", r"\bcounters?\b",
    r"\baffiliation", r"\bteam ?up\b", r"\bsuit up\b", r"\bgadgeteer\b", r"\brecruit\b",
    r"\bspark\b", r"\bteamwork\b", r"\bsynergy\b", r"\bunderdog\b", r"\bvengeance\b",
    r"\bimmortal\b", r"\bcleave\b", r"\brush\b", r"\biron will\b", r"\bresistance\b",
    r"\bheroic\b", r"\bfrag\b", r"\bturtle power\b", r"\bback for more\b", r"\bcommon ground\b",
    r"\bcrosspulse\b", r"\benlistment\b", r"\bexperience\b", r"\bstrike\b", r"\bdestiny\b",
    r"\bvillains?\b", r"\bheroes?\b", r"\bhero\b", r"\bpersonality\b", r"\bdeck\b", r"\bcards? in\b",
    r"\bteam\b", r"\bcustom", r"\bplayers?\b(?! .*opponent)", r"\bmultiplayer\b",
]
EASY_PATTERNS = [
    r"^when fielded, deal \d damage to (target|each|your opponent|target opponent)",
    r"^when fielded, ko target", r"^when (this character|.+) attacks, deal \d damage",
    r"gets? \+\d[ad]", r"can't be blocked", r"^when fielded, gain \d life",
    r"^when fielded, draw", r"^when fielded, (target|each) .* can't block",
]

def strip_reminders(text):
    return re.sub(r"\s*\([^)]*\)", "", text or "").strip()

def used_characters(cards_path):
    by_id = {c["id"]: c for c in BULK}
    used = set()
    if cards_path:
        ids = [c["id"] for c in json.loads(pathlib.Path(cards_path).read_text())]
    else:
        src = "".join(p.read_text() for p in (ROOT / "src/DiceFight.V2/Data").glob("*.cs"))
        ids = re.findall(r'Id: "([A-Za-z0-9]+)"', src)
    for i in ids:
        if i in by_id: used.add(by_id[i]["name"])
    # DpsCards ids aren't all in BulkCards under the same key - read the names straight from the file.
    dps = (ROOT / "src/DiceFight.V2/Data/DpsCards.cs").read_text()
    for m in re.finditer(r'Id: "(\w+)", Name: "([^"]+)"[^;]*?IncludeInTeamBuilder: true', dps, re.S):
        used.add(m.group(2))
    for p in ("BonusCards.cs", "CardCatalog.cs"):
        t = (ROOT / "src/DiceFight.V2/Data" / p).read_text()
        for m in re.finditer(r'Name: "([^"]+)"[^;]*?KingdomName:', t, re.S):
            used.add(m.group(1))
    # Native animals were reskinned from these (CARD_INSPIRATION.md "Source" column).
    insp = (ROOT / "v3/CARD_INSPIRATION.md").read_text()
    for row in re.findall(r"^\|[^|\n]+\|[^|\n]*\|[^|\n]*\|[^|\n]*\|[^|\n]*\|\s*([^|\n]+?)\s*\|$", insp, re.M):
        name = re.split(r",| \(", row)[0].strip()
        if name and name not in ("Source", "---"): used.add(name)
    return used

def score(card, other_names):
    text = strip_reminders(card["rawText"])
    low = text.lower()
    why = []
    if not text:
        return 0, ["no text"]
    words = [w.strip(" .") for w in re.split(r"[.,]", text) if w.strip(" .")]
    if all(w in ENGINE_KEYWORDS or re.fullmatch(r"(Range|Attune) \d", w) for w in words):
        return 1, ["keywords only"]
    s = 10 + len(text) // 40
    for k in UNBUILT_KEYWORDS:
        if re.search(r"\b" + k.lower() + r"\b", low):
            s += 15; why.append(k + " not built")
    for pat in BLOCKERS:
        if re.search(pat, low):
            s += 25; why.append("mentions " + pat.strip("\\b()?").split("\\")[0])
    names = [n for n in other_names if n != card["name"] and len(n) > 3 and re.search(r"\b" + re.escape(n.lower()) + r"\b", low)]
    if names:
        s += 25; why.append("names " + names[0])
    if any(re.search(p, low) for p in EASY_PATTERNS):
        s -= 5; why.append("common pattern")
    if low.count(".") >= 3:
        s += 5; why.append("several clauses")
    if "global" in low:
        s += 2
    return s, why

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--top", type=int, default=200)
    ap.add_argument("--cards")
    ap.add_argument("--json")
    args = ap.parse_args()
    used = used_characters(args.cards)
    chars = [c for c in BULK if c["type"] == "Character" and not c.get("statsMissing")]
    names = {c["name"] for c in chars}
    best = {}
    for c in chars:
        if c["name"] in used: continue
        s, why = score(c, names)
        if c["name"] not in best or s < best[c["name"]][0]:
            best[c["name"]] = (s, why, c)
    ranked = sorted(best.values(), key=lambda t: (t[0], t[2]["name"]))
    print(f"{len(names)} characters, {len(used & names)} used, {len(best)} left\n")
    for s, why, c in ranked[: args.top]:
        lv = " ".join(f"{l['fieldingCost']}:{l['attack']}/{l['defense']}" for l in c["levels"])
        print(f"{s:3} {c['id']:9} {c['name']} ({c['subtitle']}) cost {c['purchaseCost']} [{lv}] - {strip_reminders(c['rawText'])[:150]}" + (f"  <{'; '.join(why)}>" if why else ""))
    if args.json:
        pathlib.Path(args.json).write_text(json.dumps([{"score": s, "why": why, **c} for s, why, c in ranked], indent=1))

main()
