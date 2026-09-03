using DiceFight.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Tests;

// v2 counterpart to SeatedController.cs - same "drive the controller class
// directly, not through a real HTTP host" reasoning.
internal static class V2SeatedController
{
    public static V2GamesController For(V2GameStore store, V2GameSession session, string playerId, string method = "POST")
    {
        var token = session.Seats.Single(s => s.PlayerId == playerId).Token;
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Headers["X-Seat-Token"] = token;
        return new V2GamesController(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    public static V2GamesController Anonymous(V2GameStore store)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        return new V2GamesController(store) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    public static V2GameStateDto Dto(ActionResult<V2GameStateDto> result) =>
        (V2GameStateDto)((OkObjectResult)result.Result!).Value!;

    public static V2CreatedGameDto CreatedDto(ActionResult<V2CreatedGameDto> result) =>
        (V2CreatedGameDto)((OkObjectResult)result.Result!).Value!;
}
