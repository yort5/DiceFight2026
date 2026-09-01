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

    // Rule 1.4.3's generic energy. Named here rather than inlined so the
    // two places that produce it - a Basic Action die's faces and a
    // Crossover's single - are visibly the same thing.
    internal const string GenericSymbolId = "Generic";
    internal const string WildSymbolId = "Wild";

    internal static int LevelFace(int level) => EnergyFaceCount + level - 1;

    internal static DieDefinition Character(
        string dieId, string energySymbolId, params (int FieldingCost, int Attack, int Defense)[] levels) =>
        Character(dieId, [energySymbolId], [], levels);

    internal static DieDefinition Character(
        string dieId, IReadOnlyList<string> energySymbolIds,
        params (int FieldingCost, int Attack, int Defense)[] levels) =>
        Character(dieId, energySymbolIds, [], levels);

    // Burst-carrying overload - a character die's own face can print a
    // burst mark too (Gambit "Ace in the Hole" checks his OWN current
    // face's burst level), not just an action die's. The plain overloads
    // above default every level to Burst: 0, which is why nothing needed
    // this until a card's OWN character face burst actually mattered.
    // `bursts[i]` pairs with `levels[i]` by index; a short/omitted list
    // defaults the rest to 0.
    internal static DieDefinition Character(
        string dieId, string energySymbolId, IReadOnlyList<int> bursts, params (int FieldingCost, int Attack, int Defense)[] levels) =>
        Character(dieId, [energySymbolId], bursts, levels);

    internal static DieDefinition Character(
        string dieId, IReadOnlyList<string> energySymbolIds, IReadOnlyList<int> bursts,
        params (int FieldingCost, int Attack, int Defense)[] levels)
    {
        var faces = new List<Face>(EnergyFaces(energySymbolIds));
        faces.AddRange(levels.Select((l, i) =>
            new Face([], new CharacterFaceData(i + 1, l.FieldingCost, l.Attack, l.Defense), Burst: i < bursts.Count ? bursts[i] : 0, Kind: FaceKind.CharacterFace)));
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
            // A card with no printed energy type (a few Action cards) still
            // has energy faces; they are generic, the same as a Basic
            // Action die's.
            var single = symbolIds.Count == 1
                ? (IReadOnlyList<SymbolAmount>)[new SymbolAmount(symbolIds[0], 1)]
                : [new SymbolAmount(GenericSymbolId, 1)];
            var doubled = symbolIds.Count == 1
                ? (IReadOnlyList<SymbolAmount>)[new SymbolAmount(symbolIds[0], 2)]
                : [new SymbolAmount(GenericSymbolId, 2)];
            yield return new Face(doubled);
            yield return new Face(doubled);
            yield return new Face(single);
            yield break;
        }

        IReadOnlyList<SymbolAmount> crossover =
            [new SymbolAmount(symbolIds[0], 1), new SymbolAmount(symbolIds[1], 1)];
        yield return new Face(crossover);
        yield return new Face(crossover);
        // The Crossover glossary: "spin the die down to its single energy
        // face (depicting either [generic] or a ?)". Generic here; a
        // four-type card's is Wild, which v1's DieFaces.cs also records
        // and which needs the card's type count, not just its ids.
        yield return new Face([new SymbolAmount(
            symbolIds.Count >= 4 ? WildSymbolId : GenericSymbolId, 1)]);
    }

    // bursts: per-face burst values, for the handful of cards whose own
    // text branches on a burst symbol ("** Instead, ..."). Which faces
    // carry which burst is part of the same stated approximation above -
    // v1 records burst per ROLLED die, never per face, so a card needing
    // a double-burst branch gets one double-burst face rather than a
    // sourced distribution.
    /// <summary>
    /// A BASIC Action die - the shared subset. Rule 1.3.10: its energy
    /// faces provide generic energy, and 2.6.1.5 makes all three doubles,
    /// which is why such a die has no single face to spin down to.
    /// </summary>
    internal static DieDefinition BasicAction(string dieId, params int[] bursts) =>
        ActionDie(dieId, [
            new Face([new SymbolAmount(GenericSymbolId, 2)]),
            new Face([new SymbolAmount(GenericSymbolId, 2)]),
            new Face([new SymbolAmount(GenericSymbolId, 2)]),
        ], bursts);

    /// <summary>
    /// A non-basic Action die - one that takes a team slot. Unlike a Basic
    /// Action, it carries the CARD'S OWN printed energy, exactly as a
    /// character die does: Batarang shows one bolt and two double-bolts,
    /// and Cosmic Treadmill "Antique Shop Discovery" (GAF009), a fist/mask
    /// Crossover, shows one generic single and two fist/mask doubles.
    /// Same EnergyFaces used by Character, for that reason.
    /// </summary>
    internal static DieDefinition Action(string dieId, IReadOnlyList<string> energySymbolIds, params int[] bursts) =>
        ActionDie(dieId, EnergyFaces(energySymbolIds), bursts);

    private static DieDefinition ActionDie(string dieId, IEnumerable<Face> energyFaces, int[] bursts)
    {
        var pattern = bursts.Length > 0 ? bursts : [0, 0, 0];
        var faces = new List<Face>(energyFaces);
        faces.AddRange(pattern.Select(b => new Face([], Burst: b, Kind: FaceKind.ActionFace)));
        return new DieDefinition(dieId, faces);
    }
}
