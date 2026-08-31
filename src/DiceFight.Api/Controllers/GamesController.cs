using DiceFight.Engine;
using DiceFight.Engine.Combat;
using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using DiceFight.Engine.TeamBuilding;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Controllers;

[ApiController]
[Route("api/games")]
public sealed class GamesController(GameStore store) : ControllerBase
{
    // With no body (or an empty TeamCardIds), falls back to the two
    // curated sample teams as before - keeps the web client's original
    // "New Game (Team A vs Team B)" button working unchanged. A non-empty
    // TeamCardIds (from /teambuilder's "Start Game", see TeamBuilderPage.
    // tsx) becomes Team A instead; unknown ids are silently dropped by
    // TeamSetup itself, same trust-boundary shape as everywhere else in
    // this controller. Team B is always a fresh RandomTeamBuilder roster
    // drawn from IsImplemented cards in that case - there's no opponent
    // selection UI, and an unscripted opponent card would just sit there
    // doing nothing.
    [HttpPost]
    public ActionResult<CreatedGameDto> Create([FromBody] CreateGameRequest? request = null)
    {
        var catalog = SampleCards.BuildCatalog();
        var teamA = new Player { Id = "teamA", Name = "Team A" };
        var teamB = new Player { Id = "teamB", Name = "Team B" };

        if (request?.TeamCardIds is { Count: > 0 } customTeam)
        {
            teamA.TeamCardIds.AddRange(customTeam);
            teamB.TeamCardIds.AddRange(RandomTeamBuilder.Build(catalog, new Random()));
        }
        else
        {
            teamA.TeamCardIds.AddRange(SampleCards.TeamACharacterIds);
            teamA.TeamCardIds.AddRange(SampleCards.TeamABasicActionIds);
            teamB.TeamCardIds.AddRange(SampleCards.TeamBCharacterIds);
            teamB.TeamCardIds.AddRange(SampleCards.TeamBBasicActionIds);
        }

        var state = GameState.NewGame(catalog, teamA, teamB);
        var session = store.Create(state);

        // Both seats come back, because the creator may be playing alone
        // (holding both) or about to hand one out as an invite. This is
        // the ONLY response that carries a seat token - any other one
        // would be showing the caller their opponent's secret.
        return Ok(new CreatedGameDto(
            GameStateDto.From(session.Id, state, state.PlayerOne.Id),
            session.Seats.Select(seat => new SeatDto(seat.PlayerId, seat.Token)).ToList()));
    }

    [HttpGet("{gameId}")]
    public ActionResult<GameStateDto> Get(string gameId)
    {
        var (session, _) = RequireSeat(gameId);
        return Ok(Result(gameId, session.State));
    }

    [HttpPost("{gameId}/advance-step")]
    public ActionResult<GameStateDto> AdvanceStep(string gameId)
    {
        var state = RequireTurn(gameId, Actor.Active);
        TurnEngine.AdvanceStep(state);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/clear-and-draw")]
    public ActionResult<GameStateDto> ClearAndDraw(string gameId)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(), queue);
        Drain(state, queue, null);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/roll")]
    public ActionResult<GameStateDto> Roll(string gameId)
    {
        var state = RequireTurn(gameId, Actor.Active);
        TurnEngine.Roll(state, new RandomDiceRoller(new Random()));
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/reroll")]
    public ActionResult<GameStateDto> Reroll(string gameId, [FromBody] RerollRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new RandomDiceRoller(new Random()), request.RerollDieIds);
        Drain(state, queue, null);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/purchase")]
    public ActionResult<GameStateDto> Purchase(string gameId, [FromBody] PurchaseRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        TurnEngine.Purchase(state, request.DieId, request.EnergyDieIds);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/field")]
    public ActionResult<GameStateDto> Field(string gameId, [FromBody] FieldRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, request.DieId, request.EnergyDieIds);
        Drain(state, queue, request.TargetDieIds);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/use-action-die")]
    public ActionResult<GameStateDto> UseActionDie(string gameId, [FromBody] UseActionDieRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, request.DieId, roller: new RandomDiceRoller(new Random()));
        Drain(state, queue, request.TargetDieIds);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/resolve-continuous-die")]
    public ActionResult<GameStateDto> ResolveContinuousDie(string gameId, [FromBody] ResolveContinuousDieRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.ResolveContinuousDie(state, queue, request.DieId);
        Drain(state, queue, request.TargetDieIds);
        return Ok(Result(gameId, state));
    }

    // Dampening Collar (DPS002) - no ability queue involved (see
    // TurnEngine.OpponentResolveContinuousDie's own remarks on why this
    // isn't "using" the die), so no Drain call here, unlike every other
    // action-taking endpoint in this file.
    [HttpPost("{gameId}/opponent-resolve-continuous-die")]
    public ActionResult<GameStateDto> OpponentResolveContinuousDie(string gameId, [FromBody] OpponentResolveContinuousDieRequest request)
    {
        var state = RequireTurn(gameId, Actor.Inactive);
        TurnEngine.OpponentResolveContinuousDie(state, request.DieId, request.AffiliateDieIdToReturn);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/use-global-ability")]
    public ActionResult<GameStateDto> UseGlobalAbility(string gameId, [FromBody] UseGlobalAbilityRequest request)
    {
        var state = RequireTurn(gameId, Actor.Either);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, request.CardId, request.PlayerId, request.EnergyDieIds);
        Drain(state, queue, request.TargetDieIds);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/enter-attack-step")]
    public ActionResult<GameStateDto> EnterAttackStep(string gameId, [FromBody] EnterAttackStepRequest? request = null)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        TurnEngine.EnterAttackStep(state, queue);
        Drain(state, queue, request?.TargetDieIds);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/skip-attack-step")]
    public ActionResult<GameStateDto> SkipAttackStep(string gameId)
    {
        var state = RequireTurn(gameId, Actor.Active);
        TurnEngine.SkipAttackStep(state);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/declare-attackers")]
    public ActionResult<GameStateDto> DeclareAttackers(string gameId, [FromBody] DeclareAttackersRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, request.AttackerDieIds);
        Drain(state, queue, request.TargetDieIds);
        return Ok(Result(gameId, state));
    }

    // Only reachable when DeclareAttackers found at least one Range
    // attacker (see CombatEngine.DeclareAttackers's own remarks) - most
    // combats skip straight past AttackSubStep.RangeWindow to
    // DeclareBlockers and never need this endpoint called at all.
    [HttpPost("{gameId}/resolve-range")]
    public ActionResult<GameStateDto> ResolveRange(string gameId, [FromBody] ResolveRangeRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        CombatEngine.ResolveRange(
            state, queue,
            request.ActivePlayerAssignments.Select(a => (a.RangeDieId, a.TargetDieId)).ToList(),
            request.InactivePlayerAssignments.Select(a => (a.RangeDieId, a.TargetDieId)).ToList());
        Drain(state, queue, null);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/declare-blockers")]
    public ActionResult<GameStateDto> DeclareBlockers(string gameId, [FromBody] DeclareBlockersRequest request)
    {
        var state = RequireTurn(gameId, Actor.Inactive);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);
        var blockerIds = request.Assignments.Select(a => a.BlockerDieId).Distinct().ToList();

        CombatEngine.DeclareBlockers(state, assignment, blockerIds);
        return Ok(Result(gameId, state));
    }

    // Only reachable when DeclareBlockers found a real Infiltrate choice
    // to offer (see its own remarks) - most combats skip straight past
    // AttackSubStep.InfiltrateWindow to ActionAndGlobalWindow and never
    // need this endpoint called at all.
    [HttpPost("{gameId}/resolve-infiltrate")]
    public ActionResult<GameStateDto> ResolveInfiltrate(string gameId, [FromBody] ResolveInfiltrateRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);

        var queue = new AbilityQueue();
        CombatEngine.ResolveInfiltrate(state, queue, assignment, request.InfiltratingDieIds);
        Drain(state, queue, null);
        return Ok(Result(gameId, state));
    }

    // Only reachable when DeclareBlockers/ResolveInfiltrate found a real
    // Tag Out choice to offer (see CombatEngine.NextSubStepAfterBlockers) -
    // most combats skip straight past AttackSubStep.TagOutWindow to
    // ActionAndGlobalWindow and never need this endpoint called at all.
    [HttpPost("{gameId}/resolve-tag-out")]
    public ActionResult<GameStateDto> ResolveTagOut(string gameId, [FromBody] ResolveTagOutRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var uses = request.Uses.Select(u => (u.TagOutDieId, u.TargetDieId)).ToList();

        var queue = new AbilityQueue();
        CombatEngine.ResolveTagOut(state, queue, uses);
        Drain(state, queue, null);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/assign-combat-damage")]
    public ActionResult<GameStateDto> AssignCombatDamage(string gameId, [FromBody] AssignCombatDamageRequest request)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);

        var splits = request.DamageSplits
            .GroupBy(s => s.AttackerDieId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, int>)g.ToDictionary(s => s.BlockerDieId, s => s.Amount));

        var queue = new AbilityQueue();
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits, new RandomDiceRoller(new Random()));
        Drain(state, queue, null);
        return Ok(Result(gameId, state));
    }

    [HttpPost("{gameId}/clean-up")]
    public ActionResult<GameStateDto> CleanUp(string gameId)
    {
        var state = RequireTurn(gameId, Actor.Active);
        var queue = new AbilityQueue();
        // roller lets a Deadly-KO'd die with Regenerate reroll instead
        // (previously always null here, so Regenerate silently never
        // applied to a real Deadly KO through the API); queue lets that
        // same KO's WhenKOd/Retaliation/WhenAnotherDieKOd reactions fire
        // (see TurnEngine.ResolveKOReactions) instead of the documented
        // gap CleanUp used to have.
        TurnEngine.CleanUp(state, new RandomDiceRoller(new Random()), queue);
        Drain(state, queue, null);
        return Ok(Result(gameId, state));
    }

    // Keyword Corrupt/RedrawFromBag (Cosmic Cube "Infinite Possibilities",
    // Rip Hunter) - the only legal next action whenever GameState.
    // PendingChoice is set (every other action endpoint refuses to run
    // until this answers it - see RequireNoPendingChoice). Validates the
    // caller's answer against the real candidate set before ever calling
    // PendingChoice.Resolve, same trust-boundary shape every other
    // endpoint already uses for its own TurnEngine/CombatEngine calls.
    [HttpPost("{gameId}/resolve-pending-choice")]
    public ActionResult<GameStateDto> ResolvePendingChoice(string gameId, [FromBody] ResolvePendingChoiceRequest request)
    {
        var (session, playerId) = RequireSeat(gameId);
        var state = session.State;
        var pending = state.PendingChoice
            ?? throw new InvalidOperationException("There is no pending choice to resolve.");
        // The choice belongs to whoever the engine says - which is not
        // always the active player: plenty of cards make the OPPONENT
        // choose one of their own dice.
        if (pending.ControllerId != playerId)
            throw new NotYourTurnException("That choice belongs to the other player.");

        var invalid = request.ChosenDieIds.Where(id => !pending.CandidateDieIds.Contains(id)).ToList();
        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Chosen die(s) [{string.Join(", ", invalid)}] are not valid candidates for '{pending.Description}'.");
        }
        if (!pending.AllowMultiple && request.ChosenDieIds.Count != 1)
            throw new InvalidOperationException($"'{pending.Description}' needs exactly one choice.");

        state.PendingChoice = null;
        pending.Resolve(request.ChosenDieIds);

        var queue = state.PendingQueue;
        state.PendingQueue = null;
        if (queue is not null) Drain(state, queue, null);

        return Ok(Result(gameId, state));
    }

    // Every action-taking endpoint above (everything except Create/Get/
    // ResolvePendingChoice itself) goes through this instead of a raw
    // store.Get - rule 3.2's own resolution-before-anything-else timing
    // means no other game action is legal while a mid-resolution choice
    // (keyword Corrupt/RedrawFromBag) is still waiting to be answered.
    // Who is allowed to take an action. The ENGINE already enforces the
    // rules of the game (that it is the Main Step, that this die is
    // yours); this is the other half - that the caller is the player
    // whose action it is at all. Before seats, the acting player was a
    // request parameter, so anyone holding a game id could act as either
    // side.
    private enum Actor
    {
        /// <summary>Whoever's turn it is - almost everything.</summary>
        Active,
        /// <summary>The defender: declaring blockers, and their own windows.</summary>
        Inactive,
        /// <summary>Rule 2.6.5.2 - Global abilities are open to both.</summary>
        Either,
    }

    // The seat secret rides in a header rather than the body so it is
    // uniform across GET and POST and never ends up in a request log's
    // query string.
    private const string SeatTokenHeader = "X-Seat-Token";

    private string? _seatPlayerId;

    private (GameSession Session, string PlayerId) RequireSeat(string gameId)
    {
        var session = store.GetSession(gameId);
        var token = Request.Headers[SeatTokenHeader].ToString();
        var playerId = session.PlayerIdFor(string.IsNullOrEmpty(token) ? null : token);
        if (playerId is null)
            throw new SeatRequiredException($"A valid {SeatTokenHeader} is required to act in this game.");
        _seatPlayerId = playerId;
        return (session, playerId);
    }

    private GameState RequireTurn(string gameId, Actor actor)
    {
        var (session, playerId) = RequireSeat(gameId);
        var state = session.State;
        var expected = actor switch
        {
            Actor.Active => state.ActivePlayerId,
            Actor.Inactive => state.OpponentOf(state.ActivePlayerId),
            _ => playerId,
        };
        if (playerId != expected)
        {
            throw new NotYourTurnException(actor == Actor.Active
                ? "It is not your turn."
                : "That is the other player's decision to make.");
        }
        if (state.PendingChoice is not null)
            throw new InvalidOperationException("Resolve the pending choice before taking another action.");
        return state;
    }

    // Every response says which side the caller holds, so the client can
    // render the board from that seat without being told separately.
    private GameStateDto Result(string gameId, GameState state) =>
        GameStateDto.From(gameId, state, _seatPlayerId);

    private GameState RequireNoPendingChoice(string gameId)
    {
        var state = RequireTurn(gameId, Actor.Active);
        if (state.PendingChoice is not null)
            throw new InvalidOperationException("Resolve the pending choice before taking another action.");
        return state;
    }

    // Drains abilities triggered by the preceding action. There's no real
    // "legal target" query system yet (see RULES_ENGINE_DESIGN.md) - every
    // TargetSpec encountered resolves to whatever die ids the caller
    // supplied, which is enough for the currently scripted abilities (each
    // needs at most one target list). Casket of Ancient Winters needs
    // three distinct target groups and isn't drivable through this simple
    // a resolver yet.
    //
    // Stops early instead of draining to completion if an ability sets
    // GameState.PendingChoice (Corrupt/RedrawFromBag - see its own
    // remarks) - whatever's still left in `queue` at that point is
    // stashed on GameState.PendingQueue so ResolvePendingChoice can pick
    // up exactly where this left off, rather than losing it.
    private static void Drain(GameState state, AbilityQueue queue, IReadOnlyList<string>? targetDieIds)
    {
        var targets = targetDieIds ?? [];
        var roller = new RandomDiceRoller(new Random());
        queue.Drain(
            ability => EffectInterpreter.Execute(
                ability.Effect,
                new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => targets, Roller: roller, Queue: queue, Trigger: ability.Trigger)),
            shouldStop: () => state.PendingChoice is not null);

        if (state.PendingChoice is not null)
            state.PendingQueue = queue;
    }
}
