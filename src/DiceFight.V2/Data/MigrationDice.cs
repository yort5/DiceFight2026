using DiceFight.V2.Model;

namespace DiceFight.V2.Data;

// Shared die-construction helpers for every migrated card (Phase 8
// tasks 2/4). Real physical face LAYOUT is NOT recoverable from v1's
// data model - v1 never stored per-die faces at all; its
// PlaceholderDiceRoller synthesizes a face shape at roll time (see its
// own remarks). v2's DieDefinition needs real face data, so migration
// uses ONE documented convention, stated here once rather than
// re-flagged on every card:
//
//   Character die = 1 energy face (1 pip of the card's own printed
//   energy symbol) + 1 character face per v1 `Levels` entry, in level
//   order. So face index 0 is always energy; 1..n are levels 1..n.
//
//   Basic Action die = 3 identical action faces (rule 1.2.11 fixes
//   every Basic Action card at DieLimit 3), carrying no symbols and no
//   character data - `TargetKind.ActionDie` keys off CardType, not face
//   content (see TargetResolver.Query), so the faces need nothing.
//
// Both are stated APPROXIMATIONS of the real physical dice (which
// typically carry several energy faces, and action dice which carry
// burst symbols on only some faces), not sourced fact. Flagged here per
// Appendix C's "never guess a wrong approximation silently" rule.
internal static class MigrationDice
{
    internal static DieDefinition Character(string dieId, string energySymbolId, params (int FieldingCost, int Attack, int Defense)[] levels)
    {
        var faces = new List<Face> { new([new SymbolAmount(energySymbolId, 1)]) };
        faces.AddRange(levels.Select((l, i) => new Face([], new CharacterFaceData(i + 1, l.FieldingCost, l.Attack, l.Defense))));
        return new DieDefinition(dieId, faces);
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
        return new DieDefinition(dieId, [.. pattern.Select(b => new Face([], Burst: b))]);
    }
}
