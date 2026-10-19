using DiceFight.Api.Recording;
using DiceFight.V2;
using DiceFight.V2.Data;
using DiceFight.V2.Model;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiceFight.Api.Tests;

// Idle games are dropped from memory (V2GameStore.ForgetIdle, 2026-10-04):
// unfinished after a day without a move, finished two hours after the end,
// unjoined invites after a day.
public class GameExpiryTests
{
    private static readonly GameExpiryOptions Expiry = new();

    private static (V2GameStore Store, V2GameSession Session) NewGame(GameRecorder? recorder = null)
    {
        Player Build(string id, string champion)
        {
            var p = new Player { Id = id, Name = champion, ChampionId = champion };
            p.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion[champion]);
            return p;
        }
        var store = new V2GameStore(recorder);
        var state = GameSetup.NewGame(DiceKingdomConfig.Config, DiceKingdomConfig.Catalog, Build("teamA", "Wolf"), Build("teamB", "Armadillo"));
        return (store, store.Create(state));
    }

    [Fact]
    public void An_Unfinished_Game_Is_Kept_For_A_Day_Without_Moves_Then_Forgotten()
    {
        var (store, session) = NewGame();

        Assert.Equal(0, store.ForgetIdle(session.LastMoveUtc.AddHours(23), Expiry));
        Assert.Same(session, store.GetSession(session.Id));

        Assert.Equal(1, store.ForgetIdle(session.LastMoveUtc.AddHours(25), Expiry));
        var gone = Assert.Throws<KeyNotFoundException>(() => store.GetSession(session.Id));
        Assert.Contains("expired", gone.Message);
    }

    [Fact]
    public void A_Move_Resets_The_Clock()
    {
        var (store, session) = NewGame();
        var created = session.LastMoveUtc;
        Thread.Sleep(5);
        session.MarkChanged();

        Assert.True(session.LastMoveUtc > created);
        Assert.Equal(0, store.ForgetIdle(session.LastMoveUtc.AddHours(23), Expiry));
    }

    [Fact]
    public void A_Finished_Game_Goes_After_Two_Hours()
    {
        var (store, session) = NewGame();
        session.State.PlayerTwo.Life = 0;
        session.State.CheckGameOver();

        Assert.Equal(0, store.ForgetIdle(session.LastMoveUtc.AddHours(1), Expiry));
        Assert.Equal(1, store.ForgetIdle(session.LastMoveUtc.AddHours(3), Expiry));
    }

    [Fact]
    public void An_Invite_Nobody_Joined_Goes_After_A_Day()
    {
        var store = new V2GameStore();
        var lobby = store.OpenLobby("Wolf", null, "Wolf", "teamA", "teamB");

        Assert.Equal(0, store.ForgetIdle(lobby.CreatedUtc.AddHours(23), Expiry));
        Assert.Equal(1, store.ForgetIdle(lobby.CreatedUtc.AddHours(25), Expiry));
        Assert.Null(store.GetLobby(lobby.Id));
        Assert.Contains("expired", Assert.Throws<KeyNotFoundException>(() => store.GetSession(lobby.Id)).Message);
    }

    [Fact]
    public void An_Abandoned_Games_Record_Is_Saved_One_Last_Time()
    {
        var recorder = new GameRecorder(new LocalFolderSink(Path.GetTempPath()), new GameRecordOptions(), NullLogger<GameRecorder>.Instance);
        var (store, session) = NewGame(recorder);
        Assert.Null(session.Record!.Result); // no turn has ended yet

        store.ForgetIdle(session.LastMoveUtc.AddHours(25), Expiry);

        Assert.NotNull(session.Record.Result);
        Assert.False(session.Record.Result!.GameOver);
    }
}
