using DiceFight.V2.Model;

namespace DiceFight.V2;

// Registers each player's chosen Champion passive (if any) directly into
// GameState's existing modifier lists - the same lists ContinuousRegistry
// populates from CardDef.Continuous, just without going through
// ContinuousRegistry itself, since a Champion has no source die for its
// ActiveSourceDice scan to find (ChampionDef's own remarks). QueryEngine
// needs no changes: GetEffectiveAttack/GetEffectiveDefense/GetFieldingCost/
// GetPurchaseCost already sum over these same lists.
//
// Called from GameSetup.NewGame right alongside ContinuousRegistry.RegisterAll -
// once per game, not per turn, same "the modifier itself re-evaluates
// live" shape as everything else in that call.
public static class ChampionRegistry
{
    public static void RegisterAll(GameState state)
    {
        foreach (var player in new[] { state.PlayerOne, state.PlayerTwo })
        {
            if (player.ChampionId is not { } championId) continue;

            var champion = state.Config.Champions.FirstOrDefault(c => c.Id == championId)
                ?? throw new InvalidOperationException($"Player '{player.Id}' has unknown ChampionId '{championId}'.");

            Register(state, player.Id, champion);
        }
    }

    private static void Register(GameState state, string ownerId, ChampionDef champion)
    {
        switch (champion.PassiveKind)
        {
            case ChampionPassiveKind.AttackBuff:
                state.AttackModifiers.Add(new ChampionDieModifier(ownerId, champion.Amount));
                break;
            case ChampionPassiveKind.DefenseBuff:
                state.DefenseModifiers.Add(new ChampionDieModifier(ownerId, champion.Amount));
                break;
            case ChampionPassiveKind.FieldingCostDiscount:
                state.FieldingCostModifiers.Add(new ChampionDieModifier(ownerId, -champion.Amount));
                break;
            case ChampionPassiveKind.PurchaseCostDiscount:
                state.PurchaseCostModifiers.Add(new ChampionCostModifier(ownerId, -champion.Amount));
                break;
        }
    }

    // Attack/Defense/FieldingCost are all "a flat delta for every die this
    // player controls, always" - one class covers all three, registered
    // into whichever list matches the passive kind above.
    private sealed class ChampionDieModifier(string ownerId, int delta) : IDieStatModifier
    {
        public bool AppliesTo(GameState state, DieInstance die) => die.ControllerId == ownerId;
        public int GetDelta(GameState state, DieInstance die) => delta;
    }

    // Purchase cost is card+payer-scoped, not die-scoped (a purchase can
    // happen before any die from the card exists in the Reserve Pool) -
    // ICardCostModifier's own reason for being a separate interface.
    private sealed class ChampionCostModifier(string ownerId, int delta) : ICardCostModifier
    {
        public bool AppliesTo(GameState state, CardDef card, string payerId) => payerId == ownerId;
        public int GetDelta(GameState state, CardDef card, string payerId) => delta;
    }
}
