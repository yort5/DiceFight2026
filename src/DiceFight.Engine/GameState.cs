using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;

namespace DiceFight.Engine;

// Root mutable game state: players, every die in play, whose turn it is.
// CardCatalog is the static card database (keyed by CardDef.Id) shared
// across the game, not per-player - matches the "cards are always laid
// out for players to see" framing in the rulebook's foreword.
public sealed class GameState
{
    public required IReadOnlyDictionary<string, CardDef> CardCatalog { get; init; }
    public required Player PlayerOne { get; init; }
    public required Player PlayerTwo { get; init; }
    public List<DieInstance> Dice { get; } = [];

    public string ActivePlayerId { get; set; } = string.Empty;
    public TurnStep CurrentStep { get; set; } = TurnStep.ClearAndDraw;
    public AttackSubStep AttackSubStep { get; set; } = AttackSubStep.NotInAttack;

    // Rule 2.3.3 - the very first turn of the game draws one fewer die.
    public bool IsFirstTurn { get; set; } = true;

    // Rule 1.2.3(3) - at most one Epic Basic Action die may be used or
    // obtained per turn; reset in CleanUp.
    public bool EpicBasicActionUsedThisTurn { get; set; }

    // Card-text-driven once-per-turn Global limiters (e.g. Falcon's "Once
    // during your turn"), keyed by cardId; reset in CleanUp.
    public HashSet<string> GlobalsUsedThisTurn { get; } = [];

    // Die ids forced to block this turn (e.g. Invisible Woman's Global),
    // enforced by CombatEngine.DeclareBlockers; reset in CleanUp.
    public HashSet<string> MustBlockThisTurn { get; } = [];

    // Keyword Deadly - die ids engaged (rule 2.7.2.3) with a Deadly die
    // this combat; recorded by CombatEngine.DeclareBlockers at the
    // moment of engagement (not at combat damage - rule Appendix 1
    // Deadly, clarification 1), resolved (KO'd) and cleared by
    // TurnEngine.CleanUp regardless of what happened to either die in
    // between. Turn-scoped like MustBlockThisTurn/CallOutTargets.
    public HashSet<string> DeadlyEngagedDieIds { get; } = [];

    // Keyword Call Out - attacker die id -> the opposing Character die it
    // targeted when it attacked (set by the SetCallOutTarget effect,
    // enforced by CombatEngine.DeclareBlockers). Scoped to one combat:
    // cleared at the start of every DeclareAttackers call, since Call
    // Out's targeting choice only exists during the Attack Step it was
    // declared in - unlike MustBlockThisTurn, this isn't turn-scoped.
    public Dictionary<string, string> CallOutTargets { get; } = [];

    // Keyword Obscure - CardIds whose dice are all unblockable for the rest
    // of this turn (set by TurnEngine.UseActionDie, enforced by
    // CombatEngine.DeclareBlockers/ActiveCallOutTargets). Turn-scoped like
    // MustBlockThisTurn, since "until end of turn" is the keyword's own text.
    public HashSet<string> ObscuredCardIds { get; } = [];

    // Die ids fielded this turn (rule 2.6.2's "Field a Character die"
    // game action, populated by TurnEngine.Field) - keyword Strike's own
    // "no other Character dice fielded this turn" check reads this live
    // (see DieStats.HasStrikeBonus). A historical record of what happened
    // this turn, not current board state - a fielded die that later left
    // play (KO'd, etc.) still counts. Turn-scoped like MustBlockThisTurn.
    public HashSet<string> FieldedThisTurn { get; } = [];

    // Keyword Experience - "if you KO'd an opposing Monster during your
    // turn" (the printed reminder text's own simplified wording - see
    // TurnEngine.CleanUp's remarks). Set by DieStats.ForceKO the moment a
    // real KO (not a Regenerate-intercepted one) lands on a die
    // controlled by the active player's opponent whose card has the
    // "Monster" affiliation; only the active player can ever earn a
    // token off it (Experience's own "on your turn" framing), so this
    // doesn't need to record *which* Monster or track anything per-die.
    // Turn-scoped like MustBlockThisTurn - reset in CleanUp, after
    // CleanUp's own token-granting check reads it.
    public bool OpposingMonsterKOdThisTurn { get; set; }

    // Keyword Experience Tokens - the first persistent, cross-turn,
    // per-CARD (not per-die, not per-turn) counter this engine has
    // needed. CardDef is otherwise static/immutable data ("never
    // mutates during a game" - see its own remarks), so this lives here
    // instead, keyed by CardId. Each token is worth +1A/+1D to every die
    // of that card (see DieStats.ExperienceBonus), forever, until
    // removed by another ability - nothing removes them yet, matching
    // "consider these tokens as permanent modifiers until specifically
    // removed by another ability." Loyalty Counters (Appendix 1) are the
    // same shape - a natural home for those too, if a card ever needs them.
    public Dictionary<string, int> ExperienceTokens { get; } = [];

    // A mid-resolution choice (keyword Corrupt, RedrawFromBag) that
    // can't be answered in the same request that triggered it - see
    // PendingChoice's own remarks. Null the rest of the time. Not turn-
    // scoped like everything else above - it's cleared the moment
    // GamesController.ResolvePendingChoice answers it, not at CleanUp
    // (a game should never reach CleanUp with one still outstanding,
    // since every other action endpoint refuses to run while it's set).
    public PendingChoice? PendingChoice { get; set; }

    // Whatever was still queued in the Drain call that PendingChoice
    // paused, preserved so ResolvePendingChoice can pick up exactly
    // where it left off instead of losing anything still waiting to
    // resolve. Only non-null while PendingChoice is also set.
    public AbilityQueue? PendingQueue { get; set; }

    public Player GetPlayer(string playerId) =>
        playerId == PlayerOne.Id ? PlayerOne
        : playerId == PlayerTwo.Id ? PlayerTwo
        : throw new ArgumentException($"Unknown player id '{playerId}'.", nameof(playerId));

    // Lets a resolved target id (e.g. from TargetSpec.CharacterDieOrPlayer)
    // be told apart from a die id without a throwing lookup.
    public bool IsPlayerId(string id) => id == PlayerOne.Id || id == PlayerTwo.Id;

    public string OpponentOf(string playerId) =>
        playerId == PlayerOne.Id ? PlayerTwo.Id
        : playerId == PlayerTwo.Id ? PlayerOne.Id
        : throw new ArgumentException($"Unknown player id '{playerId}'.", nameof(playerId));

    public IEnumerable<DieInstance> DiceFor(string playerId) =>
        Dice.Where(d => d.ControllerId == playerId);

    public IEnumerable<DieInstance> DiceIn(string playerId, Zone zone) =>
        DiceFor(playerId).Where(d => d.Zone == zone);

    // Rule 2.1.1/2.1.8 - each player starts with 8 Sidekick dice in their bag.
    public static GameState NewGame(
        IReadOnlyDictionary<string, CardDef> catalog,
        Player playerOne,
        Player playerTwo)
    {
        var state = new GameState
        {
            CardCatalog = catalog,
            PlayerOne = playerOne,
            PlayerTwo = playerTwo,
            ActivePlayerId = playerOne.Id
        };

        foreach (var player in new[] { playerOne, playerTwo })
        {
            for (var i = 0; i < 8; i++)
            {
                state.Dice.Add(new DieInstance
                {
                    Id = $"{player.Id}-sidekick-{i}",
                    CardId = null,
                    OwnerId = player.Id,
                    ControllerId = player.Id,
                    Zone = Zone.Bag,
                    Status = DieStatus.Unrolled
                });
            }

            // Rule 2.1.3 - each team's Character/Action/Basic Action dice
            // sit unpurchased on their cards until bought.
            TeamSetup.SetupTeamDice(state, player, catalog);
        }

        return state;
    }
}
