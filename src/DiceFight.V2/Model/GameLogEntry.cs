namespace DiceFight.V2.Model;

/// <summary>
/// One line of "what just happened", for the match log. Mirrors v1's
/// DiceFight.Engine.Model.GameLogEntry exactly - same shape, same reason
/// for each field (see there for the fuller rationale).
/// </summary>
/// <param name="Seq">1-based, so the client can number lines without
/// having to trust list order after a partial update.</param>
/// <param name="PlayerId">Who did it, or null for something the game
/// itself did. The client colours a line by side off this.</param>
/// <param name="Text">Already-written English, third person - the same
/// state is served to both players, only the client knows which side is
/// reading.</param>
/// <param name="IsTurnStart">True for the marker line that opens each turn,
/// so a client can draw a divider between turns.</param>
public sealed record GameLogEntry(int Seq, string? PlayerId, string Text, bool IsTurnStart = false);
