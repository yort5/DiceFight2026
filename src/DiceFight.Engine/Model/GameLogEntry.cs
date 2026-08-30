namespace DiceFight.Engine.Model;

/// <summary>
/// One line of "what just happened", for the match log.
/// </summary>
/// <param name="Seq">1-based, so the client can number lines without
/// having to trust list order after a partial update.</param>
/// <param name="PlayerId">Who did it, or null for something the game
/// itself did (a step boundary, a rules-driven consequence). The client
/// colours by side off this.</param>
/// <param name="Text">Already-written English, in the third person -
/// "Team A" rather than "You", because the same state is served to both
/// players and only the client knows which side is reading.</param>
public sealed record GameLogEntry(int Seq, string? PlayerId, string Text);
