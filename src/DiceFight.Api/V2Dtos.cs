using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.Api;

// v3 "Dice Kingdom" wire contract - Phase 3 of the mellow-sparking-comet
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
public sealed record V2CharacterFaceDto(int FieldingCost, int Attack, int Defense, int Burst = 0);

public sealed record V2CardDefDto(
    string Id, string Name, string? Subtitle, int PurchaseCost,
    IReadOnlyList<string> EnergyTypes, int DieLimit,
    IReadOnlyList<V2CharacterFaceDto> Levels, string RawText, IReadOnlyList<string> Keywords,
    // Basic Actions (2026-09-26): IsAction marks a statless action card;
    // ActionText/Global split RawText at "Global:" for display; DieEnergyType
    // is the energy its die's energy faces show (a Basic Action has no
    // purchase type of its own, so EnergyTypes can't say).
    bool IsAction = false, string? ActionText = null, V2GlobalDto? Global = null, string? DieEnergyType = null,
    // "Whenever this takes damage, it deals that much to the opponent"
    // (Rhinoceros) - read off the ability, not the name. The client shows
    // that reflected damage as its own lane marker, apart from "to face"
    // (2026-09-30: folding the two together read as confusing).
    bool ReflectsDamage = false, bool HasErrata = false, bool IsImplemented = true,
    string? Type = null, string? Set = null, IReadOnlyList<string>? Affiliations = null,
    // Pack rarity (DiceKingdomPacks) - null for Basic Actions. KingdomSet:
    // "Set 1" etc. (DiceKingdomSets), not Set - that's the Dice Masters source.
    string? Rarity = null, IReadOnlyList<int>? ActionFaceBursts = null, string? KingdomSet = null)
{
    public static V2CardDefDto From(CardDef card)
    {
        var globalIndex = card.Abilities.ToList().FindIndex(a => a.Trigger == TriggerKind.Global);
        var split = card.RawText.IndexOf("Global:", StringComparison.Ordinal);
        V2GlobalDto? global = null;
        if (globalIndex >= 0)
        {
            var ability = card.Abilities[globalIndex];
            global = new V2GlobalDto(globalIndex,
                split >= 0 ? card.RawText[(split + "Global:".Length)..].Trim() : card.RawText,
                ability.EnergyCost?.Amount ?? 0, ability.EnergyCost?.RequiredSymbolId, ability.OncePerTurn);
        }
        return new(
            card.Id, card.Name, card.Subtitle, card.PurchaseCost, card.EnergySymbolIds, card.DieLimit,
            card.Die.Faces.Where(f => f.Character is not null)
                .Select(f => new V2CharacterFaceDto(f.Character!.FieldingCost, f.Character.Attack, f.Character.Defense, f.Burst))
                .ToList(),
            card.RawText, card.Keywords,
            card.CardType.IsActionDie(),
            split >= 0 ? card.RawText[..split].Trim() : card.RawText,
            global,
            card.Die.Faces.SelectMany(f => f.Symbols).Select(sym => sym.SymbolId).FirstOrDefault(),
            card.Abilities.Any(a => a.Trigger == TriggerKind.DieDamaged
                && a.Effect is DealDamage { Target: { Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing } }),
            card.HasErrata, card.IsImplemented, card.CardType.ToString(), card.Set, card.Affiliations,
            Rarity: DiceKingdomPacks.Rarities.TryGetValue(card.Id, out var rarity) ? rarity.ToString() : null,
            ActionFaceBursts: card.CardType.IsActionDie() ? card.Die.Faces.Where(f => f.Kind == FaceKind.ActionFace).Select(f => f.Burst).ToList() : null,
            KingdomSet: DiceKingdomSets.SetOf(card.Id));
    }
}

public sealed record V2GlobalDto(int AbilityIndex, string Text, int Cost, string? EnergyType, bool OncePerTurn);

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
    int Damage = 0,
    // Declaration order among attackers (DieInstance.AttackOrder) - the
    // client stacks a lane's attackers by it.
    int? AttackOrder = null,
    // Showing an action face (a Basic Action die that can be used).
    bool IsActionFace = false,
    // What fielding this die costs right now, champion discounts and any
    // other modifier included (QueryEngine.GetFieldingCost) - the client
    // used to read the printed cost, so a discount made "pay 1" charge 0
    // with nothing on screen saying why (direct feedback, 2026-09-27).
    int? FieldingCost = null,
    // Forced to block this turn (Hermit Crab) - the client marks it and
    // won't confirm blocks without it (2026-09-30: the server's own
    // rejection used to be the only sign, and it named an internal die id).
    bool MustBlock = false,
    // Can't be blocked this turn (Obscure, 2026-10-03) - and neither can
    // anything sharing its lane. Same reason as MustBlock: a blocker's
    // only clue used to be the server's rejection.
    bool Unblockable = false,
    // Every status cue on this die, each with its source and duration
    // (DieStatuses - the status-cue design pass, 2026-10-03). The tile and
    // its tap explainer both read this. MustBlock/Unblockable above stay
    // for older clients.
    IReadOnlyList<V2DieStatusDto>? Statuses = null,
    // The latest effect-caused spin, for a one-off flash (DieInstance.LastSpin).
    V2SpinDto? LastSpin = null, int Burst = 0)
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
            die.Damage,
            die.AttackOrder,
            face?.Kind == FaceKind.ActionFace,
            hasCharacterFace ? QueryEngine.GetFieldingCost(state, die) : null,
            die.CombatFlags.Contains(CombatFlagKind.MustBlock),
            die.CombatFlags.Contains(CombatFlagKind.Unblockable),
            DieStatuses.For(state, die).Select(V2DieStatusDto.From).ToList(),
            die.LastSpin is { } spin ? new V2SpinDto(spin.Seq, spin.FromLevel, spin.ToLevel, spin.Source) : null, face?.Burst ?? 0);
    }
}

public sealed record V2DieStatusDto(string Kind, string? Source, string? Duration, string? Keyword,
    string? Description = null)
{
    public static V2DieStatusDto From(DieStatus s) => new(s.Kind, s.Source, s.Duration, s.Keyword, s.Description);
}

public sealed record V2SpinDto(int Seq, int? FromLevel, int? ToLevel, string Source);

// A card one player can't buy or field right now (Pangolin's lockout), and why.
public sealed record V2LockedCardDto(string PlayerId, string CardId, IReadOnlyList<string> Sources);

public sealed record ChampionDto(string Id, string Name, string EnergySymbolId, string PassiveText)
{
    public static ChampionDto From(ChampionDef champion) => new(
        champion.Id, champion.Name, champion.EnergySymbolId, PassiveTextOf(champion));

    // Plain-language rendering of the closed ChampionPassiveKind enum -
    // matches the four passives the "Dice Kingdom" artifact prototype
    // already established this same wording for.
    private static string PassiveTextOf(ChampionDef c) => ChampionPowers.Describe(c);
}

// ForesightAvailable: this player's Champion has Foresight and it's still
// unused this turn (TurnEngine.UseForesight) - the client offers it then.
// VirtualEnergy: generic energy from dice they couldn't draw this turn
// (GameState.VirtualEnergy) - spent automatically before any die, gone
// at the end of their Main Step.
// ChampionPowerUsable: Wolf/Armadillo/Owl's once-per-turn power can be
// used right now (ChampionPowers.CanUse - priority is still checked when
// it's used). FreeFieldAvailable: Golden Eagle's free field is unused and
// it's this player's Main Step.
public sealed record V2PlayerDto(string Id, string Name, int Life, ChampionDto? Champion, bool ForesightAvailable = false, int VirtualEnergy = 0,
    bool ChampionPowerUsable = false, bool FreeFieldAvailable = false)
{
    public static V2PlayerDto From(GameState state, Player player) => new(
        player.Id, player.Name, player.Life,
        state.Config.Champions.FirstOrDefault(c => c.Id == player.ChampionId) is { } champion
            ? ChampionDto.From(champion)
            : null,
        TurnEngine.HasForesight(state, player.Id) && !state.ForesightUsedThisTurn.Contains(player.Id),
        state.VirtualEnergyOf(player.Id),
        ChampionPowers.CanUse(state, player.Id),
        ChampionPowers.CanFieldFree(state, player.Id));
}

// Intent: what the pick does to what's picked (PendingChoice.Intent) -
// "NameCard" tells the client the pick names a CARD, so it can offer one
// option per card instead of every die of it (Pangolin's lockout).
public sealed record V2PendingChoiceDto(
    string ControllerId, string Description, IReadOnlyList<string> CandidateIds, int MinCount, int MaxCount,
    ChoiceIntent Intent = ChoiceIntent.Unknown, bool IsConfirmation = false)
{
    public static V2PendingChoiceDto From(PendingChoice pending) =>
        new(pending.ControllerId, pending.Description, pending.CandidateIds, pending.MinCount, pending.MaxCount, pending.Intent, pending.IsConfirmation);
}

public sealed record V2GameLogEntryDto(int Seq, string? PlayerId, string Text, bool IsTurnStart)
{
    public static V2GameLogEntryDto From(DiceFight.V2.Model.GameLogEntry entry) => new(entry.Seq, entry.PlayerId, entry.Text, entry.IsTurnStart);
}

public sealed record V2CreatedGameDto(V2GameStateDto Game, IReadOnlyList<SeatDto> Seats);

public sealed record V2GameStateDto(
    string GameId, string ActivePlayerId, string CurrentStep, string CurrentStepId,
    V2PlayerDto PlayerOne, V2PlayerDto PlayerTwo, IReadOnlyList<V2DieDto> Dice, V2PendingChoiceDto? PendingChoice,
    IReadOnlyList<V2GameLogEntryDto> Log,
    string? YourPlayerId = null, int Version = 0,
    // The declared blocks this combat (GameState.DeclaredBlocks), in
    // assignment order - empty before blockers are declared.
    IReadOnlyList<V2BlockAssignment>? Blocks = null,
    // Who may act in the current Main Step / action window (Priority.cs);
    // null outside those windows.
    string? PriorityPlayerId = null,
    // What each card in this game costs YOU to buy (discounts included) -
    // the purchase-side twin of V2DieDto.FieldingCost. Null without a seat.
    IReadOnlyDictionary<string, int>? PurchaseCosts = null,
    // Would the Active player's pass right now hand priority to the other
    // player (they could still use a Global - Priority.CanUseAnyGlobal), or
    // close the window? The client labels the button "Pass Priority" vs
    // "Resolve Damage" by it (2026-09-30).
    bool PassGivesPriority = false,
    // Rule 2.9: the game is over once a player's Life reaches 0 - WinnerId
    // null with GameOver true is a tie.
    bool GameOver = false,
    string? WinnerId = null,
    // Status cues (2026-10-03): lanes no blocker can enter, and lockouts.
    IReadOnlyList<int>? UnblockableLanes = null,
    IReadOnlyList<V2LockedCardDto>? LockedCards = null)
{
    public static V2GameStateDto From(string gameId, GameState state, string? yourPlayerId = null, int version = 0) => new(
        gameId, state.ActivePlayerId, state.CurrentStep.ToString(), state.CurrentStepId,
        V2PlayerDto.From(state, state.PlayerOne), V2PlayerDto.From(state, state.PlayerTwo),
        state.Dice.Select(d => V2DieDto.From(state, d)).ToList(),
        state.PendingChoice is { } pending ? V2PendingChoiceDto.From(pending) : null,
        state.Log.Select(V2GameLogEntryDto.From).ToList(),
        yourPlayerId, version,
        state.DeclaredBlocks?.Pairs.Select(p => new V2BlockAssignment(p.AttackerDieId, p.BlockerDieId)).ToList() ?? [],
        state.PriorityPlayerId,
        yourPlayerId is null ? null
            : state.PlayerOne.TeamCardIds.Concat(state.PlayerTwo.TeamCardIds).Distinct()
                .Where(state.CardCatalog.ContainsKey)
                .ToDictionary(id => id, id => QueryEngine.GetPurchaseCostNow(state, state.CardCatalog[id], yourPlayerId)),
        Priority.IsWindow(state) && state.PriorityPlayerId == state.ActivePlayerId
            && Priority.CanUseAnyGlobal(state, state.OpponentOf(state.ActivePlayerId)),
        state.IsGameOver,
        state.WinnerId,
        DieStatuses.UnblockableLanes(state),
        LockedCardsFor(state));

    private static IReadOnlyList<V2LockedCardDto> LockedCardsFor(GameState state) =>
        (from player in new[] { state.PlayerOne, state.PlayerTwo }
         from cardId in state.PlayerOne.TeamCardIds.Concat(state.PlayerTwo.TeamCardIds).Distinct()
         where state.CardCatalog.ContainsKey(cardId)
         let sources = QueryEngine.LockoutSources(state, player.Id, cardId)
         where sources.Count > 0
         select new V2LockedCardDto(player.Id, cardId, sources)).ToList();
}

// ---- Request bodies ----

// Champion presets remain compatible with existing clients. Custom teams
// provide exact quantities, and may name a Champion of their own (2026-10-19)
// for its power - its Basic Action(s) must then share the Champion's energy.
// Either seat can use a Champion preset OR an explicitly selected custom team.
// The builder enforces its own formats; the server only checks data validity.
public sealed record CustomV2CardDice(string CardId, int DiceCount);
public sealed record CustomV2TardigradeDice(string EnergyType, int DiceCount);
public sealed record CustomV2TeamRequest(
    IReadOnlyList<CustomV2CardDice> Cards,
    IReadOnlyList<CustomV2TardigradeDice> Tardigrades,
    string? ChampionId = null);
public sealed record CreateV2GameRequest(
    string? PlayerOneChampionId = null,
    string? PlayerTwoChampionId = null,
    CustomV2TeamRequest? PlayerOneCustomTeam = null,
    CustomV2TeamRequest? PlayerTwoCustomTeam = null,
    // A boss battle (V2/Data/DiceKingdomBosses.cs): the boss is player two,
    // played by the computer in the boss's style. Player Two's own fields
    // must then be empty.
    string? BossId = null);
public sealed record V2PurchaseRequest(string DieId, IReadOnlyList<string> EnergyDieIds);
public sealed record V2FieldRequest(string DieId, IReadOnlyList<string> EnergyDieIds, bool Free = false);
public sealed record V2RerollRequest(IReadOnlyList<string> DieIds);
// Lane is which of the Attack Zone's four fixed lanes (0-3) the die is
// declared into - mobile refresh (2026-09), see DieInstance.Lane's own
// remarks. Several attackers may share a lane.
public sealed record V2AttackerDeclaration(string DieId, int Lane);
public sealed record V2UseActionRequest(string DieId);
public sealed record V2UseGlobalRequest(string CardId, int AbilityIndex, IReadOnlyList<string> EnergyDieIds);
public sealed record V2DeclareAttackersRequest(IReadOnlyList<V2AttackerDeclaration> Attackers);
public sealed record V2BlockAssignment(string AttackerDieId, string BlockerDieId);
public sealed record V2DeclareBlockersRequest(IReadOnlyList<V2BlockAssignment> Assignments);
// No manual damage-split field, unlike v1's AssignCombatDamageRequest -
// none of DiceKingdomConfig's 8 Characters grant multi-blocker combat
// (CombatRuleKind.BlocksN), so every attacker has at most one live
// blocker and the controller computes the (trivial) split itself.
// Assignments is resent here for the same reason v1's own DTOs note:
// CombatAssignment isn't persisted server-side between calls.
public sealed record V2AssignCombatDamageRequest(IReadOnlyList<V2BlockAssignment> Assignments);
public sealed record V2ResolvePendingChoiceRequest(IReadOnlyList<string> ChosenDieIds);

// GET .../bot-decision - the shared computer opponent's next move
// (DiceFight.V2/Bot/DiceKingdomBot.cs), for the web client to carry out
// through the matching endpoint. Kind is the BotActionKind name in
// camelCase ("purchase", "declareAttackers", ...); only the fields that
// kind uses are meaningful.
public sealed record V2BotDecisionDto(
    string Kind,
    string Reason,
    string? DieId,
    IReadOnlyList<string> DieIds,
    IReadOnlyList<string> EnergyDieIds,
    string? CardId,
    int AbilityIndex,
    bool SkipAttack,
    IReadOnlyList<V2AttackerDeclaration> Attackers,
    IReadOnlyList<V2BlockAssignment> Assignments,
    // Field with Golden Eagle's free field.
    bool Free = false)
{
    public static V2BotDecisionDto From(DiceFight.V2.Bot.BotDecision d) => new(
        char.ToLowerInvariant(d.Kind.ToString()[0]) + d.Kind.ToString()[1..],
        d.Reason,
        d.DieId,
        d.DieIds,
        d.EnergyDieIds,
        d.CardId,
        d.AbilityIndex,
        d.SkipAttack,
        d.AttackerLanes.Select(kv => new V2AttackerDeclaration(kv.Key, kv.Value)).ToList(),
        d.Blocks.Select(b => new V2BlockAssignment(b.AttackerId, b.BlockerId)).ToList(),
        d.Free);
}

// ---- Open games: host picks only their own Champion (2026-09-30) ----

// Either a Champion preset or a custom team (2026-10-19), for each side.
public sealed record OpenV2GameRequest(string? ChampionId = null, CustomV2TeamRequest? CustomTeam = null);
public sealed record JoinV2GameRequest(string? ChampionId = null, CustomV2TeamRequest? CustomTeam = null);

// Seats: both, like V2CreatedGameDto - the host keeps theirs and turns
// the other into the invite link (web seats.ts inviteLink).
// HostChampionId is "" for a custom team without a Champion; HostLabel is
// what to call the host's team ("Wolf", "Wolf - custom team", "a custom team").
public sealed record V2OpenGameDto(string GameId, string HostChampionId, IReadOnlyList<SeatDto> Seats, string HostLabel);

// Started: the invited player has picked and the real game exists (GET
// .../{id} works from then on). YourPlayerId: which seat the caller holds.
public sealed record V2LobbyDto(string GameId, string HostChampionId, bool Started, string? YourPlayerId, string HostLabel);
