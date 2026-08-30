using DiceFight.Engine.Model;

namespace DiceFight.Engine;

// What is printed on each side of a die.
//
// This used to live inside PlaceholderDiceRoller, which meant the roller
// owned two unrelated jobs: choosing a random side, and knowing what was
// on it. That coupling is why a Crossover character could never roll the
// generic face it prints - the roller only knew how to emit one energy
// type, so faces it had no vocabulary for simply did not exist.
//
// Split apart, IDiceRoller answers only "which side came up" (an index),
// and this answers "what is on side N of this die". Two things follow:
// faces the roller could not express are now just table entries, and a
// die with more or fewer than six sides needs no roller change at all -
// only a longer or shorter list here.
//
// STILL NOT REAL DATA. No source of printed per-card face layouts exists,
// so the compositions below are the rulebook's shapes plus what the user
// has confirmed from real cards. What changed is that the uncertainty now
// sits in one obvious place, and a real face table would replace this
// file without touching the roll path.
public static class DieFaces
{
    // Rule 1.6.8 - a Sidekick die is one Level 1 character face and five
    // single-energy faces, one of them Wild. This one we know exactly.
    private static readonly RolledFace[] SidekickFaces =
    [
        new(DieStatus.SidekickCharacter, 1),
        new(DieStatus.Energy, 0, EnergyKind.Wild),
        new(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Fist),
        new(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Bolt),
        new(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Mask),
        new(DieStatus.Energy, 0, EnergyKind.Specific, EnergyType.Shield),
    ];

    // Three Action faces - blank, single burst, double burst - and three
    // double-Generic energy faces. Rule 1.3.10: "Basic Action dice provide
    // generic energy."
    private static readonly RolledFace[] BasicActionFaces =
    [
        new(DieStatus.Action, 0),
        new(DieStatus.Action, 0, BurstStars: 1),
        new(DieStatus.Action, 0, BurstStars: 2),
        new(DieStatus.Energy, 0, EnergyKind.Generic, EnergyAmount: 2),
        new(DieStatus.Energy, 0, EnergyKind.Generic, EnergyAmount: 2),
        new(DieStatus.Energy, 0, EnergyKind.Generic, EnergyAmount: 2),
    ];

    // The double-energy face of a card costing all four energy types.
    // Nothing derives these - each card just prints a pair - so they are
    // data, from the user. Every such card is its set's 121-124 slot.
    private static readonly Dictionary<string, (EnergyType First, EnergyType Second)> FourEnergyDoubles = new()
    {
        ["White Lantern Aquaman"] = (EnergyType.Fist, EnergyType.Shield),
        ["White Lantern Dove"] = (EnergyType.Mask, EnergyType.Shield),
        ["White Lantern Hal Jordan"] = (EnergyType.Bolt, EnergyType.Mask),
        ["White Lantern Superman"] = (EnergyType.Bolt, EnergyType.Fist),
        ["White Lantern Batman"] = (EnergyType.Bolt, EnergyType.Shield),
        ["White Lantern Deadman"] = (EnergyType.Bolt, EnergyType.Fist),
        ["White Lantern Sinestro"] = (EnergyType.Fist, EnergyType.Mask),
        ["White Lantern Wonder Woman"] = (EnergyType.Mask, EnergyType.Shield),
        ["Captain America with Mjolnir"] = (EnergyType.Bolt, EnergyType.Shield),
        ["Charles Xavier, Juggernaut"] = (EnergyType.Fist, EnergyType.Shield),
        ["Phoenix Force Magneto"] = (EnergyType.Bolt, EnergyType.Mask),
        ["Wolverine Lord of Vampires"] = (EnergyType.Fist, EnergyType.Mask),
        ["Captain Britain Iron Man"] = (EnergyType.Bolt, EnergyType.Mask),
        ["Groot Thor"] = (EnergyType.Bolt, EnergyType.Fist),
        ["King Black Bolt"] = (EnergyType.Fist, EnergyType.Shield),
        ["Punisher Sorcerer Supreme"] = (EnergyType.Mask, EnergyType.Shield),
        ["Blink In-Betweener"] = (EnergyType.Bolt, EnergyType.Mask),
        ["Cosmic X-23"] = (EnergyType.Mask, EnergyType.Shield),
        ["Czar Colossus"] = (EnergyType.Fist, EnergyType.Shield),
        ["Phoenix Storm"] = (EnergyType.Bolt, EnergyType.Fist),
    };

    /// <summary>
    /// Rolls one die: asks the roller which side came up, and reads what
    /// is on it. Every roll in the engine goes through here, so resolving
    /// the die's card (virtual cards included) is stated once.
    /// </summary>
    public static RolledFace Roll(GameState state, IDiceRoller roller, DieInstance die)
    {
        var cardId = die.VirtualCardId ?? die.CardId;
        var card = cardId is not null ? state.CardCatalog.GetValueOrDefault(cardId) : null;
        var faces = Of(die, card);
        return faces[Math.Clamp(roller.Roll(die, card, faces.Count), 0, faces.Count - 1)];
    }

    /// <summary>Every face of this die, in index order.</summary>
    public static IReadOnlyList<RolledFace> Of(DieInstance die, CardDef? card)
    {
        if (die.CardId is null && die.VirtualCardId is null) return SidekickFaces;
        if (card is { Type: CardType.BasicAction or CardType.EpicBasicAction }) return BasicActionFaces;

        var faces = new List<RolledFace>();

        // An Action card is an action die - three Action faces - but with
        // energy of its own type, unlike a Basic Action's Generic.
        if (card is { Type: CardType.Action })
        {
            faces.AddRange(BasicActionFaces.Take(3));
        }
        else
        {
            // A character face's index IS its level, which is what lets a
            // spin up or down be a quarter turn of the die. The count is
            // the card's own: Franklin's Galactus has four.
            var levels = card?.Levels.Count ?? 1;
            for (var level = 1; level <= Math.Min(Math.Max(levels, 1), FaceCount - 1); level++)
                faces.Add(new RolledFace(DieStatus.Character, level));
        }

        foreach (var face in EnergyFaces(card))
        {
            if (faces.Count >= FaceCount) break;
            faces.Add(face);
        }
        // A card with fewer than three levels has room for more than
        // three energy faces. Repeat the DOUBLE, not the single: a real
        // die does not carry three single-energy faces.
        var pad = faces.FirstOrDefault(f => f.Status == DieStatus.Energy && f.EnergyAmount > 1);
        while (faces.Count < FaceCount) faces.Add(pad == default ? faces[^1] : pad);
        return faces;
    }

    private const int FaceCount = 6;

    /// <summary>
    /// The three energy faces of a character or action die.
    ///
    /// One energy type: two doubles and a single, all of that type. Two or
    /// three - a Crossover - the doubles are SPLIT across both types and
    /// the single is Generic, per the Crossover glossary entry: "spin the
    /// die down to its single energy face (depicting either [generic] or a
    /// ?)". Four types: that single is Wild instead.
    /// </summary>
    private static IEnumerable<RolledFace> EnergyFaces(CardDef? card)
    {
        var types = card?.EnergyTypes ?? [];
        if (types.Count <= 1)
        {
            var type = types.Count == 1 ? types[0] : (EnergyType?)null;
            var kind = type is null ? EnergyKind.Generic : EnergyKind.Specific;
            yield return new RolledFace(DieStatus.Energy, 0, kind, type, EnergyAmount: 2);
            yield return new RolledFace(DieStatus.Energy, 0, kind, type, EnergyAmount: 2);
            yield return new RolledFace(DieStatus.Energy, 0, kind, type);
            yield break;
        }

        var (first, second) = types.Count >= 4 && card is not null && FourEnergyDoubles.TryGetValue(card.Name, out var pair)
            ? pair
            : (types[0], types[1]);

        // One of EACH type, not two of one - hence EnergyAmount 2 across
        // two types (see SecondProvidedEnergyType on RolledFace).
        yield return new RolledFace(DieStatus.Energy, 0, EnergyKind.Specific, first, EnergyAmount: 2, SecondProvidedEnergyType: second);
        yield return new RolledFace(DieStatus.Energy, 0, EnergyKind.Specific, first, EnergyAmount: 2, SecondProvidedEnergyType: second);
        yield return types.Count >= 4
            ? new RolledFace(DieStatus.Energy, 0, EnergyKind.Wild)
            : new RolledFace(DieStatus.Energy, 0, EnergyKind.Generic);
    }

    /// <summary>
    /// The single-energy face a double spins down to when only half of it
    /// is spent (rule 2.6.1.4). Null when the die has no single-energy
    /// face at all - a Basic Action die, whose energy faces are all
    /// doubles (rule 2.6.1.5).
    /// </summary>
    public static RolledFace? SingleEnergyFace(DieInstance die, CardDef? card)
    {
        // Not FirstOrDefault: RolledFace is a struct, so "no match" would
        // come back as a zeroed face rather than null.
        foreach (var face in Of(die, card))
            if (face.Status == DieStatus.Energy && face.EnergyAmount == 1) return face;
        return null;
    }
}
