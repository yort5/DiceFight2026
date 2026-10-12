using DiceFight.V2.Model;

namespace DiceFight.V2.Model.Effects;

// The 18 effect templates (V2_VOCABULARY_HISTORY.md Part 1, frozen 2026-08-22).
// This is the closed vocabulary itself - the whole point of the v2
// rewrite (ARCHITECTURE_REVIEW.md) is that this list does NOT grow per
// card the way v1's 49-node EffectNode.cs did. A card that doesn't fit
// one of these goes to V2_TAIL_POLICY.md, not a 19th record here
// (V2_PLAN.md ground rule 2).
public abstract record EffectNode;

// Finding 11 - Distribute resolves Amount as repeated 1-point choices
// instead of one lump application, when true (Cyclops "Xavier's Dream",
// Batgirl "Babs").
public sealed record DealDamage(Amount Amount, TargetFilter Target, bool Distribute = false) : EffectNode;

// TriggersKOAbilities: false is v1's Sacrifice shape - bypasses "when
// KO'd" reactions entirely, per Appendix 1's clarification that a
// Sacrifice is not a KO.
public sealed record Ko(TargetFilter Target, bool TriggersKOAbilities = true) : EffectNode;

public sealed record MoveDie(TargetFilter Target, Zone ToZone) : EffectNode;

// Keyword Sacrifice (2026-10-03): "Sacrificed Character dice are moved
// from the Field Zone to Out of Play or the Used Pile, as applicable" -
// Out of Play on its owner's turn (to the Used Pile at Clean Up), the Used
// Pile otherwise - and "will not trigger 'when KO'd' abilities". An
// ability COST (rule 2): use it as MayPay.Cost. BindAs on the target keeps
// its stats for the rest of the ability ("damage equal to its ATK").
public sealed record Sacrifice(TargetFilter Target) : EffectNode;

// Great Horned Owl's Champion power (2026-10-04): the chosen creature spins
// up a level if it's the chooser's own, down a level if it's an opponent's.
public sealed record SpinByOwner(TargetFilter Target, int Levels = 1) : EffectNode;

// "You may pay X energy. If you do, [Then]" (2026-10-03, Breath Weapon).
// One choice: the payer taps Reserve Pool energy dice to cover Amount, or
// picks none to decline. Paid like a Global (TurnEngine.PayAbilityEnergy -
// partial spend, Out of Play on your turn). Skipped outright when the pool
// can't cover it, so it never offers a payment that can't be made.
public sealed record MayPayEnergy(int Amount, EffectNode Then, string? Prompt = null) : EffectNode;

// Keyword Range X (2026-10-03). RangeShot picks this die's target and
// records it without dealing damage; ResolveRangeShots then deals every
// recorded shot at once - "All Range damage is dealt simultaneously; the
// Active player chooses their targets..., immediately followed by the
// Inactive player", and a Range die KO'd by the other side's Range still
// fires (CombatEngine.DeclareAttackers queues both).
public sealed record RangeShot(int Amount) : EffectNode;
public sealed record ResolveRangeShots : EffectNode;

// Landing in ReservePool means rolled; landing in PrepArea/Bag/UsedPile
// does not (v1's DrawDice/PrepFromBag/Corrupt convention - the
// destination zone alone decides, no separate "rolled" flag).
public sealed record DrawToZone(int Count, Zone ToZone, Zone FromZone = Zone.Bag) : EffectNode;

// A timed, live protection rule. Reuses the continuous protection definition,
// but does not require its granting card to have a die active afterward.
// A side-wide Target filter also covers creatures fielded after resolution.
public sealed record GrantTargetingProtection(TargetingProtection Protection,
    Duration Duration = Duration.EndOfTurn) : EffectNode;

// A fielded die always ends up AT some level - and normally that level
// is simply the one it rolled, since a die showing a character face
// already has one. `Level` is therefore an OVERRIDE, not the source of
// truth: null (the default) means "field it as it stands". It is named
// only when a card overrides the rolled level (Jubilee "Rebellious
// Nature" - "field this die for free at level 2"), and it is also the
// fallback for ability-driven fielding of a DORMANT die straight out of
// the Used Pile (Falcon's "field a Sidekick from your Used Pile"),
// which has no rolled face to take a level from - such a die comes in
// at its lowest character level.
//
// Corrected 2026-08-24 (user ruling): this defaulted to `int Level = 1`,
// which silently snapped a die that rolled its level-3 face down to
// level 1 on being fielded.
public sealed record FieldDie(TargetFilter Target, bool Free, int? Level = null) : EffectNode;

// Finding 8 - NonCharacterMoveTo/DamagePerMoved fold the v1
// RerollAndMoveUnlessCharacter per-die multi-target pattern (5 real
// users) into Reroll itself, since Sequence+Conditional cannot express
// a per-die branch across a multi-die reroll.
public sealed record Reroll(TargetFilter Target, Zone? NonCharacterMoveTo = null, int DamagePerMoved = 0) : EffectNode;

// LevelDelta and SetLevel are mutually exclusive (Finding 12) - exactly
// one should be set by an authored card. Not enforced here (Phase 1 is
// data-only); Phase 5's interpreter/authoring validation is where this
// gets checked.
public sealed record Spin(TargetFilter Target, int? LevelDelta = null, int? SetLevel = null) : EffectNode;

public sealed record SpinToEnergy(TargetFilter Target, int Amount = 1) : EffectNode;

// AtkDelta/DefDelta are the relative-change form; SetAttack/SetDefense
// (Finding 5) are the absolute-snapshot form, mutually exclusive with
// their matching delta field, mirroring Spin's LevelDelta/SetLevel axis.
// SetAttack/SetDefense are Amount-typed (widened from int? by Spike B) so
// they can take a live value - Archnemesis's "target character die has D
// equal to its A" is SetDefense: StatOf(target, Attack). A plain
// absolute is just Fixed(n).
public sealed record ModifyStat(
    TargetFilter Target,
    int? AtkDelta = null,
    int? DefDelta = null,
    Amount? SetAttack = null,
    Amount? SetDefense = null,
    Duration Duration = Duration.EndOfTurn,
    Amount? AttackBonus = null, Amount? DefenseBonus = null) : EffectNode;

public sealed record GrantTag(TargetFilter Target, IReadOnlyList<string> Tags, Duration Duration = Duration.EndOfTurn) : EffectNode;

// Grants a whole triggered ability to a die, rather than a tag
// (V2_VOCABULARY_HISTORY.md Parts 16, 20-21 - signed off 2026-09-01). GrantTag
// can hand a die a KEYWORD; nothing could hand it an ability until now,
// which is what Lantern Ring needs ("that die gains: deal 1 damage to
// target player for each matching energy in your Reserve Pool").
//
// The granted ability survives blanking - see GrantedAbility's remarks.
public sealed record GrantAbility(TargetFilter Target, TriggeredAbility Ability, Duration Duration = Duration.EndOfTurn) : EffectNode;

// Ignore a DIE's text (V2_VOCABULARY_HISTORY.md Parts 19-21). The default scope -
// Web Shooters, Loki "Powerful Magic". Other copies of the same card are
// untouched, which is why Wolverine "No More Distractions" has to print
// "for all copies of that die" to get the other one.
public sealed record BlankText(TargetFilter Target, Duration Duration = Duration.EndOfTurn) : EffectNode;

// Ignore a CARD's text for one player - every die of it, in play or not,
// and its Globals with them (rule 2.6.5.2). Scarlet Witch, both Shrieks,
// Prismatic Spray "Lesser Spell", Mister Sinister's side-wide half.
//
// `Target` resolves to DICE as every TargetFilter does; each resolved
// die's card is what gets suppressed, for that die's controller. That is
// what "ignore the text on target character die's character card" (
// Kryptonite) means, and it is also how a "choose an opposing card"
// effect lands once RememberCard has picked one.
//
// AllOpposing is the side-wide form: no target is resolved, and every
// card the opponent could have is suppressed. It is a separate mode
// rather than a very wide TargetFilter because the filter would only
// ever reach cards with a die already in play, and these cards
// explicitly cover the ones that are not.
public sealed record BlankCardText(
    TargetFilter? Target = null,
    bool AllOpposing = false,
    Duration Duration = Duration.EndOfTurn) : EffectNode;

// Amount is signed - positive Fixed values gain life, negative ones lose
// it (v1's separate GainLife/LoseLife collapsed into one shape).
public sealed record LifeChange(Amount Amount, TargetOwnership Whose = TargetOwnership.Own) : EffectNode;

public sealed record PurchaseModifier(int Delta, CardType? CardKind = null, Zone? GoesToZone = null) : EffectNode;

public enum CombatFlagKind
{
    MustBlock,
    CantBlock,
    MustAttack,
    CantAttack,
    OnlyBlocker,
    Unblockable, // Finding 14 - Falcon "Recon"
    // Armadillo's Champion power (2026-10-04): combat damage to this die is
    // prevented this turn (EffectInterpreter.MarkDamage). Ability damage
    // and Deadly still get through.
    PreventCombatDamage,
}
public sealed record CombatFlag(TargetFilter Target, CombatFlagKind Flag) : EffectNode;

public sealed record Sequence(IReadOnlyList<EffectNode> Steps) : EffectNode;

public sealed record EmitEvent(TriggerKind Kind, string SubjectBinding = "self") : EffectNode;

// Ground rule 8: EVERY "you may [X]" is modeled this way, cost-attached
// or not - Cost: null is a genuine free yes/no choice (Shocking Grasp),
// never collapsed to "always happens." AnsweredBy (Finding 14) routes
// the offer to the opponent instead of the ability's own controller
// (Black Widow "Tsarina").
// Prompt (2026-10-03) says what the yes/no is FOR - without it the
// offer reads a bare "You may.", which says nothing about Infiltrate's
// "return to the Field and deal 1 damage" when it pops up mid-combat.
public sealed record MayPay(EffectNode? Cost, EffectNode Then, TargetOwnership AnsweredBy = TargetOwnership.Own, string? Prompt = null) : EffectNode;

public sealed record Conditional(Condition When, EffectNode Then, EffectNode? Else = null) : EffectNode;

// Finding 6 - the target player draws Count dice from their bag; the
// ability's controller chooses exactly one, which goes to ChosenToZone,
// the rest to RestToZone. Covers v1's Corrupt (PlayerTarget: Opposing,
// UsedPile/Bag) and DrawAndChooseOneToRoll (PlayerTarget: Own,
// ReservePool/Bag) as one template.
public sealed record DrawAndChooseOne(int Count, TargetOwnership PlayerTarget, Zone ChosenToZone, Zone RestToZone) : EffectNode;

// Finding 13 - counters live on the resolved target's own CARD (see
// GameState's counter store, V2_PLAN.md Phase 2 task 1), not the die.
public sealed record GrantCounter(TargetFilter Target, string CounterName, int Amount) : EffectNode;

// "Choose an opposing [character] card, replacing all previous choices."
// The memory Blob, Drax, Shriek "Sonic Beam" and Typhoid Mary all share -
// a chosen card that a continuous template then reads back.
//
// Keyed (player, source card, name) like GameState.Counters, which gives
// "replacing all previous choices" for free: a second Blob fielded by the
// same player overwrites the first player's choice rather than stacking,
// which is what the text says happens.
public sealed record RememberCard(TargetFilter Target, string MemoryName) : EffectNode;
