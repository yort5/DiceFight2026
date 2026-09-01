using DiceFight.V2.Model;

namespace DiceFight.V2.Data;

// V2_PLAN.md Phase 8 task 1 - the current physical game, expressed as one
// GameConfig. This file is the proof of Direction-C readiness
// (ARCHITECTURE_REVIEW.md Part 3): every constant here is DATA, so a
// variant ruleset is a different GameConfig value, never an engine-code
// change - see GameConfigTests for the variant-config test this task asks
// for.
//
// Sourced from v1's own real-game constants (not re-derived or guessed):
// energy symbols (Model/Enums.cs's EnergyType - Fist/Bolt/Mask/Shield,
// plus Wild for Sidekick faces), the Sidekick die's six real faces
// (DESIGN_LOG.md's "corrected Sidekick die faces" entry - one Level 1
// character face at 1A/1D/0-cost [DieStats.SidekickFace], and five
// distinct energy faces, Wild/Fist/Bolt/Mask/Shield, not five copies of
// one "always Wild" face), and the keyword id list actually printed on
// v1's migrated cards (SampleCards.cs's own KeywordInstance uses).
public static class DiceFightClassicConfig
{
    private static readonly Face SidekickCharacterFace =
        new([], new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1));

    private static Face EnergyFace(string symbolId) => new([new SymbolAmount(symbolId, 1)]);

    public static readonly DieDefinition SidekickDie = new("Sidekick",
    [
        SidekickCharacterFace,
        EnergyFace("Wild"),
        EnergyFace("Fist"),
        EnergyFace("Bolt"),
        EnergyFace("Mask"),
        EnergyFace("Shield"),
    ]);

    // The keyword ids actually printed on migrated cards so far (grown as
    // Phase 8's migration adds more) - "a game config can only use
    // declared keywords" (Phase 7 note). Declaring an id here is NOT a
    // claim its behavior is engine-coded; only Overcrush and Fast are
    // (CombatEngine, Phase 7) - everything else here is currently
    // decorative (RawText/UI-facing) until a later phase builds its
    // behavior, same as v1's own keyword rollout was incremental.
    public static readonly IReadOnlyList<KeywordDef> Keywords =
    [
        new("Ally"), new("Amplify"), new("Attune"), new("Awaken"), new("Call Out"),
        new("Continuous"), new("Corrupt"), new("Deadly"), new("Energize"), new("Energy Drain"),
        new("Fast"), new("Founder"), new("Infiltrate"), new("Intimidate"), new("Obscure"),
        new("Overcrush"), new("Range"), new("Regenerate"), new("Retaliation"), new("Sacrifice"),
        new("Strike"), new("Swarm"), new("Tag Out"), new("Teamwatch"),
    ];

    public static readonly GameConfig Config = new(
        Id: "dicefight-classic",
        Name: "DiceFight Classic",
        EnergySymbols:
        [
            new SymbolDef("Fist"), new SymbolDef("Bolt"), new SymbolDef("Mask"), new SymbolDef("Shield"),
            new SymbolDef("Wild", IsWild: true),
            // Rule 1.3.10 / 1.4.3 - Basic Action dice provide generic
            // energy, and a Crossover's single face is generic too. It
            // was previously unrepresented, which meant a Basic Action
            // die on an energy face provided NOTHING.
            new SymbolDef("Generic", IsGeneric: true),
        ],
        Keywords: Keywords,
        Rules: new RulesConfig(
            StartingLife: 20,
            DrawCount: 4,
            MaxTeamCards: 10, // 8 Character cards + up to 2 Basic Action cards
            MaxTeamDice: 20,
            BasicActionCount: 2),
        // Rule 2.1.1/2.1.8 - each player starts with 8 Sidekick dice.
        BasicDicePool: [new BasicDicePoolEntry(SidekickDie, Count: 8)],
        BasicActionSlots: 2);
}
