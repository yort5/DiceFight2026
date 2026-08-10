using DiceFight.Engine;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;

namespace DiceFight.Engine.Effects;

// Everything EffectInterpreter needs to execute one ability. ResolveTargets
// is the seam standing in for the not-yet-built "legal target" query system
// (see RULES_ENGINE_DESIGN.md open questions) - callers (tests, and
// eventually a real target-selection UI/AI) decide what a TargetSpec means
// and hand back concrete die ids; the interpreter only applies effects to
// whatever ids it's given. Random is the bare RNG DrawDice picks a bag die
// with; Roller is a full IDiceRoller, needed separately so an ability-driven
// KO (Ko node, or DealDamage KO'ing its target) can respect Regenerate -
// null in the handful of test call sites that don't care about it. Queue is
// needed by DrawDice's real roll so a die that lands Energize-eligible can
// enqueue that trigger too (rolls outside Roll and Reroll check Energize
// immediately, unlike the Roll and Reroll step itself - see TurnEngine.
// CheckEnergize's remarks) - also null wherever nothing rolled needs it.
// Trigger is the currently-resolving ability's own TriggerType - only
// consulted by LegalTargets.Query so far, to enforce Gladiator's own
// "your character dice can't be the target of Action Dice or Global
// Abilities" (GameState.ImmuneToActionAndGlobalTargetingControllerIds) -
// null in every call site that doesn't need that distinction (most of
// them), same "optional, most callers don't care" shape as Roller/Queue.
public sealed record EffectContext(
    GameState State,
    string ControllerId,
    string? SourceDieId,
    Func<TargetSpec, IReadOnlyList<string>> ResolveTargets,
    Random? Random = null,
    IDiceRoller? Roller = null,
    AbilityQueue? Queue = null,
    TriggerType? Trigger = null);
