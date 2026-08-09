using DiceFight.Engine;
using DiceFight.Engine.Model;

namespace DiceFight.Api;

// A rules-accurate *default* face composition, not a real per-card face
// table - no source for exact printed face layouts exists yet (see
// RULES_ENGINE_DESIGN.md), but the shape of each die type below (which
// faces exist and in what mix) is taken from the rulebook and a couple of
// reference card photos, not guessed. Replace with real per-card data if
// that's ever sourced; until then this is what a card of a given type
// "generally" rolls like, per the rulebook's own wording.
public sealed class PlaceholderDiceRoller(Random random) : IDiceRoller
{
    private static readonly EnergyType[] SpecificEnergyTypes =
        [EnergyType.Fist, EnergyType.Bolt, EnergyType.Mask, EnergyType.Shield];

    public RolledFace Roll(DieInstance die, CardDef? card)
    {
        if (card is { Type: CardType.BasicAction or CardType.EpicBasicAction })
        {
            // Rulebook + the reference "Front Line" card: a Basic Action
            // die is 3 double-Generic energy faces and 3 Action faces -
            // "a die with a double generic energy face (such as a Basic
            // Action Die)" is the rulebook's own canonical example of a
            // Double. The 3 Action faces are themselves blank/single-/
            // double-burst (see DieInstance.BurstStars's own remarks and
            // the dicefight2026-stats-spreadsheet memory's own note on
            // what the stat line's "*"/"**" burst marks mean) - evenly
            // split, same "no real per-card face table sourced yet"
            // caveat as everything else in this class.
            if (random.Next(2) == 0)
                return new RolledFace(DieStatus.Energy, 0, EnergyKind.Generic, EnergyAmount: 2);

            int? burstStars = random.Next(3) switch { 1 => 1, 2 => 2, _ => null };
            return new RolledFace(DieStatus.Action, 0, BurstStars: burstStars);
        }

        if (die.CardId is null)
        {
            // A Sidekick die's six faces: one Level 1 character face, plus
            // five single-value energy faces - Wild and one each of the
            // four specific types (Fist/Bolt/Mask/Shield). No doubles on
            // Sidekicks (confirmed against a reference photo of real
            // Sidekick dice).
            var face = random.Next(6);
            if (face == 0)
                return new RolledFace(DieStatus.SidekickCharacter, 1);

            return face == 1
                ? new RolledFace(DieStatus.Energy, 0, EnergyKind.Wild)
                : new RolledFace(DieStatus.Energy, 0, EnergyKind.Specific, SpecificEnergyTypes[face - 2]);
        }

        // A Character die (per user-confirmed reference and the
        // rulebook's "all dice have some sides that produce energy"):
        // usually 3 character-level faces and 3 energy faces of the
        // card's own purchase energy type, with 2 of those 3 as doubles
        // and 1 single (e.g. a Fist character: two double-Fist faces, one
        // single-Fist face). Known exceptions - a double split across two
        // types, or a double Wild/Generic - are rare and not modeled here
        // without real per-card face data; this is the common case.
        var maxLevel = Math.Max(1, card?.Levels.Count ?? 1);
        var energyType = card?.EnergyTypes.FirstOrDefault() ?? EnergyType.Fist; // shouldn't happen for a real Character card
        var roll = random.Next(6);
        return roll switch
        {
            0 or 1 or 2 => new RolledFace(DieStatus.Character, Math.Min(roll + 1, maxLevel)),
            3 or 4 => new RolledFace(DieStatus.Energy, 0, EnergyKind.Specific, energyType, EnergyAmount: 2),
            _ => new RolledFace(DieStatus.Energy, 0, EnergyKind.Specific, energyType),
        };
    }
}
