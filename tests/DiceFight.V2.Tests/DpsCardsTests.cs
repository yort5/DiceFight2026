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
        // Ronan's die: face 0 energy, faces 1..3 are levels 1..3.
        var dormant = new Model.DieInstance { Id = "dormant", CardId = DpsCards.RonanTheAccuserTreason.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.UsedPile, CurrentFaceIndex = null };
        state.Dice.Add(dormant);
        var making = Ready(state, DpsCards.MakingTheTeam, "p1", 0);
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, making.Id);
        Drain(state, queue, rollIndex: 3); // rolls its level-3 face

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
}
