using DiceFight.V2.Data;
using DiceFight.V2.Model;

namespace DiceFight.V2.Tests;

// Direct proof-of-life for the 24 Characters added in the roster
// expansion (2026-09-06/07) - Config.ValidateCatalog (Config_And_
// Catalog_Are_Structurally_Valid) only checks that their ability/target
// wiring is well-formed, not that resolving one actually does anything.
// Same real firing path DpsCardsTests uses for v1's migrated pool: a
// TurnEngine action (which fires its own event, e.g. Field firing
// DieFielded per rule 2.6.3.6) -> AbilityQueue -> EffectInterpreter.
// DrainQueue - not calling effects directly. One test per effect shape
// genuinely new to this catalog (Ko), plus a StatAura on the new
// AtkDelta direction and a plain damage-on-field sanity check.
public class InstinctClashNewCharactersTests
{
    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static void Drain(GameState state, AbilityQueue queue) =>
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

    // Answers a pending choice if one came up (e.g. an unrestricted
    // "target a creature" filter matching more than one candidate, which
    // the die being fielded is itself eligible for per rule 2.6.3.6) and
    // drains whatever that resumes - a no-op when nothing is pending.
    private static void AnswerIfPending(GameState state, AbilityQueue queue, string preferredId)
    {
        if (state.PendingChoice is not { } pending) return;
        var pick = pending.CandidateIds.Contains(preferredId) ? preferredId : pending.CandidateIds[0];
        EffectInterpreter.AnswerPendingChoice(state, [pick]);
        Drain(state, queue);
    }

    private static GameState NewGame()
    {
        var config = InstinctClashConfig.Config;
        var catalog = InstinctClashConfig.Catalog;
        var playerOne = new Player { Id = "p1", Name = "One", ChampionId = "Wolf" };
        playerOne.TeamCardIds.AddRange(InstinctClashConfig.CharactersByEnergyType["Claw"]);
        var playerTwo = new Player { Id = "p2", Name = "Two", ChampionId = "Armadillo" };
        playerTwo.TeamCardIds.AddRange(InstinctClashConfig.CharactersByEnergyType["Shell"]);
        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    // A die already rolled and sitting in the Reserve Pool, ready to
    // Field - skips ClearAndDraw/Roll/Purchase entirely (already
    // exercised by InstinctClashConfigTests' own full-cycle test), since
    // these tests are only about what happens once a Character IS
    // fielded. CharacterDie's later-Dice-Masters layout (2026-09-07)
    // prints each level once (indices 0-2), then 3 energy faces (3-5) -
    // no more doubling, so level N sits at index N-1 directly.
    private static DieInstance ReadyCharacter(GameState state, string cardId, string controllerId, int level = 1)
    {
        var die = new DieInstance
        {
            Id = $"{controllerId}-{cardId}-ready", CardId = cardId, OwnerId = controllerId,
            ControllerId = controllerId, Zone = Zone.ReservePool, CurrentFaceIndex = level - 1,
        };
        state.Dice.Add(die);
        return die;
    }

    private static DieInstance ActiveCharacter(GameState state, string cardId, string controllerId, int level = 1)
    {
        var die = new DieInstance
        {
            Id = $"{controllerId}-{cardId}-active", CardId = cardId, OwnerId = controllerId,
            ControllerId = controllerId, Zone = Zone.FieldZone, CurrentFaceIndex = level - 1,
        };
        state.Dice.Add(die);
        return die;
    }

    // Tardigrade dice in the Reserve Pool, on their own L1 face (2 of the
    // Champion's own energy type - a hybrid face, v3/DESIGN_NOTES.md's
    // locked Tardigrade spec) - real fielding-cost payment, not a raw
    // energy stub, so an overpaid/underpaid cost is caught the same way
    // a real game would catch it.
    private static string[] TardigradeEnergy(GameState state, string controllerId, string energyType, int count)
    {
        var ids = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var die = new DieInstance
            {
                Id = $"{controllerId}-energy-{i}", PoolDieId = $"Tardigrade{energyType}", OwnerId = controllerId,
                ControllerId = controllerId, Zone = Zone.ReservePool, CurrentFaceIndex = 0,
            };
            state.Dice.Add(die);
            ids.Add(die.Id);
        }
        return [.. ids];
    }

    [Fact]
    public void GrizzlyBear_KOs_A_Target_Creature_When_Fielded()
    {
        var state = NewGame();
        var queue = new AbilityQueue();

        var target = ActiveCharacter(state, InstinctClashConfig.Hippopotamus.Id, "p2");
        var grizzly = ReadyCharacter(state, InstinctClashConfig.GrizzlyBear.Id, "p1");
        var energyIds = TardigradeEnergy(state, "p1", "Claw", 1); // fielding cost 2, one L1 Tardigrade die covers it

        TurnEngine.Field(state, queue, grizzly.Id, energyIds);
        Drain(state, queue);
        // Ko's TargetFilter is unrestricted ("a target creature" - no
        // Ownership limit, matching Honey Badger's own printed text) -
        // Grizzly itself is a live candidate the instant it's fielded
        // (rule 2.6.3.6), so with two candidates this is a real player
        // choice, not an auto-resolve.
        AnswerIfPending(state, queue, target.Id);

        Assert.Equal(Zone.FieldZone, grizzly.Zone);
        Assert.Equal(Zone.PrepArea, target.Zone); // KO'd (rule 1.5.3.2)
    }

    [Fact]
    public void Stoat_Deals_1_Damage_To_The_Opponent_When_Fielded()
    {
        var state = NewGame();
        var queue = new AbilityQueue();

        var stoat = ReadyCharacter(state, InstinctClashConfig.Stoat.Id, "p1");
        var energyIds = TardigradeEnergy(state, "p1", "Claw", 1); // fielding cost 1

        var lifeBefore = state.PlayerTwo.Life;
        TurnEngine.Field(state, queue, stoat.Id, energyIds);
        Drain(state, queue);

        Assert.Equal(lifeBefore - 1, state.PlayerTwo.Life);
    }

    [Fact]
    public void CapeBuffalo_Aura_Boosts_Its_Teams_Attack_While_Active()
    {
        var state = NewGame();
        var queue = new AbilityQueue();

        var buffalo = ReadyCharacter(state, InstinctClashConfig.CapeBuffalo.Id, "p1");
        var honeyBadger = ReadyCharacter(state, InstinctClashConfig.HoneyBadger.Id, "p1");
        var energyIds = TardigradeEnergy(state, "p1", "Claw", 2); // fielding cost 2 (Buffalo) + 1 (Honey Badger), one die each

        TurnEngine.Field(state, queue, buffalo.Id, [energyIds[0]]);
        Drain(state, queue);
        TurnEngine.Field(state, queue, honeyBadger.Id, [energyIds[1]]);
        Drain(state, queue);

        // base 0 ATK + Cape Buffalo's own +1 aura + Wolf's own Champion
        // passive (+1 ATK to all your dice, ChampionRegistry) - both
        // apply to every one of p1's dice, this one included.
        Assert.Equal(2, QueryEngine.GetAttack(state, honeyBadger));
    }

    // The actual point of the whole 2026-09-07 face-layout change: a
    // Character die's energy faces are real, spendable energy - not a
    // display artifact. Indices 3/4 are its two double-energy faces,
    // index 5 the single (see CharacterDie's own remarks).
    [Fact]
    public void Character_Die_Provides_Real_Spendable_Energy_On_Its_Energy_Faces()
    {
        var state = NewGame();
        var queue = new AbilityQueue();

        var honeyBadgerEnergy = new DieInstance
        {
            Id = "p1-honeybadger-energy", CardId = InstinctClashConfig.HoneyBadger.Id, OwnerId = "p1",
            ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 3,
        };
        state.Dice.Add(honeyBadgerEnergy);

        var face = state.GetCurrentFace(honeyBadgerEnergy)!;
        Assert.Null(face.Character); // an energy face, not a stat face
        Assert.Equal(2, face.Symbols.Single(s => s.SymbolId == "Claw").Count);

        // Wolverine's fielding cost (2) exactly matches the die's full
        // 2-energy face - spent in full, not partially (see the spin-down
        // test below for that case), so it should still leave for Out of
        // Play exactly as any fully-spent energy die does. Wolverine
        // specifically (not Grizzly/Orca) because its ability is
        // On-Attack, not On-Field - fielding it alone triggers nothing,
        // so this test stays about energy spending, not target choices.
        var wolverine = ReadyCharacter(state, InstinctClashConfig.Wolverine.Id, "p1"); // fielding cost 2
        TurnEngine.Field(state, queue, wolverine.Id, [honeyBadgerEnergy.Id]);
        Drain(state, queue);

        Assert.Equal(Zone.FieldZone, wolverine.Zone);
        Assert.Equal(Zone.OutOfPlay, honeyBadgerEnergy.Zone); // spent in full
    }

    // Rule 2.6.1.4 - direct feedback (2026-09-05): "we need to be able to
    // partially spend energy - so if we spend half of the double energy
    // die, it should spin to the L2 side with single energy." Spending
    // only 1 of a Tardigrade's 2-energy L1 face for Stoat's fielding cost
    // (1) should leave the die showing the matching single-energy L2
    // face, still sitting right in the Reserve Pool - not consumed.
    [Fact]
    public void Partially_Spending_A_Tardigrades_Double_Energy_Face_Spins_It_Down_To_The_Single_Energy_Face()
    {
        var state = NewGame();
        var queue = new AbilityQueue();

        var energyDie = TardigradeEnergy(state, "p1", "Claw", 1)[0]; // L1 face, 2 Claw
        var die = state.Dice.Single(d => d.Id == energyDie);
        Assert.Equal(0, die.CurrentFaceIndex); // L1, the first of the two double-energy faces

        var stoat = ReadyCharacter(state, InstinctClashConfig.Stoat.Id, "p1"); // fielding cost 1
        TurnEngine.Field(state, queue, stoat.Id, [energyDie]);
        Drain(state, queue);

        Assert.Equal(Zone.FieldZone, stoat.Zone);
        Assert.Equal(Zone.ReservePool, die.Zone); // NOT spent - spun down and kept
        Assert.Equal(2, die.CurrentFaceIndex); // one of the two L2 (single-energy) faces
        var spunFace = state.GetCurrentFace(die)!;
        Assert.Equal(1, spunFace.Symbols.Single(s => s.SymbolId == "Claw").Count);
        Assert.Equal(2, spunFace.Character!.Level); // L2 - a real, still-fieldable creature face too
    }
}
