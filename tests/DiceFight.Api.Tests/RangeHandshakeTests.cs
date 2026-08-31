using DiceFight.Api;
using DiceFight.Engine;
using DiceFight.Engine.Combat;
using DiceFight.Engine.Model;
using DiceFight.Engine.Queueing;

namespace DiceFight.Api.Tests;

// Rule 2.7.4.2 fires every Range die at once, which is fine for an engine
// holding both sides' assignments and impossible for two browsers that
// can only speak one at a time. The server's answer is a handshake: the
// active player assigns first (rule 2.6.5.7 gives them priority, and
// going first is what priority buys), their assignments wait in the
// session, and the opponent's submission is what fires them together.
//
// What these pin down is the ordering and the waiting, since the damage
// itself is CombatEngineTests' business.
public sealed class RangeHandshakeTests
{
    private static readonly CardDef RangeCard = new()
    {
        Id = "range-character", Name = "Range Character", Type = CardType.Character,
        PurchaseCost = 3, DieLimit = 4,
        Keywords = [new KeywordInstance("Range", Params: [3])],
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 3)],
    };

    private static readonly CardDef PlainCard = new()
    {
        Id = "plain-character", Name = "Plain Character", Type = CardType.Character,
        PurchaseCost = 3, DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 1, Attack: 1, Defense: 1)],
    };

    private static DieInstance AddDie(GameState state, string id, string playerId, string cardId)
    {
        var die = new DieInstance
        {
            Id = id, CardId = cardId, OwnerId = playerId, ControllerId = playerId,
            Zone = Zone.FieldZone, Status = DieStatus.Character, Level = 1,
        };
        state.Dice.Add(die);
        return die;
    }

    /// <summary>
    /// A game already in the Range window: the active player attacks with a
    /// Range die, and both sides have a plain die to shoot at. Returns the
    /// store, the session, and the four dice.
    /// </summary>
    private static (GameStore Store, GameSession Session,
        DieInstance ActiveShooter, DieInstance ActiveTarget,
        DieInstance InactiveShooter, DieInstance InactiveTarget) RangeWindow(bool inactiveHasRange = true)
    {
        var catalog = new Dictionary<string, CardDef> { [RangeCard.Id] = RangeCard, [PlainCard.Id] = PlainCard };
        var state = GameState.NewGame(
            catalog, new Player { Id = "teamA", Name = "Team A" }, new Player { Id = "teamB", Name = "Team B" });
        state.CurrentStep = TurnStep.Attack;
        state.AttackSubStep = AttackSubStep.DeclareAttackers;

        var activeShooter = AddDie(state, "a-range", "teamA", RangeCard.Id);
        var activeTarget = AddDie(state, "a-plain", "teamA", PlainCard.Id);
        var inactiveShooter = AddDie(state, "b-range", "teamB", inactiveHasRange ? RangeCard.Id : PlainCard.Id);
        var inactiveTarget = AddDie(state, "b-plain", "teamB", PlainCard.Id);

        CombatEngine.DeclareAttackers(state, new AbilityQueue(), [activeShooter.Id]);
        Assert.Equal(AttackSubStep.RangeWindow, state.AttackSubStep);

        var store = new GameStore();
        var session = store.Create(state);
        return (store, session, activeShooter, activeTarget, inactiveShooter, inactiveTarget);
    }

    private static SubmitRangeRequest Shoot(DieInstance shooter, DieInstance target) =>
        new([new RangeAssignment(shooter.Id, target.Id)]);

    [Fact]
    public void ActivePlayerSubmitsFirst_WindowHoldsForTheOpponent()
    {
        var (store, session, shooter, _, _, target) = RangeWindow();

        var dto = SeatedController.Dto(
            SeatedController.For(store, session, "teamA").SubmitRange(session.Id, Shoot(shooter, target)));

        Assert.True(dto.RangeSubmittedByActivePlayer);
        Assert.Equal(AttackSubStep.RangeWindow, session.State.AttackSubStep);
        // Nothing has been dealt yet - the target is a 1D die that 3 Range
        // damage would KO instantly if this had resolved early.
        Assert.Equal(Zone.FieldZone, target.Zone);
    }

    [Fact]
    public void OpponentSubmitting_ResolvesBothSidesAtOnce()
    {
        var (store, session, activeShooter, activeTarget, inactiveShooter, inactiveTarget) = RangeWindow();

        SeatedController.For(store, session, "teamA").SubmitRange(session.Id, Shoot(activeShooter, inactiveTarget));
        var dto = SeatedController.Dto(
            SeatedController.For(store, session, "teamB").SubmitRange(session.Id, Shoot(inactiveShooter, activeTarget)));

        // Both shots land, neither shooter got to see the other's damage
        // first: 3 Range vs 1D KOs both targets.
        Assert.Equal(Zone.PrepArea, inactiveTarget.Zone);
        Assert.Equal(Zone.PrepArea, activeTarget.Zone);
        Assert.Equal(AttackSubStep.DeclareBlockers, session.State.AttackSubStep);
        Assert.False(dto.RangeSubmittedByActivePlayer);
        Assert.Null(session.PendingRange);
    }

    [Fact]
    public void OpponentCannotSubmitBeforeTheActivePlayer()
    {
        var (store, session, _, activeTarget, inactiveShooter, _) = RangeWindow();

        var ex = Assert.Throws<NotYourTurnException>(() =>
            SeatedController.For(store, session, "teamB").SubmitRange(session.Id, Shoot(inactiveShooter, activeTarget)));

        Assert.Contains("active player", ex.Message);
        Assert.Equal(Zone.FieldZone, activeTarget.Zone);
        Assert.Equal(AttackSubStep.RangeWindow, session.State.AttackSubStep);
    }

    // A player with nothing to decide is not asked. Otherwise the window
    // would sit waiting on someone whose only legal answer is "none".
    [Fact]
    public void OpponentWithNoRangeDice_ResolvesOnTheActivePlayersSubmissionAlone()
    {
        var (store, session, shooter, _, _, target) = RangeWindow(inactiveHasRange: false);

        var dto = SeatedController.Dto(
            SeatedController.For(store, session, "teamA").SubmitRange(session.Id, Shoot(shooter, target)));

        Assert.Equal(Zone.PrepArea, target.Zone);
        Assert.Equal(AttackSubStep.DeclareBlockers, session.State.AttackSubStep);
        Assert.False(dto.RangeSubmittedByActivePlayer);
    }

    [Fact]
    public void ActivePlayerWithNoRangeDice_LetsTheOpponentGoFirst()
    {
        var (store, session, _, activeTarget, inactiveShooter, _) = RangeWindow();
        // The attack got here on a Range attacker, which then left the
        // board - so the window is open with only the opponent able to act.
        session.State.Dice.Remove(session.State.Dice.Single(d => d.Id == "a-range"));

        var dto = SeatedController.Dto(
            SeatedController.For(store, session, "teamB").SubmitRange(session.Id, Shoot(inactiveShooter, activeTarget)));

        Assert.Equal(Zone.PrepArea, activeTarget.Zone);
        Assert.Equal(AttackSubStep.DeclareBlockers, session.State.AttackSubStep);
        Assert.False(dto.RangeSubmittedByActivePlayer);
    }

    // Rejected while its author is still the one asking. Holding a bad
    // assignment until the opponent submits would fail their request with
    // someone else's mistake, and neither player could act on that.
    [Fact]
    public void IllegalAssignmentIsRejectedAtSubmitTime_NotAtResolution()
    {
        var (store, session, shooter, activeTarget, _, _) = RangeWindow();

        Assert.Throws<InvalidOperationException>(() =>
            SeatedController.For(store, session, "teamA").SubmitRange(session.Id, Shoot(shooter, activeTarget)));

        Assert.Null(session.PendingRange);
        Assert.Equal(AttackSubStep.RangeWindow, session.State.AttackSubStep);
    }

    [Fact]
    public void CannotShootWithTheOpponentsDice()
    {
        var (store, session, activeShooter, _, _, inactiveTarget) = RangeWindow();

        // teamB trying to fire teamA's Range die at teamA's own board.
        Assert.Throws<InvalidOperationException>(() =>
            SeatedController.For(store, session, "teamB")
                .SubmitRange(session.Id, Shoot(activeShooter, inactiveTarget)));
    }

    [Fact]
    public void NoSeatToken_IsRejectedBeforeAnythingIsStored()
    {
        var (store, session, shooter, _, _, target) = RangeWindow();

        Assert.Throws<SeatRequiredException>(() =>
            SeatedController.Anonymous(store).SubmitRange(session.Id, Shoot(shooter, target)));

        Assert.Null(session.PendingRange);
    }

    // A half-collected window cannot outlive the window. If the active
    // player's assignments survived into a later turn they would fire on
    // dice that had since moved, or on nobody at all.
    [Fact]
    public void PendingAssignmentsAreDroppedWhenCombatMovesOn()
    {
        var (store, session, shooter, _, _, target) = RangeWindow();
        SeatedController.For(store, session, "teamA").SubmitRange(session.Id, Shoot(shooter, target));
        Assert.NotNull(session.PendingRange);

        // Any later action re-reads the state and finds the window closed.
        session.State.AttackSubStep = AttackSubStep.DeclareBlockers;
        var dto = SeatedController.Dto(SeatedController.For(store, session, "teamA", "GET").Get(session.Id));

        Assert.Null(session.PendingRange);
        Assert.False(dto.RangeSubmittedByActivePlayer);
    }

    // The opponent's browser only re-renders when the version moves, so a
    // submission that does not resolve anything still has to bump it -
    // otherwise "your turn to assign Range" never arrives.
    [Fact]
    public void SubmittingBumpsTheVersionEvenThoughNoDamageLands()
    {
        var (store, session, shooter, _, _, target) = RangeWindow();
        var before = session.Version;

        SeatedController.For(store, session, "teamA").SubmitRange(session.Id, Shoot(shooter, target));

        Assert.True(session.Version > before);
    }
}
