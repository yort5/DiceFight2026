using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

public class ElderBatchCardsTests
{
    private static readonly string[] Batch = ["DPS057", "DPS114", "DPS102", "DPS142", "DPS071", "DPS038", "GOTG105", "DPS031", "DPS059", "DPS099"];
    private sealed class FixedRoller : IDiceRoller { public int Roll(DieDefinition die) => 0; }
    private static GameState NewGame()
    {
        Player Player(string id) => new()
        {
            Id = id, Name = id,
            CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Wing", 4), DiceKingdomConfig.CustomTardigrade("Claw", 4)],
        };
        var one = Player("p1"); one.TeamCardIds.AddRange(Batch);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, one, Player("p2"));
        state.MoveToStep(StepIds.Main);
        return state;
    }
    private static DieInstance Add(GameState state, string cardId, Zone zone = Zone.FieldZone, int level = 1, string player = "p1")
    {
        var card = state.CardCatalog[cardId];
        var die = new DieInstance { Id = "test-" + state.Dice.Count, CardId = cardId, OwnerId = player, ControllerId = player, Zone = zone,
            CurrentFaceIndex = card.Die.Faces.Select((f, i) => (f, i)).First(x => x.f.Character?.Level == level).i };
        state.Dice.Add(die); return die;
    }
    private static DieInstance Energy(GameState state, string cardId, int amount = 2)
    {
        var die = Add(state, cardId, Zone.ReservePool);
        die.CurrentFaceIndex = state.CardCatalog[cardId].Die.Faces.Select((f, i) => (f, i))
            .First(x => x.f.Kind == FaceKind.EnergyFace && x.f.SymbolCount == amount).i;
        return die;
    }
    private static DieInstance Tardigrade(GameState state, Zone zone, int face = 3)
    {
        var die = state.DiceIn("p1", Zone.Bag).First(); die.Zone = zone; die.CurrentFaceIndex = face; return die;
    }
    private static void Drain(GameState state, AbilityQueue queue) => EffectInterpreter.DrainQueue(state, queue, new FixedRoller(), new Random(1));
    private static void Answer(GameState state, AbilityQueue queue, params string[] ids)
    { EffectInterpreter.AnswerPendingChoice(state, ids); Drain(state, queue); }
    private static void Buff(DieInstance die, int attack = 0, int defense = 0) =>
        die.AppliedModifiers.Add(new AppliedModifier(attack, defense, 0, "test bonus", Duration.EndOfTurn));
    private static void Cleanup(GameState state, AbilityQueue queue)
    { state.MoveToStep(StepIds.ReturnToField); TurnEngine.CleanUp(state, queue); Drain(state, queue); }

    [Fact]
    public void Elder_Is_The_Canonical_Keyword_And_All_Ten_Are_Playable_With_Their_Existing_Dice()
    {
        Assert.Contains(DiceKingdomConfig.Config.Keywords, k => k.Id == "Elder");
        Assert.Contains(DiceFightClassicConfig.Config.Keywords, k => k.Id == "Elder");
        foreach (var id in Batch) Assert.True(DiceKingdomConfig.Catalog[id].IsImplemented);
        foreach (var card in DiceKingdomConfig.TeamBuilderCatalog.Values)
        {
            Assert.DoesNotContain("Founder", card.Keywords);
            Assert.DoesNotContain("Founder", card.RawText);
        }
        Assert.Equal(123, DiceKingdomConfig.Catalog.Count);
        Assert.Equal(44, DiceKingdomConfig.TeamBuilderCatalog.Values.Count(c => !c.IsImplemented));
    }

    [Fact]
    public void Gibbon_Buffs_Other_Elders_Including_Copies_But_Not_Self_Opponents_Or_Blanked_Keywords()
    {
        var state = NewGame(); var gibbon = Add(state, "DPS057"); var mandrill = Add(state, "DPS114");
        var copy = Add(state, "DPS057"); var enemy = Add(state, "DPS114", player: "p2");
        Assert.Equal(3, QueryEngine.GetAttack(state, gibbon)); // other copy grants +1
        Assert.Equal(3, QueryEngine.GetAttack(state, copy));
        Assert.Equal(4, QueryEngine.GetAttack(state, mandrill)); // both Gibbons grant +1
        Assert.Equal(2, QueryEngine.GetAttack(state, enemy));
        var queue = new AbilityQueue();
        queue.Enqueue(mandrill.Id, "p1", TriggerKind.DieFielded, new BlankText(new TargetFilter(Self: true)));
        Drain(state, queue);
        Assert.Equal(2, QueryEngine.GetAttack(state, mandrill));
        copy.Zone = Zone.UsedPile; Assert.Equal(2, QueryEngine.GetAttack(state, gibbon));
    }

    [Fact]
    public void Mandrill_Energize_Doubles_Printed_Attack_Without_Removing_Bonuses_And_Its_Aura_Buffs_Tardigrades()
    {
        var state = NewGame(); var queue = new AbilityQueue(); var target = Add(state, DiceKingdomConfig.HoneyBadger.Id); Buff(target, attack: 3);
        var mandrill = Energy(state, "DPS114");
        state.MoveToStep(StepIds.RollAndReroll); TurnEngine.FinishRoll(state, queue); Drain(state, queue);
        var printed = state.GetCurrentFace(target)!.Character!.Attack;
        Assert.Equal(printed * 2 + 3, QueryEngine.GetAttack(state, target));
        mandrill.CurrentFaceIndex = 0; mandrill.Zone = Zone.FieldZone;
        var tard = Tardigrade(state, Zone.FieldZone);
        Assert.Equal(2, QueryEngine.GetAttack(state, tard));
        mandrill.Zone = Zone.UsedPile; Assert.Equal(1, QueryEngine.GetAttack(state, tard));
        Cleanup(state, queue); Assert.Equal(printed, QueryEngine.GetAttack(state, target));
    }

    [Fact]
    public void Lioness_Doubles_Each_Other_Own_Dies_Printed_Attack_In_Field_And_Attack_Zones()
    {
        var state = NewGame(); var queue = new AbilityQueue(); var lioness = Add(state, "DPS102");
        var ally = Add(state, DiceKingdomConfig.HoneyBadger.Id); Buff(ally, attack: 2);
        var other = Add(state, "DPS102", level: 3); var enemy = Add(state, "DPS102", player: "p2");
        TurnEngine.EnterAttackStep(state, queue); CombatEngine.DeclareAttackers(state, queue, [lioness.Id, ally.Id]); Drain(state, queue);
        Assert.Equal(3, QueryEngine.GetAttack(state, lioness));
        Assert.Equal(state.GetCurrentFace(ally)!.Character!.Attack * 2 + 2, QueryEngine.GetAttack(state, ally));
        Assert.Equal(10, QueryEngine.GetAttack(state, other));
        Assert.Equal(3, QueryEngine.GetAttack(state, enemy));
    }

    [Fact]
    public void Wombat_Tracks_Current_Defense_And_Stops_Setting_Attack_When_Tardigrade_Leaves()
    {
        var state = NewGame(); var wombat = Add(state, "DPS142"); Buff(wombat, attack: 5, defense: 2);
        Assert.Equal(7, QueryEngine.GetAttack(state, wombat));
        var tard = Tardigrade(state, Zone.FieldZone);
        Add(state, DiceKingdomConfig.MuskOx.Id); Add(state, "DPS057");
        Assert.Equal(QueryEngine.GetDefense(state, wombat), QueryEngine.GetAttack(state, wombat));
        Assert.Equal(7, QueryEngine.GetAttack(state, wombat)); // printed 4D + applied 2D + Musk Ox 1D
        wombat.CurrentFaceIndex = 2;
        Assert.Equal(QueryEngine.GetDefense(state, wombat), QueryEngine.GetAttack(state, wombat));
        tard.Zone = Zone.UsedPile;
        Assert.Equal(10, QueryEngine.GetAttack(state, wombat)); // 4A + applied 5A + Elder aura 1A
    }

    [Fact]
    public void Orb_Weaver_Only_Taxes_Opposing_Purchases_With_Printed_Cost_At_Most_Two()
    {
        var state = NewGame(); var source = Add(state, "DPS071");
        var cheap = state.CardCatalog[DiceKingdomConfig.HoneyBadger.Id];
        Assert.Equal(cheap.PurchaseCost + 1, QueryEngine.GetPurchaseCost(state, cheap, "p2"));
        Assert.Equal(cheap.PurchaseCost, QueryEngine.GetPurchaseCost(state, cheap, "p1"));
        var expensive = cheap with { PurchaseCost = 4 };
        Assert.Equal(4, QueryEngine.GetPurchaseCost(state, expensive, "p2"));
        source.Zone = Zone.UsedPile; Assert.Equal(cheap.PurchaseCost, QueryEngine.GetPurchaseCost(state, cheap, "p2"));
    }

    [Fact]
    public void Secretary_Bird_Requires_A_Creature_Purchase_And_Does_Not_Spend_For_An_Invalid_Global()
    {
        var state = NewGame(); var queue = new AbilityQueue(); var energy = Energy(state, "DPS038", 1);
        var card = state.CardCatalog["DPS038"]; var index = card.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);
        state.PurchasedThisTurn.Add("p1"); // an action purchase alone does not qualify
        Assert.Throws<InvalidOperationException>(() => TurnEngine.UseGlobal(state, queue, card.Id, "p1", index, [energy.Id]));
        Assert.Equal(Zone.ReservePool, energy.Zone); Assert.Empty(state.GlobalsUsedThisTurn);
        var purchased = state.Dice.First(d => d.CardId == "DPS099" && d.Zone == Zone.Unpurchased);
        TurnEngine.Purchase(state, queue, purchased.Id, [Energy(state, "DPS059").Id, Energy(state, "DPS059", 1).Id]); Drain(state, queue);
        Assert.Contains("p1", state.PurchasedCharacterThisTurn);
        TurnEngine.UseGlobal(state, queue, card.Id, "p1", index, [energy.Id]); Drain(state, queue);
        Assert.NotNull(state.PendingChoice); Answer(state, queue, state.PendingChoice!.CandidateIds[0]);
        Assert.Single(state.DiceIn("p1", Zone.PrepArea));
        Assert.Throws<InvalidOperationException>(() => TurnEngine.UseGlobal(state, queue, card.Id, "p1", index, [Energy(state, "DPS038", 1).Id]));
        Assert.Throws<InvalidOperationException>(() => TurnEngine.UseGlobal(state, queue, card.Id, "p2", index, []));
        Cleanup(state, queue); TurnEngine.ClearAndDraw(state, queue, new Random(1));
        Assert.Empty(state.PurchasedCharacterThisTurn);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Sugar_Glider_Preps_Only_When_Own_Infiltrate_Is_Actually_Used(bool accept, int prepped)
    {
        var state = NewGame(); var queue = new AbilityQueue(); Add(state, "GOTG105");
        var squirrel = Add(state, DiceKingdomConfig.FlyingSquirrel.Id);
        TurnEngine.EnterAttackStep(state, queue); CombatEngine.DeclareAttackers(state, queue, [squirrel.Id]); Drain(state, queue);
        CombatEngine.DeclareBlockers(state, queue, new CombatAssignment(), []); Drain(state, queue);
        Assert.NotNull(state.PendingChoice);
        if (accept) Answer(state, queue, state.PendingChoice!.CandidateIds[0]);
        else Answer(state, queue);
        Assert.Equal(prepped, state.DiceIn("p1", Zone.PrepArea).Count());
        Assert.Equal(accept ? Zone.FieldZone : Zone.AttackZone, squirrel.Zone);
    }

    [Fact]
    public void Sugar_Glider_Does_Not_React_To_Opponents_Infiltrate()
    {
        var state = NewGame(); var queue = new AbilityQueue(); Add(state, "GOTG105");
        var enemy = Add(state, DiceKingdomConfig.FlyingSquirrel.Id, player: "p2");
        queue.Enqueue(enemy.Id, "p2", TriggerKind.DieBlocks, new EmitEvent(TriggerKind.InfiltrateUsed)); Drain(state, queue);
        Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
    }

    [Theory]
    [InlineData("DPS031", true)]
    [InlineData("DPS114", false)]
    public void Woodpecker_Uses_The_Actual_Payment_Symbol_And_Does_Not_Treat_Offered_Extras_As_Spent(string payerCard, bool preps)
    {
        var state = NewGame(); var queue = new AbilityQueue(); var woodpecker = Add(state, "DPS031", Zone.ReservePool);
        var payment = Energy(state, payerCard); var extra = Energy(state, "DPS031");
        TurnEngine.Field(state, queue, woodpecker.Id, [payment.Id, extra.Id]); Drain(state, queue);
        Assert.Equal(preps ? 1 : 0, state.DiceIn("p1", Zone.PrepArea).Count());
        Assert.Equal(Zone.ReservePool, extra.Zone);
        Assert.Equal(1, state.GetCurrentFace(payment)!.SymbolCount); // partial 2 -> 1
    }

    [Fact]
    public void Free_Fielding_Does_Not_Trigger_Payment_Reactions()
    {
        var state = NewGame(); var queue = new AbilityQueue(); var woodpecker = Add(state, "DPS031", Zone.ReservePool);
        woodpecker.AppliedModifiers.Add(new AppliedModifier(0, 0, -1, "free", Duration.EndOfTurn));
        var offered = Energy(state, "DPS059"); TurnEngine.Field(state, queue, woodpecker.Id, [offered.Id]); Drain(state, queue);
        Assert.Equal(Zone.ReservePool, offered.Zone); Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Kangaroo_Rat_Preps_Itself_When_Used_For_Fielding_Even_If_Only_Partly_Spent(int energyAmount)
    {
        var state = NewGame(); var queue = new AbilityQueue(); var fielded = Add(state, "DPS031", Zone.ReservePool);
        var rat = Energy(state, "DPS059", energyAmount);
        TurnEngine.Field(state, queue, fielded.Id, [rat.Id]); Drain(state, queue);
        Assert.Equal(Zone.PrepArea, rat.Zone); Assert.Null(rat.CurrentFaceIndex);
        Assert.Equal(Zone.FieldZone, fielded.Zone);
    }

    [Fact]
    public void Kangaroo_Rat_Does_Not_Prep_From_Purchases_And_Failed_Fielding_Has_No_Reactions()
    {
        var state = NewGame(); var queue = new AbilityQueue(); var rat = Energy(state, "DPS059");
        var tooCostly = Add(state, "DPS102", Zone.ReservePool, level: 3);
        Assert.Throws<InvalidOperationException>(() => TurnEngine.Field(state, queue, tooCostly.Id, [Energy(state, "DPS059", 1).Id]));
        Assert.Equal(Zone.ReservePool, tooCostly.Zone); Assert.True(queue.IsEmpty);
        var purchase = state.Dice.First(d => d.CardId == "DPS099" && d.Zone == Zone.Unpurchased);
        TurnEngine.Purchase(state, queue, purchase.Id, [rat.Id, Energy(state, "DPS059", 1).Id]); Drain(state, queue);
        Assert.Equal(Zone.OutOfPlay, rat.Zone);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Dung_Beetle_Routes_Only_Exclusive_Actual_Payments_With_No_Virtual_Energy(bool mixed, bool virtualEnergy, bool routes)
    {
        var state = NewGame(); var queue = new AbilityQueue(); var purchase = state.Dice.First(d => d.CardId == "DPS099" && d.Zone == Zone.Unpurchased);
        var first = Energy(state, "DPS099"); var second = Energy(state, mixed ? "DPS059" : "DPS099", 1);
        if (virtualEnergy) state.VirtualEnergy["p1"] = 1;
        var extra = Energy(state, "DPS114"); // offered, but not actually spent
        TurnEngine.Purchase(state, queue, purchase.Id, [first.Id, second.Id, extra.Id]);
        Assert.Equal(routes ? 1 : 0, queue.Pending.Count(a => a.SourceDieId == first.Id));
        Drain(state, queue);
        Assert.Equal(routes ? Zone.PrepArea : Zone.UsedPile, purchase.Zone);
        Assert.Equal(Zone.ReservePool, extra.Zone);
        if (virtualEnergy) Assert.Equal(Zone.ReservePool, second.Zone);
    }

    [Fact]
    public void Dung_Beetle_Does_Not_Route_A_Virtual_Assisted_Purchase_Or_A_Basic_Action()
    {
        var state = NewGame(); var queue = new AbilityQueue();
        var purchase = state.Dice.First(d => d.CardId == "DPS099" && d.Zone == Zone.Unpurchased);
        state.VirtualEnergy["p1"] = 2;
        TurnEngine.Purchase(state, queue, purchase.Id, [Energy(state, "DPS099").Id]); Drain(state, queue);
        Assert.Equal(Zone.UsedPile, purchase.Zone);
        var actionCard = state.CardCatalog[DiceKingdomConfig.AngerIssues.Id];
        var action = new DieInstance { Id = "action", CardId = actionCard.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased };
        state.Dice.Add(action);
        TurnEngine.Purchase(state, queue, action.Id, [Energy(state, "DPS099").Id, Energy(state, "DPS099").Id]); Drain(state, queue);
        Assert.Equal(Zone.UsedPile, action.Zone);
    }
}
