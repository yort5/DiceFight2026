using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// KingdomImports.cs (2026-10-20) - every card with more than a keyword,
// through the real firing paths, as Dice Kingdom cards.
public class KingdomImportsTests
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

    // One of the player's own Tardigrades, out of the bag and on its level 2 face.
    private static DieInstance Tardigrade(GameState s, string owner, Zone zone = Zone.FieldZone)
    {
        var die = s.DiceIn(owner, Zone.Bag).First();
        die.Zone = zone;
        die.CurrentFaceIndex = s.GetDieDefinition(die).Faces.Select((f, i) => (f, i)).First(x => x.f.Character?.Level == 2).i;
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
        // Paid in single Eye energy (Elephant's face 5), one die per point of fielding cost.
        var energy = Enumerable.Range(0, s.GetCurrentFace(die)!.Character!.FieldingCost).Select(_ =>
        {
            var e = Add(s, DiceKingdomConfig.Elephant.Id, zone: Zone.ReservePool);
            e.CurrentFaceIndex = 5;
            return e.Id;
        }).ToList();
        TurnEngine.Field(s, q, die.Id, energy);
        Drain(s, q, pick);
    }

    private static void Attack(GameState s, AbilityQueue q, params DieInstance[] attackers)
    {
        s.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(s, q, attackers.Select((d, i) => (d.Id, i)).ToDictionary(x => x.Id, x => x.i));
        Drain(s, q);
    }

    [Fact]
    public void Text_Reads_In_Dice_Kingdom_Terms()
    {
        var cards = DiceKingdomConfig.Catalog;
        Assert.Equal("On Field: deal 1 damage to each opposing character die.", cards["XFC003"].RawText);
        Assert.Equal("Snow Leopard can only be blocked by two or more character dice.", cards["BFF033"].RawText);
        Assert.Equal("While Koala is active, your Tardigrade character dice get +2D.", cards["AI013"].RawText);
        Assert.Equal("On Attack: target opposing character die gets -2A and Swordfish gets +2A until end of turn.", cards["ASM110"].RawText);
        Assert.Contains("On Block", cards["CW054"].Keywords);
        Assert.Equal(["Deadly", "Fast"], cards["DOOM017"].Keywords);
        Assert.Equal("Firefly Squid", DiceKingdomConfig.TeamBuilderCatalog["DPS116"].Name);
    }

    [Fact]
    public void Pistol_Shrimp_Hits_Every_Opposing_Creature_And_None_Of_Yours()
    {
        var s = Game(); var q = new AbilityQueue();
        var theirs = new[] { Add(s, DiceKingdomConfig.Elephant.Id, "p2"), Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2") };
        var mine = Add(s, DiceKingdomConfig.Elephant.Id);
        Field(s, q, Add(s, "XFC003", zone: Zone.ReservePool));
        Assert.All(theirs, d => Assert.Equal(1, d.Damage));
        Assert.Equal(0, mine.Damage);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Snow_Leopard_Needs_Two_Blockers(int blockers, bool legal)
    {
        var s = Game(); var q = new AbilityQueue();
        var leopard = Add(s, "BFF033");
        var walls = Enumerable.Range(0, blockers).Select(_ => Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2")).ToList();
        Attack(s, q, leopard);
        var assignment = new CombatAssignment();
        foreach (var wall in walls) assignment.AssignBlocker(leopard.Id, wall.Id);
        var block = () => CombatEngine.DeclareBlockers(s, q, assignment, walls.Select(w => w.Id).ToList());
        if (legal) block(); else Assert.Throws<InvalidOperationException>(block);
    }

    // The bot used to send one blocker at it, which the engine refuses
    // (116 errors in the 2026-10-20 card sweep).
    [Theory]
    [InlineData(1, 20, 0)]  // one blocker: can't block it, so it doesn't
    [InlineData(2, 5, 2)]   // two and the hit is lethal: both block
    public void Bot_Blocks_Snow_Leopard_With_Two_Or_Not_At_All(int walls, int life, int expectedBlocks)
    {
        var s = Game(); var q = new AbilityQueue();
        var leopard = Add(s, "BFF033", level: 3); // 8/5
        for (var i = 0; i < walls; i++) Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        s.PlayerTwo.Life = life;
        Attack(s, q, leopard);
        var decision = Bot.DiceKingdomBot.Decide(s, "p2")!;
        Assert.Equal(Bot.BotActionKind.DeclareBlockers, decision.Kind);
        Assert.Equal(expectedBlocks, decision.Blocks.Count);
        var assignment = new CombatAssignment();
        foreach (var (attacker, blocker) in decision.Blocks) assignment.AssignBlocker(attacker, blocker);
        CombatEngine.DeclareBlockers(s, q, assignment, decision.Blocks.Select(b => b.BlockerId).ToList()); // legal
    }

    [Fact]
    public void A_Lone_Forced_Blocker_Is_Excused_From_Snow_Leopard()
    {
        var s = Game(); var q = new AbilityQueue();
        var leopard = Add(s, "BFF033");
        var crab = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        crab.CombatFlags.Add(CombatFlagKind.MustBlock);
        Attack(s, q, leopard);
        CombatEngine.DeclareBlockers(s, q, new CombatAssignment(), []); // no throw
    }

    [Fact]
    public void Red_Panda_Takes_2_ATK_Off_An_Attacker_When_It_Blocks()
    {
        var s = Game(); var q = new AbilityQueue();
        var attacker = Add(s, DiceKingdomConfig.Elephant.Id); // L1
        var before = QueryEngine.GetAttack(s, attacker);
        var panda = Add(s, "CW054", "p2");
        Attack(s, q, attacker);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, panda.Id);
        CombatEngine.DeclareBlockers(s, q, assignment, [panda.Id]);
        Drain(s, q);
        Assert.Equal(Math.Max(0, before - 2), QueryEngine.GetAttack(s, attacker));
    }

    [Fact]
    public void Koala_Gives_Your_Tardigrades_2_DEF_While_It_Is_Active()
    {
        var s = Game();
        var joey = Tardigrade(s, "p1");
        var theirs = Tardigrade(s, "p2");
        var before = QueryEngine.GetDefense(s, joey);
        var koala = Add(s, "AI013");
        Assert.Equal(before + 2, QueryEngine.GetDefense(s, joey));
        Assert.Equal(before, QueryEngine.GetDefense(s, theirs));
        Assert.Equal(s.GetCurrentFace(koala)!.Character!.Defense, QueryEngine.GetDefense(s, koala)); // not itself
        koala.Zone = Zone.UsedPile;
        Assert.Equal(before, QueryEngine.GetDefense(s, joey));
    }

    [Fact]
    public void Manatee_Gains_1_Life_Per_Opposing_Creature_That_Is_Not_A_Tardigrade()
    {
        var s = Game(); var q = new AbilityQueue();
        Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        Tardigrade(s, "p2");
        Add(s, DiceKingdomConfig.Elephant.Id); // mine - doesn't count
        var life = s.PlayerOne.Life;
        Field(s, q, Add(s, "HQ019", zone: Zone.ReservePool));
        Assert.Equal(life + 2, s.PlayerOne.Life);
    }

    [Fact]
    public void Bearded_Dragon_Grows_With_Each_Energy_Type_In_Your_Reserve()
    {
        var s = Game();
        var dragon = Add(s, "SWW020"); // L1 4/6
        void Energy(string cardId)
        {
            var d = Add(s, cardId, zone: Zone.ReservePool);
            d.CurrentFaceIndex = s.CardCatalog[cardId].Die.Faces.Select((f, i) => (f, i)).First(x => x.f.Kind == FaceKind.EnergyFace).i;
        }
        Assert.Equal((4, 6), (QueryEngine.GetAttack(s, dragon), QueryEngine.GetDefense(s, dragon)));
        Energy(DiceKingdomConfig.Elephant.Id); // Eye
        Energy(DiceKingdomConfig.Fox.Id);      // Eye again - same type
        Energy(DiceKingdomConfig.HoneyBadger.Id); // Claw
        Tardigrade(s, "p1", Zone.ReservePool);    // a Wild - doesn't count
        Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.ReservePool).CurrentFaceIndex = 3; // opponent's Shell
        Assert.Equal((6, 8), (QueryEngine.GetAttack(s, dragon), QueryEngine.GetDefense(s, dragon)));
    }

    [Fact]
    public void Cone_Snail_Can_Only_KO_A_Creature_Costing_4_Or_Less()
    {
        var s = Game(); var q = new AbilityQueue();
        var cheap = Add(s, DiceKingdomConfig.HoneyBadger.Id, "p2"); // costs 2
        var dear = Add(s, DiceKingdomConfig.Elephant.Id, "p2");     // costs 6
        IReadOnlyList<string>? offered = null;
        Field(s, q, Add(s, "FUS004", zone: Zone.ReservePool), c => { offered = c.CandidateIds; return cheap.Id; });
        Assert.Contains(cheap.Id, offered!);
        Assert.DoesNotContain(dear.Id, offered!);
        Assert.Equal(Zone.PrepArea, cheap.Zone);
    }

    [Fact]
    public void Crocodile_Can_Only_KO_A_Level_1_Creature()
    {
        var s = Game(); var q = new AbilityQueue();
        var young = Add(s, DiceKingdomConfig.Elephant.Id, "p2", level: 1);
        var grown = Add(s, DiceKingdomConfig.Elephant.Id, "p2", level: 2);
        IReadOnlyList<string>? offered = null;
        Field(s, q, Add(s, "TOA086", zone: Zone.ReservePool), c => { offered = c.CandidateIds; return young.Id; });
        Assert.DoesNotContain(grown.Id, offered!);
        Assert.Equal(Zone.PrepArea, young.Zone);
    }

    [Fact]
    public void Swordfish_Weakens_An_Opposing_Creature_And_Strengthens_Itself()
    {
        var s = Game(); var q = new AbilityQueue();
        var target = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var targetAtk = QueryEngine.GetAttack(s, target);
        var swordfish = Add(s, "ASM110"); // L1 4/5
        Attack(s, q, swordfish);
        Assert.Equal(6, QueryEngine.GetAttack(s, swordfish));
        Assert.Equal(Math.Max(0, targetAtk - 2), QueryEngine.GetAttack(s, target));
    }

    [Fact]
    public void Bull_Shark_Gets_2_ATK_When_It_Attacks()
    {
        var s = Game(); var q = new AbilityQueue();
        var shark = Add(s, "THOR021"); // L1 3/3
        Attack(s, q, shark);
        Assert.Equal(5, QueryEngine.GetAttack(s, shark));
    }

    [Fact]
    public void Bottlenose_Dolphin_Gives_The_Other_Attackers_1_ATK()
    {
        var s = Game(); var q = new AbilityQueue();
        var dolphin = Add(s, "CW042"); // L1 4/4
        var other = Add(s, DiceKingdomConfig.Elephant.Id);
        var stayedHome = Add(s, DiceKingdomConfig.Hippopotamus.Id);
        var otherAtk = QueryEngine.GetAttack(s, other);
        var homeAtk = QueryEngine.GetAttack(s, stayedHome);
        Attack(s, q, dolphin, other);
        Assert.Equal(4, QueryEngine.GetAttack(s, dolphin));
        Assert.Equal(otherAtk + 1, QueryEngine.GetAttack(s, other));
        Assert.Equal(homeAtk, QueryEngine.GetAttack(s, stayedHome));
    }

    [Theory]
    [InlineData("GOTG021", Zone.PrepArea)]   // Chipmunk
    [InlineData("IG068", Zone.ReservePool)]  // Hare
    public void Drawing_Creatures_Draw_One_Die_Where_They_Say(string cardId, Zone to)
    {
        var s = Game(); var q = new AbilityQueue();
        var bag = s.DiceIn("p1", Zone.Bag).Count();
        var there = s.DiceIn("p1", to).Count();
        Field(s, q, Add(s, cardId, zone: Zone.ReservePool));
        Assert.Equal(bag - 1, s.DiceIn("p1", Zone.Bag).Count());
        Assert.Equal(there + 1, s.DiceIn("p1", to).Count());
    }
}
