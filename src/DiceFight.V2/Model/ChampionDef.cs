namespace DiceFight.V2.Model;

// v3 "Instinct Clash" addition (2026-09-03). A Champion is NOT a card: it
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
}

public sealed record ChampionDef(
    string Id,
    string Name,
    string EnergySymbolId,
    ChampionPassiveKind PassiveKind,
    int Amount)
{
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
