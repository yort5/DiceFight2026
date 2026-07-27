using DiceFight.Engine;
using DiceFight.Engine.Model;

namespace DiceFight.Api;

public sealed record CharacterFaceDto(int FieldingCost, int Attack, int Defense, int? BurstStars);

public sealed record CardDefDto(
    string Id, string Name, string? Subtitle, string Type, int PurchaseCost,
    IReadOnlyList<string> EnergyTypes, IReadOnlyList<string> Affiliations, string? Alignment,
    int DieLimit, IReadOnlyList<CharacterFaceDto> Levels, string RawText,
    IReadOnlyList<string> Keywords, IReadOnlyList<string> AbilityTriggers)
{
    public static CardDefDto From(CardDef card) => new(
        card.Id, card.Name, card.Subtitle, card.Type.ToString(), card.PurchaseCost,
        card.EnergyTypes.Select(e => e.ToString()).ToList(),
        card.Affiliations, card.Alignment?.ToString(), card.DieLimit,
        card.Levels.Select(l => new CharacterFaceDto(l.FieldingCost, l.Attack, l.Defense, l.BurstStars)).ToList(),
        card.RawText, card.Keywords.Select(k => k.Name).ToList(),
        card.Abilities.Select(a => a.Trigger.ToString()).Distinct().ToList());
}

public sealed record DieDto(
    string Id, string? CardId, string OwnerId, string ControllerId, string Zone, string Status,
    int Level, int Damage, string EnergyKind, string? ProvidedEnergyType)
{
    public static DieDto From(DieInstance die) => new(
        die.Id, die.VirtualCardId ?? die.CardId, die.OwnerId, die.ControllerId,
        die.Zone.ToString(), die.Status.ToString(), die.Level, die.Damage,
        die.EnergyKind.ToString(), die.ProvidedEnergyType?.ToString());
}

public sealed record PlayerDto(string Id, string Name, int Life, int VirtualGenericEnergy)
{
    public static PlayerDto From(Player player) => new(player.Id, player.Name, player.Life, player.VirtualGenericEnergy);
}

public sealed record GameStateDto(
    string GameId, string ActivePlayerId, string CurrentStep, string AttackSubStep,
    bool IsFirstTurn, bool EpicBasicActionUsedThisTurn,
    PlayerDto PlayerOne, PlayerDto PlayerTwo, IReadOnlyList<DieDto> Dice)
{
    public static GameStateDto From(string gameId, GameState state) => new(
        gameId, state.ActivePlayerId, state.CurrentStep.ToString(), state.AttackSubStep.ToString(),
        state.IsFirstTurn, state.EpicBasicActionUsedThisTurn,
        PlayerDto.From(state.PlayerOne), PlayerDto.From(state.PlayerTwo),
        state.Dice.Select(DieDto.From).ToList());
}

// ---- Request bodies ----

public sealed record PurchaseRequest(string DieId, IReadOnlyList<string> EnergyDieIds);
public sealed record FieldRequest(string DieId, IReadOnlyList<string> EnergyDieIds);
public sealed record UseActionDieRequest(string DieId, IReadOnlyList<string>? TargetDieIds);
public sealed record UseGlobalAbilityRequest(
    string CardId, string PlayerId, IReadOnlyList<string> EnergyDieIds, IReadOnlyList<string>? TargetDieIds);
public sealed record FinishRollRequest(IReadOnlyList<string> RerollDieIds);
public sealed record DeclareAttackersRequest(IReadOnlyList<string> AttackerDieIds);
public sealed record BlockAssignment(string AttackerDieId, string BlockerDieId);
public sealed record DeclareBlockersRequest(IReadOnlyList<BlockAssignment> Assignments);
public sealed record DamageSplit(string AttackerDieId, string BlockerDieId, int Amount);
public sealed record AssignCombatDamageRequest(
    IReadOnlyList<BlockAssignment> Assignments, IReadOnlyList<DamageSplit> DamageSplits);
