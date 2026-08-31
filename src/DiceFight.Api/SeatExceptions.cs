namespace DiceFight.Api;

/// <summary>No seat token, or one that holds no seat in this game (401).</summary>
public sealed class SeatRequiredException(string message) : Exception(message);

/// <summary>A real seat, but not the one entitled to take this action (403).</summary>
public sealed class NotYourTurnException(string message) : Exception(message);
