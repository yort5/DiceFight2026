using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using DiceFight.V2.Model;

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
        theirs[1].CurrentFaceIndex = 4; // Bulwark 1/2 - the more valuable one
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
}
