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
// GetDelta takes `state` (Phase 6 change - was a plain `int Delta { get; }`
// property in Phase 3) because a continuous StatAura's own Amount can be
// PerMatch (a live count), not just Fixed - AppliesTo and GetDelta are
// still two separate calls (QueryEngine's own Where/Sum shape), not
// merged into one, so a modifier that wants to short-circuit "am I even
// active" before doing PerMatch's own live query still can.
public interface IDieStatModifier
{
    bool AppliesTo(GameState state, DieInstance die);
    int GetDelta(GameState state, DieInstance die);
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
    int GetDelta(GameState state, CardDef card, string payerId);
}

// TagAura's own registry shape (Phase 6) - deliberately not folded into
// IDieStatModifier (a tag grant isn't a delta) or GrantedTag (that's a
// one-shot, per-die, GrantTag-effect store, not a continuous aura).
public interface ITagAuraModifier
{
    bool AppliesTo(GameState state, DieInstance die);
    IReadOnlyList<string> GetTags(GameState state, DieInstance die);
}

// CombatRule's registry shape (Phase 6) - nothing queries this yet
// (Combat is Phase 7's own concern), same "register the closed
// vocabulary's full shape now, wire its consumer when it exists" pattern
// Phase 4 already used for DieKOd/DieDamaged emission sites.
public interface ICombatRuleModifier
{
    bool AppliesTo(GameState state, DieInstance die);
    CombatRuleKind Kind { get; }
    int? N { get; }
}

// DamageModifier's registry shape (Phase 6) - THIS one has a real
// consumer already: EffectInterpreter.ApplyDamage (Phase 5's own damage
// choke point), extended in Phase 6 to walk this registry before marking
// damage. Source lets a modifier scope itself to Ability|Combat|Any -
// only Ability is reachable before Phase 7 builds a Combat damage
// source, same "declared now, exercised once its trigger exists" shape.
public interface IDamageInterceptor
{
    bool AppliesTo(GameState state, DieInstance die, DamageSource source);
    DamageModifierMode Mode { get; }
    int GetAmount(GameState state, DieInstance die); // meaningful for Reduce/Amplify only
    DieInstance? RedirectTarget(GameState state, DieInstance originalTarget); // RedirectToSelf only
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
// GetKeywords now folds in granted keyword-shaped tags too (Phase 5's
// GrantTag, e.g. Psylocke "Telepath" granting Overcrush) - DieInstance.
// GrantedTags doesn't distinguish "keyword" from "affiliation" from
// anything else (Part 1's tag-unification note doesn't either), so this
// just unions them into the printed set. AbilitiesActive (the reserved
// 8th query) is NOT implemented here - see V2_PLAN.md's own note not to
// build it early.
public static class QueryEngine
{
    // Base* variants (Phase 6) compute from printed data + one-shot
    // AppliedModifiers only - no continuous registry fold-in. A
    // continuous template's OWN eligibility check (ContinuousRegistry's
    // SourceQualifies) resolves its Target/Whose/ActiveWhen through
    // these instead of the continuous-inclusive versions below, which is
    // what stops a self-referential aura (e.g. "each of your dice with
    // 3+ Attack gets +1 Attack," or Darkseid-shaped TagAuras whose own
    // Target filters on Tags) from recursing into itself while being
    // asked "am I even active" - found as an actual `StackOverflow`
    // crashing the Phase 6 test run, not designed in ahead of time.
    // Ordinary ability targeting/conditions (Phase 5) are unaffected -
    // they call the continuous-inclusive versions and correctly see
    // continuously-granted stats/tags.
    public static int GetBaseAttack(GameState state, DieInstance die) =>
        (state.GetCurrentFace(die)?.Character?.Attack ?? 0) + die.AppliedModifiers.Sum(m => m.AttackDelta);

    public static int GetBaseDefense(GameState state, DieInstance die) =>
        (state.GetCurrentFace(die)?.Character?.Defense ?? 0) + die.AppliedModifiers.Sum(m => m.DefenseDelta);

    public static int GetBaseFieldingCost(GameState state, DieInstance die) =>
        Math.Max(0, (state.GetCurrentFace(die)?.Character?.FieldingCost ?? 0) + die.AppliedModifiers.Sum(m => m.FieldingCostDelta));

    public static int GetBasePurchaseCost(CardDef card) => Math.Max(1, card.PurchaseCost);

    public static int GetAttack(GameState state, DieInstance die) =>
        GetBaseAttack(state, die) + state.AttackModifiers.Where(m => m.AppliesTo(state, die)).Sum(m => m.GetDelta(state, die));

    public static int GetDefense(GameState state, DieInstance die) =>
        GetBaseDefense(state, die) + state.DefenseModifiers.Where(m => m.AppliesTo(state, die)).Sum(m => m.GetDelta(state, die));

    // Floor 1 - the game's own "to a minimum of 1" purchase-discount text
    // (Dark Phoenix "Enemy of the Shi'ar" et al.) - erratum corrected
    // 2026-08-22 (V2_VOCABULARY.md Part 4), was wrongly floor 0 in an
    // earlier draft of this plan.
    public static int GetPurchaseCost(GameState state, CardDef card, string payerId)
    {
        var continuous = state.PurchaseCostModifiers.Where(m => m.AppliesTo(state, card, payerId)).Sum(m => m.GetDelta(state, card, payerId));
        return Math.Max(1, card.PurchaseCost + continuous);
    }

    // Floor 0, unlike purchase - printed-0 fielding-cost faces and
    // free-to-field grants are both real (a 0 fielding cost is a normal
    // value, not a floor being hit).
    public static int GetFieldingCost(GameState state, DieInstance die) =>
        Math.Max(0, GetBaseFieldingCost(state, die) + state.FieldingCostModifiers.Where(m => m.AppliesTo(state, die)).Sum(m => m.GetDelta(state, die)));

    // Now also folds in TagAura-granted keywords (Phase 6), same
    // reasoning as GrantTag's own fold-in: a granted tag isn't tagged as
    // "keyword" vs "affiliation" anywhere in the model, so this unions
    // whatever's active in alongside the printed set.
    public static IReadOnlySet<string> GetKeywords(GameState state, DieInstance die)
    {
        var keywords = die.CardId is { } cardId ? new HashSet<string>(state.CardCatalog[cardId].Keywords) : [];
        foreach (var granted in die.GrantedTags) keywords.Add(granted.Tag);
        foreach (var aura in state.TagAuras.Where(a => a.AppliesTo(state, die)))
            foreach (var tag in aura.GetTags(state, die)) keywords.Add(tag);
        return keywords;
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

        var continuous = state.GlobalEnergyCostModifiers.Where(m => m.AppliesTo(state, card, payerId)).Sum(m => m.GetDelta(state, card, payerId));
        return Math.Max(0, baseCost.Amount + continuous);
    }

    // Not one of the 7 frozen queries - a plumbing helper (Phase 4) that
    // composes them, needed by EventFilter/TargetFilter matching alike.
    // Part 1's tag-unification note: "a die's tag set = its card's
    // affiliations + keywords + its card name + 'sidekick' if applicable +
    // its printed energy symbol id + granted tags." Granted tags (Phase
    // 5's GrantTag effect, DieInstance.GrantedTags) are included now that
    // something actually populates them.
    public static IReadOnlySet<string> GetTags(GameState state, DieInstance die)
    {
        var tags = new HashSet<string>(GetBaseTags(state, die));
        foreach (var aura in state.TagAuras.Where(a => a.AppliesTo(state, die)))
            foreach (var tag in aura.GetTags(state, die)) tags.Add(tag);

        return tags;
    }

    // Printed + one-shot-granted tags only - deliberately excludes
    // continuous TagAuras (Phase 6). A TagAuraModifier's own AppliesTo
    // resolves ITS Target filter through this (via TargetResolver's
    // includeContinuousTags:false path), not the full GetTags above -
    // otherwise evaluating any TagAura whose Target filter matches on
    // Tags would need to evaluate every registered TagAura (including
    // itself) to answer "what are this die's tags," which is exactly a
    // stack-overflowing cycle (found by a crashing test run, not
    // designed in ahead of time). Ordinary ability targeting (Phase 5)
    // and EventFilter matching are unaffected - they call the full
    // GetTags above and correctly see continuously-granted tags.
    public static IReadOnlySet<string> GetBaseTags(GameState state, DieInstance die)
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

        foreach (var granted in die.GrantedTags) tags.Add(granted.Tag);
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

    // The Base-only counterpart (Phase 6) - same switch, but Attack/
    // Defense/FieldingCost/PurchaseCost read the continuous-EXCLUDING
    // Base* queries above. Used by ContinuousRegistry's own eligibility
    // checks for the same self-referential-cycle reason GetBaseTags is.
    public static int GetBaseStatValue(GameState state, DieInstance die, StatThreshold stat) => stat.Kind switch
    {
        StatKind.Attack => GetBaseAttack(state, die),
        StatKind.Defense => GetBaseDefense(state, die),
        StatKind.Level => state.GetCurrentFace(die)?.Character?.Level ?? 0,
        StatKind.PurchaseCost => die.CardId is { } pcId ? GetBasePurchaseCost(state.CardCatalog[pcId]) : 0,
        StatKind.FieldingCost => GetBaseFieldingCost(state, die),
        StatKind.Counter => stat.CounterName is { } name && die.CardId is { } counterCardId
            ? state.Counters.GetValueOrDefault((die.ControllerId, counterCardId, name))
            : 0,
        _ => 0,
    };
}
