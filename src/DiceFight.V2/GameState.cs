using DiceFight.V2.Model;

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
    public TurnStep CurrentStep { get; set; } = TurnStep.ClearAndDraw;

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
