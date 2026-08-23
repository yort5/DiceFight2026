using DiceFight.V2.Model;

namespace DiceFight.V2;

// Injectable roller (V2_PLAN.md Phase 2 task 5), following v1's
// IDiceRoller/PlaceholderDiceRoller PATTERN (an interface so tests can
// inject determinism) - not its face-derivation LOGIC. v1 had to
// procedurally guess a face shape from card type because no real per-die
// face table existed (PlaceholderDiceRoller's own remarks); v2's
// DieDefinition.Faces is real data from the start (Appendix B), so
// rolling is just "pick one of this die's own declared faces."
public interface IDiceRoller
{
    // Returns a face INDEX into die.Faces, not the Face itself, matching
    // DieInstance.CurrentFaceIndex's own storage shape.
    int Roll(DieDefinition die);
}

// Uniform-random over the die's own declared faces. Deterministic when
// constructed with a seeded Random - the "deterministic seeded roller for
// tests" the task asks for is just this class with a fixed seed, not a
// separate implementation.
public sealed class RandomDiceRoller(Random random) : IDiceRoller
{
    public int Roll(DieDefinition die)
    {
        if (die.Faces.Count == 0)
            throw new InvalidOperationException($"Die '{die.Id}' has no faces to roll.");
        return random.Next(die.Faces.Count);
    }
}
