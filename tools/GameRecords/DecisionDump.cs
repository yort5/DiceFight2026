using System.Text.Json;
using DiceFight.Api.Recording;
using DiceFight.V2;
using DiceFight.V2.Bot;
using DiceFight.V2.Data;
using DiceFight.V2.Model;

// One human decision for Program.cs's --dump, with dice named the way a
// player sees them ("Mongoose L2 3/2", "Tardigrade Claw 2 energy") rather
// than by die id, and the board as it was when they chose.
static class DecisionDump
{
    public static object Describe(GameRecord record, GameState state, RecordedAction action, BotDecision bot, string decision, bool agreed)
    {
        var me = action.PlayerId;
        var them = state.OpponentOf(me);
        string Die(string? id) => id is null ? "" : state.Dice.FirstOrDefault(d => d.Id == id) is { } d ? Name(state, d) : id;
        List<string> Dice(IEnumerable<string> ids) => ids.Select(Die).ToList();
        List<string> Zone(string player, Zone zone) => state.DiceIn(player, zone).Select(d => Name(state, d)).Order().ToList();
        List<string> Strings(string name) => action.Request is { } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : x.TryGetProperty("dieId", out var id) ? id.GetString()! : x.GetRawText()).ToList() : [];
        string? Str(string name) => action.Request is { } b && b.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        return new
        {
            game = record.GameId,
            champion = state.GetPlayer(me).ChampionId,
            opponent = state.GetPlayer(them).ChampionId,
            turn = action.Turn,
            step = action.Step,
            decision,
            agreed,
            played = new
            {
                action = action.Action,
                die = Die(Str("dieId")),
                dice = Dice(Strings("dieIds").Concat(Strings("chosenDieIds")).Concat(Strings("attackers"))),
                energy = Dice(Strings("energyDieIds")),
                card = Str("cardId") is { } c ? CardName(c) : null,
                request = action.Request?.GetRawText(),
            },
            bot = new
            {
                kind = bot.Kind.ToString(),
                bot.Reason,
                die = Die(bot.DieId),
                dice = Dice(bot.DieIds.Concat(bot.AttackerLanes.Keys)),
                energy = Dice(bot.EnergyDieIds),
                card = bot.CardId is { } bc ? CardName(bc) : null,
                blocks = bot.Blocks.Select(b => $"{Die(b.BlockerId)} blocks {Die(b.AttackerId)}").ToList(),
            },
            life = new { me = state.GetPlayer(me).Life, them = state.GetPlayer(them).Life },
            // The roll in progress (Roll & Reroll step).
            rolled = Zone(me, DiceFight.V2.Model.Zone.DiceFromBag).Concat(Zone(me, DiceFight.V2.Model.Zone.DiceFromPrep)).ToList(),
            reserve = Zone(me, DiceFight.V2.Model.Zone.ReservePool),
            field = Zone(me, DiceFight.V2.Model.Zone.FieldZone),
            theirField = Zone(them, DiceFight.V2.Model.Zone.FieldZone),
            bag = state.DiceIn(me, DiceFight.V2.Model.Zone.Bag).Count(),
            used = state.DiceIn(me, DiceFight.V2.Model.Zone.UsedPile).Count(),
            // Cards still to buy: name -> dice left, with the cost.
            unpurchased = state.DiceIn(me, DiceFight.V2.Model.Zone.Unpurchased).GroupBy(d => d.CardId!)
                .ToDictionary(g => $"{CardName(g.Key)} ({DiceKingdomConfig.Catalog[g.Key].PurchaseCost})", g => g.Count()),
            owned = state.DiceFor(me).Where(d => d.CardId is not null && d.Zone != DiceFight.V2.Model.Zone.Unpurchased)
                .GroupBy(d => CardName(d.CardId!)).ToDictionary(g => g.Key, g => g.Count()),
        };
    }

    static string CardName(string cardId) => DiceKingdomConfig.Catalog.TryGetValue(cardId, out var c) ? c.Name : cardId;

    public static string Name(GameState state, DieInstance die)
    {
        var name = die.CardId is { } c ? CardName(c) : die.PoolDieId?.Replace("Tardigrade", "Tardigrade ") ?? die.Id;
        if (state.GetCurrentFace(die) is not { } face) return name;
        var energy = string.Join("+", face.Symbols.Select(s => $"{s.Count} {s.SymbolId}"));
        return face.Character is { } ch
            ? $"{name} L{ch.Level} {ch.Attack}/{ch.Defense} (field {ch.FieldingCost})"
            : $"{name} [{(energy == "" ? face.Kind.ToString() : energy)}]";
    }
}
