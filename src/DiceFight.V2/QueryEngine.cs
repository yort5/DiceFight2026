using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// A die-scoped modifier: applies (or doesn't) to a specific die, and
// contributes a flat delta when it does. Deliberately dumb (V2_PLAN.md
// Phase 3 task 1) - no priority, no layers, no ordering. Dice Masters has
// no MTG-style layer system in its own rules (no "power/toughness-setting
// effects apply before +N/+N effects" concept anywhere in the rulebook) -
// every stat-affecting effect is just a delta, and real card text never
// depends on application ORDER between two different sources, only on
// their sum. Building a layers system here would be solving a problem
// this game doesn't have.
public interface IDieStatModifier
{
    bool AppliesTo(GameState state, DieInstance die);
    int Delta { get; }
}

// The card+player-scoped counterpart, for the two queries that aren't
// about a specific rolled die (a purchase/Global cost is about a CARD and
// a PAYER, before any die from it necessarily even exists in the Reserve
// Pool). Same "dumb, flat delta" shape as IDieStatModifier - kept as a
// separate interface rather than force-fitting one shape onto two
// genuinely different "what am I even checking" contexts.
public interface ICardCostModifier
{
    bool AppliesTo(GameState state, CardDef card, string payerId);
    int Delta { get; }
}

// CanBeTargeted is a boolean AND of interceptor verdicts, not a delta sum -
// a different shape from the two above, kept as its own minimal interface
// rather than shoehorned into one of them.
public interface ITargetingInterceptor
{
    bool CanBeTargeted(GameState state, DieInstance die, string byPlayerId, ProtectionFrom triggerKind);
}

// The interceptable-query spine (V2_PLAN.md Phase 3) - the replacement
// for v1's 39 one-per-card Grants* CardDef flags (ARCHITECTURE_REVIEW.md's
// central finding). Every query walks: (a) base data from the die/card
// itself, (b) the die's own one-shot AppliedModifiers, (c) the matching
// registry below, populated by Phase 6's continuous templates - empty for
// now, so every query today reproduces "just the base value" exactly,
// which is also this phase's own acceptance proof.
//
// GetKeywords is scoped to PRINTED keywords only for now - the "granted"
// half (v1's AppliedKeywords) needs per-die tag storage that only Phase
// 5's GrantTag interpreter has a reason to add; adding it speculatively
// here, before anything populates it, would be exactly the kind of
// unused-scaffolding the "few hundred lines, no behavior" phases have
// been deliberately avoiding. AbilitiesActive (the reserved 8th query) is
// NOT implemented here - see V2_PLAN.md's own note not to build it early.
public static class QueryEngine
{
    public static int GetAttack(GameState state, DieInstance die)
    {
        var face = state.GetCurrentFace(die);
        var baseValue = face?.Character?.Attack ?? 0;
        var applied = die.AppliedModifiers.Sum(m => m.AttackDelta);
        var continuous = state.AttackModifiers.Where(m => m.AppliesTo(state, die)).Sum(m => m.Delta);
        return baseValue + applied + continuous;
    }

    public static int GetDefense(GameState state, DieInstance die)
    {
        var face = state.GetCurrentFace(die);
        var baseValue = face?.Character?.Defense ?? 0;
        var applied = die.AppliedModifiers.Sum(m => m.DefenseDelta);
        var continuous = state.DefenseModifiers.Where(m => m.AppliesTo(state, die)).Sum(m => m.Delta);
        return baseValue + applied + continuous;
    }

    // Floor 1 - the game's own "to a minimum of 1" purchase-discount text
    // (Dark Phoenix "Enemy of the Shi'ar" et al.) - erratum corrected
    // 2026-08-22 (V2_VOCABULARY.md Part 4), was wrongly floor 0 in an
    // earlier draft of this plan.
    public static int GetPurchaseCost(GameState state, CardDef card, string payerId)
    {
        var continuous = state.PurchaseCostModifiers.Where(m => m.AppliesTo(state, card, payerId)).Sum(m => m.Delta);
        return Math.Max(1, card.PurchaseCost + continuous);
    }

    // Floor 0, unlike purchase - printed-0 fielding-cost faces and
    // free-to-field grants are both real (a 0 fielding cost is a normal
    // value, not a floor being hit).
    public static int GetFieldingCost(GameState state, DieInstance die)
    {
        var face = state.GetCurrentFace(die);
        var baseValue = face?.Character?.FieldingCost ?? 0;
        var applied = die.AppliedModifiers.Sum(m => m.FieldingCostDelta);
        var continuous = state.FieldingCostModifiers.Where(m => m.AppliesTo(state, die)).Sum(m => m.Delta);
        return Math.Max(0, baseValue + applied + continuous);
    }

    public static IReadOnlySet<string> GetKeywords(GameState state, DieInstance die)
    {
        if (die.CardId is null) return new HashSet<string>();
        return new HashSet<string>(state.CardCatalog[die.CardId].Keywords);
    }

    public static bool CanBeTargeted(GameState state, DieInstance die, string byPlayerId, ProtectionFrom triggerKind) =>
        state.TargetingInterceptors.All(i => i.CanBeTargeted(state, die, byPlayerId, triggerKind));

    // ability.EnergyCost is only meaningful for Trigger == Global
    // (Events.cs's own remarks) - null base cost means "not a paid
    // ability," which callers shouldn't be asking this about, so this
    // throws rather than silently returning 0.
    public static int GetGlobalEnergyCost(GameState state, CardDef card, TriggeredAbility ability, string payerId)
    {
        if (ability.EnergyCost is not { } baseCost)
            throw new InvalidOperationException("This ability has no EnergyCost to query - it isn't a paid Global.");

        var continuous = state.GlobalEnergyCostModifiers.Where(m => m.AppliesTo(state, card, payerId)).Sum(m => m.Delta);
        return Math.Max(0, baseCost.Amount + continuous);
    }

    // Not one of the 7 frozen queries - a plumbing helper (Phase 4) that
    // composes them, needed by EventFilter/TargetFilter matching alike.
    // Part 1's tag-unification note: "a die's tag set = its card's
    // affiliations + keywords + its card name + 'sidekick' if applicable +
    // its printed energy symbol id + granted tags." Granted tags aren't
    // included yet, same reasoning as GetKeywords' own printed-only scope.
    public static IReadOnlySet<string> GetTags(GameState state, DieInstance die)
    {
        var tags = new HashSet<string>();
        if (die.IsSidekick) tags.Add("sidekick");

        if (die.CardId is { } cardId && state.CardCatalog.TryGetValue(cardId, out var card))
        {
            foreach (var affiliation in card.Affiliations) tags.Add(affiliation);
            foreach (var keyword in card.Keywords) tags.Add(keyword);
            tags.Add(card.Name);
            if (card.EnergySymbolId is { } symbolId) tags.Add(symbolId);
        }

        return tags;
    }

    // Another plumbing helper - reads whichever of the 7 queries a
    // StatThreshold names, so EventFilter.Stat/TargetFilter.Stat matching
    // (Phase 4/5) has one place to go rather than re-deriving this switch
    // per caller. Counter reads GameState.Counters directly (Finding 13) -
    // there's no dedicated query for it, since counters are card-scoped
    // state, not something a continuous-modifier registry intercepts.
    public static int GetStatValue(GameState state, DieInstance die, StatThreshold stat) => stat.Kind switch
    {
        StatKind.Attack => GetAttack(state, die),
        StatKind.Defense => GetDefense(state, die),
        StatKind.Level => state.GetCurrentFace(die)?.Character?.Level ?? 0,
        StatKind.PurchaseCost => die.CardId is { } pcId ? GetPurchaseCost(state, state.CardCatalog[pcId], die.ControllerId) : 0,
        StatKind.FieldingCost => GetFieldingCost(state, die),
        StatKind.Counter => stat.CounterName is { } name && die.CardId is { } counterCardId
            ? state.Counters.GetValueOrDefault((die.ControllerId, counterCardId, name))
            : 0,
        _ => 0,
    };
}
