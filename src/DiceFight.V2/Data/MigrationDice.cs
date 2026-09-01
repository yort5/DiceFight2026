using DiceFight.V2.Model;

namespace DiceFight.V2.Data;

// Shared die-construction helpers for every migrated card (Phase 8
// tasks 2/4).
//
// These build the REAL six-face dice, not an approximation. The earlier
// convention here (one single-pip energy face plus one face per level)
// was flagged in its own remarks as a stated approximation, made because
// v1's data model did not record face layouts. It does now:
// `src/DiceFight.Engine/DieFaces.cs` states the composition exactly, and
// this mirrors it:
//
//   Character die = 3 ENERGY faces + 1 CHARACTER face per level.
//     One energy type  -> two doubles and a single, all of that type.
//     Two or three (a Crossover) -> the doubles carry ONE OF EACH type,
//       and the single is Generic (no symbol), per the Crossover
//       glossary: "spin the die down to its single energy face".
//     Four types -> that single is Wild instead.
//
//   Basic Action die = 3 ACTION faces + 3 double-energy faces. Rule
//     2.6.1.5: a Basic Action die's energy faces are all DOUBLES, which
//     is why it has no single face to spin down to (v1's
//     SingleEnergyFace returns null for exactly this) and why a stored
//     "default energy face" per die would have been the wrong model.
//
// Face ORDER is energy faces first, then character/action faces - so a
// character die's level N is at index 2 + N. Stated once here rather
// than assumed at each call site.
internal static class MigrationDice
{
    /// <summary>Index of a character die's level-N face.</summary>
    internal const int EnergyFaceCount = 3;

    internal static int LevelFace(int level) => EnergyFaceCount + level - 1;

    internal static DieDefinition Character(
        string dieId, string energySymbolId, params (int FieldingCost, int Attack, int Defense)[] levels) =>
        Character(dieId, [energySymbolId], levels);

    internal static DieDefinition Character(
        string dieId, IReadOnlyList<string> energySymbolIds,
        params (int FieldingCost, int Attack, int Defense)[] levels)
    {
        var faces = new List<Face>(EnergyFaces(energySymbolIds));
        faces.AddRange(levels.Select((l, i) =>
            new Face([], new CharacterFaceData(i + 1, l.FieldingCost, l.Attack, l.Defense), Kind: FaceKind.CharacterFace)));
        return new DieDefinition(dieId, faces);
    }

    // Two doubles and a single. A Crossover's doubles carry one pip of
    // EACH type rather than two of one (rule 2.6.2.3), and its single is
    // symbol-less: Generic, or Wild for a four-type card. Both come out
    // of the same shape here - a face with no symbols - because v2 has no
    // Generic/Wild symbol to name, and neither is spendable as a type.
    private static IEnumerable<Face> EnergyFaces(IReadOnlyList<string> symbolIds)
    {
        if (symbolIds.Count <= 1)
        {
            var single = symbolIds.Count == 1
                ? (IReadOnlyList<SymbolAmount>)[new SymbolAmount(symbolIds[0], 1)]
                : [];
            var doubled = symbolIds.Count == 1
                ? (IReadOnlyList<SymbolAmount>)[new SymbolAmount(symbolIds[0], 2)]
                : [];
            yield return new Face(doubled);
            yield return new Face(doubled);
            yield return new Face(single);
            yield break;
        }

        IReadOnlyList<SymbolAmount> crossover =
            [new SymbolAmount(symbolIds[0], 1), new SymbolAmount(symbolIds[1], 1)];
        yield return new Face(crossover);
        yield return new Face(crossover);
        yield return new Face([]);
    }

    // bursts: per-face burst values, for the handful of cards whose own
    // text branches on a burst symbol ("** Instead, ..."). Which faces
    // carry which burst is part of the same stated approximation above -
    // v1 records burst per ROLLED die, never per face, so a card needing
    // a double-burst branch gets one double-burst face rather than a
    // sourced distribution.
    internal static DieDefinition Action(string dieId, params int[] bursts)
    {
        var pattern = bursts.Length > 0 ? bursts : [0, 0, 0];
        // Rule 1.3.10 - a Basic Action die's energy faces provide generic
        // energy, and 2.6.1.5 makes them all doubles. Symbol-less because
        // generic is not a declared symbol type.
        var faces = new List<Face> { new([], Burst: 0), new([], Burst: 0), new([], Burst: 0) };
        faces.AddRange(pattern.Select(b => new Face([], Burst: b, Kind: FaceKind.ActionFace)));
        return new DieDefinition(dieId, faces);
    }
}
