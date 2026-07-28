namespace DiceFight.Engine.Model;

// Rule 1.3 key - the physical Sidekick die face: fielding cost 0, 1A/1D.
// Sidekicks have no card (rule 1.3.9), so this isn't card-driven data.
public static class DieStats
{
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
    // 3.2.2). Regenerate/Overcrush and other keyword interactions are not
    // applied here yet (see RULES_ENGINE_DESIGN.md).
    public static bool TryResolveKO(GameState state, DieInstance die)
    {
        if (die.Damage < EffectiveDefense(state, die)) return false;

        die.Zone = Zone.PrepArea; // rule 1.5.3.2
        die.ResetToUnrolled(); // also covers rule 3.4.5.4 - modifier lifetime ends when the die leaves the Field Zone
        return true;
    }
}
