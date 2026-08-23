using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 8 task 2 acceptance: the curated teams are migrated
// with passing behavior tests, exercised through the real firing path
// (ground rule 6) - TurnEngine actions -> EventBus -> AbilityQueue ->
// EffectInterpreter.DrainQueue, not direct EffectContext invocation.
public class CardCatalogTests
{
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

    private static Model.DieInstance FieldFreshCopy(GameState state, string cardId, string controllerId, int levelFaceIndex = 1)
    {
        var die = new Model.DieInstance { Id = $"{controllerId}-{cardId}-test", CardId = cardId, OwnerId = controllerId, ControllerId = controllerId, Zone = Zone.FieldZone, CurrentFaceIndex = levelFaceIndex };
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
        var target = FieldFreshCopy(state, CardCatalog.BigE.Id, "p2", levelFaceIndex: 3);
        var dazzlerDie = new Model.DieInstance { Id = "dazzler-die", CardId = CardCatalog.Dazzler.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 1 }; // face 0 is the energy face; 1 is level 1
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
        var target = FieldFreshCopy(state, CardCatalog.HarleyQuinn.Id, "p2", levelFaceIndex: 3);
        var godDie = new Model.DieInstance { Id = "god-die", CardId = CardCatalog.GodEmperorDoom.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 1 };
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
        var grootDie = new Model.DieInstance { Id = "groot-die", CardId = CardCatalog.Groot.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 1 };
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

    // Casket of Ancient Winters is vanilla (IsImplemented: false) - see
    // CardCatalog.cs's own remarks: its second MoveDie clause collides
    // with EffectInterpreter's documented rule-3.2.5 live-resolution
    // simplification (the Ko clause's own KO'd dice dilute the later
    // Prep-Area-targeting clause's live candidate pool). This test just
    // pins that the card is inert rather than silently misbehaving.
    [Fact]
    public void CasketOfAncientWinters_Is_Vanilla_Pending_The_Rule_3_2_5_Gap()
    {
        Assert.False(CardCatalog.CasketOfAncientWinters.IsImplemented);
        Assert.Empty(CardCatalog.CasketOfAncientWinters.Abilities);
    }
}
