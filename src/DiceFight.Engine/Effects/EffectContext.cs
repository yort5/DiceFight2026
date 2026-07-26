using DiceFight.Engine.Model;

namespace DiceFight.Engine.Effects;

// Everything EffectInterpreter needs to execute one ability. ResolveTargets
// is the seam standing in for the not-yet-built "legal target" query system
// (see RULES_ENGINE_DESIGN.md open questions) - callers (tests, and
// eventually a real target-selection UI/AI) decide what a TargetSpec means
// and hand back concrete die ids; the interpreter only applies effects to
// whatever ids it's given.
public sealed record EffectContext(
    GameState State,
    string ControllerId,
    string? SourceDieId,
    Func<TargetSpec, IReadOnlyList<string>> ResolveTargets,
    Random? Random = null);
