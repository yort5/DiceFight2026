using DiceFight.Engine.Data;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Controllers;

[ApiController]
[Route("api/cards")]
public sealed class CardsController : ControllerBase
{
    // Static catalog, safe to cache client-side - fetched once, referenced
    // by CardId from every DieDto rather than repeated per game.
    private static readonly IReadOnlyList<CardDefDto> Catalog =
        SampleCards.BuildCatalog().Values.Select(CardDefDto.From).ToList();

    [HttpGet]
    public ActionResult<IReadOnlyList<CardDefDto>> GetAll() => Ok(Catalog);
}
