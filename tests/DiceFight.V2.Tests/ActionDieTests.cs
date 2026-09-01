using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// "Action die" is the BROAD category - any die with no fielding cost,
// attack or defense. Basic Action is a subset of it (the shared ones),
// and Epic Basic Action a subset of that (not modelled yet, user's call).
//
// The distinction is not academic: an ability that says "action die"
// (Attune) is satisfied by any of them, while one that says "Basic Action
// die" (Boom Boom) is not. Three of the four places that asked about card
// type compared against BasicAction when they meant the broad category.
//
// No non-basic Action card is migrated yet, so these use a synthetic one.
// The engine behaviour is what is under test, not a particular card.
public class ActionDieTests
{
    // Modelled on Cosmic Treadmill "Antique Shop Discovery" (GAF009): a
    // fist/mask Crossover Action card, which is what makes it useful -
    // it is the case where an Action die's faces differ from a Basic
    // Action's most visibly.
    private static readonly CardDef CosmicTreadmill = new(
        Id: "GAF009", Name: "Cosmic Treadmill", Subtitle: "Antique Shop Discovery", Set: "GAF",
        CardType: CardType.Action,
        PurchaseCost: 4, EnergySymbolIds: ["Fist", "Mask"],
        Die: MigrationDice.Action("GAF009Die", ["Fist", "Mask"], 0, 1, 2),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Reroll up to 2 target dice in your Reserve Pool. For each Fist or Mask rolled, deal 1 damage to target opponent.",
        Abilities: [], Continuous: []);

    private static GameState NewGame(params CardDef[] extra)
    {
        var catalog = DpsCards.All.ToDictionary(c => c.Id);
        foreach (var card in extra) catalog[card.Id] = card;
        var state = GameSetup.NewGame(DiceFightClassicConfig.Config, catalog,
            new Player { Id = "p1", Name = "One" }, new Player { Id = "p2", Name = "Two" });
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    private static DieInstance Die(GameState state, CardDef card, string controllerId, Zone zone, int faceIndex, string id)
    {
        var die = new DieInstance
        {
            Id = id, CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId,
            Zone = zone, CurrentFaceIndex = faceIndex,
        };
        state.Dice.Add(die);
        return die;
    }

    // --- The category itself ---

    [Fact]
    public void Basic_Action_Is_An_Action_Die_But_Not_Every_Action_Die_Is_Basic()
    {
        Assert.True(CardType.Action.IsActionDie());
        Assert.True(CardType.BasicAction.IsActionDie());
        Assert.False(CardType.Character.IsActionDie());

        Assert.True(CardType.BasicAction.IsCommunity());
        Assert.False(CardType.Action.IsCommunity());   // takes a team slot
        Assert.False(CardType.Character.IsCommunity());
    }

    // --- Faces: an Action die is not a Basic Action die ---

    // GAF009's own face strip: a generic single, two fist/mask doubles,
    // then three action faces.
    [Fact]
    public void An_Action_Dies_Energy_Faces_Are_The_Cards_Own_Not_Generic()
    {
        var faces = CosmicTreadmill.Die.Faces;

        Assert.Equal(6, faces.Count);
        Assert.Equal(["Fist", "Mask"], faces[0].Symbols.Select(s => s.SymbolId));
        Assert.Equal(2, faces[0].SymbolCount);
        Assert.Equal(["Generic"], faces[2].Symbols.Select(s => s.SymbolId)); // the Crossover single
        Assert.All(faces.Skip(3), f => Assert.Equal(FaceKind.ActionFace, f.Kind));
    }

    [Fact]
    public void A_Basic_Action_Dies_Energy_Faces_Are_All_Generic_Doubles()
    {
        var energyFaces = DpsCards.PowerBolt.Die.Faces.Where(f => f.Kind == FaceKind.EnergyFace);

        Assert.All(energyFaces, f => Assert.Equal(["Generic"], f.Symbols.Select(s => s.SymbolId)));
    }

    // --- Using one ---

    // "Use an Action die" is the broad category, so a non-basic Action
    // card qualifies. This used to be rejected outright.
    [Fact]
    public void A_Non_Basic_Action_Die_Can_Be_Used_As_An_Action_Die()
    {
        var state = NewGame(CosmicTreadmill);
        var die = Die(state, CosmicTreadmill, "p1", Zone.ReservePool, 3, "treadmill");

        TurnEngine.UseAction(state, new AbilityQueue(), die.Id);

        Assert.Equal(Zone.OutOfPlay, die.Zone);
    }

    [Fact]
    public void A_Character_Die_Still_Cannot_Be_Used_As_An_Action_Die()
    {
        var state = NewGame();
        var die = Die(state, DpsCards.StormExtremeWeather, "p1", Zone.ReservePool, 3, "storm");

        var ex = Assert.Throws<InvalidOperationException>(() => TurnEngine.UseAction(state, new AbilityQueue(), die.Id));
        Assert.Contains("not an Action card", ex.Message);
    }

    // An Action card takes a team slot and belongs to its owner, unlike a
    // Basic Action - so the opponent cannot buy it off your team.
    [Fact]
    public void An_Action_Card_Is_Not_Community_Property()
    {
        var state = NewGame(CosmicTreadmill);
        var mine = Die(state, CosmicTreadmill, "p2", Zone.Unpurchased, 0, "theirs");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, new AbilityQueue(), mine.Id, []));
        Assert.Contains("opponent's team", ex.Message);
    }

    // --- Targeting: the broad filter and the narrow one ---

    [Fact]
    public void TargetKind_ActionDie_Matches_Both_Kinds_And_BasicActionDie_Only_One()
    {
        var state = NewGame(CosmicTreadmill);
        Die(state, CosmicTreadmill, "p1", Zone.FieldZone, 3, "action");
        Die(state, DpsCards.PowerBolt, "p1", Zone.FieldZone, 3, "basic");
        Die(state, DpsCards.StormExtremeWeather, "p1", Zone.FieldZone, 3, "character");

        var broad = TargetResolver.Query(state, "p1",
            new TargetFilter(Kind: TargetKind.ActionDie, Count: 9), new Dictionary<string, string>());
        var narrow = TargetResolver.Query(state, "p1",
            new TargetFilter(Kind: TargetKind.BasicActionDie, Count: 9), new Dictionary<string, string>());

        Assert.Equal(["action", "basic"], broad.OrderBy(x => x));
        Assert.Equal(["basic"], narrow);
    }

    // Clean Up returns every UNUSED action die to the Used Pile, not only
    // the shared ones - an Action die left in the Reserve Pool would
    // otherwise sit there forever.
    [Fact]
    public void CleanUp_Sweeps_An_Unused_Non_Basic_Action_Die()
    {
        var state = NewGame(CosmicTreadmill);
        var die = Die(state, CosmicTreadmill, "p1", Zone.ReservePool, 3, "treadmill");
        state.CurrentStep = TurnStep.Attack;

        TurnEngine.CleanUp(state, new AbilityQueue());

        Assert.Equal(Zone.UsedPile, die.Zone);
    }
}
