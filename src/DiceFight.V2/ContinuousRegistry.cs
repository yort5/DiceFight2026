using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Compiles every CardDef.Continuous entry (V2_VOCABULARY.md Part 1's 6
// continuous templates) into the query-modifier/interceptor registries
// QueryEngine/EffectInterpreter read (Phase 3/5) - the replacement for
// v1's 39 one-per-card Grants* CardDef flags (ARCHITECTURE_REVIEW.md's
// central finding).
//
// RegisterAll walks the whole CardCatalog ONCE (GameSetup.NewGame's own
// call) and builds ONE modifier object per (card, ContinuousDef) pair -
// not one per die, not re-registered on Field/CleanUp. Each modifier's
// own AppliesTo (via QualifyingSources below) re-scans ALL of that
// card's currently-active (Field/Attack Zone) dice, across both players
// independently, every time it's asked - so "aura appears/disappears as
// the source die enters/leaves the field" and "two auras stack
// additively" (including two copies of the SAME card, DieLimit > 1) both
// fall out for free from a live re-scan, no event hook needed to add or
// remove anything.
//
// Every template resolves its own Target/Whose TargetFilter relative to
// EACH qualifying active source die's OWN controller (not a single fixed
// "the ability's controller") - a card played by either side grants its
// aura independently, exactly like a triggered ability only listens
// while ITS OWN source die is active (Part 2's Magneto example already
// established this "no special modeling needed" precedent for triggers;
// continuous templates get the identical treatment here).
public static class ContinuousRegistry
{
    public static void RegisterAll(GameState state)
    {
        foreach (var card in state.CardCatalog.Values)
        {
            foreach (var def in QueryEngine.ContinuousOf(card))
                Register(state, card, def);
        }
    }

    private static void Register(GameState state, CardDef card, ContinuousDef def)
    {
        switch (def)
        {
            case StatAura n:
                if (n.AtkDelta is not null) state.AttackModifiers.Add(new StatAuraModifier(card, n, n.AtkDelta));
                if (n.DefDelta is not null) state.DefenseModifiers.Add(new StatAuraModifier(card, n, n.DefDelta));
                break;

            case CostModifier n:
                switch (n.Kind)
                {
                    case CostKind.Purchase: state.PurchaseCostModifiers.Add(new CardCostModifierAdapter(card, n)); break;
                    case CostKind.GlobalEnergy: state.GlobalEnergyCostModifiers.Add(new CardCostModifierAdapter(card, n)); break;
                    case CostKind.Fielding: state.FieldingCostModifiers.Add(new DieCostModifierAdapter(card, n)); break;
                    case CostKind.ActionDieUse: state.ActionDieUseCostModifiers.Add(new DieCostModifierAdapter(card, n)); break;
                }
                break;

            case TagAura n: state.TagAuras.Add(new TagAuraModifier(card, n)); break;

            case AbilityBlank n: state.AbilityBlanks.Add(new AbilityBlankModifier(card, n)); break;

            case Lockout n: state.Lockouts.Add(new LockoutModifier(card, n)); break;
            case CombatRule n: state.CombatRules.Add(new CombatRuleModifier(card, n)); break;
            case DamageModifier n: state.DamageInterceptors.Add(new DamageModifierInstance(card, n)); break;
            case TargetingProtection n: state.TargetingInterceptors.Add(new TargetingProtectionInterceptor(card, n)); break;
        }
    }

    // A blanked die grants nothing. V1 answered this the same way, via
    // its DieStats.GetCard choke point, and V2_PLAN.md Phase 8 task 3
    // asked the question explicitly - match it (V2_VOCABULARY.md Part 19).
    private static IEnumerable<DieInstance> ActiveSourceDice(GameState state, CardDef card) =>
        state.Dice.Where(d => d.CardId == card.Id
            && d.Zone is Zone.FieldZone or Zone.AttackZone
            && QueryEngine.AbilitiesActive(state, d));

    private static IReadOnlyDictionary<string, string> Bindings(DieInstance source) =>
        new Dictionary<string, string> { ["self"] = source.Id };

    // The one shared "does source S's Target/Whose filter resolve to
    // include candidateId" predicate every template below is built on -
    // Whose (CostModifier) and Target (everything else) are both plain
    // TargetFilters, and TargetResolver.Query already returns a mix of
    // die and player ids, so a CostModifier's player-id candidate and a
    // StatAura's die-id candidate go through the exact same check.
    // includeContinuous: false throughout - a continuous template's own
    // eligibility must be decided from BASE state only (see QueryEngine.
    // GetBaseTags/GetBaseStatValue's own remarks), or a self-referential
    // aura recurses into evaluating itself. Found as an actual
    // StackOverflow while writing this phase's tests, not anticipated.
    private static bool SourceQualifies(GameState state, DieInstance source, Condition? activeWhen, TargetFilter filter, string candidateId)
    {
        if (activeWhen is not null && !ConditionEvaluator.Evaluate(state, source.ControllerId, activeWhen, Bindings(source), includeContinuous: false))
            return false;
        return TargetResolver.Query(state, source.ControllerId, filter, Bindings(source), includeContinuous: false).Contains(candidateId);
    }

    private static IEnumerable<DieInstance> QualifyingSources(GameState state, CardDef card, Condition? activeWhen, TargetFilter filter, string candidateId) =>
        ActiveSourceDice(state, card).Where(src => SourceQualifies(state, src, activeWhen, filter, candidateId));

    // --- StatAura ---

    private sealed class StatAuraModifier(CardDef card, StatAura def, Amount amount) : IDieStatModifier
    {
        public bool AppliesTo(GameState state, DieInstance die) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Target, die.Id).Any();

        public int GetDelta(GameState state, DieInstance die) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Target, die.Id)
                .Sum(src => AmountResolver.Resolve(state, src.ControllerId, amount, Bindings(src), includeContinuous: false));
    }

    // --- CostModifier. Purchase/GlobalEnergy are card+payer scoped, so
    // `Whose` is expected to resolve to PLAYER ids (Kind:Player - Jean
    // Grey "Xavier's Dream", V2_VOCABULARY.md Part 2); Fielding/
    // ActionDieUse are DIE scoped, so `Whose` is expected to resolve to
    // DIE ids instead (Kind:CharacterDie - Deadpool "Collect THIS!",
    // same Part 2 example). One field, two id spaces depending on which
    // registry it lands in - the adapter picks the right candidate id to
    // check against, matching each real card's own authored Whose shape. ---

    private sealed class CardCostModifierAdapter(CardDef card, CostModifier def) : ICardCostModifier
    {
        public bool AppliesTo(GameState state, CardDef _, string payerId) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Whose, payerId).Any();

        public int GetDelta(GameState state, CardDef _, string payerId) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Whose, payerId).Count() * def.Delta;
    }

    private sealed class DieCostModifierAdapter(CardDef card, CostModifier def) : IDieStatModifier
    {
        public bool AppliesTo(GameState state, DieInstance die) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Whose, die.Id).Any();

        public int GetDelta(GameState state, DieInstance die) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Whose, die.Id).Count() * def.Delta;
    }

    // --- TagAura ---

    private sealed class TagAuraModifier(CardDef card, TagAura def) : ITagAuraModifier
    {
        public bool AppliesTo(GameState state, DieInstance die) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Target, die.Id).Any();

        public IReadOnlyList<string> GetTags(GameState state, DieInstance die) => def.Tags;
    }

    // --- AbilityBlank ---

    private sealed class AbilityBlankModifier(CardDef card, AbilityBlank def) : IAbilityBlankModifier
    {
        public bool AppliesTo(GameState state, DieInstance die) =>
            // BlankSafeSources, not QualifyingSources: asking whether this
            // blank's own source is itself blanked must not consult
            // continuous blanks, or the question evaluates itself. See
            // QueryEngine.AbilitiesActiveBase.
            BlankSafeSources(state, card, def.ActiveWhen, def.Target, die.Id).Any();
    }

    private static IEnumerable<DieInstance> BlankSafeSources(
        GameState state, CardDef card, Condition? activeWhen, TargetFilter filter, string candidateId) =>
        state.Dice
            .Where(d => d.CardId == card.Id
                && d.Zone is Zone.FieldZone or Zone.AttackZone
                && QueryEngine.AbilitiesActiveBase(state, d))
            .Where(src => SourceQualifies(state, src, activeWhen, filter, candidateId));

    // --- Lockout ---

    private sealed class LockoutModifier(CardDef card, Lockout def) : ILockoutModifier
    {
        public bool Applies(GameState state, string playerId, string cardId, SuppressionKind kind)
        {
            if (def.Kind != kind) return false;

            foreach (var source in ActiveSourceDice(state, card))
            {
                // It is the SOURCE's opponent who is locked out, so a Blob
                // on the other side of the table locks nothing of yours.
                if (state.OpponentOf(source.ControllerId) != playerId) continue;

                if (def.ActiveWhen is not null
                    && !ConditionEvaluator.Evaluate(state, source.ControllerId, def.ActiveWhen, Bindings(source), includeContinuous: false))
                {
                    continue;
                }

                // A card named outright (Magneto's Professor X clause), or
                // one chosen when fielded and read back from the memory.
                var locked = def.CardId
                    ?? (def.MemoryName is { } name
                        && state.Memories.TryGetValue((source.ControllerId, card.Id, name), out var chosen)
                        ? chosen
                        : null);

                if (locked == cardId) return true;
            }

            return false;
        }
    }

    // --- CombatRule (no consumer yet - Phase 7) ---

    private sealed class CombatRuleModifier(CardDef card, CombatRule def) : ICombatRuleModifier
    {
        public CombatRuleKind Kind => def.Kind;
        public int? N => def.N;

        public bool AppliesTo(GameState state, DieInstance die) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Target, die.Id).Any();
    }

    // --- DamageModifier ---

    private sealed class DamageModifierInstance(CardDef card, DamageModifier def) : IDamageInterceptor
    {
        public DamageModifierMode Mode => def.Mode;

        public bool AppliesTo(GameState state, DieInstance die, DamageSource source)
        {
            if (def.Source != DamageSource.Any && def.Source != source) return false;
            return QualifyingSources(state, card, def.ActiveWhen, def.Target, die.Id).Any();
        }

        public int GetAmount(GameState state, DieInstance die) => def.Amount ?? 0;

        // Redirects to the first qualifying source die itself (an
        // "instead, damage comes to me" shield) - if more than one copy
        // of the card is active, the first one found takes it; no
        // authored card needs a tie-break rule finer than that yet.
        public DieInstance? RedirectTarget(GameState state, DieInstance originalTarget) =>
            QualifyingSources(state, card, def.ActiveWhen, def.Target, originalTarget.Id).FirstOrDefault();
    }

    // --- TargetingProtection ---

    private sealed class TargetingProtectionInterceptor(CardDef card, TargetingProtection def) : ITargetingInterceptor
    {
        // Protection is always against the OPPONENT of the granting
        // player (every real card text reads "your opponent can't
        // target..." - Angel "Xavier's Dream" is the precedent, see
        // V2_VOCABULARY.md Part 2) - a source never blocks its own
        // controller's own targeting.
        public bool CanBeTargeted(GameState state, DieInstance die, string byPlayerId, ProtectionFrom triggerKind)
        {
            if (def.From != ProtectionFrom.Both && def.From != triggerKind) return true;

            var blockedBy = ActiveSourceDice(state, card)
                .Where(src => src.ControllerId != byPlayerId)
                .Where(src => SourceQualifies(state, src, def.ActiveWhen, def.Target, die.Id));

            return !blockedBy.Any();
        }
    }
}
