namespace DiceFight.V2.Model;

// A die as pure data: an ordered list of faces, any count (Appendix B -
// "any face count", the Direction-C requirement that dice aren't assumed
// to be six-sided or level-1-through-3).
public sealed record DieDefinition(string Id, IReadOnlyList<Face> Faces);

/// <summary>
/// What KIND of face this is. Declared, never inferred.
/// </summary>
/// <remarks>
/// Inference was tried and cannot work. "Character is null" makes every
/// Basic Action face an energy face - they carry neither symbols nor
/// character data - so SpinToEnergy would spin an action die onto an
/// action face and call it energy. "Symbols.Any()" fails the other way,
/// because a CHARACTER face may legitimately print energy symbols too.
/// No predicate over the data classifies a face; only the card does.
///
/// The engine understands these three because it has behaviour for each
/// (levels and fielding, energy payment, action-die use). A new kind in
/// a future game needs engine behaviour too, so this being an enum
/// rather than an open string is not the thing that would hold it back.
/// </remarks>
public enum FaceKind
{
    EnergyFace,
    CharacterFace,
    ActionFace,
}

// One face. Symbols is the set of energy pips shown - a character face
// can print symbols too, per the real game's rules, which is precisely
// why Kind is stated rather than derived from which fields are set.
public sealed record Face(
    IReadOnlyList<SymbolAmount> Symbols,
    CharacterFaceData? Character = null,
    int Burst = 0,
    FaceKind Kind = FaceKind.EnergyFace)
{
    /// <summary>
    /// The total energy pips this face shows. Rule 2.6.1.4's spin-down
    /// target is chosen by this, not by a per-die "default energy face":
    /// a half-spent double goes to the SINGLE face because one pip is
    /// what is left, and a Basic Action die has no single face at all
    /// (rule 2.6.1.5 - its energy faces are all doubles), which a stored
    /// default would have papered over with a wrong answer.
    /// </summary>
    public int SymbolCount => Symbols.Sum(s => s.Count);
}

public sealed record SymbolAmount(string SymbolId, int Count);

public sealed record CharacterFaceData(int Level, int FieldingCost, int Attack, int Defense);
