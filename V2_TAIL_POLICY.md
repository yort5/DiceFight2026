# V2 Tail Policy

Cards whose real ability text doesn't fit the closed v2 vocabulary
(V2_PLAN.md ground rule 2 - Appendix C format). Policy meanings:

- **Approximate** — expressed in templates with a stated difference
  (also noted in the card's own definition comment).
- **Vanilla** — no ability; RawText still shown in UI. Default while
  migrating.
- **Ask** — flag for the user; candidate for redesign under Direction
  C, or for a small vocabulary sign-off ask later.

Every entry below is currently **Vanilla** in `src/DiceFight.V2/Data/CardCatalog.cs`
(`IsImplemented: false`) pending an **Ask** decision from the user on
whether/how to close the gap. Never guess a wrong approximation
silently (house rule, carried from v1).

## Curated team migration (V2_PLAN.md Phase 8 task 2, 2026-08-23)

Of the 20 curated-team cards (`CardCatalog.TeamA*`/`TeamB*`), 8 fit the
frozen vocabulary cleanly (Apocalypse, HarleyQuinn, CaptainMarvel,
Dazzler, ShockingGrasp, FranklinsGalactus, GodEmperorDoom, Groot) and
12 are tailed below. This is a lower fit rate than the DPS set's own
~82% (V2_VOCABULARY.md Part 11) for a specific, known reason: the
curated rosters were deliberately built by v1's own author to exercise
one live example of each Attack-Step keyword the web client needs
(Call Out, Infiltrate, Tag Out, Range, Intimidate) - and Phase 7's own
combat implementation deliberately did NOT port any of those five
keywords (only Overcrush and Fast), so every one of their showcase
cards was always going to tail here. Not a representative sample of
the wider catalog's fit rate.

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
| MSW001 | Casket of Ancient Winters (epic) | Its own effect tree IS fully expressible (Ko + 2×MoveDie), but hits `EffectInterpreter`'s documented rule-3.2.5 live-resolution simplification: the Ko clause's own KO'd dice land in the Prep Area before the later Prep-Area-targeting MoveDie clause resolves, diluting its live candidate pool from 3 to 6 and raising an unintended `PendingChoice`. Confirmed by a failing test (not guessed) - see `CardCatalog.cs`'s own remarks. Fix requires the pre-execution-snapshot target resolution Phase 5 explicitly deferred. | Ask |
| GOTG008 | Cosmic Cube (Infinite Possibilities) | A "redraw a chosen subset of dice already drawn this turn" flow - explicitly named non-coverage (V2_PLAN.md Appendix A: "draw-and-choose flows") | Ask |
