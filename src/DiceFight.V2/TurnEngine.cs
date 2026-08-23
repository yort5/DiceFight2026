using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// The turn step machine (V2_PLAN.md Phase 2 task 3) - ClearAndDraw -> Roll
// -> FinishRoll -> Main (Purchase/Field/UseGlobal/UseAction) -> Attack
// (skippable) -> CleanUp. Ported from v1's TurnEngine.cs at the STRUCTURAL
// level only, per the plan's own instruction - v1's version is 1,769 lines
// almost entirely because of ability hooks (discounts, surcharges,
// triggered reactions, keyword checks) that don't exist yet; this is the
// bare rules skeleton those hooks will attach to in Phases 3-7.
//
// Deliberate simplifications, all real gaps to close in later phases, not
// oversights:
//  - Purchase/Field now route their cost lookups through QueryEngine
//    (Phase 3 task 3), so discounts/surcharges apply automatically once
//    Phase 6 starts registering continuous modifiers - but nothing
//    populates those registries yet, so today's effective costs still
//    equal the printed ones.
//  - No "when fielded"/Global/Action triggers fire - Phase 4 (events) and
//    Phase 5/6 (effect/continuous interpreters).
//  - SpendEnergy doesn't implement partial-spend "spin down to the
//    unused value" (v1 rule 2.6.1.5/2.6.1.6) - an overspent die is simply
//    consumed whole. Real, but not needed for a skeleton turn cycle to
//    work; revisit if/when it blocks something.
//  - ClearAndDraw draws straight into PrepArea - the DiceFromBag/
//    DiceFromPrep staging zones stay declared (frozen Zone list) but
//    unused, since the interrupt window they exist for needs an ability
//    layer that isn't built until Phase 4+.
//  - No Bag-refill-from-Used-Pile-when-empty (rule ~2.3's reshuffle) yet.
public static class TurnEngine
{
    // Rule 2.3.3 - the very first turn of the game draws one fewer die.
    // Takes its own Random (draw order) separately from IDiceRoller (face
    // results at the Roll step) - the caller controls both independently
    // for deterministic tests.
    public static void ClearAndDraw(GameState state, Random random)
    {
        RequireStep(state, TurnStep.ClearAndDraw);

        var drawCount = state.Config.Rules.DrawCount - (state.IsFirstTurn ? 1 : 0);
        var bag = state.DiceIn(state.ActivePlayerId, Zone.Bag).ToList();
        Shuffle(bag, random);

        foreach (var die in bag.Take(Math.Max(0, drawCount)))
        {
            die.Zone = Zone.PrepArea;
        }

        state.IsFirstTurn = false;
        state.CurrentStep = TurnStep.RollAndReroll;
    }

    // First half of Roll & Reroll - assigns a face to every drawn die
    // without moving it yet, so a (future) caller can see results before
    // deciding what to reroll (v1's own reasoning for the Roll/FinishRoll
    // split - see the class remarks on why that's not wired up yet).
    public static void Roll(GameState state, IDiceRoller roller)
    {
        RequireStep(state, TurnStep.RollAndReroll);

        foreach (var die in state.DiceIn(state.ActivePlayerId, Zone.PrepArea))
        {
            var definition = state.GetDieDefinition(die);
            die.CurrentFaceIndex = roller.Roll(definition);
        }
    }

    // Second half - commits every rolled Prep Area die to the Reserve Pool.
    public static void FinishRoll(GameState state)
    {
        RequireStep(state, TurnStep.RollAndReroll);

        foreach (var die in state.DiceIn(state.ActivePlayerId, Zone.PrepArea).ToList())
        {
            die.Zone = Zone.ReservePool;
        }

        state.CurrentStep = TurnStep.Main;
    }

    // Rule 2.6.2 - Purchase Dice. Energy must type-match the card's own
    // EnergySymbolId (2.6.2.3); a wild symbol satisfies any requirement.
    // Default destination is the Used Pile (2.6.2.6) - no support yet for
    // an ability overriding that (e.g. v1's "goes to your bag instead").
    public static void Purchase(GameState state, string dieId, IReadOnlyList<string> energyDieIdsToSpend)
    {
        RequireStep(state, TurnStep.Main);

        var die = FindDie(state, dieId);
        if (die.Zone != Zone.Unpurchased)
            throw new InvalidOperationException($"Die '{dieId}' is not available to purchase.");

        var card = state.CardCatalog[die.CardId!];

        // Rule 2.6.2.1/2.1.2 - Basic Action cards are community property.
        var isCommunity = card.CardType == CardType.BasicAction;
        if (!isCommunity && die.OwnerId != state.ActivePlayerId)
            throw new InvalidOperationException($"Die '{dieId}' belongs to your opponent's team and isn't a Basic Action.");

        var energyDice = ResolveOwnReservePoolEnergy(state, energyDieIdsToSpend);
        var cost = QueryEngine.GetPurchaseCost(state, card, state.ActivePlayerId);
        SpendEnergy(state, energyDice, cost, card.EnergySymbolId);

        die.ControllerId = state.ActivePlayerId; // rule 1.1.4 - purchaser becomes controller
        die.Zone = Zone.UsedPile;
    }

    // Rule 2.6.3 - Field Character Dice. Fielding cost may be paid with
    // any energy type (2.6.3.2) - no type-matching requirement, unlike
    // Purchase. Sources only from the Reserve Pool, showing a character
    // face (a die must be rolled to be fielded).
    public static void Field(GameState state, string dieId, IReadOnlyList<string> energyDieIdsToSpend)
    {
        RequireStep(state, TurnStep.Main);

        var die = FindDie(state, dieId);
        if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.ReservePool)
            throw new InvalidOperationException($"Die '{dieId}' cannot be fielded from its current state.");

        var face = state.GetCurrentFace(die);
        if (face?.Character is null)
            throw new InvalidOperationException($"Die '{dieId}' is not on a character face.");

        var energyDice = ResolveOwnReservePoolEnergy(state, energyDieIdsToSpend);
        var cost = QueryEngine.GetFieldingCost(state, die);
        SpendEnergy(state, energyDice, cost, requiredSymbolId: null);

        die.Zone = Zone.FieldZone;
    }

    // Phase 4+ territory (event bus + triggered abilities / paid Global
    // activation) - stubs so callers get a clear, honest failure instead
    // of silently doing nothing.
    public static void UseGlobal(GameState state, string dieId, IReadOnlyList<string> energyDieIdsToSpend) =>
        throw new NotImplementedException("Global abilities require the event bus and effect interpreter (Phase 4+).");

    public static void UseAction(GameState state, string dieId) =>
        throw new NotImplementedException("Action dice require the effect interpreter (Phase 5+).");

    public static void EnterAttackStep(GameState state)
    {
        RequireStep(state, TurnStep.Main);
        state.CurrentStep = TurnStep.Attack;
    }

    // A player may decline to attack at all, straight from the Main Step
    // or after entering the Attack Step and choosing no attackers - either
    // way this is the only Attack-Step action Phase 2 needs (Combat itself
    // is Phase 7).
    public static void SkipAttackStep(GameState state)
    {
        if (state.CurrentStep is not (TurnStep.Main or TurnStep.Attack))
            throw new InvalidOperationException($"Cannot skip the Attack Step from {state.CurrentStep}.");
        state.CurrentStep = TurnStep.Attack;
    }

    // Reserve Pool dice (never spent) and Out of Play dice (spent energy)
    // sweep to the Used Pile; Field/Attack Zone dice remain in play.
    // AppliedModifiers expire per their Duration (Phase 3 task 2 - port of
    // v1's "AppliedModifiers cleared at Clean Up" fix, a real bug once
    // when it was missing). Then pass the turn.
    public static void CleanUp(GameState state)
    {
        RequireStep(state, TurnStep.Attack);

        var endingPlayerId = state.ActivePlayerId;

        foreach (var player in new[] { state.PlayerOne, state.PlayerTwo })
        {
            foreach (var die in state.DiceIn(player.Id, Zone.ReservePool).Concat(state.DiceIn(player.Id, Zone.OutOfPlay)).ToList())
            {
                die.Zone = Zone.UsedPile;
                die.CurrentFaceIndex = null;
            }
        }

        foreach (var die in state.Dice)
        {
            // EndOfTurn always expires here. UntilYourNextTurn survives one
            // MORE Clean Up (the granter's opponent's turn) and expires at
            // the Clean Up that hands control back to the granter - i.e.
            // exactly "gone by the start of your next turn" (see
            // V2_VOCABULARY.md Part 1's Duration note). Permanent never
            // expires on its own.
            die.AppliedModifiers.RemoveAll(m =>
                m.Duration == Duration.EndOfTurn ||
                (m.Duration == Duration.UntilYourNextTurn && m.GrantedDuringPlayerId != endingPlayerId));
        }

        state.ActivePlayerId = state.OpponentOf(state.ActivePlayerId);
        state.CurrentStep = TurnStep.ClearAndDraw;
    }

    private static void SpendEnergy(GameState state, IReadOnlyList<DieInstance> energyDice, int amountNeeded, string? requiredSymbolId)
    {
        var total = 0;
        // amountNeeded == 0 bypasses the type-matching requirement too -
        // a free purchase/field has nothing for the type check to apply
        // to. Caught while designing the Phase 2 test, but a real latent
        // bug regardless (a future Phase 3+ discount can legitimately
        // bring a cost to 0).
        var hasRequiredSymbol = requiredSymbolId is null || amountNeeded == 0;
        var wildIds = new HashSet<string>(state.Config.EnergySymbols.Where(s => s.IsWild).Select(s => s.Id));

        foreach (var die in energyDice)
        {
            var face = state.GetCurrentFace(die) ?? throw new InvalidOperationException($"Die '{die.Id}' is not showing a face.");
            total += face.Symbols.Sum(s => s.Count);

            if (requiredSymbolId is not null && face.Symbols.Any(s => s.SymbolId == requiredSymbolId || wildIds.Contains(s.SymbolId)))
                hasRequiredSymbol = true;
        }

        if (total < amountNeeded)
            throw new InvalidOperationException($"Not enough energy offered (needs {amountNeeded}, offered {total}).");
        if (!hasRequiredSymbol)
            throw new InvalidOperationException($"Offered energy doesn't include the required symbol '{requiredSymbolId}'.");

        foreach (var die in energyDice)
        {
            die.Zone = Zone.OutOfPlay; // face index left intact - see class remarks
        }
    }

    private static List<DieInstance> ResolveOwnReservePoolEnergy(GameState state, IReadOnlyList<string> dieIds)
    {
        var dice = new List<DieInstance>();
        foreach (var id in dieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.ReservePool)
                throw new InvalidOperationException($"Die '{id}' is not your own Reserve Pool energy.");
            dice.Add(die);
        }
        return dice;
    }

    private static DieInstance FindDie(GameState state, string dieId) =>
        state.Dice.FirstOrDefault(d => d.Id == dieId)
        ?? throw new InvalidOperationException($"No die with id '{dieId}'.");

    private static void RequireStep(GameState state, TurnStep expected)
    {
        if (state.CurrentStep != expected)
            throw new InvalidOperationException($"This action requires the {expected} step (currently {state.CurrentStep}).");
    }

    private static void Shuffle(List<DieInstance> dice, Random random)
    {
        for (var i = dice.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (dice[i], dice[j]) = (dice[j], dice[i]);
        }
    }
}
