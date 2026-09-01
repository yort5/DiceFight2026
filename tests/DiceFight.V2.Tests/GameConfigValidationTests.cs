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

    private static CardDef NamedCard(string id, string name, IReadOnlyList<string>? affiliations = null) => new(
        Id: id, Name: name, Subtitle: null, Set: "TEST", CardType: CardType.Character,
        PurchaseCost: 1, EnergySymbolIds: ["Fist"],
        Die: new DieDefinition(id + "Die", [new Face([], new CharacterFaceData(1, 0, 1, 1))]),
        DieLimit: 1, Affiliations: affiliations ?? [], Keywords: [], RawText: "",
        Abilities: [], Continuous: []);

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

        Assert.Contains(errors, e => e.Contains("C001") && e.Contains("also a declared energy symbol or keyword"));
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

    // The tag namespace holds keywords, card names, "sidekick" and energy
    // symbol ids, so a collision anywhere in it makes a TagQuery
    // ambiguous. Card names are the likeliest offender - they are the one
    // part nobody picks with the namespace in mind.
    [Fact]
    public void A_Card_Name_Colliding_With_A_Keyword_Is_Reported()
    {
        var config = ValidConfig(); // already declares the Overcrush keyword
        var card = NamedCard("C1", "Overcrush");

        var errors = config.ValidateCatalog([card]);

        Assert.Contains(errors, e => e.Contains("card name") && e.Contains("Overcrush"));
    }

    // The inverse of the test above, and the point of Parts 17-21:
    // affiliations left the tag namespace, so this collision stopped
    // being one. The real catalog is full of characters named after
    // their own team, and every one of them used to be an error.
    [Fact]
    public void An_Affiliation_May_Share_A_Name_With_A_Card()
    {
        var config = ValidConfig();
        var named = NamedCard("C1", "Wolverine");
        var affiliated = NamedCard("C2", "Kitty Pryde", affiliations: ["Wolverine"]);

        var errors = config.ValidateCatalog([named, affiliated]);

        Assert.Empty(errors);
    }

    // Still worth reporting, for a different reason than before: nothing
    // is ambiguous, but `Tags:` and `Affiliations:` are near-identical
    // filter fields that blanking treats oppositely, so a card whose
    // affiliation doubles as a keyword is a trap for whoever authors the
    // next filter against it.
    [Fact]
    public void An_Affiliation_That_Is_Also_A_Keyword_Is_Reported()
    {
        var config = ValidConfig(); // already declares the Overcrush keyword
        var card = NamedCard("C1", "Kitty Pryde", affiliations: ["Overcrush"]);

        var errors = config.ValidateCatalog([card]);

        Assert.Contains(errors, e => e.Contains("also a declared energy symbol or keyword"));
    }
}
