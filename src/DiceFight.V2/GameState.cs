using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Root mutable game state. Config is the GameConfig this game is running
// under (games-as-data - nothing in TurnEngine should read a constant that
// isn't reachable from here); CardCatalog is the card database, same
// "shared, not per-player" shape v1 used.
public sealed class GameState
{
    public required GameConfig Config { get; init; }
    public required IReadOnlyDictionary<string, CardDef> CardCatalog { get; init; }
    public required Player PlayerOne { get; init; }
    public required Player PlayerTwo { get; init; }
    public List<DieInstance> Dice { get; } = [];

    public string ActivePlayerId { get; set; } = string.Empty;

    // Spike C - the engine's position is a cursor into Config.Steps (one
    // flat ordered list), not a pair of enums. CurrentStep stays readable
    // and settable as a PHASE for the many callers that only care about
    // containment ("Main or Attack"); setting it parks the cursor on that
    // phase's first step, which is what every "jump straight to the Main
    // Step" caller means.
    public int CurrentStepIndex { get; set; }

    public TurnStepDef CurrentStepDef => Config.Steps[CurrentStepIndex];
    public string CurrentStepId => CurrentStepDef.Id;

    public TurnStep CurrentStep
    {
        get => CurrentStepDef.Phase;
        set
        {
            var index = -1;
            for (var i = 0; i < Config.Steps.Count; i++)
            {
                if (Config.Steps[i].Phase != value) continue;
                index = i;
                break;
            }
            CurrentStepIndex = index >= 0
                ? index
                : throw new ArgumentException($"This game's step list declares no step in phase '{value}'.", nameof(value));
        }
    }

    // Moves the cursor to a specific step by id (engine use - the step
    // machine advances one entry at a time rather than jumping phases).
    public void MoveToStep(string stepId)
    {
        for (var i = 0; i < Config.Steps.Count; i++)
        {
            if (Config.Steps[i].Id != stepId) continue;
            CurrentStepIndex = i;
            return;
        }
        throw new ArgumentException($"This game's step list declares no step '{stepId}'.", nameof(stepId));
    }

    // Rule 2.3.3 - the very first turn of the game draws one fewer die.
    // A whole-GAME flag (only ever true before the first ClearAndDraw),
    // not a per-player-turn one - ported from v1.
    public bool IsFirstTurn { get; set; } = true;

    // Finding 13 - the only card-scoped-not-die-scoped state in the model.
    // Keyed by (player, cardId, counterName) since counters belong to a
    // controller's copy of a card, not a specific die of it (v1's own
    // LoyaltyCounters/ExperienceTokens precedent, generalized to a name
    // instead of one dictionary per counter kind).
    public Dictionary<(string PlayerId, string CardId, string CounterName), int> Counters { get; } = [];

    // CARD-SCOPED suppression (V2_VOCABULARY_HISTORY.md Parts 20-21), keyed the
    // same way Counters is. Three independent things one card can have
    // turned off for one player:
    //
    //   TextIgnored  - Mister Sinister, Scarlet Witch, both Shrieks,
    //                  Prismatic Spray, Typhoid Mary, Kryptonite,
    //                  Wolverine "No More Distractions", Scarlet Spider x2
    //   CantPurchase - Blob, Drax
    //   CantField    - Blob, Drax, Magneto AOU139's Professor X clause
    //
    // Card-scoped rather than die-scoped for two reasons the text itself
    // gives: it has to cover copies NOT YET IN PLAY ("ignore all text on
    // opposing character cards"), and Globals are card-scoped (rule
    // 2.6.5.2) so a die-scoped blank could never turn one off - which
    // four of these cards explicitly say they do.
    public List<CardSuppression> CardSuppressions { get; } = [];

    // "Choose an opposing card, replacing all previous choices" - the
    // memory RememberCard writes and Lockout/AbilityBlank read back.
    // Keyed like Counters, which is what makes "replacing" automatic.
    public Dictionary<(string PlayerId, string CardId, string MemoryName), string> Memories { get; } = [];

    // The continuous-modifier registries QueryEngine reads (V2_PLAN.md
    // Phase 3 task 1) - empty until Phase 6's continuous templates start
    // registering into them. Per-game-instance (not static) so concurrent
    // games never share modifier state. Five separate lists rather than
    // one, since IDieStatModifier/ICardCostModifier genuinely check
    // different things (a die vs. a card+payer) - see QueryEngine's own
    // remarks.
    public List<IDieStatModifier> AttackModifiers { get; } = [];
    public List<IDieStatModifier> DefenseModifiers { get; } = [];
    public List<IDieStatModifier> FieldingCostModifiers { get; } = [];
    public List<ICardCostModifier> PurchaseCostModifiers { get; } = [];
    public List<ICardCostModifier> GlobalEnergyCostModifiers { get; } = [];
    public List<ITargetingInterceptor> TargetingInterceptors { get; } = [];

    // Phase 6 additions - the remaining three continuous-template
    // registries (StatAura/CostModifier/TargetingProtection already had a
    // home above via Phase 3's own five). ActionDieUseCostModifiers and
    // CombatRules have no query consumer yet (Action-die mechanics and
    // Combat are both still unbuilt), same "register the full closed
    // vocabulary now, wire the consumer when its phase arrives" pattern
    // Phase 4 already used for DieKOd/DieDamaged. Populated once by
    // ContinuousRegistry.RegisterAll (GameSetup.NewGame's own call).
    public List<ITagAuraModifier> TagAuras { get; } = [];
    public List<IAbilityBlankModifier> AbilityBlanks { get; } = [];
    public List<ILockoutModifier> Lockouts { get; } = [];
    public List<IDieStatModifier> ActionDieUseCostModifiers { get; } = [];
    public List<ICombatRuleModifier> CombatRules { get; } = [];
    public List<IDamageInterceptor> DamageInterceptors { get; } = [];

    // Card-text-driven once-per-turn Global limiters (V2_PLAN.md Phase 4
    // task 4, e.g. v1's Falcon "Once during your turn") - keyed by
    // (player, cardId) since the limit is per-controller-per-card, not
    // global across both players. Reset in TurnEngine.CleanUp, same
    // turn-scoped lifetime as every other "ThisTurn" tracker in v1.
    public HashSet<(string PlayerId, string CardId)> GlobalsUsedThisTurn { get; } = [];

    // Phase 5 additions.
    //
    // Non-null = the game is paused waiting for a player answer (see
    // Model/PendingChoice.cs) - the only legal next action is answering
    // it (EffectInterpreter.AnswerPendingChoice); nothing else should
    // drain the AbilityQueue further while this is set.
    public PendingChoice? PendingChoice { get; set; }

    // PurchaseModifier's one-shot "your next purchase (matching CardKind,
    // if set) gets Delta off and/or goes to GoesToZone instead of the Used
    // Pile" grant (V2_PLAN.md Appendix A: GrantNextPurchaseDiscount /
    // GrantNextPurchaseGoesToBag). Unlike AppliedModifier/GrantedTag this
    // isn't a per-die stat - it's a per-controller standing offer consumed
    // by the next matching TurnEngine.Purchase call, or discarded unused
    // at CleanUp (rule text is always "this turn"; nothing in the closed
    // vocabulary asks for a longer-lived version).
    public List<PendingPurchaseModifier> PendingPurchaseModifiers { get; } = [];

    // Turn-scoped trackers TurnFact/NoKOsThisTurn read (Phase 5) - reset
    // in CleanUp alongside GlobalsUsedThisTurn, same "ThisTurn" lifetime.
    // FieldedCharacterThisTurn deliberately doesn't exclude the ability's
    // own just-fielded die from its own "no OTHER character" check - a
    // known simplification (not exercised by Phase 5's own test coverage,
    // which only needs one TurnFact case); revisit once a real migrated
    // card (Phase 8) actually needs the precise "other than itself" reading.
    public HashSet<string> PurchasedThisTurn { get; } = [];
    public HashSet<string> FieldedCharacterThisTurn { get; } = [];
    public HashSet<string> CharacterDiceKOdThisTurn { get; } = [];

    // Rule 2.6.1 - each die may be voluntarily rerolled at most once
    // during its own Roll and Reroll Step. Keyed by die id, not player,
    // since TurnEngine.RerollOwn checks a specific die; cleared at the
    // top of TurnEngine.Roll (once per turn, before that turn's window
    // opens) rather than at ClearAndDraw/CleanUp - unlike the other
    // trackers above, nothing needs to read this after the step ends.
    public HashSet<string> RerolledThisStep { get; } = [];

    public bool IsPlayerId(string id) => id == PlayerOne.Id || id == PlayerTwo.Id;

    public Player GetPlayer(string playerId) =>
        playerId == PlayerOne.Id ? PlayerOne
        : playerId == PlayerTwo.Id ? PlayerTwo
        : throw new ArgumentException($"Unknown player id '{playerId}'.", nameof(playerId));

    public string OpponentOf(string playerId) =>
        playerId == PlayerOne.Id ? PlayerTwo.Id
        : playerId == PlayerTwo.Id ? PlayerOne.Id
        : throw new ArgumentException($"Unknown player id '{playerId}'.", nameof(playerId));

    public IEnumerable<DieInstance> DiceFor(string playerId) => Dice.Where(d => d.ControllerId == playerId);

    public IEnumerable<DieInstance> DiceIn(string playerId, Zone zone) => DiceFor(playerId).Where(d => d.Zone == zone);

    // Resolves a die's own DieDefinition - CardCatalog[CardId].Die for a
    // card-owned die, or the matching BasicDicePool entry for a pool die.
    // The one non-obvious lookup DieInstance's slimmer shape (see its own
    // remarks) depends on; every face-dependent computation goes through
    // this rather than storing derived face facts on the die itself.
    public DieDefinition GetDieDefinition(DieInstance die)
    {
        if (die.CardId is { } cardId)
            return CardCatalog[cardId].Die;

        if (die.PoolDieId is { } poolDieId)
        {
            foreach (var entry in Config.BasicDicePool)
            {
                if (entry.Die.Id == poolDieId) return entry.Die;
            }
            throw new InvalidOperationException($"Die '{die.Id}' references unknown pool die '{poolDieId}'.");
        }

        throw new InvalidOperationException($"Die '{die.Id}' has neither CardId nor PoolDieId set.");
    }

    // The face currently showing, or null if the die is unrolled/dormant.
    public Face? GetCurrentFace(DieInstance die) =>
        die.CurrentFaceIndex is { } index ? GetDieDefinition(die).Faces[index] : null;
}

public sealed record PendingPurchaseModifier(string PlayerId, int Delta, CardType? CardKind, Zone? GoesToZone);

/// <summary>
/// One card's text/purchase/field turned off for one player, until
/// <paramref name="Duration"/> expires. The continuous half of blanking
/// does not live here - a conditional blank is recomputed on read (see
/// QueryEngine.CardTextActive), because storing it would mean re-flipping
/// a flag on every field, KO, spin or cost change.
/// </summary>
public sealed record CardSuppression(
    string PlayerId, string CardId, SuppressionKind Kind, Duration Duration, string? GrantedDuringPlayerId = null);

public enum SuppressionKind
{
    /// <summary>The card's text box is ignored, Globals included.</summary>
    TextIgnored,
    CantPurchase,
    CantField,
}
