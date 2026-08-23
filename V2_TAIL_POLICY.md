# V2 Tail Policy

Cards whose real ability text doesn't fit the closed v2 vocabulary
(V2_PLAN.md ground rule 2 - Appendix C format). Policy meanings:

- **Approximate** — expressed in templates with a stated difference
  (also noted in the card's own definition comment).
- **Vanilla** — no ability; RawText still shown in UI. Default while
  migrating.
- **Ask** — flag for the user; candidate for redesign under Direction
  C, or for a small vocabulary sign-off ask later.

Ask-policy entries below are **Vanilla** in `src/DiceFight.V2/Data/CardCatalog.cs`
(`IsImplemented: false`) pending a user decision on whether/how to
close the gap. Never guess a wrong approximation silently (house rule,
carried from v1).

## Curated team migration (V2_PLAN.md Phase 8 task 2, 2026-08-23)

Of the 20 curated-team cards (`CardCatalog.TeamA*`/`TeamB*`), 9 are
implemented (Apocalypse, HarleyQuinn, CaptainMarvel, Dazzler,
ShockingGrasp, FranklinsGalactus, GodEmperorDoom, Groot fit cleanly;
Casket of Ancient Winters is Approximate - see its row) and 11 are
tailed Ask below. This is a lower fit rate than the DPS set's own
~82% (V2_VOCABULARY.md Part 11) for a specific, known reason: the
curated rosters were deliberately built by v1's own author to exercise
one live example of each Attack-Step keyword the web client needs
(Call Out, Infiltrate, Tag Out, Range, Intimidate) - and Phase 7's own
combat implementation deliberately did NOT port any of those five
keywords (only Overcrush and Fast), so every one of their showcase
cards was always going to tail here. Not a representative sample of
the wider catalog's fit rate.

*(2026-08-24)*: Casket of Ancient Winters' original Ask entry (the
rule-3.2.5 live-resolution gap) is RESOLVED - the user signed off on
per-ability snapshot semantics (every TargetFilter candidate pool
inside one ability resolves against that ability's own
start-of-resolution zone/face snapshot; the snapshot dissolves when
the ability finishes, so later queued abilities see live state -
which is also the semantics a blanked card's already-queued trigger
will need once the ability-blanking spike lands). Implemented in
`EffectInterpreter`/`TargetResolver`; conditions (`TargetWasKOd`) and
`PerMatch` amounts deliberately stay live. The card's remaining
difference is only its Epic Basic Action mechanics, tracked below.

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| MSW019 | Beast | Regenerate keyword (reroll instead of KO) - not CombatFlag/CombatRule-shaped, not ported in Phase 7 | Ask |
| MSW020 | Black Panther | Energize's precise trigger (an energy face showing 2+ pips, during Roll & Reroll) - `EventFilter`/`Condition` have no symbol-count check; deferred exactly per Phase 5's own note ("wiring deferred to whichever card needs it first") | Ask |
| GOTG005 | Black Widow | Call Out keyword (designated-blocker restriction + cancellation rules) - not ported in Phase 7 | Ask |
| JLL002 | Ant-Man (Through The Cracks) | Amplify keyword - reacts to ANY of the controller's Action-die uses, not just its own (`TriggerKind.DieUsed`'s self-only shape doesn't cover "any action die"); Amplify itself also not ported | Ask |
| MSW002 | Cosmic Cube (epic) | `SwapLife` - life-total swap, explicitly named non-coverage (V2_PLAN.md Appendix A); also Epic Basic Action mechanics (once-per-turn limiter, returns to card) have no `CardType` distinction | Ask |
| MSW027 | Falcon | `Teamwatch` isn't one of the 10 frozen trigger kinds; its Global's `FieldSidekickForEachPlayer` per-player "field one if able" shape has no template equivalent | Ask |
| GOTG105 | Ricochet | Infiltrate keyword (+ its own `WhenInfiltrates` reactive) - not ported in Phase 7 | Ask |
| TAG003 | Big E | Tag Out keyword - not ported in Phase 7 | Ask |
| SKC090 | Starfire (Starbolts) | Range keyword - not ported in Phase 7 | Ask |
| CW014 | Scarlet Spider | Intimidate keyword; its own destination (`Zone.Intimidated` in v1) has no equivalent in v2's 10-zone list at all | Ask |
| MSW001 | Casket of Ancient Winters (epic) | Effect tree fully implemented (rule-3.2.5 per-ability snapshot, signed off 2026-08-24 - see the dated note above). Remaining difference: Epic Basic Action mechanics (rule 1.2.3 - once-per-turn limiter, die returns to its card instead of Out of Play) aren't modeled; `CardType` has no Epic distinction, so the die behaves as an ordinary Basic Action die | Approximate |
| GOTG008 | Cosmic Cube (Infinite Possibilities) | A "redraw a chosen subset of dice already drawn this turn" flow - explicitly named non-coverage (V2_PLAN.md Appendix A: "draw-and-choose flows") | Ask |

## DPS catalog batch 1 (V2_PLAN.md Phase 8 task 4, 2026-08-24)

14 of 15 implemented. The one below is tailed.

| CardId | Name | What it needs | Policy |
|---|---|---|---|
| DPS029 | Deathbird (Treacherous) | Deadly keyword - Phase 7 deliberately ported only Overcrush and Fast. Deadly is this card's entire text, so there is nothing else to express | Ask |

### RESOLVED: the timing-window model (Spike C, signed off + implemented 2026-08-24)

The user signed off on a **flat, ordered, extensible step list** and it
is now built (`V2_VOCABULARY.md` Part 13 for the design;
`Model/TurnStep.cs`, `GameConfig.Steps`, `EventFilter.Step`).
Colossus "Piotr" is un-tailed and implemented - its ability names
`StepIds.CleanUp` and fires there and nowhere else.

What this does NOT yet un-tail: the five combat keywords (Call Out,
Infiltrate, Tag Out, Range, Intimidate) are now *expressible* - the
step list can name their windows - but expressible is not built. Each
still needs its actual keyword behavior implemented, and their step
entries are added to `TurnStepDefs.Standard` when that happens, per
the same "declare it when it has a consumer" rule. They stay Ask.

Also still open from Spike C's write-up, deliberately not done in the
same pass: the three fidelity gaps (Main's end-of-step unfielded-dice
sweep, the Reserve Pool clearing at Clean Up rather than Clear and
Draw, and the missing attack-effects / block-effects / damage-ko-effects
windows and the Fast/normal damage split). The step list has ids
reserved for all of them (`StepIds`), but they are not in
`TurnStepDefs.Standard` until their procedures move.

