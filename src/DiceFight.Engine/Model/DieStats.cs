using DiceFight.Engine;

namespace DiceFight.Engine.Model;

// Rule 1.3 key - the physical Sidekick die face: fielding cost 0, 1A/1D.
// Sidekicks have no card (rule 1.3.9), so this isn't card-driven data.
public static class DieStats
{
    // Whether the die currently has the named keyword - either printed on
    // its own card (Overcrush, Regenerate, etc. - see CardDef.Keywords/
    // KeywordInstance), or granted live by some other active die's
    // "while active, your Sidekicks gain [keyword]" text (e.g. Darkseid:
    // "your Sidekicks gain Swarm" - see CardDef.GrantsToSidekicks). The
    // grant is re-evaluated every call, not cached, since it depends on
    // the current board (the granting die must still be active) exactly
    // like any other "while active" effect. Guarded against `keyword ==
    // "Ally"` to avoid a cycle: CountsAsSidekick is how an Ally die gets
    // into the grant-eligible set in the first place, so asking "is Ally
    // granted" would recurse into itself; granting Ally via "your
    // Sidekicks gain Ally" isn't a real card pattern anyway.
    public static bool HasKeyword(GameState state, DieInstance die, string keyword)
    {
        if (HasPrintedKeyword(state, die, keyword)) return true;
        if (keyword == "Overcrush" && HasStrikeBonus(state, die)) return true;

        if (keyword == "Ally" || !CountsAsSidekick(state, die)) return false;

        var granters = state.DiceIn(die.ControllerId, Zone.FieldZone)
            .Concat(state.DiceIn(die.ControllerId, Zone.AttackZone));
        return granters.Any(granter =>
        {
            var granterCardId = granter.VirtualCardId ?? granter.CardId;
            return granterCardId is not null
                && state.CardCatalog.TryGetValue(granterCardId, out var granterCard)
                && granterCard.GrantsToSidekicks.Contains(keyword);
        });
    }

    private static bool HasPrintedKeyword(GameState state, DieInstance die, string keyword)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is not null
            && state.CardCatalog.TryGetValue(cardId, out var card)
            && card.Keywords.Any(k => k.Name == keyword);
    }

    // Keyword Strike - "On the turn you field a Character die with
    // Strike, at the end of the Main Step, if you fielded no other
    // Character dice this turn, this Character die gets +2A, +2D, and
    // Overcrush." The printed reminder text (Bizarro: "...so long as it
    // is the only character die you fielded this turn") phrases this as
    // a live, continuously-true condition rather than a one-time
    // snapshot, so it's recomputed on demand from GameState.
    // FieldedThisTurn (populated by TurnEngine.Field, cleared each turn
    // in CleanUp) instead of being applied once at a fixed instant - the
    // two only differ if something inspects this die's stats mid-Main-
    // Step before any other die could possibly have been fielded yet,
    // which changes nothing observable either way. "Fielded," not
    // "purchased" or "active" - a die fielded this turn that was later
    // KO'd still counts against a DIFFERENT Strike die's own check
    // (fielding is a historical fact about the turn, not current board
    // state), and Sidekicks fielded onto a character face count too,
    // same as TurnEngine.Field's own validation already treats them.
    public static bool HasStrikeBonus(GameState state, DieInstance die)
    {
        if (die.Zone is not (Zone.FieldZone or Zone.AttackZone)) return false;
        if (!HasPrintedKeyword(state, die, "Strike")) return false;
        if (!state.FieldedThisTurn.Contains(die.Id)) return false;

        return state.Dice.Count(d => state.FieldedThisTurn.Contains(d.Id) && d.ControllerId == die.ControllerId) == 1;
    }

    // Keyword Energy Drain X - "spin each Character die engaged with a
    // Character die with Energy Drain down [X] level(s)." Returns 0 if
    // this die doesn't have the keyword at all (checked via the printed
    // card only - no card grants Energy Drain the way Darkseid grants
    // Swarm yet, so there's no numeric amount to look up on a grant),
    // otherwise the X in "Energy Drain X" (KeywordInstance.Params[0]),
    // defaulting to 1 for the bare "Energy Drain" wording.
    public static int EnergyDrainAmount(GameState state, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        if (cardId is null || !state.CardCatalog.TryGetValue(cardId, out var card)) return 0;
        var keyword = card.Keywords.FirstOrDefault(k => k.Name == "Energy Drain");
        if (keyword is null) return 0;
        return keyword.Params is { Count: > 0 } ? keyword.Params[0] : 1;
    }

    // Keyword Ally - Appendix 1: "Character dice with the Ally keyword
    // ability are considered Sidekick Character dice while in the Field
    // Zone... in addition to their other attributes. They don't count as
    // Sidekick dice while in the bag, Prep Area, Used Pile, or Reserve
    // Pool." This is the zone-gated superset of DieInstance.IsSidekick
    // (which only knows about real, cardless physical Sidekicks) - use
    // this one for any "is this die a legal Sidekick target/effect
    // subject right now" question; use the raw property only when the
    // zone-independent physical fact is what's actually being asked (e.g.
    // "how many physical Sidekick dice does this player own").
    // AttackZone counts too, since it's a subset of the Field Zone
    // (rule 1.5.6.1), not a separate play area.
    public static bool CountsAsSidekick(GameState state, DieInstance die) =>
        die.IsSidekick || (die.Zone is Zone.FieldZone or Zone.AttackZone && HasKeyword(state, die, "Ally"));

    // Rule 3.7.4/3.7.5 - "spin" a Character die's level, clamped to its
    // card's real level range ("if able" - spinning past either end is
    // just absorbed, not an error). Returns how many levels it actually
    // moved (0 if already at the clamped end, or if the die isn't
    // currently on a character face at all - Level isn't meaningful
    // otherwise), so callers that care whether a spin *really* moved the
    // die up (Awaken) can tell a no-op from a real spin without
    // re-deriving the before/after levels themselves.
    public static int SpinLevel(GameState state, DieInstance die, int delta)
    {
        if (die.Status is not (DieStatus.Character or DieStatus.SidekickCharacter)) return 0;
        var maxLevel = GetMaxLevel(state, die);
        var oldLevel = die.Level;
        die.Level = Math.Clamp(die.Level + delta, 1, maxLevel);
        return die.Level - oldLevel;
    }

    private static int GetMaxLevel(GameState state, DieInstance die) =>
        die.CardId is null ? 1 : Math.Max(1, state.CardCatalog[die.VirtualCardId ?? die.CardId].Levels.Count);

    public static readonly CharacterFace SidekickFace = new(FieldingCost: 0, Attack: 1, Defense: 1);

    // Rule 1.6.8 - a rolled Sidekick Character die is always level 1.
    public static CharacterFace GetFace(GameState state, DieInstance die)
    {
        if (die.CardId is null)
            return SidekickFace;

        var card = state.CardCatalog[die.VirtualCardId ?? die.CardId];
        var index = Math.Clamp(die.Level - 1, 0, Math.Max(0, card.Levels.Count - 1));
        return card.Levels.Count > 0 ? card.Levels[index] : SidekickFace;
    }

    // Rule 3.6.1/3.6.4 - combine all Applied/Static modifiers, clamp at zero.
    public static int EffectiveAttack(GameState state, DieInstance die)
    {
        var face = GetFace(state, die);
        var total = face.Attack + die.AppliedModifiers.Sum(m => m.AttackDelta);
        if (HasStrikeBonus(state, die)) total += 2;
        return Math.Max(0, total);
    }

    public static int EffectiveDefense(GameState state, DieInstance die)
    {
        var face = GetFace(state, die);
        var total = face.Defense + die.AppliedModifiers.Sum(m => m.DefenseDelta);
        if (HasStrikeBonus(state, die)) total += 2;
        return Math.Max(0, total);
    }

    // Rule 2.7.6.1 - KO once damage reaches/exceeds defense. Shared by
    // CombatEngine (simultaneous batch check after combat damage) and
    // EffectInterpreter (ability damage KOs immediately, since abilities
    // resolve one at a time rather than in a simultaneous batch - rule
    // 3.2.2). roller is optional (null in the handful of test call sites
    // that don't care about Regenerate) - without one, Regenerate simply
    // doesn't trigger and the die is KO'd normally.
    public static bool TryResolveKO(GameState state, DieInstance die, IDiceRoller? roller = null)
    {
        if (die.Damage < EffectiveDefense(state, die)) return false;

        return ForceKO(state, die, roller);
    }

    // Unconditionally KO's a die, bypassing the defense check
    // TryResolveKO does first - shared with direct ability-driven KO
    // effects (e.g. Casket of Ancient Winters' Ko node), which don't go
    // through a damage/defense comparison at all.
    //
    // Glossary - Regenerate: "If this character would be KO'd, roll it.
    // If you roll a character face, return it to the field on the rolled
    // face (but not the Attack Zone). Otherwise, move the die to your
    // Prep Area." That's an interception, not a KO that happens and then
    // gets undone - a die that regenerates was never actually KO'd, so
    // this doesn't set Zone/Prep Area at all in that case; the return value
    // (true = really KO'd, false = intercepted by Regenerate) is what lets
    // callers that care about an actual KO (a "when KO'd" trigger firing)
    // tell the difference, rather than just having called this method.
    //
    // Note this is a narrower question than "is this die still blocking" -
    // a die that Regenerates is alive (false here), but it also explicitly
    // does not return to the Attack Zone, so it stops blocking either way.
    // CombatEngine's Overcrush check is zone-based for exactly this reason
    // and treats a Regenerated blocker as removed, even though this method
    // reports it as not-KO'd.
    public static bool ForceKO(GameState state, DieInstance die, IDiceRoller? roller = null)
    {
        if (roller is not null && HasKeyword(state, die, "Regenerate"))
        {
            var cardId = die.VirtualCardId ?? die.CardId;
            var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
            var result = roller.Roll(die, card);
            if (result.Status is DieStatus.Character or DieStatus.SidekickCharacter)
            {
                die.Zone = Zone.FieldZone; // "back to the field... but not the Attack Zone"
                die.Status = result.Status;
                die.Level = result.Level;
                die.Damage = 0;
                die.EnergyKind = EnergyKind.None;
                die.ProvidedEnergyType = null;
                die.EnergyAmount = 1;
                die.AppliedModifiers.Clear(); // rule 3.4.5.4 - lifetime ends when it leaves the Field Zone (it never did here, but it's a fresh face)
                return false;
            }
            // Rolled a non-character face - falls through to a normal KO,
            // matching "otherwise, move the die to your Prep Area."
        }

        die.Zone = Zone.PrepArea; // rule 1.5.3.2
        die.ResetToUnrolled(); // also covers rule 3.4.5.4 - modifier lifetime ends when the die leaves the Field Zone
        return true;
    }
}
