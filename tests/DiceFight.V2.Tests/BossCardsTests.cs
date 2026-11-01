using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// BossCards.cs (2026-11-01) - the boss-only cards, each through the real
// firing path, as Dice Kingdom cards.
public class BossCardsTests
{
    private sealed class Roller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static GameState Game()
    {
        Player Player(string id) => new() { Id = id, Name = id,
            CustomBasicDicePool = [DiceKingdomConfig.CustomTardigrade("Wing", 8)] };
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog.ToDictionary(p => p.Key, p => p.Value),
            Player("p1"), Player("p2"));
        state.MoveToStep(StepIds.Main);
        return state;
    }

    private static DieInstance Add(GameState s, string id, string owner = "p1", Zone zone = Zone.FieldZone, int level = 1)
    {
        var card = s.CardCatalog[id];
        var die = new DieInstance { Id = "test-" + s.Dice.Count, CardId = id, OwnerId = owner, ControllerId = owner, Zone = zone,
            CurrentFaceIndex = card.Die.Faces.Select((f, i) => (f, i)).First(x => card.CardType.IsActionDie()
                ? x.f.Kind == FaceKind.ActionFace : x.f.Character?.Level == level).i };
        s.Dice.Add(die);
        return die;
    }

    // A single energy of `card`'s type in p1's Reserve Pool (face 5 of a standard die).
    private static DieInstance Energy(GameState s, string cardId, string owner = "p1")
    {
        var e = Add(s, cardId, owner, Zone.ReservePool);
        e.CurrentFaceIndex = 5;
        return e;
    }

    private static void Drain(GameState s, AbilityQueue q, Func<PendingChoice, string?>? pick = null, int roll = 5)
    {
        EffectInterpreter.DrainQueue(s, q, new Roller(roll), new Random(1));
        for (var i = 0; s.PendingChoice is { } choice; i++)
        {
            Assert.True(i < 30, "Choices did not finish.");
            var picked = pick?.Invoke(choice) ?? choice.CandidateIds[0];
            EffectInterpreter.AnswerPendingChoice(s, picked is null ? [] : [picked]);
            EffectInterpreter.DrainQueue(s, q, new Roller(roll), new Random(1));
        }
    }

    private static void Field(GameState s, AbilityQueue q, DieInstance die, Func<PendingChoice, string?>? pick = null, int roll = 5)
    {
        die.Zone = Zone.ReservePool;
        var energy = Enumerable.Range(0, s.GetCurrentFace(die)!.Character!.FieldingCost)
            .Select(_ => Energy(s, DiceKingdomConfig.Elephant.Id, die.ControllerId).Id).ToList();
        TurnEngine.Field(s, q, die.Id, energy);
        Drain(s, q, pick, roll);
    }

    private static void Use(GameState s, AbilityQueue q, DieInstance action, Func<PendingChoice, string?>? pick = null)
    {
        action.Zone = Zone.ReservePool;
        TurnEngine.UseAction(s, q, action.Id);
        Drain(s, q, pick);
    }

    private static DieInstance Tardigrade(GameState s, string owner, Zone zone = Zone.FieldZone)
    {
        var die = s.DiceIn(owner, Zone.Bag).First(d => d.CardId is null);
        die.Zone = zone;
        die.CurrentFaceIndex = s.GetDieDefinition(die).Faces.Select((f, i) => (f, i)).First(x => x.f.Character?.Level == 2).i;
        return die;
    }

    [Fact]
    public void Text_Reads_In_Dice_Kingdom_Terms()
    {
        var cards = DiceKingdomConfig.Catalog;
        Assert.Equal("Moose", cards["THOR036"].Name);
        Assert.Equal("When Moose is damaged, he gets +XA, where X is his printed A (until end of turn).", cards["THOR036"].RawText);
        Assert.Equal("Impulse: if you spent at least 3 Claw energy to purchase Tiger, you may Prep this die and deal 2 damage to each opposing character die.",
            cards["WF129"].RawText);
        Assert.Equal("On Field: spin target character die down 1 level.", cards["TIW017"].RawText);
        Assert.All(BossCards.Ids, id => Assert.True(cards.ContainsKey(id), id));
    }

    [Fact]
    public void Moose_Gets_Its_Printed_ATK_Again_When_Damaged()
    {
        var s = Game(); var q = new AbilityQueue();
        var moose = Add(s, "THOR036"); // L1 6/5
        q.Enqueue(null, "p2", TriggerKind.DieUsed, new DealDamage(new Fixed(1), new TargetFilter(Ownership: TargetOwnership.Opposing)));
        Drain(s, q, _ => moose.Id);
        Assert.Equal(12, QueryEngine.GetAttack(s, moose));
    }

    [Fact]
    public void Tiger_Bought_With_Claw_Preps_Itself_And_Hits_Every_Opposing_Creature()
    {
        var s = Game(); var q = new AbilityQueue();
        var tiger = Add(s, "WF129", zone: Zone.Unpurchased);
        var target = Add(s, DiceKingdomConfig.Elephant.Id, "p2"); // L1 DEF 4
        var energy = Enumerable.Range(0, 5).Select(_ => Energy(s, DiceKingdomConfig.HoneyBadger.Id).Id).ToList(); // Claw
        TurnEngine.Purchase(s, q, tiger.Id, energy);
        Drain(s, q);
        Assert.Equal(Zone.PrepArea, tiger.Zone);
        Assert.Equal(2, target.Damage);
    }

    [Fact]
    public void Tiger_Bought_With_Less_Than_3_Claw_Does_Nothing_Extra()
    {
        var s = Game(); var q = new AbilityQueue();
        var tiger = Add(s, "WF129", zone: Zone.Unpurchased);
        var target = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var energy = Enumerable.Range(0, 3).Select(_ => Energy(s, DiceKingdomConfig.Elephant.Id).Id) // Eye
            .Concat(Enumerable.Range(0, 2).Select(_ => Energy(s, DiceKingdomConfig.HoneyBadger.Id).Id)).ToList(); // 2 Claw
        TurnEngine.Purchase(s, q, tiger.Id, energy);
        Drain(s, q);
        Assert.NotEqual(Zone.PrepArea, tiger.Zone);
        Assert.Equal(0, target.Damage);
    }

    [Fact]
    public void Stinging_Nettle_Hits_The_Opponent_For_2()
    {
        var s = Game(); var q = new AbilityQueue();
        Use(s, q, Add(s, "TOA011", zone: Zone.ReservePool), c => c.CandidateIds.Contains("p2") ? "p2" : c.CandidateIds[0]);
        Assert.Equal(18, s.PlayerTwo.Life);
    }

    [Fact]
    public void Lyrebird_Uses_Another_Cards_On_Field_Ability()
    {
        var s = Game(); var q = new AbilityQueue();
        var badger = Add(s, DiceKingdomConfig.HoneyBadger.Id, zone: Zone.Bag); // On Field: 1 damage to a target creature
        var victim = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var lyrebird = Add(s, "XMF020", zone: Zone.ReservePool);
        Field(s, q, lyrebird, c => c.CandidateIds.Contains(badger.Id) ? badger.Id : c.CandidateIds.Contains(victim.Id) ? victim.Id : c.CandidateIds[0]);
        Assert.Equal(1, victim.Damage);
        Assert.Contains(s.Log, e => e.Text.Contains("uses Honey Badger's On Field ability"));
    }

    [Fact]
    public void Octopus_KOs_A_Target_That_Rolls_A_Creature()
    {
        var s = Game(); var q = new AbilityQueue();
        var prey = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var octopus = Add(s, "DXM009", zone: Zone.ReservePool);
        Field(s, q, octopus, c => c.CandidateIds.Contains(prey.Id) ? prey.Id : c.CandidateIds[0], roll: 0); // everything rolls level 1
        Assert.NotEqual(Zone.FieldZone, prey.Zone);
        Assert.Equal(Zone.FieldZone, octopus.Zone);
    }

    [Fact]
    public void Octopus_Is_Caught_When_The_Target_Rolls_Energy()
    {
        var s = Game(); var q = new AbilityQueue();
        var prey = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var octopus = Add(s, "DXM009", zone: Zone.ReservePool);
        Field(s, q, octopus, c => c.CandidateIds.Contains(prey.Id) ? prey.Id : c.CandidateIds[0], roll: 5); // energy faces
        Assert.Equal(Zone.ReservePool, prey.Zone); // rolled to energy: off the Field, not KO'd
        Assert.NotEqual(Zone.FieldZone, octopus.Zone);
    }

    [Fact]
    public void Orangutan_Rolls_Two_Tardigrades_From_The_Bag()
    {
        var s = Game(); var q = new AbilityQueue();
        Field(s, q, Add(s, "UXM049", zone: Zone.ReservePool));
        Assert.Equal(2, s.DiceIn("p1", Zone.ReservePool).Count(d => d.CardId is null));
    }

    [Fact]
    public void Echolocation_KOs_Once_For_The_Next_Creature_Fielded()
    {
        var s = Game(); var q = new AbilityQueue();
        var first = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        var second = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        Use(s, q, Add(s, "UXM062", zone: Zone.ReservePool));
        Field(s, q, Add(s, DiceKingdomConfig.Elephant.Id, zone: Zone.ReservePool), c => c.CandidateIds.Contains(first.Id) ? first.Id : c.CandidateIds[0]);
        Assert.NotEqual(Zone.FieldZone, first.Zone);
        Field(s, q, Add(s, DiceKingdomConfig.Elephant.Id, zone: Zone.ReservePool));
        Assert.Equal(Zone.FieldZone, second.Zone);
    }

    [Fact]
    public void The_Den_Spins_All_Your_Creatures_Up()
    {
        var s = Game(); var q = new AbilityQueue();
        var a = Add(s, DiceKingdomConfig.Elephant.Id);
        var b = Add(s, DiceKingdomConfig.HoneyBadger.Id);
        var theirs = Add(s, DiceKingdomConfig.Elephant.Id, "p2");
        Use(s, q, Add(s, "XFC040", zone: Zone.ReservePool));
        Assert.Equal(2, s.GetCurrentFace(a)!.Character!.Level);
        Assert.Equal(2, s.GetCurrentFace(b)!.Character!.Level);
        Assert.Equal(1, s.GetCurrentFace(theirs)!.Character!.Level);
    }

    [Fact]
    public void Arctic_Fox_Can_Pay_1_To_Double_Its_ATK_When_It_Attacks()
    {
        var s = Game(); var q = new AbilityQueue();
        var fox = Add(s, "UXM009"); // L1 2/4
        Energy(s, DiceKingdomConfig.Elephant.Id);
        s.MoveToStep(StepIds.SelectAttackers);
        CombatEngine.DeclareAttackers(s, q, new Dictionary<string, int> { [fox.Id] = 0 });
        Drain(s, q);
        Assert.Equal(4, QueryEngine.GetAttack(s, fox));
    }

    [Fact]
    public void Slug_Spins_A_Target_Down()
    {
        var s = Game(); var q = new AbilityQueue();
        var target = Add(s, DiceKingdomConfig.Elephant.Id, "p2", level: 2);
        Field(s, q, Add(s, "TIW017", zone: Zone.ReservePool), c => c.CandidateIds.Contains(target.Id) ? target.Id : c.CandidateIds[0]);
        Assert.Equal(1, s.GetCurrentFace(target)!.Character!.Level);
    }

    [Fact]
    public void Tsetse_Fly_Silences_A_Card_Until_Its_Owners_Next_Turn()
    {
        var s = Game(); var q = new AbilityQueue();
        var badger = Add(s, DiceKingdomConfig.HoneyBadger.Id, "p2", Zone.UsedPile);
        Field(s, q, Add(s, "IG135", zone: Zone.ReservePool), c => c.CandidateIds.Contains(badger.Id) ? badger.Id : c.CandidateIds[0]);
        Assert.False(QueryEngine.CardTextActive(s, "p2", DiceKingdomConfig.HoneyBadger.Id));
        Assert.True(QueryEngine.CardTextActive(s, "p2", DiceKingdomConfig.Elephant.Id));
    }

    [Fact]
    public void Watering_Hole_Fields_Everything_In_Your_Reserve_For_Free()
    {
        var s = Game(); var q = new AbilityQueue();
        var a = Add(s, DiceKingdomConfig.Elephant.Id, zone: Zone.ReservePool);
        var b = Add(s, DiceKingdomConfig.HoneyBadger.Id, zone: Zone.ReservePool);
        var energy = Energy(s, DiceKingdomConfig.Elephant.Id);
        Use(s, q, Add(s, "AIW024", zone: Zone.ReservePool));
        Assert.Equal(Zone.FieldZone, a.Zone);
        Assert.Equal(Zone.FieldZone, b.Zone);
        Assert.Equal(Zone.ReservePool, energy.Zone); // nothing spent
    }

    [Fact]
    public void Venus_Flytrap_Scatters_Their_Prep_And_Preps_One_Of_Yours()
    {
        var s = Game(); var q = new AbilityQueue();
        var theirs = Tardigrade(s, "p2", Zone.PrepArea);
        var before = s.DiceIn("p1", Zone.PrepArea).Count();
        Use(s, q, Add(s, "HQ023", zone: Zone.ReservePool), c => c.CandidateIds.Contains(theirs.Id) ? theirs.Id : c.CandidateIds[0]);
        Assert.Equal(Zone.UsedPile, theirs.Zone);
        Assert.Equal(before + 1, s.DiceIn("p1", Zone.PrepArea).Count());
    }
}
