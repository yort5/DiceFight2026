using DiceFight.Api;

namespace DiceFight.Api.Tests;

// v3 "Instinct Clash" - permanent regression coverage for V2GamesController,
// matching what a manual curl-driven session already confirmed working
// end to end this session (create -> clear-and-draw -> roll -> field a
// free Tardigrade -> attack -> combat -> clean up, Champion passive
// included). The controller has no injectable randomness source (unlike
// the deterministic engine-level InstinctClashConfigTests, which script
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
    public void Create_Builds_Each_Team_From_Its_Champions_Energy_Type()
    {
        var (_, session) = CreateGame("Wolf", "GreatHornedOwl");
        var state = session.State;

        Assert.Equal("Wolf", state.PlayerOne.ChampionId);
        // Deck-building basics: every Character copy sits Unpurchased
        // until bought (v1's TeamSetup shape, unchanged for v3) - no free
        // starting copies, only the Tardigrades start in Bag.
        Assert.Equal(32, state.DiceIn("teamA", DiceFight.V2.Model.Zone.Unpurchased).Count()); // 8 Claw Characters x DieLimit 4
        Assert.Equal(8, state.DiceIn("teamA", DiceFight.V2.Model.Zone.Bag).Count()); // Claw Tardigrades
        Assert.All(state.DiceIn("teamA", DiceFight.V2.Model.Zone.Unpurchased),
            d => Assert.StartsWith("IC-CLAW-", d.CardId));

        Assert.Equal("GreatHornedOwl", state.PlayerTwo.ChampionId);
        Assert.All(state.DiceIn("teamB", DiceFight.V2.Model.Zone.Unpurchased),
            d => Assert.StartsWith("IC-EYE-", d.CardId));
    }

    [Fact]
    public void Full_Turn_Cycle_Works_Through_The_Controller_With_Champion_Passive_Applied()
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
        // or the pure-energy Surge face (neither) - never a half-known mix.
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
        var fieldable = session.State.DiceIn("teamA", DiceFight.V2.Model.Zone.ReservePool)
            .First(d => d.CardId is null && session.State.GetCurrentFace(d)?.Character is not null);
        var afterField = V2SeatedController.Dto(teamA.Field(session.Id, new V2FieldRequest(fieldable.Id, [])));
        var fielded = afterField.Dice.Single(d => d.Id == fieldable.Id);
        Assert.Equal("FieldZone", fielded.Zone);
        // Wolf's passive is +1 ATK to all your dice, and every Tardigrade
        // face's base ATK is >= 0, so the buffed value is always >= 1 -
        // a die showing 0 here would mean the Champion passive never applied.
        Assert.True(fielded.EffectiveAttack >= 1);

        V2SeatedController.Dto(teamA.EnterAttackStep(session.Id));
        V2SeatedController.Dto(teamA.DeclareAttackers(session.Id, new V2DeclareAttackersRequest([fielded.Id])));
        V2SeatedController.Dto(teamB.DeclareBlockers(session.Id, new V2DeclareBlockersRequest([])));
        var afterDamage = V2SeatedController.Dto(teamA.AssignCombatDamage(session.Id, new V2AssignCombatDamageRequest([])));
        Assert.True(afterDamage.PlayerTwo.Life < 20); // unblocked attacker landed some damage

        var afterCleanUp = V2SeatedController.Dto(teamA.CleanUp(session.Id));
        Assert.Equal("teamB", afterCleanUp.ActivePlayerId);
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
