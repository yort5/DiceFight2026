namespace DiceFight.V2.Model;

// Appendix B (V2_PLAN.md) / V2_VOCABULARY.md Part 1. A GameConfig is a
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
    // Spike C (V2_VOCABULARY.md Part 13) - the game's ordered step list.
    // Data, not an enum, so a Direction-C variant reorders, removes, or
    // inserts steps with zero engine change. Defaults to the standard
    // rulebook order; a config only needs to set this if it differs.
    public IReadOnlyList<TurnStepDef> Steps { get; init; } = TurnStepDefs.Standard;
}

// An energy symbol - "no enum anywhere" (Appendix B). Wild (Sidekick-style
// energy faces) satisfies any specific-type requirement; IsWild is a
// property of the symbol, not a special-cased type.
public sealed record SymbolDef(string Id, bool IsWild = false);

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
