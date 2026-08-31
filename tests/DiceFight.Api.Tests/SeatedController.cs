using DiceFight.Api;
using DiceFight.Api.Controllers;
using DiceFight.Engine;
using DiceFight.Engine.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Tests;

// The controller is the only place that knows about seats, whose turn it
// is, and the two-phase Range handshake - none of which the engine tests
// can see, because the engine has no idea there are two browsers.
//
// These drive the controller class directly rather than through a real
// HTTP host: everything under test lives in the action methods, and the
// only pieces of HttpContext any of it reads are the seat header and the
// request method. Spinning up Kestrel to supply two strings would buy
// coverage of ASP.NET's routing, not of this code.
internal static class SeatedController
{
    /// <summary>
    /// A controller that will act as <paramref name="playerId"/>, on a POST
    /// (the method the version counter keys off).
    /// </summary>
    public static GamesController For(GameStore store, GameSession session, string playerId, string method = "POST")
    {
        var token = session.Seats.Single(s => s.PlayerId == playerId).Token;
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Headers["X-Seat-Token"] = token;
        return new GamesController(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    /// <summary>A controller holding no seat at all - an uninvited caller.</summary>
    public static GamesController Anonymous(GameStore store)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        return new GamesController(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    /// <summary>The DTO out of an <c>ActionResult&lt;GameStateDto&gt;</c>.</summary>
    public static GameStateDto Dto(ActionResult<GameStateDto> result) =>
        (GameStateDto)((OkObjectResult)result.Result!).Value!;
}
