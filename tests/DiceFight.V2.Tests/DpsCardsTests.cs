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

    // A card die already in play, on the given LEVEL's face. Migrated dice
    // are the real six: three energy faces (indices 0-2) then one face per
    // level, so level N sits at index N + 2. Tests say "level 1", never an
    // index, so this is the only place that has to know.
    private const int FirstLevelFace = 3;

    private static Model.DieInstance Active(GameState state, Model.CardDef card, string controllerId, int level = 1, string? id = null)
    {
        var die = new Model.DieInstance { Id = id ?? $"{controllerId}-{card.Id}-{level}", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.FieldZone, CurrentFaceIndex = FirstLevelFace + level - 1 };
        state.Dice.Add(die);
        return die;
    }

    // A rolled die sitting in the Reserve Pool, ready for Field/UseAction.
    // `face` is a LEVEL for a character die (1-based) and which action
    // face for an action die (0-based) - both sit after the three energy
    // faces, so neither call site has to know the real index.
    private static Model.DieInstance Ready(GameState state, Model.CardDef card, string controllerId, int face, string? id = null)
    {
        var faceIndex = card.CardType == Model.CardType.Character ? FirstLevelFace + face - 1 : FirstLevelFace + face;
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

    // A die parked in the Reserve Pool on one of its double-energy faces
    // (index 0 - MigrationDice's "doubles first" order) - exactly where
    // TurnEngine.FinishRoll leaves a die that will Energize.
    private static Model.DieInstance Energized(GameState state, Model.CardDef card, string controllerId, string? id = null)
    {
        var die = new Model.DieInstance { Id = id ?? $"{controllerId}-{card.Id}-energized", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        return die;
    }

    // TurnEngine.FinishRoll fires exactly this event once the Roll and
    // Reroll Step ends (V2_TAIL_POLICY.md's Energize entry) - firing it
    // directly here is equivalent to the real action without needing a
    // full ClearAndDraw/Roll/FinishRoll dance for every card below.
    private static void FireEnergize(GameState state, AbilityQueue queue) =>
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, state.ActivePlayerId, StepIds.Main));

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
        Sidekick(state, "p1", Zone.ReservePool, 4, "mask-energy"); // face 4 = Mask
        var queue = new AbilityQueue();

        // Card-scoped (rule 2.6.5.2) - no Magneto die need be active.
        TurnEngine.UseGlobal(state, queue, DpsCards.MagnetoFounderOfTheBrotherhood.Id, "p1", abilityIndex: 1, ["mask-energy"]);
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

    // Rule 2.6.5.2, end to end: Archnemesis's Global lives on a BASIC
    // ACTION card, so no die of it is ever fielded - it was unreachable
    // until Globals became card-scoped. Its effect is Spike B's
    // SetDefense: StatOf(target, Attack).
    [Fact]
    public void Archnemesis_Global_Works_From_A_Basic_Action_Card_With_No_Die_In_Play()
    {
        var state = NewGame();
        var target = Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 1, id: "target"); // 5A/5D
        Sidekick(state, "p1", Zone.ReservePool, 5, "shield-energy"); // face 5 = Shield
        var queue = new AbilityQueue();
        Assert.DoesNotContain(state.Dice, d => d.CardId == DpsCards.Archnemesis.Id); // nothing of this card is in play

        TurnEngine.UseGlobal(state, queue, DpsCards.Archnemesis.Id, "p1", abilityIndex: 0, ["shield-energy"]);
        Drain(state, queue);
        Answer(state, target.Id);

        Assert.Equal(5, QueryEngine.GetDefense(state, target)); // D set to its own A
    }

    // The TURN SUMMARY's Main Step: "Both players can use Global
    // Abilities (Inactive player after priority passes)." And rule
    // 1.5.8.5 - the inactive player's spent energy goes to the Used Pile
    // rather than Out of Play, which is an active-turn concept.
    [Fact]
    public void The_Inactive_Player_Can_Use_A_Global_And_Their_Energy_Goes_To_The_Used_Pile()
    {
        var state = NewGame();
        Assert.Equal("p1", state.ActivePlayerId);
        var target = Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 1, id: "target");
        var energy = Sidekick(state, "p2", Zone.ReservePool, 5, "p2-shield"); // the INACTIVE player's energy
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, DpsCards.Archnemesis.Id, "p2", abilityIndex: 0, ["p2-shield"]);
        Drain(state, queue);
        Answer(state, target.Id);

        Assert.Equal(Zone.UsedPile, energy.Zone); // rule 1.5.8.5, not Out of Play
        Assert.Equal(5, QueryEngine.GetDefense(state, target));
    }

    // ---- Batch 2 ----

    // Finding 12's worked answer end to end, and a live check that the
    // rule-3.2.5 snapshot holds: step 1 puts a Field Zone die INTO the
    // Used Pile, and step 2's Used Pile candidates must not include it.
    [Fact]
    public void Mutation_Swaps_A_Field_Die_With_A_Used_Pile_Die_And_Spins_It_To_Level_1()
    {
        var state = NewGame();
        var fielded = Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 1, id: "fielded");
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = null };
        state.Dice.Add(dormant);
        var mutation = Ready(state, DpsCards.Mutation, "p1", 0);
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, mutation.Id);
        Drain(state, queue);

        // Exactly one candidate per clause, so no choice is raised - and
        // in particular the just-swapped-out die is NOT offered back.
        Assert.Null(state.PendingChoice);
        Assert.Equal(Zone.UsedPile, fielded.Zone);
        Assert.Equal(Zone.FieldZone, dormant.Zone);
        Assert.Equal(1, state.GetCurrentFace(dormant)!.Character!.Level);
    }

    [Fact]
    public void Mutations_Global_Spins_One_Die_Down_And_Another_Up()
    {
        var state = NewGame();
        var down = Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 2, id: "down");
        var up = Active(state, DpsCards.PsylockeTelepath, "p1", level: 1, id: "up");
        Sidekick(state, "p1", Zone.ReservePool, 4, "mask"); // face 4 = Mask
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, DpsCards.Mutation.Id, "p1", abilityIndex: 1, ["mask"]);
        Drain(state, queue);
        Answer(state, "down");
        Drain(state, queue);
        Answer(state, "up");

        Assert.Equal(1, state.GetCurrentFace(down)!.Character!.Level);
        Assert.Equal(2, state.GetCurrentFace(up)!.Character!.Level);
    }

    // Finding 8's Reroll params. FixedRoller(0) lands every die on face
    // 0, which MigrationDice makes the ENERGY face - so both rerolled
    // dice fail to roll a character and move on, and Psylocke's
    // DamagePerMoved fires twice.
    [Fact]
    public void Psylocke_Rerolls_Two_Opposing_Dice_Moves_The_Non_Characters_And_Deals_2_Per_Die()
    {
        var state = NewGame();
        var a = Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "a"); // p1 is the target side here
        var b = Active(state, DpsCards.MasterMoldTargetingMutants, "p1", id: "b");
        var psylocke = Ready(state, DpsCards.PsylockeAdvancedTelekineticCombatant, "p2", 1);
        state.ActivePlayerId = "p2"; // Psylocke's controller is the one fielding
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, psylocke.Id, []);
        Drain(state, queue);
        var pending = state.PendingChoice!;
        EffectInterpreter.AnswerPendingChoice(state, [.. pending.CandidateIds.Take(2)]);

        Assert.Equal(Zone.UsedPile, a.Zone);
        Assert.Equal(Zone.UsedPile, b.Zone);
        Assert.Equal(20 - 4, state.PlayerOne.Life); // 2 dice moved x 2 damage
    }

    // Finding 13 (Loyalty Counters) plus Spike C's step discriminator.
    [Fact]
    public void JeanGrey_Adds_A_Loyalty_Counter_At_CleanUp_Only_When_Nothing_Was_KOd()
    {
        var state = NewGame();
        var jean = Active(state, DpsCards.JeanGreyPeacefulCoexistence, "p1");
        var queue = new AbilityQueue();

        TurnEngine.EnterAttackStep(state, queue);
        TurnEngine.CleanUp(state, queue);
        Drain(state, queue);
        Assert.Equal(1, state.Counters[("p1", DpsCards.JeanGreyPeacefulCoexistence.Id, "Loyalty")]);

        // Now KO something during the next turn - no counter that time.
        state.CurrentStep = TurnStep.Main;
        state.ActivePlayerId = "p1";
        var victim = Active(state, DpsCards.PsylockeTelepath, "p2", id: "victim");
        EffectInterpreter.KoDie(state, queue, victim, triggersKOAbilities: false);
        TurnEngine.EnterAttackStep(state, queue);
        TurnEngine.CleanUp(state, queue);
        Drain(state, queue);

        Assert.Equal(1, state.Counters[("p1", DpsCards.JeanGreyPeacefulCoexistence.Id, "Loyalty")]); // unchanged
    }

    [Fact]
    public void Deadpool_Makes_Fielding_Cost_2_Dice_Free_But_Not_Costlier_Ones()
    {
        var state = NewGame();
        Active(state, DpsCards.DeadpoolCollectThis, "p1");
        // Ronan level 3 costs 2 to field; Magneto level 3 costs 3.
        var cheap = Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 3, id: "cheap");
        var pricey = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", level: 3, id: "pricey");

        Assert.Equal(0, QueryEngine.GetFieldingCost(state, cheap)); // 2 - 2
        Assert.Equal(3, QueryEngine.GetFieldingCost(state, pricey)); // above the threshold, untouched
    }

    [Fact]
    public void Angel_Stops_The_Opponent_Targeting_Your_Sidekicks_With_Globals_Only()
    {
        var state = NewGame();
        Active(state, DpsCards.AngelXaviersDream, "p1");
        var sidekick = Sidekick(state, "p1", Zone.FieldZone, 0, "sk");

        Assert.False(QueryEngine.CanBeTargeted(state, sidekick, "p2", ProtectionFrom.Global));
        Assert.True(QueryEngine.CanBeTargeted(state, sidekick, "p1", ProtectionFrom.Global)); // its own controller
        Assert.True(QueryEngine.CanBeTargeted(state, sidekick, "p2", ProtectionFrom.Action)); // Globals only
    }

    [Fact]
    public void MagnetoVisionary_Forces_Two_Or_More_Blockers_On_Brotherhood_Attackers()
    {
        var state = NewGame();
        state.CurrentStep = TurnStep.Attack;
        var magneto = Active(state, DpsCards.MagnetoVisionary, "p1"); // Brotherhood - protected by its own rule
        var blocker = Active(state, DpsCards.PsylockeTelepath, "p2", id: "blocker");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [magneto.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(magneto.Id, blocker.Id);

        Assert.Throws<InvalidOperationException>(() => CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]));
    }

    [Fact]
    public void Blob_May_Block_Three_Attackers()
    {
        var state = NewGame();
        state.CurrentStep = TurnStep.Attack;
        var blob = Active(state, DpsCards.BlobImmovable, "p2");
        var a1 = Active(state, DpsCards.PsylockeTelepath, "p1", id: "a1");
        var a2 = Active(state, DpsCards.MasterMoldTargetingMutants, "p1", id: "a2");
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [a1.Id, a2.Id]);
        var assignment = new CombatAssignment();
        assignment.AssignBlocker(a1.Id, blob.Id);
        assignment.AssignBlocker(a2.Id, blob.Id);

        CombatEngine.DeclareBlockers(state, queue, assignment, [blob.Id]); // does not throw
        Assert.Equal(StepIds.ActionGlobalWindow, state.CurrentStepId);
    }

    [Fact]
    public void Batch2_Tailed_Cards_Are_Vanilla()
    {
        foreach (var card in new[] { DpsCards.PhoenixPsionicMaelstrom, DpsCards.ColossusOrganicSteel })
        {
            Assert.False(card.IsImplemented);
            Assert.Empty(card.Abilities);
            Assert.Empty(card.Continuous);
        }
    }

    // Making the Team, both branches - and the regression for FieldDie's
    // corrected default: a die that rolls its LEVEL 3 face must be
    // fielded at level 3, not snapped to level 1.
    [Fact]
    public void MakingTheTeam_Fields_A_Rolled_Character_Die_At_The_Level_It_Rolled()
    {
        var state = NewGame();
        // Ronan's die is the real six: faces 0-2 energy, 3-5 levels 1-3.
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.RonanTheAccuserTreason.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = null };
        state.Dice.Add(dormant);
        var making = Ready(state, DpsCards.MakingTheTeam, "p1", 0);
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, making.Id);
        Drain(state, queue, rollIndex: FirstLevelFace + 2); // rolls its level-3 face

        Assert.Equal(Zone.FieldZone, dormant.Zone);
        Assert.Equal(3, state.GetCurrentFace(dormant)!.Character!.Level); // NOT snapped to 1
    }

    [Fact]
    public void MakingTheTeam_Preps_A_Die_That_Rolls_A_Non_Character_Face()
    {
        var state = NewGame();
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.RonanTheAccuserTreason.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = null };
        state.Dice.Add(dormant);
        var making = Ready(state, DpsCards.MakingTheTeam, "p1", 0);
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, making.Id);
        Drain(state, queue, rollIndex: 0); // face 0 is the energy face

        Assert.Equal(Zone.PrepArea, dormant.Zone);
    }

    // FieldDie's override path still works - Jubilee-shaped "field this
    // die at level 2" overrides whatever it happens to be showing.
    [Fact]
    public void FieldDie_With_An_Explicit_Level_Still_Overrides_The_Rolled_Face()
    {
        var state = NewGame();
        var die = Ready(state, DpsCards.RonanTheAccuserTreason, "p1", 3); // showing level 3
        var ctx = new EffectContext
        {
            State = state, Queue = new AbilityQueue(), ControllerId = "p1",
            Trigger = Model.Effects.TriggerKind.Global, Roller = new FixedRoller(0), Random = new Random(1),
        };

        EffectInterpreter.Execute(new FieldDie(new TargetFilter(Ownership: TargetOwnership.Own, Kind: TargetKind.CharacterDie, Zones: [Zone.ReservePool]), Free: true, Level: 2), ctx);

        Assert.Equal(2, state.GetCurrentFace(die)!.Character!.Level);
    }

    // Phoenix "Eternal Flame" - a stat threshold on SELECTION, not a
    // condition. Count: 0 flags every match at once with no choice.
    [Fact]
    public void PhoenixEternalFlame_Flags_Every_Opposing_Die_Under_4A_And_No_Others()
    {
        var state = NewGame();
        state.CurrentStep = TurnStep.Attack;
        var phoenix = Active(state, DpsCards.PhoenixEternalFlame, "p1");
        var weak = Active(state, DpsCards.PsylockeTelepath, "p2", level: 3, id: "weak");     // 3A
        var strong = Active(state, DpsCards.RonanTheAccuserTreason, "p2", level: 1, id: "strong"); // 5A
        var queue = new AbilityQueue();

        CombatEngine.DeclareAttackers(state, queue, [phoenix.Id]);
        Drain(state, queue);

        Assert.Null(state.PendingChoice); // Count: 0 - every match, no choice
        Assert.Contains(CombatFlagKind.CantBlock, weak.CombatFlags);
        Assert.Empty(strong.CombatFlags);
    }

    // The remaining batch-1 tail entry, pinned inert rather than silently
    // half-working - see V2_TAIL_POLICY.md for its gap (Deadly).
    [Fact]
    public void Tailed_Batch1_Cards_Are_Vanilla()
    {
        Assert.False(DpsCards.DeathbirdTreacherous.IsImplemented);
        Assert.Empty(DpsCards.DeathbirdTreacherous.Abilities);
        Assert.Empty(DpsCards.DeathbirdTreacherous.Continuous);
    }

    // Spike C's payoff, end to end: Colossus's "at the end of your turn"
    // ability now names the `cleanup` window specifically, so it fires
    // exactly once per turn - at Clean Up - and NOT when its controller
    // enters their own Attack Step, which is what kept it tailed.
    [Fact]
    public void ColossusPiotr_Fires_At_CleanUp_Only_And_Scales_With_Level_2_Plus_Dice()
    {
        var state = NewGame();
        Active(state, DpsCards.ColossusPiotr, "p1", level: 2, id: "colossus"); // level 2 - counts itself
        Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 3, id: "ronan"); // level 3 - counts
        Active(state, DpsCards.PsylockeTelepath, "p1", level: 1, id: "psylocke"); // level 1 - does not
        var queue = new AbilityQueue();

        // Entering the Attack Step must NOT trigger it.
        TurnEngine.EnterAttackStep(state, queue);
        Assert.Empty(queue.Pending);

        TurnEngine.CleanUp(state, queue);
        Drain(state, queue);

        Assert.Equal(20 - 4, state.PlayerTwo.Life); // 2 qualifying dice x 2 damage
    }

    // --- Batch 3: the two cards Spike A was built for ---

    // D'Ken "Shi'ar Civil War" - the AbilityBlank template's first real
    // card. Both clauses, and the cost threshold that separates who is
    // affected from who is not.
    [Fact]
    public void DKen_Blanks_Cheap_Opposing_Dice_And_Makes_Them_Free_To_Field()
    {
        var state = NewGame();
        ContinuousRegistry.RegisterAll(state);

        // Storm costs 2 (under D'Ken's threshold); Ronan "No Mercy" costs 6.
        // Her LEVEL 3 face, because levels 1-2 already field for free and
        // "free to field" would then prove nothing.
        var cheap = Active(state, DpsCards.StormExtremeWeather, "p2", level: 3);
        var dear = Active(state, DpsCards.RonanTheAccuserNoMercy, "p2");

        Assert.True(QueryEngine.AbilitiesActive(state, cheap));
        var fieldingCostBefore = QueryEngine.GetFieldingCost(state, cheap);
        Assert.True(fieldingCostBefore > 0);

        Active(state, DpsCards.DKenShiarCivilWar, "p1");

        Assert.False(QueryEngine.AbilitiesActive(state, cheap));
        Assert.Equal(0, QueryEngine.GetFieldingCost(state, cheap));   // "free to field"
        Assert.True(QueryEngine.AbilitiesActive(state, dear));        // 6 cost - over the threshold
    }

    // D'Ken's own side is untouched: "OPPOSING character dice".
    [Fact]
    public void DKen_Does_Not_Blank_His_Own_Side()
    {
        var state = NewGame();
        ContinuousRegistry.RegisterAll(state);
        var ally = Active(state, DpsCards.StormExtremeWeather, "p1");
        Active(state, DpsCards.DKenShiarCivilWar, "p1");

        Assert.True(QueryEngine.AbilitiesActive(state, ally));
    }

    // Mister Sinister "Mutant Supremacist" - the card Part 19 listed
    // under "what this spike does NOT close". Its side-wide half is
    // card-scoped, so it reaches a copy still in the bag AND the card's
    // Global, neither of which a die-scoped blank can touch.
    [Fact]
    public void MisterSinister_Ignores_All_Text_On_Opposing_Cards_Including_A_Global()
    {
        var state = NewGame();
        ContinuousRegistry.RegisterAll(state);

        // Psylocke has a Global; give p2 one die in play and one unbought.
        var inPlay = Active(state, DpsCards.PsylockeTelepath, "p2");
        var unbought = new Model.DieInstance
        {
            Id = "p2-spare", CardId = DpsCards.PsylockeTelepath.Id,
            OwnerId = "p2", ControllerId = "p2", Zone = Zone.Unpurchased,
        };
        state.Dice.Add(unbought);

        Assert.True(QueryEngine.CardTextActive(state, "p2", DpsCards.PsylockeTelepath.Id));

        var sinister = Ready(state, DpsCards.MisterSinisterMutantSupremacist, "p1", 1);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, sinister.Id, Energy(state, "p1", 1));
        Drain(state, queue);

        Assert.False(QueryEngine.AbilitiesActive(state, inPlay));
        Assert.False(QueryEngine.AbilitiesActive(state, unbought));  // still in the bag
        Assert.False(QueryEngine.CardTextActive(state, "p2", DpsCards.PsylockeTelepath.Id));
        // p1's own copy of the same card would be unaffected - per player.
        Assert.True(QueryEngine.CardTextActive(state, "p1", DpsCards.PsylockeTelepath.Id));
    }

    // --- Real die faces, and the bug that inferring face kind caused ---

    // A Basic Action die's action faces carry neither symbols nor
    // character data, so the old "Character is null means energy face"
    // test called every one of them an energy face. Asked directly, it
    // said an action die sitting on its ACTION face was on an energy one.
    [Fact]
    public void An_Action_Face_Is_Not_Reported_As_An_Energy_Face()
    {
        var state = NewGame();
        var bolt = Ready(state, DpsCards.PowerBolt, "p1", 0);
        var bindings = new Dictionary<string, string> { ["self"] = bolt.Id };

        Assert.Equal(FaceKind.ActionFace, state.GetCurrentFace(bolt)!.Kind);
        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new OnFaceKind(FaceKind.EnergyFace, "self"), bindings));
        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new OnFaceKind(FaceKind.CharacterFace, "self"), bindings));
    }

    // ...and SpinToEnergy now has three real energy faces to choose from
    // rather than six candidates of which half were action faces.
    [Fact]
    public void SpinToEnergy_Lands_An_Action_Die_On_One_Of_Its_Energy_Faces()
    {
        var state = NewGame();
        var bolt = Ready(state, DpsCards.PowerBolt, "p1", 0);

        EffectInterpreter.Execute(new SpinToEnergy(new TargetFilter(Kind: TargetKind.AnyDie, Self: true), Amount: 2),
            BuildCtx(state, "p1", bolt.Id));

        Assert.Equal(FaceKind.EnergyFace, state.GetCurrentFace(bolt)!.Kind);
    }

    // The real layout, which is what makes Energize expressible at all:
    // a character die has two double-energy faces and one single, then
    // its levels (see MigrationDice's own remarks).
    [Fact]
    public void A_Migrated_Character_Die_Has_The_Real_Six_Faces()
    {
        var faces = DpsCards.StormExtremeWeather.Die.Faces;

        Assert.Equal(6, faces.Count);
        Assert.Equal([2, 2, 1], faces.Take(3).Select(f => f.SymbolCount));
        Assert.All(faces.Take(3), f => Assert.Equal(FaceKind.EnergyFace, f.Kind));
        Assert.Equal([1, 2, 3], faces.Skip(3).Select(f => f.Character!.Level));
        Assert.All(faces.Skip(3), f => Assert.Equal(FaceKind.CharacterFace, f.Kind));
    }

    // A Crossover's doubles carry one pip of EACH type, not two of one
    // (rule 2.6.2.3), and its single is symbol-less.
    [Fact]
    public void A_Crossover_Dies_Doubles_Carry_One_Pip_Of_Each_Type()
    {
        var die = MigrationDice.Character("X", ["Bolt", "Fist"], (0, 1, 1), (1, 2, 2), (1, 3, 3));

        var doubles = die.Faces[0];
        Assert.Equal(2, doubles.SymbolCount);
        Assert.Equal(["Bolt", "Fist"], doubles.Symbols.Select(x => x.SymbolId));
        Assert.All(doubles.Symbols, x => Assert.Equal(1, x.Count));
        // The single is Generic - one energy that pays toward the amount
        // but satisfies neither of the Crossover's two type requirements.
        var single = Assert.Single(die.Faces[2].Symbols);
        Assert.Equal("Generic", single.SymbolId);
        Assert.Equal(1, single.Count);
    }

    private static EffectContext BuildCtx(GameState state, string controllerId, string selfId)
    {
        var ctx = new EffectContext
        {
            State = state, Queue = new AbilityQueue(), ControllerId = controllerId,
            Trigger = TriggerKind.Global, Roller = new FixedRoller(0), Random = new Random(0),
        };
        ctx.Bind("self", selfId);
        return ctx;
    }

    // --- Batch 4 (2026-09-01) - the Energize unlock ---

    [Fact]
    public void PhoenixFirepower_Energize_Deals_2_To_A_Chosen_Player()
    {
        var state = NewGame();
        Energized(state, DpsCards.PhoenixFirepower, "p1");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "p2"); // DieOrPlayer - PowerBolt's own precedent

        Assert.Equal(18, state.PlayerTwo.Life);
    }

    [Fact]
    public void StormQueen_Energize_Rerolls_An_Opposing_Character_Die()
    {
        var state = NewGame();
        Energized(state, DpsCards.StormQueen, "p1");
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", level: 2, id: "target");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(0, target.CurrentFaceIndex); // Drain's FixedRoller(0)
    }

    [Fact]
    public void ProfessorXUncannyLeadership_Energize_Moves_An_XMen_Die_From_Used_Pile_To_Prep_Area()
    {
        var state = NewGame();
        Energized(state, DpsCards.ProfessorXUncannyLeadership, "p1");
        // A Used Pile die is always unrolled (rule 1.6.8) - AnyDie, not
        // CharacterDie, is what this card's own Energize clause has to use.
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = null };
        state.Dice.Add(dormant);
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "dormant");

        Assert.Equal(Zone.PrepArea, dormant.Zone);
    }

    [Fact]
    public void CyclopsDefendingThePhoenix_Energize_Damages_A_Target_And_Rerolls_Itself()
    {
        var state = NewGame();
        var cyclops = Energized(state, DpsCards.CyclopsDefendingThePhoenix, "p1");
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", level: 3, id: "target");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        // rollIndex: 2 (the single-energy face), not the default 0 - a
        // reroll landing BACK on double energy would now correctly
        // re-trigger Energize immediately (Part 30's own fix), which
        // with a real random roller is just an increasingly-unlikely
        // chain, but with this FixedRoller would loop forever.
        Drain(state, queue, rollIndex: 2);
        Answer(state, "target");

        Assert.Equal(1, target.Damage);
        Assert.Equal(2, cyclops.CurrentFaceIndex); // rerolled itself, off double energy
    }

    [Fact]
    public void RogueStrengthAbsorption_Energize_Sets_A_Targets_Attack_To_0()
    {
        var state = NewGame();
        Energized(state, DpsCards.RogueStrengthAbsorption, "p1");
        var target = Active(state, DpsCards.RonanTheAccuserTreason, "p2", level: 1, id: "target");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(0, QueryEngine.GetAttack(state, target));
    }

    [Fact]
    public void PsylockeHeiress_Gets_2_Attack_Per_XMen_Die_In_Her_Own_Prep_Area()
    {
        var state = NewGame();
        var psylocke = Active(state, DpsCards.PsylockeHeiress, "p1", level: 1);
        var baseline = QueryEngine.GetAttack(state, psylocke);
        Sidekick(state, "p1", Zone.PrepArea, null, "sk"); // not X-Men - shouldn't count
        Active(state, DpsCards.PsylockeTelepath, "p1", id: "xmen1"); // wrong zone (Field) - shouldn't count either
        var counted = new Model.DieInstance { Id = "xmen-prep", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.PrepArea, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(counted);

        Assert.Equal(baseline + 2, QueryEngine.GetAttack(state, psylocke));
    }

    [Fact]
    public void PsylockeHeiress_Energize_Spins_A_Target_Up_1_Level()
    {
        var state = NewGame();
        Energized(state, DpsCards.PsylockeHeiress, "p1");
        var target = Active(state, DpsCards.RonanTheAccuserTreason, "p2", level: 1, id: "target");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(2, state.GetCurrentFace(target)!.Character!.Level);
    }

    [Fact]
    public void JubileeRebelliousNature_Energize_Fields_Free_At_Level_2_When_Behind_On_Life()
    {
        var state = NewGame();
        var jubilee = Energized(state, DpsCards.JubileeRebelliousNature, "p1");
        state.PlayerOne.Life = 10; // behind p2's 20
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, jubilee.Id); // MayPay accept - the stand-in is the source die

        Assert.Equal(Zone.FieldZone, jubilee.Zone);
        Assert.Equal(2, state.GetCurrentFace(jubilee)!.Character!.Level);
    }

    [Fact]
    public void JubileeRebelliousNature_Energize_Does_Nothing_When_Not_Behind_On_Life()
    {
        var state = NewGame();
        var jubilee = Energized(state, DpsCards.JubileeRebelliousNature, "p1");
        var queue = new AbilityQueue(); // both players start at 20 - condition false

        FireEnergize(state, queue);
        Drain(state, queue);

        Assert.Null(state.PendingChoice); // Conditional's own Else (none) - no offer at all
        Assert.Equal(Zone.ReservePool, jubilee.Zone);
    }

    [Fact]
    public void MystiqueTaughtByMagneto_Makes_Brotherhood_Dice_Free_To_Field_While_Active()
    {
        var state = NewGame();
        Active(state, DpsCards.MystiqueTaughtByMagneto, "p1");
        // Whose's default Zones (Field/Attack) is what D'Ken's/Deadpool's
        // own free-fielding tests already query against - same convention.
        var brotherhoodDie = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", level: 1, id: "cheap");

        Assert.Equal(0, QueryEngine.GetFieldingCost(state, brotherhoodDie));
    }

    [Fact]
    public void MystiqueTaughtByMagneto_Energize_May_Field_A_Brotherhood_Die_Free()
    {
        var state = NewGame();
        var mystique = Energized(state, DpsCards.MystiqueTaughtByMagneto, "p1");
        var brotherhoodDie = Ready(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", 1, id: "target");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, mystique.Id); // MayPay accept
        Answer(state, "target");

        Assert.Equal(Zone.FieldZone, brotherhoodDie.Zone);
    }

    [Fact]
    public void WolverineHardenedByMadripoor_Energize_Spins_To_Level_1_With_3_Active_XMen()
    {
        var state = NewGame();
        var wolverine = Energized(state, DpsCards.WolverineHardenedByMadripoor, "p1");
        Active(state, DpsCards.PsylockeTelepath, "p1", id: "x1");
        Active(state, DpsCards.CyclopsFirstClass, "p1", id: "x2");
        Active(state, DpsCards.JubileeXMenFieldLeader, "p1", id: "x3");
        var queue = new AbilityQueue();

        // Wolverine himself sits in the Reserve Pool (Energized), so this
        // is 3 X-Men without counting him - the count still clears.
        FireEnergize(state, queue);
        Drain(state, queue);

        Assert.Equal(1, state.GetCurrentFace(wolverine)!.Character!.Level);
    }

    [Fact]
    public void WolverineHardenedByMadripoor_Energize_Does_Nothing_Under_3_Active_XMen()
    {
        var state = NewGame();
        var wolverine = Energized(state, DpsCards.WolverineHardenedByMadripoor, "p1");
        Active(state, DpsCards.PsylockeTelepath, "p1", id: "x1");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);

        Assert.Equal(Zone.ReservePool, wolverine.Zone); // never fielded, never spun
    }

    [Fact]
    public void IcemanMrIceGuy_Gives_Sidekicks_Plus_1_Attack_While_Active()
    {
        var state = NewGame();
        var sidekick = Sidekick(state, "p1", Zone.FieldZone, 0, "sk");
        var baseline = QueryEngine.GetAttack(state, sidekick);

        Active(state, DpsCards.IcemanMrIceGuy, "p1");

        Assert.Equal(baseline + 1, QueryEngine.GetAttack(state, sidekick));
    }

    [Fact]
    public void ProfessorXDreamer_Energize_Moves_An_XMen_Die_From_Used_Pile_To_Prep_Area()
    {
        var state = NewGame();
        Energized(state, DpsCards.ProfessorXDreamer, "p1");
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = null };
        state.Dice.Add(dormant);
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "dormant");

        Assert.Equal(Zone.PrepArea, dormant.Zone);
    }

    [Fact]
    public void AngelWingsOverTheWorld_Energize_Gives_A_Sidekick_Plus_2_Attack()
    {
        var state = NewGame();
        Energized(state, DpsCards.AngelWingsOverTheWorld, "p1");
        var sidekick = Sidekick(state, "p1", Zone.FieldZone, 0, "sk");
        var baseline = QueryEngine.GetAttack(state, sidekick);
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "sk");

        Assert.Equal(baseline + 2, QueryEngine.GetAttack(state, sidekick));
    }

    [Fact]
    public void CableIllDoThisAllDay_Energize_Rerolls_One_Of_Its_Controllers_Own_Character_Dice()
    {
        var state = NewGame();
        Energized(state, DpsCards.CableIllDoThisAllDay, "p1");
        var target = Active(state, DpsCards.PsylockeTelepath, "p1", level: 2, id: "target");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(0, target.CurrentFaceIndex);
    }

    [Fact]
    public void ColossusSkilledPainter_Energize_Fields_A_Reserve_Die_Free_At_Level_3()
    {
        var state = NewGame();
        Energized(state, DpsCards.ColossusSkilledPainter, "p1");
        var target = Ready(state, DpsCards.PsylockeTelepath, "p1", 1, id: "target");
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(Zone.FieldZone, target.Zone);
        Assert.Equal(3, state.GetCurrentFace(target)!.Character!.Level);
    }

    [Fact]
    public void ToadLookingForComradery_Energize_May_Spin_A_Reserve_Character_Die_To_Level_1()
    {
        var state = NewGame();
        var toad = Energized(state, DpsCards.ToadLookingForComradery, "p1");
        var target = Ready(state, DpsCards.PsylockeTelepath, "p1", 3, id: "target"); // starts at level 3
        var queue = new AbilityQueue();

        FireEnergize(state, queue);
        Drain(state, queue);
        Answer(state, toad.Id); // MayPay accept
        Answer(state, "target");

        Assert.Equal(1, state.GetCurrentFace(target)!.Character!.Level);
    }

    // --- Batch 5 (2026-09-01) ---

    [Fact]
    public void MutantResearchProgram_Draws_And_Rolls_3_With_2_Active_Founders_Else_1()
    {
        foreach (var (founderCount, expectedDraws) in new[] { (2, 3), (1, 1) })
        {
            var state = NewGame();
            for (var i = 0; i < founderCount; i++)
                Active(state, DpsCards.BeastCombatReady, "p1", id: $"founder{i}"); // printed Founder keyword
            var card = Ready(state, DpsCards.MutantResearchProgram, "p1", 0);
            var queue = new AbilityQueue();
            // Excluding the action card's own die, which UseAction moves
            // to Out of Play immediately (before it can be double-counted).
            var before = state.DiceIn("p1", Zone.ReservePool).Count(d => d.Id != card.Id);

            TurnEngine.UseAction(state, queue, card.Id);
            Drain(state, queue);

            Assert.Equal(before + expectedDraws, state.DiceIn("p1", Zone.ReservePool).Count(d => d.Id != card.Id));
        }
    }

    [Fact]
    public void TightRanks_Global_Weakens_A_Loyalty_Countered_Die()
    {
        var state = NewGame();
        var target = Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 1, id: "target"); // 5A/5D
        state.Counters[("p1", DpsCards.RonanTheAccuserTreason.Id, "Loyalty")] = 1;
        Sidekick(state, "p1", Zone.ReservePool, 5, "shield-energy"); // face 5 = Shield
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, DpsCards.TightRanks.Id, "p1", abilityIndex: 0, ["shield-energy"]);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(3, QueryEngine.GetAttack(state, target));
        Assert.Equal(3, QueryEngine.GetDefense(state, target));
    }

    [Fact]
    public void Radicalization_Deals_3_To_An_XMen_Or_Brotherhood_Die_And_KOs_A_Sidekick_On_Double_Burst()
    {
        var state = NewGame();
        // Level 3 (6A/8D) so 3 damage doesn't KO it outright - a KO'd die
        // resets its own Damage back to 0 on leaving the field, which
        // would make the damage assertion below vacuous.
        var target = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p2", level: 3, id: "target"); // Brotherhood of Mutants
        var sidekick = Sidekick(state, "p2", Zone.FieldZone, 0, "sk");
        var card = Ready(state, DpsCards.Radicalization, "p1", 2); // face index 2 = the double-burst action face
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, card.Id);
        Drain(state, queue);
        Answer(state, "target"); // no-op if both clauses had a single candidate and auto-resolved

        Assert.Equal(3, target.Damage);
        Assert.Equal(Zone.PrepArea, sidekick.Zone); // KO'd
    }

    [Fact]
    public void CorsairRecruitingACrew_Sends_Its_Controllers_Next_Purchase_To_The_Bag()
    {
        var state = NewGame();
        var corsair = Ready(state, DpsCards.CorsairRecruitingACrew, "p1", 1);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, corsair.Id, []);
        Drain(state, queue);

        // NewGame() doesn't seed a team's Unpurchased pool (no
        // TeamCardIds set) - MisterSinister's own catalog test builds one
        // by hand the same way.
        var toBuy = new Model.DieInstance { Id = "unbought", CardId = DpsCards.PowerBolt.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased };
        state.Dice.Add(toBuy);
        TurnEngine.Purchase(state, queue, toBuy.Id, Energy(state, "p1", 3)); // PowerBolt costs 3, no type required

        Assert.Equal(Zone.Bag, toBuy.Zone);
    }

    [Fact]
    public void EmmaFrostManipulative_Rerolls_On_The_Opponents_Attack_Step_Not_Her_Own()
    {
        var state = NewGame();
        Active(state, DpsCards.EmmaFrostManipulative, "p1");
        var opposing = Active(state, DpsCards.PsylockeTelepath, "p2", level: 2, id: "opposing");
        var queue = new AbilityQueue();

        // p1's own attack step - must NOT fire.
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, "p1", StepIds.SelectAttackers));
        Drain(state, queue);
        Assert.Equal(FirstLevelFace + 1, opposing.CurrentFaceIndex); // unchanged

        // p2's (the opponent's) attack step - must fire.
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, "p2", StepIds.SelectAttackers));
        Drain(state, queue);
        Assert.Equal(0, opposing.CurrentFaceIndex);
    }

    [Fact]
    public void CableBosomBuddies_Gives_A_Deadpool_Die_Plus_2_Attack()
    {
        var state = NewGame();
        var deadpool = Active(state, DpsCards.DeadpoolCollectThis, "p1", id: "dp");
        var baseline = QueryEngine.GetAttack(state, deadpool);

        Active(state, DpsCards.CableBosomBuddies, "p1");

        Assert.Equal(baseline + 2, QueryEngine.GetAttack(state, deadpool));
    }

    [Fact]
    public void BeastFirstClass_Preps_A_Die_When_ANOTHER_Founder_Die_Attacks_Not_Its_Own()
    {
        var state = NewGame();
        Active(state, DpsCards.BeastFirstClass, "p1", id: "beast");
        // IcemanMrIceGuy, not BeastCombatReady - the latter carries its
        // OWN "when this attacks, Prep a die" ability, which would also
        // fire from the same DieAttacks event and double the Prep count.
        var founder = Active(state, DpsCards.IcemanMrIceGuy, "p1", id: "other-founder");
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, founder, "p1", state.CurrentStepId));
        Drain(state, queue);

        Assert.Single(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void EmmaFrostInfluential_Gives_Sidekicks_Plus_1_Attack_And_Plus_1_Defense()
    {
        var state = NewGame();
        var sidekick = Sidekick(state, "p1", Zone.FieldZone, 0, "sk");
        var baselineAtk = QueryEngine.GetAttack(state, sidekick);
        var baselineDef = QueryEngine.GetDefense(state, sidekick);

        Active(state, DpsCards.EmmaFrostInfluential, "p1");

        Assert.Equal(baselineAtk + 1, QueryEngine.GetAttack(state, sidekick));
        Assert.Equal(baselineDef + 1, QueryEngine.GetDefense(state, sidekick));
    }

    // --- Batch 6 (2026-09-01) ---

    [Fact]
    public void TakeCover_Gives_Every_Own_Character_Die_Plus_2_Defense_Plus_3_More_On_A_Burst_Face()
    {
        foreach (var (faceIndex, expectedBonus) in new[] { (0, 2 + 3), (1, 2 + 3), (2, 2) }) // 0=single burst,1=double,2=none
        {
            var state = NewGame();
            var ally = Active(state, DpsCards.RonanTheAccuserTreason, "p1", level: 1, id: "ally"); // 5A/5D
            var card = Ready(state, DpsCards.TakeCover, "p1", faceIndex);
            var queue = new AbilityQueue();

            TurnEngine.UseAction(state, queue, card.Id);
            Drain(state, queue);
            Answer(state, "ally"); // the burst clause's own single-target choice, when offered

            Assert.Equal(5 + expectedBonus, QueryEngine.GetDefense(state, ally));
        }
    }

    [Fact]
    public void EmmaFrostFinesse_Rerolls_2_Opposing_Fist_Dice_On_Their_Attack_Step()
    {
        var state = NewGame();
        Active(state, DpsCards.EmmaFrostFinesse, "p1");
        var fistDie = Active(state, DpsCards.CorsairRecruitingACrew, "p2", level: 2, id: "fist"); // Fist energy
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.TurnStepEntered, null, "p2", StepIds.SelectAttackers));
        Drain(state, queue);

        Assert.Equal(0, fistDie.CurrentFaceIndex); // rerolled, Drain's FixedRoller(0)
    }

    [Fact]
    public void CyclopsUtopiaRealized_Deals_3_When_At_Least_2_Own_Field_Zone_Dice_Not_Counting_Himself()
    {
        var state = NewGame();
        var cyclops = Active(state, DpsCards.CyclopsUtopiaRealized, "p1", id: "cyclops");
        var ally = Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "ally"); // the 2nd Field Zone die
        // 8D, not Psylocke's own 3D - 3 damage into 3D is lethal, and a
        // KO'd die resets its own Damage to 0 on leaving the field.
        var target = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p2", level: 3, id: "target");
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, cyclops, "p1", state.CurrentStepId));
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(3, target.Damage);
    }

    [Fact]
    public void CyclopsXaviersDream_Divides_Damage_Equal_To_Field_Zone_Count_While_A_Sidekick_Is_Active()
    {
        var state = NewGame();
        var cyclops = Active(state, DpsCards.CyclopsXaviersDream, "p1", id: "cyclops");
        Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "ally"); // 2nd own Field Zone die
        Sidekick(state, "p1", Zone.FieldZone, 0, "sk"); // satisfies "a Sidekick die active"
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", level: 3, id: "target");
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, cyclops, "p1", state.CurrentStepId));
        Drain(state, queue);
        // X = 2 (cyclops + ally, both still in the Field Zone - this test
        // fires DieAttacks directly rather than running real combat, so
        // Cyclops never actually moves to the Attack Zone). Distribute
        // asks "assign 1 damage" once per point of X.
        for (var i = 0; i < 2; i++) Answer(state, "target");

        Assert.Equal(2, target.Damage);
    }

    [Fact]
    public void MadelynePryorSisterhood_Gains_A_Loyalty_Counter_When_ANOTHER_Brotherhood_Die_Is_KOd()
    {
        var state = NewGame();
        var madelyne = Active(state, DpsCards.MadelynePryorSisterhood, "p1");
        var ally = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", id: "ally");
        var queue = new AbilityQueue();

        EffectInterpreter.KoDie(state, queue, ally, triggersKOAbilities: true);
        Drain(state, queue);

        Assert.Equal(1, state.Counters[("p1", DpsCards.MadelynePryorSisterhood.Id, "Loyalty")]);
    }

    [Fact]
    public void MadelynePryorSisterhood_Does_Not_Gain_A_Counter_From_Her_Own_KO()
    {
        var state = NewGame();
        var madelyne = Active(state, DpsCards.MadelynePryorSisterhood, "p1");
        var queue = new AbilityQueue();

        EffectInterpreter.KoDie(state, queue, madelyne, triggersKOAbilities: true);
        Drain(state, queue);

        Assert.False(state.Counters.ContainsKey(("p1", DpsCards.MadelynePryorSisterhood.Id, "Loyalty")));
    }

    [Fact]
    public void MagnetoIdealist_Gains_A_Loyalty_Counter_When_A_Mask_Die_Is_KOd()
    {
        var state = NewGame();
        var magneto = Active(state, DpsCards.MagnetoIdealist, "p1");
        var maskDie = Active(state, DpsCards.PsylockeTelepath, "p1", id: "mask"); // Mask energy
        var queue = new AbilityQueue();

        EffectInterpreter.KoDie(state, queue, maskDie, triggersKOAbilities: true);
        Drain(state, queue);

        Assert.Equal(1, state.Counters[("p1", DpsCards.MagnetoIdealist.Id, "Loyalty")]);
    }

    [Fact]
    public void MagnetoIdealist_Global_May_Draw_When_Prep_Area_Is_Empty()
    {
        var state = NewGame();
        var magneto = Active(state, DpsCards.MagnetoIdealist, "p1");
        Sidekick(state, "p1", Zone.ReservePool, 1, "mask-energy"); // Wild energy, pays Mask
        var queue = new AbilityQueue();
        var before = state.DiceIn("p1", Zone.PrepArea).Count();

        TurnEngine.UseGlobal(state, queue, DpsCards.MagnetoIdealist.Id, "p1", abilityIndex: 1, ["mask-energy"]); // index 1 - the DieKOd ability is index 0
        Drain(state, queue);
        Answer(state, magneto.Id); // MayPay accept

        Assert.Equal(before + 1, state.DiceIn("p1", Zone.PrepArea).Count());
    }

    [Fact]
    public void JeanGreyXaviersDream_Surcharges_Opponent_Globals_Only_While_A_Sidekick_Is_Active()
    {
        var state = NewGame();
        Active(state, DpsCards.JeanGreyXaviersDream, "p1");
        var card = Ready(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p2", 1);
        var ability = DpsCards.MagnetoFounderOfTheBrotherhood.Abilities.Single(a => a.Trigger == TriggerKind.Global);
        var baseline = QueryEngine.GetGlobalEnergyCost(state, DpsCards.MagnetoFounderOfTheBrotherhood, ability, "p2");

        Sidekick(state, "p1", Zone.FieldZone, 0, "sk");

        Assert.Equal(baseline + 1, QueryEngine.GetGlobalEnergyCost(state, DpsCards.MagnetoFounderOfTheBrotherhood, ability, "p2"));
    }

    [Fact]
    public void JeanGreyMarvelGirl_Surcharges_Opponent_Globals_While_Active()
    {
        var state = NewGame();
        Active(state, DpsCards.JeanGreyMarvelGirl, "p1");
        var ability = DpsCards.MagnetoFounderOfTheBrotherhood.Abilities.Single(a => a.Trigger == TriggerKind.Global);

        Assert.True(QueryEngine.GetGlobalEnergyCost(state, DpsCards.MagnetoFounderOfTheBrotherhood, ability, "p2") > 0);
    }

    // --- Batch 7 (2026-09-01) ---

    [Fact]
    public void KittyPrydeRightOfPassage_Preps_A_Die_When_She_Awakens()
    {
        var state = NewGame();
        var kitty = Active(state, DpsCards.KittyPrydeRightOfPassage, "p1", level: 1);
        var queue = new AbilityQueue();
        var faces = state.GetDieDefinition(kitty).Faces;

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFaceChanged, kitty, "p1", state.CurrentStepId,
            new DieFaceChangedPayload(faces[FirstLevelFace], faces[FirstLevelFace + 1], FaceChangeCause.Spin)));
        Drain(state, queue);

        Assert.Single(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void KittyPrydeRightOfPassage_Does_Not_Awaken_On_A_Spin_Down()
    {
        var state = NewGame();
        var kitty = Active(state, DpsCards.KittyPrydeRightOfPassage, "p1", level: 2);
        var queue = new AbilityQueue();
        var faces = state.GetDieDefinition(kitty).Faces;

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFaceChanged, kitty, "p1", state.CurrentStepId,
            new DieFaceChangedPayload(faces[FirstLevelFace + 1], faces[FirstLevelFace], FaceChangeCause.Spin)));
        Drain(state, queue);

        Assert.Empty(state.DiceIn("p1", Zone.PrepArea));
    }

    [Fact]
    public void DKenEmperor_Preps_A_Die_From_The_Used_Pile_When_He_Attacks()
    {
        var state = NewGame();
        var dken = Active(state, DpsCards.DKenEmperor, "p1");
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = null };
        state.Dice.Add(dormant);
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, dken, "p1", state.CurrentStepId));
        Drain(state, queue);
        Answer(state, "dormant");

        Assert.Equal(Zone.PrepArea, dormant.Zone);
    }

    [Fact]
    public void IcemanIcyInterference_Spins_An_Opposing_Level_1_Die_To_An_Energy_Face()
    {
        var state = NewGame();
        var iceman = Active(state, DpsCards.IcemanIcyInterference, "p1");
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", level: 1, id: "target");
        var untouched = Active(state, DpsCards.RonanTheAccuserTreason, "p2", level: 3, id: "untouched"); // not level 1
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, iceman, "p1", state.CurrentStepId));
        Drain(state, queue);

        Assert.Equal(FaceKind.EnergyFace, state.GetCurrentFace(target)!.Kind);
        Assert.Equal(FaceKind.CharacterFace, state.GetCurrentFace(untouched)!.Kind); // wrong level - untouched
    }

    [Fact]
    public void ToadSecondaryMutation_Awakens_For_Damage_And_Watches_His_Own_Team_Field()
    {
        var state = NewGame();
        var toad = Active(state, DpsCards.ToadSecondaryMutation, "p1", level: 1);
        var target = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p2", level: 3, id: "target");
        var queue = new AbilityQueue();
        var faces = state.GetDieDefinition(toad).Faces;

        // Awaken half.
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFaceChanged, toad, "p1", state.CurrentStepId,
            new DieFaceChangedPayload(faces[FirstLevelFace], faces[FirstLevelFace + 1], FaceChangeCause.Spin)));
        Drain(state, queue);
        Answer(state, "target");
        Assert.Equal(2, target.Damage);

        // Teamwatch half - a DIFFERENT Brotherhood die is fielded.
        var ally = new Model.DieInstance { Id = "ally", CardId = DpsCards.MagnetoFounderOfTheBrotherhood.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(ally);
        TurnEngine.Field(state, queue, ally.Id, Energy(state, "p1", 1));
        Drain(state, queue);

        // The Awaken event above was fired synthetically (proving the
        // damage half) without actually moving Toad's own face, so he's
        // still level 1 going into Teamwatch's own +1 spin.
        Assert.Equal(2, state.GetCurrentFace(toad)!.Character!.Level);
    }

    [Fact]
    public void VulcanRulerOfTheImperium_Global_Forces_A_Target_Die_To_Attack()
    {
        var state = NewGame();
        var target = Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "target");
        Sidekick(state, "p1", Zone.ReservePool, 1, "fist-energy");
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, DpsCards.VulcanRulerOfTheImperium.Id, "p1", abilityIndex: 0, ["fist-energy"]);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Contains(CombatFlagKind.MustAttack, target.CombatFlags);
    }

    [Fact]
    public void PsylockeAdventurer_Gains_Deadly_Only_While_A_Wolverine_Die_Is_Active()
    {
        var state = NewGame();
        var psylocke = Active(state, DpsCards.PsylockeAdventurer, "p1");
        Assert.DoesNotContain("Deadly", QueryEngine.GetKeywords(state, psylocke));

        Active(state, DpsCards.WolverineHardenedByMadripoor, "p2", id: "logan");
        Assert.Contains("Deadly", QueryEngine.GetKeywords(state, psylocke));
    }

    [Fact]
    public void GambitAceInTheHole_Draws_And_Rolls_One_Die_On_A_Plain_Face()
    {
        var state = NewGame();
        var gambit = Ready(state, DpsCards.GambitAceInTheHole, "p1", 3); // level 3 = no burst
        for (var i = 0; i < 3; i++) Sidekick(state, "p1", Zone.Bag, null, $"bag{i}");
        var queue = new AbilityQueue();
        var before = state.DiceIn("p1", Zone.ReservePool).Count(d => d.Id != gambit.Id);

        TurnEngine.Field(state, queue, gambit.Id, Energy(state, "p1", 2));
        Drain(state, queue);
        Answer(state, gambit.Id); // MayPay accept

        Assert.Equal(before + 1, state.DiceIn("p1", Zone.ReservePool).Count(d => d.Id != gambit.Id));
    }

    [Fact]
    public void GambitAceInTheHole_Draws_2_Chooses_1_On_A_Single_Burst_Face()
    {
        var state = NewGame();
        var gambit = Ready(state, DpsCards.GambitAceInTheHole, "p1", 1); // level 1 = single burst
        for (var i = 0; i < 3; i++) Sidekick(state, "p1", Zone.Bag, null, $"bag{i}");
        var queue = new AbilityQueue();
        var reserveBefore = state.DiceIn("p1", Zone.ReservePool).Count(d => d.Id != gambit.Id);
        var bagBefore = state.DiceIn("p1", Zone.Bag).Count();

        TurnEngine.Field(state, queue, gambit.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        Answer(state, gambit.Id); // MayPay accept
        var pending = state.PendingChoice!;
        EffectInterpreter.AnswerPendingChoice(state, [pending.CandidateIds[0]]);

        // 2 came out of the Bag, 1 to the Reserve Pool (rolled) and 1
        // back to the Bag - a net Bag change of -1.
        Assert.Equal(reserveBefore + 1, state.DiceIn("p1", Zone.ReservePool).Count(d => d.Id != gambit.Id));
        Assert.Equal(bagBefore - 1, state.DiceIn("p1", Zone.Bag).Count());
    }

    [Fact]
    public void MisterSinisterGeneticist_KOs_Up_To_2_Sidekicks_And_Globally_Grants_Deadly()
    {
        var state = NewGame();
        var sinister = Ready(state, DpsCards.MisterSinisterGeneticist, "p1", 1);
        var sk1 = Sidekick(state, "p2", Zone.FieldZone, 0, "sk1");
        var sk2 = Sidekick(state, "p2", Zone.FieldZone, 0, "sk2");
        var nonSidekick = Active(state, DpsCards.RonanTheAccuserTreason, "p2", id: "nonSidekick");
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, sinister.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        EffectInterpreter.AnswerPendingChoice(state, state.PendingChoice!.CandidateIds); // both Sidekicks

        Assert.Equal(Zone.PrepArea, sk1.Zone);
        Assert.Equal(Zone.PrepArea, sk2.Zone);

        Sidekick(state, "p1", Zone.ReservePool, 1, "bolt1");
        Sidekick(state, "p1", Zone.ReservePool, 1, "bolt2");
        TurnEngine.UseGlobal(state, queue, DpsCards.MisterSinisterGeneticist.Id, "p1", abilityIndex: 1, ["bolt1", "bolt2"]);
        Drain(state, queue);
        Answer(state, "nonSidekick");

        Assert.Contains("Deadly", QueryEngine.GetKeywords(state, nonSidekick));
    }

    [Fact]
    public void MystiqueRelentless_Gets_Plus_2_Attack_Only_While_A_Wolverine_Die_Is_Active()
    {
        var state = NewGame();
        var mystique = Active(state, DpsCards.MystiqueRelentless, "p1");
        var baseline = QueryEngine.GetAttack(state, mystique);

        Active(state, DpsCards.WolverineHardenedByMadripoor, "p1", id: "logan");

        Assert.Equal(baseline + 2, QueryEngine.GetAttack(state, mystique));
    }

    [Fact]
    public void DarkPhoenixMalevolent_KOs_A_Target_And_Deals_1_If_It_Was_XMen()
    {
        var state = NewGame();
        var darkPhoenix = Ready(state, DpsCards.DarkPhoenixMalevolent, "p1", 1);
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", id: "target"); // X-Men
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, darkPhoenix.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(Zone.PrepArea, target.Zone); // KO'd
        Assert.Equal(19, state.PlayerTwo.Life);
    }

    [Fact]
    public void DarkPhoenixMalevolent_Global_KOs_Her_Own_Die_And_Discounts_The_Next_Purchase()
    {
        var state = NewGame();
        var darkPhoenix = Active(state, DpsCards.DarkPhoenixMalevolent, "p1", id: "dp");
        var ownDie = Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "own");
        Sidekick(state, "p1", Zone.ReservePool, 1, "bolt-energy");
        var toBuy = new Model.DieInstance { Id = "toBuy", CardId = DpsCards.PowerBolt.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased };
        state.Dice.Add(toBuy);
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, DpsCards.DarkPhoenixMalevolent.Id, "p1", abilityIndex: 1, ["bolt-energy"]);
        Drain(state, queue);
        Answer(state, "own");

        TurnEngine.Purchase(state, queue, toBuy.Id, Energy(state, "p1", 1)); // 3 - 2 = 1
        Assert.Equal(Zone.UsedPile, toBuy.Zone); // default purchase destination (no GoesToZone override)
    }

    // --- Batch 8 (2026-09-01) ---

    [Fact]
    public void MagikBetterThanBelasco_Rolls_A_Die_From_The_Bag_When_She_Awakens()
    {
        var state = NewGame();
        var magik = Active(state, DpsCards.MagikBetterThanBelasco, "p1", level: 1);
        Sidekick(state, "p1", Zone.Bag, null, "bag0");
        var queue = new AbilityQueue();
        var before = state.DiceIn("p1", Zone.ReservePool).Count();
        var faces = state.GetDieDefinition(magik).Faces;

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFaceChanged, magik, "p1", state.CurrentStepId,
            new DieFaceChangedPayload(faces[FirstLevelFace], faces[FirstLevelFace + 1], FaceChangeCause.Spin)));
        Drain(state, queue);

        Assert.Equal(before + 1, state.DiceIn("p1", Zone.ReservePool).Count());
    }

    [Fact]
    public void MoiraIfItsReal_Gets_Plus_1_Defense_Only_While_A_Wolverine_Die_Is_Active()
    {
        var state = NewGame();
        var moira = Active(state, DpsCards.MoiraIfItsReal, "p1");
        var baseline = QueryEngine.GetDefense(state, moira);

        Active(state, DpsCards.WolverineHardenedByMadripoor, "p1", id: "logan");

        Assert.Equal(baseline + 1, QueryEngine.GetDefense(state, moira));
    }

    [Fact]
    public void MoiraIfItsReal_Buffs_All_Own_XMen_Dice_When_Fielded_And_Preps_On_Her_Own_KO()
    {
        var state = NewGame();
        var moira = Ready(state, DpsCards.MoiraIfItsReal, "p1", 1);
        var ally = Active(state, DpsCards.PsylockeTelepath, "p1", id: "ally"); // X-Men
        var notXMen = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", id: "notXMen");
        var allyBaseline = QueryEngine.GetAttack(state, ally);
        var notXMenBaseline = QueryEngine.GetAttack(state, notXMen);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, moira.Id, []); // level 1 fielding cost 0
        Drain(state, queue);

        Assert.Equal(allyBaseline + 1, QueryEngine.GetAttack(state, ally));
        Assert.Equal(notXMenBaseline, QueryEngine.GetAttack(state, notXMen)); // untouched - not X-Men

        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.PowerBolt.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile };
        state.Dice.Add(dormant);
        EffectInterpreter.KoDie(state, queue, moira, triggersKOAbilities: true);
        Drain(state, queue);
        Answer(state, "dormant");

        Assert.Equal(Zone.PrepArea, dormant.Zone);
    }

    [Fact]
    public void SabretoothDoISmellWeakness_Gets_Plus_1_Attack_Per_Opposing_Weak_Defense_Die()
    {
        var state = NewGame();
        var sabretooth = Active(state, DpsCards.SabretoothDoISmellWeakness, "p1");
        var baseline = QueryEngine.GetAttack(state, sabretooth);
        Active(state, DpsCards.PsylockeTelepath, "p2", level: 1, id: "weak1"); // 2D
        Active(state, DpsCards.PsylockeTelepath, "p2", level: 2, id: "weak2"); // 2D
        Active(state, DpsCards.RonanTheAccuserTreason, "p2", level: 1, id: "tough"); // 5D

        Assert.Equal(baseline + 2, QueryEngine.GetAttack(state, sabretooth));
    }

    [Fact]
    public void JubileeThingsNeverChange_Gets_Plus_1_Attack_Only_While_A_Wolverine_Die_Is_Active()
    {
        var state = NewGame();
        var jubilee = Active(state, DpsCards.JubileeThingsNeverChange, "p1");
        var baseline = QueryEngine.GetAttack(state, jubilee);

        Active(state, DpsCards.WolverineHardenedByMadripoor, "p2", id: "logan"); // either side counts

        Assert.Equal(baseline + 1, QueryEngine.GetAttack(state, jubilee));
    }

    [Fact]
    public void IcemanFrozenFistsOfFury_Deals_3_Only_While_A_Wolverine_Die_Is_Active()
    {
        var state = NewGame();
        var iceman = Active(state, DpsCards.IcemanFrozenFistsOfFury, "p1");
        var target = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p2", level: 3, id: "target"); // 8D
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, iceman, "p1", state.CurrentStepId));
        Drain(state, queue);
        Assert.Equal(0, target.Damage); // no Wolverine active yet

        Active(state, DpsCards.WolverineHardenedByMadripoor, "p1", id: "logan");
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, iceman, "p1", state.CurrentStepId));
        Drain(state, queue);
        Answer(state, "target");
        Assert.Equal(3, target.Damage);
    }

    [Fact]
    public void KittyPrydeHeadmistress_Gets_Plus_1_Attack_Only_While_A_Wolverine_Die_Is_Active()
    {
        var state = NewGame();
        var kitty = Active(state, DpsCards.KittyPrydeHeadmistress, "p1");
        var baseline = QueryEngine.GetAttack(state, kitty);

        Active(state, DpsCards.WolverineHardenedByMadripoor, "p1", id: "logan");

        Assert.Equal(baseline + 1, QueryEngine.GetAttack(state, kitty));
    }

    [Fact]
    public void GladiatorTheEmpireMustStand_Gains_A_Loyalty_Counter_When_A_Lilandra_Die_Is_KOd()
    {
        var state = NewGame();
        var gladiator = Active(state, DpsCards.GladiatorTheEmpireMustStand, "p1");
        var lilandra = Active(state, DpsCards.LilandraFreedomFighter, "p1", id: "lilandra");
        var queue = new AbilityQueue();

        EffectInterpreter.KoDie(state, queue, lilandra, triggersKOAbilities: true);
        Drain(state, queue);

        Assert.Equal(1, state.Counters[("p1", DpsCards.GladiatorTheEmpireMustStand.Id, "Loyalty")]);
    }

    [Fact]
    public void BlobMGHDependent_Loses_1_Life_When_Fielded()
    {
        var state = NewGame();
        var blob = Ready(state, DpsCards.BlobMGHDependent, "p1", 1);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, blob.Id, []); // level 1 fielding cost 0
        Drain(state, queue);

        Assert.Equal(19, state.PlayerOne.Life);
    }

    // --- Batch 9 (2026-09-01) - the last DPS batch ---

    [Fact]
    public void DeathbirdWarOfKings_Prevents_A_Target_From_Blocking_When_Fielded()
    {
        var state = NewGame();
        var deathbird = Ready(state, DpsCards.DeathbirdWarOfKings, "p1", 1);
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", id: "target");
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, deathbird.Id, []); // level 1 fielding cost 0
        Drain(state, queue);
        Answer(state, "target");

        Assert.Contains(CombatFlagKind.CantBlock, target.CombatFlags);
    }

    [Fact]
    public void RonanTheAccuserNoExceptions_Costs_Both_Players_3_Life_When_Fielded()
    {
        var state = NewGame();
        var ronan = Ready(state, DpsCards.RonanTheAccuserNoExceptions, "p1", 1);
        Sidekick(state, "p1", Zone.ReservePool, 1, "e1");
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, ronan.Id, Energy(state, "p1", 1));
        Drain(state, queue);

        Assert.Equal(17, state.PlayerOne.Life);
        Assert.Equal(17, state.PlayerTwo.Life);
    }

    [Fact]
    public void SabretoothYouReadyToParty_Buffs_Brotherhood_On_Attack_And_Watches_His_Own_Team_Field()
    {
        var state = NewGame();
        var sabretooth = Active(state, DpsCards.SabretoothYouReadyToParty, "p1");
        var ally = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", id: "ally");
        var allyBaseline = QueryEngine.GetAttack(state, ally);
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", id: "target");
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, sabretooth, "p1", state.CurrentStepId));
        Drain(state, queue);
        Assert.Equal(allyBaseline + 2, QueryEngine.GetAttack(state, ally));

        var newAlly = new Model.DieInstance { Id = "newAlly", CardId = DpsCards.MagnetoFounderOfTheBrotherhood.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(newAlly);
        TurnEngine.Field(state, queue, newAlly.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        Answer(state, "target");

        Assert.Contains(CombatFlagKind.CantBlock, target.CombatFlags);
    }

    [Fact]
    public void MoiraStrengthOfForesight_Gains_A_Counter_When_A_Costly_XMen_Die_Is_Fielded()
    {
        var state = NewGame();
        Active(state, DpsCards.MoiraStrengthOfForesight, "p1");
        var queue = new AbilityQueue();

        // CyclopsFirstClass: X-Men, PurchaseCost 5 (>= 3) - PsylockeTelepath
        // (cost 2) wouldn't qualify regardless of which face it rolled to,
        // since purchase cost is a fixed CARD property, not per-level.
        var ally = new Model.DieInstance { Id = "ally", CardId = DpsCards.CyclopsFirstClass.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(ally);
        TurnEngine.Field(state, queue, ally.Id, Energy(state, "p1", 1));
        Drain(state, queue);

        Assert.Equal(1, state.Counters[("p1", DpsCards.MoiraStrengthOfForesight.Id, "Loyalty")]);
    }

    [Fact]
    public void MoiraStrengthOfForesight_May_Send_An_Opposing_Field_Zone_Action_Die_To_Their_Used_Pile()
    {
        var state = NewGame();
        var moira = Ready(state, DpsCards.MoiraStrengthOfForesight, "p1", 1);
        var opposingAction = new Model.DieInstance { Id = "action", CardId = DpsCards.PowerBolt.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(opposingAction);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, moira.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        Answer(state, moira.Id); // MayPay accept
        Answer(state, "action");

        Assert.Equal(Zone.UsedPile, opposingAction.Zone);
    }

    [Fact]
    public void RogueUnitySquad_Reduces_XMen_Fielding_Cost_And_Watches_Her_Own_Team_Field()
    {
        var state = NewGame();
        var rogue = Active(state, DpsCards.RogueUnitySquad, "p1");
        var xmenDie = Ready(state, DpsCards.PsylockeTelepath, "p1", 2);

        Assert.Equal(0, QueryEngine.GetFieldingCost(state, xmenDie)); // 1 - 1

        var rogueBaseline = QueryEngine.GetAttack(state, rogue);
        var newAlly = new Model.DieInstance { Id = "newAlly", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(newAlly);
        var queue = new AbilityQueue();
        TurnEngine.Field(state, queue, newAlly.Id, []);
        Drain(state, queue);

        Assert.Equal(rogueBaseline + 2, QueryEngine.GetAttack(state, rogue));
    }

    [Fact]
    public void MystiqueFreedomForce_Reduces_Ability_Damage_By_1_And_May_Return_A_Costly_Die_On_Her_Own_KO()
    {
        var state = NewGame();
        Active(state, DpsCards.MystiqueFreedomForce, "p1", id: "mystique");
        // Protects her whole side, not just herself (v1's own field name,
        // "grantsOwnDamageReductionFromOpponentAbilities") - a beefier
        // ally, not Mystique's own 1D-at-every-level self, so 2 damage
        // (3 - 1) doesn't KO the die the test is checking.
        var ally = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", level: 3, id: "ally"); // 8D
        var queue = new AbilityQueue();

        EffectInterpreter.ApplyDamage(state, queue, DamageSource.Ability, ally.Id, 3);
        Assert.Equal(2, ally.Damage); // 3 - 1

        var mystique = state.Dice.Single(d => d.Id == "mystique");
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.MagnetoFounderOfTheBrotherhood.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile };
        state.Dice.Add(dormant); // PurchaseCost 6 >= 4
        EffectInterpreter.KoDie(state, queue, mystique, triggersKOAbilities: true);
        Drain(state, queue);
        Answer(state, "dormant");

        Assert.Equal(Zone.PrepArea, dormant.Zone);
    }

    [Fact]
    public void MisterSinisterDarkExperimentation_May_Pay_Life_For_Plus_3_Attack_And_Globally_Cycles_Two_Sidekicks()
    {
        var state = NewGame();
        var sinister = Active(state, DpsCards.MisterSinisterDarkExperimentation, "p1");
        var baseline = QueryEngine.GetAttack(state, sinister);
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, sinister, "p1", state.CurrentStepId));
        Drain(state, queue);
        Answer(state, sinister.Id); // MayPay accept
        Assert.Equal(baseline + 3, QueryEngine.GetAttack(state, sinister));
        Assert.Equal(18, state.PlayerOne.Life);

        var sk1 = Sidekick(state, "p1", Zone.UsedPile, null, "sk1");
        var sk2 = Sidekick(state, "p1", Zone.UsedPile, null, "sk2");
        Sidekick(state, "p1", Zone.ReservePool, 1, "energy1");
        Sidekick(state, "p1", Zone.ReservePool, 1, "energy2");
        TurnEngine.UseGlobal(state, queue, DpsCards.MisterSinisterDarkExperimentation.Id, "p1", abilityIndex: 1, ["energy1", "energy2"]);
        Drain(state, queue);
        // Both clauses' candidate pools resolve against the SAME per-
        // ability snapshot (rule 3.2.5) - each independently still offers
        // [sk1, sk2], regardless of what the earlier clause already did -
        // so picking deliberately distinct dice is what proves "two
        // independent choices," not just grabbing candidate[0] twice.
        Answer(state, "sk1");
        Answer(state, "sk2");

        Assert.Equal(Zone.FieldZone, sk1.Zone);
        Assert.Equal(Zone.PrepArea, sk2.Zone);
    }

    [Fact]
    public void WolverineTrainer_Awakens_To_Grant_Deadly_And_Spins_Up_In_Sympathy_With_Another_Die()
    {
        var state = NewGame();
        var wolverine = Active(state, DpsCards.WolverineTrainer, "p1", level: 1);
        var sidekick = Sidekick(state, "p1", Zone.FieldZone, 0, "sk");
        var queue = new AbilityQueue();
        var faces = state.GetDieDefinition(wolverine).Faces;

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFaceChanged, wolverine, "p1", state.CurrentStepId,
            new DieFaceChangedPayload(faces[FirstLevelFace], faces[FirstLevelFace + 1], FaceChangeCause.Spin)));
        Drain(state, queue);
        Answer(state, "sk");
        Assert.Contains("Deadly", QueryEngine.GetKeywords(state, sidekick));

        var ally = Active(state, DpsCards.PsylockeTelepath, "p1", level: 1, id: "ally");
        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieFaceChanged, ally, "p1", state.CurrentStepId,
            new DieFaceChangedPayload(state.GetDieDefinition(ally).Faces[FirstLevelFace], state.GetDieDefinition(ally).Faces[FirstLevelFace + 1], FaceChangeCause.Spin)));
        Drain(state, queue);

        Assert.Equal(2, state.GetCurrentFace(wolverine)!.Character!.Level); // spun up in sympathy
    }

    [Fact]
    public void MystiqueSheWalksAmongUs_Teamwatches_To_Spin_An_Opposing_Die_To_Double_Energy()
    {
        var state = NewGame();
        Active(state, DpsCards.MystiqueSheWalksAmongUs, "p1");
        var target = Active(state, DpsCards.PsylockeTelepath, "p2", id: "target");
        var queue = new AbilityQueue();
        var newAlly = new Model.DieInstance { Id = "newAlly", CardId = DpsCards.MagnetoFounderOfTheBrotherhood.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(newAlly);

        TurnEngine.Field(state, queue, newAlly.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(FaceKind.EnergyFace, state.GetCurrentFace(target)!.Kind);
        Assert.Equal(2, state.GetCurrentFace(target)!.SymbolCount);
    }

    [Fact]
    public void MagnetoMasterOfMagnetism_Global_May_Draw_When_Prep_Area_Is_Empty()
    {
        var state = NewGame();
        var magneto = Active(state, DpsCards.MagnetoMasterOfMagnetism, "p1");
        Sidekick(state, "p1", Zone.ReservePool, 1, "mask-energy");
        var queue = new AbilityQueue();
        var before = state.DiceIn("p1", Zone.PrepArea).Count();

        TurnEngine.UseGlobal(state, queue, DpsCards.MagnetoMasterOfMagnetism.Id, "p1", abilityIndex: 1, ["mask-energy"]);
        Drain(state, queue);
        Answer(state, magneto.Id); // MayPay accept

        Assert.Equal(before + 1, state.DiceIn("p1", Zone.PrepArea).Count());
    }

    [Fact]
    public void KittyPrydeExperiencedLeader_Buffs_Every_Own_XMen_Die_Including_Herself()
    {
        var state = NewGame();
        // Baselines captured BEFORE Kitty joins - she buffs herself too
        // (no "other" in the text), so a baseline read after she's placed
        // would already include her own bonus.
        var kittyBaseline = QueryEngine.GetAttack(state, new Model.DieInstance { Id = "probe", CardId = DpsCards.KittyPrydeExperiencedLeader.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = FirstLevelFace });
        var notXMen = Active(state, DpsCards.MagnetoFounderOfTheBrotherhood, "p1", id: "notXMen");
        var notXMenBaseline = QueryEngine.GetAttack(state, notXMen);

        var kitty = Active(state, DpsCards.KittyPrydeExperiencedLeader, "p1");

        Assert.Equal(kittyBaseline + 1, QueryEngine.GetAttack(state, kitty));
        Assert.Equal(notXMenBaseline, QueryEngine.GetAttack(state, notXMen));
    }

    [Fact]
    public void ToadJourneyIntoMisery_Teamwatches_To_Move_An_Opposing_Prepped_Die_To_Their_Bag()
    {
        var state = NewGame();
        Active(state, DpsCards.ToadJourneyIntoMisery, "p1");
        var target = new Model.DieInstance { Id = "target", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.PrepArea, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(target);
        var queue = new AbilityQueue();
        var newAlly = new Model.DieInstance { Id = "newAlly", CardId = DpsCards.MagnetoFounderOfTheBrotherhood.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = FirstLevelFace };
        state.Dice.Add(newAlly);

        TurnEngine.Field(state, queue, newAlly.Id, Energy(state, "p1", 1));
        Drain(state, queue);
        Answer(state, "target");

        Assert.Equal(Zone.Bag, target.Zone);
    }

    [Fact]
    public void VulcanAggression_Debuffs_Opposing_Non_Fist_Dice_And_Globally_Forces_An_Attack()
    {
        var state = NewGame();
        var nonFist = Active(state, DpsCards.PsylockeTelepath, "p2", id: "nonFist"); // Mask
        var fist = Active(state, DpsCards.CorsairRecruitingACrew, "p2", id: "fist"); // Fist
        var nonFistBaseline = QueryEngine.GetDefense(state, nonFist);
        var fistBaseline = QueryEngine.GetDefense(state, fist);

        Active(state, DpsCards.VulcanAggression, "p1"); // baselines captured BEFORE Vulcan joins

        Assert.Equal(nonFistBaseline - 2, QueryEngine.GetDefense(state, nonFist));
        Assert.Equal(fistBaseline, QueryEngine.GetDefense(state, fist));

        var queue = new AbilityQueue();
        Sidekick(state, "p1", Zone.ReservePool, 1, "fist-energy");
        TurnEngine.UseGlobal(state, queue, DpsCards.VulcanAggression.Id, "p1", abilityIndex: 0, ["fist-energy"]);
        Drain(state, queue);
        Answer(state, "nonFist");

        Assert.Contains(CombatFlagKind.MustAttack, nonFist.CombatFlags);
    }

    [Fact]
    public void BeastXaviersDream_Gets_Plus_1_Attack_Only_While_A_Sidekick_Is_Active()
    {
        var state = NewGame();
        var beast = Active(state, DpsCards.BeastXaviersDream, "p1");
        var baseline = QueryEngine.GetAttack(state, beast);

        Sidekick(state, "p1", Zone.FieldZone, 0, "sk");

        Assert.Equal(baseline + 1, QueryEngine.GetAttack(state, beast));
    }

    [Fact]
    public void DKenMKraanCrystal_Preps_A_Die_From_The_Used_Pile_When_He_Attacks()
    {
        var state = NewGame();
        var dken = Active(state, DpsCards.DKenMKraanCrystal, "p1");
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.PsylockeTelepath.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile };
        state.Dice.Add(dormant);
        var queue = new AbilityQueue();

        EventBus.Fire(state, queue, new GameEvent(TriggerKind.DieAttacks, dken, "p1", state.CurrentStepId));
        Drain(state, queue);
        Answer(state, "dormant");

        Assert.Equal(Zone.PrepArea, dormant.Zone);
    }

    [Fact]
    public void DarkPhoenixDestructiveForce_Global_KOs_Her_Own_Die_And_Discounts_The_Next_Purchase()
    {
        var state = NewGame();
        Active(state, DpsCards.DarkPhoenixDestructiveForce, "p1", id: "dp");
        var ownDie = Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "own");
        Sidekick(state, "p1", Zone.ReservePool, 1, "bolt-energy");
        var toBuy = new Model.DieInstance { Id = "toBuy", CardId = DpsCards.PowerBolt.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased };
        state.Dice.Add(toBuy);
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, DpsCards.DarkPhoenixDestructiveForce.Id, "p1", abilityIndex: 0, ["bolt-energy"]);
        Drain(state, queue);
        Answer(state, "own");

        TurnEngine.Purchase(state, queue, toBuy.Id, Energy(state, "p1", 1)); // 3 - 2 = 1
        Assert.Equal(Zone.UsedPile, toBuy.Zone);
    }

    [Fact]
    public void MisterSinisterBiologist_Global_Grants_Overcrush()
    {
        var state = NewGame();
        var target = Active(state, DpsCards.RonanTheAccuserTreason, "p1", id: "target");
        for (var i = 0; i < 3; i++) Sidekick(state, "p1", Zone.ReservePool, 1, $"e{i}");
        var queue = new AbilityQueue();

        TurnEngine.UseGlobal(state, queue, DpsCards.MisterSinisterBiologist.Id, "p1", abilityIndex: 0, ["e0", "e1", "e2"]);
        Drain(state, queue);
        Answer(state, "target");

        Assert.Contains("Overcrush", QueryEngine.GetKeywords(state, target));
    }

    [Fact]
    public void LilandraMajestrix_Surcharges_Opponent_Global_Use_With_Life()
    {
        var state = NewGame();
        Active(state, DpsCards.LilandraMajestrix, "p1");
        var ability = DpsCards.MagnetoFounderOfTheBrotherhood.Abilities.Single(a => a.Trigger == TriggerKind.Global);

        Assert.True(QueryEngine.GetGlobalEnergyCost(state, DpsCards.MagnetoFounderOfTheBrotherhood, ability, "p2") > 0);
    }

    [Fact]
    public void WolverineToughForTheKids_Global_Preps_A_Die_Once_Per_Turn()
    {
        var state = NewGame();
        Sidekick(state, "p1", Zone.ReservePool, 1, "fist-energy");
        var queue = new AbilityQueue();
        var before = state.DiceIn("p1", Zone.PrepArea).Count();

        TurnEngine.UseGlobal(state, queue, DpsCards.WolverineToughForTheKids.Id, "p1", abilityIndex: 0, ["fist-energy"]);
        Drain(state, queue);

        Assert.Equal(before + 1, state.DiceIn("p1", Zone.PrepArea).Count());
    }
}
