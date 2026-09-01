using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 8 task 2 acceptance: the curated teams are migrated
// with passing behavior tests, exercised through the real firing path
// (ground rule 6) - TurnEngine actions -> EventBus -> AbilityQueue ->
// EffectInterpreter.DrainQueue, not direct EffectContext invocation.
public class CardCatalogTests
{
    // Migrated dice are the real six: three energy faces (0-2), then one
    // face per level. Tests name a level; only this knows the index.
    private static int LevelFace(int level) => 3 + level - 1;

    private static readonly IReadOnlyDictionary<string, Model.CardDef> Catalog = CardCatalog.BuildCatalog();

    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    [Fact]
    public void The_Curated_Catalog_Is_Valid_Against_The_Classic_Config()
    {
        var errors = DiceFightClassicConfig.Config.ValidateCatalog(Catalog.Values.ToList());
        Assert.Empty(errors);
    }

    [Fact]
    public void Team_Rosters_Total_Ten_Cards_Each()
    {
        Assert.Equal(8, CardCatalog.TeamACharacterIds.Count);
        Assert.Equal(2, CardCatalog.TeamABasicActionIds.Count);
        Assert.Equal(8, CardCatalog.TeamBCharacterIds.Count);
        Assert.Equal(2, CardCatalog.TeamBBasicActionIds.Count);
    }

    private static GameState NewTestGame(out Player playerOne, out Player playerTwo)
    {
        playerOne = new Player { Id = "p1", Name = "One" };
        playerOne.TeamCardIds.AddRange(CardCatalog.TeamACharacterIds);
        playerOne.TeamCardIds.AddRange(CardCatalog.TeamABasicActionIds);
        playerTwo = new Player { Id = "p2", Name = "Two" };
        playerTwo.TeamCardIds.AddRange(CardCatalog.TeamBCharacterIds);
        playerTwo.TeamCardIds.AddRange(CardCatalog.TeamBBasicActionIds);
        return GameSetup.NewGame(DiceFightClassicConfig.Config, Catalog, playerOne, playerTwo);
    }

    // `level` is a LEVEL, not a face index - see LevelFace above.
    private static Model.DieInstance FieldFreshCopy(GameState state, string cardId, string controllerId, int level = 1)
    {
        var die = new Model.DieInstance { Id = $"{controllerId}-{cardId}-test", CardId = cardId, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.FieldZone, CurrentFaceIndex = LevelFace(level) };
        state.Dice.Add(die);
        return die;
    }

    [Fact]
    public void Apocalypse_Carries_The_Overcrush_Keyword()
    {
        var state = NewTestGame(out _, out _);
        var die = FieldFreshCopy(state, CardCatalog.Apocalypse.Id, "p1");
        Assert.Contains("Overcrush", QueryEngine.GetKeywords(state, die));
    }

    [Fact]
    public void CaptainMarvel_Buffs_Her_Own_Teams_Character_Dice()
    {
        var state = NewTestGame(out _, out _);
        var captainMarvel = FieldFreshCopy(state, CardCatalog.CaptainMarvel.Id, "p1");
        var ally = FieldFreshCopy(state, CardCatalog.HarleyQuinn.Id, "p1");
        var opposing = FieldFreshCopy(state, CardCatalog.Groot.Id, "p2");

        var allyBaseAttack = state.GetCurrentFace(ally)!.Character!.Attack;
        Assert.Equal(allyBaseAttack + 1, QueryEngine.GetAttack(state, ally));

        var opposingBaseAttack = state.GetCurrentFace(opposing)!.Character!.Attack;
        Assert.Equal(opposingBaseAttack, QueryEngine.GetAttack(state, opposing)); // not buffed - not her own team
    }

    [Fact]
    public void Dazzler_Deals_4_Damage_To_A_Mask_Character_Die_When_Fielded()
    {
        var state = NewTestGame(out _, out _);
        state.CurrentStep = TurnStep.Main;
        // Level 3 (7 Defense) so the 4 damage marks rather than KOing (KO
        // would reset Damage back to 0 as part of leaving the Field Zone -
        // a separate, already-covered behavior, not what this test is for).
        var target = FieldFreshCopy(state, CardCatalog.BigE.Id, "p2", level: 3);
        var dazzlerDie = new Model.DieInstance { Id = "dazzler-die", CardId = CardCatalog.Dazzler.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = LevelFace(1) };
        state.Dice.Add(dazzlerDie);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, dazzlerDie.Id, []);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));
        AnswerAnyPendingChoiceWith(state, target.Id); // Dazzler herself is [Mask] too, so this is a real 2-candidate choice

        Assert.Equal(4, target.Damage);
    }

    [Fact]
    public void GodEmperorDoom_Deals_3_Damage_And_Rerolls_A_Target_When_Fielded()
    {
        var state = NewTestGame(out _, out _);
        state.CurrentStep = TurnStep.Main;
        // Level 3 (4 Defense) so the 3 damage marks rather than KOing -
        // a KO'd die leaves the Field Zone (Damage resets, and Reroll
        // would then find no live target at all), which isn't what this
        // test is checking.
        var target = FieldFreshCopy(state, CardCatalog.HarleyQuinn.Id, "p2", level: 3);
        var godDie = new Model.DieInstance { Id = "god-die", CardId = CardCatalog.GodEmperorDoom.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = LevelFace(1) };
        state.Dice.Add(godDie);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, godDie.Id, []);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(2), new Random(1));
        // Neither DealDamage nor Reroll restricts ownership ("target character
        // die" - real card text), so each step is a real choice between the
        // target and God Emperor Doom's own die; answer both toward target.
        AnswerAnyPendingChoiceWith(state, target.Id);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(2), new Random(1));
        AnswerAnyPendingChoiceWith(state, target.Id);

        Assert.Equal(3, target.Damage);
        Assert.Equal(2, target.CurrentFaceIndex); // rerolled onto whatever FixedRoller(2) landed
    }

    private static void AnswerAnyPendingChoiceWith(GameState state, string preferredId)
    {
        if (state.PendingChoice is not { } pending) return;
        var answer = pending.CandidateIds.Contains(preferredId) ? preferredId : pending.CandidateIds[0];
        EffectInterpreter.AnswerPendingChoice(state, [answer]);
    }

    [Fact]
    public void Groot_Draws_And_Rolls_2_Dice_From_Bag_When_Fielded()
    {
        var state = NewTestGame(out _, out _);
        state.CurrentStep = TurnStep.Main;
        var grootDie = new Model.DieInstance { Id = "groot-die", CardId = CardCatalog.Groot.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = LevelFace(1) };
        state.Dice.Add(grootDie);
        var queue = new AbilityQueue();
        var bagCountBefore = state.DiceIn("p1", Zone.Bag).Count();

        TurnEngine.Field(state, queue, grootDie.Id, []);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

        Assert.Equal(2, state.DiceIn("p1", Zone.ReservePool).Count(d => d.Id != grootDie.Id));
        Assert.Equal(bagCountBefore - 2, state.DiceIn("p1", Zone.Bag).Count());
    }

    [Fact]
    public void ShockingGrasp_KOs_A_1_Defense_Target_And_May_Prep_Itself()
    {
        var state = NewTestGame(out _, out _);
        state.CurrentStep = TurnStep.Main;
        // A Sidekick (1D) dies to Shocking Grasp's 1 damage.
        var sidekick = new Model.DieInstance { Id = "sk", PoolDieId = DiceFightClassicConfig.SidekickDie.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(sidekick);
        var grasp = new Model.DieInstance { Id = "grasp-die", CardId = CardCatalog.ShockingGrasp.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(grasp);
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, grasp.Id);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

        Assert.Equal(Zone.PrepArea, sidekick.Zone); // KO'd
        Assert.NotNull(state.PendingChoice); // "you may Prep this die" is still a real choice (ground rule 8)
        EffectInterpreter.AnswerPendingChoice(state, state.PendingChoice!.CandidateIds); // accept
        Assert.Equal(Zone.PrepArea, grasp.Zone);
    }

    // Rule 3.2.5's per-ability snapshot, exercised by its own motivating
    // card: the Ko clause KOs 3 dice INTO the opponent's Prep Area (rule
    // 1.5.3.2), and the later Prep-Area-targeting clause must still see
    // only the 3 dice that were there when the ability began - exactly 3
    // candidates each, so every clause auto-resolves with no PendingChoice.
    [Fact]
    public void CasketOfAncientWinters_KOd_Dice_Do_Not_Dilute_Its_Own_Later_PrepArea_Clause()
    {
        var state = NewTestGame(out _, out _);
        state.CurrentStep = TurnStep.Main;
        for (var i = 0; i < 3; i++) state.Dice.Add(new Model.DieInstance { Id = $"p2-field-{i}", PoolDieId = DiceFightClassicConfig.SidekickDie.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.FieldZone, CurrentFaceIndex = 0 });
        for (var i = 0; i < 3; i++) state.Dice.Add(new Model.DieInstance { Id = $"p2-reserve-{i}", PoolDieId = DiceFightClassicConfig.SidekickDie.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.ReservePool, CurrentFaceIndex = LevelFace(1) });
        for (var i = 0; i < 3; i++) state.Dice.Add(new Model.DieInstance { Id = $"p2-prep-{i}", PoolDieId = DiceFightClassicConfig.SidekickDie.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.PrepArea, CurrentFaceIndex = null });

        var casket = new Model.DieInstance { Id = "casket-die", CardId = CardCatalog.CasketOfAncientWinters.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(casket);
        var queue = new AbilityQueue();

        TurnEngine.UseAction(state, queue, casket.Id);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

        Assert.Null(state.PendingChoice); // every clause had exactly Count candidates - no choice anywhere
        Assert.All(state.Dice.Where(d => d.Id.StartsWith("p2-field")), d => Assert.Equal(Zone.PrepArea, d.Zone)); // KO'd - and NOT swept onward
        Assert.All(state.Dice.Where(d => d.Id.StartsWith("p2-reserve")), d => Assert.Equal(Zone.Bag, d.Zone));
        Assert.All(state.Dice.Where(d => d.Id.StartsWith("p2-prep")), d => Assert.Equal(Zone.UsedPile, d.Zone));
    }

    // The snapshot's other half: it ends when its own ability finishes. A
    // LATER ability in the same queue drain resolves against live state,
    // so it DOES see the dice the previous ability just KO'd into the
    // Prep Area - the queue-level semantics the per-ability scope exists
    // to preserve (and the reason a blanked card's already-queued trigger
    // will fire-but-do-nothing once the ability-blanking spike lands).
    [Fact]
    public void The_Snapshot_Ends_With_Its_Ability_A_Later_Queued_Ability_Sees_Live_State()
    {
        var state = NewTestGame(out _, out _);
        state.CurrentStep = TurnStep.Main;
        var victim = new Model.DieInstance { Id = "victim", PoolDieId = DiceFightClassicConfig.SidekickDie.Id, OwnerId = "p2", ControllerId = "p2", Zone = Zone.FieldZone, CurrentFaceIndex = 0 };
        state.Dice.Add(victim);

        var queue = new AbilityQueue();
        // Ability 1: KO the field die (it lands in p2's Prep Area).
        queue.Enqueue(null, "p1", Model.Effects.TriggerKind.Global,
            new Model.Effects.Ko(new Model.Effects.TargetFilter(Kind: Model.Effects.TargetKind.CharacterDie, Ownership: Model.Effects.TargetOwnership.Opposing)));
        // Ability 2: sweep ALL of p2's Prep Area dice to the Used Pile.
        queue.Enqueue(null, "p1", Model.Effects.TriggerKind.Global,
            new Model.Effects.MoveDie(new Model.Effects.TargetFilter(Kind: Model.Effects.TargetKind.AnyDie, Ownership: Model.Effects.TargetOwnership.Opposing, Zones: [Zone.PrepArea], Count: 0), Zone.UsedPile));

        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

        // Ability 2's own fresh snapshot included the just-KO'd die, so it
        // was swept - proof the first ability's snapshot didn't leak.
        Assert.Equal(Zone.UsedPile, victim.Zone);
    }
}
