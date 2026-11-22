namespace DiceFight.V2.Model;

// v3 "Dice Kingdom" addition (2026-09-03). A Champion is NOT a card: it
// has no DieDefinition (CardDef.Die is non-nullable - there is genuinely
// no way to express a die-less source through CardDef), is never fielded,
// never purchased, never costs anything. It is a single passive that is
// simply always true for whichever player picked it, for the whole game.
//
// Every continuous effect in the closed vocabulary (StatAura,
// CostModifier, ...) is compiled by ContinuousRegistry and gated through
// ActiveSourceDice - a real DieInstance of some card, fielded/attacking.
// A Champion has no such die, so it is registered separately by
// ChampionRegistry directly into GameState's existing modifier lists,
// with AppliesTo checking the die/payer's OWNER instead of any source
// die's activity. QueryEngine's stat/cost queries need no changes at all
// - they already sum over those same lists.
//
// Deliberately a closed enum of four passive kinds, not a general
// mini-DSL - "dumb flat delta," the exact philosophy IDieStatModifier's
// own doc comment states for every other modifier in this engine. Extend
// only when a real fifth Champion needs a shape these four don't cover.
public enum ChampionPassiveKind
{
    AttackBuff,
    DefenseBuff,
    FieldingCostDiscount,
    PurchaseCostDiscount,
    // Once per turn, reroll one die in your Reserve Pool (Great Horned
    // Owl, 2026-09-27 - replaced its purchase discount, which played as
    // too strong). An action the player takes (TurnEngine.UseForesight),
    // not a modifier, so ChampionRegistry registers nothing for it.
    Foresight,

    // Once-per-turn powers (2026-10-04, user's redesign: the always-on
    // anthems were wildly uneven - Wolf's +1 ATK to everything was worth
    // ~30 points of win rate, Owl's Foresight -16). Each is one decision a
    // turn with a bounded effect (ChampionPowers).
    // Wolf: once per your turn, one of your creatures gets +Amount ATK.
    PumpOneAttack,
    // Armadillo: once per turn, after blocks, prevent all combat damage to
    // one of your creatures in combat this turn.
    ShieldOneFromCombat,
    // Golden Eagle: once per your turn, field one creature without paying
    // its fielding cost.
    FieldOneFree,
    // Great Horned Owl: once per your turn, spin one of its own creatures
    // up a level (it spun an opponent's down too until 2026-11-22).
    SpinOne,
}

public sealed record ChampionDef(
    string Id,
    string Name,
    string EnergySymbolId,
    ChampionPassiveKind PassiveKind,
    int Amount)
{
    // Wolf's pump only in the Main Step - before attackers are declared, so
    // the opponent can block around the pumped creature - not after blocks
    // (2026-10-04 what-if: after blocks it's 3 guaranteed face damage).
    public bool PowerBeforeBlocksOnly { get; init; }

    // A second, genuinely new thing GameConfig.BasicDicePool couldn't
    // express: that list is ONE shared pool seeded identically for BOTH
    // players (GameSetup.SeedBasicDicePool's own loop), which is exactly
    // right for classic Dice Masters' uniform Sidekick dice but wrong for
    // v3, where each player's basic dice (Tardigrades) must match THEIR
    // OWN Champion's energy type, not a pool shared across both sides.
    // Empty by default (same opt-in pattern as GameConfig.Steps/Champions)
    // so a config that declares Champions without per-Champion pools, or a
    // player with no ChampionId at all, falls back to
    // GameConfig.BasicDicePool unchanged - see GameSetup.SeedBasicDicePool.
    public IReadOnlyList<BasicDicePoolEntry> TardigradePool { get; init; } = [];
}
