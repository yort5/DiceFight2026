using DiceFight.Engine;
using DiceFight.Engine.Combat;
using DiceFight.Engine.Data;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Controllers;

[ApiController]
[Route("api/games")]
public sealed class GamesController(GameStore store) : ControllerBase
{
    // Always the two curated sample teams for now - real team building/
    // selection is a separate, later feature.
    [HttpPost]
    public ActionResult<GameStateDto> Create()
    {
        var catalog = SampleCards.BuildCatalog();
        var teamA = new Player { Id = "teamA", Name = "Team A" };
        teamA.TeamCardIds.AddRange(SampleCards.TeamACharacterIds);
        teamA.TeamCardIds.AddRange(SampleCards.TeamABasicActionIds);
        var teamB = new Player { Id = "teamB", Name = "Team B" };
        teamB.TeamCardIds.AddRange(SampleCards.TeamBCharacterIds);
        teamB.TeamCardIds.AddRange(SampleCards.TeamBBasicActionIds);

        var state = GameState.NewGame(catalog, teamA, teamB);
        var gameId = store.Create(state);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpGet("{gameId}")]
    public ActionResult<GameStateDto> Get(string gameId) => Ok(GameStateDto.From(gameId, store.Get(gameId)));

    [HttpPost("{gameId}/advance-step")]
    public ActionResult<GameStateDto> AdvanceStep(string gameId)
    {
        var state = RequireNoPendingChoice(gameId);
        TurnEngine.AdvanceStep(state);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/clear-and-draw")]
    public ActionResult<GameStateDto> ClearAndDraw(string gameId)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        TurnEngine.ClearAndDraw(state, new Random(), queue);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/roll")]
    public ActionResult<GameStateDto> Roll(string gameId)
    {
        var state = RequireNoPendingChoice(gameId);
        TurnEngine.Roll(state, new PlaceholderDiceRoller(new Random()));
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/reroll")]
    public ActionResult<GameStateDto> Reroll(string gameId, [FromBody] RerollRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        TurnEngine.Reroll(state, queue, new PlaceholderDiceRoller(new Random()), request.RerollDieIds);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/purchase")]
    public ActionResult<GameStateDto> Purchase(string gameId, [FromBody] PurchaseRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        TurnEngine.Purchase(state, request.DieId, request.EnergyDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/field")]
    public ActionResult<GameStateDto> Field(string gameId, [FromBody] FieldRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, request.DieId, request.EnergyDieIds);
        Drain(state, queue, request.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/use-action-die")]
    public ActionResult<GameStateDto> UseActionDie(string gameId, [FromBody] UseActionDieRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, request.DieId, roller: new PlaceholderDiceRoller(new Random()));
        Drain(state, queue, request.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/resolve-continuous-die")]
    public ActionResult<GameStateDto> ResolveContinuousDie(string gameId, [FromBody] ResolveContinuousDieRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        TurnEngine.ResolveContinuousDie(state, queue, request.DieId);
        Drain(state, queue, request.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    // Dampening Collar (DPS002) - no ability queue involved (see
    // TurnEngine.OpponentResolveContinuousDie's own remarks on why this
    // isn't "using" the die), so no Drain call here, unlike every other
    // action-taking endpoint in this file.
    [HttpPost("{gameId}/opponent-resolve-continuous-die")]
    public ActionResult<GameStateDto> OpponentResolveContinuousDie(string gameId, [FromBody] OpponentResolveContinuousDieRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        TurnEngine.OpponentResolveContinuousDie(state, request.DieId, request.AffiliateDieIdToReturn);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/use-global-ability")]
    public ActionResult<GameStateDto> UseGlobalAbility(string gameId, [FromBody] UseGlobalAbilityRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, request.CardId, request.PlayerId, request.EnergyDieIds);
        Drain(state, queue, request.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/enter-attack-step")]
    public ActionResult<GameStateDto> EnterAttackStep(string gameId, [FromBody] EnterAttackStepRequest? request = null)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        TurnEngine.EnterAttackStep(state, queue);
        Drain(state, queue, request?.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/skip-attack-step")]
    public ActionResult<GameStateDto> SkipAttackStep(string gameId)
    {
        var state = RequireNoPendingChoice(gameId);
        TurnEngine.SkipAttackStep(state);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/declare-attackers")]
    public ActionResult<GameStateDto> DeclareAttackers(string gameId, [FromBody] DeclareAttackersRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, request.AttackerDieIds);
        Drain(state, queue, request.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    // Only reachable when DeclareAttackers found at least one Range
    // attacker (see CombatEngine.DeclareAttackers's own remarks) - most
    // combats skip straight past AttackSubStep.RangeWindow to
    // DeclareBlockers and never need this endpoint called at all.
    [HttpPost("{gameId}/resolve-range")]
    public ActionResult<GameStateDto> ResolveRange(string gameId, [FromBody] ResolveRangeRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        CombatEngine.ResolveRange(
            state, queue,
            request.ActivePlayerAssignments.Select(a => (a.RangeDieId, a.TargetDieId)).ToList(),
            request.InactivePlayerAssignments.Select(a => (a.RangeDieId, a.TargetDieId)).ToList());
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/declare-blockers")]
    public ActionResult<GameStateDto> DeclareBlockers(string gameId, [FromBody] DeclareBlockersRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);
        var blockerIds = request.Assignments.Select(a => a.BlockerDieId).Distinct().ToList();

        CombatEngine.DeclareBlockers(state, assignment, blockerIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    // Only reachable when DeclareBlockers found a real Infiltrate choice
    // to offer (see its own remarks) - most combats skip straight past
    // AttackSubStep.InfiltrateWindow to ActionAndGlobalWindow and never
    // need this endpoint called at all.
    [HttpPost("{gameId}/resolve-infiltrate")]
    public ActionResult<GameStateDto> ResolveInfiltrate(string gameId, [FromBody] ResolveInfiltrateRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);

        var queue = new AbilityQueue();
        CombatEngine.ResolveInfiltrate(state, queue, assignment, request.InfiltratingDieIds);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    // Only reachable when DeclareBlockers/ResolveInfiltrate found a real
    // Tag Out choice to offer (see CombatEngine.NextSubStepAfterBlockers) -
    // most combats skip straight past AttackSubStep.TagOutWindow to
    // ActionAndGlobalWindow and never need this endpoint called at all.
    [HttpPost("{gameId}/resolve-tag-out")]
    public ActionResult<GameStateDto> ResolveTagOut(string gameId, [FromBody] ResolveTagOutRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var uses = request.Uses.Select(u => (u.TagOutDieId, u.TargetDieId)).ToList();

        var queue = new AbilityQueue();
        CombatEngine.ResolveTagOut(state, queue, uses);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/assign-combat-damage")]
    public ActionResult<GameStateDto> AssignCombatDamage(string gameId, [FromBody] AssignCombatDamageRequest request)
    {
        var state = RequireNoPendingChoice(gameId);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);

        var splits = request.DamageSplits
            .GroupBy(s => s.AttackerDieId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, int>)g.ToDictionary(s => s.BlockerDieId, s => s.Amount));

        var queue = new AbilityQueue();
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits, new PlaceholderDiceRoller(new Random()));
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/clean-up")]
    public ActionResult<GameStateDto> CleanUp(string gameId)
    {
        var state = RequireNoPendingChoice(gameId);
        var queue = new AbilityQueue();
        // roller lets a Deadly-KO'd die with Regenerate reroll instead
        // (previously always null here, so Regenerate silently never
        // applied to a real Deadly KO through the API); queue lets that
        // same KO's WhenKOd/Retaliation/WhenAnotherDieKOd reactions fire
        // (see TurnEngine.ResolveKOReactions) instead of the documented
        // gap CleanUp used to have.
        TurnEngine.CleanUp(state, new PlaceholderDiceRoller(new Random()), queue);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
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
        var state = store.Get(gameId);
        var pending = state.PendingChoice
            ?? throw new InvalidOperationException("There is no pending choice to resolve.");

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

        return Ok(GameStateDto.From(gameId, state));
    }

    // Every action-taking endpoint above (everything except Create/Get/
    // ResolvePendingChoice itself) goes through this instead of a raw
    // store.Get - rule 3.2's own resolution-before-anything-else timing
    // means no other game action is legal while a mid-resolution choice
    // (keyword Corrupt/RedrawFromBag) is still waiting to be answered.
    private GameState RequireNoPendingChoice(string gameId)
    {
        var state = store.Get(gameId);
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
        var roller = new PlaceholderDiceRoller(new Random());
        queue.Drain(
            ability => EffectInterpreter.Execute(
                ability.Effect,
                new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => targets, Roller: roller, Queue: queue, Trigger: ability.Trigger)),
            shouldStop: () => state.PendingChoice is not null);

        if (state.PendingChoice is not null)
            state.PendingQueue = queue;
    }
}
