using DiceFight.V2.Data;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 8 task 4, batch 1. Every card exercised through the
// real firing path (ground rule 6) - a TurnEngine action -> EventBus ->
// AbilityQueue -> EffectInterpreter.DrainQueue - not by invoking effects
// directly.
public class DpsCardsTests
{
    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static readonly IReadOnlyDictionary<string, Model.CardDef> Catalog =
        DpsCards.All.ToDictionary(c => c.Id);

    private static GameState NewGame()
    {
        var state = GameSetup.NewGame(DiceFightClassicConfig.Config, Catalog,
            new Player { Id = "p1", Name = "One" }, new Player { Id = "p2", Name = "Two" });
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    // A card die already in play, on the given level's face (face 0 is
    // always the energy face - MigrationDice's convention).
    private static Model.DieInstance Active(GameState state, Model.CardDef card, string controllerId, int level = 1, string? id = null)
    {
        var die = new Model.DieInstance { Id = id ?? $"{controllerId}-{card.Id}-{level}", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.FieldZone, CurrentFaceIndex = level };
        state.Dice.Add(die);
        return die;
    }

    // A rolled die sitting in the Reserve Pool, ready for Field/UseAction.
    private static Model.DieInstance Ready(GameState state, Model.CardDef card, string controllerId, int faceIndex, string? id = null)
    {
        var die = new Model.DieInstance { Id = id ?? $"{controllerId}-{card.Id}-ready", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.ReservePool, CurrentFaceIndex = faceIndex };
        state.Dice.Add(die);
        return die;
    }

    private static Model.DieInstance Sidekick(GameState state, string controllerId, Zone zone, int? face, string id)
    {
        var die = new Model.DieInstance { Id = id, PoolDieId = DiceFightClassicConfig.SidekickDie.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = zone, CurrentFaceIndex = face };
        state.Dice.Add(die);
        return die;
    }

    // Wild-energy Sidekick dice in the Reserve Pool, for fielding costs
    // (face 1 of the Sidekick die is Wild - rule 2.6.3.2, a fielding cost
    // takes any energy type).
    private static string[] Energy(GameState state, string controllerId, int count) =>
        [.. Enumerable.Range(0, count).Select(i => Sidekick(state, controllerId, Zone.ReservePool, 1, $"{controllerId}-energy{i}").Id)];

    private static void Drain(GameState state, AbilityQueue queue, int rollIndex = 0) =>
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(rollIndex), new Random(1));

    private static void Answer(GameState state, string preferredId)
    {
        if (state.PendingChoice is not { } pending) return;
        var pick = pending.CandidateIds.Contains(preferredId) ? preferredId : pending.CandidateIds[0];
        EffectInterpreter.AnswerPendingChoice(state, [pick]);
    }

    [Fact]
    public void The_Dps_Batch_Is_Valid_Against_The_Classic_Config()
    {
        Assert.Empty(DiceFightClassicConfig.Config.ValidateCatalog([.. DpsCards.All]));
    }

    [Fact]
    public void PowerBolt_Deals_2_Damage_To_A_Chosen_Player()
    {
        var state = NewGame();
        var bolt = Ready(state, DpsCards.PowerBolt, "p1", 0);
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, bolt.Id);
        Drain(state, queue);
        Answer(state, "p2"); // DieOrPlayer - both players and any character die are candidates

        Assert.Equal(18, state.PlayerTwo.Life);
    }

    [Fact]
    public void Ronan_Loses_You_1_Life_When_Fielded_And_Costs_The_Opponent_1_When_KOd()
    {
        var state = NewGame();
        var ronan = Ready(state, DpsCards.RonanTheAccuserTreason, "p1", 1);
        var energy = Energy(state, "p1", 1); // every Ronan face costs 1+ to field
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, ronan.Id, energy);
        Drain(state, queue);
        Assert.Equal(19, state.PlayerOne.Life);

        EffectInterpreter.KoDie(state, queue, ronan, triggersKOAbilities: true);
        Drain(state, queue);
        Assert.Equal(19, state.PlayerTwo.Life);
    }

    [Fact]
    public void StormCloudCover_Flags_A_3A_Die_But_Not_A_Stronger_One()
    {
        var state = NewGame();
        var weak = Active(state, DpsCards.PsylockeTelepath, "p2", level: 3, id: "weak"); // 3A
        var strong = Active(state, DpsCards.RonanTheAccuserTreason, "p2", level: 1, id: "strong"); // 5A
        var storm = Ready(state, DpsCards.StormCloudCover, "p1", 1);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, storm.Id, []);
        Drain(state, queue);

        // Storm herself is 2A at level 1, so she and `weak` both qualify;
        // `strong` (5A) is filtered out by the Stat threshold entirely.
        Assert.NotNull(state.PendingChoice);
        Assert.DoesNotContain("strong", state.PendingChoice!.CandidateIds);
        Answer(state, "weak");

        Assert.Contains(CombatFlagKind.CantBlock, weak.CombatFlags);
        Assert.Empty(strong.CombatFlags);
    }

    [Fact]
    public void Rally_Moves_Up_To_2_Sidekicks_Normally_And_3_On_A_Double_Burst_Face()
    {
        // Face 2 of Rally's die carries the double burst (MigrationDice.Action).
        foreach (var (faceIndex, expected) in new[] { (0, 2), (2, 3) })
        {
            var state = NewGame();
            for (var i = 0; i < 4; i++) Sidekick(state, "p1", Zone.UsedPile, null, $"sk{i}");
            var rally = Ready(state, DpsCards.Rally, "p1", faceIndex);
            var queue = new AbilityQueue();

            TurnEngine.UseAction(state, queue, rally.Id);
            Drain(state, queue);
            // "up to N" of 4 candidates is a real choice - take the max.
            var pending = state.PendingChoice!;
            EffectInterpreter.AnswerPendingChoice(state, [.. pending.CandidateIds.Take(expected)]);

            Assert.Equal(expected, state.DiceIn("p1", Zone.FieldZone).Count());
        }
    }

    [Fact]
    public void PsylockeTelepath_Grants_Overcrush_To_Its_Target()
    {
        var state = NewGame();
        var ally = Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "ally");
        var psylocke = Ready(state, DpsCards.PsylockeTelepath, "p1", 1);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, psylocke.Id, []);
        Drain(state, queue);
        Answer(state, "ally");

        Assert.Contains("Overcrush", QueryEngine.GetKeywords(state, ally));
    }

    [Fact]
    public void MasterMold_KOs_Only_A_Brotherhood_Tagged_Die()
    {
        var state = NewGame();
        var brotherhood = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p2", id: "bom");
        var xmen = Active(state, DpsCards.PsylockeTelepath, "p2", id: "xmen");
        var masterMold = Ready(state, DpsCards.MasterMoldTargetingMutants, "p1", 1);
        var energy = Energy(state, "p1", 1);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, masterMold.Id, energy);
        Drain(state, queue);

        // Only one legal target, so it auto-resolves with no choice.
        Assert.Null(state.PendingChoice);
        Assert.Equal(Zone.PrepArea, brotherhood.Zone); // KO'd
        Assert.Equal(Zone.FieldZone, xmen.Zone);
    }

    [Fact]
    public void Magneto_Reacts_To_One_Of_His_Own_Brotherhood_Dice_Being_KOd()
    {
        var state = NewGame();
        var magneto = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", id: "magneto");
        var opposing = Active(state, DpsCards.PsylockeTelepath, "p2", id: "opposing");
        var queue = new AbilityQueue();

        // Magneto is himself Brotherhood-affiliated, so KO'ing him is a
        // legitimate trigger of his own reactive ability (EventBus keeps
        // the subject die as a listener candidate even once it's left the
        // Field Zone - the Phase 4 fix).
        EffectInterpreter.KoDie(state, queue, magneto, triggersKOAbilities: true);
        Drain(state, queue);

        Assert.Equal(Zone.PrepArea, opposing.Zone); // KO'd by Magneto's reaction
    }

    [Fact]
    public void Magnetos_Global_Draws_To_Prep_Only_While_The_Prep_Area_Is_Empty()
    {
        var state = NewGame();
        var magneto = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1");
        Sidekick(state, "p1", Zone.ReservePool, 4, "mask-energy"); // face 4 = Mask
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, magneto.Id, abilityIndex: 1, ["mask-energy"]);
        Drain(state, queue);

        Assert.Single(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void CyclopsFirstClass_Fires_For_Another_Founder_Die_But_Not_A_Plain_One()
    {
        var state = NewGame();
        var cyclops = Active(state, DpsCards.CyclopsFirstClass, "p1");
        var victim = Active(state, DpsCards.RonanTheAccuserTreason, "p2", id: "victim"); // 5D at level 1
        var queue = new AbilityQueue();

        // A non-Founder die fielded (Deathbird is vanilla, so anything
        // queued here could only have come from Cyclops): no reaction.
        var plain = Ready(state, DpsCards.DeathbirdTreacherous, "p1", 1, "plain");
        TurnEngine.Field(state, queue, plain.Id, []);
        Assert.Empty(queue.Pending);

        // A second Founder die fielded: Cyclops reacts.
        var founder = Ready(state, DpsCards.CyclopsFirstClass, "p1", 1, "founder");
        TurnEngine.Field(state, queue, founder.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        Answer(state, "victim");

        Assert.Equal(2, victim.Damage);
    }

    [Fact]
    public void CorsairCriminalRecord_KOs_One_Villain_Normally_And_Two_Against_A_Wide_Board()
    {
        foreach (var (opposingCount, expectedKOs) in new[] { (2, 1), (4, 2) })
        {
            var state = NewGame();
            // Villains-tagged opposing dice, enough of them to be KO'd.
            for (var i = 0; i < opposingCount; i++)
                Active(state, DpsCards.MasterMoldTargetingMutants, "p2", id: $"villain{i}");

            var corsair = Ready(state, DpsCards.CorsairCriminalRecord, "p1", 1);
            var queue = new AbilityQueue();

            TurnEngine.Field(state, queue, corsair.Id, []);
            Drain(state, queue);
            if (state.PendingChoice is { } pending)
                EffectInterpreter.AnswerPendingChoice(state, [.. pending.CandidateIds.Take(pending.MaxCount)]);

            Assert.Equal(expectedKOs, state.Dice.Count(d => d.Id.StartsWith("villain") && d.Zone == Zone.PrepArea));
        }
    }

    [Fact]
    public void DarkPhoenix_Deals_2_To_The_Opponent_When_She_Attacks()
    {
        var state = NewGame();
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;
        var phoenix = Active(state, DpsCards.DarkPhoenixEnemyOfTheShiar, "p1");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [phoenix.Id]);
        Drain(state, queue);

        Assert.Equal(18, state.PlayerTwo.Life);
    }

    [Fact]
    public void Magik_Discounts_The_Next_Basic_Action_Purchase_Only()
    {
        var state = NewGame();
        var magik = Ready(state, DpsCards.MagikWielderOfTheSoulsword, "p1", 1);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, magik.Id, []);
        Drain(state, queue);

        var pending = Assert.Single(state.PendingPurchaseModifiers);
        Assert.Equal(-1, pending.Delta);
        Assert.Equal(CardType.BasicAction, pending.CardKind);
    }

    // The two batch-1 tail entries, pinned inert rather than silently
    // half-working - see V2_TAIL_POLICY.md for each one's gap.
    [Fact]
    public void Tailed_Batch1_Cards_Are_Vanilla()
    {
        foreach (var card in new[] { DpsCards.DeathbirdTreacherous, DpsCards.ColossusPiotr })
        {
            Assert.False(card.IsImplemented);
            Assert.Empty(card.Abilities);
            Assert.Empty(card.Continuous);
        }
    }
}
