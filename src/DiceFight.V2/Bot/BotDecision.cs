using DiceFight.V2.Data;

namespace DiceFight.V2.Bot;

// One move the computer opponent wants to make next - exactly one engine
// call's worth (the same granularity as one API endpoint), so the web
// client can pace/animate each one and the simulator can apply them in a
// loop. DiceKingdomBot.Decide produces these; BotDriver (simulator, tests)
// and the web client (via GET .../bot-decision, api.ts) carry them out.
public enum BotActionKind
{
    ClearAndDraw,
    Roll,
    Reroll,             // DieIds - the one reroll decision (rule 2.4.3); advances to Main
    FinishRoll,         // no reroll; advances to Main
    Foresight,          // DieId - Great Horned Owl's passive
    Field,              // DieId + EnergyDieIds
    Purchase,           // DieId (an Unpurchased die) + EnergyDieIds
    UseAction,          // DieId - an action die showing an action face
    UseGlobal,          // CardId + AbilityIndex + EnergyDieIds
    Pass,               // priority pass; SkipAttack only matters for the Active player in Main
    DeclareAttackers,   // AttackerLanes
    DeclareBlockers,    // Blocks
    ResolvePendingChoice, // DieIds (candidate ids - may include a player id)
    CleanUp,
    UseChampionPower,   // Wolf / Armadillo / Owl's once-per-turn power; its target is a pending choice after
}

public sealed record BotDecision(BotActionKind Kind, string Reason)
{
    public string? DieId { get; init; }
    public IReadOnlyList<string> DieIds { get; init; } = [];
    public IReadOnlyList<string> EnergyDieIds { get; init; } = [];
    public string? CardId { get; init; }
    public int AbilityIndex { get; init; }
    public bool SkipAttack { get; init; }
    /// <summary>Field: use Golden Eagle's free field.</summary>
    public bool Free { get; init; }
    public IReadOnlyDictionary<string, int> AttackerLanes { get; init; } = new Dictionary<string, int>();
    public IReadOnlyList<(string AttackerId, string BlockerId)> Blocks { get; init; } = [];
}

// Per-Champion play style (user, 2026-09-28: "a bot running Wolf may act
// more aggressively"). Every policy decision that is a matter of taste
// rather than arithmetic reads one of these; ForChampion picks the one a
// seat plays with, so the web opponent and the simulator agree.
public sealed record BotPersona(
    // How readily an attacker swings into a board that could kill it:
    // 0 = only when no blocker can kill it, 1 = accepts even trades,
    // 2 = swings into bad trades too.
    double Aggression = 1.0,
    // How many points of incoming damage a chump block (losing the
    // blocker) is worth at full life; scales up as life drops.
    double ChumpThreshold = 6.0,
    // A card to race toward: worth RushBonus extra when buying its first
    // copy, bought before fielding when affordable, and - until the first
    // copy is owned - rerolls favor energy over bodies.
    string? RushCardId = null,
    double RushBonus = 0,
    // A card whose Global to keep energy back for on the opponent's turn:
    // one matching energy-face die (or Wild) stays unspent at the end of
    // Main whenever the opponent has an attacker with HoldAgainstAttack+ ATK.
    string? HoldForGlobalCardId = null,
    int HoldAgainstAttack = 4,
    // What one more die in the Bag costs a purchase plan (2026-10-08,
    // user: "you don't want too many dice in your bag, or it will take
    // forever to get around to the dice you just bought") - so one big
    // buy can beat two small ones of more total cost. 2 since 2026-10-11:
    // the value that best matched 48 real games' purchases (and the most
    // even simulator results) - v3/DESIGN_NOTES.md.
    double DilutionPerDie = 2)
{
    public static readonly BotPersona Default = new();

    // Armadillo (2026-09-29, user call): race to Rhinoceros. It also kept
    // a Shell back for Distraction's Global until Archnemesis replaced
    // Distraction the same day (Archnemesis's Global - D becomes A - does
    // little for Armadillo's own low-ATK dice, so nothing is held now).
    public static readonly BotPersona Armadillo = Default with
    {
        RushCardId = DiceKingdomConfig.Rhinoceros.Id,
        RushBonus = 5,
    };

    public static BotPersona ForChampion(string? championId) => championId switch
    {
        "Armadillo" => Armadillo,
        _ => Default,
    };
}
