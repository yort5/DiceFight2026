using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// Rule 2.6.5.4 - a Global ability's energy price, e.g. "Pay 1 Mask".
// RequiredType null means any energy (including generic) satisfies it -
// none of the currently-scripted Globals need that case, but Basic
// Action cards themselves have no energy type (rule 1.2.4), so a Global
// printed on one would need this.
public sealed record EnergyCost(int Amount, EnergyType? RequiredType = null);

// A single authored ability on a card: when it fires, what it costs
// (rule 3.1.16 - Ability Cost, distinct from energy cost), and what it does.
// Cost and Effects are both effect trees so a cost like "KO one of your
// dice" reuses the same primitives as an effect. EnergyCost is only
// meaningful for Trigger == Global (rule 2.6.5.4); Non-global abilities
// don't have an energy price of their own.
public sealed record AbilityDef(
    TriggerType Trigger,
    IReadOnlyList<EffectNode>? Cost,
    EffectNode Effect,
    EnergyCost? EnergyCost = null);
