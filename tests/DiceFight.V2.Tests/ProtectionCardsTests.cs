using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

public class ProtectionCardsTests
{
    private sealed class FixedRoller : IDiceRoller
    {
        public int Roll(DieDefinition die) => 2;
    }

    private static GameState NewGame()
    {
        Player Player(string id) => new()
        {
            Id = id, Name = id,
            CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Eye", 4), DiceKingdomConfig.CustomTardigrade("Shell", 4)],
        };
        var one = Player("p1");
        one.TeamCardIds.AddRange(["DPS033", "DPS113", "DPS148", DiceKingdomConfig.AngerIssues.Id]);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, one, Player("p2"));
        state.MoveToStep(StepIds.Main);
        return state;
    }

    private static DieInstance Add(GameState state, string cardId, string owner, Zone zone, int level = 3)
    {
        var definition = state.CardCatalog[cardId].Die;
        var face = definition.Faces.Select((f, i) => (f, i)).First(pair => pair.f.Character?.Level == level).i;
        var die = new DieInstance
        {
            Id = $"{owner}-{cardId}-{state.Dice.Count}", CardId = cardId,
            OwnerId = owner, ControllerId = owner, Zone = zone, CurrentFaceIndex = face,
        };
        state.Dice.Add(die);
        return die;
    }

    private static string[] Energy(GameState state, string type, int count = 1, string player = "p1") =>
        state.DiceIn(player, Zone.Bag).Where(d => d.PoolDieId == "Tardigrade" + type).Take(count).Select(d =>
        {
            d.Zone = Zone.ReservePool; d.CurrentFaceIndex = 2;
            return d.Id;
        }).ToArray();

    private static EffectContext Context(GameState state, AbilityQueue queue, string controller, TriggerKind trigger,
        DieInstance? self = null)
    {
        var ctx = new EffectContext
        {
            State = state, Queue = queue, ControllerId = controller, Trigger = trigger,
            Roller = new FixedRoller(), Random = new Random(1),
        };
        if (self is not null) ctx.Bind("self", self.Id);
        return ctx;
    }

    private static void Drain(GameState state, AbilityQueue queue) =>
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(), new Random(1));

    private static void Global(GameState state, AbilityQueue queue, string cardId, string type, string controller = "p1")
    {
        var index = state.CardCatalog[cardId].Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);
        TurnEngine.UseGlobal(state, queue, cardId, controller, index, Energy(state, type, player: controller));
        Drain(state, queue);
    }

    private static void Cleanup(GameState state, AbilityQueue queue)
    {
        state.MoveToStep(StepIds.ReturnToField);
        TurnEngine.CleanUp(state, queue);
        Drain(state, queue);
    }

    [Theory]
    [InlineData("DPS033", "Eye")]
    [InlineData("DPS113", "Shell")]
    public void Protection_Globals_Block_Both_Players_And_Cover_Later_Fielding_Until_Cleanup(string cardId, string type)
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var mine = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        var theirs = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        Global(state, queue, cardId, type); // No die of the granting card needs to be active.
        Assert.Null(state.PendingChoice);

        foreach (var controller in new[] { "p1", "p2" })
        foreach (var source in new[] { ProtectionFrom.Action, ProtectionFrom.Global })
        {
            Assert.False(QueryEngine.CanBeTargeted(state, mine, controller, source));
            Assert.True(QueryEngine.CanBeTargeted(state, theirs, controller, source));
        }
        var status = Assert.Single(DieStatuses.For(state, mine), s => s.Kind == "targetProtected");
        Assert.Equal(state.CardCatalog[cardId].Name, status.Source);
        Assert.Equal("turn", status.Duration);
        Assert.Contains("either player's", status.Description);

        var later = state.DiceIn("p1", Zone.Bag).First(d => d.CardId is null);
        later.Zone = Zone.ReservePool; later.CurrentFaceIndex = 3; // Wild + L2 Tardigrade.
        Assert.True(QueryEngine.CanBeTargeted(state, later, "p2", ProtectionFrom.Global));
        TurnEngine.Field(state, queue, later.Id, []);
        Drain(state, queue);
        Assert.False(QueryEngine.CanBeTargeted(state, later, "p2", ProtectionFrom.Global));

        Cleanup(state, queue);
        Assert.True(QueryEngine.CanBeTargeted(state, mine, "p2", ProtectionFrom.Global));
        Assert.True(QueryEngine.CanBeTargeted(state, later, "p1", ProtectionFrom.Action));
        Assert.Empty(state.GrantedTargetingProtections);
        Assert.DoesNotContain(DieStatuses.For(state, mine), s => s.Kind == "targetProtected");
    }

    [Fact]
    public void An_Inactive_Player_Uses_The_Shared_Global_To_Protect_Their_Own_Side()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var mine = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        var theirs = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        state.MoveToStep(StepIds.ActionGlobalWindow);
        Global(state, queue, "DPS113", "Shell", "p2");

        Assert.True(QueryEngine.CanBeTargeted(state, mine, "p2", ProtectionFrom.Global));
        Assert.False(QueryEngine.CanBeTargeted(state, theirs, "p1", ProtectionFrom.Global));
        Cleanup(state, queue);
        Assert.True(QueryEngine.CanBeTargeted(state, theirs, "p1", ProtectionFrom.Global));
    }

    [Fact]
    public void Targeting_Protection_Does_Not_Prevent_Creature_Abilities_Or_Nontargeting_Area_Damage()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var mine = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        Global(state, queue, "DPS113", "Shell");
        var target = new TargetFilter(Ownership: TargetOwnership.Opposing);
        EffectInterpreter.Execute(new DealDamage(new Fixed(1), target), Context(state, queue, "p2", TriggerKind.DieUsed));
        Assert.Equal(0, mine.Damage); // A targeted action is blocked.
        EffectInterpreter.Execute(new DealDamage(new Fixed(1), target), Context(state, queue, "p2", TriggerKind.Global));
        Assert.Equal(0, mine.Damage); // A targeted Global is blocked.
        EffectInterpreter.Execute(new DealDamage(new Fixed(1), target), Context(state, queue, "p2", TriggerKind.DieFielded));
        Assert.Equal(1, mine.Damage); // A creature's ability can still target it.
        EffectInterpreter.Execute(new DealDamage(new Fixed(1), target with { Count = 0 }), Context(state, queue, "p2", TriggerKind.DieUsed));
        Assert.Equal(2, mine.Damage); // "Each creature" does not target.
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Combat, mine.Id, 1);
        Assert.Equal(3, mine.Damage);
    }

    [Fact]
    public void A_Targeted_Global_With_No_Legal_Target_Does_Not_Consume_Energy()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        Global(state, queue, "DPS113", "Shell");
        var energyId = Energy(state, "Eye")[0];
        var energy = state.Dice.First(d => d.Id == energyId);
        energy.CurrentFaceIndex = 3; // Wild can pay Anger Issues' Claw cost.
        var index = DiceKingdomConfig.AngerIssues.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);
        Assert.Throws<InvalidOperationException>(() => TurnEngine.UseGlobal(state, queue,
            DiceKingdomConfig.AngerIssues.Id, "p1", index, [energy.Id]));
        Assert.Equal(Zone.ReservePool, energy.Zone);
    }

    [Fact]
    public void Frigatebird_Fields_With_Intimidate_And_Its_Global_Grants_Protection()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var target = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        var frigate = Add(state, "DPS033", "p1", Zone.ReservePool, level: 1);
        TurnEngine.Field(state, queue, frigate.Id, []);
        Drain(state, queue);
        Assert.Equal(Zone.Intimidated, target.Zone);
        Global(state, queue, "DPS033", "Eye");
        Assert.False(QueryEngine.CanBeTargeted(state, frigate, "p2", ProtectionFrom.Action));
        Cleanup(state, queue);
        Assert.Equal(Zone.FieldZone, target.Zone);
        Assert.True(QueryEngine.CanBeTargeted(state, frigate, "p2", ProtectionFrom.Action));
    }

    [Fact]
    public void Bison_Prevents_Noncombat_Damage_To_Allies_But_Not_Itself_Or_Combat_Damage()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var bison = Add(state, "DPS148", "p1", Zone.ReservePool);
        var ally = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        var opposing = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.FieldZone);
        TurnEngine.Field(state, queue, bison.Id, Energy(state, "Eye", 2));
        Drain(state, queue);
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Ability, ally.Id, 1);
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Ability, bison.Id, 1);
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Ability, opposing.Id, 1);
        Assert.Equal(0, ally.Damage);
        Assert.Equal(1, bison.Damage);
        Assert.Equal(1, opposing.Damage);
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Combat, ally.Id, 1);
        Assert.Equal(1, ally.Damage);
        EffectInterpreter.Execute(new MoveDie(new TargetFilter(Self: true), Zone.UsedPile),
            Context(state, queue, "p1", TriggerKind.DieFielded, bison));
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Ability, ally.Id, 1);
        Assert.Equal(2, ally.Damage); // The aura ends when its source leaves.
    }

    [Fact]
    public void Bison_Protects_Other_Copies_Of_Its_Card_And_Blanking_Disables_The_Aura()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var one = Add(state, "DPS148", "p1", Zone.FieldZone);
        var two = Add(state, "DPS148", "p1", Zone.FieldZone);
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Ability, one.Id, 1);
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Ability, two.Id, 1);
        Assert.Equal(0, one.Damage);
        Assert.Equal(0, two.Damage);
        EffectInterpreter.Execute(new BlankText(new TargetFilter(Self: true)), Context(state, queue, "p1", TriggerKind.Global, one));
        EffectInterpreter.MarkDamage(state, queue, DamageSource.Ability, two.Id, 1);
        Assert.Equal(1, two.Damage);
    }

    [Fact]
    public void Bison_Global_Grants_Overcrush_Through_Combat_And_Expires_At_Cleanup()
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var attacker = Add(state, DiceKingdomConfig.Hippopotamus.Id, "p1", Zone.FieldZone);
        var blocker = Add(state, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.FieldZone, level: 1);
        var index = state.CardCatalog["DPS148"].Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);
        TurnEngine.UseGlobal(state, queue, "DPS148", "p1", index, Energy(state, "Eye", 3));
        Drain(state, queue);
        EffectInterpreter.AnswerPendingChoice(state, [attacker.Id]);
        Drain(state, queue);
        Assert.Contains("Overcrush", QueryEngine.GetKeywords(state, attacker));
        var excess = QueryEngine.GetAttack(state, attacker) - QueryEngine.GetDefense(state, blocker);
        Assert.True(excess > 0);
        state.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        Assert.Equal(20 - excess, state.PlayerTwo.Life);
        Cleanup(state, queue);
        Assert.DoesNotContain("Overcrush", QueryEngine.GetKeywords(state, attacker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcludeSelf_Also_Rejects_The_Source_When_A_Filter_Uses_A_Fixed_Binding(bool bound)
    {
        var state = NewGame();
        var source = Add(state, "DPS148", "p1", Zone.FieldZone);
        var bindings = new Dictionary<string, string> { ["self"] = source.Id, ["picked"] = source.Id };
        var filter = new TargetFilter(Self: !bound, Bound: bound ? "picked" : null, ExcludeSelf: true);
        Assert.Empty(TargetResolver.Query(state, "p1", filter, bindings));
    }

    [Theory]
    [InlineData("p1")]
    [InlineData("p2")]
    public void A_Longer_Protection_Grant_Expires_At_Its_Controllers_Next_Turn(string controller)
    {
        var state = NewGame();
        var queue = new AbilityQueue();
        var target = Add(state, DiceKingdomConfig.Hippopotamus.Id, controller, Zone.FieldZone);
        var effect = new GrantTargetingProtection(new TargetingProtection(
            new TargetFilter(Ownership: TargetOwnership.Own), ProtectionFrom.Global), Duration.UntilYourNextTurn);
        EffectInterpreter.Execute(effect, Context(state, queue, controller, TriggerKind.Global));
        Assert.False(QueryEngine.CanBeTargeted(state, target, state.OpponentOf(controller), ProtectionFrom.Global));
        Assert.True(QueryEngine.CanBeTargeted(state, target, controller, ProtectionFrom.Global)); // Configurable opponent-only scope.
        Cleanup(state, queue);
        Assert.Equal(controller == "p1", state.GrantedTargetingProtections.Count == 1);
        if (controller == "p1") Cleanup(state, queue);
        Assert.Empty(state.GrantedTargetingProtections);
    }
}
