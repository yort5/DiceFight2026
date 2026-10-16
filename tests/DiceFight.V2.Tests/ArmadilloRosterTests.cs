using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// 2026-09-29 engine/roster changes: the end-of-Main sweep keeps energy
// faces, Hermit Crab's forced block, and Armadillo's Archnemesis.
public class ArmadilloRosterTests
{
    // Armadillo (p1) vs Wolf (p2), Champion passives zeroed so stats are
    // the printed ones.
    // `kreeGlobal`: Musk Ox carries Kree Captain's purchase-discount Global
    // (live 2026-10-04, then taken off; the engine and bot support stay).
    private static GameState NewGame(bool kreeGlobal = false)
    {
        Player Build(string id, string champion)
        {
            var p = new Player { Id = id, Name = champion, ChampionId = champion };
            p.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[champion]);
            p.TeamCardIds.Add(DiceKingdomConfig.ActionByChampion[champion]);
            return p;
        }
        var config = DiceKingdomConfig.Config with
        {
            Champions = DiceKingdomConfig.Config.Champions.Select(c => c with { Amount = 0 }).ToList(),
        };
        var catalog = DiceKingdomConfig.Catalog.ToDictionary(kv => kv.Key, kv => kv.Value);
        if (kreeGlobal) catalog[DiceKingdomConfig.MuskOx.Id] = DiceKingdomConfig.WithKreeCaptainGlobal(DiceKingdomConfig.MuskOx);
        var state = GameSetup.NewGame(config, catalog, Build("p1", "Armadillo"), Build("p2", "Wolf"));
        state.MoveToStep(StepIds.Main);
        return state;
    }

    private static DieInstance Place(GameState state, string owner, Func<DieInstance, bool> which, Zone zone, int face)
    {
        var die = state.Dice.First(d => d.OwnerId == owner && which(d) && d.Zone is Zone.Bag or Zone.Unpurchased);
        die.Zone = zone;
        die.ControllerId = owner;
        die.CurrentFaceIndex = face;
        return die;
    }

    private static void Drain(GameState state, AbilityQueue queue) =>
        EffectInterpreter.DrainQueue(state, queue, new RandomDiceRoller(new Random(1)), new Random(1));

    [Fact]
    public void Main_End_Sweeps_Stats_Only_Faces_But_Keeps_Any_Face_With_Energy()
    {
        var state = NewGame();
        var hybrid = Place(state, "p1", d => d.CardId is null, Zone.ReservePool, 3);  // Tardigrade L2: 1/1 + a Wild
        var bulwark = Place(state, "p1", d => d.CardId is null, Zone.ReservePool, 4); // Tardigrade Bulwark: 1/2, no energy
        var crab = Place(state, "p1", d => d.CardId == DiceKingdomConfig.HermitCrab.Id, Zone.ReservePool, 0); // stats only

        TurnEngine.EnterAttackStep(state, new AbilityQueue());

        Assert.Equal(Zone.ReservePool, hybrid.Zone);
        Assert.Equal(Zone.UsedPile, bulwark.Zone);
        Assert.Equal(Zone.UsedPile, crab.Zone);
    }

    [Fact]
    public void Hermit_Crab_Forces_A_Block_But_Only_When_There_Is_Something_To_Block()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var target = Place(state, "p2", d => d.CardId == DiceKingdomConfig.HoneyBadger.Id, Zone.FieldZone, 1);
        var crab = Place(state, "p1", d => d.CardId == DiceKingdomConfig.HermitCrab.Id, Zone.ReservePool, 0);

        TurnEngine.Field(state, queue, crab.Id, []);
        Drain(state, queue);
        if (state.PendingChoice is { } choice) EffectInterpreter.AnswerPendingChoice(state, [target.Id]);
        Assert.Contains(CombatFlagKind.MustBlock, target.CombatFlags);

        TurnEngine.EnterAttackStep(state, queue);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [crab.Id] = 0 });
        Assert.Throws<InvalidOperationException>(() =>
            CombatEngine.DeclareBlockers(state, queue, new CombatAssignment(), []));

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(crab.Id, target.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [target.Id]);
    }

    [Fact]
    public void A_Forced_Blocker_With_No_Attackers_Doesnt_Block_The_Empty_Declaration()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var target = Place(state, "p2", d => d.CardId == DiceKingdomConfig.HoneyBadger.Id, Zone.FieldZone, 1);
        target.CombatFlags.Add(CombatFlagKind.MustBlock);

        TurnEngine.EnterAttackStep(state, queue);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int>());
        CombatEngine.DeclareBlockers(state, queue, new CombatAssignment(), []); // no throw
    }

    // "Deal damage to each other" is simultaneous: Honey Badger (2/2) is
    // KO'd by Hummingbird's 2, and still hits back for the 2 ATK it had.
    [Fact]
    public void Archnemesis_Fights_Simultaneously()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var mine = Place(state, "p1", d => d.CardId == DiceKingdomConfig.Hummingbird.Id, Zone.FieldZone, 1);  // L2: 2/3
        var theirs = Place(state, "p2", d => d.CardId == DiceKingdomConfig.HoneyBadger.Id, Zone.FieldZone, 1); // L2: 2/2
        var action = Place(state, "p1", d => d.CardId == DiceKingdomConfig.Archnemesis.Id, Zone.ReservePool, 0);

        TurnEngine.UseAction(state, queue, action.Id);
        Drain(state, queue);

        Assert.Null(state.PendingChoice); // one candidate each side - no choice needed
        Assert.Equal(Zone.PrepArea, theirs.Zone); // KO'd
        Assert.Equal(Zone.FieldZone, mine.Zone);
        Assert.Equal(2, mine.Damage); // ...but it still hit back
    }

    // Kree Captain's Global on Musk Ox (2026-10-04, since taken off the live
    // card): pay 2 Shell, the next creature this turn costs 3 less (min 1).
    private static int MuskOxGlobal => DiceKingdomConfig.WithKreeCaptainGlobal(DiceKingdomConfig.MuskOx).Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);

    private static DieInstance Shell(GameState state, int face) => Place(state, "p1", d => d.CardId is null, Zone.ReservePool, face); // 0 = 2 Shell, 2 = 1 Shell

    [Fact]
    public void Musk_Ox_Global_Takes_3_Off_The_Next_Creature_Only()
    {
        var state = NewGame(kreeGlobal: true);
        var queue = new AbilityQueue();
        var payGlobal = Shell(state, 0);
        var pay = new[] { Shell(state, 0), Shell(state, 2) }; // 3 Shell - Rhinoceros costs 6

        TurnEngine.UseGlobal(state, queue, DiceKingdomConfig.MuskOx.Id, "p1", MuskOxGlobal, [payGlobal.Id]);
        Drain(state, queue);
        Assert.Equal(3, QueryEngine.GetPurchaseCostNow(state, DiceKingdomConfig.Rhinoceros, "p1"));
        Assert.Equal(1, QueryEngine.GetPurchaseCostNow(state, DiceKingdomConfig.HermitCrab, "p1")); // 2 - 3, minimum 1

        var rhino = state.Dice.First(d => d.CardId == DiceKingdomConfig.Rhinoceros.Id && d.Zone == Zone.Unpurchased);
        TurnEngine.Purchase(state, queue, rhino.Id, pay.Select(d => d.Id).ToList());

        Assert.Equal(Zone.UsedPile, rhino.Zone);
        Assert.Equal(2, QueryEngine.GetPurchaseCostNow(state, DiceKingdomConfig.HermitCrab, "p1")); // used up
        Assert.Contains(state.Log, l => l.Text.Contains("purchases Rhinoceros for 3 (discounted)"));
        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobal(state, queue, DiceKingdomConfig.MuskOx.Id, "p1", MuskOxGlobal, [Shell(state, 0).Id])); // once per turn
    }

    [Fact]
    public void Musk_Ox_Global_Is_Not_Offered_To_The_Player_Who_Cant_Buy_This_Turn()
    {
        var state = NewGame(kreeGlobal: true); // p1's Main
        var wilds = new[] { Place(state, "p2", d => d.CardId is null, Zone.ReservePool, 3), Place(state, "p2", d => d.CardId is null, Zone.ReservePool, 5) };

        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.UseGlobal(state, new AbilityQueue(), DiceKingdomConfig.MuskOx.Id, "p2", MuskOxGlobal, wilds.Select(d => d.Id).ToList()));
    }

    [Fact]
    public void Bot_Pays_For_The_Discount_When_It_Brings_Rhinoceros_Into_Reach()
    {
        var state = NewGame(kreeGlobal: true);
        Shell(state, 0); Shell(state, 0); Shell(state, 2); // 5 Shell: Rhinoceros (6) is one short

        var decision = Bot.DiceKingdomBot.Decide(state, "p1")!;
        Assert.Equal(Bot.BotActionKind.UseGlobal, decision.Kind);
        Assert.Equal(DiceKingdomConfig.MuskOx.Id, decision.CardId);

        var queue = new AbilityQueue();
        TurnEngine.UseGlobal(state, queue, decision.CardId!, "p1", decision.AbilityIndex, decision.EnergyDieIds);
        Drain(state, queue);
        var next = Bot.DiceKingdomBot.Decide(state, "p1")!;
        Assert.Equal(Bot.BotActionKind.Purchase, next.Kind);
        Assert.Equal(DiceKingdomConfig.Rhinoceros.Id, state.Dice.Single(d => d.Id == next.DieId).CardId);
    }

    [Fact]
    public void Bot_Skips_The_Discount_When_It_Buys_Nothing_More()
    {
        var state = NewGame(kreeGlobal: true);
        state.Dice.First(d => d.CardId == DiceKingdomConfig.Rhinoceros.Id && d.Zone == Zone.Unpurchased).Zone = Zone.UsedPile; // no longer racing to it
        Shell(state, 0); // 2 Shell: paying for the Global leaves nothing to buy with

        var decision = Bot.DiceKingdomBot.Decide(state, "p1")!;
        Assert.NotEqual(Bot.BotActionKind.UseGlobal, decision.Kind);
    }

    // Hermit Crab + Rhinoceros (user, 2026-10-04): with a forced blocker on
    // their side, Rhinoceros leads one lane and everything else attacking
    // stacks behind it - the forced blocker has to block it, Rhinoceros
    // soaks the blockers' damage first, and 2+ attackers have Overcrush.
    [Fact]
    public void Bot_Stacks_Its_Attack_Behind_Rhinoceros_Into_A_Forced_Blocker()
    {
        var state = NewGame();
        state.MoveToStep(StepIds.SelectAttackers);
        var rhino = Place(state, "p1", d => d.CardId == DiceKingdomConfig.Rhinoceros.Id, Zone.FieldZone, 1); // L2 2/7
        Place(state, "p1", d => d.CardId == DiceKingdomConfig.CapeBuffalo.Id, Zone.FieldZone, 2); // L3 7/7
        Place(state, "p1", d => d.CardId == DiceKingdomConfig.CapeBuffalo.Id, Zone.FieldZone, 2);
        var forced = Place(state, "p2", d => d.CardId is not null && state.CardCatalog[d.CardId].CardType == CardType.Character, Zone.FieldZone, 0);
        forced.CombatFlags.Add(CombatFlagKind.MustBlock);

        var decision = Bot.DiceKingdomBot.Decide(state, "p1")!;

        // Three attackers: the stacking rule lets one join Rhinoceros's lane
        // only with the other in a lane of its own.
        Assert.Equal(Bot.BotActionKind.DeclareAttackers, decision.Kind);
        Assert.Equal(rhino.Id, decision.AttackerLanes.Keys.First()); // declared first: takes blocker damage first
        Assert.Equal(3, decision.AttackerLanes.Count);
        Assert.Equal(2, decision.AttackerLanes.Values.Count(lane => lane == decision.AttackerLanes[rhino.Id]));
        Assert.True(CombatEngine.LanesAllowed(decision.AttackerLanes.Values));
    }

    // Armadillo's shield prevents the damage Rhinoceros would send back, so
    // the bot doesn't spend it there - but still does on any other creature.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bot_Never_Shields_Rhinoceros(bool rhinoAttacks)
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = rhinoAttacks
            ? Place(state, "p1", d => d.CardId == DiceKingdomConfig.Rhinoceros.Id, Zone.FieldZone, 0)  // L1 1/5
            : Place(state, "p1", d => d.CardId == DiceKingdomConfig.MuskOx.Id, Zone.FieldZone, 0);      // L1 2/4
        var blocker = Place(state, "p2", d => d.CardId is not null && state.CardCatalog[d.CardId].CardType == CardType.Character, Zone.FieldZone, 2);
        blocker.AppliedModifiers.Add(new AppliedModifier(10, 0, 0, "test", Duration.EndOfTurn)); // lethal to either
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        Drain(state, queue);
        Priority.Sync(state);

        var decision = Bot.DiceKingdomBot.Decide(state, "p1")!;

        if (rhinoAttacks) Assert.NotEqual(Bot.BotActionKind.UseChampionPower, decision.Kind);
        else Assert.Equal(Bot.BotActionKind.UseChampionPower, decision.Kind);
    }
}
