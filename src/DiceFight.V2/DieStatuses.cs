using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// "Something is going on with this die" (2026-10-03, the status-cue design
// pass - v3/STATUS_CUES_BRIEF.md and Claude Design's "face frame" handoff).
// One list per die, read by both the tile and its tap explainer, so the two
// can't drift apart (the same idea as the stat breakdown).
//
// Kind is the client's key: mustBlock, cantBlock, unblockable, mustAttack,
// cantAttack, onlyBlocker, blanked, granted, deadly, intimidated.
// Duration: "turn" (cleared at Clean Up as a this-turn effect), "cleanup"
// (resolves at Clean Up), "whileActive" (as long as its source is out), or
// null when the engine doesn't know (a blank from no tracked source).
public sealed record DieStatus(string Kind, string? Source, string? Duration, string? Keyword = null,
    string? Description = null);

public static class DieStatuses
{
    public static IReadOnlyList<DieStatus> For(GameState state, DieInstance die)
    {
        var list = new List<DieStatus>();
        if (die.Zone == Zone.Intimidated)
        {
            list.Add(new DieStatus("intimidated", die.IntimidatedBy, "cleanup"));
            return list;
        }
        if (die.Zone is not (Zone.FieldZone or Zone.AttackZone) || state.GetCurrentFace(die)?.Character is null)
            return list;

        foreach (var flag in die.CombatFlags)
            list.Add(new DieStatus(FlagKind(flag), die.CombatFlagSources.GetValueOrDefault(flag), "turn"));

        foreach (var protection in state.GrantedTargetingProtections.Where(grant => grant.AppliesTo(state, die)))
            list.Add(new DieStatus("targetProtected", protection.Source,
                protection.Duration == Duration.EndOfTurn ? "turn" : null, Description: protection.Description));

        if (die.CardId is not null && !QueryEngine.AbilitiesActive(state, die))
            list.Add(new DieStatus("blanked", null, null));

        var keywordIds = state.Config.Keywords.Select(k => k.Id).ToHashSet();
        foreach (var granted in die.GrantedTags.Where(t => IsKeyword(t.Tag, keywordIds)))
        {
            // Printed already? Then it isn't a change worth a cue.
            if (die.CardId is { } cid && state.CardCatalog[cid].Keywords.Contains(granted.Tag)) continue;
            list.Add(new DieStatus("granted", granted.Source, granted.Duration == Duration.EndOfTurn ? "turn" : null, granted.Tag));
        }

        if (state.DeadlyEngagedDieIds.TryGetValue(die.Id, out var deadlyIds))
        {
            var names = deadlyIds.Select(id => state.Dice.FirstOrDefault(d => d.Id == id))
                .Select(d => d?.CardId is { } c ? state.CardCatalog[c].Name : "a Tardigrade").Distinct();
            list.Add(new DieStatus("deadly", string.Join(", ", names) + " (Deadly)", "cleanup"));
        }
        return list;
    }

    // Lanes holding an unblockable attacker - the whole lane can't be blocked
    // (CombatEngine.ValidateUnblockable).
    public static IReadOnlyList<int> UnblockableLanes(GameState state) =>
        state.DiceIn(state.ActivePlayerId, Zone.AttackZone)
            .Where(d => d.Lane is not null && d.CombatFlags.Contains(CombatFlagKind.Unblockable))
            .Select(d => d.Lane!.Value).Distinct().OrderBy(l => l).ToList();

    private static string FlagKind(CombatFlagKind flag) => flag switch
    {
        CombatFlagKind.MustBlock => "mustBlock",
        CombatFlagKind.CantBlock => "cantBlock",
        CombatFlagKind.Unblockable => "unblockable",
        CombatFlagKind.MustAttack => "mustAttack",
        CombatFlagKind.CantAttack => "cantAttack",
        CombatFlagKind.OnlyBlocker => "onlyBlocker",
        CombatFlagKind.PreventCombatDamage => "protected",
        _ => flag.ToString(),
    };

    // A granted tag counts as a keyword when the game declares it, including
    // an X form ("Range 2" for "Range").
    private static bool IsKeyword(string tag, IReadOnlySet<string> keywordIds) =>
        keywordIds.Contains(tag) || keywordIds.Any(k => KeywordAbilities.ParamOf(tag, k) is not null);
}
