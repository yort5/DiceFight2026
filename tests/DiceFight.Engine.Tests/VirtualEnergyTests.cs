using DiceFight.Engine;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

// Virtual generic energy - the half of a double-Generic face the Active
// player keeps when they spend only one of it (rule 2.6.1.6), and the
// energy gained for a short draw (rule 1.4.4).
//
// Three rules independently say when it expires, all of them the same:
//   1.4.5    "must be spent by the end of the Main Step, or it will be lost"
//   2.6.1.6  "keep the other as a virtual generic energy until the end of the Main Step"
//   2.6.7.1(2) "At the end of the Main Step [...] any unspent virtual generic energy is lost"
public class VirtualEnergyTests
{
    private static GameState MainStepGame()
    {
        var p1 = new Player { Id = "p1", Name = "P1" };
        var p2 = new Player { Id = "p2", Name = "P2" };
        var state = GameState.NewGame(new Dictionary<string, CardDef>(), p1, p2);
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    private static void GiveVirtualEnergy(GameState state, string playerId, int amount) =>
        state.Dice.Add(new DieInstance
        {
            Id = $"{playerId}-virtual-generic",
            OwnerId = playerId,
            ControllerId = playerId,
            Zone = Zone.ReservePool,
            Status = DieStatus.Energy,
            EnergyKind = EnergyKind.Generic,
            EnergyAmount = amount,
            IsVirtualEnergy = true,
        });

    private static bool HasVirtualEnergy(GameState state) => state.Dice.Any(d => d.IsVirtualEnergy);

    [Fact]
    public void EnteringTheAttackStep_LosesUnspentVirtualEnergy()
    {
        var state = MainStepGame();
        GiveVirtualEnergy(state, "p1", 1);

        TurnEngine.EnterAttackStep(state);

        Assert.False(HasVirtualEnergy(state));
    }

    [Fact]
    public void SkippingTheAttackStep_LosesUnspentVirtualEnergy()
    {
        var state = MainStepGame();
        GiveVirtualEnergy(state, "p1", 2);

        TurnEngine.SkipAttackStep(state);

        Assert.False(HasVirtualEnergy(state));
    }

    [Fact]
    public void AdvancingOutOfMain_LosesUnspentVirtualEnergy()
    {
        var state = MainStepGame();
        GiveVirtualEnergy(state, "p1", 1);

        TurnEngine.AdvanceStep(state);

        Assert.Equal(TurnStep.Attack, state.CurrentStep);
        Assert.False(HasVirtualEnergy(state));
    }

    // It survives the steps BEFORE the Main Step ends - a short draw in
    // Clear and Draw (rule 1.4.4) is meant to be spendable in Main.
    [Fact]
    public void AdvancingIntoMain_KeepsVirtualEnergy()
    {
        var state = MainStepGame();
        state.CurrentStep = TurnStep.RollAndReroll;
        GiveVirtualEnergy(state, "p1", 3);

        TurnEngine.AdvanceStep(state);

        Assert.Equal(TurnStep.Main, state.CurrentStep);
        Assert.True(HasVirtualEnergy(state));
    }
}
