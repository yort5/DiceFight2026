using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// Champion once-per-turn powers (2026-10-04, replacing the always-on
// passives): Wolf +3 ATK to one creature, Armadillo shields one creature
// from combat damage after blocks, Golden Eagle fields one creature free,
// Great Horned Owl spins one of its own creatures up a level.
public class ChampionPowersTests
{
    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static void Drain(GameState state, AbilityQueue queue) =>
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

    private static GameState NewGame(string p1Champion, string p2Champion)
    {
        var one = new Player { Id = "p1", Name = "One", ChampionId = p1Champion };
        one.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[p1Champion]);
        var two = new Player { Id = "p2", Name = "Two", ChampionId = p2Champion };
        two.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[p2Champion]);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, one, two);
        state.TurnNumber = ChampionPowers.FirstPowerTurn; // past the first-turn lockout
        state.ActivePlayerId = "p1";
        state.MoveToStep(StepIds.Main);
        Priority.Sync(state);
        return state;
    }

    private static DieInstance AddDie(GameState state, string cardId, string controllerId, Zone zone, int level = 1, string tag = "")
    {
        var die = new DieInstance
        {
            Id = $"{controllerId}-{cardId}-{zone}{tag}", CardId = cardId, OwnerId = controllerId,
            ControllerId = controllerId, Zone = zone, CurrentFaceIndex = level - 1,
        };
        state.Dice.Add(die);
        return die;
    }

    // p1 attacks with `attacker` into `blocker`; leaves the game in the
    // action window with p1 holding priority.
    private static CombatAssignment Fight(GameState state, AbilityQueue queue, DieInstance attacker, DieInstance blocker)
    {
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        Drain(state, queue);
        Priority.Sync(state);
        return assignment;
    }

    [Fact]
    public void Wolf_Gives_One_Creature_Plus_3_ATK_Once_Per_Turn()
    {
        var state = NewGame("Wolf", "Armadillo");
        var queue = new AbilityQueue();
        var badger = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone);
        var before = QueryEngine.GetAttack(state, badger);

        ChampionPowers.Use(state, queue, "p1");
        Drain(state, queue);

        Assert.Equal(before + 3, QueryEngine.GetAttack(state, badger));
        Assert.Throws<InvalidOperationException>(() => ChampionPowers.Use(state, queue, "p1"));
        Assert.False(ChampionPowers.CanUse(state, "p2")); // not their turn
    }

    [Fact]
    public void Armadillo_Shield_Saves_A_Blocker_From_Lethal_Combat_Damage_And_Gets_Priority_For_It()
    {
        var state = NewGame("Wolf", "Armadillo");
        var queue = new AbilityQueue();
        var attacker = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3); // 5 ATK
        var blocker = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone); // 2 DEF
        Assert.False(ChampionPowers.CanUse(state, "p2")); // Main: nothing in combat yet

        Fight(state, queue, attacker, blocker);
        Assert.True(ChampionPowers.CanUse(state, "p2"));
        Priority.Pass(state, queue, "p1"); // the attacker passes - Armadillo is offered priority, not skipped
        Assert.Equal("p2", state.PriorityPlayerId);

        ChampionPowers.Use(state, queue, "p2");
        Drain(state, queue);
        Assert.Contains(CombatFlagKind.PreventCombatDamage, blocker.CombatFlags);
        Assert.Contains(DieStatuses.For(state, blocker), s => s.Kind == "protected" && s.Source == "Armadillo's power");

        Priority.AfterGlobal(state, "p2");
        Priority.Pass(state, queue, "p1"); // closes the window: combat damage
        Drain(state, queue);
        Assert.Equal(Zone.FieldZone, blocker.Zone); // survived 5 damage on 2 DEF
        Assert.Equal(0, blocker.Damage);
    }

    [Fact]
    public void Golden_Eagle_Fields_One_Creature_Free_Once_Per_Turn()
    {
        var state = NewGame("GoldenEagle", "Wolf");
        var queue = new AbilityQueue();
        var phoenix = AddDie(state, DiceKingdomConfig.Phoenix.Id, "p1", Zone.ReservePool);
        var second = AddDie(state, DiceKingdomConfig.Osprey.Id, "p1", Zone.ReservePool);
        Assert.True(ChampionPowers.CanFieldFree(state, "p1"));

        TurnEngine.Field(state, queue, phoenix.Id, [], free: true);

        Assert.Equal(Zone.FieldZone, phoenix.Zone);
        Assert.False(ChampionPowers.CanFieldFree(state, "p1"));
        Assert.Throws<InvalidOperationException>(() => TurnEngine.Field(state, queue, second.Id, [], free: true));
        Assert.Contains(state.Log, l => l.Text.Contains("for free (Golden Eagle)"));
    }

    // Owl's power spins only its own creatures, and only up (2026-11-22,
    // user: consistent with the other Champions' powers). It used to spin
    // an opposing creature down too.
    [Fact]
    public void Owl_Spins_One_Of_Its_Own_Creatures_Up_A_Level()
    {
        var state = NewGame("GreatHornedOwl", "Wolf");
        var queue = new AbilityQueue();
        var mine = AddDie(state, DiceKingdomConfig.Magpie.Id, "p1", Zone.FieldZone, level: 1);
        var other = AddDie(state, DiceKingdomConfig.Hyena.Id, "p1", Zone.FieldZone, level: 2);
        var top = AddDie(state, DiceKingdomConfig.Basilisk.Id, "p1", Zone.FieldZone, level: 3);
        var theirs = AddDie(state, DiceKingdomConfig.Mongoose.Id, "p2", Zone.FieldZone, level: 3);

        ChampionPowers.Use(state, queue, "p1");
        Drain(state, queue);
        Assert.Equal(new[] { mine.Id, other.Id }.Order(), state.PendingChoice!.CandidateIds.Order()); // not top, not theirs
        EffectInterpreter.AnswerPendingChoice(state, [mine.Id]);
        Drain(state, queue);
        Assert.Equal(2, state.GetCurrentFace(mine)!.Character!.Level);
        Assert.Equal(3, state.GetCurrentFace(theirs)!.Character!.Level);
        Assert.Equal("Great Horned Owl's power", mine.LastSpin!.Source);
    }

    [Fact]
    public void Owl_Cant_Use_Its_Power_With_Nothing_Of_Its_Own_That_Can_Go_Up()
    {
        var state = NewGame("GreatHornedOwl", "Wolf");
        AddDie(state, DiceKingdomConfig.Basilisk.Id, "p1", Zone.FieldZone, level: 3);
        AddDie(state, DiceKingdomConfig.Mongoose.Id, "p2", Zone.FieldZone, level: 1);

        Assert.False(ChampionPowers.CanUse(state, "p1"));
    }

    [Fact]
    public void Bot_Pumps_An_Unblocked_Attacker_And_Shields_A_Doomed_Blocker()
    {
        // Wolf: an unblocked attacker in the window.
        var state = NewGame("Wolf", "Armadillo");
        var queue = new AbilityQueue();
        var badger = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [badger.Id]);
        CombatEngine.DeclareBlockers(state, queue, new CombatAssignment(), []);
        Priority.Sync(state);
        Assert.Equal(BotActionKind.UseChampionPower, DiceKingdomBot.Decide(state, "p1")!.Kind);

        // Armadillo: its blocker would die - shield it.
        var state2 = NewGame("Wolf", "Armadillo");
        var queue2 = new AbilityQueue();
        var attacker = AddDie(state2, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3);
        var blocker = AddDie(state2, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone);
        Fight(state2, queue2, attacker, blocker);
        Priority.Pass(state2, queue2, "p1");
        Assert.Equal(BotActionKind.UseChampionPower, DiceKingdomBot.Decide(state2, "p2")!.Kind);
        ChampionPowers.Use(state2, queue2, "p2");
        Drain(state2, queue2);
        if (state2.PendingChoice is { } pending)
            Assert.Equal([blocker.Id], DiceKingdomBot.Decide(state2, "p2")!.DieIds);
    }

    // User, 2026-11-15: Armadillo shielded a Tardigrade in a lane sending
    // nothing through rather than a Fox about to die with 2 Overcrush
    // behind it. The shield's choice was marked harmful and carried no
    // effect, so the bot fell back to sacrificing its least valuable die.
    [Fact]
    public void Bot_Shields_The_Doomed_Blocker_Worth_Most_Not_The_Least()
    {
        var state = NewGame("Wolf", "Armadillo");
        var queue = new AbilityQueue();
        var hippoA = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3, tag: "a");
        var hippoB = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3, tag: "b");
        var fox = AddDie(state, DiceKingdomConfig.Fox.Id, "p2", Zone.FieldZone);
        var crab = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone); // 1/2 (3 with the Fox)
        // Enough to KO both (Fox's own aura makes it 2/6).
        foreach (var hippo in new[] { hippoA, hippoB }) hippo.AppliedModifiers.Add(new AppliedModifier(5, 0, 0, "test", Duration.EndOfTurn));
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [hippoA.Id] = 0, [hippoB.Id] = 1 });
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(hippoA.Id, fox.Id);
        assignment.AssignBlocker(hippoB.Id, crab.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [fox.Id, crab.Id]);
        Drain(state, queue);
        Priority.Sync(state);
        Priority.Pass(state, queue, "p1");

        Assert.Equal(BotActionKind.UseChampionPower, DiceKingdomBot.Decide(state, "p2")!.Kind);
        ChampionPowers.Use(state, queue, "p2");
        Drain(state, queue);
        Assert.NotNull(state.PendingChoice);
        Assert.Equal([fox.Id], DiceKingdomBot.Decide(state, "p2")!.DieIds);
    }

    [Fact]
    public void Bot_Uses_Eagles_Free_Field_On_The_Costliest_Creature()
    {
        var state = NewGame("GoldenEagle", "Wolf");
        AddDie(state, DiceKingdomConfig.Swift.Id, "p1", Zone.ReservePool); // fielding 0
        var phoenix = AddDie(state, DiceKingdomConfig.Phoenix.Id, "p1", Zone.ReservePool);
        var decision = DiceKingdomBot.Decide(state, "p1")!;
        Assert.Equal(BotActionKind.Field, decision.Kind);
        Assert.True(decision.Free);
        Assert.Equal(phoenix.Id, decision.DieId);
    }

    // User question (2026-10-04): Overcrush only carries through once EVERY
    // blocker in the lane is gone, so a shielded blocker - which takes no
    // combat damage and stays - must stop it. Both kinds of Overcrush.
    [Fact]
    public void A_Shielded_Blocker_Stops_Keyword_Overcrush()
    {
        var state = NewGame("Wolf", "Armadillo");
        var queue = new AbilityQueue();
        var bear = AddDie(state, DiceKingdomConfig.GrizzlyBear.Id, "p1", Zone.FieldZone, level: 3); // Overcrush, 6 ATK
        var crab = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone); // 2 DEF
        Fight(state, queue, bear, crab);
        Priority.Pass(state, queue, "p1");
        ChampionPowers.Use(state, queue, "p2");
        Drain(state, queue);
        Priority.AfterGlobal(state, "p2");
        var life = state.PlayerTwo.Life;

        Priority.Pass(state, queue, "p1"); // combat damage
        Drain(state, queue);

        Assert.Equal(Zone.FieldZone, crab.Zone); // shielded: still there...
        Assert.Equal(life, state.PlayerTwo.Life); // ...so no Overcrush carry-through (6 - 2 = 4 without the shield)
    }

    [Fact]
    public void A_Shielded_Blocker_Stops_A_Two_Attacker_Lanes_Overcrush_But_An_Unshielded_One_Does_Not()
    {
        foreach (var shield in new[] { true, false })
        {
            var state = NewGame("Wolf", "Armadillo");
            var queue = new AbilityQueue();
            var a1 = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 1, tag: "a"); // 2 ATK
            var a2 = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 1, tag: "b"); // 2 ATK
            var escort = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone, level: 1, tag: "c"); // 1 ATK, unblocked
            var crab = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone); // 2 DEF
            state.MoveToStep(StepIds.SelectAttackers);
            // One lane: auto-Overcrush. The stacking rule needs another lane as full: the escort.
            CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [a1.Id] = 0, [a2.Id] = 0, [escort.Id] = 1 });
            var assignment = new CombatAssignment();
            assignment.AssignBlocker(a1.Id, crab.Id);
            CombatEngine.DeclareBlockers(state, queue, assignment, [crab.Id]);
            Drain(state, queue);
            Priority.Sync(state);
            Priority.Pass(state, queue, "p1");
            if (shield)
            {
                ChampionPowers.Use(state, queue, "p2");
                Drain(state, queue);
                Priority.AfterGlobal(state, "p2");
                Priority.Pass(state, queue, "p1");
            }
            else Priority.Pass(state, queue, "p2");
            Drain(state, queue);

            // 4 lane ATK into 2 DEF: 2 carries through only when the blocker is gone
            // (and the escort's 1 either way).
            Assert.Equal(shield ? 19 : 17, state.PlayerTwo.Life);
            Assert.Equal(shield ? Zone.FieldZone : Zone.PrepArea, crab.Zone);
        }
    }

    // "X wins!" reads last (2026-11-22, a real game): a lethal Overcrush
    // marked its damage - ending the game - before logging its own line,
    // so "Wolf wins!" came out above the hit that won it.
    [Fact]
    public void The_Win_Line_Comes_After_The_Overcrush_That_Won()
    {
        var state = NewGame("Wolf", "Armadillo");
        var queue = new AbilityQueue();
        state.PlayerTwo.Life = 2;
        var a1 = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 1, tag: "a");
        var a2 = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 1, tag: "b");
        var escort = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone, level: 1, tag: "c");
        escort.AppliedModifiers.Add(new AppliedModifier(-1, 0, 0, "test", Duration.EndOfTurn)); // 0 ATK: only the Overcrush is lethal
        var crab = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [a1.Id] = 0, [a2.Id] = 0, [escort.Id] = 1 });
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(a1.Id, crab.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [crab.Id]);
        Drain(state, queue);
        Priority.Sync(state);
        Priority.Pass(state, queue, "p1");
        Priority.Pass(state, queue, "p2");
        Drain(state, queue);

        Assert.True(state.IsGameOver);
        Assert.EndsWith("wins!", state.Log[^1].Text);
        Assert.StartsWith("Overcrush:", state.Log[^2].Text);
        Assert.True(state.Log[^1].Seq > state.Log[^2].Seq);
    }
}
