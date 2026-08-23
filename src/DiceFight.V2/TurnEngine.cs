using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// The turn step machine (V2_PLAN.md Phase 2 task 3, event-wired in Phase
// 4) - ClearAndDraw -> Roll -> FinishRoll -> Main (Purchase/Field/
// UseGlobal/UseAction) -> Attack (skippable) -> CleanUp. Ported from v1's
// TurnEngine.cs at the STRUCTURAL level only, per the plan's own
// instruction - v1's version is 1,769 lines almost entirely because of
// ability hooks (discounts, surcharges, keyword checks) this rewrite
// replaced with the query/event pipeline instead of re-porting one by one.
//
// Deliberate simplifications, all real gaps to close in later phases, not
// oversights:
//  - Purchase/Field route their cost lookups through QueryEngine (Phase
//    3), so discounts/surcharges apply automatically once Phase 6 starts
//    registering continuous modifiers - none do yet.
//  - Every action that has a real event now fires it and enqueues matches
//    (Phase 4) - Field/Purchase/ClearAndDraw/Roll/EnterAttackStep/CleanUp,
//    plus UseGlobal's own paid activation. Nothing DRAINS the queue here
//    (see AbilityQueue's own remarks on why that stays the caller's job),
//    and nothing can yet - Phase 5 is the effect interpreter that gives
//    `resolve` something real to do. DieKOd/DieDamaged/DieAttacks/
//    DieBlocks/DieUsed have no emission site yet because no KO/damage/
//    combat/Action-die mechanic exists to emit them from - they'll be
//    wired at the same time those mechanics are built (Phase 5 KO
//    effects, Phase 7 combat), not fabricated early.
//  - SpendEnergy doesn't implement partial-spend "spin down to the
//    unused value" (v1 rule 2.6.1.5/2.6.1.6) - an overspent die is simply
//    consumed whole.
//  - ClearAndDraw draws straight into PrepArea - the DiceFromBag/
//    DiceFromPrep staging zones stay declared (frozen Zone list) but
//    unused; the interrupt window they exist for needs a real mid-
//    resolution pause, which Phase 5's interpreter is what will produce.
//  - No Bag-refill-from-Used-Pile-when-empty (rule ~2.3's reshuffle) yet.
public static class TurnEngine
{
    // Rule 2.3.3 - the very first turn of the game draws one fewer die.
    // Takes its own Random (draw order) separately from IDiceRoller (face
    // results at the Roll step) - the caller controls both independently
    // for deterministic tests.
    // Opens the turn. The TURN SUMMARY's first entry is "any abilities
    // that take place at the start of your turn" - a peer window BEFORE
    // Clear and Draw, which Spike C models as its own step rather than as
    // a before/at modifier. So this begins on `start-of-turn`, fires that
    // window (nothing occupies it yet, but a Pepper-Potts-shaped card
    // addresses it by naming the id), then moves into `clear-and-draw`
    // proper. A dedicated step-machine `Advance` that walks these
    // automatically is Phase 9 API-shape work; until then the turn's
    // first two steps are entered by this one call.
    public static void ClearAndDraw(GameState state, AbilityQueue queue, Random random)
    {
        RequireStep(state, TurnStep.StartOfTurn);

        // Turn-scoped trackers reset as the new turn begins rather than
        // as the old one ends - see CleanUp's own remarks for the card
        // that forced this (an end-of-turn ability resolves after CleanUp
        // has returned, and must still be able to read the turn it is
        // ending).
        state.GlobalsUsedThisTurn.Clear();
        state.PurchasedThisTurn.Clear();
        state.FieldedCharacterThisTurn.Clear();
        state.CharacterDiceKOdThisTurn.Clear();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, state.ActivePlayerId, StepIds.StartOfTurn));

        state.MoveToStep(StepIds.ClearAndDraw);
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, state.ActivePlayerId, StepIds.ClearAndDraw));

        var drawCount = state.Config.Rules.DrawCount - (state.IsFirstTurn ? 1 : 0);
        var bag = state.DiceIn(state.ActivePlayerId, Zone.Bag).ToList();
        Shuffle(bag, random);

        var drawn = bag.Take(Math.Max(0, drawCount)).ToList();
        foreach (var die in drawn)
        {
            die.Zone = Zone.PrepArea;
        }

        if (drawn.Count > 0)
        {
            EventBus.Fire(state, queue, new GameEvent(TriggerKind.DiceDrawn, null, state.ActivePlayerId, state.CurrentStepId));
        }

        state.IsFirstTurn = false;
        state.MoveToStep(StepIds.RollAndReroll);
    }

    // First half of Roll & Reroll - assigns a face to every drawn die
    // without moving it yet, so a (future) caller can see results before
    // deciding what to reroll (v1's own reasoning for the Roll/FinishRoll
    // split - see the class remarks on why interactive rerolling isn't
    // wired up yet). Fires DieFaceChanged for every die rolled - this is
    // the one face-mutation site that exists so far; Reroll/Spin/
    // SpinToEnergy (Phase 5) and any future site MUST fire it too (Part
    // 1's own warning: a skipped site is the silently-never-fires bug
    // class v1's Awaken/Energize gate bug already taught this project).
    public static void Roll(GameState state, AbilityQueue queue, IDiceRoller roller)
    {
        RequireStep(state, TurnStep.RollAndReroll);

        foreach (var die in state.DiceIn(state.ActivePlayerId, Zone.PrepArea).ToList())
        {
            var definition = state.GetDieDefinition(die);
            var priorFace = state.GetCurrentFace(die); // null pre-roll - a dormant die has no "prior" face
            var newIndex = roller.Roll(definition);
            die.CurrentFaceIndex = newIndex;
            var newFace = definition.Faces[newIndex];

            if (priorFace is not null)
            {
                var payload = new DieFaceChangedPayload(priorFace, newFace, FaceChangeCause.Roll);
                EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFaceChanged, die, die.ControllerId, state.CurrentStepId, payload));
            }
            // A die rolling for the first time ever (priorFace null - e.g.
            // fresh off a card) has no meaningful "changed from" state to
            // report; Energize/Awaken both key off symbol count/level
            // INCREASE, which a null prior face can't express anyway, so
            // skipping emission here isn't the silently-never-fires bug
            // the class remarks warn about - there's nothing a filter
            // could legitimately match against a null PriorFace.
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

        state.MoveToStep(StepIds.Main);
    }

    // Rule 2.6.2 - Purchase Dice. Energy must type-match the card's own
    // EnergySymbolId (2.6.2.3); a wild symbol satisfies any requirement.
    // Default destination is the Used Pile (2.6.2.6) - no support yet for
    // an ability overriding that (e.g. v1's "goes to your bag instead").
    public static void Purchase(GameState state, AbilityQueue queue, string dieId, IReadOnlyList<string> energyDieIdsToSpend)
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

        // Phase 5's PurchaseModifier effect (Appendix A: GrantNextPurchase
        // Discount/GrantNextPurchaseGoesToBag) - a one-shot, per-controller
        // offer consumed by the first purchase matching its own CardKind
        // (null = matches any), on top of whatever QueryEngine's own
        // continuous registry already applies.
        var pending = state.PendingPurchaseModifiers.FirstOrDefault(m =>
            m.PlayerId == state.ActivePlayerId && (m.CardKind is null || m.CardKind == card.CardType));
        var cost = QueryEngine.GetPurchaseCost(state, card, state.ActivePlayerId);
        if (pending is not null) cost = Math.Max(1, cost + pending.Delta);
        SpendEnergy(state, energyDice, cost, card.EnergySymbolId);

        die.ControllerId = state.ActivePlayerId; // rule 1.1.4 - purchaser becomes controller
        die.Zone = pending?.GoesToZone ?? Zone.UsedPile;
        if (pending is not null) state.PendingPurchaseModifiers.Remove(pending);

        state.PurchasedThisTurn.Add(state.ActivePlayerId);
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.PurchaseMade, die, state.ActivePlayerId, state.CurrentStepId));
    }

    // Rule 2.6.3 - Field Character Dice. Fielding cost may be paid with
    // any energy type (2.6.3.2) - no type-matching requirement, unlike
    // Purchase. Sources only from the Reserve Pool, showing a character
    // face (a die must be rolled to be fielded).
    public static void Field(GameState state, AbilityQueue queue, string dieId, IReadOnlyList<string> energyDieIdsToSpend)
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
        state.FieldedCharacterThisTurn.Add(die.ControllerId);

        // Rule 2.6.3.6 - "when fielded" fires immediately upon entering
        // the Field Zone, which is why this die is already eligible to
        // react to its OWN fielding (Fire scans active dice, and it's
        // active as of the line above).
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFielded, die, die.ControllerId, state.CurrentStepId));
    }

    // Rule 2.6.5.2/2.6.5.4 - a paid Global ability. Addressed by CARD,
    // not by a die: a Global is usable by card ownership alone, with no
    // die of that card active anywhere (v1's UseGlobalAbility keys on
    // (cardId, playerId) for exactly this reason). And per the TURN
    // SUMMARY's Main Step - "Both players can use Global Abilities
    // (Inactive player after priority passes)" - `playerId` is whoever
    // is using it, NOT necessarily the active player.
    //
    // Corrected 2026-08-24: Phase 4 built this die-scoped, which made a
    // Global printed on a Basic Action card unusable (no such die is
    // ever fielded - Archnemesis DPS001) and locked the inactive player
    // out of every Global.
    //
    // abilityIndex selects among a card's own Global entries (a card
    // could print more than one); the caller is expected to know which
    // it is invoking.
    public static void UseGlobal(GameState state, AbilityQueue queue, string cardId, string playerId, int abilityIndex, IReadOnlyList<string> energyDieIdsToSpend)
    {
        if (state.CurrentStep is not (TurnStep.Main or TurnStep.Attack))
            throw new InvalidOperationException("Global abilities are usable during the Main Step or the Attack Step's action window.");

        // Catalog membership is the "is this card in this game" test,
        // matching v1, which checks the same thing and no more. The
        // stricter reading - the card must be on one of the two team
        // rosters - belongs at the API layer (Phase 9), where rosters
        // are authoritative and a catalog may legitimately be broader.
        if (!state.CardCatalog.TryGetValue(cardId, out var card))
            throw new InvalidOperationException($"Unknown card '{cardId}'.");
        if (abilityIndex < 0 || abilityIndex >= card.Abilities.Count || card.Abilities[abilityIndex].Trigger != TriggerKind.Global)
            throw new InvalidOperationException($"Card '{cardId}' has no Global ability at index {abilityIndex}.");

        var ability = card.Abilities[abilityIndex];

        if (ability.OncePerTurn && state.GlobalsUsedThisTurn.Contains((playerId, cardId)))
            throw new InvalidOperationException($"'{card.Name}''s Global has already been used this turn.");

        var energyDice = ResolveReservePoolEnergy(state, playerId, energyDieIdsToSpend);
        var cost = QueryEngine.GetGlobalEnergyCost(state, card, ability, playerId);
        // Rule 1.5.8.5 - the INACTIVE player's spent energy goes to the
        // Used Pile rather than Out of Play, since Out of Play is an
        // active-player-turn concept. Only reachable now that the
        // inactive player can use Globals at all.
        SpendEnergy(state, energyDice, cost, ability.EnergyCost?.RequiredSymbolId,
            playerId == state.ActivePlayerId ? Zone.OutOfPlay : Zone.UsedPile);

        if (ability.OncePerTurn) state.GlobalsUsedThisTurn.Add((playerId, cardId));

        // Rule 3.1.16 - Globals enqueue like any other triggered ability;
        // using one IS the trigger, so there's no event to fire. Source
        // die id is null: the ability belongs to the card, not to any
        // die, so there is no "self" to bind (see MayPay's own remarks on
        // its stand-in candidate for the card-scoped case).
        queue.Enqueue(null, playerId, TriggerKind.Global, ability.Effect);
    }

    // Rule 2.6.4.1 (Phase 8 - the first Basic Action cards migrated needed
    // a real way to be "used" at all). Default destination for a used die
    // is Out of Play; a card's own WhenUsed ability can move it elsewhere
    // afterward once the queue drains (Shocking Grasp's own "you may Prep
    // this die," V2_VOCABULARY.md's own MayPay motivating example - its
    // MoveDie(Self, PrepArea) runs after this returns and overrides the
    // default). Epic/Continuous Basic Action mechanics (rule 1.2.3,
    // 2.6.4.2 - a once-per-turn limiter, returning to the card instead of
    // Out of Play, a Continuous die fielded instead of resolved
    // immediately) are NOT modeled - CardType has no Epic/Continuous
    // distinction in the closed vocabulary; not exercised by anything
    // migrated so far (Cosmic Cube "Switch life totals," the one curated-
    // team Epic card, is already tailed for its own SwapLife gap).
    public static void UseAction(GameState state, AbilityQueue queue, string dieId)
    {
        if (state.CurrentStep is not (TurnStep.Main or TurnStep.Attack))
            throw new InvalidOperationException("Action dice are usable during the Main Step or the Attack Step's action window.");

        var die = FindDie(state, dieId);
        if (die.ControllerId != state.ActivePlayerId || die.Zone != Zone.ReservePool)
            throw new InvalidOperationException($"Die '{dieId}' must be your own Reserve Pool die to use as an Action die.");

        var cardId = die.CardId ?? throw new InvalidOperationException($"Die '{dieId}' has no card - only card dice can be used as Action dice.");
        var card = state.CardCatalog[cardId];
        if (card.CardType != CardType.BasicAction)
            throw new InvalidOperationException($"Card '{cardId}' is not a Basic Action card.");

        die.Zone = Zone.OutOfPlay;
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieUsed, die, die.ControllerId, state.CurrentStepId));
    }

    public static void EnterAttackStep(GameState state, AbilityQueue queue)
    {
        RequireStep(state, TurnStep.Main);
        state.MoveToStep(StepIds.SelectAttackers); // Spike C - the Attack phase's first step
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, state.ActivePlayerId, StepIds.SelectAttackers));
    }

    // A player may decline to attack at all, straight from the Main Step
    // or after entering the Attack Step and choosing no attackers.
    // Delegates to EnterAttackStep when coming from Main so the
    // TurnStepEntered(Attack) event fires exactly once either way, not a
    // separate "skip" event of its own - Combat itself is Phase 7.
    public static void SkipAttackStep(GameState state, AbilityQueue queue)
    {
        if (state.CurrentStep == TurnStep.Main)
        {
            EnterAttackStep(state, queue);
        }
        else if (state.CurrentStep != TurnStep.Attack)
        {
            throw new InvalidOperationException($"Cannot skip the Attack Step from {state.CurrentStep}.");
        }
    }

    // Reserve Pool dice (never spent) and Out of Play dice (spent energy)
    // sweep to the Used Pile; Field/Attack Zone dice remain in play.
    // AppliedModifiers expire per their Duration (Phase 3 - port of v1's
    // "AppliedModifiers cleared at Clean Up" fix, a real bug once when it
    // was missing). Then pass the turn and fire TurnStepEntered(ClearAndDraw)
    // for whoever's turn it now is.
    public static void CleanUp(GameState state, AbilityQueue queue)
    {
        RequireStep(state, TurnStep.Attack);

        var endingPlayerId = state.ActivePlayerId;

        // "At the end of your turn" abilities (Colossus "Piotr", DPS103).
        // Now addressable: Spike C gave EventFilter a Step discriminator,
        // so a listener can name `cleanup` specifically instead of
        // matching every TurnStepEntered. Fired FIRST, before any sweep
        // or expiry below, and attributed to the ENDING player so an
        // EventFilter{Ownership: Own, Step: cleanup} reads as "my turn is
        // ending".
        //
        // Caveat, documented rather than papered over: nothing drains the
        // queue here (AbilityQueue's enqueue/drain split - the caller
        // drains), so an end-of-turn ability actually RESOLVES after this
        // method has swept and passed the turn. Harmless for Colossus
        // (Field Zone dice, which its PerMatch counts, survive the sweep,
        // and "your opponent" resolves against the ability's own
        // controller). A future end-of-turn card reading Reserve Pool or
        // active-player state would NOT be safe - that needs an explicit
        // drain point here, which is a Phase 9 API-shape decision.
        state.MoveToStep(StepIds.CleanUp);
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, endingPlayerId, StepIds.CleanUp));

        foreach (var player in new[] { state.PlayerOne, state.PlayerTwo })
        {
            foreach (var die in state.DiceIn(player.Id, Zone.ReservePool).Concat(state.DiceIn(player.Id, Zone.OutOfPlay)).ToList())
            {
                die.Zone = Zone.UsedPile;
                die.CurrentFaceIndex = null;
                // Leaving active play (rule 3.4.5.4's own reasoning,
                // applied here same as EffectInterpreter.MoveToZone) - an
                // unblocked attacker sitting in Out of Play since combat
                // still needs this reset once it actually leaves for good.
                die.Damage = 0;
                die.GrantedTags.Clear();
                die.CombatFlags.Clear();
            }
        }

        // Rule 2.8.1 - clear damage on Character dice that were NOT KO'd
        // (a KO'd die already had its Damage reset when it left the Field/
        // Attack Zone - see EffectInterpreter.MoveToZone/Ko). Every
        // survivor still sitting in the Field Zone keeps its face/stats
        // but loses whatever damage combat marked on it this turn,
        // regardless of controller.
        foreach (var die in state.DiceIn(state.PlayerOne.Id, Zone.FieldZone).Concat(state.DiceIn(state.PlayerTwo.Id, Zone.FieldZone)))
            die.Damage = 0;

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

            // Phase 5's GrantedTag follows AppliedModifier's own Duration
            // expiry rule exactly (same three-value enum, same
            // GrantedDuringPlayerId convention). CombatFlags have no
            // Duration of their own - every real use is "(this turn)," so
            // they always clear here, unconditionally.
            die.GrantedTags.RemoveAll(t =>
                t.Duration == Duration.EndOfTurn ||
                (t.Duration == Duration.UntilYourNextTurn && t.GrantedDuringPlayerId != endingPlayerId));
            die.CombatFlags.Clear();
        }

        // Turn-scoped TRACKERS are deliberately NOT reset here - they are
        // reset at the start of the next turn instead (ClearAndDraw).
        // Found via Jean Grey "Peaceful Coexistence" (DPS035), whose
        // "if no character dice were KO'd that turn" condition reads
        // exactly this state: an end-of-turn ability only RESOLVES after
        // CleanUp returns (the caller drains the queue), so clearing here
        // meant she always saw an empty KO set and always scored her
        // Loyalty Counter. Resetting at turn start instead leaves the
        // just-ended turn's facts readable for precisely as long as
        // end-of-turn abilities need them, and is equivalent for every
        // within-turn reader.
        //
        // Grants still expire HERE, not at turn start - the rulebook's
        // Cleanup Step is "end all effects", and a "this turn" grant must
        // not survive into the opponent's turn.
        state.PendingPurchaseModifiers.RemoveAll(m => m.PlayerId == endingPlayerId);
        state.ActivePlayerId = state.OpponentOf(state.ActivePlayerId);
        state.MoveToStep(StepIds.StartOfTurn);

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, state.ActivePlayerId, StepIds.StartOfTurn));
    }

    private static void SpendEnergy(GameState state, IReadOnlyList<DieInstance> energyDice, int amountNeeded, string? requiredSymbolId, Zone spentZone = Zone.OutOfPlay)
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
            die.Zone = spentZone; // face index left intact - see class remarks
        }
    }

    private static List<DieInstance> ResolveOwnReservePoolEnergy(GameState state, IReadOnlyList<string> dieIds) =>
        ResolveReservePoolEnergy(state, state.ActivePlayerId, dieIds);

    // Player-parameterised because Globals can be paid for by either
    // player (rule 2.6.5.2), unlike purchasing/fielding/Action dice,
    // which are active-player-only.
    private static List<DieInstance> ResolveReservePoolEnergy(GameState state, string playerId, IReadOnlyList<string> dieIds)
    {
        var dice = new List<DieInstance>();
        foreach (var id in dieIds)
        {
            var die = FindDie(state, id);
            if (die.ControllerId != playerId || die.Zone != Zone.ReservePool)
                throw new InvalidOperationException($"Die '{id}' is not {playerId}'s own Reserve Pool energy.");
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
