using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Controllers;

// v3 "Instinct Clash" API - Phase 3 of the mellow-sparking-comet plan.
// Mirrors GamesController.cs's shape (seat tokens, RequireTurn/Result/
// Drain helpers, one endpoint per TurnEngine/CombatEngine action) against
// DiceFight.V2 + InstinctClashConfig instead of v1's DiceFight.Engine +
// SampleCards - a parallel controller, not a shared abstraction, matching
// V2_PLAN.md's own Phase 9 note ("keeps v1 untouched").
//
// No team-builder: creating a game picks two Champions and
// InstinctClashConfig.CharactersByEnergyType builds each team
// automatically. No Global abilities/Range/Tag Out/Continuous-die
// endpoints - none of InstinctClashConfig's 8 Characters use those
// mechanisms, so there is nothing for them to drive yet.
[ApiController]
[Route("api/v2/games")]
public sealed class V2GamesController(V2GameStore store) : ControllerBase
{
    [HttpPost]
    public ActionResult<V2CreatedGameDto> Create([FromBody] CreateV2GameRequest request)
    {
        var config = InstinctClashConfig.Config;
        var catalog = InstinctClashConfig.Catalog;

        var playerOne = BuildPlayer("teamA", request.PlayerOneChampionId);
        var playerTwo = BuildPlayer("teamB", request.PlayerTwoChampionId);

        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        var session = store.Create(state);

        return Ok(new V2CreatedGameDto(
            V2GameStateDto.From(session.Id, state, state.PlayerOne.Id),
            session.Seats.Select(seat => new SeatDto(seat.PlayerId, seat.Token)).ToList()));
    }

    private static Player BuildPlayer(string id, string championId)
    {
        var champion = InstinctClashConfig.Champions.FirstOrDefault(c => c.Id == championId)
            ?? throw new InvalidOperationException($"Unknown Champion id '{championId}'.");
        var player = new Player { Id = id, Name = champion.Name, ChampionId = champion.Id };
        player.TeamCardIds.AddRange(InstinctClashConfig.CharactersByEnergyType[champion.EnergySymbolId]);
        return player;
    }

    [HttpGet("cards")]
    public ActionResult<IReadOnlyList<V2CardDefDto>> Cards() =>
        Ok(InstinctClashConfig.Catalog.Values.Select(V2CardDefDto.From).ToList());

    [HttpGet("champions")]
    public ActionResult<IReadOnlyList<ChampionDto>> Champions() =>
        Ok(InstinctClashConfig.Champions.Select(ChampionDto.From).ToList());

    [HttpGet("{gameId}")]
    public ActionResult<V2GameStateDto> Get(string gameId)
    {
        var (session, _) = RequireSeat(gameId);
        return Ok(Result(gameId, session.State));
    }

    [HttpPost("{gameId}/clear-and-draw")]
    public ActionResult<V2GameStateDto> ClearAndDraw(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, queue, new Random());
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/roll")]
    public ActionResult<V2GameStateDto> Roll(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.Roll(state, queue, new DiceFight.V2.RandomDiceRoller(new Random()));
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/reroll")]
    public ActionResult<V2GameStateDto> Reroll(string gameId, [FromBody] V2RerollRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.RerollOwn(state, queue, new DiceFight.V2.RandomDiceRoller(new Random()), request.DieIds);
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
        var queue = new AbilityQueue();
        TurnEngine.Purchase(state, queue, request.DieId, request.EnergyDieIds);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/field")]
    public ActionResult<V2GameStateDto> Field(string gameId, [FromBody] V2FieldRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, request.DieId, request.EnergyDieIds);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/enter-attack-step")]
    public ActionResult<V2GameStateDto> EnterAttackStep(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.EnterAttackStep(state, queue);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/skip-attack-step")]
    public ActionResult<V2GameStateDto> SkipAttackStep(string gameId)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.SkipAttackStep(state, queue);
        Drain(state, queue);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/declare-attackers")]
    public ActionResult<V2GameStateDto> DeclareAttackers(string gameId, [FromBody] V2DeclareAttackersRequest request)
    {
        var state = RequireTurn(gameId, V2Actor.Active);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, request.AttackerDieIds);
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
        var state = RequireTurn(gameId, V2Actor.Active);
        var assignment = BuildAssignment(request.Assignments);

        // No CombatRuleKind.BlocksN in InstinctClashConfig's catalog, so
        // every attacker has at most one live blocker - the split is
        // always "all of it," never a real player decision. See
        // V2AssignCombatDamageRequest's own remarks.
        var splits = new Dictionary<string, IReadOnlyDictionary<string, int>>();
        foreach (var attackerId in request.Assignments.Select(a => a.AttackerDieId).Distinct())
        {
            var blockerIds = assignment.BlockersOf(attackerId);
            if (blockerIds.Count == 0) continue;
            if (blockerIds.Count > 1)
                throw new InvalidOperationException($"Attacker '{attackerId}' has more than one blocker - no card grants that yet.");

            var attacker = state.Dice.First(d => d.Id == attackerId);
            splits[attackerId] = new Dictionary<string, int> { [blockerIds[0]] = QueryEngine.GetAttack(state, attacker) };
        }

        var queue = new AbilityQueue();
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);
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
        _seatPlayerId = playerId;
        _session = session;
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
        if (HttpMethods.IsPost(Request.Method)) session.MarkChanged();
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
        var roller = new DiceFight.V2.RandomDiceRoller(new Random());
        EffectInterpreter.DrainQueue(state, queue, roller, new Random());
        if (_session is not null) _session.PendingQueue = state.PendingChoice is not null ? queue : null;
    }
}
