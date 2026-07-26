using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

public class PurchaseTests
{
    private static CardDef MakeSingleTypeCharacter(string id, EnergyType type, int cost = 3) => new()
    {
        Id = id, Name = id, Type = CardType.Character, PurchaseCost = cost, EnergyTypes = [type], DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)]
    };

    private static CardDef MakeBasicAction(string id, int cost = 2, bool epic = false) => new()
    {
        Id = id, Name = id, Type = epic ? CardType.EpicBasicAction : CardType.BasicAction,
        PurchaseCost = cost, DieLimit = 3
    };

    private static GameState CreateState(params CardDef[] cards)
    {
        var catalog = cards.ToDictionary(c => c.Id);
        var p1 = new Player { Id = "p1", Name = "P1" };
        var p2 = new Player { Id = "p2", Name = "P2" };
        var state = GameState.NewGame(catalog, p1, p2);
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    private static List<DieInstance> GiveEnergy(GameState state, string playerId, int count, EnergyKind kind, EnergyType? type = null)
    {
        var dice = new List<DieInstance>();
        for (var i = 0; i < count; i++)
        {
            var die = new DieInstance
            {
                Id = $"{playerId}-energy-{i}", CardId = null, OwnerId = playerId, ControllerId = playerId,
                Zone = Zone.ReservePool, Status = DieStatus.Energy, EnergyKind = kind, ProvidedEnergyType = type
            };
            state.Dice.Add(die);
            dice.Add(die);
        }
        return dice;
    }

    [Fact]
    public void Purchase_WithMatchingSpecificEnergyType_Succeeds()
    {
        var maskCard = MakeSingleTypeCharacter("mask-guy", EnergyType.Mask);
        var state = CreateState(maskCard);
        var die = new DieInstance
        {
            Id = "p1-mask-guy-1", CardId = maskCard.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased
        };
        state.Dice.Add(die);
        var energy = GiveEnergy(state, "p1", 3, EnergyKind.Specific, EnergyType.Mask);

        TurnEngine.Purchase(state, die.Id, energy.Select(e => e.Id).ToList());

        Assert.Equal(Zone.UsedPile, die.Zone);
        Assert.Equal("p1", die.ControllerId);
        Assert.All(energy, e => Assert.Equal(Zone.OutOfPlay, e.Zone));
    }

    [Fact]
    public void Purchase_WithoutMatchingEnergyType_Throws()
    {
        var maskCard = MakeSingleTypeCharacter("mask-guy", EnergyType.Mask);
        var state = CreateState(maskCard);
        var die = new DieInstance
        {
            Id = "p1-mask-guy-1", CardId = maskCard.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased
        };
        state.Dice.Add(die);
        // 3 Bolt energy - enough total, but none is Mask or Wild.
        var energy = GiveEnergy(state, "p1", 3, EnergyKind.Specific, EnergyType.Bolt);

        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, die.Id, energy.Select(e => e.Id).ToList()));
    }

    [Fact]
    public void Purchase_WildEnergySatisfiesAnySpecificType()
    {
        var maskCard = MakeSingleTypeCharacter("mask-guy", EnergyType.Mask);
        var state = CreateState(maskCard);
        var die = new DieInstance
        {
            Id = "p1-mask-guy-1", CardId = maskCard.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased
        };
        state.Dice.Add(die);
        var energy = GiveEnergy(state, "p1", 3, EnergyKind.Wild); // Sidekick energy faces

        TurnEngine.Purchase(state, die.Id, energy.Select(e => e.Id).ToList());

        Assert.Equal(Zone.UsedPile, die.Zone);
    }

    [Fact]
    public void Purchase_GenericEnergyNeverSatisfiesASpecificTypeRequirement()
    {
        var maskCard = MakeSingleTypeCharacter("mask-guy", EnergyType.Mask);
        var state = CreateState(maskCard);
        var die = new DieInstance
        {
            Id = "p1-mask-guy-1", CardId = maskCard.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased
        };
        state.Dice.Add(die);
        var energy = GiveEnergy(state, "p1", 3, EnergyKind.Generic); // Basic Action dice

        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, die.Id, energy.Select(e => e.Id).ToList()));
    }

    [Fact]
    public void Purchase_FromOpponentsNonBasicCard_Throws()
    {
        var maskCard = MakeSingleTypeCharacter("mask-guy", EnergyType.Mask);
        var state = CreateState(maskCard);
        var opponentsDie = new DieInstance
        {
            Id = "p2-mask-guy-1", CardId = maskCard.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.Unpurchased
        };
        state.Dice.Add(opponentsDie);
        var energy = GiveEnergy(state, "p1", 3, EnergyKind.Wild);

        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, opponentsDie.Id, energy.Select(e => e.Id).ToList()));
    }

    [Fact]
    public void Purchase_BasicActionCard_IsCommunityAndPurchasableRegardlessOfWhoBroughtIt()
    {
        var basicAction = MakeBasicAction("shared-action");
        var state = CreateState(basicAction);
        // p2 "brought" the card (owner), but p1 is the Active player purchasing it.
        var die = new DieInstance
        {
            Id = "p2-shared-action-1", CardId = basicAction.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.Unpurchased
        };
        state.Dice.Add(die);
        var energy = GiveEnergy(state, "p1", 2, EnergyKind.Generic);

        TurnEngine.Purchase(state, die.Id, energy.Select(e => e.Id).ToList());

        Assert.Equal(Zone.UsedPile, die.Zone);
        Assert.Equal("p1", die.ControllerId); // purchaser becomes controller...
        Assert.Equal("p2", die.OwnerId);      // ...but ownership (rule 1.1.4) is untouched
    }

    [Fact]
    public void Purchase_EpicBasicAction_RequiresActiveCharacterDieWithCost4Plus()
    {
        var epic = MakeBasicAction("epic-thing", cost: 4, epic: true);
        var state = CreateState(epic);
        var die = new DieInstance
        {
            Id = "p1-epic-thing-1", CardId = epic.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased
        };
        state.Dice.Add(die);
        var energy = GiveEnergy(state, "p1", 4, EnergyKind.Generic);

        Assert.Throws<InvalidOperationException>(() =>
            TurnEngine.Purchase(state, die.Id, energy.Select(e => e.Id).ToList()));
    }

    [Fact]
    public void Purchase_EpicBasicAction_SucceedsWithQualifyingActiveCharacter()
    {
        var bigCharacter = MakeSingleTypeCharacter("big-guy", EnergyType.Mask, cost: 4);
        var epic = MakeBasicAction("epic-thing", cost: 4, epic: true);
        var state = CreateState(bigCharacter, epic);
        state.Dice.Add(new DieInstance
        {
            Id = "p1-big-guy-1", CardId = bigCharacter.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1
        });
        var die = new DieInstance
        {
            Id = "p1-epic-thing-1", CardId = epic.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased
        };
        state.Dice.Add(die);
        var energy = GiveEnergy(state, "p1", 4, EnergyKind.Generic);

        TurnEngine.Purchase(state, die.Id, energy.Select(e => e.Id).ToList());

        Assert.Equal(Zone.UsedPile, die.Zone);
    }
}
