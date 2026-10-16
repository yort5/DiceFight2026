using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Bot;

// The Dice Kingdom computer opponent - ONE policy shared by the web
// client's "vs computer" seat (GET /api/v2/games/{id}/bot-decision) and
// tools/Simulator (user call, 2026-09-28: the two had drifted into two
// different bots, so the simulator's balance numbers didn't describe the
// opponent people actually play). Rule-based, no lookahead: each call
// answers "what is the single next thing I should do" from what's
// visible on GameState, the same information a human player sees
// (it never peeks at the opponent's Bag order or future rolls).
//
// What it does, step by step (see v3/DESIGN_NOTES.md, 2026-09-28, for the
// simulator findings behind each choice):
//   - Roll & Reroll: rerolls a Character die that rolled energy (a body
//     beats 1-2 energy), and fishes for a Wild with 1-pip Tardigrades when
//     a card it wants needs an energy type it can't otherwise pay.
//   - Main: Field real Characters first (unfielded ones go to the Used
//     Pile at the end of Main anyway), then buy the best-VALUE plan of up
//     to three cards the energy affords - not simply the cheapest or the
//     priciest; extra copies of a card are worth progressively less, and
//     an opponent's Basic Action is worth more when its energy type pays
//     for an off-type card on this team. Only THEN field leftover
//     Tardigrades - fielding one first burns the energy it carries, which
//     is what kept the old simulator bot from ever buying a finisher.
//   - Uses Basic Actions and Globals (Anger Issues on an unblocked
//     attacker, Distraction's Global against the biggest unblocked
//     attacker, Resurrection's draw with spare Wing energy, ...).
//   - Attack: never swings a 0-ATK die, never swings into a blocker that
//     kills it for free (an unblocked attacker leaves play - rule
//     2.7.4.3.1 - so a swing has a real cost), keeps enough blockers home
//     to survive the crack-back, and goes all-in when the damage is lethal.
//   - Block: chump-blocks only to survive or when life is low, otherwise
//     takes good trades and free walls.
//   - Pending choices: harmful effects aim at the opponent's best die (or
//     this player's least valuable one when the choice is forced),
//     helpful ones at its own best (PendingChoice.Intent).
public static class DiceKingdomBot
{

    private const int MaxLanes = 4;

    // `skip`: dice the caller already saw rejected this turn (a legality
    // rule this policy doesn't model) - never offered again. Null when
    // it isn't this player's decision.
    public static BotDecision? Decide(GameState state, string botId, IReadOnlySet<string>? skip = null, BotPersona? persona = null)
    {
        if (state.IsGameOver) return null;
        skip ??= new HashSet<string>();
        persona ??= BotPersona.ForChampion(state.GetPlayer(botId).ChampionId);
        var active = state.ActivePlayerId;

        if (state.PendingChoice is { } pending)
            return pending.ControllerId == botId ? AnswerChoice(state, botId, pending) : null;

        if (Priority.IsWindow(state))
        {
            var holder = state.PriorityPlayerId ?? active; // Priority.Sync opens every window with the Active player
            if (holder != botId) return null;
            if (state.CurrentStepId == StepIds.Main)
                return botId == active ? ActiveMain(state, botId, skip, persona) : InactiveMain(state, botId);
            return botId == active ? ActiveWindow(state, botId) : InactiveWindow(state, botId);
        }

        switch (state.CurrentStep)
        {
            case TurnStep.StartOfTurn or TurnStep.ClearAndDraw when botId == active:
                return new(BotActionKind.ClearAndDraw, "Start of turn.");
            case TurnStep.RollAndReroll when botId == active:
                return state.DiceIn(botId, Zone.DiceFromBag).Any() || state.DiceIn(botId, Zone.DiceFromPrep).Any()
                    ? new(BotActionKind.Roll, "Roll the drawn dice.")
                    : DecideReroll(state, botId, persona);
        }

        return state.CurrentStepId switch
        {
            StepIds.SelectAttackers when botId == active => DecideAttackers(state, botId, persona),
            StepIds.AssignBlockers when botId == state.OpponentOf(active) => DecideBlockers(state, botId, persona),
            StepIds.ReturnToField when botId == active => new(BotActionKind.CleanUp, "End of turn."),
            _ => null,
        };
    }

    // ------------------------------------------------------------------
    // Roll & Reroll
    // ------------------------------------------------------------------

    private static BotDecision DecideReroll(GameState state, string botId, BotPersona persona)
    {
        var reserve = state.DiceIn(botId, Zone.ReservePool).Where(d => !state.RerolledThisStep.Contains(d.Id)).ToList();
        var picks = new List<string>();
        var rushing = Rushing(state, botId, persona);

        // A purchased Character that rolled energy: 3 of its 6 faces are
        // a body, and a body is why it was bought (user call 2026-09-28:
        // "at the very least, prefer stat faces to energy"). Not while
        // racing to a rush card (BotPersona.RushCardId) - then energy is
        // the point, and a zero-energy Bulwark Tardigrade gets rerolled
        // for some instead.
        if (!rushing)
        {
            foreach (var die in reserve.Where(d => IsCharacterCard(state, d) && state.GetCurrentFace(d)?.Character is null))
                picks.Add(die.Id);
        }
        else
        {
            foreach (var die in reserve.Where(d => d.CardId is null && BotEnergy.Pips(state, d) == 0))
                picks.Add(die.Id);
        }

        // Fish for a Wild (two Tardigrade faces in 6) when a card
        // worth buying needs an energy type nothing in the pool can pay -
        // but only with Tardigrades showing a single pip, so the gamble
        // can't cost more than one energy.
        var wanted = OffTypeDemand(state, botId);
        var canPayWanted = wanted.Count == 0 || reserve.Any(d => BotEnergy.IsWild(state, d) || wanted.Any(t => BotEnergy.Pays(state, d, t)));
        if (!canPayWanted)
        {
            foreach (var die in reserve.Where(d => d.CardId is null && BotEnergy.Pips(state, d) == 1 && !BotEnergy.IsWild(state, d)))
                picks.Add(die.Id);
        }

        return picks.Count > 0
            ? new(BotActionKind.Reroll, picks.Count == 1 ? "Reroll one die." : $"Reroll {picks.Count} dice.") { DieIds = picks }
            : new(BotActionKind.FinishRoll, "Keep the roll.");
    }

    // ------------------------------------------------------------------
    // Main Step
    // ------------------------------------------------------------------

    private static BotDecision ActiveMain(GameState state, string botId, IReadOnlySet<string> skip, BotPersona persona)
    {
        // A die kept back for a Global on the opponent's turn (BotPersona.
        // HoldForGlobalCardId) is invisible to everything below.
        var held = HeldForGlobal(state, botId, persona);
        var reserve = state.DiceIn(botId, Zone.ReservePool).Where(d => !skip.Contains(d.Id) && d != held).ToList();

        // Great Horned Owl's Foresight - on a Character die that rolled
        // energy, or failing that the least useful energy die.
        if (TurnEngine.HasForesight(state, botId) && !state.ForesightUsedThisTurn.Contains(botId))
        {
            var target = reserve.FirstOrDefault(d => IsCharacterCard(state, d) && state.GetCurrentFace(d)?.Character is null)
                ?? reserve.Where(d => d.CardId is null && state.GetCurrentFace(d)?.Character is null && !BotEnergy.IsWild(state, d))
                    .OrderBy(d => BotEnergy.Pips(state, d)).FirstOrDefault();
            if (target is not null)
                return new(BotActionKind.Foresight, "Foresight: reroll a weak die.") { DieId = target.Id };
        }

        // Golden Eagle: the free field goes on the die that costs the most to field.
        if (ChampionPowers.CanFieldFree(state, botId)
            && reserve.Where(d => state.GetCurrentFace(d)?.Character is not null && (d.CardId is not { } c || QueryEngine.CanField(state, botId, c)))
                .OrderByDescending(d => QueryEngine.GetFieldingCost(state, d)).FirstOrDefault() is { } freebie
            && QueryEngine.GetFieldingCost(state, freebie) > 0)
            return new(BotActionKind.Field, $"Field {CardName(state, freebie)} free.") { DieId = freebie.Id, Free = true };

        // Wolf, when its pump is Main-only: on the creature most worth
        // swinging with, before attackers are declared.
        if (ChampionPowers.CanUse(state, botId) && ChampionPowers.Of(state, botId) is { PassiveKind: ChampionPassiveKind.PumpOneAttack, PowerBeforeBlocksOnly: true }
            && PotentialAttackersOrAny(state, botId) is not null)
            return new(BotActionKind.UseChampionPower, "Wolf: pump before attacking.");

        // Great Horned Owl: spin before fielding and attacking.
        if (ChampionPowers.CanUse(state, botId) && ChampionPowers.Of(state, botId)?.PassiveKind == ChampionPassiveKind.SpinOne
            && SpinTarget(state, botId) is not null)
            return new(BotActionKind.UseChampionPower, "Owl: spin.");

        // Basic Actions that pay off in Main (a combat pump waits for the
        // Attack Step's window - see ActiveWindow).
        foreach (var die in reserve.Where(d => IsActionFace(state, d) && ActionCouldHaveResult(state, d)))
        {
            if (ShouldUseActionInMain(state, botId, die))
                return new(BotActionKind.UseAction, $"Use {CardName(state, die)}.") { DieId = die.Id };
        }

        // Racing to a rush card: buy its first copy the moment it's
        // affordable, before fielding spends any of the energy - with a
        // purchase discount first if that's what brings it into reach.
        if (Rushing(state, botId, persona) && PurchaseDiscountGlobal(state, botId, reserve, skip, persona) is { } rushDiscount)
            return rushDiscount;
        if (Rushing(state, botId, persona)
            && state.Dice.FirstOrDefault(d => d.CardId == persona.RushCardId && d.OwnerId == botId && d.Zone == Zone.Unpurchased && !skip.Contains(d.Id)) is { } rushDie
            && QueryEngine.CanPurchase(state, botId, persona.RushCardId!))
        {
            var card = state.CardCatalog[persona.RushCardId!];
            if (BotEnergy.PickWithVirtual(state, reserve.Where(d => BotEnergy.Pips(state, d) > 0).ToList(),
                    QueryEngine.GetPurchaseCostNow(state, card, botId), card.EnergySymbolIds.FirstOrDefault(), state.VirtualEnergyOf(botId)) is { } rushPay)
                return new(BotActionKind.Purchase, $"Buy {card.Name} - the card this persona races for.") { DieId = rushDie.Id, EnergyDieIds = rushPay.Dice };
        }

        // Real Characters first: an unfielded one goes to the Used Pile at
        // the end of Main (TurnEngine.EnterAttackStep), and a Tardigrade's
        // zero-energy Bulwark face (1/2) likewise has nothing else to give.
        // Deck-out burn (TurnEngine.ClearAndDraw): each die short of a full
        // draw costs 1 life per turn, paid back as 1 generic energy. A
        // real player takes that burn on purpose when there's life to
        // spare - a bigger board early is worth a few points - but never
        // lets it get anywhere near lethal (user, 2026-09-28). So the
        // allowed shortfall scales with life left after the opponent's
        // current board swings in.
        var drawCount = state.Config.Rules.DrawCount;
        var allowedShortfall = BurnBudget(state, botId);
        foreach (var die in reserve
            .Where(d => state.GetCurrentFace(d)?.Character is not null && (IsCharacterCard(state, d) || BotEnergy.Pips(state, d) == 0))
            .Where(d => d.CardId is null || QueryEngine.CanField(state, botId, d.CardId))
            .OrderByDescending(d => DieValue(state, d)))
        {
            if (Circulating(state, botId) - 1 < drawCount - allowedShortfall) break;
            var pay = BotEnergy.PickWithVirtual(state, reserve.Where(x => x.Id != die.Id).ToList(), QueryEngine.GetFieldingCost(state, die), null,
                state.VirtualEnergyOf(botId));
            if (pay is not null)
                return new(BotActionKind.Field, $"Field {CardName(state, die)}.") { DieId = die.Id, EnergyDieIds = pay.Value.Dice };
        }

        // Then spend energy on the best purchase plan - first paying for a
        // purchase discount when that buys more.
        if (PurchaseDiscountGlobal(state, botId, reserve, skip, persona) is { } discountGlobal) return discountGlobal;
        if (PlanPurchase(state, botId, reserve, skip, persona) is { } buy) return buy;

        // Spare Wing energy: Resurrection's Global draws a die into Prep.
        if (CanDraw(state, botId) && GlobalDecision(state, botId, onlyRealPips: true, g => g.Effect is DrawToZone) is { } draw) return draw;

        // Leftover Tardigrades: a hybrid face (stats + energy) now stays in
        // the Reserve Pool as energy when Main ends (TurnEngine.
        // EnterAttackStep), but a body on the board is usually worth more.
        // A weak body is worth less burn than a real Character - one point
        // less of budget.
        var leftover = reserve
            .Where(d => d.CardId is null && state.GetCurrentFace(d)?.Character is not null)
            .OrderByDescending(d => DieValue(state, d))
            .FirstOrDefault();
        if (leftover is not null && Circulating(state, botId) - 1 >= drawCount - Math.Max(0, allowedShortfall - 1))
            return new(BotActionKind.Field, "Field a leftover Tardigrade.") { DieId = leftover.Id, EnergyDieIds = [] };

        return new(BotActionKind.Pass, "Done in Main - on to the Attack Step.");
    }

    // The opponent's Main: one Global, or pass. Energy left in the Reserve
    // Pool now is cleared at this player's next Clear and Draw anyway.
    private static BotDecision InactiveMain(GameState state, string botId) =>
        (CanDraw(state, botId) ? GlobalDecision(state, botId, onlyRealPips: false, g => g.Effect is DrawToZone) : null)
        ?? new(BotActionKind.Pass, "Pass.");

    // Still racing to the persona's rush card: it has no copy bought yet,
    // and one is left to buy.
    private static bool Rushing(GameState state, string botId, BotPersona persona) =>
        persona.RushCardId is { } rush
        && !state.Dice.Any(d => d.CardId == rush && d.ControllerId == botId && d.Zone != Zone.Unpurchased)
        && state.Dice.Any(d => d.CardId == rush && d.OwnerId == botId && d.Zone == Zone.Unpurchased);

    // The die to keep back through the opponent's turn for the persona's
    // Global (BotPersona.HoldForGlobalCardId - Armadillo: Distraction's
    // "send an attacker back"), when they have a big enough attacker to
    // be worth it. Any face with energy on it survives the end of Main
    // (hybrid stat+energy faces included - TurnEngine.EnterAttackStep).
    private static DieInstance? HeldForGlobal(GameState state, string botId, BotPersona persona)
    {
        if (persona.HoldForGlobalCardId is not { } cardId || !state.CardCatalog.TryGetValue(cardId, out var card)) return null;
        if (!state.PlayerOne.TeamCardIds.Concat(state.PlayerTwo.TeamCardIds).Contains(cardId)) return null;
        var type = card.Abilities.FirstOrDefault(a => a.Trigger == TriggerKind.Global)?.EnergyCost?.RequiredSymbolId;
        if (type is null) return null;
        var threat = PotentialAttackers(state, state.OpponentOf(botId)).Any(d => QueryEngine.GetAttack(state, d) >= persona.HoldAgainstAttack);
        if (!threat) return null;
        return state.DiceIn(botId, Zone.ReservePool)
            .Where(d => BotEnergy.Pips(state, d) > 0 && (BotEnergy.Pays(state, d, type) || BotEnergy.IsWild(state, d)))
            .OrderBy(d => BotEnergy.IsWild(state, d)).ThenBy(d => BotEnergy.Pips(state, d))
            .FirstOrDefault();
    }

    // A draw-into-Prep effect is worth it only with something to draw, and
    // only if moving that die from the Bag to Prep doesn't push next turn's
    // deck-out burn past this player's budget (a Resurrection Global drawn
    // on the opponent's turn decked an Owl to death at 1 life, 2026-09-28).
    private static bool CanDraw(GameState state, string botId) =>
        (state.DiceIn(botId, Zone.Bag).Any() || state.DiceIn(botId, Zone.UsedPile).Any())
        && Circulating(state, botId) - 1 >= state.Config.Rules.DrawCount - BurnBudget(state, botId);

    // Dice available to next turn's draw from the Bag: everything this
    // player controls that isn't on the board, unbought, or in the Prep
    // Area (spent energy in Out of Play returns to the Used Pile at Clean
    // Up, and an empty Bag refills from the Used Pile). Prep dice come back
    // too, but ON TOP of the draw - they never cover a shortfall. Counting
    // them was the bug behind a Wolf losing at 1 life to 1 burn: Mountain
    // Goat's "draw a die into your Prep Area" kept moving dice from the
    // Bag into Prep, one short of what this counted (2026-09-28).
    private static int Circulating(GameState state, string botId) =>
        state.Dice.Count(d => d.ControllerId == botId
            && d.Zone is not (Zone.FieldZone or Zone.AttackZone or Zone.Unpurchased or Zone.PrepArea))
        - PendingAttackDraws(state, botId);

    // Dice that on-attack draws (Mountain Goat, Swift, ...) would still
    // pull from the Bag into Prep this turn if everything able to swing
    // does - counted against circulation, since that's exactly the second
    // way a Wolf decked itself to death at low life (2026-09-28). Only
    // before attacks are declared; afterward the draws already happened.
    private static int PendingAttackDraws(GameState state, string botId) =>
        state.ActivePlayerId == botId && state.CurrentStep == TurnStep.Main
            ? PotentialAttackers(state, botId).Count(d => DrawsOnAttack(state, d))
            : 0;

    private static bool DrawsOnAttack(GameState state, DieInstance die) =>
        QueryEngine.AbilitiesOf(state, die).Any(a => a.Trigger == TriggerKind.DieAttacks && a.Effect is DrawToZone);

    // How many dice short of a full draw (so how many life per turn) this
    // player will accept, from its life left after the opponent's whole
    // board hits: plenty -> 2, some -> 1, tight -> none, and at 3 life or
    // less -1: keep a spare die circulating, since an On Attack/Awaken draw
    // into Prep can still pull one out of the Bag before next turn.
    private static int BurnBudget(GameState state, string botId)
    {
        var life = state.GetPlayer(botId).Life;
        if (life <= 3) return -1;
        var theirSwing = PotentialAttackers(state, state.OpponentOf(botId)).Sum(d => QueryEngine.GetAttack(state, d));
        var cushion = life - theirSwing;
        return cushion >= 14 ? 2 : cushion >= 8 ? 1 : 0;
    }

    private static bool ShouldUseActionInMain(GameState state, string botId, DieInstance die)
    {
        var card = state.CardCatalog[die.CardId!];
        var effect = card.Abilities.FirstOrDefault(a => a.Trigger == TriggerKind.DieUsed)?.Effect;
        var oppId = state.OpponentOf(botId);
        // Obscure only pays off before attackers are declared: an
        // unblockable swing beats holding the action for the window.
        if (PotentialAttackers(state, botId).Any(d => QueryEngine.GetKeywords(state, d).Contains("Obscure")))
            return true;
        switch (effect)
        {
            // Anger Issues-style combat pump: wait for the window.
            case Sequence s when s.Steps.Any(x => x is ModifyStat): return false;
            case ModifyStat: return false;
            // Archnemesis: a fight (your creature vs theirs, simultaneous) -
            // only when one of the pairings comes out ahead.
            case Sequence s when s.Steps.FirstOrDefault() is GrantTag { Tags.Count: 0 }:
                return BestFight(state, botId) is not null;
            // Distraction: only worth it with an attack coming into blockers.
            case CombatFlag { Flag: CombatFlagKind.CantBlock }:
                return PotentialAttackers(state, botId).Any() && PotentialBlockers(state, oppId).Any();
            // Mutation: swap only for a real Character waiting in the Used Pile.
            case Sequence s when s.Steps.FirstOrDefault() is MoveDie { ToZone: Zone.UsedPile }:
                return state.DiceIn(botId, Zone.FieldZone).Any()
                    && state.DiceIn(botId, Zone.UsedPile).Any(d => IsCharacterCard(state, d) && state.CardCatalog[d.CardId!].PurchaseCost >= 3);
            // Resurrection: a free die back, as long as there's one to take.
            case Sequence s when s.Steps.FirstOrDefault() is MoveDie { ToZone: Zone.ReservePool }:
                return state.DiceIn(botId, Zone.UsedPile).Any();
            default:
                return true;
        }
    }

    // Best purchase plan: up to three buys (repeats allowed), each paid
    // from what's left after the previous one, scored by PurchaseValue.
    // Returns the plan's first step; the next call re-plans from there.
    private static BotDecision? PlanPurchase(GameState state, string botId, List<DieInstance> reserve, IReadOnlySet<string> skip, BotPersona persona)
    {
        // A standing "next creature costs N less" (Musk Ox's Global) goes on
        // the first creature bought in the plan.
        var discount = state.PendingPurchaseModifiers.Where(m => m.PlayerId == botId && m.CardKind is null or CardType.Character)
            .Select(m => -m.Delta).FirstOrDefault();
        var (_, first) = BestPurchasePlan(state, botId, reserve, skip, persona, discount);
        if (first is not { } step) return null;
        return new(BotActionKind.Purchase, $"Buy {step.Card.Name}.") { DieId = step.Die.Id, EnergyDieIds = step.Pay };
    }

    // Musk Ox's Global (pay 2 Shell: the next creature costs 3 less): worth
    // it when the plan bought with what's left after paying beats the plan
    // without it - an expensive creature it brings into reach, or a second
    // buy it frees energy for.
    private static BotDecision? PurchaseDiscountGlobal(GameState state, string botId, List<DieInstance> reserve, IReadOnlySet<string> skip, BotPersona persona)
    {
        if (state.PendingPurchaseModifiers.Any(m => m.PlayerId == botId)) return null;
        if (GlobalDecision(state, botId, onlyRealPips: false, g => g.Effect is PurchaseModifier { Delta: < 0 }) is not { } use) return null;
        var discount = -((PurchaseModifier)state.CardCatalog[use.CardId!].Abilities[use.AbilityIndex].Effect).Delta;
        var without = BestPurchasePlan(state, botId, reserve, skip, persona, 0).Value;
        var with = BestPurchasePlan(state, botId, reserve.Where(d => !use.EnergyDieIds.Contains(d.Id)).ToList(), skip, persona, discount).Value;
        return with > without + 0.5 ? use with { Reason = $"Use {state.CardCatalog[use.CardId!].Name}'s Global - a cheaper creature." } : null;
    }

    private static (double Value, (CardDef Card, DieInstance Die, IReadOnlyList<string> Pay)? First) BestPurchasePlan(
        GameState state, string botId, List<DieInstance> reserve, IReadOnlySet<string> skip, BotPersona persona, int discount)
    {
        var options = state.Dice
            .Where(d => d.Zone == Zone.Unpurchased && d.CardId is not null && !skip.Contains(d.Id))
            .Where(d => state.CardCatalog[d.CardId!].CardType.IsCommunity() || d.OwnerId == botId)
            .GroupBy(d => d.CardId!)
            .Select(g => (Card: state.CardCatalog[g.Key], Dice: g.ToList()))
            .Where(x => QueryEngine.CanPurchase(state, botId, x.Card.Id))
            .ToList();
        if (options.Count == 0) return (0, null);

        var owned = state.Dice.Where(d => d.ControllerId == botId && d.CardId is not null && d.Zone != Zone.Unpurchased)
            .GroupBy(d => d.CardId!).ToDictionary(g => g.Key, g => g.Count());
        var offType = OffTypeDemand(state, botId);

        (double Value, (CardDef Card, DieInstance Die, IReadOnlyList<string> Pay)? First) best = (0, null);

        void Search(List<DieInstance> pool, int virtualLeft, Dictionary<string, int> copies, double value, int depth,
            (CardDef, DieInstance, IReadOnlyList<string>)? first, int discountLeft)
        {
            if (value > best.Value) best = (value, first);
            if (depth == 3) return;
            foreach (var (card, dice) in options)
            {
                var bought = copies.GetValueOrDefault(card.Id) - owned.GetValueOrDefault(card.Id);
                if (bought >= dice.Count) continue;
                var gain = PurchaseValue(state, botId, card, copies.GetValueOrDefault(card.Id), offType)
                    + (card.Id == persona.RushCardId && copies.GetValueOrDefault(card.Id) == 0 ? persona.RushBonus : 0);
                if (gain < 2.0) continue; // not worth diluting the bag for
                var discounted = discountLeft > 0 && card.CardType == CardType.Character;
                var cost = QueryEngine.GetPurchaseCost(state, card, botId);
                if (discounted) cost = Math.Max(1, cost - discountLeft);
                if (BotEnergy.PickWithVirtual(state, pool, cost, card.EnergySymbolIds.FirstOrDefault(), virtualLeft) is not { } pay) continue;
                var nextCopies = new Dictionary<string, int>(copies) { [card.Id] = copies.GetValueOrDefault(card.Id) + 1 };
                Search(pool.Where(d => !pay.Dice.Contains(d.Id)).ToList(), virtualLeft - pay.VirtualUsed, nextCopies, value + gain - persona.DilutionPerDie, depth + 1,
                    first ?? (card, dice[bought], pay.Dice), discounted ? 0 : discountLeft);
            }
        }

        Search(reserve.Where(d => BotEnergy.Pips(state, d) > 0).ToList(), state.VirtualEnergyOf(botId),
            new Dictionary<string, int>(owned), 0, 0, null, discount);
        return best;
    }

    // How much one more copy of `card` is worth. Purchase cost is the
    // baseline - the roster is costed by the Homash model, so cost already
    // tracks power - discounted for copies already owned, and for cheap
    // cards once the bag is already full of them.
    private static double PurchaseValue(GameState state, string botId, CardDef card, int copiesOwned, IReadOnlySet<string> offTypeDemand)
    {
        if (card.CardType.IsActionDie())
        {
            var energyType = ActionEnergyType(card);
            var v = 2.5 + (energyType is not null && offTypeDemand.Contains(energyType) ? 1.5 : 0)
                // Every action die also fires the team's Attune/Obscure creatures.
                + (TeamHasActionPayoff(state, botId) ? 1.5 : 0);
            return v * (copiesOwned switch { 0 => 1.0, 1 => 0.6, _ => 0.3 });
        }
        var purchasedDice = state.Dice.Count(d => d.ControllerId == botId && d.CardId is not null && d.Zone != Zone.Unpurchased);
        var baseValue = card.PurchaseCost + 1.0;
        if (card.PurchaseCost <= 3 && purchasedDice >= 8) baseValue *= 0.6;
        return baseValue * (copiesOwned switch { 0 => 1.0, 1 => 0.9, 2 => 0.7, _ => 0.45 });
    }

    private static bool IsActionPayoff(string keyword) => keyword == "Obscure" || KeywordAbilities.AttuneAmount(keyword) is not null;

    private static bool TeamHasActionPayoff(GameState state, string botId) =>
        state.GetPlayer(botId).TeamCardIds.Any(id => state.CardCatalog.TryGetValue(id, out var c) && c.Keywords.Any(IsActionPayoff));

    // Energy types this player's team needs but its own dice don't make:
    // any card still Unpurchased whose type isn't the Champion's own.
    private static IReadOnlySet<string> OffTypeDemand(GameState state, string botId)
    {
        var own = OwnEnergyType(state, botId);
        return state.Dice
            .Where(d => d.Zone == Zone.Unpurchased && d.OwnerId == botId && d.CardId is not null)
            .Select(d => state.CardCatalog[d.CardId!])
            .Where(c => !c.CardType.IsCommunity())
            .SelectMany(c => c.EnergySymbolIds)
            .Where(t => t != own)
            .ToHashSet();
    }

    private static string? OwnEnergyType(GameState state, string botId) =>
        state.GetPlayer(botId).ChampionId is { } id ? state.Config.Champions.FirstOrDefault(c => c.Id == id)?.EnergySymbolId : null;

    private static string? ActionEnergyType(CardDef card) =>
        card.Die.Faces.SelectMany(f => f.Symbols).Select(s => s.SymbolId).FirstOrDefault();

    // ------------------------------------------------------------------
    // Globals
    // ------------------------------------------------------------------

    private sealed record GlobalOption(CardDef Card, int Index, EffectNode Effect, int Cost, string? Type);

    // Every Global this player could pay for right now whose effect passes
    // `want`, cheapest first; `onlyRealPips` keeps Wilds for later.
    private static BotDecision? GlobalDecision(GameState state, string botId, bool onlyRealPips, Func<GlobalOption, bool> want)
    {
        var pool = state.DiceIn(botId, Zone.ReservePool).Where(d => BotEnergy.Pips(state, d) > 0).ToList();
        if (onlyRealPips) pool = pool.Where(d => !BotEnergy.IsWild(state, d)).ToList();
        foreach (var cardId in state.PlayerOne.TeamCardIds.Concat(state.PlayerTwo.TeamCardIds).Distinct())
        {
            if (!state.CardCatalog.TryGetValue(cardId, out var card) || !QueryEngine.CardTextActive(state, botId, cardId)) continue;
            for (var i = 0; i < card.Abilities.Count; i++)
            {
                var ability = card.Abilities[i];
                if (ability.Trigger != TriggerKind.Global) continue;
                if (ability.OncePerTurn && state.GlobalsUsedThisTurn.Contains((botId, cardId))) continue;
                var option = new GlobalOption(card, i, ability.Effect, QueryEngine.GetGlobalEnergyCost(state, card, ability, botId),
                    ability.EnergyCost?.RequiredSymbolId);
                if (!want(option)) continue;
                if (!AbilityPreview.CouldHaveResult(state, botId, ability.Effect, ProtectionFrom.Global)) continue; // rule 3.1.10
                if (BotEnergy.PickWithVirtual(state, pool, option.Cost, option.Type, state.VirtualEnergyOf(botId)) is not { } pay) continue;
                return new(BotActionKind.UseGlobal, $"Use {card.Name}'s Global.") { CardId = cardId, AbilityIndex = i, EnergyDieIds = pay.Dice };
            }
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Attack Step
    // ------------------------------------------------------------------

    private static BotDecision DecideAttackers(GameState state, string botId, BotPersona persona)
    {
        var oppId = state.OpponentOf(botId);
        var oppLife = state.GetPlayer(oppId).Life;
        var myLife = state.GetPlayer(botId).Life;
        var candidates = PotentialAttackers(state, botId).OrderByDescending(d => QueryEngine.GetAttack(state, d)).ToList();
        var blockers = PotentialBlockers(state, oppId).ToList();
        if (candidates.Count == 0) return Declare([], "Nothing worth attacking with.");

        // Lethal: even if they block our biggest lanes, the rest kills.
        var spread = candidates.Take(MaxLanes).ToList();
        var unblockableDamage = spread.Skip(Math.Min(blockers.Count, spread.Count)).Sum(d => QueryEngine.GetAttack(state, d))
            + candidates.Where(d => d.CombatFlags.Contains(CombatFlagKind.Unblockable)).Except(spread).Sum(d => QueryEngine.GetAttack(state, d));
        if (unblockableDamage >= oppLife) return Declare(candidates, "All in - lethal.");

        // Hermit Crab's forced block + a retaliator (Rhinoceros): Rhinoceros
        // leads ONE lane, so the forced blocker has nothing else to block and
        // every point it hits for comes back at its controller. Whatever else
        // attacks joins that lane as far as the stacking rule allows (user, 2026-10-04): blockers' damage lands
        // lethal-first in declaration order, so Rhinoceros soaks it ahead of
        // the others, and a 2+ attacker lane has Overcrush.
        var forced = blockers.Where(b => b.CombatFlags.Contains(CombatFlagKind.MustBlock)).ToList();
        var retaliator = candidates.Where(d => IsRetaliator(state, d)).OrderByDescending(d => QueryEngine.GetDefense(state, d)).FirstOrDefault();
        var forcedCombo = forced.Count > 0 && retaliator is not null && forced.Max(b => QueryEngine.GetAttack(state, b)) >= 1;

        // Crack-back: what they're likely to swing with NEXT turn - not just
        // what's on their Field now, which right after their own swing is
        // usually nothing (that read made every game a pure race, 2026-10-04).
        var theirAttack = ExpectedThreats(state, oppId);

        // An on-attack draw pulls a die out of the Bag into Prep, which
        // doesn't help next turn's draw - don't let those swings push the
        // deck-out burn past what this player can afford.
        var drawable = Circulating(state, botId);
        var minDrawable = state.Config.Rules.DrawCount - BurnBudget(state, botId);

        var attackers = new List<DieInstance>();
        foreach (var die in candidates)
        {
            // A retaliator (Rhinoceros) does its work blocking: swung in,
            // it goes unblocked, chips for a point or two, and leaves play
            // (rule 2.7.4.3.1) - which is exactly what the simulator saw it
            // do every game (2026-09-29).
            if (IsRetaliator(state, die)) continue;
            if (!WorthSwinging(state, die, blockers, persona)) continue;
            if (DrawsOnAttack(state, die))
            {
                if (drawable - 1 < minDrawable) continue;
                drawable--;
            }
            var home = PotentialBlockers(state, botId).Count(d => d != die && !attackers.Contains(d));
            var crackBack = theirAttack.Skip(home).Sum();
            if (crackBack >= myLife) continue;
            // The trade (user, 2026-10-04: "keep enough blockers so that I can
            // block the higher attack characters I expect my opponent to have
            // next turn"): swung, this die deals its ATK now and leaves play;
            // kept home, it blocks the next-biggest threat they're likely to
            // bring. Each weighed against the life it comes off, so a low
            // opponent pulls toward swinging and a low self toward holding.
            var absorbed = home < theirAttack.Count ? theirAttack[home] : 0;
            var swing = (double)QueryEngine.GetAttack(state, die) / Math.Max(1, oppLife);
            var hold = absorbed / Math.Max(1, myLife);
            if (hold > swing * (0.75 + 0.25 * persona.Aggression)) continue;
            attackers.Add(die);
        }
        // Retaliating blockers (Rhinoceros): each can block one attacker and
        // send its ATK back at us - it'll pick the biggest. Swinging wide is
        // still worth it if more gets through than comes back; otherwise
        // hold (the "never swing into it" first version stalled 18% of
        // simulated games to the turn cap, 2026-09-29).
        var retaliators = blockers.Count(b => IsRetaliator(state, b));
        if (retaliators > 0 && attackers.Count > 0)
        {
            var atks = attackers.Select(d => QueryEngine.GetAttack(state, d)).OrderByDescending(a => a).ToList();
            var reflected = atks.Take(retaliators).Sum();
            var through = atks.Skip(blockers.Count).Sum();
            if (reflected >= through) attackers.Clear();
        }
        if (forcedCombo)
        {
            // Each joins Rhinoceros's lane when the stacking rule allows it
            // (another lane as full), else the emptiest other lane.
            var stack = new Dictionary<string, int> { [retaliator!.Id] = 0 }; // declared first
            var counts = new int[MaxLanes];
            counts[0] = 1;
            foreach (var d in attackers)
            {
                var lane = CombatEngine.CanAddToLane(counts, 0) ? 0
                    : Enumerable.Range(1, MaxLanes - 1).OrderBy(l => counts[l]).First();
                counts[lane]++;
                stack[d.Id] = lane;
            }
            return new(BotActionKind.DeclareAttackers, stack.Count == 1 ? "Rhinoceros into a forced blocker."
                : $"Rhinoceros into a forced blocker, {counts[0] - 1} more behind it.") { AttackerLanes = stack };
        }
        return Declare(attackers, attackers.Count == 0 ? "Hold back - no good attacks." : $"Attack with {attackers.Count}.");
    }

    // What `playerId` is likely to attack with next turn, biggest first:
    // their Field creatures exactly, plus every die that may come up -
    // the Prep Area for sure, the Bag at its draw odds, the Used Pile and
    // Out of Play only for whatever the Bag can't cover - each weighted by
    // its chance of rolling a creature face and that face's average ATK.
    // Every die's zone is public, so this is information a player has too.
    // Ignores whether they can afford to field it, and rerolls.
    private static List<double> ExpectedThreats(GameState state, string playerId)
    {
        var threats = PotentialAttackers(state, playerId).Select(d => (double)QueryEngine.GetAttack(state, d)).ToList();
        double Expected(DieInstance d)
        {
            var faces = state.GetDieDefinition(d).Faces;
            var bodies = faces.Where(f => f.Character is not null).ToList();
            return bodies.Count == 0 ? 0 : (double)bodies.Count / faces.Count * bodies.Average(f => f.Character!.Attack);
        }
        var draws = state.Config.Rules.DrawCount;
        var bag = state.DiceIn(playerId, Zone.Bag).ToList();
        var refill = state.DiceIn(playerId, Zone.UsedPile).Concat(state.DiceIn(playerId, Zone.OutOfPlay)).ToList();
        var bagOdds = bag.Count == 0 ? 0 : Math.Min(1.0, (double)draws / bag.Count);
        var refillOdds = bag.Count >= draws || refill.Count == 0 ? 0 : Math.Min(1.0, (double)(draws - bag.Count) / refill.Count);
        threats.AddRange(state.DiceIn(playerId, Zone.PrepArea).Select(Expected));
        threats.AddRange(bag.Select(d => bagOdds * Expected(d)));
        threats.AddRange(refill.Select(d => refillOdds * Expected(d)));
        threats = threats.Where(t => t > 0.05).OrderByDescending(t => t).ToList();
        // Their Wolf's pump lands on one attacker.
        if (threats.Count > 0 && ChampionPowers.Of(state, playerId) is { PassiveKind: ChampionPassiveKind.PumpOneAttack } wolf)
            threats[0] += wolf.Amount;
        return threats;
    }

    // Would the defender's best response to this attacker be bad for us?
    // A blocker that kills it and survives is a free kill for them.
    private static bool WorthSwinging(GameState state, DieInstance attacker, IReadOnlyList<DieInstance> blockers, BotPersona persona)
    {
        if (attacker.CombatFlags.Contains(CombatFlagKind.Unblockable)) return true;
        var atk = QueryEngine.GetAttack(state, attacker);
        var def = QueryEngine.GetDefense(state, attacker) - attacker.Damage;
        var deadly = QueryEngine.GetKeywords(state, attacker).Contains("Deadly");
        var myValue = DieValue(state, attacker);
        foreach (var b in blockers)
        {
            var kills = QueryEngine.GetAttack(state, b) >= def || QueryEngine.GetKeywords(state, b).Contains("Deadly");
            var survives = QueryEngine.GetDefense(state, b) - b.Damage > atk && !deadly;
            if (kills && survives && persona.Aggression < 2) return false;
            if (kills && !survives && persona.Aggression < 1) return false;
            if (kills && !survives && DieValue(state, b) < myValue * 0.75 && persona.Aggression < 2) return false; // a bad trade
        }
        return true;
    }

    private static BotDecision Declare(IReadOnlyList<DieInstance> attackers, string reason)
    {
        // One attacker per lane first (each needs its own blocker), extras
        // stacked onto the lanes in turn (a 2+ lane gains Overcrush).
        var lanes = attackers.Select((d, i) => (d.Id, Lane: i % MaxLanes)).ToDictionary(x => x.Id, x => x.Lane);
        return new(BotActionKind.DeclareAttackers, reason) { AttackerLanes = lanes };
    }

    private static BotDecision DecideBlockers(GameState state, string botId, BotPersona persona)
    {
        var activeId = state.OpponentOf(botId);
        var myLife = state.GetPlayer(botId).Life;
        // One unblockable attacker makes its whole lane unblockable
        // (CombatEngine.ValidateUnblockable - a lane is the unit of combat).
        var attacking = state.DiceIn(activeId, Zone.AttackZone).ToList();
        var openLanes = attacking.GroupBy(d => d.Lane ?? -1)
            .Where(g => !g.Any(a => a.CombatFlags.Contains(CombatFlagKind.Unblockable)))
            .Select(g => g.Key).ToHashSet();
        var lanes = attacking
            .Where(d => openLanes.Contains(d.Lane ?? -1))
            .GroupBy(d => d.Lane ?? -1)
            .Select(g => new Lane(g.ToList(),
                g.Sum(a => QueryEngine.GetAttack(state, a)),
                g.Count() >= 2 || g.Any(a => QueryEngine.GetKeywords(state, a).Contains("Overcrush")),
                g.Any(a => QueryEngine.GetKeywords(state, a).Contains("Deadly"))))
            .OrderByDescending(l => l.Attack)
            .ToList();
        var unblockable = attacking.Where(d => !openLanes.Contains(d.Lane ?? -1)).Sum(d => QueryEngine.GetAttack(state, d));
        var available = PotentialBlockers(state, botId).ToList();
        var blocks = new List<(string, string)>();
        var incoming = lanes.Sum(l => l.Attack) + unblockable;

        // How much a point of damage matters, rising as life falls.
        double DamageWeight() => persona.ChumpThreshold / Math.Max(1, myLife) * 0.5;

        // Armadillo's shield (prevent combat damage to one of its creatures,
        // after blocks) makes one block a free wall: plan for it here, and
        // ShieldTarget protects that blocker in the action window.
        var shieldLeft = ChampionPowers.Of(state, botId)?.PassiveKind == ChampionPassiveKind.ShieldOneFromCombat
            && !state.ChampionPowerUsedThisTurn.Contains(botId);

        // Wolf's pump comes after blocks (2026-10-04): assume it's coming
        // while unused - one attacker gets +N, so +N to what's incoming and
        // to what any blocker has to survive.
        var pump = ChampionPowers.Of(state, activeId) is { PassiveKind: ChampionPassiveKind.PumpOneAttack } wolf
            && !state.ChampionPowerUsedThisTurn.Contains(activeId) ? wolf.Amount : 0;
        incoming += pump;

        foreach (var lane in lanes)
        {
            if (available.Count == 0) break;
            var mustBlock = incoming >= myLife;
            (DieInstance Blocker, double Score)? best = null;
            foreach (var b in available)
            {
                var score = BlockScore(state, b, lane, DamageWeight(), pump: pump);
                if (best is null || score > best.Value.Score) best = (b, score);
            }
            if (shieldLeft && !lane.Deadly)
            {
                var shielded = available.Select(b => (Blocker: b, Score: BlockScore(state, b, lane, DamageWeight(), shielded: true, pump: pump)))
                    .OrderByDescending(x => x.Score).First();
                if (shielded.Score > Math.Max(0, best?.Score ?? 0) + 0.5)
                {
                    best = shielded;
                    shieldLeft = false;
                }
            }
            if (best is not { } pick) break;
            if (pick.Score <= 0 && !mustBlock) continue;

            blocks.Add((lane.Attackers[0].Id, pick.Blocker.Id));
            available.Remove(pick.Blocker);
            var prevented = lane.Overcrush ? Math.Min(lane.Attack, QueryEngine.GetDefense(state, pick.Blocker)) : lane.Attack;
            incoming -= prevented;
        }

        // Forced blockers (Hermit Crab's "must block") have to be declared
        // if there's anything to block - into whichever lane hurts least.
        if (lanes.Count > 0)
        {
            foreach (var b in available.Where(d => d.CombatFlags.Contains(CombatFlagKind.MustBlock)).ToList())
            {
                var lane = lanes.OrderByDescending(l => BlockScore(state, b, l, DamageWeight())).First();
                blocks.Add((lane.Attackers[0].Id, b.Id));
                available.Remove(b);
            }
        }

        return new(BotActionKind.DeclareBlockers, blocks.Count == 0 ? "No blocks." : $"Block {blocks.Count}.") { Blocks = blocks };
    }

    private sealed record Lane(IReadOnlyList<DieInstance> Attackers, int Attack, bool Overcrush, bool Deadly);

    // Outcome of putting `b` in front of a lane: its attackers' value if
    // it kills one, minus its own if it dies, plus the damage it keeps off
    // this player's life (weighted by how much life matters right now).
    // `shielded`: Armadillo will prevent the combat damage to it. `pump`:
    // Wolf's +N may still land on the attacker after blocks.
    //
    // Dice Kingdom is a bag-builder, so where a die ENDS UP matters, not just
    // whether it survives (user, 2026-10-04 - the Dice Masters "let the
    // sidekick through / chump with the sidekick" calculation):
    //   - a KO'd die goes to its owner's Prep Area and is rolled ON TOP of
    //     next turn's draw - an extra die, mostly energy for a Tardigrade
    //     (PrepBonus). So a dying blocker isn't a full loss, and KO'ing an
    //     attacker hands its owner that extra die;
    //   - an unblocked attacker leaves play for the Used Pile and then the
    //     Bag - a fielded Tardigrade is effectively culled, and letting one
    //     through un-culls it into the attacker's draws (UncullBonus).
    private static double BlockScore(GameState state, DieInstance b, Lane lane, double damageWeight, bool shielded = false, int pump = 0)
    {
        var bAtk = QueryEngine.GetAttack(state, b);
        var bDef = QueryEngine.GetDefense(state, b) - b.Damage;
        var survives = (shielded || bDef > lane.Attack + pump) && !lane.Deadly;
        var bDeadly = QueryEngine.GetKeywords(state, b).Contains("Deadly");
        var killed = lane.Attackers.Where(a => bDeadly || QueryEngine.GetDefense(state, a) - a.Damage <= bAtk)
            .OrderByDescending(a => DieValue(state, a)).FirstOrDefault();
        var prevented = lane.Overcrush && !shielded ? Math.Min(lane.Attack, bDef) : lane.Attack;

        var score = prevented * damageWeight;
        // A retaliating blocker sends the damage it takes to the attacker's
        // controller; a retaliating attacker sends ours back to us.
        var reflectWeight = Math.Max(0.5, 6.0 / Math.Max(1, state.GetPlayer(state.ActivePlayerId).Life));
        if (IsRetaliator(state, b) && !shielded) score += Math.Min(lane.Attack, bDef) * reflectWeight; // shielded, it takes nothing to reflect
        if (lane.Attackers.Any(a => IsRetaliator(state, a))) score -= bAtk * damageWeight * 1.5;
        if (killed is not null) score += DieValue(state, killed) - PrepBonus(killed);
        if (!survives) score -= DieValue(state, b) - PrepBonus(b);
        if (survives && killed is null) score += 0.5; // a free wall
        // Blocking keeps the lane's surviving Tardigrade attackers on the
        // Field (still culled) instead of sending them back to their Bag.
        score -= lane.Attackers.Count(a => a.CardId is null && a != killed) * UncullBonus(state, lane.Attackers[0].ControllerId);
        return score;
    }

    // An extra die rolled next turn: mostly energy for a Tardigrade (5 of 6
    // faces carry it); a Character has to be fielded again, so less.
    private static double PrepBonus(DieInstance die) => die.CardId is null ? 1.0 : 0.5;

    // A Tardigrade back in `ownerId`'s rotation dilutes their draws - but
    // only once they own something better to draw instead.
    private static double UncullBonus(GameState state, string ownerId) =>
        state.Dice.Any(d => d.OwnerId == ownerId && d.CardId is { } c && !state.CardCatalog[c].CardType.IsActionDie() && d.Zone != Zone.Unpurchased)
            ? 0.5 : 0;

    // ------------------------------------------------------------------
    // Attack Step's Action/Global window
    // ------------------------------------------------------------------

    private static BotDecision ActiveWindow(GameState state, string botId)
    {
        var unblocked = UnblockedAttackers(state).Where(d => d.ControllerId == botId).ToList();
        var attacking = state.DiceIn(botId, Zone.AttackZone).ToList();

        // A combat pump action (Anger Issues) is only good now.
        var pump = state.DiceIn(botId, Zone.ReservePool)
            .FirstOrDefault(d => IsActionFace(state, d) && IsCombatPump(state, d) && ActionCouldHaveResult(state, d));
        if (pump is not null && attacking.Count > 0)
            return new(BotActionKind.UseAction, $"Use {CardName(state, pump)}.") { DieId = pump.Id };

        if (SetDefenseGlobal(state, botId) is { } setDef) return setDef;

        if (ChampionPowerInWindow(state, botId) is { } power) return power;

        // +ATK Globals on an unblocked attacker are straight extra damage.
        if (unblocked.Count > 0 && GlobalDecision(state, botId, onlyRealPips: false, g => g.Effect is ModifyStat { AtkDelta: > 0 }) is { } g)
            return g;

        return new(BotActionKind.Pass, "Pass - resolve combat.");
    }

    private static BotDecision InactiveWindow(GameState state, string botId)
    {
        // Send the biggest unblocked attacker home (Distraction's Global).
        var biggest = UnblockedAttackers(state).Where(d => d.ControllerId != botId)
            .Select(d => QueryEngine.GetAttack(state, d)).DefaultIfEmpty(0).Max();
        if (biggest >= 2 && GlobalDecision(state, botId, onlyRealPips: false,
                g => g.Effect is MoveDie { Target.AttackersOnly: true }) is { } send)
            return send;
        if (SetDefenseGlobal(state, botId) is { } setDef) return setDef;
        if (ChampionPowerInWindow(state, botId) is { } power) return power;
        return new(BotActionKind.Pass, "Pass.");
    }

    // Wolf's pump and Armadillo's shield, in the action window after blocks.
    private static BotDecision? ChampionPowerInWindow(GameState state, string botId)
    {
        if (!ChampionPowers.CanUse(state, botId)) return null;
        return ChampionPowers.Of(state, botId)?.PassiveKind switch
        {
            ChampionPassiveKind.PumpOneAttack when PumpTarget(state, botId) is not null => new(BotActionKind.UseChampionPower, "Wolf: pump."),
            ChampionPassiveKind.ShieldOneFromCombat when ShieldTarget(state, botId) is not null => new(BotActionKind.UseChampionPower, "Armadillo: shield."),
            _ => null,
        };
    }

    // Wolf in Main (pump before attacking): the strongest creature that
    // can attack, else any of its creatures.
    private static DieInstance? PotentialAttackersOrAny(GameState state, string botId) =>
        state.DiceIn(botId, Zone.FieldZone).Where(d => state.GetCurrentFace(d)?.Character is not null
                && !d.CombatFlags.Contains(CombatFlagKind.CantAttack) && !d.CombatFlags.Contains(CombatFlagKind.OnlyBlocker))
            .OrderByDescending(d => QueryEngine.GetAttack(state, d) + QueryEngine.GetDefense(state, d)).FirstOrDefault();

    // Wolf: an unblocked attacker turns +ATK straight into face damage;
    // failing that, a blocked attacker (it may now win its fight).
    private static DieInstance? PumpTarget(GameState state, string botId)
    {
        if (state.CurrentStepId == StepIds.Main) return PotentialAttackersOrAny(state, botId);
        var unblocked = UnblockedAttackers(state).Where(d => d.ControllerId == botId).ToList();
        if (unblocked.Count > 0) return unblocked.OrderByDescending(d => DieValue(state, d)).First();
        return EngagedOwnDice(state, botId).Where(d => d.ControllerId == state.ActivePlayerId)
            .OrderByDescending(d => DieValue(state, d)).FirstOrDefault();
    }

    // Armadillo: the most valuable of this player's dice that combat would
    // otherwise KO - incoming is the other side of its lane's whole ATK.
    private static DieInstance? ShieldTarget(GameState state, string botId)
    {
        if (state.DeclaredBlocks is not { } blocks) return null;
        var attackers = state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList();
        int Incoming(DieInstance die)
        {
            if (die.ControllerId == state.ActivePlayerId)
            {
                var lane = attackers.Where(a => a.Lane == die.Lane).ToList();
                return lane.SelectMany(a => blocks.BlockersOf(a.Id)).Distinct()
                    .Select(id => state.Dice.First(d => d.Id == id)).Sum(b => QueryEngine.GetAttack(state, b));
            }
            var blocked = attackers.Where(a => blocks.BlockersOf(a.Id).Contains(die.Id)).Select(a => a.Lane).ToHashSet();
            return attackers.Where(a => blocked.Contains(a.Lane)).Sum(a => QueryEngine.GetAttack(state, a));
        }
        // Never a retaliator (Rhinoceros): the shield prevents the very
        // damage it would send back - it cancelled the Hermit Crab combo on
        // ~0.9 turns a game in the Owl matchup (2026-10-04). KO'd, it goes
        // to the Prep Area and is rolled again next turn.
        return EngagedOwnDice(state, botId)
            .Where(d => !IsRetaliator(state, d))
            .Where(d => Incoming(d) >= QueryEngine.GetDefense(state, d) - d.Damage)
            .OrderByDescending(d => DieValue(state, d)).FirstOrDefault();
    }

    // Owl: spin down the opponent's most valuable creature at L2+, else spin
    // up one of this player's below L3.
    private static DieInstance? SpinTarget(GameState state, string botId)
    {
        var inPlay = state.Dice.Where(d => d.Zone is Zone.FieldZone or Zone.AttackZone && state.GetCurrentFace(d)?.Character is not null).ToList();
        return inPlay.Where(d => d.ControllerId != botId && QueryEngine.LevelsAround(state, d, above: false) > 0)
                .OrderByDescending(d => DieValue(state, d)).FirstOrDefault()
            ?? inPlay.Where(d => d.ControllerId == botId && QueryEngine.LevelsAround(state, d, above: true) > 0)
                .OrderByDescending(d => DieValue(state, d)).FirstOrDefault();
    }

    // Archnemesis's Global ("target creature's D becomes its A this turn"),
    // in combat, when it swings a die's D by 2+ the right way: up for one
    // of ours (a high-ATK die that would otherwise die), down for one of
    // theirs (a wall our damage can then get through).
    private static BotDecision? SetDefenseGlobal(GameState state, string botId)
    {
        var best = state.Dice.Where(d => d.Zone == Zone.AttackZone).Select(d => SetDefenseGain(state, botId, d)).DefaultIfEmpty(0).Max();
        return best >= 2 ? GlobalDecision(state, botId, onlyRealPips: false, g => g.Effect is ModifyStat { SetDefense: not null }) : null;
    }

    private static double SetDefenseGain(GameState state, string botId, DieInstance die)
    {
        if (state.GetCurrentFace(die)?.Character is null) return -1;
        var change = QueryEngine.GetAttack(state, die) - QueryEngine.GetDefense(state, die);
        return die.ControllerId == botId ? change : -change;
    }

    // Archnemesis's fight: the (yours, theirs) pairing with the best
    // outcome - their die's value if yours KOs it, minus yours if theirs
    // KOs it back (simultaneous). Null when no pairing comes out ahead.
    private static (DieInstance Mine, DieInstance Theirs)? BestFight(GameState state, string botId)
    {
        static bool InPlay(GameState s, DieInstance d) => d.Zone is Zone.FieldZone or Zone.AttackZone && s.GetCurrentFace(d)?.Character is not null;
        var mine = state.Dice.Where(d => d.ControllerId == botId && InPlay(state, d)).ToList();
        var theirs = state.Dice.Where(d => d.ControllerId == state.OpponentOf(botId) && InPlay(state, d)).ToList();
        (DieInstance, DieInstance)? best = null;
        var bestScore = 0.5;
        foreach (var a in mine)
            foreach (var b in theirs)
            {
                var kills = QueryEngine.GetAttack(state, a) >= QueryEngine.GetDefense(state, b) - b.Damage;
                var dies = QueryEngine.GetAttack(state, b) >= QueryEngine.GetDefense(state, a) - a.Damage;
                var score = (kills ? DieValue(state, b) : 0) - (dies ? DieValue(state, a) : 0);
                if (score > bestScore) { bestScore = score; best = (a, b); }
            }
        return best;
    }

    private static IEnumerable<DieInstance> UnblockedAttackers(GameState state)
    {
        var blocks = state.DeclaredBlocks;
        var attackers = state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList();
        var blockedLanes = attackers.Where(a => blocks?.BlockersOf(a.Id).Count > 0).Select(a => a.Lane).ToHashSet();
        return attackers.Where(a => !blockedLanes.Contains(a.Lane));
    }

    // ------------------------------------------------------------------
    // Pending choices
    // ------------------------------------------------------------------

    private static BotDecision AnswerChoice(GameState state, string botId, PendingChoice pending)
    {
        double Score(string id)
        {
            if (state.IsPlayerId(id))
            {
                // A "you may" stand-in, or a player target.
                if (pending.CandidateIds.Count == 1 && pending.MinCount == 0) return pending.Intent == ChoiceIntent.Harmful ? -1 : 1;
                var mine = id == botId;
                return pending.Intent == ChoiceIntent.Harmful ? (mine ? -10 : 3) : (mine ? 3 : -10);
            }
            var die = state.Dice.First(d => d.Id == id);
            var own = die.ControllerId == botId;
            if (pending.Intent == ChoiceIntent.NameCard)
                return own || die.CardId is null ? -10 : CardThreat(state, die.ControllerId, die.CardId);
            var value = ChoiceValue(state, die);
            // In the window, what matters most is who's hitting face.
            if (state.CurrentStepId == StepIds.ActionGlobalWindow && UnblockedAttackers(state).Any(a => a.Id == id))
                value += 2 * QueryEngine.GetAttack(state, die);
            return pending.Intent == ChoiceIntent.Harmful ? (own ? -value : value) : (own ? value : -value);
        }

        // Picks the intent alone can't judge (PendingChoice.Effect).
        switch (pending.Effect)
        {
            // Infiltrate: 1 damage and the die stays home, or its full ATK
            // and it leaves play. Stay home unless the full hit is lethal
            // (and 1 isn't) or the full hit is big enough to be worth a die.
            // Champion powers: the same target the decision to use it was based on.
            case ModifyStat { AtkDelta: > 0 } when ChampionPowers.Of(state, botId) is { PassiveKind: ChampionPassiveKind.PumpOneAttack } wolf
                && pending.Description.StartsWith(wolf.Name + ":")
                && PumpTarget(state, botId) is { } pumped && pending.CandidateIds.Contains(pumped.Id):
                return new(BotActionKind.ResolvePendingChoice, "Pump it.") { DieIds = [pumped.Id] };
            case CombatFlag { Flag: CombatFlagKind.PreventCombatDamage } when ShieldTarget(state, botId) is { } shielded && pending.CandidateIds.Contains(shielded.Id):
                return new(BotActionKind.ResolvePendingChoice, "Shield it.") { DieIds = [shielded.Id] };
            case SpinByOwner when SpinTarget(state, botId) is { } spun && pending.CandidateIds.Contains(spun.Id):
                return new(BotActionKind.ResolvePendingChoice, "Spin it.") { DieIds = [spun.Id] };

            // Attune / Energize: KO the best opposing creature the damage
            // actually kills; otherwise the damage goes to the opponent's face.
            case DealDamage { Target.Kind: TargetKind.CharacterDieOrOpponent, Amount: Fixed { Value: var dmg } }:
                var kill = pending.CandidateIds.Where(id => !state.IsPlayerId(id))
                    .Select(id => state.Dice.First(d => d.Id == id))
                    .Where(d => d.ControllerId != botId && QueryEngine.GetDefense(state, d) - d.Damage <= dmg)
                    .OrderByDescending(d => DieValue(state, d)).FirstOrDefault();
                var face = pending.CandidateIds.FirstOrDefault(id => state.IsPlayerId(id) && id != botId);
                if ((kill?.Id ?? face) is { } attunePick)
                    return new(BotActionKind.ResolvePendingChoice, kill is not null ? "Attune: finish a creature." : "Attune: hit face.") { DieIds = [attunePick] };
                break;
            // Range: KO something if the shots already called on it plus this
            // one finish it (they all land together); else the best target.
            case RangeShot shot:
                var shotPick = pending.CandidateIds.Select(id => state.Dice.First(d => d.Id == id))
                    .Select(d => (Die: d, Left: QueryEngine.GetDefense(state, d) - d.Damage - state.RangeShots.Where(r => r.TargetId == d.Id).Sum(r => r.Amount)))
                    .Where(x => x.Left > 0)
                    .OrderByDescending(x => x.Left <= shot.Amount ? 1 : 0).ThenByDescending(x => DieValue(state, x.Die))
                    .Select(x => x.Die).FirstOrDefault();
                if (shotPick is not null)
                    return new(BotActionKind.ResolvePendingChoice, "Range shot.") { DieIds = [shotPick.Id] };
                break;

            // Breath Weapon: worth the energy for lethal or at least one KO.
            case MayPayEnergy breath:
                var opp = state.OpponentOf(botId);
                var x = breath.Amount;
                var kills = state.DiceIn(opp, Zone.FieldZone).Concat(state.DiceIn(opp, Zone.AttackZone))
                    .Count(d => state.GetCurrentFace(d)?.Character is not null && QueryEngine.GetDefense(state, d) - d.Damage <= x);
                var payDice = BotEnergy.Pick(state, pending.CandidateIds.Select(id => state.Dice.First(d => d.Id == id)).ToList(), x, null);
                var breathe = payDice is not null && (state.GetPlayer(opp).Life <= x || kills > 0);
                return new(BotActionKind.ResolvePendingChoice, breathe ? $"Breath Weapon - {kills} KO(s)." : "Keep the energy.")
                    { DieIds = breathe ? payDice! : [] };

            // Tag Out: only with one of this player's dice in a fight, where
            // +2/+2 can swing it - then that die gets the bonus.
            case MayPay offer when offer == KeywordAbilities.TagOutChoice:
                var tagOut = EngagedOwnDice(state, botId).Any();
                return new(BotActionKind.ResolvePendingChoice, tagOut ? "Tag Out." : "No Tag Out - nothing fighting.")
                    { DieIds = tagOut ? [pending.CandidateIds[0]] : [] };
            case ModifyStat { AtkDelta: 2, DefDelta: 2 } when pending.Description.Contains("Tag Out")
                && EngagedOwnDice(state, botId).Where(d => pending.CandidateIds.Contains(d.Id)).OrderByDescending(d => DieValue(state, d)).FirstOrDefault() is { } tagged:
                return new(BotActionKind.ResolvePendingChoice, "Tag Out: boost the fighter.") { DieIds = [tagged.Id] };

            // Sacrifice (Army Ant): only for a trade that comes out ahead -
            // a creature's ATK must KO an opposing creature worth more.
            case MayPay { Cost: Sacrifice }:
                var plan = SacrificePlan(state, botId);
                return new(BotActionKind.ResolvePendingChoice, plan is null ? "No good sacrifice." : "Sacrifice for a KO.")
                    { DieIds = plan is null ? [] : [pending.CandidateIds[0]] };
            case Sacrifice when SacrificePlan(state, botId) is { } sacrifice && pending.CandidateIds.Contains(sacrifice.Fodder.Id):
                return new(BotActionKind.ResolvePendingChoice, "Sacrifice the cheap one.") { DieIds = [sacrifice.Fodder.Id] };
            case DealDamage { Amount: StatOf { Binding: "sacrificed" } } when pending.DamageHint is { } hit:
                var victim = pending.CandidateIds.Where(id => !state.IsPlayerId(id)).Select(id => state.Dice.First(d => d.Id == id))
                    .Where(d => d.ControllerId != botId)
                    .OrderByDescending(d => QueryEngine.GetDefense(state, d) - d.Damage <= hit ? 1 : 0).ThenByDescending(d => DieValue(state, d))
                    .FirstOrDefault();
                if (victim is not null)
                    return new(BotActionKind.ResolvePendingChoice, "Sacrifice damage.") { DieIds = [victim.Id] };
                break;

            case MayPay offer when offer == KeywordAbilities.InfiltrateChoice:
                var infiltrator = state.Dice.First(d => d.Id == pending.CandidateIds[0]);
                var oppLife = state.GetPlayer(state.OpponentOf(botId)).Life;
                var atk = QueryEngine.GetAttack(state, infiltrator);
                var goHome = oppLife <= 1 || (atk < oppLife && atk <= 2);
                return new(BotActionKind.ResolvePendingChoice, goHome ? "Infiltrate - back to the Field." : "Infiltrate declined - full hit.")
                    { DieIds = goHome ? [infiltrator.Id] : [] };
            case GrantTag { Tags.Count: 0 } when BestFight(state, botId) is { } fight && pending.CandidateIds.Contains(fight.Mine.Id):
                return new(BotActionKind.ResolvePendingChoice, "Archnemesis: my fighter.") { DieIds = [fight.Mine.Id] };
            case DealDamage { Amount: StatOf } when BestFight(state, botId) is { } fight && pending.CandidateIds.Contains(fight.Theirs.Id):
                return new(BotActionKind.ResolvePendingChoice, "Archnemesis: their fighter.") { DieIds = [fight.Theirs.Id] };
            case ModifyStat { SetDefense: not null }:
                var pick = pending.CandidateIds.Where(id => !state.IsPlayerId(id))
                    .OrderByDescending(id => SetDefenseGain(state, botId, state.Dice.First(d => d.Id == id))
                        + (state.Dice.First(d => d.Id == id).Zone == Zone.AttackZone ? 1 : 0))
                    .FirstOrDefault();
                if (pick is not null)
                    return new(BotActionKind.ResolvePendingChoice, "D becomes A.") { DieIds = [pick] };
                break;
        }

        var ranked = pending.CandidateIds.Select(id => (Id: id, Score: Score(id))).OrderByDescending(x => x.Score).ToList();
        var picks = ranked.Take(pending.MinCount).Select(x => x.Id).ToList();
        picks.AddRange(ranked.Skip(pending.MinCount).Take(pending.MaxCount - pending.MinCount).Where(x => x.Score > 0).Select(x => x.Id));
        return new(BotActionKind.ResolvePendingChoice, $"Answer: {pending.Description}") { DieIds = picks };
    }

    // This player's dice in a fight right now: attackers in a blocked lane,
    // or blockers.
    private static IEnumerable<DieInstance> EngagedOwnDice(GameState state, string botId)
    {
        if (state.DeclaredBlocks is not { } blocks) return [];
        var attackers = state.DiceIn(state.ActivePlayerId, Zone.AttackZone).ToList();
        var blockedLanes = attackers.Where(a => blocks.BlockersOf(a.Id).Count > 0).Select(a => a.Lane).ToHashSet();
        return state.DiceIn(botId, Zone.AttackZone)
            .Where(d => d.ControllerId != state.ActivePlayerId || blockedLanes.Contains(d.Lane));
    }

    // Best Sacrifice trade: one of this player's creatures whose ATK KOs
    // an opposing creature worth clearly more than it.
    private static (DieInstance Fodder, DieInstance Victim)? SacrificePlan(GameState state, string botId)
    {
        var oppId = state.OpponentOf(botId);
        var best = ((DieInstance, DieInstance)?)null;
        var bestGain = 1.0;
        foreach (var fodder in state.DiceIn(botId, Zone.FieldZone).Where(d => state.GetCurrentFace(d)?.Character is not null))
        {
            var atk = QueryEngine.GetAttack(state, fodder);
            foreach (var victim in state.DiceIn(oppId, Zone.FieldZone).Concat(state.DiceIn(oppId, Zone.AttackZone)))
            {
                if (state.GetCurrentFace(victim)?.Character is null || QueryEngine.GetDefense(state, victim) - victim.Damage > atk) continue;
                var gain = DieValue(state, victim) - DieValue(state, fodder);
                if (gain > bestGain) { bestGain = gain; best = (fodder, victim); }
            }
        }
        return best;
    }

    // How much locking `ownerId` out of a card (Blob/Drax-style, the
    // NameCard choice) would cost them. Mostly the copies they already OWN
    // that are coming back around to be fielded - their workhorse, what's
    // actually hurting you - with still-unbought copies a minor, maybe-
    // someday factor. Weighting by purchase cost alone named an unbought
    // finisher nearly every time, and finishers land in ~15% of games, so
    // that lockout almost never mattered (simulator, 2026-09-28).
    private static double CardThreat(GameState state, string ownerId, string cardId)
    {
        var dice = state.Dice.Where(d => d.OwnerId == ownerId && d.CardId == cardId).ToList();
        var circulating = dice.Count(d => d.Zone is Zone.Bag or Zone.UsedPile or Zone.PrepArea or Zone.ReservePool or Zone.OutOfPlay);
        var unbought = dice.Count(d => d.Zone == Zone.Unpurchased);
        var cost = state.CardCatalog[cardId].PurchaseCost;
        return circulating * (cost + 2) + unbought * 0.3 * cost + 0.1;
    }

    // A die in play is worth its stats; one out of play (Used Pile, Bag,
    // Prep) is worth what its card cost.
    private static double ChoiceValue(GameState state, DieInstance die)
    {
        if (die.Zone is Zone.FieldZone or Zone.AttackZone && state.GetCurrentFace(die)?.Character is not null)
            return DieValue(state, die);
        return die.CardId is { } cardId ? state.CardCatalog[cardId].PurchaseCost : 0.5;
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    private static double DieValue(GameState state, DieInstance die)
    {
        if (state.GetCurrentFace(die)?.Character is null) return die.CardId is { } c ? state.CardCatalog[c].PurchaseCost * 0.5 : 0.5;
        var value = QueryEngine.GetAttack(state, die) * 1.2 + (QueryEngine.GetDefense(state, die) - die.Damage) * 0.8;
        if (die.CardId is { } cardId)
        {
            var card = state.CardCatalog[cardId];
            value += 1 + (card.Abilities.Count > 0 || card.Continuous.Count > 0 || card.Keywords.Count > 0 ? 1 : 0);
        }
        return value;
    }

    private static IEnumerable<DieInstance> PotentialAttackers(GameState state, string playerId) =>
        state.DiceIn(playerId, Zone.FieldZone)
            .Where(d => state.GetCurrentFace(d)?.Character is not null)
            .Where(d => !d.CombatFlags.Contains(CombatFlagKind.CantAttack) && !d.CombatFlags.Contains(CombatFlagKind.OnlyBlocker))
            .Where(d => QueryEngine.GetAttack(state, d) > 0);

    private static IEnumerable<DieInstance> PotentialBlockers(GameState state, string playerId) =>
        state.DiceIn(playerId, Zone.FieldZone)
            .Where(d => state.GetCurrentFace(d)?.Character is not null && !d.CombatFlags.Contains(CombatFlagKind.CantBlock));

    // "Whenever this takes damage, deal that much to the opponent"
    // (Rhinoceros) - read off the card's abilities, not its name, so any
    // future retaliator is played the same way.
    private static bool IsRetaliator(GameState state, DieInstance die) =>
        QueryEngine.AbilitiesOf(state, die).Any(a => a.Trigger == TriggerKind.DieDamaged
            && a.Effect is DealDamage { Target: { Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing } });

    private static bool IsCharacterCard(GameState state, DieInstance die) =>
        die.CardId is { } id && state.CardCatalog[id].CardType == CardType.Character;

    private static bool IsActionFace(GameState state, DieInstance die) =>
        die.CardId is { } id && state.CardCatalog[id].CardType.IsActionDie() && state.GetCurrentFace(die)?.Kind == FaceKind.ActionFace;

    // Rule 2.6.4.6 - TurnEngine.UseAction refuses an Action die that would do nothing.
    private static bool ActionCouldHaveResult(GameState state, DieInstance die) =>
        state.CardCatalog[die.CardId!].Abilities.Where(a => a.Trigger == TriggerKind.DieUsed)
            .Any(a => AbilityPreview.CouldHaveResult(state, die.ControllerId, a.Effect, ProtectionFrom.Action));

    private static bool IsCombatPump(GameState state, DieInstance die) =>
        state.CardCatalog[die.CardId!].Abilities.FirstOrDefault(a => a.Trigger == TriggerKind.DieUsed)?.Effect switch
        {
            ModifyStat => true,
            Sequence s => s.Steps.Any(x => x is ModifyStat),
            _ => false,
        };

    private static string CardName(GameState state, DieInstance die) =>
        die.CardId is { } id ? state.CardCatalog[id].Name : "a Tardigrade";
}
