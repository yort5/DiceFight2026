namespace DiceFight.V2.Model;

// A die as pure data: an ordered list of faces, any count (Appendix B -
// "any face count", the Direction-C requirement that dice aren't assumed
// to be six-sided or level-1-through-3).
public sealed record DieDefinition(string Id, IReadOnlyList<Face> Faces);

// One face. Character is null for an energy face; Symbols is the set of
// energy pips shown (a character face can print symbols too, per the real
// game's rules - a face is not exclusively "character" or "energy" in its
// data shape, only in whether Character is populated).
public sealed record Face(
    IReadOnlyList<SymbolAmount> Symbols,
    CharacterFaceData? Character = null,
    int Burst = 0);

public sealed record SymbolAmount(string SymbolId, int Count);

public sealed record CharacterFaceData(int Level, int FieldingCost, int Attack, int Defense);
