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
        var state = store.Get(gameId);
        TurnEngine.AdvanceStep(state);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/clear-and-draw")]
    public ActionResult<GameStateDto> ClearAndDraw(string gameId)
    {
        var state = store.Get(gameId);
        TurnEngine.ClearAndDraw(state, new Random());
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/roll")]
    public ActionResult<GameStateDto> Roll(string gameId)
    {
        var state = store.Get(gameId);
        TurnEngine.Roll(state, new PlaceholderDiceRoller(new Random()));
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/finish-roll")]
    public ActionResult<GameStateDto> FinishRoll(string gameId, [FromBody] FinishRollRequest request)
    {
        var state = store.Get(gameId);
        TurnEngine.FinishRoll(state, new PlaceholderDiceRoller(new Random()), request.RerollDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/purchase")]
    public ActionResult<GameStateDto> Purchase(string gameId, [FromBody] PurchaseRequest request)
    {
        var state = store.Get(gameId);
        TurnEngine.Purchase(state, request.DieId, request.EnergyDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/field")]
    public ActionResult<GameStateDto> Field(string gameId, [FromBody] FieldRequest request)
    {
        var state = store.Get(gameId);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, request.DieId, request.EnergyDieIds);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/use-action-die")]
    public ActionResult<GameStateDto> UseActionDie(string gameId, [FromBody] UseActionDieRequest request)
    {
        var state = store.Get(gameId);
        var queue = new AbilityQueue();
        TurnEngine.UseActionDie(state, queue, request.DieId);
        Drain(state, queue, request.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/use-global-ability")]
    public ActionResult<GameStateDto> UseGlobalAbility(string gameId, [FromBody] UseGlobalAbilityRequest request)
    {
        var state = store.Get(gameId);
        var queue = new AbilityQueue();
        TurnEngine.UseGlobalAbility(state, queue, request.CardId, request.PlayerId, request.EnergyDieIds);
        Drain(state, queue, request.TargetDieIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/enter-attack-step")]
    public ActionResult<GameStateDto> EnterAttackStep(string gameId)
    {
        var state = store.Get(gameId);
        TurnEngine.EnterAttackStep(state);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/skip-attack-step")]
    public ActionResult<GameStateDto> SkipAttackStep(string gameId)
    {
        var state = store.Get(gameId);
        TurnEngine.SkipAttackStep(state);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/declare-attackers")]
    public ActionResult<GameStateDto> DeclareAttackers(string gameId, [FromBody] DeclareAttackersRequest request)
    {
        var state = store.Get(gameId);
        var queue = new AbilityQueue();
        CombatEngine.DeclareAttackers(state, queue, request.AttackerDieIds);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/declare-blockers")]
    public ActionResult<GameStateDto> DeclareBlockers(string gameId, [FromBody] DeclareBlockersRequest request)
    {
        var state = store.Get(gameId);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);
        var blockerIds = request.Assignments.Select(a => a.BlockerDieId).Distinct().ToList();

        CombatEngine.DeclareBlockers(state, assignment, blockerIds);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/assign-combat-damage")]
    public ActionResult<GameStateDto> AssignCombatDamage(string gameId, [FromBody] AssignCombatDamageRequest request)
    {
        var state = store.Get(gameId);
        var assignment = new CombatAssignment();
        foreach (var a in request.Assignments) assignment.AssignBlocker(a.AttackerDieId, a.BlockerDieId);

        var splits = request.DamageSplits
            .GroupBy(s => s.AttackerDieId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, int>)g.ToDictionary(s => s.BlockerDieId, s => s.Amount));

        var queue = new AbilityQueue();
        CombatEngine.AssignCombatDamage(state, queue, assignment, splits);
        Drain(state, queue, null);
        return Ok(GameStateDto.From(gameId, state));
    }

    [HttpPost("{gameId}/clean-up")]
    public ActionResult<GameStateDto> CleanUp(string gameId)
    {
        var state = store.Get(gameId);
        TurnEngine.CleanUp(state);
        return Ok(GameStateDto.From(gameId, state));
    }

    // Drains abilities triggered by the preceding action. There's no real
    // "legal target" query system yet (see RULES_ENGINE_DESIGN.md) - every
    // TargetSpec encountered resolves to whatever die ids the caller
    // supplied, which is enough for the currently scripted abilities (each
    // needs at most one target list). Casket of Ancient Winters needs
    // three distinct target groups and isn't drivable through this simple
    // a resolver yet.
    private static void Drain(GameState state, AbilityQueue queue, IReadOnlyList<string>? targetDieIds)
    {
        var targets = targetDieIds ?? [];
        queue.Drain(ability => EffectInterpreter.Execute(
            ability.Effect, new EffectContext(state, ability.ControllerId, ability.SourceDieId, _ => targets)));
    }
}
