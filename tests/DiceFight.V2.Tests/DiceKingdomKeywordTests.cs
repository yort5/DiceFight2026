using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// Intimidate and Infiltrate (2026-10-03) - engine-built keywords
// (KeywordAbilities), proven on the real Dice Kingdom cards that carry
// them (Frilled Lizard, Flying Squirrel) through the real firing path:
// TurnEngine/CombatEngine action -> AbilityQueue -> DrainQueue.
public class DiceKingdomKeywordTests
{
    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static void Drain(GameState state, AbilityQueue queue) =>
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

    // Armadillo (own +1 DEF) attacks and Great Horned Owl (Foresight)
    // defends: neither passive touches an ATK or DEF these tests read.
    private static GameState NewGame()
    {
        var playerOne = new Player { Id = "p1", Name = "One", ChampionId = "Armadillo" };
        playerOne.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["Armadillo"]);
        var playerTwo = new Player { Id = "p2", Name = "Two", ChampionId = "GreatHornedOwl" };
        playerTwo.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["GreatHornedOwl"]);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, playerOne, playerTwo);
        state.ActivePlayerId = "p1";
        state.MoveToStep(StepIds.Main);
        return state;
    }

    private static DieInstance AddDie(GameState state, string cardId, string controllerId, Zone zone, int level = 1, string? tag = null)
    {
        var die = new DieInstance
        {
            Id = $"{controllerId}-{cardId}-{tag ?? zone.ToString()}", CardId = cardId, OwnerId = controllerId,
            ControllerId = controllerId, Zone = zone, CurrentFaceIndex = level - 1,
        };
        state.Dice.Add(die);
        return die;
    }

    private static string[] Energy(GameState state, string controllerId, string energyType, int count)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var die = new DieInstance
            {
                Id = $"{controllerId}-energy-{i}", PoolDieId = $"Tardigrade{energyType}", OwnerId = controllerId,
                ControllerId = controllerId, Zone = Zone.ReservePool, CurrentFaceIndex = 0, // 2 energy
            };
            state.Dice.Add(die);
            ids.Add(die.Id);
        }
        return [.. ids];
    }

    private static DieInstance FieldLizard(GameState state, AbilityQueue queue)
    {
        var lizard = AddDie(state, DiceKingdomConfig.FrilledLizard.Id, "p1", Zone.ReservePool);
        TurnEngine.Field(state, queue, lizard.Id, Energy(state, "p1", "Eye", 1)); // L1 fields for 1
        Drain(state, queue);
        return lizard;
    }

    // --- Awaken ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Awaken_Does_Not_Trigger_When_A_Roll_Or_Reroll_Lands_Higher(bool reroll)
    {
        var state = NewGame();
        state.MoveToStep(StepIds.RollAndReroll);
        var queue = new AbilityQueue();
        var swallow = AddDie(state, DiceKingdomConfig.BarnSwallow.Id, "p1",
            reroll ? Zone.ReservePool : Zone.DiceFromBag);

        if (reroll) TurnEngine.RerollOwn(state, queue, new FixedRoller(1), [swallow.Id]);
        else TurnEngine.Roll(state, queue, new FixedRoller(1));
        Drain(state, queue);

        Assert.Equal(2, state.GetCurrentFace(swallow)!.Character!.Level);
        Assert.Empty(queue.Pending);
        Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void Awaken_Does_Not_Trigger_When_An_Ability_Rerolls_To_A_Higher_Level()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var swallow = AddDie(state, DiceKingdomConfig.BarnSwallow.Id, "p1", Zone.FieldZone);
        var context = new EffectContext
        {
            State = state, Queue = queue, ControllerId = "p1", Trigger = TriggerKind.Global,
            Roller = new FixedRoller(1), Random = new Random(1),
        };
        context.Bind("self", swallow.Id);

        EffectInterpreter.Execute(new Reroll(new TargetFilter(Self: true)), context);
        Drain(state, queue);

        Assert.Equal(2, state.GetCurrentFace(swallow)!.Character!.Level);
        Assert.Empty(queue.Pending);
        Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void Mutations_Global_Still_Triggers_Awaken_When_It_Spins_A_Creature_Up()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var down = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 2);
        var swallow = AddDie(state, DiceKingdomConfig.BarnSwallow.Id, "p1", Zone.FieldZone);
        var globalIndex = DiceKingdomConfig.Mutation.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);

        TurnEngine.UseGlobal(state, queue, DiceKingdomConfig.Mutation.Id, "p1", globalIndex,
            Energy(state, "p1", "Eye", 1));
        Drain(state, queue);
        var pending = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Contains(swallow.Id, pending.CandidateIds);
        EffectInterpreter.AnswerPendingChoice(state, [swallow.Id]);
        Drain(state, queue);

        Assert.Null(state.PendingChoice);
        Assert.Equal(1, state.GetCurrentFace(down)!.Character!.Level);
        Assert.Equal(2, state.GetCurrentFace(swallow)!.Character!.Level);
        Assert.Single(state.DiceIn("p1", Zone.PrepArea)); // Barn Swallow's Awaken drew a die.
    }

    // --- Intimidate ---

    [Fact]
    public void Intimidate_Removes_The_Target_Until_CleanUp_Then_It_Returns_On_The_Same_Face()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var target = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone, level: 3);

        FieldLizard(state, queue); // the only opposing creature: auto-targeted

        Assert.Null(state.PendingChoice);
        Assert.Equal(Zone.Intimidated, target.Zone);
        Assert.Equal(2, target.CurrentFaceIndex);

        state.MoveToStep(StepIds.ReturnToField);
        TurnEngine.CleanUp(state, queue);

        Assert.Equal(Zone.FieldZone, target.Zone);
        Assert.Equal(2, target.CurrentFaceIndex); // still level 3
    }

    [Fact]
    public void Intimidate_Asks_Which_Opposing_Creature_And_Never_Offers_Your_Own()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var mine = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone);
        var big = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        var small = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone);

        FieldLizard(state, queue);

        var pending = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Equal("p1", pending.ControllerId);
        Assert.Equal([big.Id, small.Id], pending.CandidateIds.Order());
        Assert.DoesNotContain(mine.Id, pending.CandidateIds);

        EffectInterpreter.AnswerPendingChoice(state, [big.Id]);
        Drain(state, queue);
        Assert.Equal(Zone.Intimidated, big.Zone);
        Assert.Equal(Zone.FieldZone, small.Zone);
    }

    [Fact]
    public void An_Intimidated_Creature_Cannot_Block_And_Its_While_Active_Aura_Is_Off()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var muskOx = AddDie(state, DiceKingdomConfig.MuskOx.Id, "p2", Zone.FieldZone); // "your creatures get +1 DEF"
        var crab = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone);
        var crabDefense = QueryEngine.GetDefense(state, crab);

        FieldLizard(state, queue);
        if (state.PendingChoice is not null)
        {
            EffectInterpreter.AnswerPendingChoice(state, [muskOx.Id]);
            Drain(state, queue);
        }

        Assert.Equal(Zone.Intimidated, muskOx.Zone);
        Assert.Equal(crabDefense - 1, QueryEngine.GetDefense(state, crab));

        var attacker = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, muskOx.Id);
        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareBlockers(state, queue, assignment, [muskOx.Id]));
    }

    [Fact]
    public void Intimidate_Fizzles_With_No_Opposing_Creature()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var lizard = FieldLizard(state, queue);

        Assert.Null(state.PendingChoice);
        Assert.Equal(Zone.FieldZone, lizard.Zone);
    }

    // --- Infiltrate ---

    // Flying Squirrel L2 is 3 ATK. Declares it alone (lane 0) against an
    // opponent with one potential blocker, `blocked` deciding whether it
    // is used.
    private static (GameState state, AbilityQueue queue, DieInstance squirrel, CombatAssignment assignment) SwingSquirrel(bool blocked)
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var squirrel = AddDie(state, DiceKingdomConfig.FlyingSquirrel.Id, "p1", Zone.FieldZone, level: 2);
        var wall = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [squirrel.Id]);
        Drain(state, queue);

        var assignment = new CombatAssignment();
        if (blocked) assignment.AssignBlocker(squirrel.Id, wall.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, blocked ? [wall.Id] : []);
        Drain(state, queue);
        return (state, queue, squirrel, assignment);
    }

    [Fact]
    public void Unblocked_Infiltrate_Accepted_Returns_To_The_Field_For_1_Damage_And_Stays_In_Play()
    {
        var (state, queue, squirrel, assignment) = SwingSquirrel(blocked: false);

        var pending = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Equal("p1", pending.ControllerId);
        Assert.Contains("Infiltrate", pending.Description);

        EffectInterpreter.AnswerPendingChoice(state, [squirrel.Id]);
        Drain(state, queue);
        Assert.Equal(Zone.FieldZone, squirrel.Zone);
        Assert.Null(squirrel.Lane);
        Assert.Equal(19, state.PlayerTwo.Life);

        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        TurnEngine.CleanUp(state, queue);
        Assert.Equal(19, state.PlayerTwo.Life); // no combat damage on top
        Assert.Equal(Zone.FieldZone, squirrel.Zone); // never went Out of Play
    }

    [Fact]
    public void Unblocked_Infiltrate_Declined_Hits_For_Full_Attack_And_Leaves_Play()
    {
        var (state, queue, squirrel, assignment) = SwingSquirrel(blocked: false);

        EffectInterpreter.AnswerPendingChoice(state, []);
        Drain(state, queue);
        Assert.Equal(Zone.AttackZone, squirrel.Zone);

        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        Assert.Equal(17, state.PlayerTwo.Life);
        Assert.Equal(Zone.OutOfPlay, squirrel.Zone);
    }

    [Fact]
    public void A_Blocked_Infiltrate_Attacker_Gets_No_Offer()
    {
        var (state, _, squirrel, _) = SwingSquirrel(blocked: true);

        Assert.Null(state.PendingChoice);
        Assert.Equal(Zone.AttackZone, squirrel.Zone);
    }

    [Fact]
    public void Bot_Takes_Infiltrate_On_A_Small_Hit_And_Declines_When_The_Full_Hit_Is_Lethal()
    {
        var (state, _, squirrel, _) = SwingSquirrel(blocked: false);
        var decision = DiceKingdomBot.Decide(state, "p1")!;
        Assert.Equal(BotActionKind.ResolvePendingChoice, decision.Kind);
        Assert.Empty(decision.DieIds!); // 3 ATK: worth more than the die's trip home

        squirrel.CurrentFaceIndex = 0; // L1: 2 ATK
        Assert.Equal([squirrel.Id], DiceKingdomBot.Decide(state, "p1")!.DieIds!);

        state.PlayerTwo.Life = 2; // the full 2 is lethal, 1 isn't
        Assert.Empty(DiceKingdomBot.Decide(state, "p1")!.DieIds!);
    }

    // --- Attune / Obscure ---

    // An Anger Issues die already on its action face in p1's Reserve Pool.
    private static DieInstance ActionDieReady(GameState state)
    {
        var die = AddDie(state, DiceKingdomConfig.AngerIssues.Id, "p1", Zone.ReservePool, level: 1, tag: "action");
        if (!state.PlayerOne.TeamCardIds.Contains(DiceKingdomConfig.AngerIssues.Id))
            state.PlayerOne.TeamCardIds.Add(DiceKingdomConfig.AngerIssues.Id);
        return die;
    }

    // Answers every pending choice: Attune's with `attunePick`, anything
    // else (Anger Issues' own pump) with its first candidate.
    private static void ResolveAll(GameState state, AbilityQueue queue, string attunePick)
    {
        while (state.PendingChoice is { } pending)
        {
            var pick = pending.Description.Contains("Attune") ? attunePick : pending.CandidateIds[0];
            EffectInterpreter.AnswerPendingChoice(state, [pick]);
            Drain(state, queue);
        }
    }

    [Fact]
    public void Attune_Deals_1_To_The_Opponent_Or_A_Character_Each_Time_You_Use_An_Action_Die()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var eel = AddDie(state, DiceKingdomConfig.ElectricEel.Id, "p1", Zone.FieldZone);
        var crab = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone);

        TurnEngine.UseAction(state, queue, ActionDieReady(state).Id);
        Drain(state, queue);

        var pending = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Contains("Attune", pending.Description);
        Assert.Contains("p2", pending.CandidateIds);
        Assert.DoesNotContain("p1", pending.CandidateIds); // never yourself
        Assert.Contains(crab.Id, pending.CandidateIds);
        Assert.Contains(eel.Id, pending.CandidateIds); // any character die

        ResolveAll(state, queue, "p2");
        Assert.Equal(19, state.PlayerTwo.Life);
    }

    [Fact]
    public void Attune_Can_KO_A_Creature()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        AddDie(state, DiceKingdomConfig.ElectricEel.Id, "p1", Zone.FieldZone);
        var badger = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone); // L1 1 DEF

        TurnEngine.UseAction(state, queue, ActionDieReady(state).Id);
        Drain(state, queue);
        ResolveAll(state, queue, badger.Id);

        Assert.Equal(Zone.PrepArea, badger.Zone); // KO'd
    }

    [Fact]
    public void Attune_Does_Not_Fire_On_A_Global_Or_While_Inactive()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        AddDie(state, DiceKingdomConfig.ElectricEel.Id, "p1", Zone.PrepArea); // not active
        var activeEel = AddDie(state, DiceKingdomConfig.ElectricEel.Id, "p1", Zone.FieldZone, tag: "field");
        ActionDieReady(state);

        // A Global is not an action die.
        TurnEngine.UseGlobal(state, queue, DiceKingdomConfig.AngerIssues.Id, "p1", 1, Energy(state, "p1", "Claw", 1));
        Drain(state, queue);
        while (state.PendingChoice is { } pending)
        {
            Assert.DoesNotContain("Attune", pending.Description);
            EffectInterpreter.AnswerPendingChoice(state, [pending.CandidateIds[0]]);
            Drain(state, queue);
        }
        Assert.Equal(20, state.PlayerTwo.Life);

        // The die in the Prep Area adds nothing: one action, one Attune.
        activeEel.Zone = Zone.FieldZone;
        TurnEngine.UseAction(state, queue, state.Dice.First(d => d.Id.EndsWith("-action")).Id);
        Drain(state, queue);
        var attunes = 0;
        while (state.PendingChoice is { } pending)
        {
            if (pending.Description.Contains("Attune")) attunes++;
            EffectInterpreter.AnswerPendingChoice(state, [pending.Description.Contains("Attune") ? "p2" : pending.CandidateIds[0]]);
            Drain(state, queue);
        }
        Assert.Equal(1, attunes);
    }

    [Fact]
    public void Attune_N_Deals_N_And_A_Granted_Attune_Stacks_With_The_Printed_One()
    {
        Assert.Equal(1, KeywordAbilities.AttuneAmount("Attune"));
        Assert.Equal(2, KeywordAbilities.AttuneAmount("Attune 2"));
        Assert.Null(KeywordAbilities.AttuneAmount("Attuned"));

        var state = NewGame();
        var queue = new AbilityQueue();
        var eel = AddDie(state, DiceKingdomConfig.ElectricEel.Id, "p1", Zone.FieldZone);
        eel.GrantedTags.Add(new GrantedTag("Attune 2", Duration.EndOfTurn, "p1"));

        TurnEngine.UseAction(state, queue, ActionDieReady(state).Id);
        Drain(state, queue);
        ResolveAll(state, queue, "p2");

        Assert.Equal(17, state.PlayerTwo.Life); // 1 + 2
    }

    [Fact]
    public void Obscure_Makes_The_Creature_Unblockable_This_Turn_When_You_Use_An_Action_Die()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var chameleon = AddDie(state, DiceKingdomConfig.Chameleon.Id, "p1", Zone.FieldZone);
        var wall = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);

        TurnEngine.UseAction(state, queue, ActionDieReady(state).Id);
        Drain(state, queue);
        ResolveAll(state, queue, "p2");
        Assert.Contains(CombatFlagKind.Unblockable, chameleon.CombatFlags);

        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [chameleon.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(chameleon.Id, wall.Id);
        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareBlockers(state, queue, assignment, [wall.Id]));

        state.MoveToStep(StepIds.ReturnToField);
        chameleon.Zone = Zone.FieldZone;
        TurnEngine.CleanUp(state, queue);
        Assert.DoesNotContain(CombatFlagKind.Unblockable, chameleon.CombatFlags); // this turn only
    }

    [Fact]
    public void Bot_Attune_Finishes_A_Creature_It_Can_Kill_Else_Hits_Face()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        AddDie(state, DiceKingdomConfig.ElectricEel.Id, "p1", Zone.FieldZone);
        var hippo = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone); // 5 DEF: a ping does nothing
        TurnEngine.UseAction(state, queue, ActionDieReady(state).Id);
        Drain(state, queue);
        Assert.Contains("Attune", state.PendingChoice!.Description);
        Assert.Equal(["p2"], DiceKingdomBot.Decide(state, "p1")!.DieIds!);

        hippo.Damage = 4; // now 1 more kills it
        Assert.Equal([hippo.Id], DiceKingdomBot.Decide(state, "p1")!.DieIds!);
    }

    // A lane holding an unblockable attacker can't be blocked at all, so
    // the bot must not block its lane-mate either (the simulator's first
    // Chameleon run threw "is unblockable this turn" in 20 games).
    [Fact]
    public void Bot_Never_Blocks_A_Lane_With_An_Unblockable_Attacker_In_It()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var chameleon = AddDie(state, DiceKingdomConfig.Chameleon.Id, "p1", Zone.FieldZone);
        var badger = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone);
        AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone, level: 3); // blocks anything for free
        chameleon.CombatFlags.Add(CombatFlagKind.Unblockable);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [chameleon.Id] = 0, [badger.Id] = 0 });

        var decision = DiceKingdomBot.Decide(state, "p2")!;
        Assert.Equal(BotActionKind.DeclareBlockers, decision.Kind);
        Assert.Empty(decision.Blocks);
    }

    // --- Energize / Energy Drain / Tag Out / Sacrifice ---

    private static int CountChoices(GameState state, AbilityQueue queue, string marker, string answerForMarker)
    {
        var count = 0;
        while (state.PendingChoice is { } pending)
        {
            var mine = pending.Description.Contains(marker);
            if (mine) count++;
            EffectInterpreter.AnswerPendingChoice(state, mine ? [answerForMarker] : pending.MinCount == 0 ? [] : [pending.CandidateIds[0]]);
            Drain(state, queue);
        }
        return count;
    }

    [Fact]
    public void Energize_Fires_Once_When_Its_Die_Rolls_A_Double_Energy_Face()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        AddDie(state, DiceKingdomConfig.Firefly.Id, "p1", Zone.ReservePool, level: 4); // face index 3: 2 Wing
        state.MoveToStep(StepIds.RollAndReroll);

        TurnEngine.FinishRoll(state, queue);
        Drain(state, queue);

        // The opponent is the only legal target (no creatures out), so no
        // choice comes up - the 2 damage just lands.
        Assert.Null(state.PendingChoice);
        Assert.Equal(18, state.PlayerTwo.Life);
    }

    [Fact]
    public void Energize_Does_Not_Fire_Twice_For_A_Reroll_Onto_A_Double_Face()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var firefly = AddDie(state, DiceKingdomConfig.Firefly.Id, "p1", Zone.ReservePool, level: 6); // index 5: single energy
        state.MoveToStep(StepIds.RollAndReroll);

        TurnEngine.RerollOwn(state, queue, new FixedRoller(3), [firefly.Id]); // lands on a double, then advances to Main
        Drain(state, queue);

        Assert.Equal(18, state.PlayerTwo.Life); // 2 damage once, not 4
    }

    [Fact]
    public void Energize_Ignores_A_Single_Energy_Face()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        AddDie(state, DiceKingdomConfig.Firefly.Id, "p1", Zone.ReservePool, level: 6);
        state.MoveToStep(StepIds.RollAndReroll);
        TurnEngine.FinishRoll(state, queue);
        Drain(state, queue);

        Assert.Equal(0, CountChoices(state, queue, "Energize", "p2"));
    }

    [Fact]
    public void Energy_Drain_Spins_Every_Creature_Engaged_With_It_Down_A_Level_But_Not_Below_1()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var big = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3);
        var small = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone, level: 1);
        var leech = AddDie(state, DiceKingdomConfig.Leech.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [big.Id] = 0, [small.Id] = 0 }); // one lane

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(big.Id, leech.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [leech.Id]);

        Assert.Equal(2, state.GetCurrentFace(big)!.Character!.Level); // lane-mates are engaged too
        Assert.Equal(1, state.GetCurrentFace(small)!.Character!.Level);
        Assert.Equal(1, state.GetCurrentFace(leech)!.Character!.Level); // nothing drains the Leech
    }

    [Fact]
    public void Tag_Out_Is_Offered_To_Field_Dice_After_Blocks_And_Preps_Itself_For_A_Plus_2_Plus_2()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        var blocker = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone);
        var meerkat = AddDie(state, DiceKingdomConfig.Meerkat.Id, "p2", Zone.FieldZone);
        var benchedMeerkat = AddDie(state, DiceKingdomConfig.Meerkat.Id, "p1", Zone.FieldZone, tag: "attacking");
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [attacker.Id] = 0, [benchedMeerkat.Id] = 1 });
        Drain(state, queue);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        Drain(state, queue);

        var offer = Assert.IsType<PendingChoice>(state.PendingChoice); // only p2's Field Zone Meerkat - the attacking one isn't in the Field
        Assert.Equal("p2", offer.ControllerId);
        Assert.Equal([meerkat.Id], offer.CandidateIds);
        var defenseBefore = QueryEngine.GetDefense(state, blocker);

        EffectInterpreter.AnswerPendingChoice(state, [meerkat.Id]);
        Drain(state, queue);
        EffectInterpreter.AnswerPendingChoice(state, [blocker.Id]);
        Drain(state, queue);

        Assert.Equal(Zone.PrepArea, meerkat.Zone);
        Assert.Equal(defenseBefore + 2, QueryEngine.GetDefense(state, blocker));
        Assert.Null(state.PendingChoice);
    }

    [Fact]
    public void Sacrifice_Moves_The_Die_Out_Of_Play_Without_A_KO_And_Deals_Its_Attack()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var fodder = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3); // 5 ATK
        var victim = AddDie(state, DiceKingdomConfig.MuskOx.Id, "p2", Zone.FieldZone); // 4 DEF (+1 its own aura)
        var ant = AddDie(state, DiceKingdomConfig.ArmyAnt.Id, "p1", Zone.ReservePool);
        TurnEngine.Field(state, queue, ant.Id, Energy(state, "p1", "Claw", 1));
        Drain(state, queue);

        Assert.Contains("Sacrifice", state.PendingChoice!.Description);
        EffectInterpreter.AnswerPendingChoice(state, [ant.Id]); // yes (the stand-in)
        Drain(state, queue);
        EffectInterpreter.AnswerPendingChoice(state, [fodder.Id]); // the sacrifice
        Drain(state, queue);
        Assert.Equal(Zone.OutOfPlay, fodder.Zone); // not the Prep Area: not a KO
        EffectInterpreter.AnswerPendingChoice(state, [victim.Id]);
        Drain(state, queue);

        Assert.Equal(Zone.PrepArea, victim.Zone); // 5 damage KO'd it
        state.MoveToStep(StepIds.ReturnToField);
        TurnEngine.CleanUp(state, queue);
        Assert.Equal(Zone.UsedPile, fodder.Zone);
    }

    [Fact]
    public void Sacrificing_On_The_Opponents_Turn_Goes_Straight_To_The_Used_Pile()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var theirs = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone);
        EffectInterpreter.Execute(new Sacrifice(new TargetFilter(Ownership: TargetOwnership.Own)),
            new EffectContext { State = state, Queue = queue, ControllerId = "p2", Trigger = TriggerKind.Global, Roller = new FixedRoller(0), Random = new Random(1) });

        Assert.Equal(Zone.UsedPile, theirs.Zone);
    }

    // --- Range / Aftershock / Breath Weapon ---

    [Fact]
    public void Range_Both_Sides_Pick_Then_All_Shots_Land_Together()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var myFish = AddDie(state, DiceKingdomConfig.Archerfish.Id, "p1", Zone.FieldZone);
        var theirFish = AddDie(state, DiceKingdomConfig.Archerfish.Id, "p2", Zone.FieldZone);
        var myBadger = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone);
        myBadger.Damage = 1; // 2 DEF with Armadillo's +1 - one more KOs it
        AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone); // so p1's shot is a real choice
        theirFish.Damage = 1; // one more KOs it
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [myFish.Id]);
        Drain(state, queue);

        var first = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Equal("p1", first.ControllerId); // Active player picks first
        Assert.Contains("Range", first.Description);
        EffectInterpreter.AnswerPendingChoice(state, [theirFish.Id]);
        Drain(state, queue);
        Assert.Equal(Zone.FieldZone, theirFish.Zone); // nothing has landed yet

        var second = Assert.IsType<PendingChoice>(state.PendingChoice); // Badger or my Archerfish
        Assert.Equal("p2", second.ControllerId);
        EffectInterpreter.AnswerPendingChoice(state, [myBadger.Id]);
        Drain(state, queue);

        Assert.Equal(Zone.PrepArea, theirFish.Zone); // KO'd by my shot...
        Assert.Equal(Zone.PrepArea, myBadger.Zone); // ...and its own shot still landed
    }

    [Fact]
    public void Range_Only_Fires_When_A_Range_Creature_Attacks()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        AddDie(state, DiceKingdomConfig.Archerfish.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        Drain(state, queue);

        Assert.Null(state.PendingChoice);
    }

    [Fact]
    public void Aftershock_Fires_When_The_Opponent_KOs_It_In_Combat()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3); // 5 ATK
        var beetle = AddDie(state, DiceKingdomConfig.BombardierBeetle.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, beetle.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [beetle.Id]);
        Drain(state, queue);
        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        Drain(state, queue);

        Assert.Equal(Zone.PrepArea, beetle.Zone);
        // The attacking Hippopotamus is the only creature left, so the
        // Aftershock's 2 damage lands on it without a choice.
        Assert.Contains(state.Log, l => l.Text == "Bombardier Beetle deals 2 damage to Hippopotamus.");
    }

    [Fact]
    public void Aftershock_Fires_On_The_Opponents_Intimidate_But_Not_Your_Own_Sacrifice()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var beetle = AddDie(state, DiceKingdomConfig.BombardierBeetle.Id, "p2", Zone.FieldZone);
        AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3); // the Aftershock's only target (5 DEF)
        FieldLizard(state, queue); // p1's Intimidate - the beetle is p2's only creature
        Assert.Equal(Zone.Intimidated, beetle.Zone);
        var hippo = state.Dice.First(d => d.CardId == DiceKingdomConfig.Hippopotamus.Id);
        var aftershock = Assert.IsType<PendingChoice>(state.PendingChoice); // Hippopotamus or the Lizard
        Assert.Equal("p2", aftershock.ControllerId);
        EffectInterpreter.AnswerPendingChoice(state, [hippo.Id]);
        Drain(state, queue);
        Assert.Equal(2, hippo.Damage);

        var state2 = NewGame();
        var queue2 = new AbilityQueue();
        var ownBeetle = AddDie(state2, DiceKingdomConfig.BombardierBeetle.Id, "p1", Zone.FieldZone);
        EffectInterpreter.Execute(new Sacrifice(new TargetFilter(Ownership: TargetOwnership.Own)),
            new EffectContext { State = state2, Queue = queue2, ControllerId = "p1", Trigger = TriggerKind.Global, Roller = new FixedRoller(0), Random = new Random(1) });
        Assert.Equal(Zone.OutOfPlay, ownBeetle.Zone);
        Assert.Empty(queue2.Pending);
    }

    private static (GameState state, AbilityQueue queue, DieInstance[] energy) SwingKomodo(int komodos = 1)
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var dragons = Enumerable.Range(0, komodos)
            .Select(i => AddDie(state, DiceKingdomConfig.KomodoDragon.Id, "p1", Zone.FieldZone, tag: $"k{i}")).ToList();
        var energy = Energy(state, "p1", "Claw", 2).Select(id => state.Dice.First(d => d.Id == id)).ToArray();
        energy[1].CurrentFaceIndex = 2; // a 1-pip Tardigrade face
        AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone); // 1 DEF - dies
        AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone); // 5 DEF - survives
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, dragons.Select(d => d.Id).ToList());
        Drain(state, queue);
        return (state, queue, energy);
    }

    [Fact]
    public void Breath_Weapon_Paid_Hits_The_Opponent_And_Every_Creature_They_Have()
    {
        var (state, queue, energy) = SwingKomodo();
        var offer = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Contains("Breath Weapon", offer.Description);
        Assert.Equal(0, offer.MinCount);

        EffectInterpreter.AnswerPendingChoice(state, [energy[1].Id]); // the 1-pip die covers X = 1
        Drain(state, queue);

        Assert.Equal(19, state.PlayerTwo.Life);
        Assert.Equal(Zone.PrepArea, state.Dice.First(d => d.CardId == DiceKingdomConfig.HoneyBadger.Id).Zone);
        Assert.Equal(1, state.Dice.First(d => d.CardId == DiceKingdomConfig.Hippopotamus.Id).Damage);
        Assert.Equal(Zone.OutOfPlay, energy[1].Zone); // spent like a Global
        Assert.Equal(Zone.ReservePool, energy[0].Zone);
    }

    [Fact]
    public void Breath_Weapon_Declined_Does_Nothing_And_Fires_Once_Per_Unique_Creature()
    {
        var (state, queue, _) = SwingKomodo(komodos: 2);
        Assert.NotNull(state.PendingChoice);
        EffectInterpreter.AnswerPendingChoice(state, []);
        Drain(state, queue);

        Assert.Null(state.PendingChoice); // no second offer for the second Komodo
        Assert.Equal(20, state.PlayerTwo.Life);
    }

    [Fact]
    public void Bot_Breathes_When_It_KOs_Something_And_Pays_With_One_Die()
    {
        var (state, _, energy) = SwingKomodo();
        var decision = DiceKingdomBot.Decide(state, "p1")!;
        // Which die is BotEnergy's call (it keeps the better body); X = 1
        // only ever needs one.
        Assert.Single(decision.DieIds);
        Assert.Contains(decision.DieIds[0], energy.Select(e => e.Id));
    }

    [Fact]
    public void Bot_Sacrifices_A_Cheap_Creature_To_KO_A_Better_One()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var badger = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p1", Zone.FieldZone, level: 3); // 2 ATK, cheap
        var silverback = AddDie(state, DiceKingdomConfig.Silverback.Id, "p2", Zone.FieldZone, level: 3);
        silverback.Damage = QueryEngine.GetDefense(state, silverback) - 2; // 2 more finishes it
        var ant = AddDie(state, DiceKingdomConfig.ArmyAnt.Id, "p1", Zone.ReservePool);
        TurnEngine.Field(state, queue, ant.Id, Energy(state, "p1", "Claw", 1));
        Drain(state, queue);

        for (var i = 0; i < 4 && state.PendingChoice is { } pending; i++)
        {
            var decision = DiceKingdomBot.Decide(state, pending.ControllerId)!;
            EffectInterpreter.AnswerPendingChoice(state, decision.DieIds);
            Drain(state, queue);
        }

        Assert.Equal(Zone.OutOfPlay, badger.Zone);
        Assert.Equal(Zone.PrepArea, silverback.Zone);
    }

    [Fact]
    public void Bot_Declines_Sacrifice_Without_A_Good_Trade()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone); // 5 DEF: nothing of mine KOs it
        var ant = AddDie(state, DiceKingdomConfig.ArmyAnt.Id, "p1", Zone.ReservePool);
        TurnEngine.Field(state, queue, ant.Id, Energy(state, "p1", "Claw", 1));
        Drain(state, queue);

        Assert.Empty(DiceKingdomBot.Decide(state, "p1")!.DieIds);
    }

    // X keywords carry their number: "Breath Weapon 2" costs 2 and deals
    // 2 everywhere; "Range 3" shoots for 3. Granted onto dice here, since
    // the catalog's own cards are all X = 1.
    [Fact]
    public void Breath_Weapon_2_Costs_2_And_Deals_2_To_The_Opponent_And_Every_Creature()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var dragon = AddDie(state, DiceKingdomConfig.KomodoDragon.Id, "p1", Zone.FieldZone);
        dragon.GrantedTags.Add(new GrantedTag("Breath Weapon 2", Duration.EndOfTurn, "p1"));
        var energy = Energy(state, "p1", "Claw", 2).Select(id => state.Dice.First(d => d.Id == id)).ToArray();
        energy[0].CurrentFaceIndex = 2; energy[1].CurrentFaceIndex = 2; // 1 pip each
        var crab = AddDie(state, DiceKingdomConfig.HermitCrab.Id, "p2", Zone.FieldZone); // 2 DEF
        var hippo = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone); // 5 DEF
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [dragon.Id]);
        Drain(state, queue);

        var offer = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Contains("Breath Weapon 2", offer.Description);
        EffectInterpreter.AnswerPendingChoice(state, [energy[0].Id]); // only 1 - asked again
        Drain(state, queue);
        Assert.Contains("only 1", state.PendingChoice!.Description);
        EffectInterpreter.AnswerPendingChoice(state, [energy[0].Id, energy[1].Id]);
        Drain(state, queue);

        Assert.Equal(18, state.PlayerTwo.Life);
        Assert.Equal(Zone.PrepArea, crab.Zone);
        Assert.Equal(2, hippo.Damage);
    }

    [Fact]
    public void Range_3_Hits_For_3()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var fish = AddDie(state, DiceKingdomConfig.Archerfish.Id, "p1", Zone.FieldZone);
        fish.GrantedTags.Add(new GrantedTag("Range 3", Duration.EndOfTurn, "p1"));
        var hippo = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [fish.Id]);
        Drain(state, queue);

        Assert.Equal(3, hippo.Damage); // the higher of Range (1) and Range 3
    }

    // --- Status cues: every state reports its source and duration ---

    private static void PickWhenAsked(GameState state, AbilityQueue queue, string id)
    {
        while (state.PendingChoice is { } pending)
        {
            EffectInterpreter.AnswerPendingChoice(state, [pending.CandidateIds.Contains(id) ? id : pending.CandidateIds[0]]);
            Drain(state, queue);
        }
    }

    private static void FieldFor(GameState state, AbilityQueue queue, string cardId, string energyType, string pick)
    {
        var die = AddDie(state, cardId, "p1", Zone.ReservePool, tag: "fielding");
        var fieldingCost = state.CardCatalog[cardId].Die.Faces[0].Character!.FieldingCost;
        TurnEngine.Field(state, queue, die.Id, fieldingCost == 0 ? [] : Energy(state, "p1", energyType, 1));
        Drain(state, queue);
        PickWhenAsked(state, queue, pick);
    }

    [Fact]
    public void Statuses_Name_The_Card_Behind_Each_Combat_Rule()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var target = AddDie(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone);
        FieldFor(state, queue, DiceKingdomConfig.HermitCrab.Id, "Shell", target.Id);
        FieldFor(state, queue, DiceKingdomConfig.BarnOwl.Id, "Eye", target.Id);

        var statuses = DieStatuses.For(state, target);
        Assert.Contains(new DieStatus("mustBlock", "Hermit Crab", "turn"), statuses);
        Assert.Contains(new DieStatus("cantBlock", "Barn Owl", "turn"), statuses);

        state.MoveToStep(StepIds.ReturnToField);
        TurnEngine.CleanUp(state, queue);
        Assert.Empty(DieStatuses.For(state, target)); // this-turn rules are gone, sources too
        Assert.Empty(target.CombatFlagSources);
    }

    [Fact]
    public void Statuses_Cover_Obscure_And_Its_Lane_And_A_Granted_Keyword()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var chameleon = AddDie(state, DiceKingdomConfig.Chameleon.Id, "p1", Zone.FieldZone);
        TurnEngine.UseAction(state, queue, ActionDieReady(state).Id); // Anger Issues: +3A and Overcrush
        Drain(state, queue);
        PickWhenAsked(state, queue, chameleon.Id);

        var statuses = DieStatuses.For(state, chameleon);
        Assert.Contains(new DieStatus("unblockable", "Chameleon", "turn"), statuses);
        Assert.Contains(new DieStatus("granted", "Anger Issues", "turn", "Overcrush"), statuses);

        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, new Dictionary<string, int> { [chameleon.Id] = 2 });
        Assert.Equal([2], DieStatuses.UnblockableLanes(state));
    }

    [Fact]
    public void Statuses_Cover_Deadly_Intimidate_And_A_Blank()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        var opossum = AddDie(state, DiceKingdomConfig.Opossum.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, opossum.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [opossum.Id]);
        Assert.Contains(new DieStatus("deadly", "Opossum (Deadly)", "cleanup"), DieStatuses.For(state, attacker));

        var state2 = NewGame();
        var queue2 = new AbilityQueue();
        var victim = AddDie(state2, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        FieldLizard(state2, queue2);
        Assert.Equal([new DieStatus("intimidated", "Frilled Lizard", "cleanup")], DieStatuses.For(state2, victim));

        var blanked = AddDie(state2, DiceKingdomConfig.Opossum.Id, "p2", Zone.FieldZone, tag: "blanked");
        blanked.Suppressions.Add(new DieSuppression(Duration.EndOfTurn));
        Assert.Contains(DieStatuses.For(state2, blanked), s => s.Kind == "blanked");
    }

    [Fact]
    public void Energy_Drain_Records_Its_Spin_For_The_Flash()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = AddDie(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 3);
        var leech = AddDie(state, DiceKingdomConfig.Leech.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, leech.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [leech.Id]);

        var spin = Assert.IsType<SpinRecord>(attacker.LastSpin);
        Assert.Equal((3, 2, "Leech · Energy Drain"), (spin.FromLevel!.Value, spin.ToLevel!.Value, spin.Source));
        Assert.True(spin.Seq > 0);
    }

    [Fact]
    public void A_Pangolin_Lockout_Names_Pangolin()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        AddDie(state, DiceKingdomConfig.Orca.Id, "p2", Zone.FieldZone); // something to name
        state.PlayerTwo.TeamCardIds.Add(DiceKingdomConfig.Orca.Id);
        FieldFor(state, queue, DiceKingdomConfig.Pangolin.Id, "Shell", "p2-DK-CLAW-04-FieldZone");

        Assert.Equal(["Pangolin"], QueryEngine.LockoutSources(state, "p2", DiceKingdomConfig.Orca.Id));
        Assert.Empty(QueryEngine.LockoutSources(state, "p1", DiceKingdomConfig.Orca.Id));
    }

    // The keyword reference (tools/CardCatalogDocs, 2026-10-10) prints
    // DiceKingdomConfig.Keywords - so every keyword a card carries must be
    // declared there (its X form too: "Range 2" is Range), and every
    // declared one needs rules text: Dice Masters' wording, ours, or both.
    [Fact]
    public void Every_Card_Keyword_Is_Declared_And_Every_Declared_Keyword_Is_Described()
    {
        var declared = DiceKingdomConfig.Keywords.Select(k => k.Id).ToList();
        var undeclared = DiceKingdomConfig.Catalog.Values
            .SelectMany(c => c.Keywords.Select(k => (Card: c.Name, Keyword: k)))
            .Where(x => !declared.Any(id => KeywordAbilities.ParamOf(x.Keyword, id) is not null))
            .Select(x => $"{x.Card}: {x.Keyword}")
            .ToList();
        Assert.Empty(undeclared);
        Assert.All(DiceKingdomConfig.Keywords, k =>
            Assert.False(string.IsNullOrWhiteSpace(k.Description) && string.IsNullOrWhiteSpace(k.Original), k.Id));
    }
}
