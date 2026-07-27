using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;

namespace DiceFight.Engine;

// The result of rolling a single die (rule 1.6 - Rolled vs. Unrolled Dice).
// NOTE: real Dice Masters dice have a fixed, card-specific face layout
// (typically a mix of energy and character faces) that isn't captured by
// CardDef yet - that's physical-component data, not rules logic, and is
// tracked as a follow-up. IDiceRoller exists so the turn engine's zone/step
// mechanics can be built and tested now, independent of where face results
// come from (a real weighted roll, a human reporting a physical die, etc.).
// EnergyKind/ProvidedEnergyType only matter when Status is Energy - the
// roller decides them as part of the face itself (which specific energy
// face was rolled is a physical fact about the die, same as Status/Level),
// rather than TurnEngine inferring them from the die's card afterward.
public readonly record struct RolledFace(
    DieStatus Status, int Level, EnergyKind EnergyKind = EnergyKind.None, EnergyType? ProvidedEnergyType = null);

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

        // Carry over anything a KO or a Prep effect left sitting in the
        // Prep Area since this player's last Roll & Reroll - it rolls
        // alongside this turn's fresh draw, but is kept in its own zone
        // (rather than merged into the same PrepArea the draw uses) so
        // that a die a card Preps *later in this same Clear and Draw step*
        // (see remarks on the Zone enum) is left alone: it lands in
        // PrepArea after this sweep already ran, so it won't be picked up
        // until this same sweep runs again next turn.
        foreach (var die in state.DiceIn(activeId, Zone.PrepArea).ToList())
            die.Zone = Zone.DiceFromPrep;

        // Rule 2.3.3 - the very first turn of the game draws 3, not 4, with
        // the 4th die drawn and set Out of Play instead of into Prep Area.
        var drawCount = state.IsFirstTurn ? 3 : 4;
        var drawn = DrawFromBag(state, activeId, drawCount, random);

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
            picked.Zone = Zone.DiceFromBag; // out of the Bag immediately, so it isn't drawn twice this loop
            drawn.Add(picked);
        }

        return drawn;
    }

    // Rule 2.4 - Roll and Reroll Step, split into two calls so a player can
    // see this turn's roll (rule 2.4.1/2.4.2) before deciding what to
    // reroll (rule 2.4.3 - any/all/none, as a single group) rather than
    // having to commit to a reroll set blind, before the roll happens.
    // Combines this turn's fresh draw (DiceFromBag) with whatever Clear and
    // Draw carried over from the Prep Area (DiceFromPrep). Deliberately
    // does NOT read Zone.PrepArea itself - anything sitting there was
    // either never swept (a die Prepped after this step's Clear phase
    // already ran) or belongs to a future Roll & Reroll, not this one.
    // Rolled dice land straight in the Reserve Pool (rule 2.4.2) - the
    // reroll decision that follows (Reroll, below) acts on them there,
    // there's no separate "rolled but not yet placed" holding zone.
    public static void Roll(GameState state, IDiceRoller roller)
    {
        if (state.CurrentStep != TurnStep.RollAndReroll)
            throw new InvalidOperationException($"Expected RollAndReroll step, was {state.CurrentStep}.");

        var dice = state.DiceIn(state.ActivePlayerId, Zone.DiceFromBag)
            .Concat(state.DiceIn(state.ActivePlayerId, Zone.DiceFromPrep))
            .ToList();

        foreach (var die in dice)
            ApplyRoll(state, roller, die);

        foreach (var die in dice)
            die.Zone = Zone.ReservePool;
    }

    // rerollDieIds is the player's rule-2.4.3 decision, made after seeing
    // Roll's results (any/some/none of them) - purely optional, since Roll
    // already placed everyone in the Reserve Pool. Anything in the active
    // player's Reserve Pool right now is this turn's roll: Clear and Draw
    // always empties it first (rule 2.3.1), and nothing else can add to it
    // before this step ends.
    public static void Reroll(GameState state, IDiceRoller roller, IReadOnlyList<string> rerollDieIds)
    {
        if (state.CurrentStep != TurnStep.RollAndReroll)
            throw new InvalidOperationException($"Expected RollAndReroll step, was {state.CurrentStep}.");

        var rerollSet = rerollDieIds.ToHashSet();
        foreach (var die in state.DiceIn(state.ActivePlayerId, Zone.ReservePool).Where(d => rerollSet.Contains(d.Id)))
            ApplyRoll(state, roller, die);
    }

    private static void ApplyRoll(GameState state, IDiceRoller roller, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
        var result = roller.Roll(die, card);
        die.Status = result.Status;
        die.Level = result.Level;

        if (die.Status != DieStatus.Energy)
        {
            die.EnergyKind = EnergyKind.None;
            die.ProvidedEnergyType = null;
            return;
        }

        // Rule 1.3.10/1.4.2 - what an energy face provides is a property of
        // which specific face got rolled, decided by the roller (see
        // RolledFace remarks), not inferred here from the die's card.
        die.EnergyKind = result.EnergyKind;
        die.ProvidedEnergyType = result.ProvidedEnergyType;
    }

    // Rule 2.6.2 - Purchase Dice, one of the four Main Step game actions.
    // Unlike fielding, purchasing requires at least one spent energy die
    // per distinct type the card requires (2.6.2.3) - Wild energy (one of a
    // Sidekick die's five energy faces - the other four are Fist/Bolt/
    // Mask/Shield, same as any specific-type energy) satisfies any type;
    // Generic energy (Basic Action dice) never does. Always purchases for
    // the Active player - the Inactive player's only Main Step actions are
    // Global abilities (rule 2.6.6.3).
    public static void Purchase(GameState state, string dieId, IReadOnlyList<string> energyDieIdsToSpend)
    {
        if (state.CurrentStep != TurnStep.Main)
            throw new InvalidOperationException("Purchasing requires the Main Step.");

        var die = FindDie(state, dieId);
        if (die.Zone != Zone.Unpurchased)
            throw new InvalidOperationException($"Die {dieId} is not available to purchase.");

        var card = state.CardCatalog[die.CardId!];

        // Rule 2.6.2.1/2.1.2 - Basic Action cards are community property,
        // purchasable by either player regardless of who brought them.
        // Rule 2.6.2.2 - everything else can only be purchased from your
        // own team's cards.
        var isCommunity = card.Type is CardType.BasicAction or CardType.EpicBasicAction;
        if (!isCommunity && die.OwnerId != state.ActivePlayerId)
            throw new InvalidOperationException("You may not purchase dice from your opponent's cards.");

        if (card.Type == CardType.EpicBasicAction)
        {
            // Rule 1.2.3(4) - requires an active Character die costing 4+.
            var hasQualifyingActive = state.DiceIn(state.ActivePlayerId, Zone.FieldZone).Any(d =>
                (d.VirtualCardId ?? d.CardId) is { } activeCardId &&
                state.CardCatalog.TryGetValue(activeCardId, out var activeCard) &&
                activeCard.Type == CardType.Character &&
                activeCard.PurchaseCost >= 4);
            if (!hasQualifyingActive)
            {
                throw new InvalidOperationException(
                    "Purchasing an Epic Basic Action die requires an active Character die with purchase cost 4 or greater.");
            }
        }

        var energyDice = energyDieIdsToSpend.Select(id => FindEnergyDie(state, id)).ToList();
        if (energyDice.Count < card.PurchaseCost)
            throw new InvalidOperationException($"Not enough energy offered to purchase {dieId} (needs {card.PurchaseCost}).");

        var spent = energyDice.Take(card.PurchaseCost).ToList();
        var unclaimed = new List<DieInstance>(spent);
        foreach (var requiredType in card.EnergyTypes.Distinct())
        {
            var match = unclaimed.FirstOrDefault(e =>
                e.EnergyKind == EnergyKind.Wild ||
                (e.EnergyKind == EnergyKind.Specific && e.ProvidedEnergyType == requiredType));
            if (match is null)
                throw new InvalidOperationException($"Purchasing {dieId} requires at least one {requiredType} energy.");
            unclaimed.Remove(match); // rule 2.6.2.3's example - one energy per required type, not reused
        }

        foreach (var energyDie in spent)
            energyDie.Zone = Zone.OutOfPlay; // rule 2.6.2.6

        // Rule 1.1.4 - the purchaser becomes controller; OwnerId (whoever
        // brought the card) is untouched, which matters for community
        // Basic Actions bought by the non-bringing player.
        die.ControllerId = state.ActivePlayerId;
        die.Zone = Zone.UsedPile; // rule 2.6.2.6
    }

    // Rule 2.6.3 - Field Character Dice, one of the four Main Step game
    // actions. Fielding cost may be paid with any type of energy (2.6.3.2),
    // so energyDieIdsToSpend just needs to be Reserve Pool energy dice
    // totalling at least the fielding cost - no type-matching requirement
    // (that only applies to Purchase Dice, rule 2.6.2.3 - see Purchase below).
    public static void Field(GameState state, AbilityQueue queue, string dieId, IReadOnlyList<string> energyDieIdsToSpend)
    {
        if (state.CurrentStep != TurnStep.Main)
            throw new InvalidOperationException("Fielding requires the Main Step.");

        var die = FindDie(state, dieId);
        if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.ReservePool)
            throw new InvalidOperationException($"Die {dieId} cannot be fielded from its current state.");
        if (die.Status is not (DieStatus.Character or DieStatus.SidekickCharacter))
            throw new InvalidOperationException($"Die {dieId} is not on a character face.");

        var fieldingCost = DieStats.GetFace(state, die).FieldingCost;
        var energyDice = energyDieIdsToSpend.Select(id => FindEnergyDie(state, id)).ToList();
        if (energyDice.Count < fieldingCost)
            throw new InvalidOperationException($"Not enough energy offered to field {dieId} (needs {fieldingCost}).");

        foreach (var energyDie in energyDice.Take(fieldingCost))
            energyDie.Zone = Zone.OutOfPlay; // rule 2.6.3.2

        die.Zone = Zone.FieldZone;

        // Rule 2.6.3.6 - "when fielded" fires immediately upon entering the Field Zone.
        EnqueueTriggered(state, queue, die, TriggerType.WhenFielded);
    }

    // Rule 2.6.4 - Use Action Dice Abilities, one of the four Main Step
    // game actions (also usable in the Attack Step's Action/Global window,
    // rule 2.7.3.1). Only the Active player may use Action dice (2.6.4.1).
    public static void UseActionDie(GameState state, AbilityQueue queue, string dieId)
    {
        if (!InMainOrAttackActionWindow(state))
            throw new InvalidOperationException("Action dice can only be used during the Main Step or the Attack Step's Action/Global window.");

        var die = FindDie(state, dieId);
        if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.ReservePool || die.Status != DieStatus.Action)
            throw new InvalidOperationException($"Die {dieId} is not an eligible Action die.");

        // Rule 2.6.4.1 - the ability is initiated (queued) before the die's
        // post-use zone move; Shocking Grasp's own "you may Prep this die"
        // effect, for example, overrides the default destination below.
        EnqueueTriggered(state, queue, die, TriggerType.WhenUsed);

        var cardId = die.VirtualCardId ?? die.CardId;
        var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
        if (card?.Type == CardType.EpicBasicAction)
        {
            // Rule 1.2.3(2)/(3) - returns to its card instead of Out of
            // Play, and only one Epic Basic Action die may be used per turn.
            if (state.EpicBasicActionUsedThisTurn)
                throw new InvalidOperationException("Only one Epic Basic Action die may be used per turn.");
            state.EpicBasicActionUsedThisTurn = true;
            die.Zone = Zone.Unpurchased;
        }
        else
        {
            die.Zone = Zone.OutOfPlay; // rule 2.6.4.1
        }
    }

    // Rule 2.6.5 - Use Global Abilities. Available to either player during
    // the Main Step or the Attack Step's Action/Global window (2.6.5.9),
    // regardless of who owns the card the ability is printed on (2.6.5.2) -
    // cardId is looked up in the shared catalog, not tied to a specific die.
    public static void UseGlobalAbility(
        GameState state, AbilityQueue queue, string cardId, string playerId, IReadOnlyList<string> energyDieIdsToSpend)
    {
        if (!InMainOrAttackActionWindow(state))
            throw new InvalidOperationException("Global abilities can only be used during the Main Step or the Attack Step's Action/Global window.");

        if (!state.CardCatalog.TryGetValue(cardId, out var card))
            throw new InvalidOperationException($"Unknown card '{cardId}'.");
        var ability = card.Abilities.FirstOrDefault(a => a.Trigger == TriggerType.Global)
            ?? throw new InvalidOperationException($"Card '{cardId}' has no Global ability.");
        var cost = ability.EnergyCost
            ?? throw new InvalidOperationException($"Card '{cardId}''s Global ability has no defined energy cost.");

        var energyDice = energyDieIdsToSpend.Select(id => FindPlayerEnergyDie(state, playerId, id)).ToList();
        if (energyDice.Count < cost.Amount)
            throw new InvalidOperationException($"Not enough energy offered to pay for {cardId}'s Global ability (needs {cost.Amount}).");

        var spent = energyDice.Take(cost.Amount).ToList();
        if (cost.RequiredType is { } requiredType &&
            !spent.Any(e => e.EnergyKind == EnergyKind.Wild || (e.EnergyKind == EnergyKind.Specific && e.ProvidedEnergyType == requiredType)))
        {
            throw new InvalidOperationException($"{cardId}'s Global ability requires at least one {requiredType} energy.");
        }

        // Rule 2.6.1.1/2.6.1.2 - the Active player's spent energy goes Out
        // of Play; the Inactive player's goes straight to the Used Pile,
        // since Out of Play doesn't exist on their turn (rule 1.5.8.5).
        var destination = playerId == state.ActivePlayerId ? Zone.OutOfPlay : Zone.UsedPile;
        foreach (var energyDie in spent)
            energyDie.Zone = destination;

        // Rule 3.1.5 - the source of a non-damage Global ability is the
        // player who paid for it, not a specific die.
        queue.Enqueue(sourceDieId: null, playerId, TriggerType.Global, ability.Effect);
    }

    private static bool InMainOrAttackActionWindow(GameState state) =>
        state.CurrentStep == TurnStep.Main ||
        (state.CurrentStep == TurnStep.Attack && state.AttackSubStep == AttackSubStep.ActionAndGlobalWindow);

    private static DieInstance FindEnergyDie(GameState state, string id) => FindPlayerEnergyDie(state, state.ActivePlayerId, id);

    private static DieInstance FindPlayerEnergyDie(GameState state, string playerId, string id)
    {
        var die = FindDie(state, id);
        if (die.ControllerId != playerId || die.Zone != Zone.ReservePool || die.Status != DieStatus.Energy)
            throw new InvalidOperationException($"Die {id} is not available energy in the Reserve Pool for player {playerId}.");
        return die;
    }

    private static DieInstance FindDie(GameState state, string id) =>
        state.Dice.SingleOrDefault(d => d.Id == id)
        ?? throw new InvalidOperationException($"No die with id '{id}'.");

    // Shared by TurnEngine.Field and CombatEngine - enqueues every ability
    // on a die's card matching the given trigger. Internal so CombatEngine
    // (same assembly) can reuse it for WhenAttacks/WhenKOd.
    internal static void EnqueueTriggered(GameState state, AbilityQueue queue, DieInstance die, TriggerType trigger)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        if (cardId is null || !state.CardCatalog.TryGetValue(cardId, out var card)) return;

        foreach (var ability in card.Abilities.Where(a => a.Trigger == trigger))
            queue.Enqueue(die.Id, die.ControllerId, trigger, ability.Effect);
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

        // Rule 1.2.3(3) - the once-per-turn Epic Basic Action limit resets.
        state.EpicBasicActionUsedThisTurn = false;

        state.IsFirstTurn = false;
        state.ActivePlayerId = state.OpponentOf(activeId);
        state.CurrentStep = TurnStep.ClearAndDraw;
        state.AttackSubStep = AttackSubStep.NotInAttack;
    }
}
