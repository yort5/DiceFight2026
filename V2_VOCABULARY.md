# DiceFight v2 — the vocabulary

**What this is**: the closed vocabulary the v2 engine actually implements
today, as of 2026-09-01. It is derived from the code, not from an earlier
version of itself — which is the whole reason it was rewritten. The
previous version was a 3,700-line append-only log whose "FROZEN" spec at
the top had been amended eight times further down, so a reader had to
reconstruct the truth from corrections.

**What it is not**: the reasoning. That lives in
`V2_VOCABULARY_HISTORY.md` (28 parts, the validation arc and every
decision) and in the code's own comments, which are the more reliable of
the two. Read the history to learn *why*, or to check whether a shape was
already tried and rejected.

**Ground rules, unchanged**: implementing sessions code against this file.
A card that does not fit goes to `V2_TAIL_POLICY.md` — never add
vocabulary without user sign-off. The vocabulary is deliberately closed;
that is the bet the whole v2 rewrite is making (see
`ARCHITECTURE_REVIEW.md` for what an open one cost).

---

## The shape of an ability

```
TriggeredAbility(Trigger, Effect, Filter?, EnergyCost?, OncePerTurn)
ContinuousDef                        // always-on, no trigger
```

A card carries four lists: `Abilities` and `Continuous`, plus
`PermanentAbilities` and `PermanentContinuous` for text that blanking
cannot remove. Nothing enumerates them directly — ask
`QueryEngine.AbilitiesOf(state, die)` / `ContinuousOf(card)`.

## Triggers (11)

`DieFielded` · `DieKOd` · `DieDamaged` · `DieAttacks` · `DieBlocks` ·
`DiceDrawn` · `PurchaseMade` · `TurnStepEntered` · `DieUsed` ·
`DieFaceChanged` · `Global`

Several v1 trigger types are **not** here because they are an existing
trigger plus a filter: "when ANOTHER die is fielded/KO'd/attacks" is
`ExcludeSelf`; "start of the opponent's attack step" is
`TurnStepEntered` + `Step`; Awaken is `DieFaceChanged` + `LevelIncreased`;
Teamwatch is `DieFielded` + `SharesAffiliationWithListener`.

### EventFilter

`Ownership` · `Tags` · `Affiliations` · `ExcludeSelf` · `LevelIncreased`
· `SharesAffiliationWithListener` · `MinPurchaseCost` · `Stat` · `Step`

## Effect templates (21)

| | |
|---|---|
| **Damage / removal** | `DealDamage` `Ko` `LifeChange` |
| **Movement** | `MoveDie` `DrawToZone` `FieldDie` `PrepFromBag`* |
| **Faces** | `Reroll` `Spin` `SpinToEnergy` |
| **Granting** | `GrantTag` `GrantAbility` `GrantCounter` |
| **Blanking** | `BlankText` (die) `BlankCardText` (card) `RememberCard` |
| **Stats / costs** | `ModifyStat` `PurchaseModifier` `CombatFlag` |
| **Control flow** | `Sequence` `Conditional` `MayPay` `DrawAndChooseOne` |

\* `PrepFromBag` is spelled `DrawToZone(1, Zone.PrepArea, Zone.Bag)`.

## Continuous templates (8)

`StatAura` · `CostModifier` · `TagAura` · `CombatRule` · `DamageModifier`
· `TargetingProtection` · `AbilityBlank` · `Lockout`

## Conditions (7)

`CountAtLeast` · `TargetWasKOd` · `OnBurstFace` · `OnFaceKind` ·
`LifeComparison` · `NoKOsThisTurn` · `TurnFact`

## Amounts (4)

`Fixed` · `PerMatch` · `StatOf(binding, stat)` · `EventValue`

`StatOf` captures at BIND time, not use time — which is what makes
rule 3.1.7's simultaneity fall out for free — and reads the `GetBase*`
queries, so applied modifiers count and conditional auras do not.

## Durations (3)

`EndOfTurn` · `UntilYourNextTurn` · `Permanent`

`UntilYourNextTurn` survives one further Clean Up, expiring at the one
that hands control back to the granter.

---

## TargetFilter — one shape for everything

```
Ownership  Zones  Kind  Count  Tags  Affiliations  Stat
Optional  Self  BindAs  Bound  AnsweredBy
```

`Kind`: `AnyDie` · `CharacterDie` · `ActionDie` · `BasicActionDie` ·
`Player` · `DieOrPlayer`.

**Tags and Affiliations are separate on purpose.** Tags hold keywords,
the card name, energy symbol ids and `"sidekick"`; affiliations have
their own field and their own query. The rules make affiliation a card
*attribute* (1.2) while keywords are *abilities* (3.4.7) — and blanking
takes the second and not the first, which one flat string set could not
express.

---

## Dice

A `DieDefinition` is a list of `Face`s. Every face **declares** its kind —
`EnergyFace`, `CharacterFace`, `ActionFace` — because nothing in the data
can classify it: a character face may print energy symbols, and an action
face has neither symbols nor character data.

The migrated dice are the real six (`MigrationDice`):

| Die | Faces |
|---|---|
| Character | 2 doubles + 1 single of its own type, then one face per level |
| Crossover character | doubles carry **one pip of each** type; the single is Generic (or Wild at four types) |
| Action | the card's **own** energy faces, then 3 action faces |
| Basic Action | 3 **generic double** faces (2.6.1.5), then 3 action faces |

Energy faces come first, so a character die's level N is at index N + 2.

### Card types nest

```
Action                 no fielding cost, attack or defense
  └─ BasicAction       the subset both players share (2.1.2)
       └─ EpicBasicAction   not modelled (user's call)
```

Ask `CardTypes.IsActionDie()` / `.IsCommunity()`, never `== BasicAction`.
"Action die" in card text means the whole tree; "Basic Action die" means
the shared subset only.

### Energy symbols

Declared in `GameConfig`, with two flags that are opposites (rule 1.4.3):

- `IsWild` — represents any **one** of the four types. One pip, one type.
- `IsGeneric` — pays toward the amount, never toward a type requirement.

---

## Blanking, in one place

A blanked die **loses** its card's keywords, triggered abilities, Globals
and continuous templates. It **keeps** every printed attribute (name,
subtitle, cost, energy type, affiliation), its face stats, its die kind,
anything **granted** to it, and its card's **permanent** text.

Two scopes, both needed: die-scoped is the default, and card-scoped
reaches copies not yet in play and the card's Globals (which need no die,
so a die-scoped blank could never turn one off).

Four derived queries fold stored one-shot suppression with live
continuous blanks: `AbilitiesActive` · `CardTextActive` · `CanPurchase` ·
`CanField`. `AbilitiesActiveBase` is the recursion break — two
mutually-blanking dice are a paradox in the rules, and the engine
resolves one level and stops.

---

## Where to look

| Question | File |
|---|---|
| What can I express? | this file |
| Why is it like that? | `V2_VOCABULARY_HISTORY.md`, and the code's comments |
| What doesn't fit, and what's the plan? | `V2_TAIL_POLICY.md` |
| What's built, what's next? | `V2_PLAN.md` |
| Why a closed vocabulary at all? | `ARCHITECTURE_REVIEW.md` |
