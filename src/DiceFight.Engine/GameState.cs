using DiceFight.Engine.Model;

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
