using DiceFight.Engine;

namespace DiceFight.Engine.Model;

// Rule 1.3 key - the physical Sidekick die face: fielding cost 0, 1A/1D.
// Sidekicks have no card (rule 1.3.9), so this isn't card-driven data.
public static class DieStats
{
    // Whether the die's card has the named keyword (Overcrush, Regenerate,
    // etc. - see CardDef.Keywords/KeywordInstance). Sidekicks (CardId
    // null) never have one.
    public static bool HasKeyword(GameState state, DieInstance die, string keyword)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        return cardId is not null
            && state.CardCatalog.TryGetValue(cardId, out var card)
            && card.Keywords.Any(k => k.Name == keyword);
    }

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
        return Math.Max(0, total);
    }

    public static int EffectiveDefense(GameState state, DieInstance die)
    {
        var face = GetFace(state, die);
        var total = face.Defense + die.AppliedModifiers.Sum(m => m.DefenseDelta);
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
    // this doesn't set Zone/Prep Area at all in that case, and callers
    // that treat "was this die actually removed" as a yes/no (Overcrush's
    // "if this attacker KO's all its blockers" check, "when KO'd"
    // triggers) see it as still alive precisely because it never left the
    // Field Zone.
    // Returns true if the die was actually KO'd, false if Regenerate
    // intercepted it back onto the field instead - callers that need to
    // know whether the die really left play (Overcrush's "all blockers
    // dead" check, "when KO'd" triggers) key off this, not just off having
    // called the method.
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
