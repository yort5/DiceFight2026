namespace DiceFight.V2.Model;

// Appendix B (V2_PLAN.md) / V2_VOCABULARY_HISTORY.md Part 1. A GameConfig is a
// whole game DEFINITION - the classic Dice Masters ruleset is one config
// among possibly-many (see Direction C, ARCHITECTURE_REVIEW.md Part 3);
// nothing in the engine should hardcode energy symbols, keyword ids, dice
// composition, or turn constants outside this record.
public sealed record GameConfig(
    string Id,
    string Name,
    IReadOnlyList<SymbolDef> EnergySymbols,
    IReadOnlyList<KeywordDef> Keywords,
    RulesConfig Rules,
    IReadOnlyList<BasicDicePoolEntry> BasicDicePool,
    int BasicActionSlots)
{
    // Spike C (V2_VOCABULARY_HISTORY.md Part 13) - the game's ordered step list.
    // Data, not an enum, so a Direction-C variant reorders, removes, or
    // inserts steps with zero engine change. Defaults to the standard
    // rulebook order; a config only needs to set this if it differs.
    public IReadOnlyList<TurnStepDef> Steps { get; init; } = TurnStepDefs.Standard;

    // v3 "Instinct Clash" addition (2026-09-03) - a Champion is a single
    // passive, always-on, no-die, no-cost team effect a player picks at
    // team setup (physical Dice Masters has no such concept, hence empty
    // default here). ChampionRegistry reads this directly, not
    // ContinuousRegistry, since a Champion has no source die to gate an
    // ActiveSourceDice scan on - see ChampionDef's own remarks.
    public IReadOnlyList<ChampionDef> Champions { get; init; } = [];
}

// An energy symbol - "no enum anywhere" (Appendix B). Both flags are
// properties OF the symbol rather than special-cased types, so a variant
// game can declare its own.
//
// Rule 1.4.3 defines the two non-type symbols, and they are opposites:
//
//   IsWild    - "you may consider this to represent any of the four
//               energy types". Satisfies ONE type requirement, chosen
//               when spent. A wildcard is one energy, not a skeleton key.
//   IsGeneric - "can be spent on purchasing/fielding/abilities but is NOT
//               considered to be any type of energy". Pays toward the
//               amount and never toward a type requirement.
public sealed record SymbolDef(string Id, bool IsWild = false, bool IsGeneric = false);

// A declared keyword. The engine knows a keyword's BEHAVIOR by its Id
// (Phase 7 - "keyword behavior is engine code keyed to keyword ids
// declared in GameConfig... a game config can only use declared
// keywords"); this record is just the declaration, not the behavior.
public sealed record KeywordDef(string Id, string? Description = null);

// One entry in the basic (Sidekick-equivalent) dice pool - a DieDefinition
// plus how many of it are in the shared pool. This is exactly where
// Direction C's "8 identical Sidekick dice -> two 4-die sets" becomes
// pure data: a config just lists more than one distinct DieDefinition here.
public sealed record BasicDicePoolEntry(DieDefinition Die, int Count);
