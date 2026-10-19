using DiceFight.V2;
using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.Api.Accounts;
using DiceFight.Api.Recording;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DiceFight.Api.Controllers;

// v3 "Dice Kingdom" API - Phase 3 of the mellow-sparking-comet plan.
// Mirrors GamesController.cs's shape (seat tokens, RequireTurn/Result/
// Drain helpers, one endpoint per TurnEngine/CombatEngine action) against
// DiceFight.V2 + DiceKingdomConfig instead of v1's DiceFight.Engine +
// SampleCards - a parallel controller, not a shared abstraction, matching
// V2_PLAN.md's own Phase 9 note ("keeps v1 untouched").
//
// Champion games still use their preset team definitions. Custom games can
// instead specify the exact catalog cards and Tardigrades for either side. No Global abilities/Range/Tag Out/Continuous-die
// endpoints - none of DiceKingdomConfig's Characters use those
// mechanisms, so there is nothing for them to drive yet.
[ApiController]
[Route("api/v2/games")]
public sealed class V2GamesController(V2GameStore store) : ControllerBase, IActionFilter
{
    // The game's own seeded generator (V2GameSession.Rng) - set by
    // RequireSeat, which every action calls first.
    private Random Rng => _session?.Rng ?? throw new InvalidOperationException("No game session.");

    [HttpPost]
    public ActionResult<V2CreatedGameDto> Create([FromBody] CreateV2GameRequest request)
    {
        var config = DiceKingdomConfig.Config;
        var catalog = DiceKingdomConfig.Catalog;

        Player playerOne;
        Player playerTwo;
        try
        {
            playerOne = BuildSelectedPlayer("teamA", request.PlayerOneChampionId, request.PlayerOneCustomTeam);
            playerTwo = BuildSelectedPlayer("teamB", request.PlayerTwoChampionId, request.PlayerTwoCustomTeam);
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { error = e.Message });
        }

        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        var session = store.Create(state);
        store.Accounts?.GameCreated(session, HttpContext.Player());

        return Ok(new V2CreatedGameDto(
            V2GameStateDto.From(session.Id, state, state.PlayerOne.Id),
            session.Seats.Select(seat => new SeatDto(seat.PlayerId, seat.Token)).ToList()));
    }

    // Open an online game with only your own Champion picked (2026-09-30,
    // user request - it used to take picking BOTH, starting, then copying
    // the invite link from inside the match). Returns both seats: the host
    // keeps teamA and turns teamB into the invite link. The game itself is
    // built when the invited player picks (Join).
    [HttpPost("open")]
    public ActionResult<V2OpenGameDto> Open([FromBody] OpenV2GameRequest request)
    {
        string label;
        try
        {
            // Builds the host's team only to validate it; Join builds it for real.
            label = TeamLabel(BuildSelectedPlayer("teamA", request.ChampionId, request.CustomTeam), request.CustomTeam);
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { error = e.Message });
        }
        var hostChampionId = request.CustomTeam is { } team ? team.ChampionId : request.ChampionId;
        var lobby = store.OpenLobby(hostChampionId, request.CustomTeam, label, "teamA", "teamB");
        store.Accounts?.LobbyOpened(lobby, HttpContext.Player());
        return Ok(new V2OpenGameDto(lobby.Id, lobby.HostChampionId ?? "",
            lobby.Seats.Select(seat => new SeatDto(seat.PlayerId, seat.Token)).ToList(), lobby.HostLabel));
    }

    // "Wolf", "Wolf - custom team", or "a custom team" - what the other
    // player is told they're up against.
    private static string TeamLabel(Player player, CustomV2TeamRequest? custom) =>
        custom is null ? player.Name
        : player.ChampionId is null ? "a custom team"
        : $"{player.Name} - custom team";

    // Waiting or started? Either seat's token works; the host polls this
    // while waiting, and the invited player reads the host's pick from it.
    [HttpGet("{gameId}/lobby")]
    public ActionResult<V2LobbyDto> Lobby(string gameId)
    {
        var token = Request.Headers[SeatTokenHeader].ToString();
        if (store.GetLobby(gameId) is { } lobby)
        {
            var seat = lobby.Seats.FirstOrDefault(s => s.Token == token)
                ?? throw new SeatRequiredException($"A valid {SeatTokenHeader} is required for this game.");
            return Ok(new V2LobbyDto(lobby.Id, lobby.HostChampionId ?? "", Started: false, seat.PlayerId, lobby.HostLabel));
        }
        var (session, playerId) = RequireSeat(gameId); // started (or never a lobby) - a normal game
        return Ok(new V2LobbyDto(session.Id, session.State.PlayerOne.ChampionId ?? "", Started: true, playerId, session.State.PlayerOne.Name));
    }

    // The invited player picks their Champion (or custom team); the real game starts now,
    // under the lobby's own id and seat tokens.
    [HttpPost("{gameId}/join")]
    public ActionResult<V2GameStateDto> Join(string gameId, [FromBody] JoinV2GameRequest request)
    {
        var lobby = store.GetLobby(gameId)
            ?? (store.HasGame(gameId)
                ? throw new InvalidOperationException("That game has already started.")
                : throw new KeyNotFoundException($"No V2 game with id '{gameId}'."));
        var token = Request.Headers[SeatTokenHeader].ToString();
        var guest = lobby.Seats[1];
        if (guest.Token != token)
            throw new SeatRequiredException("Only the invited player can pick the other Champion.");

        Player host, guestPlayer;
        try
        {
            host = BuildSelectedPlayer(lobby.Seats[0].PlayerId, lobby.HostTeam is null ? lobby.HostChampionId : null, lobby.HostTeam);
            guestPlayer = BuildSelectedPlayer(guest.PlayerId, request.ChampionId, request.CustomTeam);
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { error = e.Message });
        }
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, host, guestPlayer);
        var session = store.StartLobby(lobby, state);
        store.Accounts?.LobbyJoined(lobby, session, HttpContext.Player());
        _session = session;
        _seatPlayerId = guest.PlayerId;
        session.MarkChanged();
        Priority.Sync(state);
        return Ok(V2GameStateDto.From(session.Id, state, guest.PlayerId, session.Version));
    }

    // A custom team is checked for data validity only - house formats change
    // the usual caps (12 Tardigrades, 30 dice, 3 Basic Actions, ...), so the
    // builder enforces whichever format it was asked for. This ceiling is
    // just far above any format: without it one request could ask for two
    // billion dice and take the server down.
    public const int MaxCustomDice = 100;

    private static Player BuildSelectedPlayer(string id, string? championId, CustomV2TeamRequest? custom)
    {
        if (custom is null)
        {
            if (string.IsNullOrWhiteSpace(championId))
                throw new ArgumentException($"Player {id} needs a Champion or a custom team.");
            return BuildPlayer(id, championId);
        }
        if (!string.IsNullOrWhiteSpace(championId))
            throw new ArgumentException($"Player {id} cannot use a Champion with a custom team.");
        if (custom.Cards is null || custom.Tardigrades is null)
            throw new ArgumentException("Custom teams must specify cards and Tardigrades.");

        ChampionDef? champion = null;
        if (!string.IsNullOrWhiteSpace(custom.ChampionId))
            champion = DiceKingdomConfig.Champions.FirstOrDefault(c => c.Id == custom.ChampionId)
                ?? throw new ArgumentException($"Unknown Champion id '{custom.ChampionId}'.");

        var player = new Player { Id = id, Name = champion?.Name ?? "Custom Team", ChampionId = champion?.Id,
            CustomCardDiceCounts = new Dictionary<string, int>() };
        var cardIds = new HashSet<string>();
        foreach (var entry in custom.Cards)
        {
            if (entry is null || entry.DiceCount <= 0 ||
                string.IsNullOrWhiteSpace(entry.CardId) ||
                !DiceKingdomConfig.TeamBuilderCatalog.TryGetValue(entry.CardId, out var card) ||
                !cardIds.Add(entry.CardId))
                throw new ArgumentException("Invalid or duplicate card in custom team.");
            if (!card.IsImplemented)
                throw new ArgumentException($"{card.Name} is Incomplete and cannot be used in a game yet.");
            if (!DiceKingdomConfig.Catalog.ContainsKey(entry.CardId))
                throw new ArgumentException($"{card.Name} is not available in Dice Kingdom games yet.");
            // A Champion's team plays only Basic Actions of its own energy
            // (user, 2026-10-19).
            if (champion is not null && card.CardType == CardType.BasicAction &&
                BasicActionEnergy(card) != champion.EnergySymbolId)
                throw new ArgumentException($"{card.Name} is a {BasicActionEnergy(card)} Basic Action - {champion.Name} can only take {champion.EnergySymbolId} ones.");
            // No server-side team-size or printed die-limit enforcement.
            player.TeamCardIds.Add(entry.CardId);
            player.CustomCardDiceCounts[entry.CardId] = entry.DiceCount;
        }

        var types = new HashSet<string>();
        var pool = new List<BasicDicePoolEntry>();
        foreach (var entry in custom.Tardigrades)
        {
            if (entry is null || entry.DiceCount < 0 || !types.Add(entry.EnergyType))
                throw new ArgumentException("Invalid or duplicate Tardigrade entry.");
            try { pool.Add(DiceKingdomConfig.CustomTardigrade(entry.EnergyType, entry.DiceCount)); }
            catch (ArgumentException e) { throw new ArgumentException(e.Message); }
        }
        if (custom.Cards.Sum(c => (long)c.DiceCount) > MaxCustomDice)
            throw new ArgumentException($"A custom team can have at most {MaxCustomDice} character dice.");
        if (custom.Tardigrades.Sum(t => (long)t.DiceCount) > MaxCustomDice)
            throw new ArgumentException($"A custom team can have at most {MaxCustomDice} Tardigrades.");
        player.CustomBasicDicePool = pool;
        return player;
    }

    // The energy a Basic Action's die shows on its energy faces - the same
    // reading V2CardDefDto.DieEnergyType gives the client.
    public static string? BasicActionEnergy(CardDef card) =>
        card.Die.Faces.SelectMany(f => f.Symbols).Select(sym => sym.SymbolId).FirstOrDefault();

    private static Player BuildPlayer(string id, string championId)
    {
        var champion = DiceKingdomConfig.Champions.FirstOrDefault(c => c.Id == championId)
            ?? throw new InvalidOperationException($"Unknown Champion id '{championId}'.");
        var player = new Player { Id = id, Name = champion.Name, ChampionId = champion.Id };
        player.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[champion.Id]);
        // The Champion's own Basic Action - community dice (either player
        // may buy them), and its Global is usable by either player.
        player.TeamCardIds.Add(DiceKingdomConfig.ActionByChampion[champion.Id]);
        return player;
    }

    [HttpGet("cards")]
    public ActionResult<IReadOnlyList<V2CardDefDto>> Cards() =>
        Ok(DiceKingdomConfig.TeamBuilderCatalog.Values.Select(V2CardDefDto.From).ToList());

    [HttpGet("champions")]
    public ActionResult<IReadOnlyList<ChampionDto>> Champions() =>
        Ok(DiceKingdomConfig.Champions.Select(ChampionDto.From).ToList());

    [HttpGet("{gameId}")]
    public ActionResult<V2GameStateDto> Get(string gameId)
    {
        var (session, _) = RequireSeat(gameId);
        return Ok(Result(gameId, session.State));
    }

    // The shared computer opponent's next move for the calling seat
    // (DiceFight.V2/Bot) - 204 No Content when it isn't that seat's
    // decision. Read-only: the client carries the move out through the
    // matching endpoint itself, so its pacing/animation stays client-side.
    // `skip`: comma-separated die ids the client already saw rejected this
    // turn (a rule the bot doesn't model), never offered again.
    [HttpGet("{gameId}/bot-decision")]
    public ActionResult<V2BotDecisionDto> BotDecision(string gameId, [FromQuery] string? skip)
    {
        var (session, playerId) = RequireSeat(gameId);
        var skipIds = (skip ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        store.Accounts?.BotSeat(session, playerId);
        var decision = DiceKingdomBot.Decide(session.State, playerId, skipIds);
        return decision is null ? NoContent() : Ok(V2BotDecisionDto.From(decision));
    }

    [HttpPost("{gameId}/clear-and-draw")]
    public ActionResult<V2GameStateDto> ClearAndDraw(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, queue, Rng);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/roll")]
    public ActionResult<V2GameStateDto> Roll(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.Roll(state, queue, new DiceFight.V2.RandomDiceRoller(Rng));
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/reroll")]
    public ActionResult<V2GameStateDto> Reroll(string gameId, [FromBody] V2RerollRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.RerollOwn(state, queue, new DiceFight.V2.RandomDiceRoller(Rng), request.DieIds);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/finish-roll")]
    public ActionResult<V2GameStateDto> FinishRoll(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.FinishRoll(state, queue);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/purchase")]
    public ActionResult<V2GameStateDto> Purchase(string gameId, [FromBody] V2PurchaseRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        Priority.RequireHolder(state, state.ActivePlayerId);
        var queue = new AbilityQueue();
        TurnEngine.Purchase(state, queue, request.DieId, request.EnergyDieIds);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/field")]
    public ActionResult<V2GameStateDto> Field(string gameId, [FromBody] V2FieldRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        Priority.RequireHolder(state, state.ActivePlayerId);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, request.DieId, request.EnergyDieIds, request.Free);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/enter-attack-step")]
    public ActionResult<V2GameStateDto> EnterAttackStep(string gameId)
    {
        // "Done in Main, I'll attack" - the Active player's pass (Priority.cs).
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        Priority.Pass(state, queue, state.ActivePlayerId, skipAttack: false);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/skip-attack-step")]
    public ActionResult<V2GameStateDto> SkipAttackStep(string gameId)
    {
        // "Done in Main, no attack" - the Active player's pass (Priority.cs).
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        Priority.Pass(state, queue, state.ActivePlayerId, skipAttack: true);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/declare-attackers")]
    public ActionResult<V2GameStateDto> DeclareAttackers(string gameId, [FromBody] V2DeclareAttackersRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        var lanes = request.Attackers.ToDictionary(a => a.DieId, a => a.Lane);
        CombatEngine.DeclareAttackers(state, queue, lanes);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/declare-blockers")]
    public ActionResult<V2GameStateDto> DeclareBlockers(string gameId, [FromBody] V2DeclareBlockersRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Inactive);
        var assignment = BuildAssignment(request.Assignments);
        var blockerIds = request.Assignments.Select(a => a.BlockerDieId).Distinct().ToList();

        var queue = new AbilityQueue();
        CombatEngine.DeclareBlockers(state, queue, assignment, blockerIds);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/assign-combat-damage")]
    public ActionResult<V2GameStateDto> AssignCombatDamage(string gameId, [FromBody] V2AssignCombatDamageRequest request)
    {
        // The Active player's pass in the Action/Global window (Priority.cs).
        // Damage resolves when the window closes, from the blocks the
        // defender declared (GameState.DeclaredBlocks) - never from what
        // the attacker's client sends, which in two-device play never had
        // them. The engine splits each lane's damage itself.
        _ = request;
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        Priority.Pass(state, queue, state.ActivePlayerId);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    // Wolf / Armadillo / Owl's once-per-turn power (ChampionPowers). Its
    // target comes back as a pending choice. Like a Global, the Inactive
    // player using it hands priority back (Armadillo's, after blocks).
    [HttpPost("{gameId}/champion-power")]
    public ActionResult<V2GameStateDto> ChampionPower(string gameId)
    {
        var (session, playerId) = RequireSeat(gameId);
        var state = session.State;
        if (state.PendingChoice is not null)
            throw new InvalidOperationException("Resolve the pending choice before taking another action.");
        var queue = new AbilityQueue();
        ChampionPowers.Use(state, queue, playerId);
        Drain(state, queue);
        Priority.AfterGlobal(state, playerId);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/foresight")]
    public ActionResult<V2GameStateDto> Foresight(string gameId, [FromBody] V2UseActionRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        Priority.RequireHolder(state, state.ActivePlayerId);
        var queue = new AbilityQueue();
        TurnEngine.UseForesight(state, queue, new DiceFight.V2.RandomDiceRoller(Rng), state.ActivePlayerId, request.DieId);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/use-action")]
    public ActionResult<V2GameStateDto> UseAction(string gameId, [FromBody] V2UseActionRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        Priority.RequireHolder(state, state.ActivePlayerId);
        var queue = new AbilityQueue();
        TurnEngine.UseAction(state, queue, request.DieId);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/use-global")]
    public ActionResult<V2GameStateDto> UseGlobal(string gameId, [FromBody] V2UseGlobalRequest request)
    {
        var (session, playerId) = RequireSeat(gameId);
        var state = session.State;
        if (state.PendingChoice is not null)
            throw new InvalidOperationException("Resolve the pending choice before taking another action.");
        Priority.RequireHolder(state, playerId);
        // Only this game's own Globals - TurnEngine.UseGlobal accepts any
        // card in the catalog (see its own remarks on rosters).
        if (!state.PlayerOne.TeamCardIds.Concat(state.PlayerTwo.TeamCardIds).Contains(request.CardId))
            throw new InvalidOperationException("That card isn't in this game.");

        var queue = new AbilityQueue();
        TurnEngine.UseGlobal(state, queue, request.CardId, playerId, request.AbilityIndex, request.EnergyDieIds);
        Drain(state, queue);
        Priority.AfterGlobal(state, playerId);
        return Ok(Result(gameId, state));
    }

    // Whoever holds priority passes it (Priority.cs). The Active player
    // normally passes through enter-attack-step / skip-attack-step /
    // assign-combat-damage, which say what should happen when Main or the
    // action window ends; this is mostly the Inactive player's button.
    [HttpPost("{gameId}/pass")]
    public ActionResult<V2GameStateDto> Pass(string gameId)
    {
        var (session, playerId) = RequireSeat(gameId);
        var state = session.State;
        if (state.PendingChoice is not null)
            throw new InvalidOperationException("Resolve the pending choice before taking another action.");
        var queue = new AbilityQueue();
        Priority.Pass(state, queue, playerId);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/clean-up")]
    public ActionResult<V2GameStateDto> CleanUp(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.CleanUp(state, queue);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/resolve-pending-choice")]
    public ActionResult<V2GameStateDto> ResolvePendingChoice(string gameId, [FromBody] V2ResolvePendingChoiceRequest request)
    {
        var (session, playerId) = RequireSeat(gameId);
        var state = session.State;
        if (state.PendingChoice is not { } pending)
            throw new InvalidOperationException("There is no pending choice to resolve.");
        if (playerId != pending.ControllerId)
            throw new NotYourTurnException("That is the other player's decision to make.");

        var queue = session.PendingQueue ?? new AbilityQueue();
        session.PendingQueue = null;
        EffectInterpreter.AnswerPendingChoice(state, request.ChosenDieIds);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    private static CombatAssignment BuildAssignment(IReadOnlyList<V2BlockAssignment> assignments)
    {
        var assignment = new CombatAssignment();
        foreach (var a in assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);
        return assignment;
    }

    // --- Game recording (Recording/GameRecorder.cs, 2026-10-04). ASP.NET
    // calls these around every action of a controller that implements
    // IActionFilter. Every game-changing POST is recorded - rejected ones
    // too, with their error - and a GET for the computer's move marks that
    // seat as the bot. (Tests that call action methods directly skip this.)

    private string? _recordAction;
    private object? _recordRequest;
    private string? _recordStep;

    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (store.Recorder is null || !context.ActionArguments.TryGetValue("gameId", out var id) || id is not string gameId) return;
        var action = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?.ActionName;
        if (action is null or nameof(Join) or nameof(Lobby)) return;
        if (!HttpMethods.IsPost(Request.Method) && action != nameof(BotDecision)) return;
        _recordAction = action;
        _recordRequest = context.ActionArguments.Values.FirstOrDefault(v => v is not string);
        _recordStep = store.HasGame(gameId) ? store.GetSession(gameId).State.CurrentStepId : null;
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context)
    {
        if (_recordAction is null || _recordStep is null || store.Recorder is not { } recorder) return;
        var gameId = (string)context.RouteData.Values["gameId"]!;
        var session = _session ?? store.GetSession(gameId);
        var playerId = _seatPlayerId ?? session.PlayerIdFor(Request.Headers[SeatTokenHeader].ToString());
        if (playerId is null) return; // no valid seat - nothing happened
        if (_recordAction == nameof(BotDecision))
        {
            GameRecorder.MarkBot(session, playerId);
            return;
        }
        // A seat or turn refusal changed nothing and says nothing about play.
        if (context.Exception is SeatRequiredException or NotYourTurnException) return;
        recorder.Record(session, playerId, _recordAction, _recordRequest, _recordStep, context.Exception?.Message,
            auto: Request.Headers["X-Auto-Move"] == "1");
    }

    private enum V2Actor { Active, Inactive }

    private const string SeatTokenHeader = "X-Seat-Token";
    private string? _seatPlayerId;
    private V2GameSession? _session;

    private (V2GameSession Session, string PlayerId) RequireSeat(string gameId)
    {
        var session = store.GetSession(gameId);
        var token = Request.Headers[SeatTokenHeader].ToString();
        var playerId = session.PlayerIdFor(string.IsNullOrEmpty(token) ? null : token);
        if (playerId is null)
            throw new SeatRequiredException($"A valid {SeatTokenHeader} is required to act in this game.");
        // Rule 2.9 - nothing more happens once someone is out of Life.
        if (HttpMethods.IsPost(Request.Method) && session.State.IsGameOver)
            throw new InvalidOperationException("The game is over.");
        _seatPlayerId = playerId;
        _session = session;
        store.Accounts?.SeatUsed(session, playerId, HttpContext.Player());
        return (session, playerId);
    }

    private GameState RequireTurn(string gameId, V2Actor actor)
    {
        var (session, playerId) = RequireSeat(gameId);
        var state = session.State;
        var expected = actor == V2Actor.Active ? state.ActivePlayerId : state.OpponentOf(state.ActivePlayerId);
        if (playerId != expected)
        {
            throw new NotYourTurnException(actor == V2Actor.Active
                ? "It is not your turn."
                : "That is the other player's decision to make.");
        }
        if (state.PendingChoice is not null)
            throw new InvalidOperationException("Resolve the pending choice before taking another action.");
        return state;
    }

    private V2GameStateDto Result(string gameId, GameState state)
    {
        var session = store.GetSession(gameId);
        if (HttpMethods.IsPost(Request.Method))
        {
            session.MarkChanged();
            store.Accounts?.AfterMove(session);
        }
        Priority.Sync(state);
        return V2GameStateDto.From(gameId, state, _seatPlayerId, session.Version);
    }

    // Same "there's no real legal-target UI resolver yet" shape as v1's
    // own Drain - the difference is v2 doesn't need one: every real
    // player decision already routes through PendingChoice
    // (EffectInterpreter.ResolveTarget's own auto-vs-choice branch), so
    // draining just needs a roller/random to hand the interpreter, not a
    // caller-supplied target list. The queue itself is stashed on the
    // SESSION (not GameState - see V2GameSession.PendingQueue's own
    // remarks) whenever a PendingChoice interrupts it mid-drain.
    private void Drain(GameState state, AbilityQueue queue)
    {
        var roller = new DiceFight.V2.RandomDiceRoller(Rng);
        EffectInterpreter.DrainQueue(state, queue, roller, Rng);
        if (_session is not null) _session.PendingQueue = state.PendingChoice is not null ? queue : null;
    }
}
