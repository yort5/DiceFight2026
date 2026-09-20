# Dice Kingdom cost model (2026-09-20)

Why: the original roster was ported from Dice Masters cards whose printed costs already priced in their abilities. Once abilities were simplified, costs and stats no longer matched (Elephant cost 6 for 1/1-2/1-3/1 stats; Greyhound cost 4 for 1/5-3/8).

## Homash value

From https://dmunited.eu/what-in-the-world-are-homash-values/ - total ATK+DEF across the three levels, divided by the cost of using the die:

    Homash = sum(ATK + DEF over 3 levels) / (purchase cost + sum of fielding cost over 3 levels)

The article calls ~2.0 reasonable and >2.0 strong, and notes that abilities aren't in the formula - so here the ability is *paid for out of the stat budget*. (Our dice also carry 3 energy faces, but every Character has the same 3, so it cancels out of comparisons.)

## Bands (enforced by `DiceKingdomCostModelTests`)

| Card type | Target Homash |
|---|---|
| Vanilla (no ability, no keyword) | 2.2-2.6 - "really good stats" or cheap |
| Keyword-only (Fast/Overcrush) | ~1.9-2.1 |
| Ability card | 1.0-2.2, lower the stronger the ability |

Rough stat-point prices used when re-statting (per 3-level total): gain life 1.5/pt; 1 dmg to a creature 3; 2 dmg 5; 3 dmg 8; KO 9; 1 direct dmg on attack 3, 2 direct 5; draw 4-4.5; +1 ATK aura 7, +1 DEF aura 5; Fast 4; Overcrush 3. Vanilla baseline = 2.4 x total cost; an ability card gets that minus its ability price.

## Keywords now on cards

`Fast` and `Overcrush` - both already implemented in `CombatEngine`; declared in `DiceKingdomConfig.Config.Keywords`. Cards: Wolverine (Fast + 1 direct), Peregrine Falcon (Fast + 3 dmg), Greyhound (Fast), Grizzly Bear (Overcrush), Tiger (Overcrush + 2 direct). Vanilla: Elephant, Hippopotamus, Hermit Crab.

Not yet engine-supported (candidates for later): Regenerate, Retaliation, Swarm, Range/Infiltrate (UI already checks these names), Global abilities.

## Current roster

| Card | Cost (buy/field) | Levels (A/D) | Homash | Ability |
|---|---|---|---|---|
| Elephant | 6/2 | 2/6 3/7 3/8 | 2.42 | Vanilla - no ability. |
| Hippopotamus | 4/2 | 0/6 1/7 2/8 | 2.40 | Vanilla - no ability. |
| Hermit Crab | 2/1 | 0/3 1/3 1/3 | 2.20 | Vanilla - no ability. |
| Grizzly Bear | 5/2 | 2/4 3/5 4/5 | 2.09 | Overcrush. |
| Homing Pigeon | 4/2 | 0/5 1/6 1/7 | 2.00 | On field: gain 2 life. |
| Albatross | 5/2 | 1/4 2/5 3/6 | 1.91 | On field: deal 2 damage to a target creature. |
| Greyhound | 4/2 | 1/4 2/5 2/5 | 1.90 | Fast. |
| Pangolin | 3/1 | 0/2 1/3 2/3 | 1.83 | On field: gain 1 life. |
| Opossum | 3/1 | 0/2 1/3 2/3 | 1.83 | On field: a weak target creature (3 ATK or less) can't block this turn. |
| Mongoose | 3/1 | 2/1 2/1 3/2 | 1.83 | Whenever this levels up: deal 2 damage to a target creature. |
| Cowbird | 3/1 | 1/2 1/3 2/2 | 1.83 | Whenever this levels up: move an opposing die from their Prep Area back to their Bag. |
| Cape Buffalo | 6/2 | 1/4 1/6 2/8 | 1.83 | While active, your creatures get +1 ATK. |
| Raven | 5/2 | 0/5 1/6 1/7 | 1.82 | On field: deal 2 damage to a target creature. |
| Fox | 5/2 | 1/4 1/5 2/7 | 1.82 | While active, your creatures get +1 DEF. |
| Swift | 2/1 | 1/1 1/2 2/2 | 1.80 | On attack: draw a die into your Prep Area. |
| Osprey | 4/2 | 0/4 1/5 2/6 | 1.80 | On attack: move a die from your discard to your Prep Area. |
| Musk Ox | 4/2 | 0/5 1/5 1/6 | 1.80 | While active, your creatures get +1 DEF. |
| Hyena | 4/2 | 1/4 1/5 2/5 | 1.80 | Gets +1 ATK for each weak opposing creature (2 DEF or less). |
| Honey Badger | 2/1 | 0/2 1/2 1/3 | 1.80 | On field: deal 1 damage to a target creature. |
| Cuttlefish | 2/1 | 0/2 1/2 1/3 | 1.80 | On attack: spin a target opposing level 1 creature to an energy face. |
| Barn Owl | 4/2 | 0/5 1/5 1/6 | 1.80 | On field: a weak target creature (3 ATK or less) can't block this turn. |
| Anglerfish | 6/2 | 1/4 2/5 3/6 | 1.75 | On attack: every weak opposing creature (3 DEF or less) can't block this turn. |
| Stoat | 4/1 | 0/2 1/3 2/4 | 1.71 | On field: deal 1 damage to the opponent directly. |
| Wolverine | 4/2 | 1/3 2/4 3/4 | 1.70 | Fast. On attack: deal 1 damage to the opponent directly. |
| Tiger | 6/2 | 2/3 3/4 3/5 | 1.67 | Overcrush. On attack: deal 2 damage to the opponent directly. |
| Magpie | 3/1 | 1/1 1/2 2/3 | 1.67 | On field: draw a die into your Prep Area. |
| Box Turtle | 3/1 | 0/2 1/3 1/3 | 1.67 | On field: deal 1 damage to a target creature. |
| Barn Swallow | 3/1 | 0/2 0/3 1/4 | 1.67 | Whenever this levels up: draw a die into your Prep Area. |
| Snapping Turtle | 5/2 | 0/4 1/5 2/6 | 1.64 | On field: KO a target creature. |
| Queen Termite | 4/2 | 1/3 1/4 2/5 | 1.60 | While active, your creatures get +1 ATK. |
| Monarch Butterfly | 4/1 | 0/2 1/3 2/3 | 1.57 | Gets +2 ATK for each of your creatures waiting in your Prep Area. |
| Orca | 5/2 | 1/3 2/4 2/5 | 1.55 | On field: KO a target creature. |
| Peregrine Falcon | 6/2 | 1/3 2/4 3/5 | 1.50 | Fast. On field: deal 3 damage to a target creature. |
| Mountain Goat | 3/1 | 0/2 1/2 1/3 | 1.50 | On attack: draw a die into your Prep Area. |
| Hummingbird | 4/1 | 0/2 1/2 1/3 | 1.29 | On field: KO a target creature. |
