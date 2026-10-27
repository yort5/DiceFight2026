using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// The twelve painted animals swapped into Set 1 (2026-10-27), through the
// real firing paths, as Dice Kingdom cards.
public class PaintedSwapCardsTests
{
    private sealed class Roller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static GameState Game()
    {
        Player Player(string id) => new() { Id = id, Name = id,
            CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Wing", 8)] };
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog.ToDictionary(p => p.Key, p => p.Value),
            Player("p1"), Player("p2"));
        state.MoveToStep(StepIds.Main);
        return state;
    }

    private static DieInstance Add(GameState s, string id, string owner = "p1", Zone zone = Zone.FieldZone, int level = 1)
    {
        var card = s.CardCatalog[id];
        var die = new DieInstance { Id = "test-" + s.Dice.Count, CardId = id, OwnerId = owner, ControllerId = owner, Zone = zone,
            CurrentFaceIndex = card.Die.Faces.Select((f, i) => (f, i)).First(x => x.f.Character?.Level == level).i };
        s.Dice.Add(die);
        return die;
    }

    // A die of a native card on its face 5, a single energy of that card's type.
    private static DieInstance Energy(GameState s, string cardId)
    {
        var die = Add(s, cardId, zone: Zone.ReservePool);
        die.CurrentFaceIndex = 5;
        return die;
    }

    private static void Drain(GameState s, AbilityQueue q, Func<PendingChoice, string>? pick = null)
    {
        EffectInterpreter.DrainQueue(s, q, new Roller(5), new Random(1));
        for (var i = 0; s.PendingChoice is { } choice; i++)
        {
            Assert.True(i < 30, "Choices did not finish.");
            EffectInterpreter.AnswerPendingChoice(s, [pick?.Invoke(choice) ?? choice.CandidateIds[0]]);
            EffectInterpreter.DrainQueue(s, q, new Roller(5), new Random(1));
        }
    }

    private static void Field(GameState s, AbilityQueue q, DieInstance die, Func<PendingChoice, string>? pick = null)
    {
        die.Zone = Zone.ReservePool;
        var energy = Enumerable.Range(0, s.GetCurrentFace(die)!.Character!.FieldingCost)
            .Select(_ => Energy(s, DiceKingdomConfig.Elephant.Id).Id).ToList();
        TurnEngine.Field(s, q, die.Id, energy);
        Drain(s, q, pick);
    }

    private static void Attack(GameState s, AbilityQueue q, params DieInstance[] attackers) => Attack(s, q, null, attackers);

    private static void Attack(GameState s, AbilityQueue q, Func<PendingChoice, string>? pick, params DieInstance[] attackers)
    {
        s.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(s, q, attackers.Select((d, i) => (d.Id, i)).ToDictionary(x => x.Id, x => x.i));
        Drain(s, q, pick);
    }

    [Fact]
    public void Text_Reads_In_Dice_Kingdom_Terms()
    {
        var cards = DiceKingdomConfig.Catalog;
        Assert.Equal("On Field: KO target character die.", cards["DPS027"].RawText);
        Assert.Equal("On Attack: deal 3 damage to target character die.", cards["DPS086"].RawText);
        Assert.Contains("Fast", cards["DPS056"].Keywords);
        Assert.Equal("Global: Pay Shell. Once per turn, on your turn, Prep a die from your bag.", cards["DPS152"].RawText);
        Assert.Equal("While Beaver is active, your other character dice in the Field Zone get +3D.", cards["AOU136"].RawText);
        Assert.Contains("Deadly", cards["DPS139"].Keywords);
        Assert.Contains("Intimidate", cards["CW056"].Keywords);
        Assert.Equal(["Shell"], cards["DPS063"].EnergySymbolIds);
    }

    [Fact]
    public void Tasmanian_Devil_KOs_A_Creature_When_Fielded()
    {
        var s = Game(); var q = new AbilityQueue();
        var victim = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        Field(s, q, Add(s, "DPS027", zone: Zone.ReservePool), c => victim.Id);
        Assert.NotEqual(Zone.FieldZone, victim.Zone);
    }

    [Fact]
    public void Lionfish_Deals_3_Damage_When_It_Attacks()
    {
        var s = Game(); var q = new AbilityQueue();
        var lionfish = Add(s, "DPS086");
        var target = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2", level: 3); // 7 DEF, survives
        Attack(s, q, c => target.Id, lionfish);
        Assert.Equal(3, target.Damage);
    }

    [Fact]
    public void Nutcracker_Preps_A_Die_From_The_Bag_When_Fielded()
    {
        var s = Game(); var q = new AbilityQueue();
        var before = s.DiceIn("p1", Zone.PrepArea).Count();
        Field(s, q, Add(s, "DPS047", zone: Zone.ReservePool));
        Assert.Equal(before + 1, s.DiceIn("p1", Zone.PrepArea).Count());
    }

    [Fact]
    public void Cuckoo_Rerolls_Every_Opposing_Creature_And_Energy_Results_Go_To_The_Used_Pile()
    {
        var s = Game(); var q = new AbilityQueue();
        var a = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var b = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        Field(s, q, Add(s, "DPS072", zone: Zone.ReservePool)); // the test roller lands every die on face 5, an energy face
        Assert.Equal(Zone.UsedPile, a.Zone);
        Assert.Equal(Zone.UsedPile, b.Zone);
    }

    [Fact]
    public void King_Cobra_Ignores_The_Abilities_Of_Its_Blocker()
    {
        var s = Game(); var q = new AbilityQueue();
        var cobra = Add(s, "DPS095");
        var before = QueryEngine.GetAttack(s, cobra);
        var panda = Add(s, "CW054", "p2"); // Red Panda: On Block, the attacker gets -2A
        Attack(s, q, cobra);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(cobra.Id, panda.Id);
        CombatEngine.DeclareBlockers(s, q, assignment, [panda.Id]);
        Drain(s, q);
        Assert.Equal(before, QueryEngine.GetAttack(s, cobra));
    }

    [Fact]
    public void Giant_Tortoise_Takes_1_Off_Damage_To_Your_Creatures()
    {
        var s = Game(); var q = new AbilityQueue();
        Add(s, "DPS063", "p2");
        var target = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2", level: 3);
        Attack(s, q, c => target.Id, Add(s, "DPS086")); // Lionfish, 3 damage
        Assert.Equal(2, target.Damage);
    }

    [Fact]
    public void Horseshoe_Crab_Cannot_Be_Targeted_By_Opposing_Action_Dice()
    {
        var s = Game(); var q = new AbilityQueue();
        var crab = Add(s, "DPS019", "p2");
        var other = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        Add(s, DiceKingdomConfig.Elephant.Id); // Archnemesis' own creature
        var card = s.CardCatalog[DiceKingdomConfig.Archnemesis.Id]; // target yours and target opposing
        var action = new DieInstance { Id = "anger", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool,
            CurrentFaceIndex = card.Die.Faces.Select((f, i) => (f, i)).First(x => x.f.Kind == FaceKind.ActionFace).i };
        s.Dice.Add(action);
        var offered = new List<string>();
        TurnEngine.UseAction(s, q, action.Id);
        Drain(s, q, c => { offered.AddRange(c.CandidateIds); return c.CandidateIds.Contains(other.Id) ? other.Id : c.CandidateIds[0]; });
        Assert.DoesNotContain(crab.Id, offered); // a lone legal target is picked without a choice
        Assert.Equal(0, crab.Damage);
        Assert.True(other.Damage > 0);
    }

    [Fact]
    public void Starfish_Global_Preps_A_Die_Once_Per_Turn()
    {
        var s = Game(); var q = new AbilityQueue();
        Add(s, "DPS152");
        var before = s.DiceIn("p1", Zone.PrepArea).Count();
        TurnEngine.UseGlobal(s, q, "DPS152", "p1", 0, [Energy(s, DiceKingdomConfig.Hippopotamus.Id).Id]);
        Drain(s, q);
        Assert.Equal(before + 1, s.DiceIn("p1", Zone.PrepArea).Count());
        Assert.ThrowsAny<Exception>(() =>
            TurnEngine.UseGlobal(s, q, "DPS152", "p1", 0, [Energy(s, DiceKingdomConfig.Hippopotamus.Id).Id]));
    }

    [Fact]
    public void Beaver_Gives_Your_Other_Creatures_3_DEF()
    {
        var s = Game();
        var friend = Add(s, DiceKingdomConfig.Elephant.Id);
        var foe = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var friendBefore = QueryEngine.GetDefense(s, friend);
        var foeBefore = QueryEngine.GetDefense(s, foe);
        var beaver = Add(s, "AOU136");
        var printed = s.GetCurrentFace(beaver)!.Character!.Defense;
        Assert.Equal(friendBefore + 3, QueryEngine.GetDefense(s, friend));
        Assert.Equal(foeBefore, QueryEngine.GetDefense(s, foe));
        Assert.Equal(printed, QueryEngine.GetDefense(s, beaver));
    }
}
