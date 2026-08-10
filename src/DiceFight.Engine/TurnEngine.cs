using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;

namespace DiceFight.Engine;

// The result of rolling a single die (rule 1.6 - Rolled vs. Unrolled Dice).
// IDiceRoller exists so the turn engine's zone/step mechanics can be built
// and tested independent of where face results come from (a weighted roll
// against a rules-accurate default face composition - see
// DiceFight.Api.PlaceholderDiceRoller - a real per-card face table if one
// is ever sourced, or a human reporting a physical die). EnergyKind/
// ProvidedEnergyType/EnergyAmount only matter when Status is Energy - the
// roller decides them as part of the face itself (which specific energy
// face was rolled, and how much it's worth, is a physical fact about the
// die, same as Status/Level), rather than TurnEngine inferring them from
// the die's card afterward.
public readonly record struct RolledFace(
    DieStatus Status,
    int Level,
    EnergyKind EnergyKind = EnergyKind.None,
    EnergyType? ProvidedEnergyType = null,
    int EnergyAmount = 1,
    // Only meaningful when Status == Action - a Basic Action/Action die's
    // face is one of blank/single-/double-burst (see DieInstance.
    // BurstStars's own remarks for why this needs to persist per-die
    // rather than being derived, unlike a Character die's burst symbol).
    int? BurstStars = null);

public interface IDiceRoller
{
    RolledFace Roll(DieInstance die, CardDef? card);
}

public static class TurnEngine
{
    private static readonly TurnStep[] StepOrder =
        [TurnStep.ClearAndDraw, TurnStep.RollAndReroll, TurnStep.Main, TurnStep.Attack, TurnStep.CleanUp];

    // Keyword Attune's own built-in effect (Appendix 1) - identical on
    // every Attune card, so it's one shared constant rather than
    // per-CardDef authored text. See UseActionDie.
    private static readonly EffectNode AttuneDamage =
        new DealDamage(1, TargetSpec.CharacterDieOrPlayer("target player or character die"));

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
    // queue is optional - most call sites (every test that isn't
    // exercising a WhenDrawn card) have no use for it and shouldn't have
    // to construct/drain one just to call this. When supplied, each
    // successfully-drawn die is checked for a WhenDrawn ability (e.g.
    // Cosmic Cube's "Infinite Possibilities" printing) - see
    // EffectNode.RedrawFromBag's remarks for why its own replacement
    // draws land back in DiceFromBag rather than rolling immediately.
    public static void ClearAndDraw(GameState state, Random random, AbilityQueue? queue = null)
    {
        if (state.CurrentStep != TurnStep.ClearAndDraw)
            throw new InvalidOperationException($"Expected ClearAndDraw step, was {state.CurrentStep}.");

        var activeId = state.ActivePlayerId;

        // Rule 2.3.1 - clear the Reserve Pool (unspent energy from the
        // opponent's turn) to the Used Pile.
        foreach (var die in state.DiceIn(activeId, Zone.ReservePool).ToList())
        {
            die.Zone = Zone.UsedPile;
            die.ResetToUnrolled();
        }

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

        // Keyword Swarm - "While a Character die with Swarm is active,
        // and you draw another copy of that die from your bag during
        // your Clear and Draw Step, draw an extra die from your bag and
        // add it to your Roll and Reroll." The match is on card identity
        // (CardId) - "another copy of that die" - not on any rolled face:
        // dice sitting in DiceFromBag right now are still unrolled (Roll
        // happens later), so there's no face to compare in the first
        // place, and the rule's own wording ("copy of that die") is about
        // which character it is, not what it shows. (1)/(4): one trigger
        // per *drawn* die matching some active Swarm die's card, no
        // matter how many active copies of that card exist - checking
        // per drawn die rather than per active die gets this right without
        // extra bookkeeping. (3): checked once against this original
        // batch, not re-checked against its own bonus draws below, so
        // Swarm can't chain off itself.
        //
        // A real physical Sidekick's "CardId" is null (rule 1.3.9), and
        // that's deliberately kept as a real set entry here rather than
        // filtered out: Darkseid-style "your Sidekicks gain Swarm" grants
        // can reach a real Sidekick (whose identity really is just
        // "a Sidekick," fungible with every other one) as well as an
        // active Ally die (which counts as a Sidekick per DieStats.
        // CountsAsSidekick, but keeps its own real CardId for this
        // match - an active granted-Swarm Ally does NOT match a drawn
        // plain Sidekick, or vice versa, only another copy of itself).
        var activeSwarmCardIds = state.DiceIn(activeId, Zone.FieldZone)
            .Concat(state.DiceIn(activeId, Zone.AttackZone))
            .Where(d => DieStats.HasKeyword(state, d, "Swarm"))
            .Select(d => d.VirtualCardId ?? d.CardId)
            .ToHashSet();

        var swarmBonusDice = new List<DieInstance>();
        if (activeSwarmCardIds.Count > 0)
        {
            var swarmTriggerCount = drawn.Count(d => activeSwarmCardIds.Contains(d.VirtualCardId ?? d.CardId));
            // (2) - a bonus pull that comes up empty is not a shortfall
            // (no life loss/virtual energy); DrawFromBag already just
            // stops short rather than throwing, so this needs no special
            // casing beyond simply not folding it into `drawn` below.
            for (var i = 0; i < swarmTriggerCount; i++)
                swarmBonusDice.AddRange(DrawFromBag(state, activeId, 1, random));
        }

        if (queue is not null)
        {
            foreach (var die in drawn.Concat(swarmBonusDice))
                EnqueueTriggered(state, queue, die, TriggerType.WhenDrawn);

            // Bespoke text like Rip Hunter's "Navigate the Sands of Time"
            // printing - see TriggerType.ClearAndDraw's own remarks. Fires
            // once per unique active card (deduped by CardId, same as
            // Teamwatch), regardless of whether that card's own dice were
            // drawn this turn - this is a "while active" condition on the
            // step itself, not a per-drawn-die reaction like WhenDrawn above.
            foreach (var reactor in state.DiceIn(activeId, Zone.FieldZone)
                .Concat(state.DiceIn(activeId, Zone.AttackZone))
                .Where(d => d.CardId is not null)
                .GroupBy(d => d.VirtualCardId ?? d.CardId)
                .Select(g => g.First()))
            {
                EnqueueTriggered(state, queue, reactor, TriggerType.ClearAndDraw);
            }
        }

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
            state.GetPlayer(activeId).Life -= shortfall;
            AddVirtualGenericEnergy(state, activeId, shortfall);
        }
    }

    // Rule 2.3.2/2.3.5-2.3.9 - draw randomly from the bag, refilling once
    // from the Used Pile if the bag runs dry mid-draw; stops short (rather
    // than throwing) if there is truly nothing left to draw. Internal (not
    // private) so EffectInterpreter's Corrupt case can reuse the exact
    // same refill behavior for its own "draws X dice from their bag
    // (refilling from the Used Pile if necessary)" text rather than
    // re-implementing it. random is nullable there only because
    // EffectContext.Random is - always non-null for ClearAndDraw's own
    // call, which picks the first bag die instead of a random one on the
    // (never-exercised-in-practice) null path, same fallback DrawDice uses.
    internal static List<DieInstance> DrawFromBag(GameState state, string playerId, int count, Random? random)
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

            var picked = random is not null ? bag[random.Next(bag.Count)] : bag[0];
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
    public static void Reroll(GameState state, AbilityQueue queue, IDiceRoller roller, IReadOnlyList<string> rerollDieIds)
    {
        if (state.CurrentStep != TurnStep.RollAndReroll)
            throw new InvalidOperationException($"Expected RollAndReroll step, was {state.CurrentStep}.");

        var rerollSet = rerollDieIds.ToHashSet();
        foreach (var die in state.DiceIn(state.ActivePlayerId, Zone.ReservePool).Where(d => rerollSet.Contains(d.Id)))
            ApplyRoll(state, roller, die);

        // Keyword Energize - checked once against the Roll and Reroll
        // step's *final* state, not the initial roll: a die rerolled off
        // double energy never triggers it, but a die left alone on double
        // energy (whether that was its initial roll or this reroll) does,
        // right as the reroll decision (and so the step) closes.
        foreach (var die in state.DiceIn(state.ActivePlayerId, Zone.ReservePool))
            CheckEnergize(state, queue, die);

        // Rule 2.4.3/2.4.4 - the reroll decision (any/some/none of the
        // rolled dice) is made once, after seeing Roll's results; once
        // it's made there's nothing else legal in this step, so this
        // advances straight to Main rather than leaving a redundant click.
        AdvanceStep(state);
    }

    // Keyword Energize - fires whenever an Energize die lands on a
    // double-energy face from any roll, with one carve-out: the Roll and
    // Reroll step's own initial roll doesn't check here (see Reroll, which
    // checks once at the end of that step instead) because the reroll
    // decision hasn't been made yet - a die that's about to be rerolled off
    // double energy never actually triggered it. Every other roll (a
    // DrawDice-style ability rolling a fresh die mid-Main-Step, etc.) checks
    // immediately, since there's no equivalent "decision pending" window.
    internal static void CheckEnergize(GameState state, AbilityQueue queue, DieInstance die)
    {
        if (die.Status == DieStatus.Energy && die.EnergyAmount >= 2 && DieStats.HasKeyword(state, die, "Energize"))
            EnqueueTriggered(state, queue, die, TriggerType.Energize);
    }

    // Keyword Awaken - "When a Character die with Awaken spins up 1 or
    // more levels, you may use its Awaken ability." Takes the spin's
    // *actual* level delta (DieStats.SpinLevel's return value), not the
    // requested one - a spin "if able" that couldn't actually move a
    // maxed-out die doesn't count. Fires from every spin-up source alike
    // (Amplify above, or EffectInterpreter's Spin case for an ability-
    // driven spin), all funneled through this one check so Awaken can't
    // silently miss a source some future keyword adds.
    internal static void CheckAwaken(GameState state, AbilityQueue queue, DieInstance die, int actualLevelDelta)
    {
        if (actualLevelDelta > 0 && DieStats.HasKeyword(state, die, "Awaken"))
            EnqueueTriggered(state, queue, die, TriggerType.Awaken);
    }

    private static void ApplyRoll(GameState state, IDiceRoller roller, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
        var result = roller.Roll(die, card);
        die.Status = result.Status;
        die.Level = result.Level;

        if (die.Status == DieStatus.Energy)
        {
            // Rule 1.3.10/1.4.2 - what an energy face provides is a
            // property of which specific face got rolled, decided by the
            // roller (see RolledFace remarks), not inferred here from the
            // die's card.
            die.EnergyKind = result.EnergyKind;
            die.ProvidedEnergyType = result.ProvidedEnergyType;
            die.EnergyAmount = result.EnergyAmount;
        }
        else
        {
            die.EnergyKind = EnergyKind.None;
            die.ProvidedEnergyType = null;
            die.EnergyAmount = 1;
        }

        die.BurstStars = die.Status == DieStatus.Action ? result.BurstStars : null;
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
            throw new InvalidOperationException($"{DisplayName(state, die)} is not available to purchase.");

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

        // Dark Phoenix/Magik's own "next die/action die you purchase this
        // turn costs N less" (GameState.PendingPurchaseDiscount) - only
        // consumed once a purchase actually matches RequiredType (or any
        // purchase at all, if null); an unmatched purchase leaves it
        // pending for a later one this same turn.
        var effectiveCost = card.PurchaseCost;
        if (state.PendingPurchaseDiscount is { } discount &&
            (discount.RequiredType is null || discount.RequiredType == card.Type))
        {
            effectiveCost = Math.Max(1, effectiveCost - discount.Amount);
            state.PendingPurchaseDiscount = null;
        }

        // Dark Phoenix ("Malevolent", DPS027) - "costs 1 less to purchase
        // if your opponent has an X-Men character on their team." A
        // SELF-referential discount checked against the card being
        // purchased directly, no granter scan (see the field's own
        // remarks).
        if (card.GrantsSelfPurchaseDiscountIfOpponentHasAffiliation is { } selfDiscount)
        {
            var opponentHasIt = state.GetPlayer(state.OpponentOf(state.ActivePlayerId)).TeamCardIds.Any(id =>
                state.CardCatalog.TryGetValue(id, out var opponentCard) &&
                opponentCard.Affiliations.Contains(selfDiscount.OpponentAffiliation));
            if (opponentHasIt) effectiveCost = Math.Max(1, effectiveCost - selfDiscount.Amount);
        }

        // Beast ("Combat Ready", DPS098) - "the first Beast die you
        // purchase each game costs 1 extra." SELF-referential surcharge,
        // consumed exactly once per game per card - see Player.
        // SurchargedFirstPurchaseCardIds's own remarks. Checked (not
        // consumed) here, before payment is validated; only actually
        // recorded once the purchase below really succeeds, same
        // "don't burn a one-shot on a rejected attempt" reasoning as
        // OncePerTurn Globals.
        var isFirstEverPurchaseOfThisCard = card.SelfFirstPurchaseSurcharge is not null &&
            !state.GetPlayer(state.ActivePlayerId).SurchargedFirstPurchaseCardIds.Contains(card.Id);
        if (isFirstEverPurchaseOfThisCard) effectiveCost += card.SelfFirstPurchaseSurcharge!.Value;

        // Cable ("Bosom Buddies", DPS062) - "your Deadpool costs 1 less
        // to purchase (to a minimum of 1)." Continuous (active-granter
        // scan), unlike PendingPurchaseDiscount above (one-shot,
        // consumed) - re-checked fresh every purchase, same shape as
        // every other Grants*-while-active field.
        var activeGranters = state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Concat(state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
            .Select(d => DieStats.GetCard(state, d))
            .Where(c => c is not null)
            .Distinct();
        foreach (var granter in activeGranters)
        {
            if (granter!.GrantsNamedCardSupport is { PurchaseDiscount: > 0 } support && support.CardName == card.Name)
                effectiveCost = Math.Max(1, effectiveCost - support.PurchaseDiscount);
        }

        // Forge ("Support Technician", DPS071) - "your opponents must
        // pay 1 more to purchase a die with purchase cost of 2 or less."
        // Scanned against the PURCHASER's opponent's active dice (the
        // granter's own side), same cross-player shape as
        // GrantsOpponentStatDebuff. Applied after any discounts above,
        // same order real rules text implies ("costs 1 less" and "pay 1
        // more" are independent modifiers, not one overriding the other).
        var opponentGranters = state.DiceIn(state.OpponentOf(state.ActivePlayerId), Zone.FieldZone)
            .Concat(state.DiceIn(state.OpponentOf(state.ActivePlayerId), Zone.AttackZone))
            .Select(d => DieStats.GetCard(state, d))
            .Where(c => c is not null)
            .Distinct();
        foreach (var granter in opponentGranters)
        {
            if (granter!.GrantsOpponentPurchaseSurcharge is { } surcharge &&
                (surcharge.MaxPurchaseCost is null || card.PurchaseCost <= surcharge.MaxPurchaseCost))
                effectiveCost += surcharge.Amount;
        }

        var energyDice = energyDieIdsToSpend.Select(id => FindEnergyDie(state, id)).ToList();
        SpendEnergy(
            state, state.ActivePlayerId, energyDice, effectiveCost, card.EnergyTypes, Zone.OutOfPlay,
            () => $"Not enough energy offered to purchase {card.Name} (needs {effectiveCost}).",
            requiredType => $"Purchasing {card.Name} requires at least one {requiredType} energy.");

        if (isFirstEverPurchaseOfThisCard) state.GetPlayer(state.ActivePlayerId).SurchargedFirstPurchaseCardIds.Add(card.Id);

        // Rule 1.1.4 - the purchaser becomes controller; OwnerId (whoever
        // brought the card) is untouched, which matters for community
        // Basic Actions bought by the non-bringing player.
        die.ControllerId = state.ActivePlayerId;

        // Corsair ("Recruiting a Crew", DPS024) - GameState.
        // PendingNextPurchaseGoesToBag overrides rule 2.6.2.6's normal
        // Used Pile destination for the very next purchase, then clears.
        if (state.PendingNextPurchaseGoesToBag)
        {
            die.Zone = Zone.Bag;
            state.PendingNextPurchaseGoesToBag = false;
        }
        else
        {
            die.Zone = Zone.UsedPile; // rule 2.6.2.6
        }

        var purchaser = state.GetPlayer(state.ActivePlayerId);
        purchaser.PurchasedDieThisTurn = true;
        if (card.Type == CardType.Character) purchaser.PurchasedCharacterDieThisTurn = true;
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
            throw new InvalidOperationException($"{DisplayName(state, die)} cannot be fielded from its current state.");
        if (die.Status is not (DieStatus.Character or DieStatus.SidekickCharacter))
            throw new InvalidOperationException($"{DisplayName(state, die)} is not on a character face.");

        // Gambit ("I Like Solitaire", DPS072) - "you may not field any
        // more character dice this turn."
        if (state.CantFieldCharacterDiceThisTurn.Contains(die.ControllerId))
            throw new InvalidOperationException($"{DisplayName(state, die)}'s controller can't field any more character dice this turn.");

        var fieldingCost = DieStats.GetFace(state, die).FieldingCost;
        if (IsFreeToField(state, die, fieldingCost)) fieldingCost = 0;
        else fieldingCost = Math.Max(0, fieldingCost - FieldingCostReductionFor(state, die));
        var energyDice = energyDieIdsToSpend.Select(id => FindEnergyDie(state, id)).ToList();
        SpendEnergy(
            state, state.ActivePlayerId, energyDice, fieldingCost, [], Zone.OutOfPlay,
            () => $"Not enough energy offered to field {DisplayName(state, die)} (needs {fieldingCost}).");

        die.Zone = Zone.FieldZone;
        state.FieldedThisTurn.Add(die.Id); // keyword Strike's own "fielded this turn" check

        // Rule 2.6.3.6 - "when fielded" fires immediately upon entering the Field Zone.
        EnqueueTriggered(state, queue, die, TriggerType.WhenFielded);

        // Keyword Teamwatch - "When a character with Teamwatch is active
        // and you field a different Character die with the same
        // affiliation, use their Teamwatch ability." Scans the SAME
        // player's own active Teamwatch holders (fielding is always the
        // active player's own action), deduplicated by CardId so multiple
        // copies of the same Teamwatch character only trigger once
        // (clarification 1 - "counts different active characters, not
        // dice," the same shape as Retaliation's own dedup). "Different"
        // excludes both the fielded die matching the Teamwatch holder's
        // own card and a Sidekick being fielded (CardId is null, so it
        // has no affiliations to share with anything).
        var fieldedCardId = die.VirtualCardId ?? die.CardId;
        if (fieldedCardId is not null && state.CardCatalog.TryGetValue(fieldedCardId, out var fieldedCard))
        {
            var teamwatchers = state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
                .Concat(state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
                .Where(d => DieStats.HasKeyword(state, d, "Teamwatch"))
                .GroupBy(d => d.VirtualCardId ?? d.CardId)
                .Select(g => g.First());

            foreach (var teamwatcher in teamwatchers)
            {
                var teamwatcherCardId = teamwatcher.VirtualCardId ?? teamwatcher.CardId;
                if (teamwatcherCardId is null || teamwatcherCardId == fieldedCardId) continue;
                if (!state.CardCatalog.TryGetValue(teamwatcherCardId, out var teamwatcherCard)) continue;
                if (!teamwatcherCard.Affiliations.Any(fieldedCard.Affiliations.Contains)) continue;

                EnqueueTriggered(state, queue, teamwatcher, TriggerType.Teamwatch);
            }
        }

        ResolveWhenAnotherDieFielded(state, queue, die);
    }

    // Deadpool ("Collect THIS!", DPS108)/Mystique ("Taught by Magneto",
    // DPS125) - "your character dice with fielding cost of 2/[a given
    // affiliation] are free to field." Same granter-side scan shape as
    // DieStats.StaticTeamBonusFor/GrantsToSidekicks (the controller's own
    // active dice, deduplicated by CardId isn't needed here since this is
    // a plain pass/fail, not a stacking numeric bonus), just checked once
    // at Field time against CardDef.GrantsFreeFielding instead of applied
    // to a running stat total.
    private static bool IsFreeToField(GameState state, DieInstance die, int fieldingCost)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        if (cardId is null || !state.CardCatalog.TryGetValue(cardId, out var card)) return false;

        // Wolverine ("Pure of Heart", DPS056) - "if you have no Villains
        // character dice on your team, Wolverine is free to field." A
        // SELF-referential check against the controller's own roster
        // (Player.TeamCardIds), not a granter scan - the die being
        // fielded isn't active yet, so it couldn't participate in one.
        if (card.SelfFreeFieldingUnlessTeamHasAffiliation is { } excludedAffiliation)
        {
            var teamHasIt = state.GetPlayer(die.ControllerId).TeamCardIds.Any(id =>
                state.CardCatalog.TryGetValue(id, out var teamCard) && teamCard.Affiliations.Contains(excludedAffiliation));
            if (!teamHasIt) return true;
        }

        // Jean Grey ("Marvel Girl", DPS115) - "while you have a
        // different X-Men character die in your Field Zone, Jean Grey is
        // free to field." The board-state counterpart to the roster
        // check above - "different" is automatic here since the die
        // being fielded isn't in the Field/Attack Zone yet.
        if (card.SelfFreeFieldingWhileOtherActiveAffiliation is { } requiredAffiliation)
        {
            var hasOtherActive = state.DiceIn(die.ControllerId, Zone.FieldZone)
                .Concat(state.DiceIn(die.ControllerId, Zone.AttackZone))
                .Any(d => DieStats.HasAffiliation(state, d, requiredAffiliation));
            if (hasOtherActive) return true;
        }

        var granterCards = state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Concat(state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
            .Select(d => DieStats.GetCard(state, d))
            .Where(c => c is not null)
            .Distinct();

        foreach (var granterCard in granterCards)
        {
            if (granterCard!.GrantsFreeFielding is not { } grant) continue;
            if (grant.RequiredAffiliation is { } affiliation && !card.Affiliations.Contains(affiliation)) continue;
            if (grant.MaxFieldingCost is { } maxCost && fieldingCost > maxCost) continue;
            return true;
        }
        return false;
    }

    // Rogue ("Unity Squad", DPS129) - "your X-Men character dice cost 1
    // less to field." Same active-granter scan shape as IsFreeToField
    // just above, a partial reduction instead of an all-the-way-to-zero
    // pass/fail - summed across multiple distinct granters, same as
    // every other numeric Grants* scan in this file.
    private static int FieldingCostReductionFor(GameState state, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        if (cardId is null || !state.CardCatalog.TryGetValue(cardId, out var card)) return 0;

        var granterCards = state.DiceIn(die.ControllerId, Zone.FieldZone)
            .Concat(state.DiceIn(die.ControllerId, Zone.AttackZone))
            .Select(d => DieStats.GetCard(state, d))
            .Where(c => c is not null)
            .Distinct();

        var reduction = 0;
        foreach (var granterCard in granterCards)
        {
            if (granterCard!.GrantsFieldingCostReduction is not { } grant) continue;
            if (grant.RequiredAffiliation is { } affiliation && !card.Affiliations.Contains(affiliation)) continue;
            reduction += grant.Amount;
        }
        return reduction;
    }

    // TriggerType.WhenAnotherDieFielded - same shape as
    // ResolveWhenAnotherDieKOd, just reacting to a fielding instead of a
    // KO. Scans EVERY active die on the board, not just the fielded die's
    // own controller's - FieldedDieMatch.Ownership expresses "must share
    // the fielded die's controller" per-card instead, same reasoning
    // WhenAnotherDieKOd's own remarks give (not every card with this
    // trigger needs that restriction, even though both currently-authored
    // users happen to want Own).
    private static void ResolveWhenAnotherDieFielded(GameState state, AbilityQueue queue, DieInstance fieldedDie)
    {
        var fieldedCardId = fieldedDie.VirtualCardId ?? fieldedDie.CardId;
        var fieldedCard = fieldedCardId is not null ? state.CardCatalog.GetValueOrDefault(fieldedCardId) : null;

        foreach (var reactor in state.Dice.Where(d => d.Zone is Zone.FieldZone or Zone.AttackZone).ToList())
        {
            // DieStats.GetCard, not a raw lookup - a blanked reactor's
            // own reactive abilities don't fire either.
            if (DieStats.GetCard(state, reactor) is not { } reactorCard) continue;

            foreach (var ability in reactorCard.Abilities.Where(a => a.Trigger == TriggerType.WhenAnotherDieFielded))
            {
                var filter = ability.FieldedFilter
                    ?? throw new InvalidOperationException($"{reactorCard.Name}'s WhenAnotherDieFielded ability has no FieldedFilter.");
                if (!MatchesFieldedFilter(state, filter, fieldedDie, fieldedCard, reactor)) continue;

                queue.Enqueue(reactor.Id, reactor.ControllerId, TriggerType.WhenAnotherDieFielded, ability.Effect);
            }
        }
    }

    private static bool MatchesFieldedFilter(
        GameState state, FieldedDieMatch filter, DieInstance fieldedDie, CardDef? fieldedCard, DieInstance reactor)
    {
        if (filter.ExcludeSelf && fieldedDie.Id == reactor.Id) return false;
        if (filter.Ownership == TargetOwnership.Own && fieldedDie.ControllerId != reactor.ControllerId) return false;
        if (filter.Ownership == TargetOwnership.Opposing && fieldedDie.ControllerId == reactor.ControllerId) return false;
        if (filter.RequiredKeyword is { } keyword && !DieStats.HasKeyword(state, fieldedDie, keyword)) return false;
        if (filter.AffiliationContains is { } affiliation &&
            (fieldedCard is null || !fieldedCard.Affiliations.Contains(affiliation))) return false;
        if (filter.MinPurchaseCost is { } minCost && (fieldedCard is null || fieldedCard.PurchaseCost < minCost)) return false;
        return true;
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
            throw new InvalidOperationException($"{DisplayName(state, die)} is not an eligible Action die.");

        var cardId = die.VirtualCardId ?? die.CardId;
        var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
        var isContinuous = DieStats.HasKeyword(state, die, "Continuous");

        // Rule 2.6.4.2 - a Continuous Action die's own ability does NOT
        // run here; moving it to the Field Zone IS "using" it (see below),
        // but the ability itself only resolves later, when its controller
        // chooses to remove it (ResolveContinuousDie). Every other Action
        // die's ability is initiated (queued) before the die's post-use
        // zone move; Shocking Grasp's own "you may Prep this die" effect,
        // for example, overrides the default destination below.
        if (!isContinuous)
            EnqueueTriggered(state, queue, die, TriggerType.WhenUsed);

        // Keyword Amplify - "When you use an Action die, spin each
        // Character die with Amplify up one level (if able)." Any Action
        // die triggers it, not just the Amplify die's own - this is a
        // keyword reacting to the controller's own Action-die usage in
        // general, unlike WhenUsed above (which is that specific die's
        // own ability). "If able" is exactly SpinLevel's clamp behavior:
        // a die already at max level is silently unaffected.
        foreach (var amplified in state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Concat(state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
            .Where(d => DieStats.HasKeyword(state, d, "Amplify"))
            .ToList())
        {
            var actualDelta = DieStats.SpinLevel(state, amplified, +1);
            CheckAwaken(state, queue, amplified, actualDelta);
        }

        // Keyword Attune - "While a Character die you control with Attune
        // is active, when you use an Action die, that character deals 1
        // damage to target player or Character die (no matter how many of
        // that Character's dice are active)." Each active Attune die
        // triggers its OWN instance (rule's example: two active dice from
        // the same Character means two separate Attune uses, each
        // independently targeted) - the 1-damage effect is the keyword's
        // own built-in behavior, identical on every card, so it's injected
        // here rather than authored per-CardDef; EnqueueTriggered still
        // runs too, for any card-specific text layered on top of "when you
        // use Attune" (e.g. Wasp's own stat-boost follow-up).
        foreach (var attuner in state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Concat(state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
            .Where(d => DieStats.HasKeyword(state, d, "Attune"))
            .ToList())
        {
            queue.Enqueue(attuner.Id, attuner.ControllerId, TriggerType.Attune, AttuneDamage);
            EnqueueTriggered(state, queue, attuner, TriggerType.Attune);
        }

        // Keyword Obscure - "When you use an Action die, all dice from the
        // applicable Character card are unblockable until end of turn."
        // Same "any Action die triggers it" shape as Amplify above (not
        // just the Obscure die's own use). The effect is recorded by
        // CardId, not die id, since it covers every die from that card -
        // including ones not currently active - and is consumed by
        // CombatEngine.DeclareBlockers/ActiveCallOutTargets; cleared each
        // turn in CleanUp.
        foreach (var obscurer in state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Concat(state.DiceIn(state.ActivePlayerId, Zone.AttackZone))
            .Where(d => DieStats.HasKeyword(state, d, "Obscure")))
        {
            var obscuredCardId = obscurer.VirtualCardId ?? obscurer.CardId;
            if (obscuredCardId is not null) state.ObscuredCardIds.Add(obscuredCardId);
        }

        if (isContinuous)
        {
            die.Zone = Zone.FieldZone; // rule 2.6.4.2
        }
        else if (card?.Type == CardType.EpicBasicAction)
        {
            // Rule 1.2.3(2)/(3) - returns to its card instead of Out of
            // Play, and only one Epic Basic Action die may be used per turn.
            if (state.EpicBasicActionUsedThisTurn)
                throw new InvalidOperationException("Only one Epic Basic Action die may be used per turn.");
            state.EpicBasicActionUsedThisTurn = true;
            die.Zone = Zone.Unpurchased;
            die.ResetToUnrolled();
        }
        else
        {
            die.Zone = Zone.OutOfPlay; // rule 2.6.4.1
        }
    }

    // Rule 2.6.4.2/2.6.4.3 and Appendix 1's Continuous entry - the second
    // half of a Continuous Action die's lifecycle: its controller chooses
    // to remove it from the Field Zone "whenever [they] could use a
    // Global ability" (same window UseGlobalAbility itself checks), which
    // is when its authored ability actually runs. Every currently-
    // authored Continuous card's own text bundles the zone move into the
    // ability itself ("send this die to your Used Pile to/and [effect]"),
    // so the move to the Used Pile happens here, unconditionally, rather
    // than being something the card's own EffectNode tree has to say -
    // Appendix 1 clarification (2) restricts this to the die's own
    // controller (not "either player," unlike a real Global ability).
    // Gear/Trap's own Continuous sub-variants remove themselves
    // differently (attach to a card, move on a delay) - not handled here,
    // since no currently-authored card needs it; a future one would need
    // its own path rather than stretching this method.
    public static void ResolveContinuousDie(GameState state, AbilityQueue queue, string dieId)
    {
        if (!InMainOrAttackActionWindow(state))
            throw new InvalidOperationException(
                "A Continuous Action die can only be resolved during the Main Step or the Attack Step's Action/Global window.");

        var die = FindDie(state, dieId);
        if (die.Zone != Zone.FieldZone || !DieStats.HasKeyword(state, die, "Continuous"))
            throw new InvalidOperationException($"{DisplayName(state, die)} is not a Continuous Action die sitting in the Field Zone.");

        // Rule 2.6.4.2's clarification (2) - only the controller who
        // purchased it may act on it; no explicit playerId parameter to
        // check that against, same as every other engine method here
        // (this project has no caller-identity/auth layer at all, see
        // RULES_ENGINE_DESIGN.md's next-steps list - not this method's
        // problem to solve).

        // Rule 2.6.4.3 - resolving is explicitly NOT a second "use," so
        // this only enqueues the die's own ContinuousResolve ability, none
        // of UseActionDie's "you used an Action die" reactions (Amplify/
        // Attune/Obscure) fire again.
        EnqueueTriggered(state, queue, die, TriggerType.ContinuousResolve);
        die.Zone = Zone.UsedPile;
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
        // Mister Sinister's own "(including Global Abilities)" - the
        // whole-side blank specifically calls this out, so it's the one
        // enforced here. The per-die blank (GameState.BlankedDieIds -
        // Mister Sinister's own Global target, Vulcan's combat-scoped
        // grant) is deliberately NOT checked here: UseGlobalAbility only
        // ever receives a cardId/playerId, never which specific die is
        // invoking it (rule 2.6.5.2 - a Global can be used by CARD
        // ownership alone, without any die of it even being active), so
        // there's no die-level distinction to make at this choke point.
        if (state.BlankedControllerIds.Contains(playerId))
            throw new InvalidOperationException($"{card.Name}'s text is currently ignored - its Global ability can't be used.");
        var ability = card.Abilities.FirstOrDefault(a => a.Trigger == TriggerType.Global)
            ?? throw new InvalidOperationException($"{card.Name} has no Global ability.");
        var cost = ability.EnergyCost
            ?? throw new InvalidOperationException($"{card.Name}'s Global ability has no defined energy cost.");

        // Card-text once-per-turn limiter (e.g. Falcon's "Once during your
        // turn") - checked before payment is even validated, so a rejected
        // attempt (wrong energy, etc.) after this point doesn't burn the
        // use, but a plain "already used it" attempt fails fast too.
        if (ability.OncePerTurn && state.GlobalsUsedThisTurn.Contains(cardId))
            throw new InvalidOperationException($"{card.Name}'s Global ability can only be used once per turn.");

        // Jean Grey ("Xavier's Dream"/DPS075, "Marvel Girl"/DPS115) -
        // "your opponent must pay 1 extra to use a Global Ability."
        // Scanned against the USER's opponent's active dice (the
        // granter's own side) - RequiresOwnActiveSidekick gates on the
        // GRANTER's controller having an active Sidekick ("Xavier's
        // Dream"'s own extra "and one of your Sidekick dice are active"
        // clause), not the user's.
        var opponentOfUserId = state.OpponentOf(playerId);
        var surchargeGranters = state.DiceIn(opponentOfUserId, Zone.FieldZone)
            .Concat(state.DiceIn(opponentOfUserId, Zone.AttackZone))
            .Select(d => DieStats.GetCard(state, d))
            .Where(c => c is not null)
            .Distinct();
        var requiredEnergyAmount = cost.Amount;
        foreach (var granter in surchargeGranters)
        {
            if (granter!.GrantsOpponentGlobalSurcharge is not { } surcharge) continue;
            if (surcharge.RequiresOwnActiveSidekick && !DieStats.HasActiveSidekick(state, opponentOfUserId)) continue;
            requiredEnergyAmount += surcharge.Amount;
        }

        var energyDice = energyDieIdsToSpend.Select(id => FindPlayerEnergyDie(state, playerId, id)).ToList();
        // Rule 2.6.1.1/2.6.1.2 - the Active player's spent energy goes Out
        // of Play; the Inactive player's goes straight to the Used Pile,
        // since Out of Play doesn't exist on their turn (rule 1.5.8.5).
        var destination = playerId == state.ActivePlayerId ? Zone.OutOfPlay : Zone.UsedPile;
        SpendEnergy(
            state, playerId, energyDice, requiredEnergyAmount, cost.RequiredType is { } t ? [t] : [], destination,
            () => $"Not enough energy offered to pay for {card.Name}'s Global ability (needs {requiredEnergyAmount}).",
            requiredType => $"{card.Name}'s Global ability requires at least one {requiredType} energy.");

        if (ability.OncePerTurn) state.GlobalsUsedThisTurn.Add(cardId);

        // Rule 3.1.5 - the source of a non-damage Global ability is the
        // player who paid for it, not a specific die.
        queue.Enqueue(sourceDieId: null, playerId, TriggerType.Global, ability.Effect);
    }

    // Shared by Purchase/Field/UseGlobalAbility - spends `chosenDice` (in
    // the order given) to cover `amountNeeded` total energy, honoring at
    // least one die per distinct required type (Wild substitutes for any -
    // rule 2.6.2.3; Generic never does). Stops consuming as soon as the
    // amount is met, so any dice offered beyond what's needed are left
    // untouched - same as the old "just count dice" version, generalized
    // from counting dice to summing their EnergyAmount.
    //
    // The rulebook's "Doubles" rule matters once a die's face is worth 2
    // and only part of it is needed: a typed double (e.g. double Fist, the
    // usual case on a character die - see PlaceholderDiceRoller) "spins
    // down" to its single-energy face of the same type and stays in the
    // Reserve Pool, still spendable later this turn. A Generic double
    // (e.g. a Basic Action die, which has no single-energy face to spin
    // to) instead moves out fully right away; if the Active player is
    // paying, the unspent half is banked as a real virtual-energy die (see
    // AddVirtualGenericEnergy) rather than "spinning" a face that doesn't
    // exist on that physical die - but per rule 2.6.1.6, only the Active
    // player gets this banking. The Inactive player's unspent half is just
    // lost (rule 2.6.1.5's reasoning for a double-only die applies the same
    // way here). Only the very last die needed can ever be partially
    // spent, by construction - every die consumed before it was necessary
    // in full to reach the target.
    //
    // missingTypeMessage is only ever needed when requiredTypes is
    // non-empty (Field has none - fielding cost has no type requirement,
    // rule 2.6.3.2), so it's optional.
    private static void SpendEnergy(
        GameState state, string payerId, IReadOnlyList<DieInstance> chosenDice, int amountNeeded,
        IReadOnlyList<EnergyType> requiredTypes, Zone destinationZone,
        Func<string> insufficientMessage, Func<EnergyType, string>? missingTypeMessage = null)
    {
        var consumed = new List<DieInstance>();
        var used = 0;
        foreach (var die in chosenDice)
        {
            if (used >= amountNeeded) break;
            consumed.Add(die);
            used += die.EnergyAmount;
        }

        if (used < amountNeeded)
            throw new InvalidOperationException(insufficientMessage());

        var unclaimed = new List<DieInstance>(consumed);
        foreach (var requiredType in requiredTypes.Distinct())
        {
            var match = unclaimed.FirstOrDefault(d =>
                d.EnergyKind == EnergyKind.Wild ||
                (d.EnergyKind == EnergyKind.Specific && d.ProvidedEnergyType == requiredType));
            if (match is null)
                throw new InvalidOperationException(missingTypeMessage!(requiredType));
            unclaimed.Remove(match); // rule 2.6.2.3's example - one energy per required type, not reused
        }

        var overspend = used - amountNeeded;
        for (var i = 0; i < consumed.Count; i++)
        {
            var die = consumed[i];
            var isPartial = i == consumed.Count - 1 && overspend > 0;

            // A virtual energy die (see AddVirtualGenericEnergy) isn't a
            // real physical die, so there's nothing to move to
            // destinationZone - fully spending it just makes it vanish
            // (same as it would at Clean Up if left unspent), and
            // partially spending it just lowers its tracked amount in
            // place, same shape as a typed double's spin-down.
            if (die.IsVirtualEnergy)
            {
                if (isPartial) die.EnergyAmount = overspend;
                else state.Dice.Remove(die);
                continue;
            }

            if (isPartial)
            {
                if (die.EnergyKind == EnergyKind.Generic)
                {
                    die.Zone = destinationZone;
                    if (destinationZone == Zone.UsedPile) die.ResetToUnrolled();

                    // Rule 2.6.1.6 only grants the "keep the other as
                    // virtual generic energy" banking to the Active player.
                    // Rule 2.6.1.5's framing for the Inactive player (a
                    // double-only die "cannot be spun to a single energy
                    // face," so the unused half is simply lost) implies the
                    // same for a Generic double - the Inactive player has no
                    // Main Step of their own to spend banked energy in
                    // anyway.
                    if (payerId == state.ActivePlayerId)
                        AddVirtualGenericEnergy(state, payerId, overspend);
                }
                else
                {
                    die.EnergyAmount = overspend; // "spin down" to the single-energy face
                }
            }
            else
            {
                die.Zone = destinationZone;
                // Out of Play is transient (swept - and reset - at Clean
                // Up); a die landing straight in the Used Pile (the
                // Inactive player's Global payments skip Out of Play
                // entirely - rule 1.5.8.5) is already dormant, so reset
                // now rather than leaving a stale face to linger.
                if (destinationZone == Zone.UsedPile) die.ResetToUnrolled();
            }
        }
    }

    // Rule 1.4.4/1.4.5 - "virtual" generic energy (from a draw shortfall,
    // or from partially spending a Generic double that has no
    // single-energy face to spin down to) represented as a real spendable
    // die in the Reserve Pool rather than a separate counter, so it goes
    // through the exact same selection/SpendEnergy path as any other
    // energy die - a player can just click it like any other energy chip.
    // One die per player, found-or-created by a deterministic id so
    // multiple grants in the same turn accumulate onto it rather than
    // cluttering the Reserve Pool with several tiny virtual chips.
    private static void AddVirtualGenericEnergy(GameState state, string playerId, int amount)
    {
        var id = $"{playerId}-virtual-generic";
        var existing = state.Dice.FirstOrDefault(d => d.Id == id);
        if (existing is not null)
        {
            existing.EnergyAmount += amount;
            return;
        }

        state.Dice.Add(new DieInstance
        {
            Id = id,
            OwnerId = playerId,
            ControllerId = playerId,
            Zone = Zone.ReservePool,
            Status = DieStatus.Energy,
            EnergyKind = EnergyKind.Generic,
            EnergyAmount = amount,
            IsVirtualEnergy = true,
        });
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

    // For user-facing error messages - a card's name reads far better than
    // its raw die id (e.g. "teamB-falcon-1"), and which team it belongs to
    // is already visible elsewhere in the UI, not worth repeating here.
    private static string DisplayName(GameState state, DieInstance die) =>
        die.CardId is { } cardId && state.CardCatalog.TryGetValue(cardId, out var card) ? card.Name : die.Id;

    // Shared by TurnEngine.Field and CombatEngine - enqueues every ability
    // on a die's card matching the given trigger. Internal so CombatEngine
    // (same assembly) can reuse it for WhenAttacks/WhenKOd.
    internal static void EnqueueTriggered(GameState state, AbilityQueue queue, DieInstance die, TriggerType trigger)
    {
        // DieStats.GetCard (not a raw CardId lookup) - Mister Sinister/
        // Vulcan's own "ignore text" both mean a blanked die's triggered
        // abilities simply never fire.
        var card = DieStats.GetCard(state, die);
        if (card is null) return;

        foreach (var ability in card.Abilities.Where(a => a.Trigger == trigger))
            queue.Enqueue(die.Id, die.ControllerId, trigger, ability.Effect);
    }

    // The single choke point for "everything that reacts to a KO" -
    // WhenKOd, Retaliation, and WhenAnotherDieKOd. Every real KO site
    // (CombatEngine's combat-damage wave and Range, EffectInterpreter's
    // Ko/DealDamage/DealDamagePerActiveAffiliate, CleanUp's Deadly KOs)
    // calls this once with its own already-KO'd batch, rather than each
    // remembering to wire up WhenKOd/Retaliation individually - which is
    // exactly how this gap was found: Retaliation never fired off a
    // Range KO, and nothing at all fired off an ability-driven or
    // Deadly KO, because each call site had its own copy-pasted (or
    // missing) reaction logic instead of one shared path. koDieIds is
    // treated as one simultaneous batch (Appendix 1 clarification 1) -
    // WhenKOd fires for every one of them first (order-independent,
    // rule 2.7.6.5), then Retaliation and WhenAnotherDieKOd are each
    // scanned once per KO'd die, but only after the WHOLE batch's KOs
    // already happened to `state` (by the time this is called - the
    // caller KO's everything first, then calls this), so a reactor
    // that was ALSO KO'd in the same batch is correctly already gone
    // from the active scan by the time its own reaction (or anyone
    // else's) is checked. queue may be null - a no-op in that case rather
    // than a required param, so KO call sites that don't have one (tests,
    // mostly, plus any CleanUp caller that doesn't pass one) don't need
    // to fake one.
    internal static void ResolveKOReactions(GameState state, AbilityQueue? queue, IReadOnlyList<string> koDieIds)
    {
        if (queue is null || koDieIds.Count == 0) return;

        foreach (var koId in koDieIds)
            EnqueueTriggered(state, queue, FindDie(state, koId), TriggerType.WhenKOd);

        foreach (var koId in koDieIds)
            ResolveRetaliation(state, queue, FindDie(state, koId));

        foreach (var koId in koDieIds)
            ResolveWhenAnotherDieKOd(state, queue, FindDie(state, koId));
    }

    // Keyword Retaliation - "If a character you control with Retaliation
    // is active, and a Character die you control that shares an
    // affiliation with it is KO'd, deal 1 damage to an opposing player."
    // Moved here (was CombatEngine-only) now that every KO site shares
    // this one reactive-scan path via ResolveKOReactions above. Scans
    // koDie's OWN controller's currently-active dice for Retaliation
    // holders sharing an affiliation with koDie's card, deduplicated by
    // CardId (clarification 2 - multiple copies of the SAME Retaliation
    // character only trigger once, even though each is independently
    // active).
    private static void ResolveRetaliation(GameState state, AbilityQueue queue, DieInstance koDie)
    {
        var koCardId = koDie.VirtualCardId ?? koDie.CardId;
        if (koCardId is null || !state.CardCatalog.TryGetValue(koCardId, out var koCard)) return;

        var retaliators = state.DiceIn(koDie.ControllerId, Zone.FieldZone)
            .Concat(state.DiceIn(koDie.ControllerId, Zone.AttackZone))
            .Where(d => DieStats.HasKeyword(state, d, "Retaliation"))
            .GroupBy(d => d.VirtualCardId ?? d.CardId)
            .Select(g => g.First());

        foreach (var retaliator in retaliators)
        {
            var retaliatorCardId = retaliator.VirtualCardId ?? retaliator.CardId;
            if (retaliatorCardId is null || !state.CardCatalog.TryGetValue(retaliatorCardId, out var retaliatorCard))
                continue;
            if (!retaliatorCard.Affiliations.Any(koCard.Affiliations.Contains)) continue;

            EnqueueTriggered(state, queue, retaliator, TriggerType.Retaliation);
        }
    }

    // TriggerType.WhenAnotherDieKOd - bespoke card text shaped like
    // Retaliation/Teamwatch (a reactive scan over the controller's own
    // active dice) but with an authored filter (AbilityDef.KOdFilter)
    // instead of a hardcoded one, since these cards each filter
    // differently (see KOdDieMatch's own remarks). Scans EVERY active
    // die on the board, not just koDie's own controller's the way
    // Retaliation does - KOdFilter.Ownership expresses "must share the
    // KO'd die's controller" per-card instead, since (unlike Retaliation)
    // not every card with this trigger actually requires that (Supreme
    // Intelligence's "a card with Kree in its name" has no ownership
    // restriction at all).
    private static void ResolveWhenAnotherDieKOd(GameState state, AbilityQueue queue, DieInstance koDie)
    {
        var koCardId = koDie.VirtualCardId ?? koDie.CardId;
        var koCard = koCardId is not null ? state.CardCatalog.GetValueOrDefault(koCardId) : null;

        foreach (var reactor in state.Dice.Where(d => d.Zone is Zone.FieldZone or Zone.AttackZone).ToList())
        {
            // DieStats.GetCard, not a raw lookup - a blanked reactor's
            // own reactive abilities don't fire either.
            if (DieStats.GetCard(state, reactor) is not { } reactorCard) continue;

            foreach (var ability in reactorCard.Abilities.Where(a => a.Trigger == TriggerType.WhenAnotherDieKOd))
            {
                var filter = ability.KOdFilter
                    ?? throw new InvalidOperationException($"{reactorCard.Name}'s WhenAnotherDieKOd ability has no KOdFilter.");
                if (!MatchesKOdFilter(state, filter, koDie, koCard, reactor)) continue;

                queue.Enqueue(reactor.Id, reactor.ControllerId, TriggerType.WhenAnotherDieKOd, ability.Effect);
            }
        }
    }

    private static bool MatchesKOdFilter(GameState state, KOdDieMatch filter, DieInstance koDie, CardDef? koCard, DieInstance reactor)
    {
        if (filter.ExcludeSelf && koDie.Id == reactor.Id) return false;
        if (filter.Ownership == TargetOwnership.Own && koDie.ControllerId != reactor.ControllerId) return false;
        if (filter.Ownership == TargetOwnership.Opposing && koDie.ControllerId == reactor.ControllerId) return false;
        if (filter.RequiredEnergyType is { } energyType && (koCard is null || !koCard.EnergyTypes.Contains(energyType))) return false;
        if (filter.NameContains is { } nameFragment &&
            (koCard is null || !koCard.Name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))) return false;
        if (filter.AffiliationContains is { } affiliation && (koCard is null || !koCard.Affiliations.Contains(affiliation))) return false;
        return true;
    }

    // Rule 2.6.7.1(3)/2.6.7.2 - the active player chooses to attack or not
    // at the end of the Main Step. queue is optional (defaults null, a
    // no-op) so every existing caller that doesn't care about
    // TriggerType.StartOfOpponentsAttackStep reactions (most games have
    // no such card in play) doesn't need to start passing one.
    public static void EnterAttackStep(GameState state, AbilityQueue? queue = null)
    {
        if (state.CurrentStep != TurnStep.Main)
            throw new InvalidOperationException("Must be in the Main Step to enter the Attack Step.");
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        if (queue is null) return;

        // Both Emma Frost printings (DPS070/DPS110) - "at the start of
        // your opponent's Attack Step, [...]." The active player IS the
        // one whose Attack Step just started, so the reacting side is
        // always the OTHER player - a fixed relationship, unlike
        // WhenAnotherDieKOd/WhenAnotherDieFielded's own per-card filter,
        // so no filter object is needed here at all.
        var reactingPlayerId = state.OpponentOf(state.ActivePlayerId);
        foreach (var die in state.DiceIn(reactingPlayerId, Zone.FieldZone)
            .Concat(state.DiceIn(reactingPlayerId, Zone.AttackZone)).ToList())
        {
            EnqueueTriggered(state, queue, die, TriggerType.StartOfOpponentsAttackStep);
        }
    }

    public static void SkipAttackStep(GameState state)
    {
        if (state.CurrentStep != TurnStep.Main)
            throw new InvalidOperationException("Must be in the Main Step to skip the Attack Step.");

        // Rule-text "must attack" (Vulcan's Global) - CombatEngine.
        // DeclareAttackers enforces this once the Attack Step is actually
        // entered, but skipping the step outright bypasses that check
        // entirely unless it's guarded here too. Same "if able" scoping
        // (only currently-eligible dice) as that check.
        var forcedButSkipped = state.DiceIn(state.ActivePlayerId, Zone.FieldZone)
            .Where(d => state.MustAttackThisTurn.Contains(d.Id))
            .ToList();
        if (forcedButSkipped.Count > 0)
        {
            var names = string.Join(", ", forcedButSkipped.Select(d => DisplayName(state, d)));
            throw new InvalidOperationException($"{names} must attack this turn - cannot skip the Attack Step.");
        }

        state.CurrentStep = TurnStep.CleanUp;
    }

    // Rule 2.8 - Clean Up Step, and the turn handoff described in 2.8.6.
    // NOTE: 2.8.2's ordered resolution of Applied-then-Persistent abilities
    // is not implemented here yet - it depends on AbilityQueue being wired
    // to real card triggers. roller is optional (null in call sites that
    // don't care) - it's what lets a Deadly-KO'd die with Regenerate
    // reroll instead, same convention as AssignCombatDamage's own roller.
    // queue is optional too (same nullable convention as ClearAndDraw's
    // own) - callers that don't pass one just don't get WhenKOd/
    // Retaliation/WhenAnotherDieKOd reactions off a Deadly KO (ResolveKO
    // Reactions itself no-ops on a null queue), same as every other
    // KO-producing call site.
    public static void CleanUp(GameState state, IDiceRoller? roller = null, AbilityQueue? queue = null)
    {
        if (state.CurrentStep != TurnStep.CleanUp)
            throw new InvalidOperationException($"Expected CleanUp step, was {state.CurrentStep}.");

        var activeId = state.ActivePlayerId;

        // Keyword Deadly (rule Appendix 1 clarification 2 - "Deadly is a
        // Persistent ability. Therefore, it is resolved in the Clean Up
        // Step.") - a forced KO, not a damage/defense check, so this goes
        // through ForceKO directly rather than TryResolveKO. Previously
        // this never fired WhenKOd/Retaliation at all (a documented gap -
        // CleanUp had no queue to enqueue into); now routed through the
        // same shared ResolveKOReactions every other KO site uses, once
        // `queue` is actually supplied by the caller.
        var deadlyKoIds = new List<string>();
        foreach (var id in state.DeadlyEngagedDieIds)
        {
            if (DieStats.ForceKO(state, FindDie(state, id), roller)) deadlyKoIds.Add(id);
        }
        state.DeadlyEngagedDieIds.Clear();
        ResolveKOReactions(state, queue, deadlyKoIds);

        // Keyword Intimidate - "remove target opposing Character die from
        // the Field Zone until end of turn." No tracked set needed (unlike
        // Deadly) - Zone.Intimidated is itself the marker, and clarification
        // 1 ("if the Intimidating die is removed, the Intimidate effect is
        // not canceled") means this always resolves on a fixed timer, not
        // conditionally on anything else. Not scoped to the active player -
        // Intimidate always targets an *opposing* die relative to whoever
        // fielded it, so the die sitting in Zone.Intimidated could belong to
        // either player. Returns on its same face/level, not reset - this
        // isn't a dormant zone (rule 1.6.8 only lists Prep Area/Used
        // Pile/Bag), matching Capture's own "original level" precedent
        // (rule 3.8.4) for the same kind of temporary removal.
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.Intimidated).ToList())
            die.Zone = Zone.FieldZone;

        // Rule 2.8.1 - clear damage on Character dice that weren't KO'd.
        foreach (var die in state.Dice.Where(d => d.Zone == Zone.FieldZone))
            die.Damage = 0;

        // Rule 3.4.3.9 - Applied ability modifiers (e.g. Wasp's Attune
        // buff) last only until the end of turn, even for a die that
        // stayed in the Field Zone the whole time and was never KO'd or
        // rerolled - the "leaves the Field Zone" half of this rule is
        // already covered by DieInstance.ResetToUnrolled (called from
        // ForceKO and the Action-die/Out-of-Play sweeps below), but nothing
        // previously expired a survivor's modifiers at the turn boundary
        // itself. Applies to every die regardless of controller - an
        // Applied modifier can be granted to either player's die (e.g. an
        // opposing Global ability), and it's the turn ending, not whose
        // turn it was, that matters.
        foreach (var die in state.Dice)
        {
            die.AppliedModifiers.Clear();
            die.AppliedKeywords.Clear();
        }

        // Rule 2.8.3 - Action dice left on their action face in the Reserve
        // Pool move to the Used Pile.
        foreach (var die in state.DiceIn(activeId, Zone.ReservePool).Where(d => d.Status == DieStatus.Action).ToList())
        {
            die.Zone = Zone.UsedPile;
            die.ResetToUnrolled();
        }

        // Rule 2.8.6 - Out of Play empties to the Used Pile and the turn passes.
        foreach (var die in state.DiceIn(activeId, Zone.OutOfPlay).ToList())
        {
            die.Zone = Zone.UsedPile;
            die.ResetToUnrolled();
        }

        // Unspent virtual generic energy does not carry over (rule 1.4.5/
        // 2.6.7.1(2)) - removed outright rather than swept to the Used
        // Pile like a real die, since it was never a physical one. Covers
        // both players, not just the one whose turn is ending - the
        // inactive player can bank virtual energy too (e.g. partially
        // spending a Generic double to pay for a Global on the active
        // player's turn), and it's just as fictional.
        state.Dice.RemoveAll(d => d.IsVirtualEnergy);

        // Keyword Experience - "All Character dice with this keyword
        // that are active when [an opposing Monster is KO'd] and remain
        // active at the end of the turn gain one Experience Token."
        // Simplified per every printed card's own reminder text (which
        // drops the mid-turn-snapshot nuance): "opposing Monster KO'd
        // THIS TURN" (state.OpposingMonsterKOdThisTurn, set by DieStats.
        // ForceKO) + "card active RIGHT NOW" (this check), not "active
        // at the instant of the KO." An unblocked attacker naturally
        // fails this anyway - rule 2.7.4.3.1 already moves it to Out of
        // Play the moment its combat damage resolves, well before Clean
        // Up runs - which is exactly clarification 5's "an unblocked
        // Adventurer cannot gain a token": no separate code needed for
        // it, it falls out of the same active-zone check for free.
        // Deduplicated by CardId - clarification 2, "a card can only
        // gain one Experience Token per turn" - even if multiple active
        // copies of it qualify.
        if (state.OpposingMonsterKOdThisTurn)
        {
            foreach (var cardId in state.DiceIn(activeId, Zone.FieldZone)
                .Concat(state.DiceIn(activeId, Zone.AttackZone))
                .Where(d => DieStats.HasKeyword(state, d, "Experience"))
                .Select(d => d.VirtualCardId ?? d.CardId)
                .Where(id => id is not null)
                .Distinct())
            {
                state.ExperienceTokens[cardId!] = state.ExperienceTokens.GetValueOrDefault(cardId!) + 1;
            }
        }
        state.OpposingMonsterKOdThisTurn = false;

        // TriggerType.EndOfYourTurn (e.g. Jean Grey, DPS035 - "at the end
        // of each of your turns... put a Loyalty Counter"). Executed
        // directly rather than via AbilityQueue, same as Deadly's KOs
        // above and for the same reason (CleanUp has no queue to
        // enqueue into - a documented gap) - safe here specifically
        // because no currently-authored EndOfYourTurn ability needs an
        // external target choice (Jean Grey's own Conditional/
        // GrantLoyaltyCounter tree is entirely self-contained). A future
        // card that DOES need real targeting here would need the queue
        // gap closed first, not just a new case added to this loop.
        foreach (var die in state.DiceIn(activeId, Zone.FieldZone).Concat(state.DiceIn(activeId, Zone.AttackZone)).ToList())
        {
            var cardId = die.VirtualCardId ?? die.CardId;
            if (cardId is null || !state.CardCatalog.TryGetValue(cardId, out var card)) continue;
            foreach (var ability in card.Abilities.Where(a => a.Trigger == TriggerType.EndOfYourTurn))
                EffectInterpreter.Execute(ability.Effect, new EffectContext(state, activeId, die.Id, _ => [], Roller: roller, Trigger: ability.Trigger));
        }
        state.AnyCharacterKOdThisTurn = false;

        // Rule 1.2.3(3) - the once-per-turn Epic Basic Action limit resets.
        state.EpicBasicActionUsedThisTurn = false;

        // Card-text once-per-turn Global limiters (e.g. Falcon) reset too.
        state.GlobalsUsedThisTurn.Clear();

        // Rule-text turn-scoped flags reset (Invisible Woman's forced
        // blockers, Starfire's "purchased a die this turn" check,
        // Lilandra's "purchased a character die this turn" check).
        state.MustBlockThisTurn.Clear();
        state.MustAttackThisTurn.Clear();
        state.CantBlockThisTurn.Clear();
        state.CantFieldCharacterDiceThisTurn.Clear();
        state.PendingPurchaseDiscount = null;
        state.PendingNextPurchaseGoesToBag = false;
        state.UsedDamageRedirectThisTurn.Clear();
        state.BlankedDieIds.Clear();
        state.BlankedControllerIds.Clear();
        state.ImmuneToActionAndGlobalTargetingControllerIds.Clear();
        var cleaningPlayer = state.GetPlayer(activeId);
        cleaningPlayer.PurchasedDieThisTurn = false;
        cleaningPlayer.PurchasedCharacterDieThisTurn = false;

        // Keyword Obscure - "unblockable until end of turn" expires here.
        state.ObscuredCardIds.Clear();

        // Keyword Strike's own "this turn" window resets too.
        state.FieldedThisTurn.Clear();

        state.IsFirstTurn = false;
        state.ActivePlayerId = state.OpponentOf(activeId);
        state.CurrentStep = TurnStep.ClearAndDraw;
        state.AttackSubStep = AttackSubStep.NotInAttack;
    }
}
