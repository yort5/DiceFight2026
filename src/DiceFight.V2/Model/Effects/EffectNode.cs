using DiceFight.V2.Model;

namespace DiceFight.V2.Model.Effects;

// The 18 effect templates (V2_VOCABULARY.md Part 1, frozen 2026-08-22).
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

// Landing in ReservePool means rolled; landing in PrepArea/Bag/UsedPile
// does not (v1's DrawDice/PrepFromBag/Corrupt convention - the
// destination zone alone decides, no separate "rolled" flag).
public sealed record DrawToZone(int Count, Zone ToZone, Zone FromZone = Zone.Bag) : EffectNode;

public sealed record FieldDie(TargetFilter Target, bool Free, int Level = 1) : EffectNode;

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
public sealed record ModifyStat(
    TargetFilter Target,
    int? AtkDelta = null,
    int? DefDelta = null,
    int? SetAttack = null,
    int? SetDefense = null,
    Duration Duration = Duration.EndOfTurn) : EffectNode;

public sealed record GrantTag(TargetFilter Target, IReadOnlyList<string> Tags, Duration Duration = Duration.EndOfTurn) : EffectNode;

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
}
public sealed record CombatFlag(TargetFilter Target, CombatFlagKind Flag) : EffectNode;

public sealed record Sequence(IReadOnlyList<EffectNode> Steps) : EffectNode;

// Ground rule 8: EVERY "you may [X]" is modeled this way, cost-attached
// or not - Cost: null is a genuine free yes/no choice (Shocking Grasp),
// never collapsed to "always happens." AnsweredBy (Finding 14) routes
// the offer to the opponent instead of the ability's own controller
// (Black Widow "Tsarina").
public sealed record MayPay(EffectNode? Cost, EffectNode Then, TargetOwnership AnsweredBy = TargetOwnership.Own) : EffectNode;

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
