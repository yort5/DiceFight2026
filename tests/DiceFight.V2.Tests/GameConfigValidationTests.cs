using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 1 task 3 - GameConfig.Validate()/ValidateCatalog()
// against deliberately-broken configs.
public class GameConfigValidationTests
{
    private static GameConfig ValidConfig() => new(
        Id: "test",
        Name: "Test Config",
        EnergySymbols: [new SymbolDef("Fist"), new SymbolDef("Wild", IsWild: true)],
        Keywords: [new KeywordDef("Overcrush")],
        Rules: new RulesConfig(StartingLife: 20, DrawCount: 4, MaxTeamCards: 8, MaxTeamDice: 20, BasicActionCount: 2),
        BasicDicePool:
        [
            new BasicDicePoolEntry(
                new DieDefinition("Sidekick", [new Face([new SymbolAmount("Wild", 1)])]),
                Count: 8),
        ],
        BasicActionSlots: 2);

    [Fact]
    public void Valid_Config_Produces_No_Errors()
    {
        Assert.Empty(ValidConfig().Validate());
    }

    [Fact]
    public void Die_With_No_Faces_Is_Reported()
    {
        var config = ValidConfig() with
        {
            BasicDicePool = [new BasicDicePoolEntry(new DieDefinition("Empty", []), Count: 1)],
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("Empty") && e.Contains("no faces"));
    }

    [Fact]
    public void Face_Referencing_Undeclared_Symbol_Is_Reported()
    {
        var config = ValidConfig() with
        {
            BasicDicePool =
            [
                new BasicDicePoolEntry(
                    new DieDefinition("Bad", [new Face([new SymbolAmount("Nonexistent", 1)])]),
                    Count: 1),
            ],
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("Nonexistent") && e.Contains("undeclared"));
    }

    [Fact]
    public void Symbol_Id_Colliding_With_Keyword_Id_Is_Reported()
    {
        var config = ValidConfig() with
        {
            EnergySymbols = [new SymbolDef("Overcrush")],
            Keywords = [new KeywordDef("Overcrush")],
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("Overcrush") && e.Contains("collides"));
    }

    [Fact]
    public void Card_Affiliation_Colliding_With_Symbol_Id_Is_Reported()
    {
        var config = ValidConfig();
        var card = new CardDef(
            Id: "C001",
            Name: "Test Card",
            Subtitle: null,
            Set: "TEST",
            CardType: CardType.Character,
            PurchaseCost: 3,
            EnergySymbolIds: ["Fist"],
            Die: new DieDefinition("C001Die", [new Face([], Character: new CharacterFaceData(1, 1, 2, 2))]),
            DieLimit: 4,
            Affiliations: ["Fist"], // collides with the energy symbol id
            Keywords: [],
            RawText: "",
            Abilities: [],
            Continuous: []);

        var errors = config.ValidateCatalog([card]);

        Assert.Contains(errors, e => e.Contains("C001") && e.Contains("collides"));
    }

    [Fact]
    public void Card_Die_With_No_Faces_Is_Reported_Via_ValidateCatalog()
    {
        var config = ValidConfig();
        var card = new CardDef(
            Id: "C002",
            Name: "Empty Die Card",
            Subtitle: null,
            Set: "TEST",
            CardType: CardType.Character,
            PurchaseCost: 3,
            EnergySymbolIds: ["Fist"],
            Die: new DieDefinition("C002Die", []),
            DieLimit: 4,
            Affiliations: [],
            Keywords: [],
            RawText: "",
            Abilities: [],
            Continuous: []);

        var errors = config.ValidateCatalog([card]);

        Assert.Contains(errors, e => e.Contains("C002") && e.Contains("no faces"));
    }
}
