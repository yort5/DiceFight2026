namespace DiceFight.Engine.Model;

public sealed class Player
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    // Rule 2.1.7 - starting life; rule 1.1.3 - can never exceed this via gain.
    public const int StartingLife = 20;
    public int Life { get; set; } = StartingLife;

    // Rule 1.4.4/1.4.5 - gained from drawing fewer than 4 dice (2.3.10) or
    // partially spending a double-generic face; lost if unspent by the end
    // of the Main Step.
    public int VirtualGenericEnergy { get; set; }

    // Team is up to 8 unique Character/Action cards + 2 Basic Action cards
    // (rule 2.1.1), referenced by CardDef.Id.
    public List<string> TeamCardIds { get; } = [];
}
