using DiceFight.Engine;
using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Xunit;

namespace DiceFight.Engine.Tests;

// A fake roller lets step tests be deterministic without modeling real
// physical die face tables yet (see TurnEngine.RolledFace remarks).
file sealed class FixedRoller(
    DieStatus status, int level, EnergyKind energyKind = EnergyKind.None,
    EnergyType? providedEnergyType = null, int EnergyAmount = 1)
    : IDiceRoller
{
    public RolledFace Roll(DieInstance die, CardDef? card) => new(status, level, energyKind, providedEnergyType, EnergyAmount);
}

// Distinguishes "rerolled" from "kept as originally rolled" by giving each
// successive Roll() call, across the test's whole Roll+Reroll sequence, a
// strictly increasing Level - a die that gets rolled twice (Roll, then
// Reroll) ends up with a visibly different Level than one only ever
// rolled once.
file sealed class SequentialRoller : IDiceRoller
{
    private int _calls;
    public RolledFace Roll(DieInstance die, CardDef? card) => new(DieStatus.SidekickCharacter, ++_calls);
}

public class TurnEngineTests
{
    private static GameState CreateNewGame() =>
        GameState.NewGame(
            new Dictionary<string, CardDef>(),
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });

    [Fact]
    public void ClearAndDraw_FirstTurn_Draws3ToDiceFromBagAnd1OutOfPlay()
    {
        var state = CreateNewGame();

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(3, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Single(state.DiceIn("p1", Zone.OutOfPlay));
        Assert.Equal(4, state.DiceIn("p1", Zone.Bag).Count()); // 8 - 3 - 1
    }

    [Fact]
    public void ClearAndDraw_SubsequentTurn_Draws4ToDiceFromBag()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Empty(state.DiceIn("p1", Zone.OutOfPlay));
    }

    [Fact]
    public void ClearAndDraw_RefillsBagFromUsedPile_WhenBagRunsDry()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        // Empty the bag into the Used Pile except for 2 dice.
        var toStash = state.DiceIn("p1", Zone.Bag).Take(6).ToList();
        foreach (var die in toStash) die.Zone = Zone.UsedPile;

        TurnEngine.ClearAndDraw(state, new Random(1));

        // Still draws the full 4 by refilling from the Used Pile mid-draw:
        // 2 from the bag, then a refill from the 6-die Used Pile for the
        // other 2, leaving 4 of that refill in the bag.
        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Equal(4, state.DiceIn("p1", Zone.Bag).Count());
    }

    [Fact]
    public void ClearAndDraw_Shortfall_LosesLifeAndGainsVirtualEnergy()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        // Remove all but 2 dice entirely (simulate nothing left to draw).
        var toRemove = state.DiceIn("p1", Zone.Bag).Skip(2).ToList();
        foreach (var die in toRemove) state.Dice.Remove(die);

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(2, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Equal(18, state.PlayerOne.Life); // 20 - 2 short

        // Rule 1.4.4 - represented as a real spendable die in the Reserve
        // Pool, not a separate counter (see TurnEngine.AddVirtualGenericEnergy).
        var virtualDie = Assert.Single(state.DiceIn("p1", Zone.ReservePool));
        Assert.True(virtualDie.IsVirtualEnergy);
        Assert.Equal(EnergyKind.Generic, virtualDie.EnergyKind);
        Assert.Equal(2, virtualDie.EnergyAmount);
    }

    [Fact]
    public void ClearAndDraw_TriggersWhenDrawnForADieActuallyDrawnThisTurn()
    {
        var card = new CardDef
        {
            Id = "test-when-drawn", Name = "Test WhenDrawn", Type = CardType.BasicAction,
            PurchaseCost = 2, DieLimit = 3,
            Abilities = [new AbilityDef(TriggerType.WhenDrawn, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;

        // Move every real Sidekick out of reach (neither Bag nor Used
        // Pile) so the only die left to draw is guaranteed to be this one.
        foreach (var d in state.DiceIn("p1", Zone.Bag).ToList()) d.Zone = Zone.ReservePool;
        var whenDrawnDie = new DieInstance
        {
            Id = "p1-whendrawn-1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(whenDrawnDie);

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        Assert.Contains(whenDrawnDie.Id, state.DiceIn("p1", Zone.DiceFromBag).Select(d => d.Id));
        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.WhenDrawn, queue.Pending[0].Trigger);
        Assert.Equal(whenDrawnDie.Id, queue.Pending[0].SourceDieId);
    }

    [Fact]
    public void ClearAndDraw_DoesNotTriggerWhenDrawn_ForADieLeftInTheBag()
    {
        var card = new CardDef
        {
            Id = "test-when-drawn", Name = "Test WhenDrawn", Type = CardType.BasicAction,
            PurchaseCost = 2, DieLimit = 3,
            Abilities = [new AbilityDef(TriggerType.WhenDrawn, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;

        // 9 dice total for p1 (8 real Sidekicks + this one), only 4 get
        // drawn - not guaranteed to be this one, so just assert the
        // invariant: this die only ever ends up queued if it was actually
        // among the ones drawn.
        var whenDrawnDie = new DieInstance
        {
            Id = "p1-whendrawn-1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(whenDrawnDie);

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        var wasDrawn = state.DiceIn("p1", Zone.DiceFromBag).Any(d => d.Id == whenDrawnDie.Id);
        Assert.Equal(wasDrawn, queue.Count == 1);
    }

    [Fact]
    public void ClearAndDraw_OmittingTheQueue_StillDrawsNormally()
    {
        var card = new CardDef
        {
            Id = "test-when-drawn", Name = "Test WhenDrawn", Type = CardType.BasicAction,
            PurchaseCost = 2, DieLimit = 3,
            Abilities = [new AbilityDef(TriggerType.WhenDrawn, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;
        state.Dice.Add(new DieInstance
        {
            Id = "p1-whendrawn-1", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        });

        TurnEngine.ClearAndDraw(state, new Random(1)); // no queue supplied

        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    [Fact]
    public void CosmicCubeInfinitePossibilities_WhenDrawn_CanSendDrawnDiceOutOfPlayAndRedraw()
    {
        var cube = SampleCards.CosmicCubeInfinitePossibilities;
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [cube.Id] = cube },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;

        // Guarantee the Cosmic Cube die is drawn without depending on RNG
        // order: leave exactly 3 Sidekicks in the bag alongside it (4
        // total = this turn's draw count, so all 4 get picked regardless
        // of order), and stash the other 5 in the Used Pile - untouched
        // for now, but reachable for the replacement draws later.
        var sidekicks = state.DiceIn("p1", Zone.Bag).ToList();
        foreach (var d in sidekicks.Skip(3)) d.Zone = Zone.UsedPile;
        var cubeDie = new DieInstance
        {
            Id = "p1-cosmiccube-1", CardId = cube.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(cubeDie);

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        Assert.Equal(1, queue.Count);
        var drawnThisTurn = state.DiceIn("p1", Zone.DiceFromBag).ToList();

        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect,
            new EffectContext(
                state, ability.ControllerId, ability.SourceDieId,
                _ => drawnThisTurn.Select(d => d.Id).ToList(), // send everything drawn this turn Out of Play
                Random: new Random(2))));

        Assert.All(drawnThisTurn, d => Assert.Equal(Zone.OutOfPlay, d.Zone));
        // One replacement per die sent Out of Play, landing unrolled in
        // DiceFromBag (not immediately rolled - see RedrawFromBag's remarks).
        Assert.Equal(drawnThisTurn.Count, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.All(state.DiceIn("p1", Zone.DiceFromBag), d => Assert.Equal(DieStatus.Unrolled, d.Status));
    }

    private static (GameState state, CardDef swarmCard) CreateSwarmCatalogState()
    {
        var swarmCard = new CardDef
        {
            Id = "test-swarm", Name = "Test Swarm", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Keywords = [new KeywordInstance("Swarm")],
            Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [swarmCard.Id] = swarmCard },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;
        return (state, swarmCard);
    }

    private static DieInstance AddSwarmDie(GameState state, CardDef swarmCard, Zone zone, string suffix)
    {
        var die = new DieInstance
        {
            Id = $"p1-swarm-{suffix}", CardId = swarmCard.Id, OwnerId = "p1", ControllerId = "p1", Zone = zone,
            Status = zone is Zone.FieldZone or Zone.AttackZone ? DieStatus.Character : DieStatus.Unrolled,
            Level = 1,
        };
        state.Dice.Add(die);
        return die;
    }

    // Rule Appendix 1 - Swarm: "While a Character die with Swarm is
    // active, and you draw another copy of that die from your bag during
    // your Clear and Draw Step, draw an extra die." At draw time every
    // die here is Status=Unrolled with no face at all yet (Roll happens
    // later, in a separate step) - matchingCopy and the plain Sidekicks
    // drawn alongside it are otherwise indistinguishable (same Status,
    // same default Level). The only thing that could possibly be
    // triggering the bonus draw below is CardId - proof the check is
    // "which card is this," not anything about a rolled face.
    [Fact]
    public void ClearAndDraw_Swarm_DrawingAMatchingCopy_DrawsAnExtraDie()
    {
        var (state, swarmCard) = CreateSwarmCatalogState();
        AddSwarmDie(state, swarmCard, Zone.FieldZone, "active");
        var matchingCopy = AddSwarmDie(state, swarmCard, Zone.Bag, "copy");

        // Bag: matchingCopy + 3 Sidekicks = exactly this turn's draw
        // count, so all 4 are guaranteed drawn regardless of RNG order;
        // the other 5 Sidekicks sit in the Used Pile, reachable for the
        // Swarm bonus pull's own refill.
        var sidekicks = state.DiceIn("p1", Zone.Bag).Where(d => d.Id != matchingCopy.Id).ToList();
        foreach (var d in sidekicks.Skip(3)) d.Zone = Zone.UsedPile;

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        Assert.Equal(5, state.DiceIn("p1", Zone.DiceFromBag).Count()); // 4 normal + 1 Swarm bonus
    }

    // Rule (1) - "Swarm may trigger multiple times if multiple copies of
    // dice with Swarm are drawn."
    [Fact]
    public void ClearAndDraw_Swarm_TwoMatchingCopiesDrawn_TriggersTwice()
    {
        var (state, swarmCard) = CreateSwarmCatalogState();
        AddSwarmDie(state, swarmCard, Zone.FieldZone, "active");
        var copy1 = AddSwarmDie(state, swarmCard, Zone.Bag, "copy1");
        var copy2 = AddSwarmDie(state, swarmCard, Zone.Bag, "copy2");

        var sidekicks = state.DiceIn("p1", Zone.Bag).Where(d => d.CardId != swarmCard.Id).ToList();
        foreach (var d in sidekicks.Skip(2)) d.Zone = Zone.UsedPile;
        // Bag now: copy1 + copy2 + 2 Sidekicks = 4 = this turn's draw count.

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        Assert.Equal(6, state.DiceIn("p1", Zone.DiceFromBag).Count()); // 4 normal + 2 Swarm bonuses
    }

    // Rule (4) - "You only draw one die no matter how many copies of a
    // Character die with Swarm are active." Two active copies, but only
    // one drawn copy - still just one bonus draw, not two.
    [Fact]
    public void ClearAndDraw_Swarm_TwoActiveCopiesButOnlyOneDrawn_TriggersOnce()
    {
        var (state, swarmCard) = CreateSwarmCatalogState();
        AddSwarmDie(state, swarmCard, Zone.FieldZone, "active1");
        AddSwarmDie(state, swarmCard, Zone.AttackZone, "active2");
        var copyInBag = AddSwarmDie(state, swarmCard, Zone.Bag, "copy");

        var sidekicks = state.DiceIn("p1", Zone.Bag).Where(d => d.Id != copyInBag.Id).ToList();
        foreach (var d in sidekicks.Skip(3)) d.Zone = Zone.UsedPile;

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        Assert.Equal(5, state.DiceIn("p1", Zone.DiceFromBag).Count()); // 4 normal + only 1 bonus
    }

    // Rule (3) - "All events related to the act of drawing dice during
    // the Clear and Draw Step (this includes Swarm) occur simultaneously."
    // Modeled as: Swarm is checked once against the original draw batch,
    // never re-checked against its own bonus draws - a bonus-drawn copy
    // must not cause a second bonus draw.
    [Fact]
    public void ClearAndDraw_Swarm_BonusDrawnCopyDoesNotChainTrigger()
    {
        var (state, swarmCard) = CreateSwarmCatalogState();
        AddSwarmDie(state, swarmCard, Zone.FieldZone, "active");
        var copyInBag = AddSwarmDie(state, swarmCard, Zone.Bag, "copy-main");
        // The only die reachable for the bonus pull once the normal draw
        // empties the bag - and it happens to be another Swarm copy too.
        var copyForBonus = AddSwarmDie(state, swarmCard, Zone.UsedPile, "copy-bonus");

        // Out of Play, not Reserve Pool - ClearAndDraw's own opening
        // sweep (rule 2.3.1) empties the Reserve Pool into the Used Pile
        // before the draw even starts, which would make these reachable
        // for the bonus pull's refill too and defeat the point of this test.
        var sidekicks = state.DiceIn("p1", Zone.Bag).Where(d => d.Id != copyInBag.Id).ToList();
        foreach (var d in sidekicks.Skip(3)) d.Zone = Zone.OutOfPlay;

        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(1), queue);

        // 4 normal (including copyInBag, triggering 1 bonus) + exactly 1
        // bonus (copyForBonus) - if it chained, this would be 6, not 5.
        Assert.Equal(5, state.DiceIn("p1", Zone.DiceFromBag).Count());
        Assert.Contains(copyForBonus.Id, state.DiceIn("p1", Zone.DiceFromBag).Select(d => d.Id));
    }

    // Rule (2) - "If Swarm is triggered and there are no dice left in
    // your bag to pull, or in the Used Pile to refill your bag, you would
    // not lose one Life and gain one virtual generic energy for being
    // unable to pull those dice." Unlike the ordinary Clear and Draw
    // shortfall (rule 2.3.10), a failed Swarm bonus pull is simply a
    // no-op - it is deliberately kept out of the shortfall calculation.
    [Fact]
    public void ClearAndDraw_Swarm_FailedBonusPull_DoesNotCauseAShortfallPenalty()
    {
        var (state, swarmCard) = CreateSwarmCatalogState();
        AddSwarmDie(state, swarmCard, Zone.FieldZone, "active");
        var copyInBag = AddSwarmDie(state, swarmCard, Zone.Bag, "copy");

        // Bag has exactly this turn's draw count (4) - the normal draw
        // succeeds in full, no shortfall from that. Nothing is left
        // anywhere (Bag or Used Pile) for the Swarm bonus pull afterward -
        // Out of Play rather than Reserve Pool, since ClearAndDraw's own
        // opening sweep (rule 2.3.1) would otherwise empty the Reserve
        // Pool into the Used Pile before the draw even starts.
        var sidekicks = state.DiceIn("p1", Zone.Bag).Where(d => d.Id != copyInBag.Id).ToList();
        foreach (var d in sidekicks.Skip(3)) d.Zone = Zone.OutOfPlay;

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count()); // normal draw succeeded fully
        Assert.Equal(Player.StartingLife, state.PlayerOne.Life); // no penalty for the failed bonus pull
        Assert.DoesNotContain(state.Dice, d => d.IsVirtualEnergy);
    }

    [Fact]
    public void ParademonSwarm_RealCard_DrawsAnExtraDieWhenAnotherCopyIsDrawn()
    {
        var card = SampleCards.Parademon;
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;

        var active = new DieInstance
        {
            Id = "p1-parademon-active", CardId = card.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(active);
        var copyInBag = new DieInstance
        {
            Id = "p1-parademon-copy", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(copyInBag);

        var sidekicks = state.DiceIn("p1", Zone.Bag).Where(d => d.Id != copyInBag.Id).ToList();
        foreach (var d in sidekicks.Skip(3)) d.Zone = Zone.UsedPile;

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(5, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    // Darkseid ("Force of Entropy"): "While Darkseid is active, your
    // Sidekicks gain Swarm." Reaches an active Ally die too (Alfred
    // Pennyworth counts as a Sidekick while fielded - DieStats.
    // CountsAsSidekick), but Swarm's own match is still on the specific
    // die's card identity: a granted-Swarm real Sidekick only matches
    // another real Sidekick (they're mutually fungible - CardId null for
    // all of them), and a granted-Swarm Alfred only matches another copy
    // of Alfred specifically. Four tests: the user's two "does NOT
    // trigger" examples, plus a positive control for each so a
    // regression that broke the grant entirely wouldn't slip through
    // silently as "everything correctly doesn't trigger."
    private static (GameState state, DieInstance darkseid) CreateDarkseidGame()
    {
        var state = GameState.NewGame(
            SampleCards.BuildCatalog(),
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.IsFirstTurn = false;
        var darkseid = new DieInstance
        {
            Id = "p1-darkseid", CardId = SampleCards.Darkseid.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(darkseid);
        return (state, darkseid);
    }

    [Fact]
    public void ClearAndDraw_DarkseidSwarm_ActiveGrantedSidekick_DrawingAlfred_DoesNotTrigger()
    {
        var (state, _) = CreateDarkseidGame();
        var activeSidekick = state.DiceIn("p1", Zone.Bag).First();
        activeSidekick.Zone = Zone.FieldZone;
        activeSidekick.Status = DieStatus.SidekickCharacter;

        // Every other real Sidekick stashed out of reach (neither Bag nor
        // Used Pile), so the only thing drawable is Alfred - isolates
        // whether drawing HIM specifically triggers the active plain
        // Sidekick's granted Swarm. It shouldn't - different card identity.
        foreach (var d in state.DiceIn("p1", Zone.Bag).ToList()) d.Zone = Zone.OutOfPlay;
        var alfredInBag = new DieInstance
        {
            Id = "p1-alfred-copy", CardId = SampleCards.AlfredPennyworthCaretaker.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(alfredInBag);

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Single(state.DiceIn("p1", Zone.DiceFromBag)); // just Alfred - no Swarm bonus
    }

    [Fact]
    public void ClearAndDraw_DarkseidSwarm_ActiveGrantedSidekick_DrawingAnotherSidekick_Triggers()
    {
        var (state, _) = CreateDarkseidGame();
        var activeSidekick = state.DiceIn("p1", Zone.Bag).First();
        activeSidekick.Zone = Zone.FieldZone;
        activeSidekick.Status = DieStatus.SidekickCharacter;

        var remainingSidekicks = state.DiceIn("p1", Zone.Bag).ToList();
        var copyInBag = remainingSidekicks[0];
        foreach (var d in remainingSidekicks.Skip(1)) d.Zone = Zone.OutOfPlay;

        // 3 unrelated filler dice (a real Character card, not a Sidekick
        // and not Alfred) round out this turn's other draw slots without
        // being eligible to match anything themselves.
        for (var i = 0; i < 3; i++)
        {
            state.Dice.Add(new DieInstance
            {
                Id = $"p1-filler-{i}", CardId = SampleCards.Cyclops.Id, OwnerId = "p1", ControllerId = "p1",
                Zone = Zone.Bag,
            });
        }
        // One more spare, reachable only via the Used Pile, for the
        // Swarm bonus pull itself to succeed.
        state.Dice.Add(new DieInstance
        {
            Id = "p1-bonus-spare", CardId = SampleCards.Cyclops.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.UsedPile,
        });

        TurnEngine.ClearAndDraw(state, new Random(1));

        // 4 normal draws (copyInBag + 3 filler) + exactly 1 Swarm bonus.
        Assert.Equal(5, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    [Fact]
    public void ClearAndDraw_DarkseidSwarm_ActiveAlfred_DrawingPlainSidekicks_DoesNotTrigger()
    {
        var (state, _) = CreateDarkseidGame();
        var activeAlfred = new DieInstance
        {
            Id = "p1-alfred-active", CardId = SampleCards.AlfredPennyworthCaretaker.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(activeAlfred);

        // No isolation needed here: none of the default bag's plain
        // Sidekicks can ever match Alfred's own card identity, so drawing
        // several of them alongside is safe, unambiguous filler.
        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count()); // no Swarm bonus
    }

    [Fact]
    public void ClearAndDraw_DarkseidSwarm_ActiveAlfred_DrawingAnotherAlfred_Triggers()
    {
        var (state, _) = CreateDarkseidGame();
        var activeAlfred = new DieInstance
        {
            Id = "p1-alfred-active", CardId = SampleCards.AlfredPennyworthCaretaker.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(activeAlfred);
        var alfredCopyInBag = new DieInstance
        {
            Id = "p1-alfred-copy", CardId = SampleCards.AlfredPennyworthCaretaker.Id,
            OwnerId = "p1", ControllerId = "p1", Zone = Zone.Bag,
        };
        state.Dice.Add(alfredCopyInBag);

        // Bag: alfredCopyInBag + 3 real Sidekicks = this turn's draw
        // count exactly, so all 4 are guaranteed drawn regardless of RNG
        // order; one more Sidekick sits in the Used Pile as the Swarm
        // bonus pull's own spare, the rest stashed out of reach entirely.
        var sidekicks = state.DiceIn("p1", Zone.Bag).Where(d => d.Id != alfredCopyInBag.Id).ToList();
        sidekicks[3].Zone = Zone.UsedPile;
        foreach (var d in sidekicks.Skip(4)) d.Zone = Zone.OutOfPlay;

        TurnEngine.ClearAndDraw(state, new Random(1));

        // alfredCopyInBag + 3 Sidekicks drawn (4) + 1 Swarm bonus (5) -
        // the 3 plain Sidekicks are safe filler (can't match Alfred's own
        // identity), only alfredCopyInBag itself triggers.
        Assert.Equal(5, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    [Fact]
    public void ClearAndDraw_SweepsExistingPrepAreaDiceIntoDiceFromPrep()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        // e.g. left there by a KO, or Shocking Grasp's own Conditional Prep,
        // sometime before this player's turn came back around.
        var leftover = state.DiceIn("p1", Zone.Bag).First();
        leftover.Zone = Zone.PrepArea;

        TurnEngine.ClearAndDraw(state, new Random(1));

        Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
        Assert.Contains(leftover, state.DiceIn("p1", Zone.DiceFromPrep));
        Assert.Equal(4, state.DiceIn("p1", Zone.DiceFromBag).Count());
    }

    [Fact]
    public void Roll_RollsAndSettlesDiceStraightIntoReservePool()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        var leftover = state.DiceIn("p1", Zone.Bag).First();
        leftover.Zone = Zone.PrepArea;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state); // Main is skipped for this test's purposes; just move past ClearAndDraw
        state.CurrentStep = TurnStep.RollAndReroll;

        TurnEngine.Roll(state, new FixedRoller(DieStatus.SidekickCharacter, 1));

        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.Equal(5, reserve.Count); // 4 fresh draws + the 1 carried over from the Prep Area
        Assert.Contains(leftover, reserve);
        Assert.All(reserve, d => Assert.Equal(DieStatus.SidekickCharacter, d.Status));
        Assert.Empty(state.DiceIn("p1", Zone.DiceFromBag));
        Assert.Empty(state.DiceIn("p1", Zone.DiceFromPrep));
    }

    [Fact]
    public void Roll_TrustsTheRollersEnergyKindAndType_ForEnergyFaces()
    {
        // A Sidekick rolling a specific-type energy face (not Wild) is
        // exactly the case ApplyRoll used to get wrong - it used to
        // hardcode every Sidekick energy face as Wild regardless of what
        // the roller actually rolled. Now it just trusts the roller.
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Bolt));

        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.All(reserve, d => Assert.Equal(EnergyKind.Specific, d.EnergyKind));
        Assert.All(reserve, d => Assert.Equal(EnergyType.Bolt, d.ProvidedEnergyType));
    }

    [Fact]
    public void Reroll_RerollsOnlySelectedDice_LeavesEveryoneElseAsRolled()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        var roller = new SequentialRoller();
        TurnEngine.Roll(state, roller);
        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        var toReroll = reserve[0];
        var levelsAfterRoll = reserve.ToDictionary(d => d.Id, d => d.Level);

        TurnEngine.Reroll(state, new AbilityQueue(), roller, [toReroll.Id]);

        Assert.NotEqual(levelsAfterRoll[toReroll.Id], toReroll.Level); // rerolled
        foreach (var die in reserve.Where(d => d.Id != toReroll.Id))
            Assert.Equal(levelsAfterRoll[die.Id], die.Level); // everyone else kept as originally rolled
        Assert.All(reserve, d => Assert.Equal(Zone.ReservePool, d.Zone)); // reroll doesn't move anyone

        // Rule 2.4.3/2.4.4 - the reroll decision is made once; nothing else
        // is legal in Roll & Reroll afterward, so it auto-advances to Main.
        Assert.Equal(TurnStep.Main, state.CurrentStep);
    }

    [Fact]
    public void Reroll_CanOnlyBeUsedOnce_EvenWithNoDiceSelected()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        var roller = new SequentialRoller();
        TurnEngine.Roll(state, roller);
        TurnEngine.Reroll(state, new AbilityQueue(), roller, []); // "I don't want to reroll anything" is still the one decision
        Assert.Equal(TurnStep.Main, state.CurrentStep);

        var ex = Assert.Throws<InvalidOperationException>(() => TurnEngine.Reroll(state, new AbilityQueue(), roller, []));
        Assert.Contains("RollAndReroll", ex.Message);
    }

    private static (GameState State, DieInstance Die) CreateEnergizeGame(int energyAmount)
    {
        var card = new CardDef
        {
            Id = "test-energize", Name = "Test Energize", Type = CardType.Character,
            PurchaseCost = 1, DieLimit = 1,
            Keywords = [new KeywordInstance("Energize")],
            Abilities = [new AbilityDef(TriggerType.Energize, Cost: null, Effect: new GainLife(1))],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [card.Id] = card },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        state.CurrentStep = TurnStep.RollAndReroll;

        var die = new DieInstance
        {
            Id = "energize-die", CardId = card.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.ReservePool, Status = DieStatus.Energy, EnergyKind = EnergyKind.Generic, EnergyAmount = energyAmount,
        };
        state.Dice.Add(die);
        return (state, die);
    }

    [Fact]
    public void Reroll_EnergizeDieLeftOnDoubleEnergy_TriggersOnceAtEndOfStep()
    {
        var (state, die) = CreateEnergizeGame(energyAmount: 2);
        var queue = new AbilityQueue();

        // Nothing selected to reroll - the die stays exactly as it was
        // rolled, so Energize should fire once the step closes.
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1), []);

        Assert.Equal(1, queue.Count);
        Assert.Equal(TriggerType.Energize, queue.Pending[0].Trigger);
        Assert.Equal(die.Id, queue.Pending[0].SourceDieId);
    }

    [Fact]
    public void Reroll_EnergizeDieRerolledOffDoubleEnergy_DoesNotTrigger()
    {
        var (state, die) = CreateEnergizeGame(energyAmount: 2);
        var queue = new AbilityQueue();

        // Rerolling it lands on single energy this time - Energize checks
        // the step's final state, not the initial roll it's replacing.
        TurnEngine.Reroll(state, queue, new FixedRoller(DieStatus.Energy, 1, EnergyKind.Generic), [die.Id]);

        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Reroll_EnergizeDieRerolledButStillDoubleEnergy_StillTriggersOnce()
    {
        var (state, die) = CreateEnergizeGame(energyAmount: 2);
        var queue = new AbilityQueue();

        var roller = new FixedRoller(DieStatus.Energy, 1, EnergyKind.Generic, EnergyAmount: 2);
        TurnEngine.Reroll(state, queue, roller, [die.Id]);

        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public void Roll_LeavesDieSteppedIntoPrepAreaAfterThisSteps_ClearPhase_ForNextTurn()
    {
        // Models a card like Pepper Potts ("draw an extra die at the
        // beginning of your Clear and Draw Step... If it is a non-Sidekick
        // die, Prep it.") - a die that lands in the Prep Area *after* this
        // step's own Clear-phase sweep already ran must sit out this
        // turn's Roll & Reroll and only get rolled next turn.
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));
        var lateEntrant = state.DiceIn("p1", Zone.Bag).First();
        lateEntrant.Zone = Zone.PrepArea; // simulates a same-step Prep effect
        TurnEngine.AdvanceStep(state);
        state.CurrentStep = TurnStep.RollAndReroll;

        TurnEngine.Roll(state, new FixedRoller(DieStatus.SidekickCharacter, 1));

        Assert.Equal(4, state.DiceIn("p1", Zone.ReservePool).Count()); // just this turn's draw
        Assert.Equal(Zone.PrepArea, lateEntrant.Zone); // untouched - waits for next turn
    }

    [Fact]
    public void FullTurnCycle_SkippingAttack_HandsPriorityToOpponent()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;

        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0));
        TurnEngine.AdvanceStep(state);
        Assert.Equal(TurnStep.Main, state.CurrentStep);

        TurnEngine.SkipAttackStep(state);
        Assert.Equal(TurnStep.CleanUp, state.CurrentStep);

        TurnEngine.CleanUp(state);

        Assert.Equal("p2", state.ActivePlayerId);
        Assert.Equal(TurnStep.ClearAndDraw, state.CurrentStep);
        Assert.False(state.IsFirstTurn);
    }

    [Fact]
    public void AdvanceStep_PastCleanUp_Throws()
    {
        var state = CreateNewGame();
        state.CurrentStep = TurnStep.CleanUp;

        Assert.Throws<InvalidOperationException>(() => TurnEngine.AdvanceStep(state));
    }

    [Fact]
    public void ClearAndDraw_SweepsUnspentReservePoolDice_ResetsThemToUnrolled()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Mask));
        var rolled = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.All(rolled, d => Assert.Equal(DieStatus.Energy, d.Status)); // sanity - actually rolled

        // Nothing gets spent; the turn passes and it's p1's Clear & Draw again.
        state.CurrentStep = TurnStep.ClearAndDraw;
        TurnEngine.ClearAndDraw(state, new Random(2));

        // Rulebook's "More About Dice" - the Used Pile holds "unrolled
        // dice," and it doesn't matter what face happened to be showing.
        Assert.All(rolled, d => Assert.Equal(Zone.UsedPile, d.Zone));
        Assert.All(rolled, d => Assert.False(d.IsRolled));
        Assert.All(rolled, d => Assert.Equal(DieStatus.Unrolled, d.Status));
        Assert.All(rolled, d => Assert.Equal(EnergyKind.None, d.EnergyKind));
        Assert.All(rolled, d => Assert.Null(d.ProvidedEnergyType));
        Assert.All(rolled, d => Assert.Equal(1, d.EnergyAmount));
    }

    [Fact]
    public void ClearAndDraw_SweepsUnspentReservePoolDice_TwoDifferentRolledFacesBecomeIndistinguishable()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new SequentialRoller()); // each die gets a different Level
        var rolled = state.DiceIn("p1", Zone.ReservePool).ToList();
        Assert.True(rolled.Select(d => d.Level).Distinct().Count() > 1); // sanity - genuinely different faces

        state.CurrentStep = TurnStep.ClearAndDraw;
        TurnEngine.ClearAndDraw(state, new Random(2));

        // Once dormant, dice that were on different faces are - correctly,
        // per the rulebook - indistinguishable from each other, which is
        // exactly what lets the web client collapse them into one "×N"
        // chip instead of listing each separately.
        var distinctStates = rolled
            .Select(d => (d.Status, d.Level, d.Damage, d.EnergyKind, d.ProvidedEnergyType, d.EnergyAmount))
            .Distinct()
            .Count();
        Assert.Equal(1, distinctStates);
    }

    [Fact]
    public void CleanUp_SweepsOutOfPlayAndUnusedActionDice_ResetsThemToUnrolled()
    {
        var state = CreateNewGame();
        state.IsFirstTurn = false;
        TurnEngine.ClearAndDraw(state, new Random(1));
        TurnEngine.AdvanceStep(state);
        TurnEngine.Roll(state, new FixedRoller(DieStatus.Energy, 0, EnergyKind.Wild));
        TurnEngine.AdvanceStep(state); // Main

        // One energy die spent (-> Out of Play), one Action die rolled but
        // never used (-> stays in the Reserve Pool on its Action face).
        var reserve = state.DiceIn("p1", Zone.ReservePool).ToList();
        var spent = reserve[0];
        spent.Zone = Zone.OutOfPlay;
        var unusedAction = reserve[1];
        unusedAction.Status = DieStatus.Action;

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        foreach (var die in new[] { spent, unusedAction })
        {
            Assert.Equal(Zone.UsedPile, die.Zone);
            Assert.False(die.IsRolled);
            Assert.Equal(DieStatus.Unrolled, die.Status);
            Assert.Equal(EnergyKind.None, die.EnergyKind);
        }
    }

    // Keyword Deadly - "At the end of the turn, character dice that were
    // engaged with a Character die that has Deadly are KO'd." Recorded
    // earlier by CombatEngine.DeclareBlockers (see CombatEngineTests);
    // this just covers CleanUp's own half of resolving it.
    [Fact]
    public void CleanUp_KOsDiceRecordedAsDeadlyEngaged()
    {
        var state = CreateNewGame();
        var engaged = state.DiceIn("p1", Zone.Bag).First();
        engaged.Zone = Zone.FieldZone;
        engaged.Status = DieStatus.SidekickCharacter;
        state.DeadlyEngagedDieIds.Add(engaged.Id);

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        Assert.Equal(Zone.PrepArea, engaged.Zone);
        Assert.Equal(DieStatus.Unrolled, engaged.Status);
        Assert.Empty(state.DeadlyEngagedDieIds);
    }

    // Clarification: "...even if the Character die with Deadly has been
    // KO'd or leaves the Field Zone" - the engaged die is KO'd
    // regardless of what happened to the Deadly die in the meantime;
    // modeled here by simply never even needing the Deadly die itself to
    // still exist in a meaningful state by Clean Up.
    [Fact]
    public void CleanUp_KOsEngagedDie_EvenThoughTheDeadlyDieIsNoLongerTracked()
    {
        var state = CreateNewGame();
        var engaged = state.DiceIn("p1", Zone.Bag).First();
        engaged.Zone = Zone.FieldZone;
        engaged.Status = DieStatus.SidekickCharacter;
        state.DeadlyEngagedDieIds.Add(engaged.Id); // the only bookkeeping Deadly leaves behind

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        Assert.Equal(Zone.PrepArea, engaged.Zone); // KO'd regardless
    }

    [Fact]
    public void CleanUp_DeadlyKO_RespectsRegenerate_WhenRollerSupplied()
    {
        var regenCard = new CardDef
        {
            Id = "regen-engaged", Name = "Regen Engaged", Type = CardType.Character,
            PurchaseCost = 2, DieLimit = 4,
            Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
            Keywords = [new KeywordInstance("Regenerate")],
        };
        var state = GameState.NewGame(
            new Dictionary<string, CardDef> { [regenCard.Id] = regenCard },
            new Player { Id = "p1", Name = "Player One" },
            new Player { Id = "p2", Name = "Player Two" });
        var engaged = new DieInstance
        {
            Id = "p1-regen-1", CardId = regenCard.Id, OwnerId = "p1", ControllerId = "p1",
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(engaged);
        state.DeadlyEngagedDieIds.Add(engaged.Id);

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state, new FixedRoller(DieStatus.Character, 2));

        Assert.Equal(Zone.FieldZone, engaged.Zone); // Regenerated, not KO'd
        Assert.Equal(2, engaged.Level);
        Assert.Empty(state.DeadlyEngagedDieIds);
    }

    // Keyword Intimidate - "remove target opposing Character die from the
    // Field Zone until end of turn." No tracked set (unlike Deadly) -
    // Zone.Intimidated is itself the marker CleanUp sweeps back.
    [Fact]
    public void CleanUp_ReturnsIntimidatedDiceToFieldZone_PreservingLevelAndStatus()
    {
        var state = CreateNewGame();
        var intimidated = state.DiceIn("p1", Zone.Bag).First();
        intimidated.Zone = Zone.Intimidated;
        intimidated.Status = DieStatus.SidekickCharacter;
        intimidated.Level = 1;

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        Assert.Equal(Zone.FieldZone, intimidated.Zone);
        Assert.Equal(DieStatus.SidekickCharacter, intimidated.Status); // not reset - not a dormant zone
    }

    // Intimidate always targets an *opposing* die relative to whoever
    // fielded it - the die sitting in Zone.Intimidated could belong to
    // either player, so the return sweep isn't scoped to the active player.
    [Fact]
    public void CleanUp_ReturnsIntimidatedDice_RegardlessOfWhichPlayerControlsThem()
    {
        var state = CreateNewGame();
        var p2Intimidated = state.DiceIn("p2", Zone.Bag).First();
        p2Intimidated.Zone = Zone.Intimidated;
        p2Intimidated.Status = DieStatus.SidekickCharacter;

        state.CurrentStep = TurnStep.CleanUp; // p1 is still ActivePlayerId
        TurnEngine.CleanUp(state);

        Assert.Equal(Zone.FieldZone, p2Intimidated.Zone);
    }

    // Keyword Obscure - "unblockable until end of turn" expires at Clean
    // Up, same lifetime shape as MustBlockThisTurn. Enforcement itself
    // (CombatEngine.DeclareBlockers/ActiveCallOutTargets) is covered in
    // CombatEngineTests; this just covers the turn-scoped expiry.
    [Fact]
    public void CleanUp_ClearsObscuredCardIds()
    {
        var state = CreateNewGame();
        state.ObscuredCardIds.Add("some-card");

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        Assert.Empty(state.ObscuredCardIds);
    }

    // Keyword Strike's own "fielded this turn" record - see DieStats.
    // HasStrikeBonus for how it's actually consumed (a live check, not a
    // triggered ability, so there's nothing to drain here).
    [Fact]
    public void Field_AddsTheFieldedDieToFieldedThisTurn()
    {
        var state = CreateNewGame();
        state.CurrentStep = TurnStep.Main;
        var die = state.DiceIn("p1", Zone.Bag).First();
        die.Zone = Zone.ReservePool;
        die.Status = DieStatus.SidekickCharacter;

        TurnEngine.Field(state, new AbilityQueue(), die.Id, energyDieIdsToSpend: []);

        Assert.Contains(die.Id, state.FieldedThisTurn);
    }

    [Fact]
    public void CleanUp_ClearsFieldedThisTurn()
    {
        var state = CreateNewGame();
        state.FieldedThisTurn.Add("some-die");

        state.CurrentStep = TurnStep.CleanUp;
        TurnEngine.CleanUp(state);

        Assert.Empty(state.FieldedThisTurn);
    }
}
