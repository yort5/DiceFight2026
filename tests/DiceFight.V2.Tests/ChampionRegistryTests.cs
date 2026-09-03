using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// v3 "Instinct Clash" addition (2026-09-03) - ChampionRegistry, the
// passive/always-on/no-die/no-cost team effect GameConfig.Champions
// declares. Registered directly into GameState's existing modifier
// lists (not through ContinuousRegistry, which needs a source die) -
// these tests exercise all four ChampionPassiveKind values and confirm
// a Champion never leaks onto the opponent's dice.
public class ChampionRegistryTests
{
    private static CardDef BuildCharacterCard(string id, int fieldingCost, int purchaseCost) => new(
        Id: id,
        Name: id,
        Subtitle: null,
        Set: "TEST",
        CardType: CardType.Character,
        PurchaseCost: purchaseCost,
        EnergySymbolIds: ["Claw"],
        Die: new DieDefinition($"{id}Die",
        [
            new Face([], new CharacterFaceData(Level: 1, FieldingCost: fieldingCost, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        ]),
        DieLimit: 1,
        Affiliations: [],
        Keywords: [],
        RawText: "",
        Abilities: [],
        Continuous: []);

    private static GameConfig BuildConfig(ChampionPassiveKind kind, int amount) => new(
        Id: "champion-test",
        Name: "Champion Test Config",
        EnergySymbols: [new SymbolDef("Claw"), new SymbolDef("Wild", IsWild: true)],
        Keywords: [],
        Rules: new RulesConfig(StartingLife: 20, DrawCount: 4, MaxTeamCards: 1, MaxTeamDice: 1, BasicActionCount: 0),
        BasicDicePool: [],
        BasicActionSlots: 0)
    {
        Champions = [new ChampionDef("champ-1", "Test Champion", "Claw", kind, amount)],
    };

    private static (GameState state, DieInstance p1Die, DieInstance p2Die) SetUp(
        ChampionPassiveKind kind, int amount, int fieldingCost = 1, int purchaseCost = 2)
    {
        var config = BuildConfig(kind, amount);
        var card = BuildCharacterCard("T001", fieldingCost, purchaseCost);
        var catalog = new Dictionary<string, CardDef> { [card.Id] = card };

        var playerOne = new Player { Id = "p1", Name = "One", ChampionId = "champ-1" };
        playerOne.TeamCardIds.Add(card.Id);
        var playerTwo = new Player { Id = "p2", Name = "Two" }; // no Champion picked
        playerTwo.TeamCardIds.Add(card.Id);

        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);

        var p1Die = state.DiceIn("p1", Zone.Unpurchased).Single();
        var p2Die = state.DiceIn("p2", Zone.Unpurchased).Single();
        p1Die.CurrentFaceIndex = 0;
        p2Die.CurrentFaceIndex = 0;
        p1Die.Zone = Zone.FieldZone;
        p2Die.Zone = Zone.FieldZone;

        return (state, p1Die, p2Die);
    }

    [Fact]
    public void AttackBuff_Applies_Only_To_The_Champions_Owner()
    {
        var (state, p1Die, p2Die) = SetUp(ChampionPassiveKind.AttackBuff, amount: 1);
        Assert.Equal(2, QueryEngine.GetAttack(state, p1Die)); // 1 printed + 1 passive
        Assert.Equal(1, QueryEngine.GetAttack(state, p2Die)); // untouched
    }

    [Fact]
    public void DefenseBuff_Applies_Only_To_The_Champions_Owner()
    {
        var (state, p1Die, p2Die) = SetUp(ChampionPassiveKind.DefenseBuff, amount: 1);
        Assert.Equal(2, QueryEngine.GetDefense(state, p1Die));
        Assert.Equal(1, QueryEngine.GetDefense(state, p2Die));
    }

    [Fact]
    public void FieldingCostDiscount_Floors_At_Zero()
    {
        var (state, p1Die, p2Die) = SetUp(ChampionPassiveKind.FieldingCostDiscount, amount: 5, fieldingCost: 2);
        Assert.Equal(0, QueryEngine.GetFieldingCost(state, p1Die)); // 2 - 5, floored
        Assert.Equal(2, QueryEngine.GetFieldingCost(state, p2Die));
    }

    [Fact]
    public void PurchaseCostDiscount_Floors_At_One()
    {
        var (state, _, _) = SetUp(ChampionPassiveKind.PurchaseCostDiscount, amount: 5, purchaseCost: 3);
        var card = state.CardCatalog["T001"];
        Assert.Equal(1, QueryEngine.GetPurchaseCost(state, card, "p1")); // 3 - 5, floored
        Assert.Equal(3, QueryEngine.GetPurchaseCost(state, card, "p2"));
    }

    [Fact]
    public void No_ChampionId_Registers_Nothing()
    {
        var config = BuildConfig(ChampionPassiveKind.AttackBuff, amount: 1);
        var card = BuildCharacterCard("T001", fieldingCost: 1, purchaseCost: 2);
        var catalog = new Dictionary<string, CardDef> { [card.Id] = card };
        var playerOne = new Player { Id = "p1", Name = "One" }; // ChampionId left null
        playerOne.TeamCardIds.Add(card.Id);
        var playerTwo = new Player { Id = "p2", Name = "Two" };

        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        var die = state.DiceIn("p1", Zone.Unpurchased).Single();
        die.CurrentFaceIndex = 0;
        die.Zone = Zone.FieldZone;

        Assert.Equal(1, QueryEngine.GetAttack(state, die)); // no passive applied
    }
}
