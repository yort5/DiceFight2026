using DiceFight.Engine.Data;
using DiceFight.Engine.Model;
using Microsoft.AspNetCore.Mvc;

namespace DiceFight.Api.Controllers;

[ApiController]
[Route("api/cards")]
public sealed class CardsController : ControllerBase
{
    // Static catalog, safe to cache client-side - fetched once, referenced
    // by CardId from every DieDto rather than repeated per game.
    // CardType.Token (e.g. Master Mold's own Sentinel) is excluded - it's
    // a real GameState.CardCatalog entry so ability-created dice resolve
    // like any other Character die, but was never a real printed card a
    // team could be built from.
    private static readonly IReadOnlyList<CardDefDto> Catalog =
        SampleCards.BuildCatalog().Values.Where(c => c.Type != CardType.Token).Select(CardDefDto.From).ToList();

    [HttpGet]
    public ActionResult<IReadOnlyList<CardDefDto>> GetAll() => Ok(Catalog);
}
