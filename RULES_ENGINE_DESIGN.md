# Dice Masters Rules Engine — Design Notes (v0)

## Start here (current state, 2026-07-27)

**What exists**: `DiceFight.Engine` (rules engine - all four Main Step
actions, combat, the ability queue, the legal-target system),
`DiceFight.Api` (ASP.NET Core wrapper, one controller action per engine
action, in-memory game store, no persistence), `web/` (React + Vite - a
functional "dev console" board: click dice, contextual action tray, real
zone layout, a "How to Play" dialog explaining the controls - not a
polished game UI yet). 50 xUnit tests, all passing. `Zone` now splits the
Prep Area into a persistent zone (targeted by KO/Prep effects) plus two
transient staging zones used only within a single Clear & Draw → Roll &
Reroll cycle (`DiceFromBag`, `DiceFromPrep`) - see the two most recent
status updates for why (a Pepper Potts-shaped rules interaction the old
single-zone model couldn't express) and for the follow-up split of
`TurnEngine.RollAndReroll` into `Roll` + `FinishRoll` so a player sees the
roll before deciding what to reroll, instead of committing blind.
`Data/SampleCards.cs` declares 26 real cards; two fixed 10-card teams
(8 characters + 2 Basic Actions each, per real team construction rules -
the other 6 cards stay in the catalog, unused by either team) with real
names/subtitles/ability text pulled from
Teambuilder's data; only 7 have a scripted `AbilityDef` (see the
"Scripting policy" note near the top of that file) - the rest are
intentionally vanilla rather than simulating a partial/wrong subset of a
more complex card. Numeric stats (cost/energy/attack/defense) are mostly
still placeholder values (see next paragraph) - Big Barda, Harley Quinn,
Robin, and Starfire are the exception, with real cost/energy/per-level
stats pulled from the user's reference spreadsheet (see the bottom-most
status update).

**Deployed**: live on GCP Cloud Run with continuous deployment already
configured - every push to `main` on GitHub auto-builds the repo-root
`Dockerfile` (a combined container: the React build is copied into the
API's `wwwroot`, one process serves both `/api/*` and the app) and
redeploys automatically. Publicly accessible with no auth - acceptable for
now, explicitly a hobby project rather than something sensitive. The GCP
console/IAM setup details aren't recorded here since it's done and
working; nothing about continuing game development needs to touch it
unless the deploy itself breaks.

**Blocked on user input, not a technical task**: real per-level
attack/defense numbers. None of the six cloned DiceCoalition repos contain
them (confirmed by inspection - every community tool represents combat
stats via card-face images, never structured data), so every sample card
currently runs on placeholder stats. Getting real numbers means either a
manual data-entry effort against real physical/reference cards, or finding
a different data source - worth asking the user before investing time
here rather than assuming which approach they'd want.

**Actionable next steps, roughly high to low value**:
1. ~~Keyword *behavior*~~ - see "Implemented keywords" below for the
   full alphabetical list (now including Range) with rule summaries and
   example cards; full citations and design rationale are in each
   keyword's own "Status update" section further down.
   BlackPanther's Energize is fully scripted; Robin's Energize
   (a purchase-cost discount) and all three Alfred Pennyworth printings'
   Ally effects (each a "Batman die OR Sidekick" compound target) are
   deliberately left unscripted - the former needs a purchase-cost-
   modifier mechanism, the latter an affiliation-based `TargetSpec`
   filter plus an either-of-two-specs union, neither built yet (note:
   Attune's own "target player or Character die" union IS now built -
   `TargetSpec.PlayersAllowed`/`CharacterDieOrPlayer` - but that's a
   fixed die-or-player choice, not the general N-arbitrary-specs union
   Alfred's text would need). The Rock's Global ("Sacrifice a Superstar
   die, reduce the next purchase by 2") is the same purchase-cost-
   modifier gap - left vanilla for now, see the Sacrifice status update.
   Next up, per the user's own framing: work through the rest of the
   Dice Masters keywords on wizkids.com/dicemasters/keywords one at a
   time, each scripted against a real example card (user- or
   randomly-picked from the stats sheet).
2. ~~Global ability UX~~ - done as a standing sidebar (`GlobalAbilitiesPanel`)
   with its own energy-then-targets flow; every Global on both rosters
   (Distraction, Falcon, Invisible Woman, Starfire) is scripted, and the
   three rough edges called out here are now addressed (see the status
   update) - still no legal-target filtering on the *targets* stage
   specifically (would need the server to expose real legal-target
   queries, not just whether a target exists at all).
3. Legal-target exclusions for captured dice / per-die "cannot be
   targeted" abilities - blocked on Capturing (rule 3.8) not being built.
4. Team-construction legality (die-limit-sum-to-20, unique-card-name
   checks, rule 2.1.1/2.1.3) - currently unenforced; every card gets its
   full die limit regardless of team size (see `TeamSetup.cs`'s remarks).
5. Auth/login in front of the API - currently wide open, matches the
   deployment note above.
6. Real priority-passing (turn player acts, passes to the non-turn
   player for one window, who may act-or-pass, turn ends when the
   non-turn player passes and the turn player then passes with nothing in
   between) - this engine currently just has an open, un-timed action
   window per step instead. A concrete symptom: `OncePerTurn` Global
   limiters like Falcon's "Once during your turn" are enforced as a flat
   once-per-turn-cycle limit usable by *either* player, not scoped to
   whoever's turn it actually reads as - see the status update where this
   was found. A real, separate architectural piece (new state, a "pass"
   action, every Main Step action needing to reason about whose window it
   is), not a one-line fix - deferred pending the user's prioritization.
7. Virtual generic energy currently only expires at Clean Up, not at the
   end of the Main Step where rule 1.4.5 ("must be spent by the end of
   the Main Step, or it will be lost") actually puts it - found during
   the full turn-sequence review (see the status update) and confirmed
   reachable: a player can bank virtual energy in the Main Step and still
   spend it on a Global during the Attack Step's Action/Global window
   today. Deliberately left as a known gap rather than fixed immediately
   (the user's call) - fix shape when picked up: purge
   `IsVirtualEnergy` dice for the active player in `EnterAttackStep` and
   `SkipAttackStep`, not just `CleanUp`.
8. ~~`TurnEngine.CleanUp` never clears `AppliedModifiers`~~ - fixed (see
   the status update): now demonstrably reachable (Wasp's real Attune
   buff), not just hypothetical, so no longer deferred.
9. ~~`GamesController`'s `/declare-attackers` endpoint has no
   `TargetDieIds`~~ - fixed for `/declare-attackers` specifically (see
   the Increment 6 status update): `DeclareAttackersRequest` now carries
   `TargetDieIds`, threaded into `Drain`, with a real web client flow
   (`DeclareAttackersPanel`) that asks for a target whenever a declared
   attacker has Call Out. The flat-single-target-list-per-batch
   limitation this item originally flagged is still real (unfixed) - it
   just doesn't matter yet since Black Widow is the only Call Out card
   on either roster. The `/clear-and-draw` half (Cosmic Cube/Rip
   Hunter's `WhenDrawn`/`ClearAndDraw` abilities) turned out not to need
   a `TargetDieIds` field at all - see the "pending mid-resolution
   choice" status update: the real blocker wasn't a missing request
   field, it was that the player can't answer this in the same request
   that triggers it (the candidates don't exist, or can't be seen, until
   the draw already happened). Closed via a general pause/resume
   mechanism instead (`GameState.PendingChoice`), not a `TargetDieIds`
   addition.
10. ~~Rip Hunter's "Navigate the Sands of Time"~~ - implemented (see the
    status update). Turned out to need a new `TriggerType.ClearAndDraw`
    rather than reusing `WhenDrawn` (the gate is "while active," not
    "while this specific die is drawn"), and the "once during your Clear
    and Draw Step" limiter needed no new state at all - the Step itself
    only runs once per turn. Its own post-draw choice is now answered
    through `GameState.PendingChoice` (see item #9 and the "pending
    mid-resolution choice" status update), same as Cosmic Cube's.
11. ~~The web client's Attack Step UI has no case for `AttackSubStep.
    InfiltrateWindow`, `TagOutWindow`, or `RangeWindow`~~ - fixed (see
    the Increment 2/3/4/5 status updates): `attackSubStep` is now a real
    `AttackSubStep` union type, and `InfiltrateWindowPanel`/
    `TagOutWindowPanel`/`RangeWindowPanel` all exist and are wired into
    `App.tsx`'s panel-swap chain via `canResolveInfiltrate`/
    `canResolveTagOut`/`canResolveRange`. Verified end-to-end in a real
    browser session for all three (Ricochet/Big E/Starfire "Starbolts").
12. ~~Card catalog search/browse scaling seam~~ - the "thousands of
    cards" pool this item warned about is now actually imported
    (~3,637 bulk cards, see the "bulk-import the full reference sheet"
    status update) and the client-side filtering/sorting approach held
    up fine - verified fast (sub-second) at the real ~3,700-card scale
    in a live browser check, no server-side move needed after all.
13. ~~`/teambuilder` team selection + query-string encoding~~ - done
    (see the "team selection on /teambuilder" status update):
    `?team=<id>:<count>,<id>:<count>,...`, a "Strict rules" checkbox
    enforcing rules 2.1.1/2.1.3-2.1.5 by default with an override, and
    a "Copy team link" round-trip. Still not done: wiring a built team
    into actually starting a digital game - `GamesController.Create`
    is untouched, still always the two curated rosters. Natural next
    increment.
14. ~~A Discord bot~~ - card lookup (`/card`) and Teambuilder-link
    preview (`/team`) done, re-implemented (not copied) from the
    user's separate community bot - see the "a Discord bot" status
    update for the full scope discussion and what was deliberately left
    out. **Deferred, needs a real datastore first** (same gap as item
    #5/auth-then-team-storage): event roster/attendance/score-reporting/
    Challonge integration, and trade/want-list matching - both were real
    features in the original bot, both were backed there by Google
    Sheets used as a live read/write database (a pattern explicitly not
    worth porting - see the status update for why). Also not built:
    YouTube/RSS content-feed posting - a generic, real pattern, just out
    of scope for the card-lookup pass; could land later as its own
    `BackgroundService` in `DiceFight.DiscordBot` without touching the
    existing commands.
15. **In progress**: working through the Dark Phoenix Saga (DPS) set
    card by card - see the "Dark Phoenix Saga, first pass" status
    update for the first six cards hand-curated (Storm, Kitty Pryde,
    Phoenix, D'Ken, Ronan the Accuser, Power Bolt) and the full
    breakdown of *why* the rest of the set's unimplemented cards don't
    fit current primitives - several are real small missing subsystems
    (Continuous Action dice, Loyalty Counters, per-die targeting/
    blocking protection, "while [named card] active" conditional
    grants), not one-off skips. Per the user's own prioritization,
    Continuous is now built (see the "the Continuous keyword, and Lab
    Test" status update) and Lab Test (DPS005) is its first real card;
    Loyalty is now built too (see the "the Loyalty keyword, and Jean
    Grey" and "shared KO-reaction pipeline" status updates), with Jean
    Grey, Magneto, Supreme Intelligence, and Madelyne Pryor all real
    now - the reactive-KO-scan gap that blocked 3 of those 4 turned into
    a real engine fix (`TurnEngine.ResolveKOReactions`), not just a
    per-card workaround, and also closed a live bug in Retaliation
    (never fired off a Range KO) and WhenKOd (never fired off any
    ability-driven or Deadly KO). Still open: Gladiator (needs the
    unbuilt "can't be targeted" protection status, see next-steps item
    3), the 3 consumer-side Loyalty DPS cards (need a "has a Loyalty
    Counter" `TargetSpec` filter plus an aggregate team-wide count
    check), the other three Continuous DPS cards, and web client UI for
    actually resolving a Continuous die once it's sitting in the Field
    Zone. Also now real: Angel, Cable, Colossus, Toad, Lilandra (see the
    "five more DPS cards" status update) - which also caught a real
    authoring bug (Kitty Pryde/Phoenix had Energize/Awaken `AbilityDef`s
    with no matching `Keywords` entry, so neither would have actually
    fired in a real game; both fixed, plus a blanket test now scans the
    whole catalog for the same mismatch). Jubilee (DPS036) looked
    buildable the same way Colossus is but isn't yet - `FieldDie`
    assumes the target is already on a character face, which an
    Energize-triggering die by definition isn't. Also now real: Vulcan,
    Psylocke, Blob, and two more second-printing cards (see the "'must
    attack,' a conditional self keyword grant" status update) - which
    added `GameState.MustAttackThisTurn`/`ForceAttack` (the Declare-
    Attackers mirror of `MustBlockThisTurn`/`ForceBlock`) and `CardDef.
    GrantsSelfKeywordWhileNamedCardActive` (a live conditional keyword
    grant, first used by "gains Deadly while Wolverine is active").
    Burst/double-burst symbols (`*`/`**` in ability text) are now real
    too - `EffectCondition.OnSingleBurstFace`/`OnDoubleBurstFace` plus a
    new `Conditional.Else` branch (see the "burst and double-burst
    symbols" status update, including a correction to how these marks
    had been read in earlier updates on this list). Basic Action/Action
    dice now have a real per-face model too (`DieInstance.BurstStars`,
    `RolledFace.BurstStars`, `PlaceholderDiceRoller` actually
    randomizing among the 3 Action faces - see the "Basic Action dice
    now have real burst faces" status update) - Rally (DPS013) and now
    Gambit (DPS032, see the "closing the loop on the burst-symbol
    thread" status update - needed one more new primitive,
    `DrawAndChooseOneToRoll`, structurally almost identical to Corrupt)
    are both real. Still open: Take Cover/Radicalization/Explosion each
    have their own unrelated blocking gap (mass-apply-to-all-your-dice;
    a temporary affiliation grant; an AoE-to-everyone-plus-mana-sink-loop).
    Also now real: `CantBlock` (the restriction mirror of `ForceBlock`/
    `MustBlockThisTurn`, enforced by `CombatEngine.DeclareBlockers`
    rejecting the die as an eligible blocker outright) with Deathbird
    ("War of Kings", DPS109) as its first card, plus Deadpool ("More than
    a Chump Blocker", DPS068 - `WhenAttacks` dealing damage straight to
    the opponent, no new primitive) and Ronan the Accuser ("No
    Exceptions", DPS130 - "each player loses 3" is just two `LoseLife`
    calls, not a new mechanism) - see the "CantBlock, and three more DPS
    cards" status update. Real gaps found scoping the next batch, not yet
    built: a "reroll; each die that doesn't land on a character face goes
    to the Used Pile" primitive (blocks Gambit DPS112, Psylocke DPS150,
    Storm "Queen" DPS132 - a recurring pattern, not one-off), a
    stat-threshold `TargetSpec` filter (blocks Storm "Cloud Cover"
    DPS092's own CantBlock use), and "each player makes their own choice"
    threaded through an ability the opponent didn't trigger (blocks Ronan
    "No Mercy" DPS090's KO side, distinct from "No Exceptions"'s fixed
    amounts). Also now real: `RerollAndMoveUnlessCharacter` ("reroll
    target die(s); each that doesn't land on a character face goes to
    the Used Pile," confirmed recurring across ~20 cards spanning many
    sets, not DPS-only) plus an `optional` param on `TargetSpec.
    CharacterDie` it needed along the way - Gambit ("Unless I Got
    Someone to Play With", DPS112), Psylocke ("Advanced Telekinetic
    Combatant", DPS150, adding a damage-per-moved-die follow-up), and
    Storm ("Queen", DPS132, three abilities across WhenFielded/
    WhenAttacks/Energize) are all real now - see the "RerollAndMoveUnless
    Character, and three more DPS cards" status update, including why
    Storm's own sheet text ("Move each die that DOES roll a character...")
    is read as a typo rather than a real rules variant. Also now real:
    `GrantKeyword` ("target character die gains/gets [keyword]" -
    modeled as an Applied ability per rule 3.4.3.9, so it defaults to
    "until end of turn" on `DieInstance.AppliedKeywords`, same lifecycle
    as `AppliedModifiers`, even though neither Magik's nor Psylocke's own
    text says so explicitly) and `TargetSpec.MaxAttack` ("target
    character die with 3A or less," checked against `DieStats.
    EffectiveAttack` and enforced by `EffectInterpreter.Resolve`'s own
    existing legal-target check) - Magik ("Sorceress of Limbo", DPS120),
    Psylocke ("Telepath", DPS088), and Storm ("Cloud Cover", DPS092) are
    all real now, closing both gaps flagged in the previous entry - see
    the "GrantKeyword, TargetSpec.MaxAttack, and five more DPS cards"
    status update, including a real authoring trap it caught (the bulk
    sheet mis-attributes Magik's OWN granted keyword as one of her
    printed Keywords). Also now real: `TriggerType.WhenAnotherDieFielded`
    (same shape as `WhenAnotherDieKOd`, fired from `TurnEngine.Field`
    instead of a KO), `StaticTeamBonus.RequiredAffiliation`, `CardDef.
    GrantsSelfAttackBonusPerMatchingDie` (+ `TargetSpec.MaxDefense`), a
    `FieldDie` fix (Sidekick-aware Status, a configurable Level - closing
    the exact gap that had left Jubilee "Rebellious Nature" vanilla), and
    `EffectCondition.OwnLifeLessThanOpponent` - nine more DPS cards
    (Kitty Pryde, Sabretooth x2, Psylocke, Magneto, Toad, Jubilee x2,
    Cyclops) landed in the same round - see the "WhenAnotherDieFielded, 5
    more primitives, and nine more DPS cards" status update, including a
    retroactive fix it needed (Jean Grey's own "Founder" prefix, first
    treated as pure flavor text, needed a real `KeywordInstance` once
    Cyclops's own filter had to recognize it). Also now real:
    `Conditional.AffiliationParam`/`NamedCardParam`/`CountParam` (three
    parametrized `EffectCondition`s - `TargetHasAffiliation`,
    `NamedCardIsActive`, `OpponentHasAtLeastNCharacterDiceInFieldZone`),
    `GameState.PendingPurchaseDiscount`/`GrantNextPurchaseDiscount` ("the
    next die you purchase this turn costs N less," consumed by
    `TurnEngine.Purchase`), `CardDef.GrantsFreeFielding` (a granter-side
    check consumed by `TurnEngine.Field`'s new `IsFreeToField`), and
    `CardDef.CannotBeTargetedByOpponentWhileNamedCardActive` (enforced
    in `LegalTargets.Query`) - ten more DPS cards (Jubilee, Kitty Pryde,
    Corsair, Phoenix, Dark Phoenix, Magik, Take Cover, Deadpool,
    Mystique, Iceman) landed in this round, including Take Cover - a
    Basic Action previously flagged as blocked by "mass-apply-to-all-
    your-dice," now unblocked entirely by the already-existing
    `MatchAll` - see the "six more primitives, and ten more DPS cards"
    status update, including a closing list of what's left that's each
    its own deeper, real gap (a "who caused this KO" tracking gap, an
    opponent-makes-their-own-choice mechanism, a start-of-opponent's-
    Attack-Step trigger hook, a cross-player static debuff, ability-
    blanking, spawning a token die not backed by any card, damage
    redirection, and a temporary Global-activated targeting-immunity
    shape distinct from Kitty Pryde's own continuous one). **Update: four
    of those closed in the very next round** - `OpponentKOsOwnCharacterDie`
    (Ronan "No Mercy"/DPS090 - turns out `GameState.PendingChoice`
    already generalizes to "the opponent answers" with zero new
    plumbing, since `ControllerId` on it was never actually enforced
    against the submitting player), `TriggerType.
    StartOfOpponentsAttackStep` (both Emma Frost printings, fired from a
    newly-optional-queue-param `TurnEngine.EnterAttackStep`),
    `PlaceToken`/`CardType.Token` (Master Mold "Endless Sentinels" -
    caught a real pre-existing bug along the way: `DieStats.GetFace`/
    `GetMaxLevel` both checked `CardId is null` alone instead of
    `VirtualCardId ?? CardId`, so a die with only a `VirtualCardId` -
    never exercised before now - silently got the bare 1A/1D Sidekick
    face instead of its real stats), and `CardDef.
    GrantsOpponentStatDebuff` (Vulcan "Aggession," the cross-player
    mirror of `GrantsStaticTeamBonus`) - see the "tackling the deeper
    gaps" status update, including why the other three (damage
    redirection, ability-blanking, Gladiator's temporary team-wide
    targeting immunity) stayed deliberately unattempted: each would
    touch several existing call sites at once, where a partial version
    risks silently-wrong behavior elsewhere rather than just missing one
    card. Also now real: `SpinToEnergyFace` ("spin [a/
    target] die to its single/an energy face," reusing `PlaceholderDiceRoller`'s
    own Character-die energy-face formula) and `TargetSpec.RequiredLevel`
    ("target opposing level 1 character die") - Magik ("Better than
    Belasco", DPS080, pure Awaken+DrawDice, no new primitive), Professor
    X ("Uncanny Leadership", DPS127, `SpinToEnergyFace`'s first user plus
    an Energize ability that caught a real gap: a Used Pile die is always
    unrolled per rule 1.6.8, so `TargetSpec.CharacterDie`'s
    `CharacterDiceOnly` filter can never match one - `AnyDie` needed a
    `requiredAffiliations` param instead), and Iceman ("Icy
    Interference", DPS034, combining `SpinToEnergyFace` with
    `RequiredLevel`) are all real now - see the "SpinToEnergyFace,
    TargetSpec.RequiredLevel, and three more DPS cards" status update.
    Two OTHER "spin to an energy face" DPS cards (Magneto/Mystique) say
    "of your opponent's choice" - a real, separate "other player makes a
    choice" gap, same category as Ronan "No Mercy," deliberately left
    for later. Also now real: `CardDef.
    GrantsSelfStatBonusWhileNamedCardActive` (the stat-bonus counterpart
    to the existing keyword-grant version, closing a gap flagged in that
    field's own remarks several updates back) and `SetStat` ("target
    character die has 0A this turn" - a snapshot to an exact value,
    stored as an ordinary `Modifier` computed once so it expires at Clean
    Up for free) - Cyclops ("Defending the Phoenix", DPS065, pure
    existing-primitive reuse), Rogue ("Strength Absorption", DPS151,
    `SetStat`'s first user), and Moira ("If It's Real", DPS084, all
    three of its abilities built at once: the stat-bonus-while-active
    grant, a `WhenFielded` X-Men-wide buff combining `RequiredAffiliations`
    + `MatchAll`, and a `WhenKOd` Prep) are all real now - see the
    "GrantsSelfStatBonusWhileNamedCardActive, SetStat, and three more
    DPS cards" status update. Also now real: `TargetSpec.RequiredAffiliations`
    ("target Brotherhood of Mutants/X-Men character die," matching ANY
    of a listed affiliation set) and `TargetSpec.MatchAll` ("deal 2
    damage to ALL X-Men and Brotherhood of Mutants character dice" /
    "opposing character dice with less than 4A can't block" - no chosen
    target at all, so `EffectInterpreter.Resolve` short-circuits straight
    to every legal match without asking a caller to choose) - Master
    Mold's "Targeting Mutants"/DPS082 and "Untold Electronic Expertise"/
    DPS122 use the affiliation filter alone, "Inexplicable Durability"/
    DPS042 combines it with MatchAll, and Phoenix "Eternal Flame"/DPS126
    combines MatchAll with the existing MaxAttack filter instead - see
    the "TargetSpec.RequiredAffiliations, TargetSpec.MatchAll, and four
    more DPS cards" status update. This was the affiliation-filter gap
    the bulk-card-catalog memory had already flagged as blocking ~15
    more bulk cards beyond DPS, not just a DPS-scoped fix.
    **Update: the three remaining deeper gaps from two updates back are
    now all closed too** - damage redirection (`DieStats.ApplyDamage`,
    the single choke point every damage-application site, combat and
    ability alike, now funnels through - Colossus "Organic Steel"/
    DPS063), ability-blanking (`DieStats.GetCard`, a second choke point
    consulted everywhere a die's card is looked up for keywords/
    triggered/static-ability purposes specifically, NOT for affiliation/
    energy-type/printed-stat purposes - `GameState.BlankedDieIds`/
    `BlankedControllerIds` - Vulcan "Power Suppression"/DPS095's own
    combat-scoped engaged-dice blank and Mister Sinister "Mutant
    Supremacist"/DPS083's whole-side and single-die blanks), and
    Gladiator's temporary Global-activated targeting immunity
    (`EffectContext.Trigger` threading the currently-resolving ability's
    own `TriggerType` down into `LegalTargets.Query`, which now excludes
    a protected controller's dice specifically when the query's trigger
    is `Global` or `WhenUsed` - `GameState.
    ImmuneToActionAndGlobalTargetingControllerIds` - both Gladiator
    printings, "Psi Resistance"/DPS033 and "Majestor Kallark"/DPS113) -
    see the "damage redirection, ability-blanking, and targeting
    immunity" status update for the full design writeup of each.
    Fourteen more DPS cards landed the next round, each needing at most
    one small, narrowly-scoped new primitive: two self-referential
    fielding conditions (`SelfFreeFieldingUnlessTeamHasAffiliation`/
    `SelfFreeFieldingWhileOtherActiveAffiliation` - Wolverine "Pure of
    Heart"/DPS056, Jean Grey "Marvel Girl"/DPS115), two cross-player
    surcharges (`GrantsOpponentPurchaseSurcharge`/
    `GrantsOpponentGlobalSurcharge` - Forge "Support Technician"/DPS071,
    both Jean Grey printings), a named-card support buff
    (`GrantsNamedCardSupport` - Cable "Bosom Buddies"/DPS062),
    `StaticTeamBonus.RequiredKeyword`/`ExcludeSelf` (Angel "Jean Grey's
    School"/DPS057) and `GrantsSelfStatBonusWhileOwnSidekickActive`
    (Beast "Xavier's Dream"/DPS138), two new `TargetSpec` dimensions
    (`ActionDiceOnly`/`MatchesOwnTeamAffiliation` - Rogue "Surveillance
    Immunity"/DPS089, Moira "Strength of Foresight"/DPS124, Mystique
    "Relentless"/DPS045), a new effect node pair (`SwapAttack`/
    `GrantNextPurchaseGoesToBag` - Rogue "Mrs. X"/DPS049, Corsair
    "Recruiting a Crew"/DPS024), and a reuse of Gladiator's own
    `TriggerType`-aware `LegalTargets.Query` filtering for a continuous,
    granter-active-scan Sidekick-targeting immunity (Angel "Xavier's
    Dream"/DPS137). Deadpool "#1 Draft Pick" (DPS028) needed nothing at
    all - its whole printed text is conditioned on a draft format this
    project doesn't model, so it's vanilla by simple absence of any true
    condition. Still open: Lilandra's two printings (need Action-Die
    usage cost plumbing, which doesn't exist at all yet - Action dice
    are currently just free to use), and Iceman/Cyclops's own "Xavier's
    Dream" printings (share Beast's "own active Sidekick" gate but land
    on a live A=D relationship and a divided-damage `WhenAttacks`
    respectively, neither fitting the flat-delta shape built for Beast)
    - see the "fourteen more DPS cards" status update.
    Ten more DPS cards landed the round after that, several sharing new
    primitives across 2+ cards: five new count-threshold
    `EffectCondition`s (`OwnCharacterDiceInFieldZoneAtLeast` - Cyclops
    "Utopia Realized"/DPS105; `OwnActiveAffiliationOrKeywordCountAtLeast`
    - Wolverine "Hardened by Madripoor"/DPS096 AND Mutant Research
    Program/DPS008; `OwnTeamWideLoyaltyCounterCountAtLeast` - Living the
    Dream/DPS006; `OnlyCharacterFieldedThisTurn` - Gambit "I Like
    Solitaire"/DPS072), two new effect nodes (`SpinToCharacterLevel`,
    `DoublePrintedAttackOfEach` - Cable "High Stakes"/DPS102), and four
    new granter-side CardDef fields each used by one card
    (`GrantsFieldingCostReduction` - Rogue "Unity Squad"/DPS129;
    `GrantsMinimumBlockersRequirement` - Magneto "Visionary"/DPS081, a
    new `CombatEngine.ValidateMinimumBlockers`; `SelfFirstPurchaseSurcharge`
    - Beast "Combat Ready"/DPS098; `GrantsSelfPurchaseDiscountIfOpponentHasAffiliation`
    - Dark Phoenix "Malevolent"/DPS027, which also reuses
    DarkPhoenixEnemyOfTheShiar's own Global `Sequence` verbatim). Also
    found and fixed a real, previously-unexercised bug along the way:
    `EffectInterpreter.Resolve`'s `TargetSpec.Self` case returned an
    empty list whenever `ctx.SourceDieId` was null - always true for a
    Global ability (rule 3.1.5) - which silently forced every state-only
    `Conditional` keyed on `TargetSpec.Self` (`PrepAreaEmpty` etc.) to its
    Else/no-op branch regardless of the real condition; Magneto
    ("Idealist," DPS041)'s own Global had this exact shape and had
    apparently never once actually fired correctly before the fix. See
    the "ten more DPS cards" status update for the full list and the
    Damage-resets-to-0-on-KO test-authoring trap it caught again.

## Implemented keywords

Every Appendix 1 keyword built so far, alphabetically. Each has its own
"Status update" section below (in build order) with full rule citations
and design rationale - this is just the scannable index.

- **Ally** (Alfred Pennyworth - all three printings) - a Character die
  with Ally counts as a Sidekick while in the Field/Attack Zone, in
  addition to its own attributes.
- **Amplify** (Ant-Man) - each time you use an Action die, spin every
  active Amplify die up one level (if able).
- **Attune** (Wasp) - while active, each time you use an Action die,
  deals 1 damage to a target player or Character die.
- **Awaken** (Cyclops) - fires once per die, every time an Awaken die
  spins up one or more levels, whatever caused it.
- **Call Out** (Black Widow) - when this die attacks, target an
  opposing Character die; only that die (or none) may legally block it.
- **Continuous** (Lab Test) - a Basic Action die that's "used" by moving
  it to the Field Zone rather than resolving its ability right away;
  its controller later chooses to remove it (`TurnEngine.
  ResolveContinuousDie`, `TriggerType.ContinuousResolve`), which is when
  the ability actually runs - explicitly not a second "use" (rule
  2.6.4.3), so it doesn't re-trigger Amplify/Attune/Obscure the way the
  original move did.
- **Corrupt** (Polaris) - target player draws X dice from their bag,
  refilling from the Used Pile if needed, then a real choice of which
  one goes to the Used Pile - answered through `GameState.PendingChoice`
  (see its own status update), since the candidates don't exist until
  the draw actually happens mid-effect.
- **Darkseid's keyword grant** (Darkseid) - "while active, your
  Sidekicks gain Swarm" - not itself an Appendix 1 keyword, but the
  first live, continuously-recomputed grant (as opposed to a discrete
  triggered ability).
- **Deadly** (Deathbird) - a die engaged with a Deadly die is KO'd at
  Clean Up, regardless of what happened to either die in between.
- **Energize** (Black Panther, "Clutching Reality") - triggers once a
  die with this keyword ends Roll and Reroll on a double-energy face.
- **Energy Drain** (Madalyne Pryor) - after blockers are assigned,
  spins every Character die engaged with an Energy Drain die down a level.
- **Experience** (Jamilah "Shipwrecked on Chult," D&D set only) - if you
  KO'd an opposing Monster-affiliation die this turn, every active
  Experience card gets a permanent +1A/+1D token at Clean Up.
- **Fast** (Wasp Pixie) - deals its combat damage in an earlier wave
  than non-Fast dice, so a KO'd non-Fast target never gets to hit back.
- **Infiltrate** (The Spot; Ricochet reacts to it) - an unblocked
  attacker may remove itself from combat to deal 1 damage directly and
  return to the Field Zone.
- **Intimidate** (Scarlet Spider) - when fielded, removes a target
  opposing Character die from the Field Zone until end of turn.
- **Loyalty** (Jean Grey, Magneto, Supreme Intelligence, Madelyne
  Pryor) - a per-CARD, cross-turn counter (unlike a per-die
  `AppliedModifiers` entry) worth a permanent +1A/+1D to every die of
  that card, regardless of zone - same shape as Experience Tokens.
  Jean Grey's own end-of-turn condition and three cards that grant a
  counter off *another* die's KO (`TriggerType.WhenAnotherDieKOd` +
  `AbilityDef.KOdFilter`, see the "shared KO-reaction pipeline" status
  update) are built; Gladiator's own Loyalty grant is still vanilla,
  blocked on its Global needing an unbuilt "can't be targeted"
  protection status, unrelated to Loyalty itself. Also see
  **Retaliation**'s own entry above - `WhenAnotherDieKOd` shares its
  new reactive-KO choke point (`TurnEngine.ResolveKOReactions`), which
  fixed real gaps in Retaliation's own firing (Range KOs never
  triggered it) and `WhenKOd`'s (ability-driven and Deadly KOs never
  fired it at all) along the way.
- **Obscure** (Drow Mercenary) - using any Action die makes every die
  of this card unblockable until end of turn.
- **Overcrush** (Apocalypse) - if the attacker KOs or otherwise removes
  all its blockers, any leftover attack damage hits the opponent directly.
- **Range** (Starfire "Starbolts") - when a Range attacker attacks,
  every active Range die on both sides simultaneously deals its own
  damage to a target opposing Character die.
- **Regenerate** (Beast) - a die that would be KO'd rolls instead;
  landing on a character face saves it (back to the Field Zone, not the
  Attack Zone), an energy face doesn't.
- **Retaliation** (Superman "Kal-El"; Black Manta "Deep Sea Deviant"
  scales the amount) - if an active Retaliation character shares an
  affiliation with one of your Character dice that's KO'd, deal damage
  to your opponent. Now fires off every real KO in the game (Range
  KOs used to silently skip it - see the "shared KO-reaction pipeline"
  status update), not just combat damage.
- **Sacrifice** (Spidey's Last Stand; The Rock is cataloged but left
  vanilla) - moves a Character die from the Field Zone to Out of Play
  (owner's turn) or the Used Pile (otherwise), never counting as a KO.
- **Strike** (Bizarro) - the sole Character die you field this turn
  gets +2A/+2D and Overcrush for the rest of the turn.
- **Swarm** (Parademon) - while active, drawing another copy of that
  card during Clear and Draw pulls an extra die.
- **Tag Out** (Big E) - after blockers are declared, you may Prep a die
  sitting in the Field Zone to give a target Character die +2A/+2D
  until end of turn.
- **Teamwatch** (Falcon; Black Panther shares his real "Avengers"
  affiliation) - fielding a different, affiliation-matching Character
  die while a Teamwatch die is active triggers its ability.

Not Appendix 1 keywords, but bespoke card text built alongside this
work using the same infrastructure: Cosmic Cube's mid-Clear-and-Draw
redraw and Rip Hunter's "while active" Clear and Draw reaction (see
their own status updates) - both also answered through `GameState.
PendingChoice` for the same reason Corrupt is.

Found but deliberately not built: **Heist** (a real Basic Action card -
"target opponent draws 2 dice from their bag, place one in their Prep
Area, roll the other and place it in your Reserve Pool, at end of turn
place it in your opponent's Used Pile"). Same draw-then-choose shape as
Corrupt/RedrawFromBag (would reuse `PendingChoice`), but also moves a
die into an *opponent's* Reserve Pool under your control - cross-player
die placement/control isn't modeled anywhere in this engine yet (see
the "Not yet designed" note on `Controlling`/`Copying`/`Swapping` up
top), so this is real, separate, additional work, not a drop-in use of
what Corrupt/RedrawFromBag already built. Not authored in
`SampleCards.cs`.

Also done, out of order (the user asked for it explicitly once the above
was clear): a visual pass matching the physical mat's zone layout and
simple die-face icons - see the latest status update. Big Barda's
placeholder stats (and any other data cleanup like it) are still
deliberately deferred to a later batch, per the user's own call.

The chronological "Status update" sections below explain the reasoning
and bugs found behind each already-built piece - worth reading before
touching that area, since several of them (the legal-target system
especially) encode a real correctness fix, not just a design choice.

---

Scope: server-side rules engine only, no UI. Goal of this pass: settle on a
data model and architecture that can scale to the full card pool (thousands
of cards, each with bespoke ability text) without rewriting the core loop
every time a new keyword or card template shows up.

## Source material reviewed
- `Dice Masters Comprehensive Rules (4.11.2023).pdf` — full turn structure,
  zone rules, ability timing/queue model, keyword index (partial — index is
  long, sampled through "Vengeance").
- `Teambuilder/cards.php` — ~thousands of cards encoded as pipe-delimited
  strings per set array (e.g. `msw`, `skc`, `dps`...). Format observed:
  `"<rarity><cost><energy/hex><attack>Name|Subtitle|ability line 1|ability line 2..."`
  for characters, and `"<rarity><cost>0<max>Name|type|effect lines"` for
  Basic Actions. Icon dictionaries (`iconname`/`iconid`) map affiliation and
  energy shorthand codes to display names. This is a display-oriented format,
  not something to build the engine's data model around — treat it as a raw
  import source, not the schema.

## Why this is a hard extensibility problem
Two very different kinds of "ability" exist in the rules:
1. **Keyword abilities** (Appendix 1) — a bounded set (~50-60) of reusable,
   well-specified mechanics: Overcrush, Regenerate, Range X, Fabricate X-Y,
   Deadly, etc. Finite, engine can implement these once as first-class
   plugins.
2. **Bespoke card text** — free-form English on each card's text box
   ("When fielded, deal 3 damage to target character die and reroll target
   character die."). Thousands of unique combinations. Cannot be hand-coded
   one `if` branch at a time without an unmaintainable core loop.

The design below treats (1) as engine-native systems and (2) as **data**
interpreted by a small effect runtime, so adding new cards is authoring data,
not touching engine code.

## Core entities

```
Player { id, life, bag[], usedPile[], outOfPlay[] }

Zone = Bag | PrepArea | ReservePool | FieldZone | AttackZone | UsedPile | OutOfPlay

CardDef {
  id, name, subtitle,
  type: Character | Action | BasicAction | EpicBasicAction,
  purchaseCost, energyTypes: EnergyType[],   // fist/bolt/mask/shield/none
  affiliations: string[], alignment?: Good|Neutral|Evil,
  dieLimit, useCount (Basic Actions only, always 3),
  levels: [{ fieldingCost, attack, defense, burst?: 1|2 }],  // characters
  rawText: string,              // verbatim card text, kept for display/audit
  keywords: [{ name: KeywordId, params?: number[] }],
  abilities: AbilityDef[]       // structured, hand-authored (see below)
}

DieInstance {
  id, cardId, ownerId, controllerId,
  zone, face,                   // face encodes level (char) or energy/action
  damage, level,
  appliedModifiers: Modifier[], // until-end-of-turn, lost if die leaves Field
  attachedGear: DieInstance[]   // Equip keyword
}
```

`CardDef` intentionally separates `rawText` (always kept, verbatim, for
tooltips/audit/debugging) from `abilities` (the structured, engine-executable
representation). Cards without an authored `abilities` entry still exist,
display correctly, and can be purchased/fielded — they just no-op on their
text box until implemented. This lets card data import (from something like
the Teambuilder dataset) happen immediately, decoupled from ability
authoring, which can proceed incrementally card-by-card or template-by-
template.

## Ability representation (the extensibility core)

An `AbilityDef` is not code, it's a small tree of primitives:

```
AbilityDef {
  trigger: WhenFielded | WhenAttacks | WhenBlocked | WhileActive |
           WhenDamaged | WhenKOd | Global | Burst1 | Burst2 | ...
  cost?: EffectNode[]      // e.g. KO a die, spin down, lose life
  targeting?: TargetSpec   // "target opposing character die", "each of your..."
  effects: EffectNode[]    // ordered list, matches rule 3.1.7 sequential resolution
}

EffectNode =
  DealDamage(amount, target) | KO(target) | Move(die, fromZone, toZone) |
  ModifyStat(target, {attack?, defense?}, duration) | Reroll(target) |
  Spin(target, delta) | Draw(count) | Prep(source) | Field(target, free?) |
  GainLife(amount) | LoseLife(amount) | ApplyKeyword(target, keyword, duration) |
  Conditional(check, then, else) | Choice(options)
```

This is effectively a tiny interpreter (~20-30 primitive ops covers the vast
majority of observed card text patterns from the rulebook's own examples).
New card text = compose primitives into a tree; the engine never needs a
code change for a new card. Keyword abilities (Overcrush, Range, Fabricate,
etc.) are implemented as **engine plugins** that hook the same trigger points
rather than being expressed as effect trees themselves — they're too
structural (e.g. Range's simultaneous dual-resolution, Overcrush's damage
math) to be worth expressing as generic primitives.

## Turn engine

Mirrors rule Section 2 directly — this part of the rulebook is essentially
already a state machine spec:

```
Steps: ClearAndDraw → RollAndReroll → Main → Attack → CleanUp
Attack sub-steps: DeclareAttackers → DeclareBlockers →
                  ActionAndGlobalWindow → AssignCombatDamage →
                  WhenDamagedAbilities → ResolveDamageAndWhenKOd
```

Each step is a function over `GameState` that (a) performs the mandatory
zone moves the rules specify, (b) opens the appropriate trigger window, and
(c) drains the ability queue before advancing. Steps are one-way (2.2.4,
2.7.0.3) — no backtracking, matches a simple linear state machine, no need
for undo/rollback support beyond the queue itself.

## Ability queue (Section 3.2)

The rulebook literally specifies a FIFO queue with interrupt semantics — this
maps directly to an implementation:

- `queue: QueuedAbility[]`
- Abilities append to the end when triggered (order-of-entry = active player
  first, in their chosen order, then inactive player).
- Only Prevent/Redirect-type effects may interrupt (jump ahead of) the queue;
  everything else resolves strictly FIFO.
- Persistent abilities (3.4.4) register a standing listener instead of
  resolving once; those listeners get checked against the *game state at
  queue-entry time*, not resolution time (rule 3.2.5) — so each queued
  ability needs to snapshot the relevant game-state facts it depends on at
  entry, not re-query live state at resolution.
- Clean Up step resolves in two explicit passes: all Applied abilities
  (active player's then inactive player's), then all Persistent abilities,
  same ordering (rule 3.2.10) — worth encoding as an explicit two-pass drain
  rather than relying on natural queue order, since Applied and Persistent
  abilities can be interleaved when they're queued.

## Data pipeline (import vs. authoring)

1. **Extraction**: parse `Teambuilder/cards.php`'s pipe-delimited arrays into
   canonical `CardDef` JSON (name, subtitle, cost, energy/affiliation via the
   `iconname`/`iconid` maps, levels, rarity, dieLimit, rawText). Mechanical,
   no rules knowledge needed — gets the full card pool into the new schema
   with `abilities: []` (unimplemented) on every card.
2. **Authoring**: separately, hand-write `abilities` trees for cards,
   prioritized by: (a) cards with only keyword abilities (near-free, keyword
   plugin already exists), (b) common templates (e.g. "when fielded, deal N
   damage to target character die" recurs constantly — worth a small
   template-matching helper to bulk-generate these), (c) everything else,
   long tail, one at a time.
3. Keep extraction and authoring as separate passes/files so re-running the
   extractor (e.g. a new set gets added to Teambuilder) never clobbers
   hand-authored ability trees.

## Testing strategy
The rulebook itself contains ~25 fully worked numeric examples (the Queue
example in 3.2.2, the Range example, the Overcrush example, etc.) — these
should become the first engine test suite, since they're pre-validated
against the real rules and cover the trickiest timing/interrupt edge cases.

## Open questions for next session
- Effect-tree primitive list is a first pass — will need extending once
  authoring starts hitting patterns it can't express (expect this early,
  keyword-adjacent stuff like Heroic pairing or Fusion will need their own
  plugins similar to keywords rather than generic primitives).
- Not yet designed: legal-target computation (rule 3.3) as a reusable
  query layer — every `TargetSpec` needs to filter by zone/attribute/"legal
  target" rules (3.1.9's Legal Target definition) — this wants to be a
  shared predicate system, not duplicated per ability.
- Not yet designed: how `Controlling`/`Copying`/`Swapping` (3.9-3.11) attach
  to the DieInstance model — these mutate "which CardDef does this die
  reference" at runtime and need their own indirection (e.g. `virtualCardId`
  override) rather than being bolted onto `appliedModifiers`.

## Design log

The full chronological history of every session's work — what was
built, why, what broke, and how it got fixed — lives in
[`DESIGN_LOG.md`](DESIGN_LOG.md), split out separately so this doc stays
a quick orientation read. Each keyword's entry in "Implemented keywords"
above, and each next-steps item, points at a specific log entry by name
(e.g. "see the Call Out status update") — search `DESIGN_LOG.md` for
that heading text to find it.
