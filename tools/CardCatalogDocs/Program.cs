using DiceFight.V2;
using System.Net;
using System.Text;
using DiceFight.V2.Data;
using DiceFight.V2.Model;

// Renders the real DiceKingdomConfig (Champions, CharactersByChampion,
// ActionByChampion, Catalog, each Champion's Tardigrade pool) as one
// static HTML reference page - every die face (energy faces too, since
// 2026-09-30), every
// stat and every ability's RawText comes straight from the live CardDef
// records, not a hand-transcribed copy, so it can't silently drift from
// what a game actually plays like the way v3/CARD_INSPIRATION.md already
// has (that file is the pre-implementation brainstorm pass, explicitly
// "not decisions" - real stats moved on without it, several picks even
// changed animal). Re-run this after any card/champion edit:
//
//   dotnet run --project tools/CardCatalogDocs [output-path]
//
// output-path defaults to tools/CardCatalogDocs/dice-kingdom-cards.html;
// a GitHub-readable .md copy is written beside it, and the default run
// also publishes the HTML to the site as web/public/dice-kingdom/cards.html.

var outPath = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(FindRepoRoot(), "tools", "CardCatalogDocs", "dice-kingdom-cards.html");

// Walks up from wherever the build output landed (AppContext.BaseDirectory
// is .../tools/CardCatalogDocs/bin/Debug/net10.0/, nowhere useful to
// default INTO) looking for the solution file, so the default output path
// is stable regardless of the working directory `dotnet run` was invoked
// from.
static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DiceFight.slnx")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

var config = DiceKingdomConfig.Config;
const string KeywordSource = "https://wizkids.com/dicemasters/keywords/";
var catalog = DiceKingdomConfig.Catalog;

// Source printings for the imports, by id (the Dice Kingdom copy has its animal's name).
var ImportSources = new[] { CardCatalog.BuildCatalog().Values.AsEnumerable(), DpsCards.All, BonusCards.All, KingdomImports.All }
    .SelectMany(cards => cards).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
var NativeSources = new Dictionary<string, string>
{
    ["DK-CLAW-01"] = "Storm, \"Extreme Weather\" (DPS052)",
    ["DK-CLAW-02"] = "Deadpool, \"More Than a Chump Blocker\" (DPS068)",
    ["DK-CLAW-04"] = "Corsair, \"Criminal Record\" (DPS104)",
    ["DK-CLAW-05"] = "Phoenix, \"Firepower\" (DPS046)",
    ["DK-CLAW-07"] = "Jubilee, \"X-Men Field Leader\" (DPS143)",
    ["DK-CLAW-09"] = "Toad, \"Secondary Mutation\" (DPS054)",
    ["DK-SHELL-02"] = "Kitty Pryde, \"Experienced Leader\" (DPS144)",
    ["DK-SHELL-06"] = "Emma Frost, \"Influential\" (DPS030)",
    ["DK-WING-01"] = "D'Ken, \"Emperor\" (DPS026)",
    ["DK-WING-02"] = "Kitty Pryde, \"Right of Passage\" (DPS037)",
    ["DK-WING-04"] = "Beast, \"First Class\" (DPS058)",
    ["DK-WING-05"] = "Psylocke, \"Heiress\" (DPS128)",
    ["DK-WING-09"] = "Beast, \"Combat Ready\" (DPS098)",
    ["DK-EYE-01"] = "Storm, \"Cloud Cover\" (DPS092)",
    ["DK-EYE-02"] = "Sabretooth, \"Do I Smell Weakness?\" (DPS091)",
    ["DK-EYE-03"] = "Phoenix, \"Eternal Flame\" (DPS126)",
    ["DK-EYE-04"] = "Toad, \"Journey Into Misery\" (DPS134)",
    ["DK-EYE-05"] = "Gambit, \"Ace in the Hole\" (DPS032)",
    ["DK-EYE-06"] = "Colossus, \"Piotr\" (DPS103)",
    ["DK-EYE-09"] = "Iceman, \"Icy Interference\" (DPS034)",
    // Basic Actions keep their Dice Masters names.
    ["DK-ACT-01"] = "Anger Issues (Basic Action, IG001)",
    ["DK-ACT-02"] = "Distraction (Basic Action, AVX025)",
    ["DK-ACT-03"] = "Resurrection (Basic Action, BFF137)",
    ["DK-ACT-04"] = "Mutation (Basic Action, DPS009)",
    ["DK-ACT-05"] = "Archnemesis (Basic Action, DPS001)",
};

var html = new StringBuilder();
html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Dice Kingdom - Card Reference</title>");
html.AppendLine("""
<style>
  body { font: 15px/1.5 -apple-system, Segoe UI, sans-serif; max-width: 980px; margin: 2rem auto; padding: 0 1rem; color: #1a1a1a; }
  h1 { margin-bottom: 0.2rem; }
  .generated { color: #666; font-size: 0.9em; margin-bottom: 2rem; }
  h2 { border-bottom: 2px solid #333; padding-bottom: 0.3rem; margin-top: 2.5rem; }
  .passive { font-style: italic; color: #444; margin-top: -0.3rem; }
  table { border-collapse: collapse; width: 100%; margin: 1rem 0 2rem; }
  th, td { border: 1px solid #ccc; padding: 0.5rem 0.6rem; text-align: left; vertical-align: top; }
  th { background: #f0f0f0; }
  tr:nth-child(even) { background: #fafafa; }
  .stats { font-family: ui-monospace, Menlo, monospace; font-size: 0.9em; white-space: nowrap; }
  .keyword { display: inline-block; background: #e8e8f8; border-radius: 4px; padding: 0 0.4em; margin: 1px; font-size: 0.85em; }
  .cost { text-align: center; font-weight: 600; }
  h3 { margin: 1.4rem 0 0.2rem; font-size: 1.05em; }
  .note { color: #555; font-size: 0.9em; margin: 0.2rem 0 0; }
  .label { color: #666; font-size: 0.8em; font-style: italic; white-space: nowrap; }
  .benched { color: #888; }
</style>
""");
html.AppendLine("</head><body>");
html.AppendLine("<h1>Dice Kingdom &mdash; Card Reference</h1>");
html.AppendLine($"<p class=\"generated\">Generated {DateTime.UtcNow:yyyy-MM-dd} from <code>src/DiceFight.V2/Data/DiceKingdomConfig.cs</code> " +
    "by <code>tools/CardCatalogDocs</code> &mdash; every field below is read straight from the live CardDef/ChampionDef records, " +
    "not hand-copied. Re-run the tool after any card change; don't hand-edit this file.</p>");

html.AppendLine("<p class=\"note\">Dice Masters source: the printing a card was imported from, or the card an original animal was first adapted from; Original means it was designed for Dice Kingdom (or reworked past recognition).</p>");
html.AppendLine("<p class=\"note\">Jump to: " + string.Join(" · ", config.Champions.Select(c => $"<a href=\"#{c.Id}\">{Enc(c.Name)}</a>")) +
    " · <a href=\"#keywords\">Keywords</a></p>");

foreach (var champion in config.Champions)
{
    html.AppendLine($"<h2 id=\"{champion.Id}\">{Enc(champion.Name)} <small>({Enc(champion.EnergySymbolId)})</small></h2>");
    html.AppendLine($"<p class=\"passive\">Power: {Enc(DescribePassive(champion))}</p>");

    // The starting dice: not cards, so they only live on the ChampionDef.
    foreach (var pool in champion.TardigradePool)
    {
        html.AppendLine($"<h3>Tardigrade die &times;{pool.Count} <small>(starts in the Bag; free to field)</small></h3>");
        html.AppendLine("<table><thead><tr><th>Face</th><th>Count</th></tr></thead><tbody>");
        foreach (var (face, count) in GroupFaces(pool.Die))
            html.AppendLine($"<tr><td class=\"stats\">{face}</td><td class=\"cost\">{count}</td></tr>");
        html.AppendLine("</tbody></table>");
    }

    html.AppendLine("<h3>Characters</h3>");
    html.AppendLine("<table><thead><tr><th>Name</th><th>Cost</th><th>Die&nbsp;Limit</th><th>Keywords</th><th>Stats (fielding&nbsp;/&nbsp;ATK&nbsp;/&nbsp;DEF per level)</th><th>Energy faces</th><th>Ability text</th><th>Dice Masters source</th></tr></thead><tbody>");
    foreach (var cardId in DiceKingdomConfig.CharactersByChampion[champion.Id])
        AppendCardRow(html, catalog[cardId]);
    html.AppendLine("</tbody></table>");
}

// Characters in the Catalog but on no Champion's pack (2026-10-03) -
// retired picks and new keyword animals waiting for a fifth Champion.
// They were invisible here before, so a card could exist with nobody
// able to see it short of reading the config.
var benched = catalog.Values
    .Where(c => c.CardType == CardType.Character && !DiceKingdomConfig.CharactersByChampion.Values.Any(team => team.Contains(c.Id)))
    .OrderBy(c => c.EnergySymbolIds.FirstOrDefault()).ThenBy(c => c.PurchaseCost).ThenBy(c => c.Name)
    .ToList();
if (benched.Count > 0)
{
    html.AppendLine("<h2>Not on a team yet</h2>");
    html.AppendLine("<p class=\"note\">In the card pool but in no Champion's pack, so they don't come up in games.</p>");
    html.AppendLine("<table><thead><tr><th>Name</th><th>Cost</th><th>Die&nbsp;Limit</th><th>Keywords</th><th>Stats (fielding&nbsp;/&nbsp;ATK&nbsp;/&nbsp;DEF per level)</th><th>Energy faces</th><th>Ability text</th><th>Dice Masters source</th></tr></thead><tbody>");
    foreach (var card in benched) AppendCardRow(html, card);
    html.AppendLine("</tbody></table>");
}

html.AppendLine("<h2>Champion Basic Actions <small>(Global abilities, one per Champion)</small></h2>");
html.AppendLine("<table><thead><tr><th>Champion</th><th>Name</th><th>Cost</th><th>Die&nbsp;Limit</th><th>Die faces</th><th>Ability text</th><th>Dice Masters source</th></tr></thead><tbody>");
foreach (var (championId, cardId) in DiceKingdomConfig.ActionByChampion)
{
    var card = catalog[cardId];
    html.AppendLine("<tr>");
    html.AppendLine($"<td>{Enc(config.Champions.First(c => c.Id == championId).Name)}</td>");
    html.AppendLine($"<td>{Enc(card.Name)}</td>");
    html.AppendLine($"<td class=\"cost\">{card.PurchaseCost}</td>");
    html.AppendLine($"<td class=\"cost\">{card.DieLimit}</td>");
    html.AppendLine($"<td class=\"stats\">{string.Join("<br>", GroupFaces(card.Die).Select(g => g.Count > 1 ? $"{g.Face} &times;{g.Count}" : g.Face))}</td>");
    html.AppendLine($"<td>{Enc(card.RawText)}</td>");
    html.AppendLine($"<td class=\"source\">{Enc(Source(card))}</td>");
    html.AppendLine("</tr>");
}
html.AppendLine("</tbody></table>");
html.AppendLine("<p class=\"note\">Either player may buy either Basic Action; each die's energy faces show its Champion's type.</p>");

// Basic Actions no Champion brings (2026-10-20) - a custom team can take
// one whose energy matches its Champion.
var otherActions = catalog.Values
    .Where(c => c.CardType == CardType.BasicAction && !DiceKingdomConfig.ActionByChampion.Values.Contains(c.Id))
    .OrderBy(ActionEnergy).ThenBy(c => c.PurchaseCost).ThenBy(c => c.Name).ToList();
html.AppendLine("<h2>Other Basic Actions</h2>");
html.AppendLine("<p class=\"note\">For custom teams: with a Champion, the Basic Action must share its energy.</p>");
html.AppendLine("<table><thead><tr><th>Energy</th><th>Name</th><th>Cost</th><th>Die&nbsp;Limit</th><th>Die faces</th><th>Ability text</th><th>Dice Masters source</th></tr></thead><tbody>");
foreach (var card in otherActions)
{
    html.AppendLine("<tr>");
    html.AppendLine($"<td>{Enc(ActionEnergy(card))}</td>");
    html.AppendLine($"<td>{Enc(card.Name)}</td>");
    html.AppendLine($"<td class=\"cost\">{card.PurchaseCost}</td>");
    html.AppendLine($"<td class=\"cost\">{card.DieLimit}</td>");
    html.AppendLine($"<td class=\"stats\">{string.Join("<br>", GroupFaces(card.Die).Select(g => g.Count > 1 ? $"{g.Face} &times;{g.Count}" : g.Face))}</td>");
    html.AppendLine($"<td>{Enc(card.RawText)}</td>");
    html.AppendLine($"<td class=\"source\">{Enc(Source(card))}</td>");
    html.AppendLine("</tr>");
}
html.AppendLine("</tbody></table>");

// Keywords (2026-10-10, user: "where does the list of currently
// implemented keywords live... could we make a handy reference") - the
// declared list (DiceKingdomConfig.Keywords) with its rules text, and
// which cards carry each one.
html.AppendLine("<h2 id=\"keywords\">Keywords</h2>");
html.AppendLine("<p class=\"note\">Every keyword the engine accepts on a Dice Kingdom card, from <code>DiceKingdomConfig.Keywords</code>. " +
    "<span class=\"label\">label</span> marks a keyword that only names a timing or cost - each card writes its own ability; " +
    "for the rest, listing the keyword is all a card needs. Grey cards are in no Champion's pack. " +
    $"Dice Masters wording from <a href=\"{KeywordSource}\">WizKids' keyword list</a>, copied once on 2026-10-10.</p>");
html.AppendLine("<table><thead><tr><th>Keyword</th><th>Dice Masters rules</th><th>In Dice Kingdom</th><th>Cards</th></tr></thead><tbody>");
foreach (var keyword in config.Keywords)
{
    var label = keyword.WrittenOnCard ? " <span class=\"label\">label</span>" : "";
    var cards = KeywordCards(keyword.Id).Select(c => c.Benched ? $"<span class=\"benched\">{Enc(c.Name)}</span>" : Enc(c.Name));
    var original = keyword.Original is { } o ? Enc(o) : "<em>not a Dice Masters keyword</em>";
    html.AppendLine($"<tr><td><b>{Enc(keyword.Id)}</b>{label}</td><td>{original}</td><td>{Enc(InDiceKingdom(keyword))}</td>" +
        $"<td>{(cards.Any() ? string.Join(", ", cards) : "<em>none yet</em>")}</td></tr>");
}
html.AppendLine("</tbody></table>");

html.AppendLine("</body></html>");

Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
File.WriteAllText(outPath, html.ToString());
Console.WriteLine($"Wrote {catalog.Count} cards across {config.Champions.Count} Champions to {outPath}");

// The same reference as Markdown beside the HTML, so it reads on GitHub
// (which shows .html files as source). Added 2026-09-30.
var mdPath = Path.ChangeExtension(outPath, ".md");
File.WriteAllText(mdPath, BuildMarkdown());
Console.WriteLine($"Wrote {mdPath}");

// And the HTML onto the live site at /dice-kingdom/cards.html (web/public
// is copied into the build as-is). Only for the default run - an explicit
// output-path is a one-off.
if (args.Length == 0)
{
    var sitePath = Path.Combine(FindRepoRoot(), "web", "public", "dice-kingdom", "cards.html");
    Directory.CreateDirectory(Path.GetDirectoryName(sitePath)!);
    File.WriteAllText(sitePath, html.ToString());
    Console.WriteLine($"Wrote {sitePath}");
}

string BuildMarkdown()
{
    var md = new StringBuilder();
    md.AppendLine("# Dice Kingdom — Card Reference");
    md.AppendLine();
    md.AppendLine($"_Generated {DateTime.UtcNow:yyyy-MM-dd} from `src/DiceFight.V2/Data/DiceKingdomConfig.cs` by `tools/CardCatalogDocs` — " +
        "every field is read straight from the live CardDef/ChampionDef records. Re-run the tool after any card change; don't hand-edit this file._");
    md.AppendLine();
    md.AppendLine("Dice Masters source: the printing a card was imported from, or the card an original animal was first adapted from; Original means it was designed for Dice Kingdom (or reworked past recognition).");
    foreach (var champion in config.Champions)
    {
        md.AppendLine();
        md.AppendLine($"## {champion.Name} ({champion.EnergySymbolId})");
        md.AppendLine();
        md.AppendLine($"_Power: {Md(DescribePassive(champion))}_");
        foreach (var pool in champion.TardigradePool)
        {
            md.AppendLine();
            md.AppendLine($"### Tardigrade die ×{pool.Count} (starts in the Bag; free to field)");
            md.AppendLine();
            md.AppendLine("| Face | Count |");
            md.AppendLine("|---|:-:|");
            foreach (var (face, count) in GroupFaces(pool.Die))
                md.AppendLine($"| {face} | {count} |");
        }
        md.AppendLine();
        md.AppendLine("### Characters");
        md.AppendLine();
        md.AppendLine("| Name | Cost | Die Limit | Keywords | Stats (fielding / ATK / DEF per level) | Energy faces | Ability text | Dice Masters source |");
        md.AppendLine("|---|:-:|:-:|---|---|---|---|---|");
        foreach (var cardId in DiceKingdomConfig.CharactersByChampion[champion.Id])
            AppendMdRow(md, catalog[cardId]);
    }
    if (benched.Count > 0)
    {
        md.AppendLine();
        md.AppendLine("## Not on a team yet");
        md.AppendLine();
        md.AppendLine("In the card pool but in no Champion's pack, so they don't come up in games.");
        md.AppendLine();
        md.AppendLine("| Name | Cost | Die Limit | Keywords | Stats (fielding / ATK / DEF per level) | Energy faces | Ability text | Dice Masters source |");
        md.AppendLine("|---|:-:|:-:|---|---|---|---|---|");
        foreach (var card in benched) AppendMdRow(md, card);
    }
    md.AppendLine();
    md.AppendLine("## Champion Basic Actions (Global abilities, one per Champion)");
    md.AppendLine();
    md.AppendLine("| Champion | Name | Cost | Die Limit | Die faces | Ability text | Dice Masters source |");
    md.AppendLine("|---|---|:-:|:-:|---|---|---|");
    foreach (var (championId, cardId) in DiceKingdomConfig.ActionByChampion)
    {
        var card = catalog[cardId];
        var faces = string.Join("<br>", GroupFaces(card.Die).Select(g => g.Count > 1 ? $"{g.Face} ×{g.Count}" : g.Face));
        md.AppendLine($"| {Md(config.Champions.First(c => c.Id == championId).Name)} | {Md(card.Name)} | {card.PurchaseCost} | {card.DieLimit} | {faces} | {Md(card.RawText)} | {Md(Source(card))} |");
    }
    md.AppendLine();
    md.AppendLine("Either player may buy either Basic Action; each die's energy faces show its Champion's type.");
    md.AppendLine();
    md.AppendLine("## Other Basic Actions");
    md.AppendLine();
    md.AppendLine("For custom teams: with a Champion, the Basic Action must share its energy.");
    md.AppendLine();
    md.AppendLine("| Energy | Name | Cost | Die Limit | Die faces | Ability text | Dice Masters source |");
    md.AppendLine("|---|---|:-:|:-:|---|---|---|");
    foreach (var card in otherActions)
    {
        var faces = string.Join("<br>", GroupFaces(card.Die).Select(g => g.Count > 1 ? $"{g.Face} ×{g.Count}" : g.Face));
        md.AppendLine($"| {ActionEnergy(card)} | {Md(card.Name)} | {card.PurchaseCost} | {card.DieLimit} | {faces} | {Md(card.RawText)} | {Md(Source(card))} |");
    }
    md.AppendLine();
    md.AppendLine("## Keywords");
    md.AppendLine();
    md.AppendLine("Every keyword the engine accepts on a Dice Kingdom card, from `DiceKingdomConfig.Keywords`. _(label)_ marks a keyword that " +
        "only names a timing or cost - each card writes its own ability; for the rest, listing the keyword is all a card needs. " +
        $"Cards in _italics_ are in no Champion's pack. Dice Masters wording from [WizKids' keyword list]({KeywordSource}), copied once on 2026-10-10.");
    md.AppendLine();
    md.AppendLine("| Keyword | Dice Masters rules | In Dice Kingdom | Cards |");
    md.AppendLine("|---|---|---|---|");
    foreach (var keyword in config.Keywords)
    {
        var cards = KeywordCards(keyword.Id).Select(c => c.Benched ? $"_{Md(c.Name)}_" : Md(c.Name)).ToList();
        md.AppendLine($"| **{Md(keyword.Id)}**{(keyword.WrittenOnCard ? " _(label)_" : "")} | {(keyword.Original is { } o ? Md(o) : "_not a Dice Masters keyword_")} | " +
            $"{Md(InDiceKingdom(keyword))} | {(cards.Count > 0 ? string.Join(", ", cards) : "_none yet_")} |");
    }
    return md.ToString();
}

// The "In Dice Kingdom" column: how this game differs, or "As written."
static string InDiceKingdom(KeywordDef k) => k.Description ?? "As written.";

// Cards carrying a keyword (its X form too - "Range 2" for Range), by name;
// Benched = in no Champion's pack.
List<(string Name, bool Benched)> KeywordCards(string keywordId) =>
    catalog.Values
        .Select(c => (Card: c, Shown: c.Keywords.FirstOrDefault(k => KeywordAbilities.ParamOf(k, keywordId) is not null)))
        .Where(x => x.Shown is not null)
        .Select(x => (Name: x.Shown == keywordId ? x.Card.Name : $"{x.Card.Name} ({x.Shown})",
            Benched: !DiceKingdomConfig.CharactersByChampion.Values.Any(team => team.Contains(x.Card.Id))
                && !DiceKingdomConfig.ActionByChampion.Values.Contains(x.Card.Id)))
        .OrderBy(x => x.Benched).ThenBy(x => x.Name)
        .ToList();

// Text for a Markdown table cell: pipes escaped, line breaks as <br>.
static string Md(string s) => s.Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");

void AppendMdRow(StringBuilder md, CardDef card)
{
    var name = card.Subtitle is { } sub ? $"{Md(card.Name)} _({Md(sub)})_" : Md(card.Name);
    var energy = string.Join("<br>", card.Die.Faces.Where(f => f.Kind == FaceKind.EnergyFace).Select(DescribeEnergy));
    md.AppendLine($"| {name} | {card.PurchaseCost} | {card.DieLimit} | {string.Join(", ", card.Keywords.Select(Md))} | {DescribeLevels(card)} | {energy} | {Md(card.RawText)} | {Md(Source(card))} |");
}

void AppendCardRow(StringBuilder sb, CardDef card)
{
    sb.AppendLine("<tr>");
    var displayName = card.Subtitle is { } sub ? $"{card.Name} <i>({Enc(sub)})</i>" : Enc(card.Name);
    sb.AppendLine($"<td>{displayName}</td>");
    sb.AppendLine($"<td class=\"cost\">{card.PurchaseCost}</td>");
    sb.AppendLine($"<td class=\"cost\">{card.DieLimit}</td>");
    sb.AppendLine($"<td>{string.Join(" ", card.Keywords.Select(k => $"<span class=\"keyword\">{Enc(k)}</span>"))}</td>");
    sb.AppendLine($"<td class=\"stats\">{DescribeLevels(card)}</td>");
    sb.AppendLine($"<td class=\"stats\">{string.Join("<br>", card.Die.Faces.Where(f => f.Kind == FaceKind.EnergyFace).Select(DescribeEnergy))}</td>");
    sb.AppendLine($"<td>{Enc(card.RawText)}</td>");
    sb.AppendLine($"<td class=\"source\">{Enc(Source(card))}</td>");
    sb.AppendLine("</tr>");
}

// "Dice Masters source" (2026-10-20, user: "include the Dice Masters card
// that inspired it, if there was one"). An import keeps its printing's id,
// so its source is exact. A native animal was reskinned from a Dice Masters
// card in v3/CARD_INSPIRATION.md, but many were reworked since - only the
// ones whose ability still plainly comes from that card are listed below;
// the rest read "Original".
string Source(CardDef card)
{
    if (ImportSources.TryGetValue(card.Id, out var source))
        return source.Subtitle is { Length: > 0 } sub && source.CardType == CardType.Character
            ? $"{source.Name}, \"{sub}\" ({source.Id})"
            : $"{source.Name} ({source.Id})";
    return NativeSources.GetValueOrDefault(card.Id, "Original");
}

// A Basic Action's energy: the first energy printed on its die (V2GamesController.BasicActionEnergy).
static string ActionEnergy(CardDef card) => card.Die.Faces.SelectMany(f => f.Symbols).Select(sym => sym.SymbolId).FirstOrDefault() ?? "";

static string DescribeLevels(CardDef card)
{
    var levels = card.Die.Faces
        .Where(f => f.Character is not null)
        .Select(f => f.Character!)
        .OrderBy(c => c.Level)
        .ToList();
    if (levels.Count == 0) return "<em>no character face</em>";
    return string.Join("<br>", levels.Select(c => $"L{c.Level}: {c.FieldingCost}&#9889; / {c.Attack}A / {c.Defense}D"));
}

// One face, as it reads on the die: a creature face's level and stats (plus
// any energy printed on the same face - Dice Kingdom's hybrid Tardigrade
// faces carry both), a bare energy face, or an action face.
static string DescribeFace(Face f)
{
    if (f.Kind == FaceKind.ActionFace) return "Action";
    var energy = f.Symbols.Count > 0 ? DescribeEnergy(f) : null;
    if (f.Character is not { } c) return energy ?? "blank";
    var stats = $"L{c.Level}: {c.FieldingCost}&#9889; / {c.Attack}A / {c.Defense}D";
    return energy is null ? stats : $"{stats} + {energy}";
}

static string DescribeEnergy(Face f) => string.Join(" + ", f.Symbols.Select(s => $"{s.Count} {Enc(s.SymbolId)}"));

// Identical faces collapsed with a count, in the die's own face order.
static List<(string Face, int Count)> GroupFaces(DieDefinition die) =>
    die.Faces.Select(DescribeFace).GroupBy(s => s).Select(g => (g.Key, g.Count())).ToList();

static string DescribePassive(ChampionDef c) => ChampionPowers.Describe(c);

static string Enc(string s) => WebUtility.HtmlEncode(s);
