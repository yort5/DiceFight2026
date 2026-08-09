namespace DiceFight.Engine.Model;

// Rule 1.5 - Play Areas. AttackZone is a subset of FieldZone (1.5.6.1) but
// tracked separately here since attacker/blocker state needs to be queryable.
// Unpurchased isn't one of the rulebook's named play areas - it's engine
// bookkeeping for "sitting on its card, not yet bought" (rule 2.1.3's dice
// supply), which the rules assume as a physical fact about components
// rather than a play area a die moves through.
//
// DiceFromBag and DiceFromPrep aren't named play areas either - they're
// transient staging zones that only ever hold dice between a
// TurnEngine.ClearAndDraw call and the TurnEngine.RollAndReroll call that
// follows it, kept apart from each other and from PrepArea so that a card
// which Preps a die *during* Clear and Draw (e.g. Pepper Potts: "draw an
// extra die at the beginning of your Clear and Draw Step... Prep it")
// doesn't get swept into this turn's roll just because it landed in the
// same zone this turn's normal draw did - see TurnEngine.ClearAndDraw's
// remarks for the full sequencing.
public enum Zone
{
    Unpurchased,
    Bag,
    PrepArea,
    DiceFromBag,
    DiceFromPrep,
    ReservePool,
    FieldZone,
    AttackZone,
    UsedPile,
    OutOfPlay,

    // Keyword Intimidate - "remove target opposing Character die from the
    // Field Zone until end of turn" (real card text: "place it next to
    // your character cards"). Deliberately its own zone rather than
    // reusing Out of Play: unlike Out of Play (swept to the Used Pile
    // every Clean Up, rule 2.8.6), a die here returns to the Field Zone
    // instead, on its same face/level - and unlike Capture (rule 3.8,
    // which Appendix 1's own Intimidate entry explicitly distinguishes
    // itself from), there's no "stack it under the capturing die"
    // relationship to model, just a plain temporary removal. Being absent
    // from TargetSpec.DefaultZones already makes a die here untargetable
    // by anything else without any extra flag needed - same reasoning as
    // every other zone-gated "can this be targeted" question in this
    // engine (see DieStats.CountsAsSidekick's own remarks for the pattern).
    Intimidated
}

// Rule 1.4.2 / 1.4.3. Wild and Generic are resolved at spend-time, not
// stored as a die's fixed type - a die's face determines what it produces.
public enum EnergyType
{
    Fist,
    Bolt,
    Mask,
    Shield
}

// Rule 1.4.2/1.3.10 - what an energy-face die actually provides once
// rolled, needed for Purchase's per-type matching requirement (2.6.2.3).
// Wild (Sidekick energy faces) satisfies any specific-type requirement;
// Generic (Basic Action dice) contributes to the total but never
// satisfies a specific-type requirement.
public enum EnergyKind
{
    None,
    Specific,
    Wild,
    Generic
}

public enum Alignment
{
    Good,
    Neutral,
    Evil
}

// Rule 1.2.2 / 1.2.3 - Basic/Epic Basic Action are subsets of Action with
// extra constraints, not a different shape of card.
public enum CardType
{
    Character,
    Action,
    BasicAction,
    EpicBasicAction
}

// Rule 1.6.4/1.6.5 - what a rolled die's face represents. Unrolled dice
// (1.6.3) have no status; this only applies once a die is in a rolled zone.
public enum DieStatus
{
    Unrolled,
    Energy,
    Character,
    SidekickCharacter,
    Action
}

// Rule 3.4 - the trigger points abilities can hook. Named after the rule's
// own terms so AbilityDef.Trigger reads the same as the card text.
public enum TriggerType
{
    WhenFielded,
    WhileActive,
    WhenAttacks,
    WhenBlocks,
    WhenBlocked,
    WhenEngaged,
    WhenDamaged,
    WhenKOd,

    // Rule 2.6.4.1 - a non-Continuous Action die's ability, triggered when
    // the die is used. Not yet wired into TurnEngine's Main Step (no
    // "use an Action die" mechanic exists yet); currently exercised only
    // via direct EffectInterpreter calls in tests.
    WhenUsed,

    // "When you draw this die from your bag" (e.g. Cosmic Cube's
    // "Infinite Possibilities" printing) - fires once per matching die,
    // during TurnEngine.ClearAndDraw's own draw, for each die actually
    // drawn this turn. Distinct from a "draw a die" *effect* elsewhere in
    // the DSL (DrawDice, Corrupt, RedrawFromBag) - this is the reactive
    // trigger point on the *receiving end* of Clear and Draw's own draw,
    // not something that draws anything itself.
    WhenDrawn,

    Global,

    // NOTE: burst-symbol-conditional bonuses ("*"/"**" in ability text)
    // are NOT a separate trigger point, despite an early design sketch
    // once assuming they would be (a since-removed, never-wired "Burst1"/
    // "Burst2" pair here) - once real cards were actually scoped, every
    // one of them turned out to modify an EXISTING trigger's effect
    // (usually WhenFielded), not fire independently. See Effects.
    // EffectCondition.OnSingleBurstFace/OnDoubleBurstFace instead.

    // Keyword Energize - fires once, after Roll and Reroll completes, for
    // any Energize-keyword die that ended up on a double-energy face. Not
    // checked against the initial roll - a die rerolled off double energy
    // never triggers it, but a die left alone on a double-energy face does,
    // once the reroll window closes.
    Energize,

    // Keyword Awaken - fires once per die, every time an Awaken-keyword
    // die spins up 1 or more levels, regardless of what caused the spin
    // (Amplify, an ability's own Spin effect, etc. - see DieStats.
    // SpinLevelAndCheckAwaken). "You get to use the ability for each
    // individual die that spins up" - two dice spinning up in the same
    // event enqueue two separate instances.
    Awaken,

    // Keyword Attune - "While a Character die you control with Attune is
    // active, when you use an Action die, that character deals 1 damage
    // to target player or Character die (no matter how many of that
    // Character's dice are active)." Fires once per active Attune die
    // (see TurnEngine.UseActionDie), each instance independently
    // targeted - the built-in 1-damage effect is injected by the engine
    // itself (identical across every Attune card, so not authored per
    // CardDef), while this same trigger also carries any card-specific
    // follow-up text some printings add (e.g. Wasp: "When you use
    // Attune, Wasp gets +1A and +1D until end of turn").
    Attune,

    // Keyword Infiltrate - "each time one of your character dice uses
    // Infiltrate" (e.g. Ricochet's "Slinger" printing). Not the
    // infiltrating die's own ability - a reactive "while active" check
    // against every one of the controller's active dice, fired once per
    // successful Infiltrate use (see CombatEngine.ResolveInfiltrate),
    // the same shape as Attune reacting to "you use an Action die."
    WhenInfiltrates,

    // Keyword Retaliation - "If a character you control with Retaliation
    // is active, and a Character die you control that shares an
    // affiliation with it is KO'd, deal 1 damage to an opposing player."
    // Unlike Attune/Infiltrate's reactors (whose built-in effect is a
    // fixed constant, identical on every card), Retaliation's own printed
    // amount can be redefined per card (e.g. Black Manta's "for each of
    // your active Villains"), so - unlike those two - there's no engine-
    // injected default: every Retaliation card carries its own AbilityDef
    // with this Trigger (see CombatEngine.ResolveRetaliation for the
    // reactive scan that fires it, checked AFTER a whole simultaneous KO
    // batch settles per Appendix 1 clarification 1).
    Retaliation,

    // Keyword Teamwatch - "When a character with Teamwatch is active and
    // you field a different Character die with the same affiliation, use
    // their Teamwatch ability." A reactive scan against the active
    // player's own Teamwatch holders, fired from TurnEngine.Field, same
    // "no stacking with multiple copies of the same character" dedup
    // shape as Retaliation (Appendix 1 clarification 1). No engine-
    // injected default effect, same reasoning as Retaliation - Teamwatch
    // cards define their own effect text (e.g. Falcon: "Prep a Sidekick
    // from your Used Pile"), there's nothing generic to inject.
    Teamwatch,

    // Bespoke text like Rip Hunter's "Navigate the Sands of Time"
    // printing - "While Rip Hunter is active, once during your Clear and
    // Draw Step, when you draw dice from your bag you may send any
    // number of them to the Used Pile and draw one new die for each of
    // them." Not itself an Appendix 1 keyword, so there's no HasKeyword
    // gate - TurnEngine.ClearAndDraw just fires this once per unique
    // active card (same "does not stack" dedup as Teamwatch/Retaliation,
    // rule 3.4.5.3), letting EnqueueTriggered's own no-op-if-no-matching-
    // ability behavior sort out who actually has one. "Once during your
    // Clear and Draw Step" needs no separate tracked limiter, unlike
    // Global's OncePerTurn - ClearAndDraw itself only runs once per turn,
    // so firing this once per call already satisfies it for free. Named
    // distinctly from (and unrelated in meaning to) TurnStep.ClearAndDraw -
    // this is the reactive trigger fired *during* that step, not the step
    // itself.
    ClearAndDraw,

    // Keyword Continuous - "Action dice with Continuous are 'used' by
    // moving them to the Field Zone. They can stay there past the end of
    // the turn... allow you to send them to the Used Pile whenever you
    // could use a Global ability" (Appendix 1). Rule 2.6.4.2/2.6.4.3 draw
    // a hard line between the die's two lifecycle moments: moving
    // Reserve Pool -> Field Zone is "using" it (so Amplify/Attune/Obscure
    // still react, same as any other Action die use - see
    // TurnEngine.UseActionDie), but that move does NOT run the die's own
    // authored ability - only actually removing it later does, and that
    // removal is explicitly NOT a second "use" (no WhenUsed re-fire, no
    // second Amplify/Attune/Obscure). So this needs its own trigger,
    // fired only by TurnEngine.ResolveContinuousDie, never by
    // UseActionDie.
    ContinuousResolve,

    // "While active, at the end of each of your turns, [effect]" (e.g.
    // Jean Grey, DPS035) - fired once per the active player's own active
    // Character die at Clean Up (TurnEngine.CleanUp), before turn-scoped
    // flags reset, same "active right now" simplification Experience's
    // own Clean Up check already uses. Deliberately a plain trigger, not
    // gated on any keyword (unlike Experience) - Jean Grey's own "if no
    // character dice were KO'd" condition lives in her Effect tree
    // (Conditional + EffectCondition.NoCharacterKOdThisTurn), not the
    // trigger itself, so a future card with the same "end of your turn,
    // while active" shape but a different condition (or none) can reuse
    // this same hook.
    EndOfYourTurn,

    // "When [a die matching some filter] is KO'd, [my card] reacts" -
    // Magneto/Supreme Intelligence/Madelyne Pryor (DPS041/053/079), each
    // reacting to a DIFFERENT other die's KO, not their own (that's
    // plain WhenKOd). The filter is data (AbilityDef.KOdFilter, a
    // KOdDieMatch), not engine code, unlike Retaliation/Teamwatch's own
    // hardcoded scan shapes - see TurnEngine.ResolveKOReactions/
    // ResolveWhenAnotherDieKOd for the reactive scan this needs, fired
    // from the same shared choke point every real KO now funnels
    // through, alongside WhenKOd and Retaliation.
    WhenAnotherDieKOd
}

// Rule 2.2.3 - the five steps of a turn, in fixed order (2.2.4 - cannot
// return to a completed step).
public enum TurnStep
{
    ClearAndDraw,
    RollAndReroll,
    Main,
    Attack,
    CleanUp
}

// Rule 2.7.0.1 - the six sub-steps of the Attack Step, stricter and
// one-way (2.7.0.3) even relative to the outer TurnStep sequence.
// NotInAttack/Done are engine bookkeeping states, not rulebook sub-steps.
// RangeWindow/InfiltrateWindow/TagOutWindow aren't among the rulebook's
// six either. RangeWindow sits right after DeclareAttackers ("when one
// or more Character dice with Range attack" - Appendix 1 - triggers the
// instant attackers are declared, before blockers even exist yet).
// InfiltrateWindow/TagOutWindow both carve out the exact same later
// timing for themselves ("immediately after blockers are declared...
// before Action dice or Global abilities may be used" - identical
// wording for each), sitting strictly between DeclareBlockers and
// ActionAndGlobalWindow. Each window is independently skippable and
// resolved by its own CombatEngine method (ResolveRange/
// ResolveInfiltrate/ResolveTagOut) so no keyword's presence changes
// anything about how the others resolve.
public enum AttackSubStep
{
    NotInAttack,
    DeclareAttackers,
    RangeWindow,
    DeclareBlockers,
    InfiltrateWindow,
    TagOutWindow,
    ActionAndGlobalWindow,
    AssignCombatDamage,
    WhenDamagedAbilities,
    ResolveDamageAndWhenKOd,
    Done
}
