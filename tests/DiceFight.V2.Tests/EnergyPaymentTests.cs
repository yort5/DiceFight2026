using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// Rule 1.4.3's three energy symbols and 2.6.2.3's payment rule, which
// between them decide every purchase and fielding in the game.
//
// The tests are the rulebook's own worked examples where it has them,
// because both bugs these cover were the kind that a plausible-looking
// implementation passes: generic energy was unrepresented (so a Basic
// Action die on an energy face paid NOTHING), and one wildcard satisfied
// every outstanding type requirement at once instead of one.
public class EnergyPaymentTests
{
    private static Dictionary<string, CardDef> BuildCatalog() =>
        DpsCards.All.ToDictionary(c => c.Id);

    private static GameState NewGame(params CardDef[] extra)
    {
        var catalog = BuildCatalog();
        foreach (var card in extra) catalog[card.Id] = card;
        var state = GameSetup.NewGame(DiceFightClassicConfig.Config, catalog,
            new Player { Id = "p1", Name = "One" }, new Player { Id = "p2", Name = "Two" });
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    // A die in the Reserve Pool showing exactly the symbols named. Backed
    // by a throwaway one-face card, because a die must resolve its faces
    // through either a card or the config's basic pool.
    private static DieInstance EnergyDie(GameState state, string id, params SymbolAmount[] symbols)
    {
        var card = new CardDef(
            Id: $"ENERGY-{id}", Name: $"Energy {id}", Subtitle: null, Set: "TEST", CardType: CardType.Character,
            PurchaseCost: 1, EnergySymbolIds: [],
            Die: new DieDefinition($"ENERGY-{id}Die", [new Face(symbols)]),
            DieLimit: 1, Affiliations: [], Keywords: [], RawText: "", Abilities: [], Continuous: []);
        ((Dictionary<string, CardDef>)state.CardCatalog)[card.Id] = card;

        var die = new DieInstance
        {
            Id = id, CardId = card.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, CurrentFaceIndex = 0,
        };
        state.Dice.Add(die);
        return die;
    }

    // --- Generic energy (rule 1.4.3, 1.3.10) ---

    // The bug in one line: a Basic Action die's energy face showed no
    // symbol at all, so spending it contributed 0 toward any cost.
    [Fact]
    public void A_Basic_Action_Dies_Energy_Face_Provides_Two_Generic()
    {
        var faces = DpsCards.PowerBolt.Die.Faces;
        var energyFaces = faces.Where(f => f.Kind == FaceKind.EnergyFace).ToList();

        Assert.Equal(3, energyFaces.Count);
        // Rule 2.6.1.5 - a Basic Action die's energy faces are ALL doubles,
        // which is why it has no single face to spin down to.
        Assert.All(energyFaces, f =>
        {
            var symbol = Assert.Single(f.Symbols);
            Assert.Equal("Generic", symbol.SymbolId);
            Assert.Equal(2, symbol.Count);
        });
    }

    // Rule 2.6.2.3 example (1): "1 mask and 2 generic energy" buys a
    // 3-cost mask die. The generic pays two thirds of the cost and the
    // single mask satisfies the type.
    [Fact]
    public void Generic_Pays_Toward_The_Amount()
    {
        var state = NewGame();
        var mask = EnergyDie(state, "mask", new SymbolAmount("Mask", 1));
        var generic = EnergyDie(state, "generic", new SymbolAmount("Generic", 2));

        // Magik costs 4 Mask; 1 mask + 2 generic is only 3, so this fails
        // on AMOUNT rather than on type - proof the generic was counted.
        var short1 = Assert.Throws<InvalidOperationException>(() =>
            Purchase(state, DpsCards.MagikSorceressOfLimbo, [mask.Id, generic.Id]));
        Assert.Contains("offered 3", short1.Message);
    }

    // ...and never toward the TYPE. Rule 1.4.3: generic "is not
    // considered to be any type of energy".
    [Fact]
    public void Generic_Never_Satisfies_A_Type_Requirement()
    {
        var state = NewGame();
        var a = EnergyDie(state, "g1", new SymbolAmount("Generic", 2));
        var b = EnergyDie(state, "g2", new SymbolAmount("Generic", 2));

        // 4 generic is enough energy for a 4-cost Mask card, but none of
        // it is a mask.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Purchase(state, DpsCards.MagikSorceressOfLimbo, [a.Id, b.Id]));

        Assert.Contains("required symbol", ex.Message);
        Assert.Contains("Mask", ex.Message);
    }

    // --- Wildcard (rule 1.4.3) ---

    // "You may consider this to represent ANY OF the four energy types" -
    // one energy, one type. A single wild used to clear every outstanding
    // requirement at once, which bought a Crossover for half its type
    // cost.
    [Fact]
    public void One_Wild_Satisfies_One_Type_Requirement_Not_Every_One()
    {
        var crossover = CrossoverCard();
        var state = NewGame(crossover);

        var wild = EnergyDie(state, "wild", new SymbolAmount("Wild", 1));
        var filler = EnergyDie(state, "filler", new SymbolAmount("Generic", 2));

        // 3 energy for a 3-cost bolt-fist Crossover, but only ONE of the
        // two required types is covered.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Purchase(state, crossover, [wild.Id, filler.Id]));
        Assert.Contains("required symbol", ex.Message);

        // Rule 2.6.2.3 example (2): a combination of 3 energy including a
        // bolt and a fist. The wild stands in for one of them.
        var state2 = NewGame(crossover);
        var wild2 = EnergyDie(state2, "wild", new SymbolAmount("Wild", 1));
        var bolt = EnergyDie(state2, "bolt", new SymbolAmount("Bolt", 1));
        var spare = EnergyDie(state2, "spare", new SymbolAmount("Generic", 1));

        Purchase(state2, crossover, [wild2.Id, bolt.Id, spare.Id]); // no throw
    }

    // A DOUBLE wild is two energy and covers two types.
    [Fact]
    public void A_Double_Wild_Covers_Two_Type_Requirements()
    {
        var crossover = CrossoverCard();
        var state = NewGame(crossover);
        var wild = EnergyDie(state, "wild", new SymbolAmount("Wild", 2));
        var spare = EnergyDie(state, "spare", new SymbolAmount("Generic", 1));

        Purchase(state, crossover, [wild.Id, spare.Id]); // no throw
    }

    // A wild is never spent covering a type a printed symbol already
    // covered - otherwise a bolt + wild would fail a bolt-fist card.
    [Fact]
    public void A_Wild_Is_Not_Wasted_On_A_Type_Already_Covered()
    {
        var crossover = CrossoverCard();
        var state = NewGame(crossover);
        var bolt = EnergyDie(state, "bolt", new SymbolAmount("Bolt", 2));
        var wild = EnergyDie(state, "wild", new SymbolAmount("Wild", 1));

        Purchase(state, crossover, [bolt.Id, wild.Id]); // the wild must become the fist
    }

    private static CardDef CrossoverCard() => new(
        Id: "XOVER", Name: "Crossover Test", Subtitle: null, Set: "TEST", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Bolt", "Fist"],
        Die: MigrationDice.Character("XOVERDie", ["Bolt", "Fist"], (0, 1, 1), (1, 2, 2), (1, 3, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [], RawText: "", Abilities: [], Continuous: []);

    private static void Purchase(GameState state, CardDef card, IReadOnlyList<string> energyDieIds)
    {
        var die = new DieInstance
        {
            Id = $"buy-{card.Id}", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased,
        };
        state.Dice.Add(die);
        TurnEngine.Purchase(state, new AbilityQueue(), die.Id, energyDieIds);
    }
}
