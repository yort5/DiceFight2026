namespace DiceFight.V2;

public sealed record SpentEnergy(string DieId, string? CardId, string SymbolId, int Count);
public sealed record EnergyPayment(IReadOnlyList<SpentEnergy> Spent, int VirtualEnergy);
