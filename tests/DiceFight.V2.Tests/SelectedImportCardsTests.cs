using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

public class SelectedImportCardsTests
{
    private sealed class Roller(int index) : IDiceRoller
    {
        public int Calls { get; private set; }
        public int Roll(DieDefinition die) { Calls++; return index; }
    }

    private static GameState Game(CardDef? replacement = null)
    {
        Player Player(string id) => new() { Id = id, Name = id,
            CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Wing", 8)] };
        var catalog = DiceKingdomConfig.Catalog.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (replacement is not null) catalog[replacement.Id] = replacement;
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, catalog, Player("p1"), Player("p2"));
        state.MoveToStep(StepIds.Main);
        return state;
    }

    private static DieInstance Add(GameState s, string id, string owner = "p1", Zone zone = Zone.FieldZone, int level = 1)
    {
        var c = s.CardCatalog[id];
        var die = new DieInstance { Id = "test-" + s.Dice.Count, CardId = id, OwnerId = owner, ControllerId = owner, Zone = zone,
            CurrentFaceIndex = zone == Zone.Unpurchased ? null : c.Die.Faces.Select((f, i) => (f, i))
                .First(x => c.CardType.IsActionDie() ? x.f.Kind == FaceKind.ActionFace : x.f.Character?.Level == level).i };
        s.Dice.Add(die); return die;
    }

    private static string[] Energy(GameState s, int count, string owner = "p1") => Enumerable.Range(0, count).Select(_ =>
    {
        var d = Add(s, "DRS022", owner, Zone.ReservePool); d.CurrentFaceIndex = 5; return d.Id;
    }).ToArray();

    private static void Drain(GameState s, AbilityQueue q, Roller? roller = null, bool accept = true)
    {
        roller ??= new Roller(5);
        EffectInterpreter.DrainQueue(s, q, roller, new Random(1));
        for (var i = 0; s.PendingChoice is { } choice; i++)
        {
            Assert.True(i < 30, "Choices did not finish.");
            EffectInterpreter.AnswerPendingChoice(s, !accept && choice.MinCount == 0 ? [] : [choice.CandidateIds[0]]);
            EffectInterpreter.DrainQueue(s, q, roller, new Random(1));
        }
    }

    private static void Cleanup(GameState s, AbilityQueue q)
    { s.MoveToStep(StepIds.ReturnToField); TurnEngine.CleanUp(s, q); Drain(s, q); }

    [Theory]
    [InlineData(Zone.FieldZone, true)]
    [InlineData(Zone.AttackZone, true)]
    [InlineData(Zone.ReservePool, false)]
    [InlineData(Zone.Bag, false)]
    [InlineData(Zone.UsedPile, false)]
    [InlineData(Zone.PrepArea, false)]
    public void Kinfolk_Counts_As_Tardigrade_Only_While_Active(Zone zone, bool counts)
    {
        var s = Game(); var d = Add(s, "DRS022", zone: zone);
        Assert.Equal(counts, QueryEngine.GetTags(s, d).Contains("sidekick"));
        Assert.False(d.IsSidekick); // Physical pool identity is unchanged.
    }

    [Fact]
    public void Kinfolk_Can_Be_Granted_And_Blanked_Through_The_Shared_Keyword_Flow()
    {
        var s = Game(); var q = new AbilityQueue(); var d = Add(s, DiceKingdomConfig.Greyhound.Id);
        q.Enqueue(d.Id, "p1", TriggerKind.Global, new GrantTag(new TargetFilter(Self: true), ["Kinfolk"])); Drain(s, q);
        Assert.Contains("sidekick", QueryEngine.GetTags(s, d));
        q.Enqueue(d.Id, "p1", TriggerKind.Global, new BlankText(new TargetFilter(Self: true))); Drain(s, q);
        Assert.DoesNotContain("sidekick", QueryEngine.GetTags(s, d));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, false)]
    public void Planarian_KO_Test_Roll_Always_Returns_The_Tardigrade_To_Used(int roll, bool recovered)
    {
        var s = Game(); var q = new AbilityQueue(); var source = Add(s, "WF075", level: 3);
        var tard = s.DiceIn("p1", Zone.Bag).First(); tard.Zone = Zone.UsedPile;
        var roller = new Roller(roll);
        EffectInterpreter.KoDie(s, q, source, true, "p2"); Drain(s, q, roller);
        Assert.Equal(recovered ? Zone.FieldZone : Zone.PrepArea, source.Zone);
        if (recovered) Assert.Equal(1, s.GetCurrentFace(source)?.Character?.Level);
        Assert.Equal(Zone.UsedPile, tard.Zone); Assert.Null(tard.CurrentFaceIndex); Assert.Equal(1, roller.Calls);
    }

    [Fact]
    public void Planarian_Recovery_Is_Optional_And_Does_Not_Roll_When_Declined()
    {
        var s = Game(); var q = new AbilityQueue(); var d = Add(s, "WF075");
        s.DiceIn("p1", Zone.Bag).First().Zone = Zone.UsedPile;
        var roller = new Roller(0); EffectInterpreter.KoDie(s, q, d, true); Drain(s, q, roller, accept: false);
        Assert.Equal(Zone.PrepArea, d.Zone); Assert.Equal(0, roller.Calls);
    }

    [Fact]
    public void Abundant_Waters_Reacts_To_Later_Tardigrades_And_Kinfolk_And_Expires()
    {
        var s = Game(); var q = new AbilityQueue(); var action = Add(s, "MYST002", zone: Zone.ReservePool);
        TurnEngine.UseAction(s, q, action.Id); Drain(s, q);
        action.Zone = Zone.UsedPile; // The resolved grant does not need its source active.
        var roller = new Roller(0);
        var tard = s.DiceIn("p1", Zone.Bag).First(); tard.Zone = Zone.ReservePool; tard.CurrentFaceIndex = 3;
        TurnEngine.Field(s, q, tard.Id, []); Drain(s, q, roller); Assert.Equal(2, roller.Calls);
        var kinfolk = Add(s, "DRS022", zone: Zone.ReservePool);
        TurnEngine.Field(s, q, kinfolk.Id, []); Drain(s, q, roller); Assert.Equal(4, roller.Calls);
        var ordinary = Add(s, "AVX019", zone: Zone.ReservePool);
        TurnEngine.Field(s, q, ordinary.Id, []); Drain(s, q, roller); Assert.Equal(4, roller.Calls);
        Cleanup(s, q); Assert.Empty(s.GrantedTriggeredAbilities);
    }

    [Fact]
    public void Personal_Action_Is_Not_A_Shared_Purchase()
    {
        var s = Game(); var d = Add(s, "MYST002", "p2", Zone.Unpurchased);
        Assert.Equal(CardType.Action, s.CardCatalog[d.CardId!].CardType);
        Assert.Throws<InvalidOperationException>(() => TurnEngine.Purchase(s, new AbilityQueue(), d.Id, Energy(s, 2)));
        Assert.Equal(Zone.Unpurchased, d.Zone);
        Assert.Contains("MYST002", DiceKingdomPacks.Rarities.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hercules_Beetle_Reacts_Once_Per_Card_To_Player_Or_Copy_Damage(bool hitCopy)
    {
        var s = Game(); var q = new AbilityQueue(); var source = Add(s, "AVX077"); Add(s, "AVX077");
        var enemy = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        EffectInterpreter.ApplyDamage(s, q, DamageSource.Ability, hitCopy ? source.Id : "p1", 1); Drain(s, q);
        Assert.Equal(2, enemy.Damage); Assert.Equal(20, s.PlayerTwo.Life);
    }

    [Fact]
    public void Hercules_Beetle_Ignores_Life_Loss_And_Damage_To_Other_Creatures()
    {
        var s = Game(); var q = new AbilityQueue(); var source = Add(s, "AVX077");
        var own = Add(s, DiceKingdomConfig.Hippopotamus.Id); var enemy = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        q.Enqueue(source.Id, "p1", TriggerKind.Global, new LifeChange(new Fixed(-1))); Drain(s, q);
        EffectInterpreter.ApplyDamage(s, q, DamageSource.Ability, own.Id, 1); Drain(s, q);
        Assert.Equal(0, enemy.Damage);
    }

    [Fact]
    public void A_Simultaneous_Board_Hit_Respects_Protection_Even_When_It_KOs_The_Aura_Source()
    {
        var s = Game(); var q = new AbilityQueue(); Add(s, "AVX077");
        var bison = Add(s, "DPS148", "p2"); bison.Damage = 3;
        var other = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        EffectInterpreter.ApplyDamage(s, q, DamageSource.Ability, "p1", 1); Drain(s, q);
        Assert.Equal(Zone.PrepArea, bison.Zone);
        Assert.Equal(0, other.Damage); Assert.Equal(Zone.FieldZone, other.Zone);
    }

    [Fact]
    public void Combat_Damage_To_A_Player_Uses_The_Same_Damage_Event()
    {
        var s = Game(); var q = new AbilityQueue(); Add(s, "AVX077", "p2");
        var survivor = Add(s, DiceKingdomConfig.Hippopotamus.Id); var attacker = Add(s, "DRS022");
        s.MoveToStep(StepIds.SelectAttackers); CombatEngine.DeclareAttackers(s, q, [attacker.Id]);
        var blocks = new CombatAssignment(); CombatEngine.DeclareBlockers(s, q, blocks, []);
        CombatEngine.AssignCombatDamage(s, q, blocks, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        Drain(s, q); Assert.Equal(2, survivor.Damage);
    }

    [Fact]
    public void Fire_Ant_Uses_Adapted_Claw_Energy_And_One_While_Active_Trigger()
    {
        var s = Game(); var q = new AbilityQueue(); Add(s, "XMF028"); Add(s, "XMF028");
        var claw = Add(s, "XMF028", zone: Zone.ReservePool); TurnEngine.Field(s, q, claw.Id, []); Drain(s, q);
        Assert.Equal(19, s.PlayerTwo.Life);
        var wing = Add(s, "DRS022", zone: Zone.ReservePool); TurnEngine.Field(s, q, wing.Id, []); Drain(s, q);
        Assert.Equal(19, s.PlayerTwo.Life);
    }

    [Fact]
    public void Electric_Ray_Suppresses_On_Field_Only_And_Stops_When_It_Leaves()
    {
        var s = Game(); var q = new AbilityQueue(); var ray = Add(s, "IG058");
        s.ActivePlayerId = "p2";
        var lizard = Add(s, DiceKingdomConfig.FrilledLizard.Id, "p2", Zone.ReservePool);
        TurnEngine.Field(s, q, lizard.Id, Energy(s, 1, "p2")); Drain(s, q);
        Assert.Equal(Zone.FieldZone, ray.Zone); Assert.Null(s.PendingChoice);
        var wolverine = Add(s, DiceKingdomConfig.Wolverine.Id, "p2");
        Assert.Contains(QueryEngine.AbilitiesOf(s, wolverine), a => a.Trigger == TriggerKind.DieAttacks);
        ray.Zone = Zone.PrepArea;
        Assert.Contains(QueryEngine.AbilitiesOf(s, lizard), a => a.Trigger == TriggerKind.DieFielded);
    }

    [Fact]
    public void Trigger_Suppression_Respects_Clauses_That_Cannot_Be_Ignored()
    {
        var immune = DiceKingdomConfig.Catalog["AVX019"] with
        {
            PermanentAbilities = [new TriggeredAbility(TriggerKind.DieFielded,
                new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        };
        var s = Game(immune); var q = new AbilityQueue(); Add(s, "IG058"); s.ActivePlayerId = "p2";
        var leaf = Add(s, "AVX019", "p2", Zone.ReservePool); TurnEngine.Field(s, q, leaf.Id, []); Drain(s, q);
        Assert.Equal(19, s.PlayerOne.Life);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void Velvet_Worm_Blocks_Through_Ordinary_Combat_And_Has_No_Burst_Healing(int level, int after)
    {
        var s = Game(); var q = new AbilityQueue(); var attacker = Add(s, DiceKingdomConfig.Hippopotamus.Id, level: level);
        var worm = Add(s, "AVX066", "p2"); s.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(s, q, [attacker.Id]); var b = new CombatAssignment(); b.AssignBlocker(attacker.Id, worm.Id);
        CombatEngine.DeclareBlockers(s, q, b, [worm.Id]); Drain(s, q);
        Assert.Equal(after == 0 ? Zone.PrepArea : Zone.AttackZone, attacker.Zone);
        if (after > 0) Assert.Equal(after, s.GetCurrentFace(attacker)?.Character?.Level);
        Assert.Equal(20, s.PlayerTwo.Life);
    }

    [Fact]
    public void Combat_Relation_Also_Reaches_Other_Attackers_In_The_Same_Lane()
    {
        var s = Game(); var q = new AbilityQueue(); var a = Add(s, DiceKingdomConfig.Hippopotamus.Id);
        var other = Add(s, DiceKingdomConfig.Hippopotamus.Id, level: 3); var worm = Add(s, "AVX066", "p2");
        var escort = Add(s, DiceKingdomConfig.HoneyBadger.Id); // the stacking rule: another lane as full
        s.MoveToStep(StepIds.SelectAttackers); CombatEngine.DeclareAttackers(s, q, new Dictionary<string, int> { [a.Id] = 0, [other.Id] = 0, [escort.Id] = 1 });
        var b = new CombatAssignment(); b.AssignBlocker(a.Id, worm.Id); CombatEngine.DeclareBlockers(s, q, b, [worm.Id]); Drain(s, q);
        Assert.Equal(Zone.PrepArea, a.Zone); Assert.Equal(2, s.GetCurrentFace(other)?.Character?.Level);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public void Orchid_Mantis_Rerolls_Its_Actual_Blocker_And_Damages_That_Controller(int roll, bool energy)
    {
        var s = Game(); var q = new AbilityQueue(); var attacker = Add(s, "XFO004");
        var blocker = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2"); s.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(s, q, [attacker.Id]); var b = new CombatAssignment(); b.AssignBlocker(attacker.Id, blocker.Id);
        CombatEngine.DeclareBlockers(s, q, b, [blocker.Id]); Drain(s, q, new Roller(roll));
        Assert.Equal(energy ? Zone.UsedPile : Zone.AttackZone, blocker.Zone);
        Assert.Equal(energy ? 18 : 20, s.PlayerTwo.Life); Assert.Equal(20, s.PlayerOne.Life);
        if (!energy) Assert.Equal(3, s.GetCurrentFace(blocker)?.Character?.Level);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public void Leaf_Insect_Uses_On_Field_Reroll_Removal(int roll, bool energy)
    {
        var s = Game(); var q = new AbilityQueue(); var enemy = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        var leaf = Add(s, "AVX019", zone: Zone.ReservePool); TurnEngine.Field(s, q, leaf.Id, []); Drain(s, q, new Roller(roll));
        Assert.Equal(energy ? Zone.UsedPile : Zone.FieldZone, enemy.Zone);
        if (!energy) Assert.Equal(3, s.GetCurrentFace(enemy)?.Character?.Level);
    }

    [Fact]
    public void Reroll_Energize_Qualifies_Before_Its_Die_Moves_To_Used()
    {
        var s = Game(); var q = new AbilityQueue(); Add(s, "XFO016", "p2");
        var leaf = Add(s, "AVX019", zone: Zone.ReservePool); TurnEngine.Field(s, q, leaf.Id, []); Drain(s, q, new Roller(3));
        Assert.Equal(2, QueryEngine.GetPurchaseCostNow(s, DiceKingdomConfig.AngerIssues, "p2"));
        Cleanup(s, q); Assert.Equal(4, QueryEngine.GetPurchaseCostNow(s, DiceKingdomConfig.AngerIssues, "p2"));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public void Mimic_Filefish_Awaken_Removes_Energy_But_Restores_Other_Results(int roll, bool energy)
    {
        var s = Game(); var q = new AbilityQueue(); var fish = Add(s, "XFC110"); var enemy = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2");
        EffectInterpreter.SpinLevel(s, q, fish, null, 1); Drain(s, q, new Roller(roll));
        Assert.Equal(energy ? Zone.UsedPile : Zone.FieldZone, enemy.Zone);
        if (!energy) Assert.Equal(1, s.GetCurrentFace(enemy)?.Character?.Level);
    }

    [Fact]
    public void Fire_Salamander_Awaken_Hits_The_Opposing_Board_And_Not_Reserve()
    {
        var s = Game(); var q = new AbilityQueue(); var source = Add(s, "XFC017");
        var enemy = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2"); var reserve = Add(s, DiceKingdomConfig.Hippopotamus.Id, "p2", Zone.ReservePool);
        EffectInterpreter.SpinLevel(s, q, source, null, 1); Drain(s, q);
        Assert.Equal(1, enemy.Damage); Assert.Equal(0, reserve.Damage);
        q.Enqueue(source.Id, "p1", TriggerKind.Global, new Reroll(new TargetFilter(Self: true))); Drain(s, q, new Roller(2));
        Assert.Equal(1, enemy.Damage); // A higher rolled level is not Awaken.
    }

    [Fact]
    public void Bowerbird_Discounts_Every_Personal_And_Basic_Action_But_Not_Creatures()
    {
        var s = Game(); var q = new AbilityQueue(); var source = Add(s, "XFO016", zone: Zone.ReservePool); source.CurrentFaceIndex = 3;
        EventBus.Fire(s, q, new GameEvent(TriggerKind.TurnStepEntered, null, "p1", StepIds.Main)); Drain(s, q);
        Assert.Equal(2, QueryEngine.GetPurchaseCostNow(s, DiceKingdomConfig.AngerIssues, "p1"));
        Assert.Equal(1, QueryEngine.GetPurchaseCostNow(s, s.CardCatalog["MYST002"], "p1"));
        Assert.Equal(4, QueryEngine.GetPurchaseCostNow(s, DiceKingdomConfig.Hippopotamus, "p1"));
        for (var i = 0; i < 2; i++)
        {
            var d = Add(s, DiceKingdomConfig.AngerIssues.Id, "p2", Zone.Unpurchased);
            TurnEngine.Purchase(s, q, d.Id, Energy(s, 2)); Drain(s, q); Assert.Equal(Zone.UsedPile, d.Zone);
        }
        Cleanup(s, q); Assert.Equal(4, QueryEngine.GetPurchaseCostNow(s, DiceKingdomConfig.AngerIssues, "p1"));
    }

    [Fact]
    public void Harris_Hawk_Grants_Range_To_Later_Allies_Without_Lowering_Existing_Range()
    {
        var s = Game(); var q = new AbilityQueue(); var hawk = Add(s, "SW018"); var ally = Add(s, "DRS022");
        var cobra = Add(s, "SKC090"); var enemy = Add(s, "DRS022", "p2");
        Assert.Equal(1, KeywordAbilities.MaxParam(QueryEngine.GetKeywords(s, ally), "Range"));
        Assert.Equal(2, KeywordAbilities.MaxParam(QueryEngine.GetKeywords(s, cobra), "Range"));
        Assert.Null(KeywordAbilities.MaxParam(QueryEngine.GetKeywords(s, enemy), "Range"));
        EffectInterpreter.KoDie(s, q, hawk, true); Drain(s, q);
        Assert.Null(KeywordAbilities.MaxParam(QueryEngine.GetKeywords(s, ally), "Range"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spring_Emergence_Has_An_Optional_Purchase_Impulse(bool accept)
    {
        var s = Game(); var q = new AbilityQueue(); var d = Add(s, "IG003", "p2", Zone.Unpurchased);
        TurnEngine.Purchase(s, q, d.Id, Energy(s, 3)); Drain(s, q, accept: accept);
        Assert.Equal(accept ? Zone.Bag : Zone.UsedPile, d.Zone); Assert.Null(d.CurrentFaceIndex);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Spring_Emergence_Purchase_Routing_Is_A_Confirmation_Not_A_Target_Pick(bool accept)
    {
        var s = Game(); var q = new AbilityQueue();
        var action = Add(s, "IG003", zone: Zone.ReservePool);
        TurnEngine.UseAction(s, q, action.Id); Drain(s, q);
        var purchase = Add(s, DiceKingdomConfig.Archnemesis.Id, "p2", Zone.Unpurchased);
        var cost = QueryEngine.GetPurchaseCostNow(s, s.CardCatalog[purchase.CardId!], "p1");
        TurnEngine.Purchase(s, q, purchase.Id, Energy(s, cost));
        EffectInterpreter.DrainQueue(s, q, new Roller(5), new Random(1));
        var choice = Assert.IsType<PendingChoice>(s.PendingChoice);
        Assert.True(choice.IsConfirmation);
        Assert.Equal("Spring Emergence: put Archnemesis directly into your Bag?", choice.Description);
        Assert.Equal(Zone.UsedPile, purchase.Zone);
        EffectInterpreter.AnswerPendingChoice(s, accept ? choice.CandidateIds : []);
        Drain(s, q);
        Assert.Equal(accept ? Zone.Bag : Zone.UsedPile, purchase.Zone);
    }

    [Fact]
    public void Spring_Emergence_Does_Not_Stack_And_Its_Routing_Combines_With_Other_Discounts()
    {
        var s = Game(); var q = new AbilityQueue(); var bower = Add(s, "XFO016", zone: Zone.ReservePool); bower.CurrentFaceIndex = 3;
        EventBus.Fire(s, q, new GameEvent(TriggerKind.TurnStepEntered, null, "p1", StepIds.Main)); Drain(s, q);
        for (var i = 0; i < 2; i++) { var action = Add(s, "IG003", zone: Zone.ReservePool); TurnEngine.UseAction(s, q, action.Id); Drain(s, q); }
        Assert.Equal(1, QueryEngine.GetPurchaseCostNow(s, DiceKingdomConfig.AngerIssues, "p1"));
        Assert.Equal(3, QueryEngine.GetPurchaseCostNow(s, DiceKingdomConfig.Hippopotamus, "p1"));
        for (var i = 0; i < 2; i++)
        {
            var d = Add(s, DiceKingdomConfig.AngerIssues.Id, "p2", Zone.Unpurchased);
            TurnEngine.Purchase(s, q, d.Id, Energy(s, 1)); Drain(s, q, accept: i == 0);
            Assert.Equal(i == 0 ? Zone.Bag : Zone.UsedPile, d.Zone);
        }
        Cleanup(s, q); Assert.Empty(s.PendingPurchaseModifiers);
    }

    [Theory]
    [InlineData(0, 0, Zone.OutOfPlay, 0)]
    [InlineData(1, 0, Zone.PrepArea, 1)]
    [InlineData(2, 3, Zone.UsedPile, 1)]
    public void Thorn_Shot_Rebounds_Only_On_Burst_And_Routes_The_Rolled_Result(int burst, int roll, Zone destination, int calls)
    {
        var s = Game(); var q = new AbilityQueue(); var d = Add(s, "JUS069", zone: Zone.ReservePool); d.CurrentFaceIndex = burst;
        var roller = new Roller(roll); TurnEngine.UseAction(s, q, d.Id); Drain(s, q, roller);
        Assert.Equal(19, s.PlayerTwo.Life); Assert.Equal(destination, d.Zone); Assert.Equal(calls, roller.Calls);
        if (destination != Zone.OutOfPlay) Assert.Null(d.CurrentFaceIndex);
    }

    [Fact]
    public void Foraging_Draws_And_Rolls_Two_Through_The_Ordinary_Action_Path()
    {
        var s = Game(); var q = new AbilityQueue(); var d = Add(s, "JUS068", zone: Zone.ReservePool); var roller = new Roller(0);
        TurnEngine.UseAction(s, q, d.Id); Drain(s, q, roller);
        Assert.Equal(2, roller.Calls); Assert.Equal(2, s.DiceIn("p1", Zone.ReservePool).Count());
    }
}
