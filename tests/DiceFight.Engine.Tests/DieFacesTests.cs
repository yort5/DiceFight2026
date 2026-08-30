using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// DieFaces - what is printed on each side of a die. The roller only picks
// an index now, so every face a die can land on is here.
public class DieFacesTests
{
    private static GameState Game(params CardDef[] cards) =>
        GameState.NewGame(
            cards.ToDictionary(c => c.Id),
            new Player { Id = "p1", Name = "P1" },
            new Player { Id = "p2", Name = "P2" });

    private static CardDef Character(string id, string name, params EnergyType[] energy) => new()
    {
        Id = id, Name = name, Type = CardType.Character, PurchaseCost = 4, DieLimit = 4,
        EnergyTypes = energy,
        Levels =
        [
            new CharacterFace(1, 1, 1),
            new CharacterFace(1, 2, 2),
            new CharacterFace(2, 3, 3),
        ],
    };

    private static DieInstance DieOf(GameState state, CardDef card)
    {
        var die = new DieInstance { Id = $"d-{card.Id}", CardId = card.Id, OwnerId = "p1", ControllerId = "p1" };
        state.Dice.Add(die);
        return die;
    }

    private static IReadOnlyList<RolledFace> FacesOf(CardDef card)
    {
        var state = Game(card);
        return DieFaces.Of(DieOf(state, card), card);
    }

    [Fact]
    public void EveryDie_HasSixFaces()
    {
        foreach (var card in new[]
        {
            Character("mono", "Mono", EnergyType.Bolt),
            Character("cross", "Cross", EnergyType.Bolt, EnergyType.Mask),
            new CardDef { Id = "ba", Name = "BA", Type = CardType.BasicAction, PurchaseCost = 2, DieLimit = 3 },
            new CardDef { Id = "act", Name = "Act", Type = CardType.Action, PurchaseCost = 2, DieLimit = 4, EnergyTypes = [EnergyType.Fist] },
        })
        {
            Assert.Equal(6, FacesOf(card).Count);
        }
    }

    [Fact]
    public void Sidekick_IsOneCharacterFaceAndFiveSingleEnergyFaces()
    {
        var state = Game();
        var sidekick = state.DiceIn("p1", Zone.Bag).First();

        var faces = DieFaces.Of(sidekick, null);

        Assert.Equal(6, faces.Count);
        Assert.Single(faces, f => f.Status == DieStatus.SidekickCharacter);
        Assert.Equal(5, faces.Count(f => f.Status == DieStatus.Energy && f.EnergyAmount == 1));
        Assert.Single(faces, f => f.EnergyKind == EnergyKind.Wild);
    }

    // A character face's index IS its level, which is what lets a spin up
    // or down be a quarter turn of the die.
    [Fact]
    public void CharacterFaces_ComeFirstAndInLevelOrder()
    {
        var faces = FacesOf(Character("c", "C", EnergyType.Bolt));

        Assert.Equal([1, 2, 3], faces.Take(3).Select(f => f.Level));
        Assert.All(faces.Take(3), f => Assert.Equal(DieStatus.Character, f.Status));
    }

    [Fact]
    public void SingleEnergyCharacter_HasTwoDoublesAndOneSingleOfItsOwnType()
    {
        var energy = FacesOf(Character("c", "C", EnergyType.Bolt)).Skip(3).ToList();

        Assert.All(energy, f => Assert.Equal(EnergyType.Bolt, f.ProvidedEnergyType));
        Assert.Equal(2, energy.Count(f => f.EnergyAmount == 2));
        Assert.Single(energy, f => f.EnergyAmount == 1);
    }

    // The Crossover glossary entry: the double covers both types, and the
    // single it spins down to is generic.
    [Fact]
    public void Crossover_HasASplitDoubleAndAGenericSingle()
    {
        var energy = FacesOf(Character("c", "C", EnergyType.Fist, EnergyType.Mask)).Skip(3).ToList();

        var doubles = energy.Where(f => f.EnergyAmount == 2).ToList();
        Assert.Equal(2, doubles.Count);
        Assert.All(doubles, f =>
        {
            Assert.Equal(EnergyType.Fist, f.ProvidedEnergyType);
            Assert.Equal(EnergyType.Mask, f.SecondProvidedEnergyType);
        });
        Assert.Single(energy, f => f.EnergyKind == EnergyKind.Generic && f.EnergyAmount == 1);
    }

    // Same entry: a card costing all four types spins down to wild
    // instead, and its double is a specific pair per card.
    [Fact]
    public void FourEnergyCharacter_HasItsOwnPairAndAWildSingle()
    {
        var card = Character("wl", "White Lantern Aquaman",
            EnergyType.Bolt, EnergyType.Fist, EnergyType.Mask, EnergyType.Shield);

        var energy = FacesOf(card).Skip(3).ToList();

        Assert.All(energy.Where(f => f.EnergyAmount == 2), f =>
        {
            Assert.Equal(EnergyType.Fist, f.ProvidedEnergyType);
            Assert.Equal(EnergyType.Shield, f.SecondProvidedEnergyType);
        });
        Assert.Single(energy, f => f.EnergyKind == EnergyKind.Wild);
    }

    // Rule 1.3.10 - "Basic Action dice provide generic energy" - unlike a
    // plain Action card, which has an energy type of its own.
    [Fact]
    public void BasicActionDie_IsThreeActionFacesAndThreeDoubleGeneric()
    {
        var faces = FacesOf(new CardDef
        { Id = "ba", Name = "BA", Type = CardType.BasicAction, PurchaseCost = 2, DieLimit = 3 });

        Assert.Equal(3, faces.Count(f => f.Status == DieStatus.Action));
        Assert.Equal(3, faces.Count(f => f.EnergyKind == EnergyKind.Generic && f.EnergyAmount == 2));
        Assert.Equal([null, 1, 2], faces.Where(f => f.Status == DieStatus.Action).Select(f => f.BurstStars));
    }

    [Fact]
    public void ActionDie_IsThreeActionFacesAndItsOwnEnergy()
    {
        var faces = FacesOf(new CardDef
        {
            Id = "act", Name = "Batarang", Type = CardType.Action, PurchaseCost = 2, DieLimit = 4,
            EnergyTypes = [EnergyType.Bolt],
        });

        Assert.Equal(3, faces.Count(f => f.Status == DieStatus.Action));
        Assert.Equal(2, faces.Count(f => f.ProvidedEnergyType == EnergyType.Bolt && f.EnergyAmount == 2));
        Assert.Single(faces, f => f.ProvidedEnergyType == EnergyType.Bolt && f.EnergyAmount == 1);
    }

    // Rule 2.6.1.4 - the face a double spins down to when half-spent.
    [Fact]
    public void SingleEnergyFace_IsGenericForACrossoverAndItsOwnTypeOtherwise()
    {
        var mono = Character("m", "M", EnergyType.Bolt);
        var cross = Character("x", "X", EnergyType.Fist, EnergyType.Mask);
        var state = Game(mono, cross);

        Assert.Equal(EnergyType.Bolt, DieFaces.SingleEnergyFace(DieOf(state, mono), mono)!.Value.ProvidedEnergyType);
        Assert.Equal(EnergyKind.Generic, DieFaces.SingleEnergyFace(DieOf(state, cross), cross)!.Value.EnergyKind);
    }

    // Rule 2.6.1.5 - a Basic Action die's energy faces are all doubles, so
    // it has nothing to spin down to.
    [Fact]
    public void SingleEnergyFace_IsNullForABasicActionDie()
    {
        var card = new CardDef { Id = "ba", Name = "BA", Type = CardType.BasicAction, PurchaseCost = 2, DieLimit = 3 };
        var state = Game(card);

        Assert.Null(DieFaces.SingleEnergyFace(DieOf(state, card), card));
    }

    // The roller only chooses an index, so a die with other than six sides
    // needs no roller change - the face list is the whole story.
    [Fact]
    public void Roll_LandsOnTheFaceTheRollerChose()
    {
        var card = Character("c", "C", EnergyType.Bolt);
        var state = Game(card);
        var die = DieOf(state, card);

        for (var index = 0; index < 6; index++)
        {
            var chosen = index;
            var face = DieFaces.Roll(state, new IndexRoller(chosen), die);
            Assert.Equal(DieFaces.Of(die, card)[chosen], face);
        }
    }

    private sealed class IndexRoller(int index) : IDiceRoller
    {
        public int Roll(DieInstance die, CardDef? card, int faceCount) => index;
    }
}
