using DiceFight.Api.Controllers;
using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.Api.Tests;

// Basic Actions + Globals (2026-09-26): each Champion brings one shared
// Basic Action whose Global either player can use, paid with 1 energy of
// that Champion's type.
public class V2ActionsAndGlobalsTests
{
    private static (V2GameStore Store, V2GameSession Session, V2GamesController TeamA, V2GamesController TeamB) StartInMain()
    {
        var store = new V2GameStore();
        var anon = V2SeatedController.Anonymous(store);
        var created = V2SeatedController.CreatedDto(anon.Create(new CreateV2GameRequest("Wolf", "GreatHornedOwl")));
        var session = store.GetSession(created.Game.GameId);
        var teamA = V2SeatedController.For(store, session, "teamA");
        var teamB = V2SeatedController.For(store, session, "teamB");
        teamA.ClearAndDraw(session.Id);
        teamA.Roll(session.Id);
        teamA.FinishRoll(session.Id);
        Assert.Equal(TurnStep.Main, session.State.CurrentStep);
        // Clear the rolled Reserve Pool so each test places exactly the dice it needs.
        foreach (var d in session.State.Dice.Where(d => d.Zone == Zone.ReservePool)) { d.Zone = Zone.UsedPile; d.CurrentFaceIndex = null; }
        return (store, session, teamA, teamB);
    }

    private static DieInstance Tardigrade(GameState state, string player, Zone zone, int face)
    {
        var die = state.Dice.First(d => d.ControllerId == player && d.CardId is null && d.Zone is Zone.Bag or Zone.UsedPile);
        die.Zone = zone;
        die.CurrentFaceIndex = face;
        return die;
    }

    // The Active player's button reads "Pass Priority" when passing would
    // hand the other player a chance to use a Global, "Resolve Damage" when
    // it would just close the window (2026-09-30).
    [Fact]
    public void PassGivesPriority_Says_Whether_The_Other_Player_Could_Respond()
    {
        var (_, session, teamA, _) = StartInMain();
        var state = session.State;
        Tardigrade(state, "teamA", Zone.FieldZone, 3); // a creature for Mutation's Global to spin

        Assert.False(V2SeatedController.Dto(teamA.Get(session.Id)).PassGivesPriority); // Owl has no energy

        Tardigrade(state, "teamB", Zone.ReservePool, 2); // one Eye - pays Mutation's Global
        Assert.True(V2SeatedController.Dto(teamA.Get(session.Id)).PassGivesPriority);
    }

    [Fact]
    public void Each_Champion_Brings_Three_Of_Its_Own_Basic_Action_Dice()
    {
        var (_, session, _, _) = StartInMain();
        var state = session.State;
        Assert.Equal(3, state.Dice.Count(d => d.OwnerId == "teamA" && d.CardId == DiceKingdomConfig.AngerIssues.Id && d.Zone == Zone.Unpurchased));
        Assert.Equal(3, state.Dice.Count(d => d.OwnerId == "teamB" && d.CardId == DiceKingdomConfig.Mutation.Id && d.Zone == Zone.Unpurchased));
    }

    [Fact]
    public void Either_Player_Can_Buy_The_Opponents_Champions_Action_Die()
    {
        var (_, session, teamA, _) = StartInMain();
        var state = session.State;
        var pay = new[] { Tardigrade(state, "teamA", Zone.ReservePool, 0), Tardigrade(state, "teamA", Zone.ReservePool, 0) }; // 2 Claw each
        var owlsAction = state.Dice.First(d => d.OwnerId == "teamB" && d.CardId == DiceKingdomConfig.Mutation.Id);

        teamA.Purchase(session.Id, new V2PurchaseRequest(owlsAction.Id, pay.Select(d => d.Id).ToList()));

        Assert.Equal("teamA", owlsAction.ControllerId);
        Assert.Equal(Zone.UsedPile, owlsAction.Zone);
    }

    [Fact]
    public void Global_Costs_One_Of_Its_Champions_Energy_And_Only_This_Games_Globals_Are_Usable()
    {
        var (_, session, teamA, teamB) = StartInMain();
        var state = session.State;
        var body = Tardigrade(state, "teamA", Zone.FieldZone, 3); // 1/1
        var claw = Tardigrade(state, "teamA", Zone.ReservePool, 0); // 2 Claw
        var angerIssues = DiceKingdomConfig.AngerIssues;
        var globalIndex = angerIssues.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);

        // Not in this game (Wolf vs Owl): Resurrection is Golden Eagle's.
        Assert.Throws<InvalidOperationException>(() =>
            teamA.UseGlobal(session.Id, new V2UseGlobalRequest(DiceKingdomConfig.Resurrection.Id, globalIndex, [claw.Id])));
        // The opponent can't use Globals during your Main Step.
        Assert.Throws<InvalidOperationException>(() =>
            teamB.UseGlobal(session.Id, new V2UseGlobalRequest(angerIssues.Id, globalIndex, [])));

        var dto = V2SeatedController.Dto(teamA.UseGlobal(session.Id, new V2UseGlobalRequest(angerIssues.Id, globalIndex, [claw.Id])));

        Assert.Equal(2, dto.Dice.Single(d => d.Id == body.Id).EffectiveAttack); // +1A from the Global
        Assert.Equal(1, dto.Dice.Single(d => d.Id == claw.Id).EnergyAmount); // 2 Claw, 1 spent - spun down to its 1-Claw face
        Assert.Contains(dto.Log, l => l.Text.Contains("Anger Issues's Global"));
    }

    // Priority (rules 2.6.6 / 2.7.3.4, Priority.cs): Active acts freely
    // and passes; Inactive gets ONE thing or passes; Inactive passing
    // right after Active ends the window.
    [Fact]
    public void Priority_Walks_Main_And_The_Attack_Window_As_In_Dice_Masters()
    {
        var (_, session, teamA, teamB) = StartInMain();
        var state = session.State;
        var attacker = Tardigrade(state, "teamA", Zone.FieldZone, 3);
        Tardigrade(state, "teamB", Zone.FieldZone, 3); // a creature for Mutation's spins
        var eye1 = Tardigrade(state, "teamB", Zone.ReservePool, 0); // 2 Eye, left over from their turn
        var eye2 = Tardigrade(state, "teamB", Zone.ReservePool, 0); // 2 more
        var mutation = DiceKingdomConfig.Mutation;
        var globalIndex = mutation.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);
        var useMutation = (string eyeId) => new V2UseGlobalRequest(mutation.Id, globalIndex, [eyeId]);
        // Mutation's "spin another creature up" has two candidates - answer it.
        void AnswerChoice()
        {
            if (state.PendingChoice is { } c) teamB.ResolvePendingChoice(session.Id, new V2ResolvePendingChoiceRequest([c.CandidateIds[0]]));
        }

        // Main: the Active player holds priority first; the Inactive can't act yet.
        Assert.Equal("teamA", V2SeatedController.Dto(teamA.Get(session.Id)).PriorityPlayerId);
        Assert.Throws<InvalidOperationException>(() => teamB.UseGlobal(session.Id, useMutation(eye1.Id)));

        // Active passes ("Done buying") - Owl can pay for Mutation, so priority goes to them, still in Main.
        var dto = V2SeatedController.Dto(teamA.EnterAttackStep(session.Id));
        Assert.Equal(StepIds.Main, dto.CurrentStepId);
        Assert.Equal("teamB", dto.PriorityPlayerId);
        Assert.Throws<InvalidOperationException>(() => teamA.Purchase(session.Id, new V2PurchaseRequest("x", [])));

        // Inactive does ONE thing - priority returns to the Active player.
        dto = V2SeatedController.Dto(teamB.UseGlobal(session.Id, useMutation(eye1.Id)));
        Assert.Equal("teamA", dto.PriorityPlayerId);
        AnswerChoice();
        Assert.Throws<InvalidOperationException>(() => teamB.UseGlobal(session.Id, useMutation(eye2.Id)));

        // Active passes again; Inactive passes; Main ends and the attack begins.
        teamA.EnterAttackStep(session.Id);
        dto = V2SeatedController.Dto(teamB.Pass(session.Id));
        Assert.Equal(StepIds.SelectAttackers, dto.CurrentStepId);
        Assert.Null(dto.PriorityPlayerId);

        // The Attack Step's action window runs the same way.
        teamA.DeclareAttackers(session.Id, new V2DeclareAttackersRequest([new V2AttackerDeclaration(attacker.Id, 0)]));
        dto = V2SeatedController.Dto(teamB.DeclareBlockers(session.Id, new V2DeclareBlockersRequest([])));
        Assert.Equal(StepIds.ActionGlobalWindow, dto.CurrentStepId);
        Assert.Equal("teamA", dto.PriorityPlayerId);
        dto = V2SeatedController.Dto(teamA.AssignCombatDamage(session.Id, new V2AssignCombatDamageRequest([])));
        Assert.Equal("teamB", dto.PriorityPlayerId); // still has Eye energy
        teamB.UseGlobal(session.Id, useMutation(eye2.Id));
        AnswerChoice();
        teamA.AssignCombatDamage(session.Id, new V2AssignCombatDamageRequest([]));
        dto = V2SeatedController.Dto(teamB.Pass(session.Id));
        Assert.NotEqual(StepIds.ActionGlobalWindow, dto.CurrentStepId); // damage resolved
        Assert.Equal(2, state.Log.Count(l => l.Text == "Great Horned Owl uses Mutation's Global."));
        Assert.Equal(2, state.Log.Count(l => l.Text == "Great Horned Owl passes."));
    }

    // The reported scenario: Wolf passes, Owl uses Mutation with its only
    // energy, Wolf passes again. Owl still gets its turn - it just can't
    // pay for anything, so it passes automatically - and the log says so.
    [Fact]
    public void A_Global_Is_Never_A_Pass_And_The_Automatic_Pass_Is_Logged()
    {
        var (_, session, teamA, teamB) = StartInMain();
        var state = session.State;
        Tardigrade(state, "teamB", Zone.FieldZone, 3);
        Tardigrade(state, "teamA", Zone.FieldZone, 3);
        var wild = Tardigrade(state, "teamB", Zone.ReservePool, 5); // 1 Wild, their only energy
        var mutation = DiceKingdomConfig.Mutation;
        var globalIndex = mutation.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);

        teamA.EnterAttackStep(session.Id);                      // Wolf passes
        teamB.UseGlobal(session.Id, new V2UseGlobalRequest(mutation.Id, globalIndex, [wild.Id])); // Owl's one thing
        if (state.PendingChoice is { } c) teamB.ResolvePendingChoice(session.Id, new V2ResolvePendingChoiceRequest([c.CandidateIds[0]]));
        Assert.Equal(StepIds.Main, state.CurrentStepId);        // a Global never ends the window
        var dto = V2SeatedController.Dto(teamA.EnterAttackStep(session.Id)); // Wolf passes again

        Assert.Equal(StepIds.SelectAttackers, dto.CurrentStepId);
        var tail = dto.Log.Select(l => l.Text).ToList();
        Assert.Equal("Great Horned Owl passes (no energy left for a Global).",
            tail.Last(t => t.StartsWith("Great Horned Owl")));
    }

    [Fact]
    public void An_Opponent_Who_Cant_Pay_For_Any_Global_Passes_Automatically()
    {
        var (_, session, teamA, _) = StartInMain(); // teamB's Reserve Pool is empty
        var dto = V2SeatedController.Dto(teamA.EnterAttackStep(session.Id));
        Assert.Equal(StepIds.SelectAttackers, dto.CurrentStepId);
    }

    [Fact]
    public void Action_Die_Is_Usable_Only_On_Its_Action_Face()
    {
        var (_, session, teamA, _) = StartInMain();
        var state = session.State;
        var body = Tardigrade(state, "teamA", Zone.FieldZone, 3); // L2: 1A
        var action = state.Dice.First(d => d.OwnerId == "teamA" && d.CardId == DiceKingdomConfig.AngerIssues.Id);
        action.Zone = Zone.ReservePool;
        action.CurrentFaceIndex = 3; // an energy face

        Assert.Throws<InvalidOperationException>(() => teamA.UseAction(session.Id, new V2UseActionRequest(action.Id)));
        Assert.False(V2SeatedController.Dto(teamA.Get(session.Id)).Dice.Single(d => d.Id == action.Id).IsActionFace);

        action.CurrentFaceIndex = 0; // an action face
        var dto = V2SeatedController.Dto(teamA.UseAction(session.Id, new V2UseActionRequest(action.Id)));

        Assert.Equal(4, dto.Dice.Single(d => d.Id == body.Id).EffectiveAttack); // +3A
        Assert.Contains("Overcrush", QueryEngine.GetKeywords(state, body));
        Assert.Equal(Zone.OutOfPlay, action.Zone);
    }

    private static (V2GameSession Session, V2GamesController A, V2GamesController B) StartInMainAs(string p1, string p2)
    {
        var store = new V2GameStore();
        var created = V2SeatedController.CreatedDto(V2SeatedController.Anonymous(store).Create(new CreateV2GameRequest(p1, p2)));
        var session = store.GetSession(created.Game.GameId);
        var a = V2SeatedController.For(store, session, "teamA");
        var b = V2SeatedController.For(store, session, "teamB");
        a.ClearAndDraw(session.Id); a.Roll(session.Id); a.FinishRoll(session.Id);
        foreach (var d in session.State.Dice.Where(d => d.Zone == Zone.ReservePool)) { d.Zone = Zone.UsedPile; d.CurrentFaceIndex = null; }
        return (session, a, b);
    }

    // Direct feedback (2026-09-27): paying a 1-cost fielding with a
    // double-energy Tardigrade didn't spin it down.
    private static DieInstance CharacterAtCost(GameState state, string player, int printedCost)
    {
        foreach (var die in state.Dice.Where(d => d.ControllerId == player && d.CardId is { } c && state.CardCatalog[c].CardType == CardType.Character))
        {
            var faces = state.CardCatalog[die.CardId!].Die.Faces;
            var i = faces.ToList().FindIndex(f => f.Character?.FieldingCost == printedCost);
            if (i < 0) continue;
            die.Zone = Zone.ReservePool;
            die.CurrentFaceIndex = i;
            return die;
        }
        throw new InvalidOperationException($"No character with a {printedCost}-cost level.");
    }

    [Fact]
    public void Owl_Paying_1_With_A_Double_Energy_Tardigrade_Spins_It_Down()
    {
        var (session, a, _) = StartInMainAs("GreatHornedOwl", "Wolf");
        var state = session.State;
        var creature = CharacterAtCost(state, "teamA", 1);
        var payer = Tardigrade(state, "teamA", Zone.ReservePool, 0); // 2 Eye

        var dto = V2SeatedController.Dto(a.Field(session.Id, new V2FieldRequest(creature.Id, [payer.Id])));

        Assert.Equal(1, dto.Dice.Single(d => d.Id == creature.Id).FieldingCost ?? 1); // Owl has no fielding discount
        Assert.Equal("FieldZone", dto.Dice.Single(d => d.Id == creature.Id).Zone);
        var paid = dto.Dice.Single(d => d.Id == payer.Id);
        Assert.Equal("ReservePool", paid.Zone);
        Assert.Equal(1, paid.EnergyAmount); // spun down, 1 Eye left
    }

    [Fact]
    public void Golden_Eagle_Fields_One_Creature_Free_Once_Per_Turn_And_Costs_Reach_The_Client()
    {
        // Golden Eagle's power (2026-10-04, was -1 fielding): once per turn,
        // field one creature without paying.
        var (eagleGame, eagle, _) = StartInMainAs("GoldenEagle", "Wolf");
        var creature = CharacterAtCost(eagleGame.State, "teamA", 1);
        var second = CharacterAtCost(eagleGame.State, "teamA", 1);
        var view = V2SeatedController.Dto(eagle.Get(eagleGame.Id));
        Assert.Equal(1, view.Dice.Single(d => d.Id == creature.Id).FieldingCost); // printed - no discount any more
        Assert.True(view.PlayerOne.FreeFieldAvailable);
        var after = V2SeatedController.Dto(eagle.Field(eagleGame.Id, new V2FieldRequest(creature.Id, [], Free: true)));
        Assert.Equal("FieldZone", after.Dice.Single(d => d.Id == creature.Id).Zone);
        Assert.False(after.PlayerOne.FreeFieldAvailable);
        Assert.Throws<InvalidOperationException>(() => eagle.Field(eagleGame.Id, new V2FieldRequest(second.Id, [], Free: true)));

        // Purchase costs come through per player too - at printed price now
        // that Owl's purchase discount is gone (replaced by Foresight).
        var (owlGame, owl, _) = StartInMainAs("GreatHornedOwl", "Wolf");
        var anyCard = owlGame.State.PlayerOne.TeamCardIds[0];
        Assert.Equal(owlGame.State.CardCatalog[anyCard].PurchaseCost, V2SeatedController.Dto(owl.Get(owlGame.Id)).PurchaseCosts![anyCard]);
    }

    // Great Horned Owl's power (2026-10-04, replaced Foresight): once per
    // your turn, spin one creature a level - yours up, theirs down. Used
    // through the champion-power endpoint; the target is a pending choice.
    [Fact]
    public void Owl_Spins_A_Creature_Through_The_Champion_Power_Endpoint()
    {
        var (session, owl, wolf) = StartInMainAs("GreatHornedOwl", "Wolf");
        var state = session.State;
        var mine = Tardigrade(state, "teamA", Zone.FieldZone, 3); // L2
        var theirs = Tardigrade(state, "teamB", Zone.FieldZone, 4); // L3 Bulwark
        Assert.True(V2SeatedController.Dto(owl.Get(session.Id)).PlayerOne.ChampionPowerUsable);
        Assert.False(V2SeatedController.Dto(owl.Get(session.Id)).PlayerTwo.ChampionPowerUsable); // not Wolf's turn

        var dto = V2SeatedController.Dto(owl.ChampionPower(session.Id));
        Assert.NotNull(dto.PendingChoice);
        Assert.Contains("Great Horned Owl", dto.PendingChoice!.Description);
        dto = V2SeatedController.Dto(owl.ResolvePendingChoice(session.Id, new V2ResolvePendingChoiceRequest([theirs.Id])));

        Assert.Equal(2, state.GetCurrentFace(theirs)!.Character!.Level); // theirs spins down
        Assert.Equal(2, state.GetCurrentFace(mine)!.Character!.Level);
        Assert.Equal("Great Horned Owl", dto.Dice.Single(d => d.Id == theirs.Id).LastSpin!.Source);
        Assert.False(dto.PlayerOne.ChampionPowerUsable); // once per turn
        Assert.Throws<InvalidOperationException>(() => owl.ChampionPower(session.Id));
    }

    // Direct feedback (2026-09-27): Resurrection's Global "did not work" -
    // with an empty Bag it drew nothing, where the rules refill the Bag
    // from the Used Pile first (2.3.2).
    [Fact]
    public void Resurrections_Global_Refills_An_Empty_Bag()
    {
        var (session, a, _) = StartInMainAs("GoldenEagle", "Wolf");
        var state = session.State;
        foreach (var d in state.Dice.Where(d => d.ControllerId == "teamA" && d.Zone == Zone.Bag)) d.Zone = Zone.UsedPile;
        var wing = Tardigrade(state, "teamA", Zone.ReservePool, 0); // 2 Wing
        var index = DiceKingdomConfig.Resurrection.Abilities.ToList().FindIndex(x => x.Trigger == TriggerKind.Global);

        a.UseGlobal(session.Id, new V2UseGlobalRequest(DiceKingdomConfig.Resurrection.Id, index, [wing.Id]));

        Assert.Single(state.DiceIn("teamA", Zone.PrepArea));
        Assert.Contains(state.Log, l => l.Text.Contains("draws 1 die into their PrepArea"));
    }

    // Playtest report (2026-09-27): 2A + 1A attacking in one lane, blocked
    // by a 3D Tardigrade; the preview said KO, but it stayed on the field.
    // (Bulwark is 1/2 since 2026-10-04 - +1D keeps the report's numbers.)
    [Fact]
    public void Two_Attackers_Pooling_3_Damage_KO_A_3_Defense_Blocker_Into_Prep()
    {
        var (session, a, b) = StartInMainAs("Wolf", "GreatHornedOwl");
        var state = session.State;
        var twoA = Tardigrade(state, "teamA", Zone.FieldZone, 3); // L2 1/1, +1A below = 2A
        twoA.AppliedModifiers.Add(new AppliedModifier(1, 0, 0, "test", Duration.EndOfTurn));
        var oneA = Tardigrade(state, "teamA", Zone.FieldZone, 3); // L2 1/1 = 1A
        var escort = Tardigrade(state, "teamA", Zone.FieldZone, 3); // the stacking rule: another lane as full
        var wall = Tardigrade(state, "teamB", Zone.FieldZone, 4); // Bulwark 1/2, +1D below = 3D
        wall.AppliedModifiers.Add(new AppliedModifier(0, 1, 0, "test", Duration.EndOfTurn));

        a.EnterAttackStep(session.Id);
        a.DeclareAttackers(session.Id, new V2DeclareAttackersRequest([new V2AttackerDeclaration(twoA.Id, 0), new V2AttackerDeclaration(oneA.Id, 0), new V2AttackerDeclaration(escort.Id, 1)]));
        b.DeclareBlockers(session.Id, new V2DeclareBlockersRequest([new V2BlockAssignment(twoA.Id, wall.Id)]));
        a.AssignCombatDamage(session.Id, new V2AssignCombatDamageRequest([]));

        Assert.Equal(Zone.PrepArea, wall.Zone);
    }

    // The stacking rule (2026-10-16): two in one lane with no other lane as
    // full is refused.
    [Fact]
    public void Two_Attackers_In_One_Lane_Need_An_Attacker_In_Another()
    {
        var (session, a, _) = StartInMainAs("Wolf", "GreatHornedOwl");
        var state = session.State;
        var one = Tardigrade(state, "teamA", Zone.FieldZone, 3);
        var two = Tardigrade(state, "teamA", Zone.FieldZone, 3);

        a.EnterAttackStep(session.Id);
        Assert.Throws<InvalidOperationException>(() =>
            a.DeclareAttackers(session.Id, new V2DeclareAttackersRequest([new V2AttackerDeclaration(one.Id, 0), new V2AttackerDeclaration(two.Id, 0)])));
        Assert.Equal(StepIds.SelectAttackers, state.CurrentStepId);
    }

    // Same report: Distraction's Global ("move one attacker back to its
    // Field Zone") could target the defender's own BLOCKER, which also
    // stands in the Attack Zone - pulling it out of a lethal block.
    [Fact]
    public void Distractions_Global_Can_Only_Move_An_Attacker_Not_A_Blocker()
    {
        var (session, a, b) = StartInMainAs("Wolf", "Armadillo");
        var state = session.State;
        // Distraction left Armadillo's team for Archnemesis (2026-09-29) but
        // is still in the catalog - put it back in this game directly (a
        // Global is card-scoped, so the card being in the game is enough).
        state.PlayerTwo.TeamCardIds.Add(DiceKingdomConfig.Distraction.Id);
        var attacker1 = Tardigrade(state, "teamA", Zone.FieldZone, 3);
        var attacker2 = Tardigrade(state, "teamA", Zone.FieldZone, 5);
        var wall = Tardigrade(state, "teamB", Zone.FieldZone, 4);
        var shell = Tardigrade(state, "teamB", Zone.ReservePool, 0); // 2 Shell, left over from their turn
        var index = DiceKingdomConfig.Distraction.Abilities.ToList().FindIndex(x => x.Trigger == TriggerKind.Global);

        // Armadillo holds Shell, so Main offers it priority - not for
        // Distraction (no attackers yet, rule 3.1.10), but for its own
        // Archnemesis's Global, which has creatures to target.
        a.EnterAttackStep(session.Id);
        b.Pass(session.Id);
        Assert.Equal(StepIds.SelectAttackers, state.CurrentStepId);
        a.DeclareAttackers(session.Id, new V2DeclareAttackersRequest([new V2AttackerDeclaration(attacker1.Id, 0), new V2AttackerDeclaration(attacker2.Id, 1)]));
        b.DeclareBlockers(session.Id, new V2DeclareBlockersRequest([new V2BlockAssignment(attacker1.Id, wall.Id)]));
        a.AssignCombatDamage(session.Id, new V2AssignCombatDamageRequest([])); // Wolf passes; Armadillo may respond
        b.UseGlobal(session.Id, new V2UseGlobalRequest(DiceKingdomConfig.Distraction.Id, index, [shell.Id]));

        Assert.NotNull(state.PendingChoice);
        Assert.Equal([attacker1.Id, attacker2.Id], state.PendingChoice!.CandidateIds.Order().ToList());
        Assert.DoesNotContain(wall.Id, state.PendingChoice.CandidateIds);
    }

    // Playtest feedback (2026-09-27): Mutation's Global's two picks looked
    // identical. Each now says which it is, and offers only dice that can
    // move that way.
    [Fact]
    public void Mutations_Global_Says_Which_Pick_Is_Which()
    {
        var (session, a, _) = StartInMainAs("GreatHornedOwl", "Wolf");
        var state = session.State;
        // A Tardigrade's levels run 2-3 (2026-10-04).
        var low = Tardigrade(state, "teamA", Zone.FieldZone, 3);  // level 2 - its lowest, can't go down
        var mid1 = Tardigrade(state, "teamA", Zone.FieldZone, 4); // level 3
        var mid2 = Tardigrade(state, "teamA", Zone.FieldZone, 4); // level 3
        var top = Tardigrade(state, "teamB", Zone.FieldZone, 4);  // level 3 - can't go up
        var eye = Tardigrade(state, "teamA", Zone.ReservePool, 0);
        var index = DiceKingdomConfig.Mutation.Abilities.ToList().FindIndex(x => x.Trigger == TriggerKind.Global);

        a.UseGlobal(session.Id, new V2UseGlobalRequest(DiceKingdomConfig.Mutation.Id, index, [eye.Id]));

        var down = state.PendingChoice!;
        Assert.Equal("Mutation: first, choose one of YOUR creatures to spin DOWN a level.", down.Description);
        Assert.Equal([mid1.Id, mid2.Id], down.CandidateIds.Order().ToList());
        a.ResolvePendingChoice(session.Id, new V2ResolvePendingChoiceRequest([mid1.Id]));

        var up = state.PendingChoice!;
        Assert.Equal("Mutation: now choose a creature to spin UP a level.", up.Description);
        Assert.Contains(low.Id, up.CandidateIds);
        Assert.DoesNotContain(top.Id, up.CandidateIds);
        a.ResolvePendingChoice(session.Id, new V2ResolvePendingChoiceRequest([low.Id]));

        Assert.Equal(2, state.GetCurrentFace(mid1)!.Character!.Level);
        Assert.Equal(3, state.GetCurrentFace(low)!.Character!.Level);
    }

    [Fact]
    public void Card_Dto_Describes_The_Action_And_Its_Global()
    {
        var dto = V2CardDefDto.From(DiceKingdomConfig.Distraction);
        Assert.True(dto.IsAction);
        Assert.Equal("Shell", dto.DieEnergyType);
        Assert.Equal(1, dto.Global!.Cost);
        Assert.Equal("Shell", dto.Global.EnergyType);
        Assert.StartsWith("Pay 1 Shell.", dto.Global.Text);
        Assert.DoesNotContain("Global", dto.ActionText);
    }
}
