# V3 — Card Inspiration Pass

Status: exploratory, generated 2026-09-02 while the design was on pause for
family feedback on the Shell icon. Not decisions — a sourcing pass to make
the next "let's build some actual cards" session faster to start.

## Method

Went through all 145 cards in the migrated DPS catalog
(`../src/DiceFight.V2/Data/DpsCards.cs`), which is the one pool where
"buildable against a v2-style engine" isn't a guess — every card there is
either already fully implemented against the closed template vocabulary, or
explicitly marked `IsImplemented: false` with a comment explaining exactly
which clause doesn't fit and why (see `../V2_TAIL_POLICY.md`). That marker
was the filter: picks below are fully implemented unless flagged
**(partial)**, meaning part of the printed card is a clean fit and part
isn't — included anyway because the buildable part is a good mechanic on
its own.

Within the buildable pool, picks lean toward single, simple triggered
abilities using the ~16 high-frequency templates (DealDamage, Ko, ModifyStat,
StatAura, MoveDie, DrawToZone, Spin/SpinToEnergy, Reroll, CombatFlag,
Conditional) rather than the more elaborate multi-clause cards, since those
are the ones most likely to survive a "simplify for a new game" pass intact.

Original stats/cost are carried over as a starting point, not a balanced
number for this game — energy types on the source cards are the *old*
Fist/Mask/Bolt/Shield/generic-affiliation colors and are irrelevant here;
what mattered for sorting into Claw/Shell/Wing/Eye below was what the
ability actually *does*, not what color it used to be. Basic-Action-style
cards (no creature, a standalone effect) are called out separately per
section since they don't get an animal.

Stats are written `ATK/DEF` per level, e.g. `1/4 → 1/5 → 1/6`.

---

## Claw (Aggro / Damage) — 14 picks

| Animal | Symbol | Cost | Stats (L1→L2→L3) | Ability | Source |
|---|---|---|---|---|---|
| — (Basic Action: "Ambush") | crossed claw slashes | 3 | — | Deal 2 damage to a target creature or player. | Power Bolt (DPS011) |
| Honey Badger | crossed claw scratches | 2 | 0/2 → 0/3 → 1/3 | On field: deal 1 damage to a target creature. | Storm, "Extreme Weather" (DPS052) |
| Grizzly Bear | bear-paw print | 5 | 1/5 → 2/6 → 3/8 | On field: KO a target creature. | Master Mold, "Targeting Mutants" (DPS082) |
| Rhino | cracked-ground burst | 5 | 1/5 → 2/6 → 3/8 | On field: deal 2 damage to every creature matching a chosen group. | Master Mold, "Inexplicable Durability" (DPS042) |
| Wolverine (the animal) | slashed X claw-mark | 4 | 0/2 → 0/2 → 1/3 | On attack: deal 1 damage to the opponent directly. | Deadpool, "More Than a Chump Blocker" (DPS068) |
| Stoat | three tiny slash marks | 4 | 0/2 → 1/3 → 2/4 | On field: deal 1 damage to the opponent and 1 to a target creature. | Jubilee, "X-Men Field Leader" (DPS143) |
| Bull Elephant | broken tusk | 5 | 1/5 → 1/6 → 2/8 | On field: lose 1 life. On KO: opponent loses 1 life. | Ronan, "Treason!" (DPS050) |
| Silverback Gorilla | clenched paw-fist | 6 | 1/5 → 1/6 → 2/8 | On field: both players lose 3 life. | Ronan, "No Exceptions" (DPS130) |
| Wolf | howling silhouette | 5 | 1/4 → 1/5 → 1/6 | Whenever you field another creature (of a chosen tag), deal 2 damage to a target. | Cyclops, "First Class" (DPS025) |
| Cape Buffalo | curved horn pair | 6 | 1/4 → 1/6 → 2/8 | At end of turn, each of your high-level creatures deals 2 damage to the opponent. | Colossus, "Piotr" (DPS103) |
| Tiger | tiger-stripe slash | 6 | 1/5 → 2/7 → 3/8 | On field: KO a target. On attack: deal 2 to the opponent. Global: KO one of your own creatures to discount your next purchase. | Dark Phoenix, "Enemy of the Shi'ar" (DPS067) |
| Orca | notched dorsal fin | 5 | 0/3 → 1/3 → 1/4 | On field: KO a target creature (or 2, if the opponent's board is crowded). | Corsair, "Criminal Record" (DPS104) |
| Saber-Toothed Cat | crossed curved claws | 5 | 1/3 → 1/4 → 2/5 | On attack: your matching-tag creatures get +2 ATK. | Sabretooth, "You Ready to Party?" (DPS131) |
| Peregrine Falcon | talon strike mark | 6 | 1/5 → 2/7 → 3/8 | On field: deal 3 damage to a target. Bonus effect on a double-energy roll: deal 2 more. | Phoenix, "Firepower" (DPS046) |

Falcon is deliberately a bird carrying Claw energy (talons, not wings) —
worth keeping as a live example that Energy and Affiliation really are
independent axes, not "birds are Wing energy" by default.

---

## Shell (Defense / Life) — 8 picks

Thinner list than the other three, honestly — the DPS catalog has a lot of
damage/removal and a fair amount of tempo/control, but comparatively little
dedicated protect-or-heal design. Worth remembering when writing original
Shell cards later: this energy may need more from-scratch design than the
other three rather than leaning on adaptation.

| Animal | Symbol | Cost | Stats (L1→L2→L3) | Ability | Source |
|---|---|---|---|---|---|
| Pangolin | overlapping scale rings | 3 | 0/2 → 1/3 → 1/3 | While active, your basics can't be targeted by opposing team-wide abilities. | Angel, "Xavier's Dream" (DPS137) |
| — (Basic Action: "Dig In") | shield of overlapping plates | 3 | — | Your creatures get +2 DEF this turn (more on a burst roll). | Take Cover (DPS014) |
| Musk Ox | ring of curved horns | 4 | — | While active, your matching-tag creatures get +1 ATK/+1 DEF. | Kitty Pryde, "Experienced Leader" (DPS144) |
| Hermit Crab | spiral shell | 2 | 1/1 → 0/1 → 2/1 | While active, reduce ability damage to your creatures by 1. On KO: may recover a strong ally from the discard. | Mystique, "Freedom Force" (DPS085) |
| Hippopotamus | wide open-jaw shield shape | 4 | 0/1 → 1/1 → 2/1 | May block up to 3 attackers instead of 1. **(partial — a bonus clause on KO isn't buildable yet)** | Blob, "Immovable" (DPS101) |
| Opossum | curled tail loop | 3 | 0/0 → 0/1 → 1/2 | On field: your matching-tag creatures get +1 ATK this turn. On KO: recover a die from the discard. | Moira, "If It's Real" (DPS084) |
| Queen Termite | hexagonal mound | 4 | 1/3 → 1/4 → 2/5 | While active, your basics get +1 ATK/+1 DEF. **(partial — an affiliation-grant clause isn't buildable yet)** | Emma Frost, "Influential" (DPS030) |
| Snapping Turtle | cracked crystal facet | 5 | 0/4 → 1/5 → 2/6 | **(inspiration only, not yet buildable as printed)** — caps damage taken per turn at a fixed number. | D'Ken, "M'Kraan Crystal" (DPS106) |

---

## Wing (Tempo / Evasion) — 12 picks

| Animal | Symbol | Cost | Stats (L1→L2→L3) | Ability | Source |
|---|---|---|---|---|---|
| — (Basic Action: "Second Wind") | forward-swept chevron | 3 | — | Move up to 2 (or 3, on a burst) of your discarded basics back onto the field. | Rally (DPS013) |
| — (Basic Action: "Scouting Party") | fanned feather trio | 2 | — | Draw and roll 3 dice if you have 2+ of a chosen tag active, otherwise 1. | Mutant Research Program (DPS008) |
| Osprey | diving wing silhouette | 4 | 0/4 → 1/5 → 2/6 | On attack: move a die from your discard back to your hand-equivalent (Prep Area). | D'Ken, "Emperor" (DPS026) |
| Barn Swallow | forked tail-wing | 3 | 0/2 → 0/3 → 1/3 | Whenever this levels up: draw a die into your Prep Area. | Kitty Pryde, "Right of Passage" (DPS037) |
| Hummingbird | blurred wing-loop | 4 | 0/1 → 0/1 → 1/2 | Whenever this levels up: roll a fresh die straight from your bag. | Magik, "Better than Belasco" (DPS080) |
| Homing Pigeon | arcing flight path | 4 | 0/3 → 1/3 → 1/4 | On field: your next purchase this turn skips the discard and goes straight to your bag. | Corsair, "Recruiting a Crew" (DPS024) |
| Greyhound | streaking dash-lines | 4 | 1/5 → 2/6 → 3/8 | Global, once per turn: draw a die into your Prep Area. | Wolverine, "Trainer" (DPS136, simplified) |
| Albatross | wide wingspan arc | 5–6 | 1/4 → 2/5 → 3/6 | Global: if your Prep Area is empty, draw a die into it. | Magneto (several printings, simplified) |
| Mountain Goat | leaping hoofprint arc | 3 | 0/2 → 1/2 → 1/3 | On attack (if a matching ally also attacked): draw a die into your Prep Area. | Beast, "First Class" (DPS058) |
| Flying Squirrel | spread membrane wing | 3 | 1/2 → 2/3 → 2/4 | Bonus effect on a double-energy roll: may spin a Reserve die back to level 1. | Toad, "Looking for Comradery" (DPS094) |
| Jackrabbit | zigzag dash mark | 4 | 1/3 → 2/3 → 2/5 | Bonus effect on a double-energy roll: reroll one of your own creatures. | Cable, "I'll Do This All Day" (DPS022) |
| Monarch Butterfly | wing chevron pair | 4 | 0/1 → 0/2 → 1/3 | Gets +2 ATK per matching ally waiting in your Prep Area. Bonus effect on a double roll: spin a target up a level. | Psylocke, "Heiress" (DPS128) |

---

## Eye (Control / Trickery / Info) — 15 picks

| Animal | Symbol | Cost | Stats (L1→L2→L3) | Ability | Source |
|---|---|---|---|---|---|
| Barn Owl | wide staring eye-disc | 4 | 0/2 → 0/3 → 1/3 | On field: a weak target creature can't block this turn. | Storm, "Cloud Cover" (DPS092) |
| Anglerfish | glowing lure-dot eye | 6 | 1/5 → 2/7 → 3/8 | On attack: every weak opposing creature can't block this turn. | Phoenix, "Eternal Flame" (DPS126) |
| Cuckoo | hooked beak-eye mark | 4 | 0/3 → 1/4 → 1/6 | Global: a target creature must attack this turn. | Vulcan, "Ruler of the Imperium" (DPS055) |
| Praying Mantis | triangular head silhouette | 6 | 0/3 → 1/4 → 1/6 | While active, a chosen group of opposing creatures get -2 DEF. Global: force a target to attack. | Vulcan, "Aggression" (DPS135) |
| Raven | eye with a reflected glint | 7 | 0/2 → 0/3 → 1/3 | On field and on attack: reroll opposing creatures, punishing the ones that whiff. | Storm, "Queen" (DPS132) |
| Elephant | swirling third-eye spiral | 6 | 1/1 → 2/1 → 3/1 | On field: spin an opposing die down to its weakest energy face. Bonus on double roll: recover a matching ally from the discard. | Professor X, "Uncanny Leadership" (DPS127) |
| Chameleon | swiveling independent eye | 4 | 1/2 → 1/3 → 1/4 | On attack: spin a weak opposing creature down to an energy face. | Iceman, "Icy Interference" (DPS034) |
| Cuttlefish | hypnotic ring-eye | 3–5 | 1/1 → 0/1 → 2/1 | Whenever an ally levels up: spin a target opposing die to its best energy face (denying the body). | Mystique / Magneto (2 printings, simplified) |
| Fox | narrow slit eye | 5 | 1/3 → 1/4 → 2/5 | While active, at the start of the opponent's attack turn, reroll a target creature they control. | Emma Frost, "Manipulative" (DPS070) |
| Snow Leopard | spotted iris ring | 5 | 1/3 → 1/4 → 2/5 | While active, at the start of the opponent's attack turn, reroll 2 of their creatures — bad rolls get bumped back to reserve. | Emma Frost, "Finesse" (DPS110) |
| Spider | web-strand eye | 4 | 1/3 → 2/5 → 3/6 | While active (with a basic also active), the opponent pays 1 extra to use a team-wide ability. | Jean Grey, "Xavier's Dream" (DPS075) |
| Cowbird | sideways glancing eye | 3 | 1/2 → 2/3 → 2/4 | Whenever an ally levels up: move a die from the opponent's hand-equivalent back to their bag. | Toad, "Journey Into Misery" (DPS134) |
| Octopus | radiating-arm eye | 4 | 1/2 → 2/4 → 2/5 | On field: may swap this creature's ATK with a target opposing creature's ATK. | Rogue, "Mrs. X" (DPS049) |
| Magpie | glinting trinket-eye | 3 | 1/1 → 1/2 → 2/4 | On field: may draw and roll a die — or, on a burst, draw 2 and keep the better one. | Gambit, "Ace in the Hole" (DPS032) |
| Hyena | scanning slit-eye | 4 | 1/3 → 1/4 → 2/5 | Gets +1 ATK for every weak opposing creature on the board. | Sabretooth, "Do I Smell Weakness?" (DPS091) |

---

## Affiliation — naming and menu

### Alternate names for the axis itself

Same instinct as Sidekick → Tardigrade — worth naming deliberately rather
than defaulting to the Dice Masters term, since "Affiliation" carries none
of the animal-kingdom flavor the rest of the game is leaning into.

| Candidate | Take |
|---|---|
| **Kin** *(my pick)* | Warm, short, flexible enough to cover both a broad group ("Mammal Kin") and a narrow one ("Pollinator Kin") without straining either. |
| Clade | The actual cladistics term (a common ancestor plus all descendants) — thematically sharp for a nature-based game, but reads more clinical/niche than a mobile audience probably wants as a constant on-screen label. |
| Order | Nice double meaning — a taxonomic Order and a knightly-order connotation both work in UI copy ("Claw Order"). Slightly more formal-sounding than Kin. |
| Family | Warm and immediately legible to kids, but taxonomically narrower than what this axis needs to cover — undersells the broad groupings (Mammal, Bird) even if it's fine for the narrow ones. |
| Habitat | Not just a rename — reframes the whole axis around *where* something lives (Forest / Ocean / Sky / Underground / Desert) instead of *what* it is taxonomically. Worth considering as a genuinely different design, not a synonym swap: habitats are arguably more visually iconic for mobile art than order/family groupings are. |

### A starting menu of tags

**Broad (class-level)** — Mammals, Birds, Fish, Reptiles, Amphibians, Insects
& Bugs.

**Narrow / cross-cutting** (Forest Shuffle-style — each one can pull members
from multiple broad groups at once): Pawed, Hooved, Burrowers, Nocturnal,
Pack Hunters, Pollinators (bees, butterflies, hummingbirds — the user's own
example), Big Cats, Primates, Rodents, Venomous, Migratory, **Shelled**
(turtles, crabs, snails, armadillos).

**Non-animal** (per the original pitch): Flora / Trees, Fungi, Stone /
Minerals.

One naming collision worth flagging now rather than later: **Shelled** as an
Affiliation tag sits right next to **Shell** as an Energy type. They'd mean
genuinely different things (a taxonomic trait vs. a mechanical resource) the
same way "Claw" energy and a hypothetical "Pawed" affiliation would — but
Shelled/Shell specifically share a root word, which the Claw/Pawed pair
doesn't. Worth either renaming this one tag (e.g. "Armored") or deciding the
echo is fine because the axes are visually distinct enough in the UI not to
matter.
