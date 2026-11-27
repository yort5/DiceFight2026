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

    // Game e1df2399 (2026-11-25): Eagle's lane of Phoenix (4/5, declared
    // first) + a 1/1 Tardigrade. The computer chumped it with its only 2/1
    // Tardigrade "to KO the Tardigrade" - but a blocker's damage goes down
    // the lane in declaration order, lethal-first (CombatEngine.AutoSplit),
    // so all 2 went into Phoenix. Two attackers mean Overcrush: the chump
    // stopped 1 damage for its life.
    [Fact]
    public void A_Chump_Does_Not_Count_On_A_KO_The_Damage_Split_Wont_Give_It()
    {
        var state = NewGame("GoldenEagle", "Wolf");
        var queue = new AbilityQueue();
        state.GetPlayer("p2").Life = 16;
        var phoenix = state.Dice.First(d => d.OwnerId == "p1" && d.CardId == DiceKingdomConfig.Phoenix.Id);
        phoenix.Zone = Zone.FieldZone; phoenix.CurrentFaceIndex = 1;
        var small = state.Dice.First(d => d.OwnerId == "p1" && d.CardId is null);
        small.Zone = Zone.FieldZone; small.CurrentFaceIndex = 3; // L2 1/1
        // A lane takes a second attacker only when another lane has one.
        var other = state.Dice.Where(d => d.OwnerId == "p1" && d.CardId is null).Skip(1).First();
        other.Zone = Zone.FieldZone; other.CurrentFaceIndex = 4; // L2 2/1
        var mine = state.Dice.First(d => d.OwnerId == "p2" && d.CardId is null);
        mine.Zone = Zone.FieldZone; mine.CurrentFaceIndex = 4; // L2 2/1
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [phoenix.Id] = 0, [small.Id] = 0, [other.Id] = 1 });
        Assert.True(QueryEngine.GetDefense(state, phoenix) > 2, "setup: Phoenix survives 2");

        var decision = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.DeclareBlockers, decision.Kind);
        Assert.DoesNotContain(decision.Blocks, b => b.Item1 == phoenix.Id || b.Item1 == small.Id);
    }

    // Game e1df2399 turn 9 (2026-11-25): the computer's Wolf at 7 life,
    // Eagle attacking with two Overcrush lanes - Phoenix 4/5 + Tardigrade
    // 1/1 (5) and Chipmunk 2/4 + Tardigrade 2/1 (4). It chumped each lane
    // with one 1-DEF Tardigrade and took exactly 7, leaving Wolverine 2/3
    // and a 1/1 Tardigrade home. Ganging up lets it live.
    [Fact]
    public void Blocks_To_Survive_Two_Overcrush_Lanes()
    {
        var state = NewGame("GoldenEagle", "Wolf");
        var queue = new AbilityQueue();
        state.GetPlayer("p2").Life = 7;
        DieInstance Put(string owner, string? cardId, int face, int skip = 0)
        {
            var d = state.Dice.Where(x => x.OwnerId == owner && x.CardId == cardId && x.Zone != Zone.FieldZone).Skip(skip).First();
            d.Zone = Zone.FieldZone; d.CurrentFaceIndex = face;
            return d;
        }
        var phoenix = Put("p1", DiceKingdomConfig.Phoenix.Id, 1);  // 4/5
        var chipmunk = Put("p1", "GOTG021", 1);                      // 2/4
        var t11 = Put("p1", null, 3);                                // 1/1
        var t21 = Put("p1", null, 4);                                // 2/1
        Put("p2", null, 4); Put("p2", null, 4); Put("p2", null, 3);   // Tardigrades 2/1, 2/1, 1/1
        Put("p2", DiceKingdomConfig.Wolverine.Id, 0);                // 2/3
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [phoenix.Id] = 0, [chipmunk.Id] = 1, [t11.Id] = 0, [t21.Id] = 1 });

        var decision = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.DeclareBlockers, decision.Kind);
        var assignment = new CombatAssignment();
        foreach (var (attacker, blocker) in decision.Blocks) assignment.AssignBlocker(attacker, blocker);
        CombatEngine.DeclareBlockers(state, queue, assignment, decision.Blocks.Select(b => b.Item2).ToList());
        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        Assert.True(state.GetPlayer("p2").Life > 0, $"life {state.GetPlayer("p2").Life} after {decision.Reason}");
    }

    // Game 7e9ce89c (2026-11-25): the Aggressive Fire Ant Colony boss swung
    // two 2/1 Tardigrades into an Owl with two 1/4 Cuttlefish and a 1/3
    // Opossum ready to block - each a free kill. Aggressive takes trades,
    // not free kills.
    [Fact]
    public void Even_An_Aggressive_Bot_Does_Not_Swing_Into_A_Free_Kill()
    {
        var state = NewGame("GreatHornedOwl", "Wolf");
        state.ActivePlayerId = "p2";
        state.MoveToStep(StepIds.SelectAttackers);
        var attacker = state.Dice.First(d => d.OwnerId == "p2" && d.CardId is null);
        attacker.Zone = Zone.FieldZone; attacker.CurrentFaceIndex = 4; // 2/1
        // One of the Owl's creatures on a face that KOs a 2/1 and survives it.
        var (wall, face) = state.Dice.Where(d => d.OwnerId == "p1" && d.CardId is { } c && state.CardCatalog[c].CardType == CardType.Character)
            .Select(d => (Die: d, Face: state.CardCatalog[d.CardId!].Die.Faces.ToList().FindIndex(f => f.Character is { Attack: >= 1, Defense: >= 3 })))
            .First(x => x.Face >= 0);
        wall.Zone = Zone.FieldZone; wall.CurrentFaceIndex = face;

        var aggressive = BotPersona.Default with { Aggression = 2, ChumpThreshold = 3 };
        var decision = DiceKingdomBot.Decide(state, "p2", persona: aggressive)!;
        Assert.Equal(BotActionKind.DeclareAttackers, decision.Kind);
        Assert.DoesNotContain(attacker.Id, decision.AttackerLanes.Keys);
    }

    // Game 6243b9b2 (2026-11-26): Armadillo's level-1 Pangolin locked the
    // computer's Owl out of its Basilisk, and the Owl kept a Cuttlefish
    // ("On Attack: spin a target opposing level 1 creature to an energy
    // face") home. Swung, it frees the Basilisk - and its spin should take
    // the Pangolin, not a Musk Ox worth more on stats alone.
    [Fact]
    public void Swings_An_On_Attack_Remover_At_A_Pangolin_Lockout()
    {
        Player Build(string id, string champion, params string[] cards)
        {
            var p = new Player { Id = id, Name = champion, ChampionId = champion };
            p.TeamCardIds.AddRange(cards);
            return p;
        }
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog,
            Build("p1", "Armadillo", DiceKingdomConfig.Pangolin.Id, DiceKingdomConfig.MuskOx.Id),
            Build("p2", "GreatHornedOwl", DiceKingdomConfig.Cuttlefish.Id, DiceKingdomConfig.Basilisk.Id));
        Priority.Sync(state);
        var queue = new AbilityQueue();
        DieInstance Put(string owner, string cardId, int face)
        {
            var d = state.Dice.First(x => x.OwnerId == owner && x.CardId == cardId && x.Zone != Zone.FieldZone);
            d.Zone = Zone.FieldZone; d.CurrentFaceIndex = face;
            return d;
        }
        var pangolin = Put("p1", DiceKingdomConfig.Pangolin.Id, 0); // L1 1/3
        Put("p1", DiceKingdomConfig.MuskOx.Id, 0);                  // L1 2/4
        var cuttlefish = Put("p2", DiceKingdomConfig.Cuttlefish.Id, 1); // L2 1/3
        state.Memories[("p1", DiceKingdomConfig.Pangolin.Id, "locked")] = DiceKingdomConfig.Basilisk.Id;
        state.ActivePlayerId = "p2";
        state.MoveToStep(StepIds.SelectAttackers);

        var attack = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.DeclareAttackers, attack.Kind);
        Assert.Contains(cuttlefish.Id, attack.AttackerLanes.Keys);

        CombatEngine.DeclareAttackers(state, queue, attack.AttackerLanes);
        EffectInterpreter.DrainQueue(state, queue, new RandomDiceRoller(new Random(1)), new Random(1));
        Assert.NotNull(state.PendingChoice);
        var pick = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.ResolvePendingChoice, pick.Kind);
        Assert.Equal([pangolin.Id], pick.DieIds);
    }

    // User, 2026-11-27: Mutation "can be played defensively - but probably
    // should have used it to remove a Rhino of mine". With the original
    // wording it can: the computer swaps the Rhinoceros out for the weakest
    // creature in its owner's Used Pile.
    [Fact]
    public void Mutation_Swaps_Out_An_Opposing_Rhinoceros_For_Their_Weakest()
    {
        Player Build(string id, string champion, params string[] cards)
        {
            var p = new Player { Id = id, Name = champion, ChampionId = champion };
            p.TeamCardIds.AddRange(cards);
            return p;
        }
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog,
            Build("p1", "Armadillo", DiceKingdomConfig.Rhinoceros.Id, DiceKingdomConfig.HermitCrab.Id, DiceKingdomConfig.MuskOx.Id),
            Build("p2", "GreatHornedOwl", DiceKingdomConfig.Mutation.Id));
        Priority.Sync(state);
        var queue = new AbilityQueue();
        DieInstance Put(string owner, string cardId, Zone zone, int face)
        {
            var d = state.Dice.First(x => x.OwnerId == owner && x.CardId == cardId && x.Zone == Zone.Unpurchased);
            d.Zone = zone; d.CurrentFaceIndex = face;
            return d;
        }
        var rhino = Put("p1", DiceKingdomConfig.Rhinoceros.Id, Zone.FieldZone, 2); // L3 3/9
        var crab = Put("p1", DiceKingdomConfig.HermitCrab.Id, Zone.UsedPile, 0);
        Put("p1", DiceKingdomConfig.MuskOx.Id, Zone.UsedPile, 0);
        var mutation = Put("p2", DiceKingdomConfig.Mutation.Id, Zone.ReservePool, 0); // an action face
        state.ActivePlayerId = "p2";
        state.MoveToStep(StepIds.Main);
        Priority.Sync(state);

        var use = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.UseAction, use.Kind);
        Assert.Equal(mutation.Id, use.DieId);
        TurnEngine.UseAction(state, queue, mutation.Id);
        EffectInterpreter.DrainQueue(state, queue, new RandomDiceRoller(new Random(1)), new Random(1));
        for (var i = 0; i < 2 && state.PendingChoice is not null; i++)
        {
            var answer = DiceKingdomBot.Decide(state, "p2")!;
            EffectInterpreter.AnswerPendingChoice(state, answer.DieIds);
            EffectInterpreter.DrainQueue(state, queue, new RandomDiceRoller(new Random(1)), new Random(1));
        }

        Assert.Equal(Zone.UsedPile, rhino.Zone);
        Assert.Equal(Zone.FieldZone, crab.Zone);
    }
}
