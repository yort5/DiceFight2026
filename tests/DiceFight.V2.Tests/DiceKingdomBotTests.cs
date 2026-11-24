using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// The shared computer opponent (Bot/DiceKingdomBot.cs) - the same policy
// the web client's "vs computer" seat and tools/Simulator both run.
public class DiceKingdomBotTests
{
    private static GameState NewGame(string one, string two)
    {
        Player Build(string id, string champion)
        {
            var p = new Player { Id = id, Name = champion, ChampionId = champion };
            p.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[champion]);
            p.TeamCardIds.Add(DiceKingdomConfig.ActionByChampion[champion]);
            return p;
        }
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, Build("p1", one), Build("p2", two));
        Priority.Sync(state);
        return state;
    }

    // Bot vs bot through BotDriver for every Champion pairing: every
    // decision must be one the engine accepts (the bot is supposed to
    // only offer legal moves). Most games end; a few can genuinely stall
    // (walled-off boards, or a player with every die on the Field and
    // nothing left to draw - there's no deck-out rule yet), so a turn cap
    // ends those rather than failing the test.
    [Theory]
    [InlineData("Wolf", "Armadillo")]
    [InlineData("Wolf", "GreatHornedOwl")]
    [InlineData("Wolf", "GoldenEagle")]
    [InlineData("Armadillo", "GreatHornedOwl")]
    [InlineData("Armadillo", "GoldenEagle")]
    [InlineData("GreatHornedOwl", "GoldenEagle")]
    public void Bot_Plays_Every_Pairing_With_Only_Legal_Moves(string one, string two)
    {
        var finished = 0;
        for (var seed = 0; seed < 5; seed++)
        {
            var state = NewGame(one, two);
            var driver = new BotDriver(state, new Random(seed));
            var turns = 0;
            var lastActive = state.ActivePlayerId;
            while (state.PlayerOne.Life > 0 && state.PlayerTwo.Life > 0 && turns < 80)
            {
                var owner = driver.DecisionOwner();
                var decision = DiceKingdomBot.Decide(state, owner);
                Assert.NotNull(decision);
                driver.Apply(owner, decision!); // throws if the engine rejects the move
                if (state.ActivePlayerId != lastActive) { lastActive = state.ActivePlayerId; turns++; }
            }
            if (state.PlayerOne.Life <= 0 || state.PlayerTwo.Life <= 0) finished++;
        }
        // Legality is the point of this test. Stalls are a real game-design
        // finding, tracked by tools/Simulator's turn-cap count rather than
        // gated here: once the bot started playing Rhinoceros as a
        // retaliating blocker (2026-09-29), Armadillo vs Owl began walling
        // up for 80+ turns in some seeds - see v3/DESIGN_NOTES.md.
        Assert.True(finished >= 1, $"no game finished within 80 turns");
    }

    [Fact]
    public void Only_The_Player_Whose_Decision_It_Is_Gets_One()
    {
        var state = NewGame("Wolf", "Armadillo");
        Assert.Equal(BotActionKind.ClearAndDraw, DiceKingdomBot.Decide(state, "p1")!.Kind);
        Assert.Null(DiceKingdomBot.Decide(state, "p2"));
    }

    // A harmful choice goes at the opponent's best creature, not this
    // player's own (the old simulator bot answered with random picks).
    [Fact]
    public void Harmful_Choice_Targets_The_Opponents_Best_Creature()
    {
        var state = NewGame("Wolf", "Armadillo");
        var mine = state.Dice.First(d => d.OwnerId == "p1" && d.CardId is null);
        var theirs = state.Dice.Where(d => d.OwnerId == "p2" && d.CardId is null).Take(2).ToList();
        foreach (var d in theirs.Append(mine)) { d.Zone = Zone.FieldZone; d.CurrentFaceIndex = 3; } // 1/1
        theirs[1].CurrentFaceIndex = 4; // L2 2/1 - the more valuable one
        string[]? answer = null;
        state.PendingChoice = new PendingChoice
        {
            ControllerId = "p1",
            Description = "Deal 1 damage to a target creature.",
            CandidateIds = [mine.Id, theirs[0].Id, theirs[1].Id],
            MinCount = 1,
            MaxCount = 1,
            Intent = ChoiceIntent.Harmful,
            Resolve = ids => answer = [.. ids],
        };

        var decision = DiceKingdomBot.Decide(state, "p1")!;

        Assert.Equal(BotActionKind.ResolvePendingChoice, decision.Kind);
        Assert.Equal([theirs[1].Id], decision.DieIds);
    }

    // A Character die that rolled energy gets rerolled for a body.
    [Fact]
    public void Rerolls_A_Character_Die_Showing_Energy()
    {
        var state = NewGame("Wolf", "Armadillo");
        state.MoveToStep(StepIds.RollAndReroll);
        var badger = state.Dice.First(d => d.OwnerId == "p1" && d.CardId == DiceKingdomConfig.HoneyBadger.Id);
        badger.Zone = Zone.ReservePool;
        badger.ControllerId = "p1";
        badger.CurrentFaceIndex = 3; // an energy face

        var decision = DiceKingdomBot.Decide(state, "p1")!;

        Assert.Equal(BotActionKind.Reroll, decision.Kind);
        Assert.Contains(badger.Id, decision.DieIds);
    }

    // Crack-back by what the opponent is likely to bring next turn, not just
    // what's on their Field now (user, 2026-10-04: "keep enough blockers so
    // that I can block the higher attack characters I expect my opponent to
    // have next turn"). Their Field is empty in both cases.
    // A real game (2026-11-22): the computer's Eagle, at 14 life with a
    // 2/1 Tardigrade on the Field, let a lone Wolverine through - and Wolf
    // pumped it +3 after blocks for 6. Blocking a lone attacker keeps the
    // pump off its life too, and a free Tardigrade is a cheap chump.
    [Fact]
    public void Bot_Chumps_A_Lone_Attacker_That_Wolf_Can_Still_Pump()
    {
        var state = NewGame("Wolf", "GoldenEagle");
        var queue = new AbilityQueue();
        state.GetPlayer("p2").Life = 14;
        var wolverine = state.Dice.First(d => d.OwnerId == "p1" && d.CardId == DiceKingdomConfig.Wolverine.Id);
        wolverine.Zone = Zone.FieldZone; wolverine.CurrentFaceIndex = 1; // L2 3/4
        var tardigrade = state.Dice.First(d => d.OwnerId == "p2" && d.CardId is null);
        tardigrade.Zone = Zone.FieldZone; tardigrade.CurrentFaceIndex = 4; // L2 2/1
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [wolverine.Id]);

        var decision = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.DeclareBlockers, decision.Kind);
        Assert.Contains((wolverine.Id, tardigrade.Id), decision.Blocks);
    }

    private static (GameState State, DieInstance Tardigrade) AboutToAttack()
    {
        var state = NewGame("Armadillo", "GreatHornedOwl");
        state.ActivePlayerId = "p1";
        state.MoveToStep(StepIds.SelectAttackers);
        var die = new DieInstance
        {
            Id = "p1-swinger", PoolDieId = "TardigradeShell", OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, CurrentFaceIndex = 3, // L2: 1/1
        };
        state.Dice.Add(die);
        return (state, die);
    }

    [Fact]
    public void Swings_When_The_Opponent_Has_Only_Tardigrades_Coming()
    {
        var (state, die) = AboutToAttack(); // their Bag: 8 Tardigrades, nothing else
        var decision = DiceKingdomBot.Decide(state, "p1")!;
        Assert.Equal(BotActionKind.DeclareAttackers, decision.Kind);
        Assert.Contains(die.Id, decision.AttackerLanes.Keys);
    }

    [Fact]
    public void Keeps_A_Blocker_Home_When_A_Big_Creature_Is_Coming_Back_From_Prep()
    {
        var (state, die) = AboutToAttack();
        // A Silverback KO'd last turn: certain to be rolled next turn.
        state.Dice.Add(new DieInstance
        {
            Id = "p2-silverback", CardId = DiceKingdomConfig.Silverback.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.PrepArea,
        });
        var decision = DiceKingdomBot.Decide(state, "p1")!;
        Assert.Equal(BotActionKind.DeclareAttackers, decision.Kind);
        Assert.DoesNotContain(die.Id, decision.AttackerLanes.Keys);
    }

    // The hold-Tardigrades experiment (BotPersona.HoldWeakTardigrades, off
    // by default - user, 2026-11-16): the bot had a 3/9 and a 1/2
    // Tardigrade against an empty Field and swung the Tardigrade.
    private static readonly BotPersona Holding = BotPersona.Default with { HoldWeakTardigrades = true };

    [Fact]
    public void Holding_Keeps_A_Tardigrade_Home_Rather_Than_Swing_For_One()
    {
        var (state, die) = AboutToAttack();
        var decision = DiceKingdomBot.Decide(state, "p1", persona: Holding)!;
        Assert.Equal(BotActionKind.DeclareAttackers, decision.Kind);
        Assert.DoesNotContain(die.Id, decision.AttackerLanes.Keys);
    }

    [Fact]
    public void Holding_Still_Swings_A_Tardigrade_For_One_When_It_Ends_The_Game()
    {
        var (state, die) = AboutToAttack();
        state.GetPlayer("p2").Life = 1;
        var decision = DiceKingdomBot.Decide(state, "p1", persona: Holding)!;
        Assert.Contains(die.Id, decision.AttackerLanes.Keys);
    }

    // Game 18d54e04 (2026-11-24): Armadillo's Rhinoceros (reflects the damage
    // it takes at the blocker's controller) attacked; Hermit Crab had forced
    // the computer's Hyena to block. The computer added its Opossum too -
    // 2 more reflected damage, for a Rhinoceros that comes straight back.
    private static (GameState State, AbilityQueue Queue, DieInstance Rhino, DieInstance Hyena, DieInstance Opossum) RhinoIntoForcedHyena()
    {
        Player Build(string id, string champion, params string[] cards)
        {
            var p = new Player { Id = id, Name = champion, ChampionId = champion };
            p.TeamCardIds.AddRange(cards);
            return p;
        }
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog,
            Build("p1", "Armadillo", DiceKingdomConfig.Rhinoceros.Id, DiceKingdomConfig.Archnemesis.Id),
            Build("p2", "GreatHornedOwl", DiceKingdomConfig.Hyena.Id, DiceKingdomConfig.Opossum.Id));
        Priority.Sync(state);
        var queue = new AbilityQueue();
        DieInstance Put(string owner, string cardId, int face)
        {
            var d = state.Dice.First(x => x.OwnerId == owner && x.CardId == cardId && x.Zone != Zone.FieldZone);
            d.Zone = Zone.FieldZone; d.CurrentFaceIndex = face;
            return d;
        }
        var rhino = Put("p1", DiceKingdomConfig.Rhinoceros.Id, 2);   // L3 3/9
        var hyena = Put("p2", DiceKingdomConfig.Hyena.Id, 1);        // L2 2/5 (+1 ATK per weak opposing creature)
        var opossum = Put("p2", DiceKingdomConfig.Opossum.Id, 1);    // L2 1/3 Deadly
        hyena.CombatFlags.Add(CombatFlagKind.MustBlock);
        state.GetPlayer("p2").Life = 17;
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [rhino.Id]);
        return (state, queue, rhino, hyena, opossum);
    }

    [Fact]
    public void A_Forced_Blocker_Covers_A_Retaliator_Alone()
    {
        var (state, _, rhino, hyena, opossum) = RhinoIntoForcedHyena();
        var decision = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.DeclareBlockers, decision.Kind);
        Assert.Contains((rhino.Id, hyena.Id), decision.Blocks);
        Assert.DoesNotContain(decision.Blocks, b => b.Item2 == opossum.Id);
    }

    [Fact]
    public void Archnemesis_Global_Only_When_It_Changes_A_Fight()
    {
        var (state, queue, rhino, hyena, _) = RhinoIntoForcedHyena();
        hyena.CurrentFaceIndex = 2; // L3 3/6
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(rhino.Id, hyena.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [hyena.Id]);
        // Rhinoceros (3/9) survives Hyena's ATK; D = A (3) would let Hyena KO it.
        var hyenaAtk = QueryEngine.GetAttack(state, hyena);
        Assert.True(hyenaAtk < 9 && hyenaAtk >= 3, $"setup: Hyena ATK {hyenaAtk}");
        Assert.True(DiceKingdomBot.SetDefenseGain(state, "p2", rhino) > 0);
        // Hyena isn't in danger from a 3-ATK Rhinoceros: raising its D gains nothing.
        Assert.Equal(0, DiceKingdomBot.SetDefenseGain(state, "p2", hyena));
        // Once Rhinoceros dies anyway, lowering its D gains nothing either.
        rhino.Damage = 6; // 3 D left: Hyena's 3 KOs it already
        Assert.Equal(0, DiceKingdomBot.SetDefenseGain(state, "p2", rhino));
    }
}
