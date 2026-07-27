using DiceFight.Engine;
using DiceFight.Engine.Model;

namespace DiceFight.Api;

// A stand-in for real physical die face data, which doesn't exist yet
// (see RULES_ENGINE_DESIGN.md - none of the cloned reference repos have
// it). Rough guess at face-mix ratios rather than anything sourced from
// real cards; replace once real face-table data is available.
public sealed class PlaceholderDiceRoller(Random random) : IDiceRoller
{
    private static readonly EnergyType[] SpecificEnergyTypes =
        [EnergyType.Fist, EnergyType.Bolt, EnergyType.Mask, EnergyType.Shield];

    public RolledFace Roll(DieInstance die, CardDef? card)
    {
        if (card is { Type: CardType.BasicAction or CardType.EpicBasicAction })
        {
            // Rough guess: about half the faces are action faces, half energy.
            return random.Next(2) == 0
                ? new RolledFace(DieStatus.Energy, 0, EnergyKind.Generic)
                : new RolledFace(DieStatus.Action, 0);
        }

        if (die.CardId is null)
        {
            // A Sidekick die's six faces: one Level 1 character face, plus
            // five energy faces - Wild and one each of the four specific
            // types (Fist/Bolt/Mask/Shield) - not uniformly Wild.
            var face = random.Next(6);
            if (face == 0)
                return new RolledFace(DieStatus.SidekickCharacter, 1);

            return face == 1
                ? new RolledFace(DieStatus.Energy, 0, EnergyKind.Wild)
                : new RolledFace(DieStatus.Energy, 0, EnergyKind.Specific, SpecificEnergyTypes[face - 2]);
        }

        // Character dice: rough guess of roughly 1-in-3 energy faces
        // (providing the card's own energy type), otherwise a uniformly
        // random level among however many the card has.
        if (random.Next(3) == 0)
            return new RolledFace(DieStatus.Energy, 0, EnergyKind.Specific, card?.EnergyTypes.FirstOrDefault());

        var maxLevel = Math.Max(1, card?.Levels.Count ?? 1);
        var level = random.Next(1, maxLevel + 1);
        return new RolledFace(DieStatus.Character, level);
    }
}
