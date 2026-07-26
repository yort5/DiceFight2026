using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// A single authored ability on a card: when it fires, what it costs
// (rule 3.1.16 - Ability Cost, distinct from energy cost), and what it does.
// Cost and Effects are both effect trees so a cost like "KO one of your
// dice" reuses the same primitives as an effect.
public sealed record AbilityDef(
    TriggerType Trigger,
    IReadOnlyList<EffectNode>? Cost,
    EffectNode Effect);
