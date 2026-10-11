using DiceFight.Api;
using DiceFight.V2.Data;

namespace DiceFight.Api.Tests;

// v3 "Dice Kingdom" - permanent regression coverage for V2GamesController,
// matching what a manual curl-driven session already confirmed working
// end to end this session (create -> clear-and-draw -> roll -> field a
// free Tardigrade -> attack -> combat -> clean up, Champion passive
// included). The controller has no injectable randomness source (unlike
// the deterministic engine-level DiceKingdomConfigTests, which script
// exact faces via a ScriptedRoller), so these assertions are written to
// hold regardless of which face actually gets rolled, rather than
// pinning exact numbers combat math already covers at the engine layer.
public class V2GamesControllerTests
{
    private static (V2GameStore Store, V2GameSession Session) CreateGame(string p1Champion = "Wolf", string p2Champion = "Armadillo")
    {
        var store = new V2GameStore();
        var anon = V2SeatedController.Anonymous(store);
        var created = V2SeatedController.CreatedDto(anon.Create(new CreateV2GameRequest(p1Champion, p2Champion)));
        var session = store.GetSession(created.Game.GameId);
        return (store, session);
    }

    [Fact]
    public void Create_Builds_Each_Team_From_Its_Champions_Character_Pack()
    {
        var (_, session) = CreateGame("Wolf", "GreatHornedOwl");
        var state = session.State;

        Assert.Equal("Wolf", state.PlayerOne.ChampionId);
        // Deck-building basics: every Character copy sits Unpurchased
        // until bought (v1's TeamSetup shape, unchanged for v3) - no free
        // starting copies, only the Tardigrades start in Bag.
        Assert.Equal(35, state.DiceIn("teamA", DiceFight.V2.Model.Zone.Unpurchased).Count()); // 8 Characters x DieLimit 4 + the Champion's 3 Basic Action dice
        Assert.Equal(8, state.DiceIn("teamA", DiceFight.V2.Model.Zone.Bag).Count()); // Claw Tardigrades - Wolf's own type, unaffected by the mixed-energy Character pack
        // Wolf's pack mixes energies (2026-09-12: 4 Claw/2 Wing/1 Shell/1
        // Eye, not a monochrome 8) - the real invariant now is "matches
        // CharactersByChampion exactly," not "every card is Claw."
        Assert.Equal(
            DiceKingdomConfig.CharactersByChampion["Wolf"].Append(DiceKingdomConfig.ActionByChampion["Wolf"]).ToHashSet(),
            state.DiceIn("teamA", DiceFight.V2.Model.Zone.Unpurchased).Select(d => d.CardId!).ToHashSet());

        Assert.Equal("GreatHornedOwl", state.PlayerTwo.ChampionId);
        Assert.Equal(
            DiceKingdomConfig.CharactersByChampion["GreatHornedOwl"].Append(DiceKingdomConfig.ActionByChampion["GreatHornedOwl"]).ToHashSet(),
            state.DiceIn("teamB", DiceFight.V2.Model.Zone.Unpurchased).Select(d => d.CardId!).ToHashSet());
    }

    [Fact]
    public void Create_Accepts_A_Custom_Team_Without_A_Champion_And_Exact_Dice_Counts()
    {
        var store = new V2GameStore();
        var custom = new CustomV2TeamRequest(
            [new(DiceKingdomConfig.Mongoose.Id, 2), new(DiceKingdomConfig.AngerIssues.Id, 3)],
            [new("Claw", 3), new("Shell", 0), new("Wing", 2), new("Eye", 3)]);
        var request = new CreateV2GameRequest(
            PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: custom);
        var created = V2SeatedController.CreatedDto(V2SeatedController.Anonymous(store).Create(request));
        var state = store.GetSession(created.Game.GameId).State;

        Assert.Null(state.PlayerOne.ChampionId);
        Assert.Equal(2, state.Dice.Count(d => d.OwnerId == "teamA" && d.CardId == DiceKingdomConfig.Mongoose.Id));
        Assert.Equal(3, state.Dice.Count(d => d.OwnerId == "teamA" && d.CardId == DiceKingdomConfig.AngerIssues.Id));
        Assert.Equal(3, state.Dice.Count(d => d.OwnerId == "teamA" && d.PoolDieId == "TardigradeClaw"));
        Assert.Equal(2, state.Dice.Count(d => d.OwnerId == "teamA" && d.PoolDieId == "TardigradeWing"));
        Assert.Equal(3, state.Dice.Count(d => d.OwnerId == "teamA" && d.PoolDieId == "TardigradeEye"));
        Assert.Equal("Armadillo", state.PlayerTwo.ChampionId);
    }

    [Fact]
    public void Full_Turn_Cycle_Works_Through_The_Controller_With_A_Champion_Power()
    {
        var (store, session) = CreateGame("Wolf", "Armadillo");
        var teamA = V2SeatedController.For(store, session, "teamA");
        var teamB = V2SeatedController.For(store, session, "teamB");

        var afterDraw = V2SeatedController.Dto(teamA.ClearAndDraw(session.Id));
        Assert.Equal("roll-and-reroll", afterDraw.CurrentStepId);
        // Rule 2.3.3 - going-first penalty: one of the normal 4 drawn
        // dice (not a 5th extra draw) is set straight to Out of Play.
        Assert.Equal(3, afterDraw.Dice.Count(d => d.ControllerId == "teamA" && d.Zone == "DiceFromBag"));
        Assert.Single(afterDraw.Dice, d => d.ControllerId == "teamA" && d.Zone == "OutOfPlay");

        // Roll (rule 2.4.2) lands every rolled die straight in the
        // Reserve Pool - there's no separate "rolled but not yet placed"
        // holding zone (see TurnEngine.Roll's own remarks).
        var afterRoll = V2SeatedController.Dto(teamA.Roll(session.Id));
        var rolledDice = afterRoll.Dice.Where(d => d.ControllerId == "teamA" && d.Zone == "ReservePool").ToList();
        Assert.Equal(3, rolledDice.Count);
        // Every rolled face is either a character face (both stats known)
        // or a pure-energy face (neither) - never a half-known mix.
        Assert.All(rolledDice, d => Assert.True(
            (d.EffectiveAttack is not null && d.EffectiveDefense is not null) ||
            (d.EffectiveAttack is null && d.EffectiveDefense is null)));

        V2SeatedController.Dto(teamA.FinishRoll(session.Id));

        // Every Tardigrade character face is free to field regardless of
        // which one was rolled - grab whichever one this run happened to
        // land on rather than scripting a specific face. Only Tardigrades
        // (CardId null) ever start in Bag/get drawn this early - every
        // Character copy sits Unpurchased until bought - but the CardId
        // check stays explicit rather than assuming it.
        // Since 2026-10-04 only 3 of a Tardigrade's 6 faces are creatures,
        // so all three can miss (~1 run in 8) - then turn one to a creature face.
        var reserve = session.State.DiceIn("teamA", DiceFight.V2.Model.Zone.ReservePool).Where(d => d.CardId is null).ToList();
        var fieldable = reserve.FirstOrDefault(d => session.State.GetCurrentFace(d)?.Character is not null) ?? reserve[0];
        if (session.State.GetCurrentFace(fieldable)?.Character is null) fieldable.CurrentFaceIndex = 3; // L2 1/1 + a Wild
        var afterField = V2SeatedController.Dto(teamA.Field(session.Id, new V2FieldRequest(fieldable.Id, [])));
        var fielded = afterField.Dice.Single(d => d.Id == fieldable.Id);
        Assert.Equal("FieldZone", fielded.Zone);
        var printedAttack = fielded.EffectiveAttack!.Value; // a Tardigrade: 0 or 1

        V2SeatedController.Dto(teamA.EnterAttackStep(session.Id));
        V2SeatedController.Dto(teamA.DeclareAttackers(session.Id, new V2DeclareAttackersRequest([new V2AttackerDeclaration(fielded.Id, 0)])));
        V2SeatedController.Dto(teamB.DeclareBlockers(session.Id, new V2DeclareBlockersRequest([])));
        // Wolf's once-per-turn power (2026-10-04, replaced its +1 ATK to
        // everything): +3 ATK on its one creature - no choice needed - so
        // even a 0-ATK Tardigrade lands damage.
        var pumped = V2SeatedController.Dto(teamA.ChampionPower(session.Id));
        Assert.Equal(printedAttack + 3, pumped.Dice.Single(d => d.Id == fielded.Id).EffectiveAttack);
        Assert.Contains(pumped.Log, l => l.Text.EndsWith("Tardigrade gets +3 ATK this turn.")); // says which creature
        var afterDamage = V2SeatedController.Dto(teamA.AssignCombatDamage(session.Id, new V2AssignCombatDamageRequest([])));
        Assert.True(afterDamage.PlayerTwo.Life < 20); // unblocked attacker landed some damage

        var afterCleanUp = V2SeatedController.Dto(teamA.CleanUp(session.Id));
        Assert.Equal("teamB", afterCleanUp.ActivePlayerId);
    }

    // Real bug (2026-09-25): blocks only lived in the defender's client,
    // so in two-device play the attacker saw no blockers and resolved
    // damage with an empty assignment - every lane went unblocked. The
    // server now keeps the declared blocks, shows them to both seats and
    // resolves from them whatever the attacker sends.
    [Fact]
    public void Declared_Blocks_Are_Shown_To_Both_Seats_And_Used_For_Damage()
    {
        var (store, session) = CreateGame("Wolf", "Armadillo");
        var teamA = V2SeatedController.For(store, session, "teamA");
        var teamB = V2SeatedController.For(store, session, "teamB");
        teamA.ClearAndDraw(session.Id);
        teamA.Roll(session.Id);
        teamA.FinishRoll(session.Id);

        // Put one Tardigrade per side straight onto the field, showing a
        // character face, rather than depending on this run's rolls.
        var state = session.State;
        DiceFight.V2.Model.DieInstance FieldOne(string player)
        {
            var die = state.Dice.First(d => d.ControllerId == player && d.CardId is null && d.Zone != DiceFight.V2.Model.Zone.FieldZone);
            die.Zone = DiceFight.V2.Model.Zone.FieldZone;
            die.CurrentFaceIndex = 3;
            Assert.NotNull(state.GetCurrentFace(die)?.Character);
            return die;
        }
        var attacker = FieldOne("teamA");
        var blocker = FieldOne("teamB");

        teamA.EnterAttackStep(session.Id);
        teamA.DeclareAttackers(session.Id, new V2DeclareAttackersRequest([new V2AttackerDeclaration(attacker.Id, 0)]));
        teamB.DeclareBlockers(session.Id, new V2DeclareBlockersRequest([new V2BlockAssignment(attacker.Id, blocker.Id)]));

        var attackersView = V2SeatedController.Dto(teamA.Get(session.Id));
        Assert.Equal([new V2BlockAssignment(attacker.Id, blocker.Id)], attackersView.Blocks);

        // The attacker's client sends nothing - the server still knows. The
        // defender (Armadillo, a die in combat) is offered its shield power
        // after the attacker passes, and passes too.
        teamA.AssignCombatDamage(session.Id, new V2AssignCombatDamageRequest([]));
        var afterDamage = V2SeatedController.Dto(teamB.Pass(session.Id));
        Assert.Equal(20, afterDamage.PlayerTwo.Life);
        Assert.Empty(afterDamage.Blocks!);
    }

    [Fact]
    public void Wrong_Seat_Cannot_Act_On_The_Opponents_Turn()
    {
        var (store, session) = CreateGame();
        var teamB = V2SeatedController.For(store, session, "teamB");

        var ex = Assert.Throws<NotYourTurnException>(() => teamB.ClearAndDraw(session.Id));
        Assert.Contains("not your turn", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
