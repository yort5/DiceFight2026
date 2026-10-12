using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// Completed imports use the ordinary setup, turn, combat and ability paths.
public class ImportedCardsTests
{
    private sealed class RecordingRoller(int faceIndex) : IDiceRoller
    {
        public int Calls { get; private set; }
        public int Roll(DieDefinition die) { Calls++; return faceIndex; }
    }

    private static GameState NewGame(CardType actionType = CardType.BasicAction)
    {
        var catalog = DiceKingdomConfig.Catalog.ToDictionary(entry => entry.Key, entry => entry.Value);
        catalog[DiceKingdomConfig.AngerIssues.Id] = DiceKingdomConfig.AngerIssues with { CardType = actionType };
        Player Player(string id) => new()
        {
            Id = id, Name = id,
            CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Eye", 8)],
        };
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, catalog, Player("p1"), Player("p2"));
        state.MoveToStep(StepIds.Main);
        return state;
    }

    private static DieInstance Add(GameState state, string cardId, string owner, Zone zone, int level = 1)
    {
        var card = state.CardCatalog[cardId];
        var faceIndex = card.Die.Faces.Select((face, index) => (face, index))
            .First(pair => card.CardType.IsActionDie()
                ? pair.face.Kind == FaceKind.ActionFace : pair.face.Character?.Level == level).index;
        var die = new DieInstance
        {
            Id = $"{owner}-{cardId}-{state.Dice.Count}", CardId = cardId,
            OwnerId = owner, ControllerId = owner, Zone = zone, CurrentFaceIndex = faceIndex,
        };
        state.Dice.Add(die);
        return die;
    }

    private static string[] Energy(GameState state, int count = 1) =>
        state.DiceIn("p1", Zone.Bag).Where(die => die.CardId is null).Take(count).Select(die =>
        {
            die.Zone = Zone.ReservePool;
            die.CurrentFaceIndex = 2; // One Eye: no overspend.
            return die.Id;
        }).ToArray();

    private static int Level(GameState state, DieInstance die) => state.GetCurrentFace(die)!.Character!.Level;

    private static void Drain(GameState state, AbilityQueue queue, IDiceRoller? roller = null) =>
        EffectInterpreter.DrainQueue(state, queue, roller ?? new RecordingRoller(2), new Random(1));

    private static void ResolveChoices(GameState state, AbilityQueue queue, string? preferred = null)
    {
        Drain(state, queue);
        for (var i = 0; state.PendingChoice is { } pending; i++)
        {
            Assert.True(i < 20, "Ability choices did not finish.");
            var pick = preferred is not null && pending.CandidateIds.Contains(preferred)
                ? preferred : pending.CandidateIds[0];
            EffectInterpreter.AnswerPendingChoice(state, [pick]);
            Drain(state, queue);
        }
    }

    [Fact]
    public void Puffin_Fields_And_Draws_And_Rolls_One_Die()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var puffin = Add(state, "MSW020", "p1", Zone.ReservePool);
        var roller = new RecordingRoller(2);
        TurnEngine.Field(state, queue, puffin.Id, []);
        Drain(state, queue, roller);

        Assert.Equal(Zone.FieldZone, puffin.Zone);
        Assert.Single(state.DiceIn("p1", Zone.ReservePool));
        Assert.Equal(1, roller.Calls);
        Assert.Null(state.PendingChoice);
    }

    [Theory]
    [InlineData(5, 3, 2)]
    [InlineData(3, 5, 0)]
    public void Puffin_Energize_Checks_The_Final_Roll_Result_Once(int initialFace, int rerollFace, int drawn)
    {
        var state = NewGame();
        state.MoveToStep(StepIds.RollAndReroll);
        var queue = new AbilityQueue();
        var puffin = Add(state, "MSW020", "p1", Zone.DiceFromBag);
        puffin.CurrentFaceIndex = null;
        TurnEngine.Roll(state, queue, new RecordingRoller(initialFace));
        Assert.Empty(queue.Pending);
        TurnEngine.RerollOwn(state, queue, new RecordingRoller(rerollFace), [puffin.Id]);
        var roller = new RecordingRoller(2);
        Drain(state, queue, roller);

        Assert.Equal(drawn, roller.Calls);
        Assert.Equal(drawn + 1, state.DiceIn("p1", Zone.ReservePool).Count());
        Assert.Equal(TurnStep.Main, state.CurrentStep);
    }

    [Fact]
    public void Puffin_Energize_Also_Responds_To_A_Later_Ability_Reroll()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var puffin = Add(state, "MSW020", "p1", Zone.ReservePool);
        puffin.CurrentFaceIndex = 5; // Single Wing.
        var context = new EffectContext
        {
            State = state, Queue = queue, ControllerId = "p1", Trigger = TriggerKind.Global,
            Roller = new RecordingRoller(3), Random = new Random(1),
        };
        context.Bind("self", puffin.Id);
        EffectInterpreter.Execute(new Reroll(new TargetFilter(Self: true)), context);
        var roller = new RecordingRoller(2);
        Drain(state, queue, roller);

        Assert.Equal(2, roller.Calls);
        Assert.Equal(3, state.DiceIn("p1", Zone.ReservePool).Count());
    }

    [Theory]
    [InlineData(CardType.BasicAction)]
    [InlineData(CardType.Action)]
    public void Amplify_Spins_Each_Own_Active_Copy_For_Any_Action_Die(CardType actionType)
    {
        var state = NewGame(actionType);
        var queue = new AbilityQueue();
        var low = Add(state, "JLL002", "p1", Zone.FieldZone);
        var middle = Add(state, "JLL002", "p1", Zone.FieldZone, level: 2);
        var top = Add(state, "JLL002", "p1", Zone.FieldZone, level: 3);
        var reserve = Add(state, "JLL002", "p1", Zone.ReservePool);
        var opposing = Add(state, "JLL002", "p2", Zone.FieldZone);
        var action = Add(state, DiceKingdomConfig.AngerIssues.Id, "p1", Zone.ReservePool);
        TurnEngine.UseAction(state, queue, action.Id);
        ResolveChoices(state, queue, low.Id);

        Assert.Equal(2, Level(state, low));
        Assert.Equal(3, Level(state, middle));
        Assert.Equal(3, Level(state, top));
        Assert.Equal(1, Level(state, reserve));
        Assert.Equal(1, Level(state, opposing));
    }

    [Fact]
    public void Amplify_Does_Not_Respond_To_A_Global()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var stag = Add(state, "JLL002", "p1", Zone.FieldZone);
        var global = DiceKingdomConfig.Mutation.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);
        var down = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone, level: 2);
        TurnEngine.UseGlobal(state, queue, DiceKingdomConfig.Mutation.Id, "p1", global, Energy(state));
        ResolveChoices(state, queue, down.Id); // Mutation spins this die down, then back up.

        Assert.Equal(1, Level(state, stag));
    }

    [Fact]
    public void Granted_Amplify_Uses_The_Same_Spin_And_Can_Trigger_Awaken()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var swallow = Add(state, DiceKingdomConfig.BarnSwallow.Id, "p1", Zone.FieldZone);
        swallow.GrantedTags.Add(new GrantedTag("Amplify", Duration.EndOfTurn, "p1"));
        var action = Add(state, DiceKingdomConfig.AngerIssues.Id, "p1", Zone.ReservePool);
        TurnEngine.UseAction(state, queue, action.Id);
        ResolveChoices(state, queue, swallow.Id);

        Assert.Equal(2, Level(state, swallow));
        Assert.Single(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void Blanked_Amplify_Does_Not_Respond_To_Action_Use()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var stag = Add(state, "JLL002", "p1", Zone.FieldZone);
        var context = new EffectContext
        {
            State = state, Queue = queue, ControllerId = "p1", Trigger = TriggerKind.Global,
            Roller = new RecordingRoller(2), Random = new Random(1),
        };
        context.Bind("self", stag.Id);
        EffectInterpreter.Execute(new BlankText(new TargetFilter(Self: true)), context);
        var action = Add(state, DiceKingdomConfig.AngerIssues.Id, "p1", Zone.ReservePool);
        TurnEngine.UseAction(state, queue, action.Id);
        ResolveChoices(state, queue, stag.Id);

        Assert.Equal(1, Level(state, stag));
    }

    [Theory]
    [InlineData("CW014", 0)]
    [InlineData("DPS061", 1)]
    [InlineData("DPS093", 0)]
    public void Imported_Intimidate_Fielding_Resolves_All_Clauses_And_Returns_The_Target(string cardId, int lifeLost)
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var target = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone, level: 2);
        var source = Add(state, cardId, "p1", Zone.ReservePool);
        var cost = state.GetCurrentFace(source)!.Character!.FieldingCost;
        TurnEngine.Field(state, queue, source.Id, Energy(state, cost));
        Drain(state, queue);

        Assert.Equal(20 - lifeLost, state.PlayerOne.Life);
        Assert.Equal(Zone.Intimidated, target.Zone);
        Assert.Equal(2, Level(state, target));
        state.MoveToStep(StepIds.ReturnToField);
        TurnEngine.CleanUp(state, queue);
        Assert.Equal(Zone.FieldZone, target.Zone);
        Assert.Equal(2, Level(state, target));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prairie_Dog_Offers_Tag_Out_After_Blocks_And_Respects_The_Choice(bool accept)
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        var blocker = Add(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone);
        var prairie = Add(state, "TAG003", "p2", Zone.FieldZone);
        var before = (QueryEngine.GetAttack(state, blocker), QueryEngine.GetDefense(state, blocker));
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        Drain(state, queue);
        var choice = Assert.IsType<PendingChoice>(state.PendingChoice);
        Assert.Equal("p2", choice.ControllerId);
        EffectInterpreter.AnswerPendingChoice(state, accept ? [prairie.Id] : []);
        ResolveChoices(state, queue, blocker.Id);

        Assert.Equal(accept ? Zone.PrepArea : Zone.FieldZone, prairie.Zone);
        Assert.Equal(before.Item1 + (accept ? 2 : 0), QueryEngine.GetAttack(state, blocker));
        Assert.Equal(before.Item2 + (accept ? 2 : 0), QueryEngine.GetDefense(state, blocker));
    }

    [Fact]
    public void Spitting_Cobra_Deals_Its_Printed_Range_2_Damage()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var cobra = Add(state, "SKC090", "p1", Zone.FieldZone);
        var target = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone, level: 3);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [cobra.Id]);
        Drain(state, queue);

        Assert.Equal(2, target.Damage);
        Assert.Empty(state.RangeShots);
    }

    [Fact]
    public void Deathstalker_Scorpion_KOs_Its_Surviving_Blocker_At_Cleanup_Even_If_The_Scorpion_Dies()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var scorpion = Add(state, "DPS029", "p1", Zone.FieldZone);
        var blocker = Add(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone, level: 3);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [scorpion.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(scorpion.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.Equal(Zone.PrepArea, scorpion.Zone);
        Assert.Equal(Zone.FieldZone, blocker.Zone);
        state.MoveToStep(StepIds.ReturnToField);
        TurnEngine.CleanUp(state, queue);
        Assert.Equal(Zone.PrepArea, blocker.Zone);
    }

    [Fact]
    public void Giant_Centipede_Intimidates_Then_Deals_Overcrush_Damage_In_Combat()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var removed = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        var blocker = Add(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone);
        var centipede = Add(state, "DPS093", "p1", Zone.ReservePool, level: 3);
        TurnEngine.Field(state, queue, centipede.Id, Energy(state, 2));
        ResolveChoices(state, queue, removed.Id);
        Assert.Equal(Zone.Intimidated, removed.Zone);

        var expectedDamage = QueryEngine.GetAttack(state, centipede) - QueryEngine.GetDefense(state, blocker);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [centipede.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(centipede.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());

        Assert.Equal(Zone.PrepArea, blocker.Zone);
        Assert.Equal(20 - expectedDamage, state.PlayerTwo.Life);
    }
}
