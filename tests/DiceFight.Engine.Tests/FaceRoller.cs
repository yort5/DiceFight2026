using DiceFight.Engine;
using DiceFight.Engine.Model;

namespace DiceFight.Engine.Tests;

// A roller that lands on a chosen face of whatever die it is given.
//
// IDiceRoller only says which SIDE came up now (see DieFaces), so a test
// can no longer hand back an invented face - it has to name one the die
// actually has. That is the point of the split, and it is also what these
// helpers exist to keep readable: `FaceRoller.Character(2)` means "roll
// this die onto its level 2 face", and fails loudly if it has none.
public sealed class FaceRoller(Func<RolledFace, bool> wanted, string description) : IDiceRoller
{
    public int Roll(DieInstance die, CardDef? card, int faceCount)
    {
        var faces = DieFaces.Of(die, card);
        for (var i = 0; i < faces.Count; i++)
            if (wanted(faces[i])) return i;
        throw new InvalidOperationException(
            $"No {description} face on {card?.Name ?? die.CardId ?? "Sidekick"} - its faces are " +
            string.Join(", ", faces.Select(Describe)) + ".");
    }

    private static string Describe(RolledFace f) => f.Status switch
    {
        DieStatus.Energy => $"{f.EnergyAmount}x {f.EnergyKind}{(f.ProvidedEnergyType is { } t ? $" {t}" : "")}",
        DieStatus.Action => $"Action{(f.BurstStars is { } b ? $" ({b} burst)" : "")}",
        _ => $"{f.Status} L{f.Level}",
    };

    /// <summary>The character face of the given level.</summary>
    public static FaceRoller Character(int level) => new(
        f => f.Status is DieStatus.Character or DieStatus.SidekickCharacter && f.Level == level,
        $"level {level} character");

    /// <summary>Any character face - the lowest level the die has.</summary>
    public static FaceRoller AnyCharacter() => new(
        f => f.Status is DieStatus.Character or DieStatus.SidekickCharacter, "character");

    public static FaceRoller Energy(EnergyKind kind, EnergyType? type = null, int amount = 1) => new(
        f => f.Status == DieStatus.Energy && f.EnergyKind == kind
             && (type is null || f.ProvidedEnergyType == type) && f.EnergyAmount == amount,
        $"{amount}x {kind}{(type is { } t ? $" {t}" : "")}");

    public static FaceRoller AnyEnergy() => new(f => f.Status == DieStatus.Energy, "energy");

    public static FaceRoller Action(int? burstStars = null) => new(
        f => f.Status == DieStatus.Action && (burstStars is null || f.BurstStars == burstStars),
        "action");
}
