using DiceFight.Engine;
using DiceFight.Engine.Model;

namespace DiceFight.Api;

// A stand-in for real physical die face data, which doesn't exist yet
// (see RULES_ENGINE_DESIGN.md - none of the cloned reference repos have
// it). Rough guess at face-mix ratios rather than anything sourced from
// real cards; replace once real face-table data is available.
public sealed class PlaceholderDiceRoller(Random random) : IDiceRoller
{
    public RolledFace Roll(DieInstance die, CardDef? card)
    {
        if (card is { Type: CardType.BasicAction or CardType.EpicBasicAction })
        {
            // Rough guess: about half the faces are action faces, half energy.
            return random.Next(2) == 0
                ? new RolledFace(DieStatus.Energy, 0)
                : new RolledFace(DieStatus.Action, 0);
        }

        // Character dice (including Sidekicks, card == null): rough guess
        // of roughly 1-in-3 energy faces, otherwise a uniformly random
        // level among however many the card has (Sidekicks are always
        // level 1 - rule 1.6.8).
        if (random.Next(3) == 0)
            return new RolledFace(DieStatus.Energy, 0);

        var maxLevel = die.CardId is null ? 1 : Math.Max(1, card?.Levels.Count ?? 1);
        var level = random.Next(1, maxLevel + 1);
        var status = die.CardId is null ? DieStatus.SidekickCharacter : DieStatus.Character;
        return new RolledFace(status, level);
    }
}
