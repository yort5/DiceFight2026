using System.Runtime.CompilerServices;
using DiceFight.Api;

namespace DiceFight.Api.Tests;

internal static class TestSetup
{
    // New games normally pick who goes first at random (2026-11-22); these
    // tests were written for player one (teamA) always starting, so that
    // stays the default here. A test of the coin flip sets its own store's
    // FirstPlayer.
    [ModuleInitializer]
    internal static void PlayerOneGoesFirst() => V2GameStore.DefaultFirstPlayer = (a, _) => a.Id;
}
