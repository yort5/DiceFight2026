using DiceFight.Engine;
using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;

namespace DiceFight.Api;

public sealed record CharacterFaceDto(int FieldingCost, int Attack, int Defense, int? BurstStars);

// Only meaningful when AbilityTriggers includes "Global" - lets the web
// client show a Global ability's price before the player commits energy
// dice to it, without needing the full AbilityDef/effect tree.
public sealed record GlobalAbilityCostDto(int Amount, string? RequiredType);

public sealed record CardDefDto(
    string Id, string Name, string? Subtitle, string Type, int PurchaseCost,
    IReadOnlyList<string> EnergyTypes, IReadOnlyList<string> Affiliations, string? Alignment,
    int DieLimit, IReadOnlyList<CharacterFaceDto> Levels, string RawText,
    IReadOnlyList<string> Keywords, IReadOnlyList<string> AbilityTriggers,
    GlobalAbilityCostDto? GlobalAbilityCost, bool GlobalAbilityNeedsTarget, bool IsImplemented,
    bool WhenFieldedNeedsTarget, string? Set)
{
    public static CardDefDto From(CardDef card)
    {
        var globalAbility = card.Abilities.FirstOrDefault(a => a.Trigger == TriggerType.Global);
        var whenFieldedAbility = card.Abilities.FirstOrDefault(a => a.Trigger == TriggerType.WhenFielded);
        return new(
            card.Id, card.Name, card.Subtitle, card.Type.ToString(), card.PurchaseCost,
            card.EnergyTypes.Select(e => e.ToString()).ToList(),
            card.Affiliations, card.Alignment?.ToString(), card.DieLimit,
            card.Levels.Select(l => new CharacterFaceDto(l.FieldingCost, l.Attack, l.Defense, l.BurstStars)).ToList(),
            card.RawText, card.Keywords.Select(k => k.Name).ToList(),
            card.Abilities.Select(a => a.Trigger.ToString()).Distinct().ToList(),
            globalAbility?.EnergyCost is { } cost
                ? new GlobalAbilityCostDto(cost.Amount, cost.RequiredType?.ToString())
                : null,
            globalAbility is not null && EffectInterpreter.NeedsTarget(globalAbility.Effect),
            card.IsImplemented,
            whenFieldedAbility is not null && EffectInterpreter.NeedsTarget(whenFieldedAbility.Effect),
            card.Set);
    }
}

public sealed record DieDto(
    string Id, string? CardId, string OwnerId, string ControllerId, string Zone, string Status,
    int Level, int Damage, string EnergyKind, string? ProvidedEnergyType, int EnergyAmount, bool IsVirtualEnergy)
{
    public static DieDto From(DieInstance die) => new(
        die.Id, die.VirtualCardId ?? die.CardId, die.OwnerId, die.ControllerId,
        die.Zone.ToString(), die.Status.ToString(), die.Level, die.Damage,
        die.EnergyKind.ToString(), die.ProvidedEnergyType?.ToString(), die.EnergyAmount, die.IsVirtualEnergy);
}

public sealed record PlayerDto(string Id, string Name, int Life)
{
    public static PlayerDto From(Player player) => new(player.Id, player.Name, player.Life);
}

// Exposed whenever GameState.PendingChoice is set (keyword Corrupt,
// RedrawFromBag - see PendingChoice's own remarks) - every other action
// endpoint refuses to run while this is non-null, so the client's only
// legal move is POST .../resolve-pending-choice.
public sealed record PendingChoiceDto(
    string ControllerId, string Description, IReadOnlyList<string> CandidateDieIds, bool AllowMultiple)
{
    public static PendingChoiceDto From(PendingChoice pending) =>
        new(pending.ControllerId, pending.Description, pending.CandidateDieIds, pending.AllowMultiple);
}

public sealed record GameStateDto(
    string GameId, string ActivePlayerId, string CurrentStep, string AttackSubStep,
    bool IsFirstTurn, bool EpicBasicActionUsedThisTurn,
    PlayerDto PlayerOne, PlayerDto PlayerTwo, IReadOnlyList<DieDto> Dice, PendingChoiceDto? PendingChoice)
{
    public static GameStateDto From(string gameId, GameState state) => new(
        gameId, state.ActivePlayerId, state.CurrentStep.ToString(), state.AttackSubStep.ToString(),
        state.IsFirstTurn, state.EpicBasicActionUsedThisTurn,
        PlayerDto.From(state.PlayerOne), PlayerDto.From(state.PlayerTwo),
        state.Dice.Select(DieDto.From).ToList(),
        state.PendingChoice is { } pending ? PendingChoiceDto.From(pending) : null);
}

// ---- Request bodies ----

public sealed record PurchaseRequest(string DieId, IReadOnlyList<string> EnergyDieIds);
// TargetDieIds feeds a WhenFielded ability that needs one (Intimidate,
// Dazzler, God Emperor Doom, Polaris) - see GamesController.Field's Drain
// call. Null/empty is fine for every other Character die.
public sealed record FieldRequest(
    string DieId, IReadOnlyList<string> EnergyDieIds, IReadOnlyList<string>? TargetDieIds = null);
public sealed record UseActionDieRequest(string DieId, IReadOnlyList<string>? TargetDieIds);
// Keyword Continuous - resolves a die already sitting in the Field Zone
// (having been "used" earlier via UseActionDieRequest) rather than one
// still in the Reserve Pool. See TurnEngine.ResolveContinuousDie.
public sealed record ResolveContinuousDieRequest(string DieId, IReadOnlyList<string>? TargetDieIds);
public sealed record UseGlobalAbilityRequest(
    string CardId, string PlayerId, IReadOnlyList<string> EnergyDieIds, IReadOnlyList<string>? TargetDieIds);
// TargetDieIds feeds TriggerType.StartOfOpponentsAttackStep reactions
// (both Emma Frost printings) - optional/nullable body since most games
// have no such card in play and the client shouldn't have to send one.
public sealed record EnterAttackStepRequest(IReadOnlyList<string>? TargetDieIds = null);
public sealed record RerollRequest(IReadOnlyList<string> RerollDieIds);
// TargetDieIds feeds Call Out's WhenAttacks-triggered SetCallOutTarget
// effect (see GamesController.DeclareAttackers's Drain call) - null/empty
// is fine for every combat that doesn't include a Call Out attacker.
public sealed record DeclareAttackersRequest(
    IReadOnlyList<string> AttackerDieIds, IReadOnlyList<string>? TargetDieIds = null);
public sealed record BlockAssignment(string AttackerDieId, string BlockerDieId);
public sealed record DeclareBlockersRequest(IReadOnlyList<BlockAssignment> Assignments);
public sealed record DamageSplit(string AttackerDieId, string BlockerDieId, int Amount);
public sealed record AssignCombatDamageRequest(
    IReadOnlyList<BlockAssignment> Assignments, IReadOnlyList<DamageSplit> DamageSplits);
// Assignments has to be resent here too - CombatAssignment isn't persisted
// server-side between calls, same as AssignCombatDamageRequest's own copy.
public sealed record ResolveInfiltrateRequest(
    IReadOnlyList<BlockAssignment> Assignments, IReadOnlyList<string> InfiltratingDieIds);
public sealed record TagOutUse(string TagOutDieId, string TargetDieId);
// No Assignments here - unlike ResolveInfiltrate, CombatEngine.ResolveTagOut
// doesn't need the blocker assignment at all (Tag Out's own eligibility
// only cares about the Field Zone, not who's blocking whom).
public sealed record ResolveTagOutRequest(IReadOnlyList<TagOutUse> Uses);
public sealed record RangeAssignment(string RangeDieId, string TargetDieId);
// Both sides' assignments in one request, same shape CombatEngine.
// ResolveRange itself takes - Range resolves before blockers even exist,
// so there's no BlockAssignment payload to resend here either.
public sealed record ResolveRangeRequest(
    IReadOnlyList<RangeAssignment> ActivePlayerAssignments, IReadOnlyList<RangeAssignment> InactivePlayerAssignments);
public sealed record ResolvePendingChoiceRequest(IReadOnlyList<string> ChosenDieIds);
