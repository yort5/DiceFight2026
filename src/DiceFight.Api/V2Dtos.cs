using DiceFight.V2;
using DiceFight.V2.Model;

namespace DiceFight.Api;

// v3 "Instinct Clash" wire contract - Phase 3 of the mellow-sparking-comet
// plan. Mirrors Dtos.cs's SHAPE (same record-per-concept, same
// Xyz.From(...) pattern) but only carries what DiceFight.V2 actually has:
// no AttackSubStep/EpicBasicActionUsedThisTurn/Range/BurstStars/
// VirtualEnergy - those are v1-only concepts. Energy type is a free-form
// string end to end, same as v1's own wire contract already was, so
// Claw/Shell/Wing/Eye needed no contract change, only new data.
//
// Every record is prefixed V2 (unlike Dtos.cs's own names) because both
// files share the DiceFight.Api namespace - Dtos.cs already owns
// CardDefDto/DieDto/PlayerDto/GameStateDto/etc. for v1. SeatDto is the one
// exception: reused as-is from Dtos.cs, since a seat is just two strings
// with no engine-specific type in it at all.
public sealed record V2CharacterFaceDto(int FieldingCost, int Attack, int Defense);

public sealed record V2CardDefDto(
    string Id, string Name, string? Subtitle, int PurchaseCost,
    IReadOnlyList<string> EnergyTypes, int DieLimit,
    IReadOnlyList<V2CharacterFaceDto> Levels, string RawText, IReadOnlyList<string> Keywords)
{
    public static V2CardDefDto From(CardDef card) => new(
        card.Id, card.Name, card.Subtitle, card.PurchaseCost, card.EnergySymbolIds, card.DieLimit,
        card.Die.Faces.Where(f => f.Character is not null)
            .Select(f => new V2CharacterFaceDto(f.Character!.FieldingCost, f.Character.Attack, f.Character.Defense))
            .ToList(),
        card.RawText, card.Keywords);
}

// EffectiveAttack/EffectiveDefense run through QueryEngine (Champion
// passives and any other stat modifier included) only for a die actually
// IN PLAY (FieldZone/AttackZone) - everywhere else (Reserve Pool above
// all) this is the PRINTED, unmodified face value. A modifier is not a
// guarantee: a Champion passive could be disabled, or a StatAura's source
// character could be KO'd, before a Reserve Pool die is ever fielded, so
// showing a boosted number there would be promising a stat the die might
// never actually have. The player's field-or-not decision belongs on the
// die's real, base stats - not a preview of a buff that may not hold.
// Null on an energy-only face (Surge) or before the die has been rolled
// at all. IsTardigrade mirrors v1's own DieInstance.IsSidekick precedent
// (CardId null = the basic pool creature) - the one status distinction
// the web client's board components actually branch on.
public sealed record V2StatModifierDto(string Label, int Delta)
{
    public static V2StatModifierDto From(StatModifierContribution c) => new(c.Label, c.Delta);
}

public sealed record V2DieDto(
    string Id, string? CardId, string OwnerId, string ControllerId, string Zone,
    bool IsTardigrade, int? Level, int? EffectiveAttack, int? EffectiveDefense,
    string? EnergySymbolId, int EnergyAmount, int? Lane,
    // Only populated alongside a non-null EffectiveAttack/EffectiveDefense
    // for a die actually in play (see the class remarks above) - the
    // printed base plus every named modifier that summed into the
    // effective value, for the tap-to-explain breakdown UI (direct
    // feedback, 2026-09-17: "click on the '1 v 3' and have it explain
    // where the numbers are coming from").
    int? BaseAttack, int? BaseDefense,
    IReadOnlyList<V2StatModifierDto>? AttackModifiers, IReadOnlyList<V2StatModifierDto>? DefenseModifiers,
    // Damage already marked on the die (e.g. from an on-field ping) - the
    // Attack Zone combat preview needs it to know how much Defense is left.
    int Damage = 0)
{
    private static readonly HashSet<DiceFight.V2.Model.Zone> InPlayZones =
        [DiceFight.V2.Model.Zone.FieldZone, DiceFight.V2.Model.Zone.AttackZone];

    public static V2DieDto From(GameState state, DieInstance die)
    {
        var face = state.GetCurrentFace(die);
        var symbol = face?.Symbols.FirstOrDefault();
        var inPlay = InPlayZones.Contains(die.Zone);
        var hasCharacterFace = face?.Character is not null;
        var showBreakdown = hasCharacterFace && inPlay;
        return new(
            die.Id, die.CardId, die.OwnerId, die.ControllerId, die.Zone.ToString(),
            die.CardId is null,
            face?.Character?.Level,
            hasCharacterFace ? (inPlay ? QueryEngine.GetAttack(state, die) : QueryEngine.GetBaseAttack(state, die)) : null,
            hasCharacterFace ? (inPlay ? QueryEngine.GetDefense(state, die) : QueryEngine.GetBaseDefense(state, die)) : null,
            symbol?.SymbolId, symbol?.Count ?? 0, die.Lane,
            showBreakdown ? QueryEngine.GetBaseAttack(state, die) : null,
            showBreakdown ? QueryEngine.GetBaseDefense(state, die) : null,
            showBreakdown ? QueryEngine.GetAttackBreakdown(state, die).Select(V2StatModifierDto.From).ToList() : null,
            showBreakdown ? QueryEngine.GetDefenseBreakdown(state, die).Select(V2StatModifierDto.From).ToList() : null,
            die.Damage);
    }
}

public sealed record ChampionDto(string Id, string Name, string EnergySymbolId, string PassiveText)
{
    public static ChampionDto From(ChampionDef champion) => new(
        champion.Id, champion.Name, champion.EnergySymbolId, PassiveTextOf(champion));

    // Plain-language rendering of the closed ChampionPassiveKind enum -
    // matches the four passives the "Instinct Clash" artifact prototype
    // already established this same wording for.
    private static string PassiveTextOf(ChampionDef c) => c.PassiveKind switch
    {
        ChampionPassiveKind.AttackBuff => $"+{c.Amount} ATK to all your dice",
        ChampionPassiveKind.DefenseBuff => $"+{c.Amount} DEF to all your dice",
        ChampionPassiveKind.FieldingCostDiscount => $"Your dice cost {c.Amount} less to field (min 0)",
        ChampionPassiveKind.PurchaseCostDiscount => $"Your Character purchases cost {c.Amount} less (min 1)",
        _ => "",
    };
}

public sealed record V2PlayerDto(string Id, string Name, int Life, ChampionDto? Champion)
{
    public static V2PlayerDto From(GameState state, Player player) => new(
        player.Id, player.Name, player.Life,
        state.Config.Champions.FirstOrDefault(c => c.Id == player.ChampionId) is { } champion
            ? ChampionDto.From(champion)
            : null);
}

public sealed record V2PendingChoiceDto(
    string ControllerId, string Description, IReadOnlyList<string> CandidateIds, int MinCount, int MaxCount)
{
    public static V2PendingChoiceDto From(PendingChoice pending) =>
        new(pending.ControllerId, pending.Description, pending.CandidateIds, pending.MinCount, pending.MaxCount);
}

public sealed record V2GameLogEntryDto(int Seq, string? PlayerId, string Text)
{
    public static V2GameLogEntryDto From(DiceFight.V2.Model.GameLogEntry entry) => new(entry.Seq, entry.PlayerId, entry.Text);
}

public sealed record V2CreatedGameDto(V2GameStateDto Game, IReadOnlyList<SeatDto> Seats);

public sealed record V2GameStateDto(
    string GameId, string ActivePlayerId, string CurrentStep, string CurrentStepId,
    V2PlayerDto PlayerOne, V2PlayerDto PlayerTwo, IReadOnlyList<V2DieDto> Dice, V2PendingChoiceDto? PendingChoice,
    IReadOnlyList<V2GameLogEntryDto> Log,
    string? YourPlayerId = null, int Version = 0)
{
    public static V2GameStateDto From(string gameId, GameState state, string? yourPlayerId = null, int version = 0) => new(
        gameId, state.ActivePlayerId, state.CurrentStep.ToString(), state.CurrentStepId,
        V2PlayerDto.From(state, state.PlayerOne), V2PlayerDto.From(state, state.PlayerTwo),
        state.Dice.Select(d => V2DieDto.From(state, d)).ToList(),
        state.PendingChoice is { } pending ? V2PendingChoiceDto.From(pending) : null,
        state.Log.Select(V2GameLogEntryDto.From).ToList(),
        yourPlayerId, version);
}

// ---- Request bodies ----

// No team-builder yet (v3/DESIGN_NOTES.md's own open question) - picking
// a Champion picks the team: both of InstinctClashConfig.
// CharactersByChampion[championId] automatically.
public sealed record CreateV2GameRequest(string PlayerOneChampionId, string PlayerTwoChampionId);
public sealed record V2PurchaseRequest(string DieId, IReadOnlyList<string> EnergyDieIds);
public sealed record V2FieldRequest(string DieId, IReadOnlyList<string> EnergyDieIds);
public sealed record V2RerollRequest(IReadOnlyList<string> DieIds);
// Lane is which of the Attack Zone's four fixed lanes (0-3) the die is
// declared into - mobile refresh (2026-09), see DieInstance.Lane's own
// remarks. Several attackers may share a lane.
public sealed record V2AttackerDeclaration(string DieId, int Lane);
public sealed record V2DeclareAttackersRequest(IReadOnlyList<V2AttackerDeclaration> Attackers);
public sealed record V2BlockAssignment(string AttackerDieId, string BlockerDieId);
public sealed record V2DeclareBlockersRequest(IReadOnlyList<V2BlockAssignment> Assignments);
// No manual damage-split field, unlike v1's AssignCombatDamageRequest -
// none of InstinctClashConfig's 8 Characters grant multi-blocker combat
// (CombatRuleKind.BlocksN), so every attacker has at most one live
// blocker and the controller computes the (trivial) split itself.
// Assignments is resent here for the same reason v1's own DTOs note:
// CombatAssignment isn't persisted server-side between calls.
public sealed record V2AssignCombatDamageRequest(IReadOnlyList<V2BlockAssignment> Assignments);
public sealed record V2ResolvePendingChoiceRequest(IReadOnlyList<string> ChosenDieIds);
