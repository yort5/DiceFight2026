using DiceFight.Engine.Model;

namespace DiceFight.Engine;

// The result of rolling a single die (rule 1.6 - Rolled vs. Unrolled Dice).
// NOTE: real Dice Masters dice have a fixed, card-specific face layout
// (typically a mix of energy and character faces) that isn't captured by
// CardDef yet - that's physical-component data, not rules logic, and is
// tracked as a follow-up. IDiceRoller exists so the turn engine's zone/step
// mechanics can be built and tested now, independent of where face results
// come from (a real weighted roll, a human reporting a physical die, etc.).
public readonly record struct RolledFace(DieStatus Status, int Level);

public interface IDiceRoller
{
    RolledFace Roll(DieInstance die, CardDef? card);
}

public static class TurnEngine
{
    private static readonly TurnStep[] StepOrder =
        [TurnStep.ClearAndDraw, TurnStep.RollAndReroll, TurnStep.Main, TurnStep.Attack, TurnStep.CleanUp];

    // Rule 2.2.4 - once a step is completed, a player cannot go back to it
    // in the same turn.
    public static void AdvanceStep(GameState state)
    {
        var index = Array.IndexOf(StepOrder, state.CurrentStep);
        if (index < 0 || index == StepOrder.Length - 1)
            throw new InvalidOperationException(
                "Cannot advance past Clean Up - call CleanUp(...) to end the turn instead.");
        state.CurrentStep = StepOrder[index + 1];
    }

    // Rule 2.3 - Clear and Draw Step.
    public static void ClearAndDraw(GameState state, Random random)
    {
        if (state.CurrentStep != TurnStep.ClearAndDraw)
            throw new InvalidOperationException($"Expected ClearAndDraw step, was {state.CurrentStep}.");

        var activeId = state.ActivePlayerId;

        // Rule 2.3.1 - clear the Reserve Pool (unspent energy from the
        // opponent's turn) to the Used Pile.
        foreach (var die in state.DiceIn(activeId, Zone.ReservePool).ToList())
            die.Zone = Zone.UsedPile;

        // Rule 2.3.3 - the very first turn of the game draws 3, not 4, with
        // the 4th die drawn and set Out of Play instead of into Prep Area.
        var drawCount = state.IsFirstTurn ? 3 : 4;
        var drawn = DrawFromBag(state, activeId, drawCount, random);
        foreach (var die in drawn) die.Zone = Zone.PrepArea;

        if (state.IsFirstTurn)
        {
            var extra = DrawFromBag(state, activeId, 1, random);
            foreach (var die in extra) die.Zone = Zone.OutOfPlay;
        }

        // Rule 2.3.10 - couldn't draw the required count even after
        // refilling from the Used Pile: lose 1 Life and gain 1 virtual
        // generic energy per die short.
        var shortfall = drawCount - drawn.Count;
        if (shortfall > 0)
        {
            var player = state.GetPlayer(activeId);
            player.Life -= shortfall;
            player.VirtualGenericEnergy += shortfall;
        }
    }

    // Rule 2.3.2/2.3.5-2.3.9 - draw randomly from the bag, refilling once
    // from the Used Pile if the bag runs dry mid-draw; stops short (rather
    // than throwing) if there is truly nothing left to draw.
    private static List<DieInstance> DrawFromBag(GameState state, string playerId, int count, Random random)
    {
        var drawn = new List<DieInstance>();
        for (var i = 0; i < count; i++)
        {
            var bag = state.DiceIn(playerId, Zone.Bag).ToList();
            if (bag.Count == 0)
            {
                var usedPile = state.DiceIn(playerId, Zone.UsedPile).ToList();
                if (usedPile.Count == 0) break;
                foreach (var die in usedPile) die.Zone = Zone.Bag;
                bag = state.DiceIn(playerId, Zone.Bag).ToList();
            }

            var picked = bag[random.Next(bag.Count)];
            picked.Zone = Zone.PrepArea; // provisional, so it isn't drawn twice this loop
            drawn.Add(picked);
        }

        return drawn;
    }

    // Rule 2.4 - Roll and Reroll Step. chooseRerolls represents the
    // player's decision (rule 2.4.3 - any/all/none, as a single group).
    public static void RollAndReroll(
        GameState state,
        IDiceRoller roller,
        Func<IReadOnlyList<DieInstance>, IReadOnlyList<string>> chooseRerolls)
    {
        if (state.CurrentStep != TurnStep.RollAndReroll)
            throw new InvalidOperationException($"Expected RollAndReroll step, was {state.CurrentStep}.");

        var prepDice = state.DiceIn(state.ActivePlayerId, Zone.PrepArea).ToList();

        foreach (var die in prepDice)
            ApplyRoll(state, roller, die);

        var rerollIds = chooseRerolls(prepDice).ToHashSet();
        foreach (var die in prepDice.Where(d => rerollIds.Contains(d.Id)))
            ApplyRoll(state, roller, die);

        // Rule 2.4.4 - move to the Reserve Pool, keeping the same face up.
        foreach (var die in prepDice)
            die.Zone = Zone.ReservePool;
    }

    private static void ApplyRoll(GameState state, IDiceRoller roller, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
        var result = roller.Roll(die, card);
        die.Status = result.Status;
        die.Level = result.Level;
    }

    // Rule 2.6.7.1(3)/2.6.7.2 - the active player chooses to attack or not
    // at the end of the Main Step.
    public static void EnterAttackStep(GameState state)
    {
        if (state.CurrentStep != TurnStep.Main)
            throw new InvalidOperationException("Must be in the Main Step to enter the Attack Step.");
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
    }

    public static void SkipAttackStep(GameState state)
    {
        if (state.CurrentStep != TurnStep.Main)
            throw new InvalidOperationException("Must be in the Main Step to skip the Attack Step.");
        state.CurrentStep = TurnStep.CleanUp;
    }

    // Rule 2.8 - Clean Up Step, and the turn handoff described in 2.8.6.
    // NOTE: 2.8.2's ordered resolution of Applied-then-Persistent abilities
    // is not implemented here yet - it depends on AbilityQueue being wired
    // to real card triggers.
    public static void CleanUp(GameState state)
    {
        if (state.CurrentStep != TurnStep.CleanUp)
            throw new InvalidOperationException($"Expected CleanUp step, was {state.CurrentStep}.");

        var activeId = state.ActivePlayerId;

        // Rule 2.8.1 - clear damage on Character dice that weren't KO'd.
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.FieldZone))
            die.Damage = 0;

        // Rule 2.8.3 - Action dice left on their action face in the Reserve
        // Pool move to the Used Pile.
        foreach (var die in state.DiceIn(activeId, Zone.ReservePool).Where(d => d.Status == DieStatus.Action).ToList())
            die.Zone = Zone.UsedPile;

        // Rule 2.8.6 - Out of Play empties to the Used Pile and the turn passes.
        foreach (var die in state.DiceIn(activeId, Zone.OutOfPlay).ToList())
            die.Zone = Zone.UsedPile;

        // Unspent virtual generic energy does not carry over (rule 1.4.5/2.6.7.1(2)).
        state.GetPlayer(activeId).VirtualGenericEnergy = 0;

        state.IsFirstTurn = false;
        state.ActivePlayerId = state.OpponentOf(activeId);
        state.CurrentStep = TurnStep.ClearAndDraw;
        state.AttackSubStep = AttackSubStep.NotInAttack;
    }
}
