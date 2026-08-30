using DiceFight.Engine;
using DiceFight.Engine.Model;

namespace DiceFight.Api;

// Rolling a die is now just picking one of its sides. What is ON each
// side is DieFaces' business, in the engine - which is why this class no
// longer has to know anything about energy types, bursts or levels, and
// why a die with other than six sides would need no change here at all.
//
// The uncertainty about real per-card face layouts has not gone away; it
// moved to DieFaces, where it belongs and where real data would land.
public sealed class RandomDiceRoller(Random random) : IDiceRoller
{
    public int Roll(DieInstance die, CardDef? card, int faceCount) => random.Next(faceCount);
}
