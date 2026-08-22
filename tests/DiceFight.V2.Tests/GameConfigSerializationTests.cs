using System.Text.Json;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 1 task 2 - proves the data model round-trips through
// System.Text.Json, keeping the door open for JSON card data and a card
// editor later (the whole point of games-as-data). Scoped to GameConfig
// specifically, per the task - CardDef's own Abilities/Continuous fields
// are polymorphic (EffectNode/ContinuousDef subtypes) and need
// JsonDerivedType configuration before THEY can round-trip; that's
// deferred to whenever CardDef JSON loading is actually needed (Phase 8),
// not scope-crept into Phase 1's data-model-only goal.
public class GameConfigSerializationTests
{
    private static GameConfig BuildSampleConfig() => new(
        Id: "classic",
        Name: "Dice Masters Classic",
        EnergySymbols:
        [
            new SymbolDef("Fist"),
            new SymbolDef("Bolt"),
            new SymbolDef("Mask"),
            new SymbolDef("Shield"),
            new SymbolDef("Wild", IsWild: true),
        ],
        Keywords:
        [
            new KeywordDef("Overcrush", "Damage dealt in excess of blocker's D is dealt to opponent."),
            new KeywordDef("Deadly"),
        ],
        Rules: new RulesConfig(
            StartingLife: 20,
            DrawCount: 4,
            MaxTeamCards: 8,
            MaxTeamDice: 20,
            BasicActionCount: 2,
            FieldZoneCap: null),
        BasicDicePool:
        [
            new BasicDicePoolEntry(
                Die: new DieDefinition("Sidekick",
                [
                    new Face([new SymbolAmount("Wild", 1)]),
                    new Face([], Character: new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 1, Defense: 1)),
                ]),
                Count: 8),
        ],
        BasicActionSlots: 2);

    [Fact]
    public void GameConfig_RoundTrips_Through_Json_To_An_Equal_Value()
    {
        // Compared via re-serialization, not record Equals() - C# records
        // only generate structural equality for array-typed properties;
        // IReadOnlyList<T> (the type used throughout this model, since it
        // reads better as an API than arrays everywhere) falls back to
        // reference equality, which a deserialized copy never has. Two
        // JSON documents matching is the actually-meaningful "round-
        // tripped correctly" check for this test anyway.
        var original = BuildSampleConfig();

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<GameConfig>(json);
        Assert.NotNull(roundTripped);
        var reserialized = JsonSerializer.Serialize(roundTripped);

        Assert.Equal(json, reserialized);
    }
}
