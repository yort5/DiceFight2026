# Dice Kingdom cost model (2026-09-21, recalibrated)

Why: the original roster was ported from Dice Masters cards whose printed costs already priced in their abilities. Once abilities were simplified, costs and stats no longer matched (Elephant cost 6 for tiny stats; Greyhound cost 4 for huge ones).

## Homash value

From https://dmunited.eu/what-in-the-world-are-homash-values/ - "add up the attack and defence stats for all 3 levels and divide that number by 3 to get the average. Then, you divide that average by the purchase cost + the average fielding cost":

    Homash = sum(ATK + DEF over 3 levels) / (3 x purchase cost + sum of fielding cost over 3 levels)

(An earlier version of this model counted purchase cost once instead of three times, which put every number on the wrong scale.) The article calls ~2 reasonable and >2 strong, and notes abilities aren't in the formula - so here **the ability is paid for out of the stat budget**. Our dice also carry 3 energy faces, but every Character has the same 3, so it cancels out of comparisons.

**Champion passives are never part of the calculation.** A card can end up on any Champion's team, so Homash uses printed costs only (no Golden Eagle discount, no Wolf +1 ATK).

## Calibration against the real cards

The 128 Dice Masters characters in the DPS catalog, under the same formula: mean Homash **1.49**, median 1.49, deciles 1.0 / 1.14 / 1.24 / 1.41 / 1.5 / 1.56 / 1.67 / 1.78 / 1.9 (max about 3.0). Fielding costs there are **per level** and average about 1.0 (0 in 21% of level slots, 1 in 48%, 2 in 24%, 3 in 6%); common patterns are 1/2/3, 0/0/1, 1/1/2, 1/2/2, 0/1/1. By purchase cost, average total fielding runs 1.9 (cost 2), 2.9 (3), 3.2 (4), 4.4 (5-6). Dice Kingdom cards use the same shapes - cheap early levels, expensive last level - rather than one flat cost (an earlier pass gave every card a flat 1 or 2, which made them ~50% costlier to field than their sources).

## Bands (enforced by `DiceKingdomCostModelTests`)

| Card type | Target Homash |
|---|---|
| Vanilla (no ability, no keyword) | >= 1.75 (roster: 1.83-2.0) - "really good stats" or cheap |
| Keyword-only (Fast/Overcrush) | ~1.65-1.7 |
| Weak ability (1 life/1 damage/draw) | ~1.45-1.6 |
| Medium (2 damage, auras, direct damage) | ~1.25-1.45 |
| Strong (KO, 3 damage, Fast + damage, Deadly) | ~1.05-1.25 |
| Anything | 1.0-2.1 |

## Keywords

**Trigger keywords** (codified, not free text - the trigger is the keyword; the effect after the colon varies): `On Field` (die is fielded), `On Attack` (declared as an attacker), `On Block` (declared as a blocker), `Awaken` (die levels up). Every card's RawText leads its trigger clause with the keyword, and `Trigger_Keywords_Match_Each_Cards_Ability_Triggers` fails if a card's keywords and abilities disagree.

**Deadly**: a die engaged with a Deadly die (blocking it or blocked by it) is KO'd at Clean Up, even if the Deadly die dealt no damage or left combat. Recorded at declare-blockers, resolved in `TurnEngine.CleanUp`. On Opossum.

**Fast** and **Overcrush** (both in `CombatEngine`): Wolverine (Fast + 1 direct), Peregrine Falcon (Fast + 3 dmg), Greyhound (Fast), Grizzly Bear (Overcrush), Tiger (Overcrush + 2 direct). Vanilla: Elephant, Hippopotamus, Hermit Crab. Also On Block: Box Turtle. Not yet engine-supported: Regenerate, Retaliation, Swarm, Range/Infiltrate (UI already checks these names), Global abilities.

## Current roster

| Card | Buy | Levels (field/ATK/DEF) | Homash | Ability |
|---|---|---|---|---|
| Hermit Crab | 2 | 0/0/4 · 0/1/4 · 1/1/4 | 2.00 | Vanilla - no ability. |
| Hippopotamus | 4 | 1/0/8 · 1/1/9 · 2/2/10 | 1.88 | Vanilla - no ability. |
| Elephant | 6 | 1/3/9 · 2/4/11 · 3/5/12 | 1.83 | Vanilla - no ability. |
| Greyhound | 4 | 1/1/6 · 1/3/7 · 2/3/7 | 1.69 | Fast. |
| Grizzly Bear | 5 | 1/3/6 · 2/4/7 · 2/5/8 | 1.65 | Overcrush. |
| Swift | 2 | 0/1/2 · 1/2/2 · 1/3/3 | 1.62 | On Attack: draw a die into your Prep Area. |
| Homing Pigeon | 4 | 1/0/6 · 1/1/8 · 2/1/10 | 1.62 | On Field: gain 2 life. |
| Cowbird | 3 | 1/2/3 · 1/2/5 · 2/4/5 | 1.62 | Awaken: move an opposing die from their Prep Area back to their Bag. |
| Pangolin | 3 | 1/0/4 · 1/1/5 · 1/2/7 | 1.58 | On Field: gain 1 life. |
| Box Turtle | 3 | 1/0/3 · 1/1/5 · 1/3/7 | 1.58 | On Block: deal 1 damage to a target creature. |
| Honey Badger | 2 | 0/0/2 · 0/1/3 · 1/1/4 | 1.57 | On Field: deal 1 damage to a target creature. |
| Barn Owl | 4 | 1/0/7 · 1/1/7 · 2/1/9 | 1.56 | On Field: a weak target creature (3 ATK or less) can't block this turn. |
| Stoat | 4 | 0/0/4 · 1/2/6 · 2/4/7 | 1.53 | On Field: deal 1 damage to the opponent directly. |
| Cuttlefish | 2 | 0/0/3 · 1/1/3 · 1/1/4 | 1.50 | On Attack: spin a target opposing level 1 creature to an energy face. |
| Barn Swallow | 3 | 0/0/4 · 1/0/5 · 2/2/7 | 1.50 | Awaken: draw a die into your Prep Area. |
| Fox | 5 | 1/1/6 · 2/2/7 · 2/3/10 | 1.45 | While active, your creatures get +1 DEF. |
| Queen Termite | 4 | 1/2/4 · 1/2/6 · 2/3/6 | 1.44 | While active, your creatures get +1 ATK. |
| Musk Ox | 4 | 1/0/6 · 1/1/7 · 2/1/8 | 1.44 | While active, your creatures get +1 DEF. |
| Mountain Goat | 3 | 1/0/3 · 1/1/4 · 1/2/7 | 1.42 | On Attack: draw a die into your Prep Area. |
| Mongoose | 3 | 0/2/2 · 1/3/2 · 2/4/4 | 1.42 | Awaken: deal 2 damage to a target creature. |
| Magpie | 3 | 1/1/2 · 1/1/4 · 1/2/7 | 1.42 | On Field: draw a die into your Prep Area. |
| Osprey | 4 | 1/0/5 · 1/1/6 · 2/2/8 | 1.38 | On Attack: move a die from your discard to your Prep Area. |
| Hyena | 4 | 1/1/5 · 1/1/6 · 2/3/6 | 1.38 | Gets +1 ATK for each weak opposing creature (2 DEF or less). |
| Albatross | 5 | 1/1/5 · 1/3/6 · 2/4/7 | 1.37 | On Field: deal 2 damage to a target creature. |
| Raven | 5 | 1/0/7 · 2/1/8 · 2/1/10 | 1.35 | On Field: deal 2 damage to a target creature. |
| Cape Buffalo | 6 | 1/1/6 · 2/1/9 · 2/3/11 | 1.35 | While active, your creatures get +1 ATK. |
| Monarch Butterfly | 4 | 0/0/4 · 1/2/5 · 2/4/5 | 1.33 | Gets +2 ATK for each of your creatures waiting in your Prep Area. |
| Tiger | 6 | 1/3/5 · 2/4/6 · 2/4/8 | 1.30 | Overcrush. On Attack: deal 2 damage to the opponent directly. |
| Anglerfish | 6 | 1/1/6 · 2/3/7 · 3/4/10 | 1.29 | On Attack: every weak opposing creature (3 DEF or less) can't block this turn. |
| Wolverine | 4 | 1/1/4 · 1/2/5 · 2/3/5 | 1.25 | Fast. On Attack: deal 1 damage to the opponent directly. |
| Orca | 5 | 1/1/4 · 1/3/5 · 2/3/7 | 1.21 | On Field: KO a target creature. |
| Snapping Turtle | 5 | 1/0/5 · 2/1/7 · 2/3/8 | 1.20 | On Field: KO a target creature. |
| Opossum | 3 | 0/0/2 · 1/0/4 · 1/2/5 | 1.18 | Deadly. |
| Peregrine Falcon | 6 | 1/2/4 · 2/3/6 · 3/4/7 | 1.08 | Fast. On Field: deal 3 damage to a target creature. |
| Hummingbird | 4 | 0/0/3 · 1/2/3 · 1/2/5 | 1.07 | On Field: KO a target creature. |
