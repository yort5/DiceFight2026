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
public class DiceKingdomNewCharactersTests
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
        var config = DiceKingdomConfig.Config;
        var catalog = DiceKingdomConfig.Catalog;
        var playerOne = new Player { Id = "p1", Name = "One", ChampionId = "Wolf" };
        playerOne.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["Wolf"]);
        var playerTwo = new Player { Id = "p2", Name = "Two", ChampionId = "Armadillo" };
        playerTwo.TeamCardIds.AddRange(DiceKingdomConfig.CharactersByChampion["Armadillo"]);
        var state = GameSetup.NewGame(config, catalog, playerOne, playerTwo);
        state.CurrentStep = TurnStep.Main;
        return state;
    }

    // A die already rolled and sitting in the Reserve Pool, ready to
    // Field - skips ClearAndDraw/Roll/Purchase entirely (already
    // exercised by DiceKingdomConfigTests' own full-cycle test), since
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
    public void Orca_KOs_A_Target_Creature_When_Fielded()
    {
        var state = NewGame();
        var queue = new AbilityQueue();

        var target = ActiveCharacter(state, DiceKingdomConfig.Hippopotamus.Id, "p2");
        var orca = ReadyCharacter(state, DiceKingdomConfig.Orca.Id, "p1");
        var energyIds = TardigradeEnergy(state, "p1", "Claw", 1); // fielding cost 2, one L1 Tardigrade die covers it

        TurnEngine.Field(state, queue, orca.Id, energyIds);
        Drain(state, queue);
        // Ko's TargetFilter is unrestricted ("a target creature" - no
        // Ownership limit, matching Honey Badger's own printed text) -
        // Orca itself is a live candidate the instant it's fielded
        // (rule 2.6.3.6), so with two candidates this is a real player
        // choice, not an auto-resolve.
        AnswerIfPending(state, queue, target.Id);

        Assert.Equal(Zone.FieldZone, orca.Zone);
        Assert.Equal(Zone.PrepArea, target.Zone); // KO'd (rule 1.5.3.2)
    }

    [Fact]
    public void Stoat_Deals_1_Damage_To_The_Opponent_When_Fielded()
    {
        var state = NewGame();
        var queue = new AbilityQueue();

        var stoat = ReadyCharacter(state, DiceKingdomConfig.Stoat.Id, "p1");
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

        var buffalo = ReadyCharacter(state, DiceKingdomConfig.CapeBuffalo.Id, "p1");
        var honeyBadger = ReadyCharacter(state, DiceKingdomConfig.HoneyBadger.Id, "p1");
        var energyIds = TardigradeEnergy(state, "p1", "Claw", 2); // fielding cost 2 (Buffalo) + 1 (Honey Badger), one die each

        TurnEngine.Field(state, queue, buffalo.Id, [energyIds[0]]);
        Drain(state, queue);
        TurnEngine.Field(state, queue, honeyBadger.Id, [energyIds[1]]);
        Drain(state, queue);

        // base 1 ATK (rebalanced 2026-09-27, was 0) + Cape Buffalo's own +1
        // aura, which applies to every one of p1's dice. (Wolf's old +1 ATK
        // passive is gone - Champions have once-per-turn powers since
        // 2026-10-04.)
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
            Id = "p1-honeybadger-energy", CardId = DiceKingdomConfig.HoneyBadger.Id, OwnerId = "p1",
            ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 3,
        };
        state.Dice.Add(honeyBadgerEnergy);

        var face = state.GetCurrentFace(honeyBadgerEnergy)!;
        Assert.Null(face.Character); // an energy face, not a stat face
        Assert.Equal(2, face.Symbols.Single(s => s.SymbolId == "Claw").Count);

        // Wolverine's level-3 fielding cost (2) exactly matches the die's full
        // 2-energy face - spent in full, not partially (see the spin-down
        // test below for that case), so it should still leave for Out of
        // Play exactly as any fully-spent energy die does. Wolverine
        // specifically (not Grizzly/Orca) because its ability is
        // On-Attack, not On-Field - fielding it alone triggers nothing,
        // so this test stays about energy spending, not target choices.
        var wolverine = ReadyCharacter(state, DiceKingdomConfig.Wolverine.Id, "p1", level: 3); // fielding cost 2 at level 3 (1/1/2 per level)
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

        var energyDie = TardigradeEnergy(state, "p1", "Claw", 1)[0]; // 2 Claw
        var die = state.Dice.Single(d => d.Id == energyDie);
        Assert.Equal(0, die.CurrentFaceIndex); // the first of the two double-energy faces

        var stoat = ReadyCharacter(state, DiceKingdomConfig.Stoat.Id, "p1", level: 2); // fielding cost 1 at level 2 (0/1/2 per level)
        TurnEngine.Field(state, queue, stoat.Id, [energyDie]);
        Drain(state, queue);

        Assert.Equal(Zone.FieldZone, stoat.Zone);
        Assert.Equal(Zone.ReservePool, die.Zone); // NOT spent - spun down and kept
        Assert.Equal(2, die.CurrentFaceIndex); // the single-energy face
        var spunFace = state.GetCurrentFace(die)!;
        Assert.Equal(1, spunFace.Symbols.Single(s => s.SymbolId == "Claw").Count);
    }

    // --- Finisher cards (2026-09-28, user request) - each one proves the
    // real mechanism fires, not just that the CardDef is well-formed. ---

    // Silverback also proves the two new closed-vocabulary primitives it
    // needed (MultipleOf, BlockedByAtLeast) actually wire end-to-end
    // through a real combat, not just in isolation.
    [Fact]
    public void Silverback_Doubles_Its_Attack_Only_While_Gang_Blocked()
    {
        var state = NewGame(); // p1 = Wolf, p2 = Armadillo
        var queue = new AbilityQueue();

        var silverback = ActiveCharacter(state, DiceKingdomConfig.Silverback.Id, "p1"); // L1: 6A/4D
        var blocker1 = ActiveCharacter(state, DiceKingdomConfig.HermitCrab.Id, "p2");
        var blocker2 = ActiveCharacter(state, DiceKingdomConfig.MuskOx.Id, "p2");

        // Before any block is declared, GameState.DeclaredBlocks is null,
        // so BlockedByAtLeast reads false - just the base attack.
        Assert.Equal(6, QueryEngine.GetAttack(state, silverback));

        TurnEngine.EnterAttackStep(state, queue);
        Drain(state, queue);
        CombatEngine.DeclareAttackers(state, queue, [silverback.Id]);
        Drain(state, queue);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(silverback.Id, blocker1.Id);
        assignment.AssignBlocker(silverback.Id, blocker2.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker1.Id, blocker2.Id]);
        Drain(state, queue);

        // Gang-blocked by 2 now - MultipleOf("self", Attack, 1) adds
        // Silverback's own BASE attack (MultipleOf reads base only, same
        // rule StatOf documents, precisely so this doesn't recurse into its
        // own not-yet-computed total): 6 (base) + 6 (this aura) = 12.
        Assert.Equal(12, QueryEngine.GetAttack(state, silverback));
    }

    // Direct feedback (2026-09-28): doubling-only made Silverback a
    // "threat only if gang-blocked" card - a single blocker could just
    // wall it for free and dodge the whole mechanic. Overcrush (added
    // alongside a real ATK bump) is what makes it dangerous EITHER way -
    // this proves the single-blocker path specifically: the doubling
    // never activates (only 1 blocker), but Overcrush still carries the
    // leftover once that one blocker is gone.
    [Fact]
    public void Silverback_Still_Overwhelms_A_Single_Blocker_Via_Overcrush()
    {
        var state = NewGame(); // p1 = Wolf, p2 = Armadillo
        var queue = new AbilityQueue();

        var silverback = ActiveCharacter(state, DiceKingdomConfig.Silverback.Id, "p1"); // 6A
        var blocker = ActiveCharacter(state, DiceKingdomConfig.HermitCrab.Id, "p2"); // 2D

        TurnEngine.EnterAttackStep(state, queue);
        Drain(state, queue);
        CombatEngine.DeclareAttackers(state, queue, [silverback.Id]);
        Drain(state, queue);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(silverback.Id, blocker.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [blocker.Id]);
        Drain(state, queue);

        Assert.Equal(6, QueryEngine.GetAttack(state, silverback)); // NOT doubled - only 1 blocker

        var lifeBefore = state.PlayerTwo.Life;
        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        Drain(state, queue);

        Assert.Equal(Zone.PrepArea, blocker.Zone); // the single blocker dies (2D < 6A)...
        Assert.Equal(lifeBefore - 4, state.PlayerTwo.Life); // ...and Overcrush carries the leftover (6 - 2) straight through
    }

    [Fact]
    public void Rhinoceros_Reflects_Combat_Damage_It_Takes_To_The_Opponent()
    {
        var state = NewGame(); // p1 = Wolf, p2 = Armadillo
        var queue = new AbilityQueue();

        var rhino = ActiveCharacter(state, DiceKingdomConfig.Rhinoceros.Id, "p2"); // L1: 1A/5D
        var attacker = ActiveCharacter(state, DiceKingdomConfig.HoneyBadger.Id, "p1", level: 3); // 2A - well under Rhino's 5D, so it survives

        TurnEngine.EnterAttackStep(state, queue);
        Drain(state, queue);
        CombatEngine.DeclareAttackers(state, queue, [attacker.Id]);
        Drain(state, queue);

        var assignment = new CombatAssignment();
        assignment.AssignBlocker(attacker.Id, rhino.Id);
        CombatEngine.DeclareBlockers(state, queue, assignment, [rhino.Id]);
        Drain(state, queue);

        var attackerAttack = QueryEngine.GetAttack(state, attacker);
        var lifeBefore = state.PlayerOne.Life; // p1 - the opponent of Rhino's controller (p2)

        CombatEngine.AssignCombatDamage(state, queue, assignment, new Dictionary<string, IReadOnlyDictionary<string, int>>());
        Drain(state, queue); // resolves the DieDamaged-triggered reflect

        Assert.Equal(Zone.FieldZone, rhino.Zone); // survived the hit (6D > 3A)
        Assert.Equal(lifeBefore - attackerAttack, state.PlayerOne.Life);
    }

    [Fact]
    public void Basilisk_Deals_2_Damage_Per_NonTardigrade_Level2Plus_Own_Creature_At_Cleanup()
    {
        var state = NewGame(); // p1 = Wolf, p2 = Armadillo - the ability itself doesn't care which Champion fields it
        var queue = new AbilityQueue();
        state.CurrentStep = TurnStep.Attack; // TurnEngine.CleanUp's own required entry step

        var basilisk = ActiveCharacter(state, DiceKingdomConfig.Basilisk.Id, "p1", level: 2); // qualifies itself: L2, non-Tardigrade
        var qualifyingAlly = ActiveCharacter(state, DiceKingdomConfig.HoneyBadger.Id, "p1", level: 2); // L2, non-Tardigrade - qualifies
        var tooLowLevel = ActiveCharacter(state, DiceKingdomConfig.Wolverine.Id, "p1", level: 1); // L1 - must NOT qualify

        // A level 2 Tardigrade (PoolDieId, no CardId - IsSidekick, tagged
        // "sidekick") sitting right in the Field Zone - must NOT qualify
        // either, which is the whole point of the Tags: NoneOf:["sidekick"]
        // clause: v3's own Tardigrades DO level past 1, unlike DPS103
        // Colossus's classic Sidekicks, which never needed this exclusion.
        state.Dice.Add(new DieInstance
        {
            Id = "p1-tardigrade-l2-test", PoolDieId = "TardigradeClaw", OwnerId = "p1",
            ControllerId = "p1", Zone = Zone.FieldZone, CurrentFaceIndex = 4, // its L2 face
        });

        var lifeBefore = state.PlayerTwo.Life;
        TurnEngine.CleanUp(state, queue);
        Drain(state, queue);

        // Exactly 2 qualifying dice (Basilisk itself + qualifyingAlly) x
        // 2 damage each = 4 - tooLowLevel and the L2 Tardigrade both
        // correctly excluded.
        Assert.Equal(lifeBefore - 4, state.PlayerTwo.Life);
    }

    // Proves CountUnit.EnergySymbols really SUMS pips shown across every
    // matching die, not just the die COUNT - the whole point of "Lantern
    // Ring"-style scaling (Model/Effects/EffectNode.cs's own GrantAbility
    // remarks). Dice are built directly on specific faces (rather than
    // via TardigradeEnergy, which always lands on the 2-pip L1 face) so
    // the fielding-cost payer is an EXACT match for Phoenix's cost (1) -
    // SpendEnergy only spins a die down to a lower face on an OVERSPEND
    // (rule 2.6.1.4), and an exact match is fully consumed instead, so
    // there's no ambiguity about whether that same die also lingers in
    // the Reserve Pool to be double-counted by its own trigger.
    [Fact]
    public void Phoenix_Deals_Damage_Equal_To_Every_Energy_Symbol_Left_In_The_Reserve_Pool_When_Fielded()
    {
        var state = NewGame(); // p1 = Wolf, p2 = Armadillo - the ability itself doesn't care which Champion fields it
        var queue = new AbilityQueue();

        var twoL1Dice = TardigradeEnergy(state, "p1", "Claw", 2); // two dice on the 2-pip L1 face = 4 pips
        var extraPip = new DieInstance
        {
            Id = "p1-extra-pip", PoolDieId = "TardigradeClaw", OwnerId = "p1",
            ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 2, // L2, 1 pip
        };
        state.Dice.Add(extraPip);
        var fieldingPayer = new DieInstance
        {
            Id = "p1-fielding-payer", PoolDieId = "TardigradeClaw", OwnerId = "p1",
            ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 3, // L2, 1 pip - exact match for cost 1
        };
        state.Dice.Add(fieldingPayer);

        var phoenix = ReadyCharacter(state, DiceKingdomConfig.Phoenix.Id, "p1"); // fielding cost 1 at level 1

        var lifeBefore = state.PlayerTwo.Life;
        TurnEngine.Field(state, queue, phoenix.Id, [fieldingPayer.Id]);
        Drain(state, queue);

        Assert.Equal(Zone.OutOfPlay, state.Dice.Single(d => d.Id == fieldingPayer.Id).Zone); // spent in full, not spun down (and so no longer in the Reserve Pool at all)
        // 4 (the two L1 dice) + 1 (extraPip's L2 face) = 5 pips still
        // sitting in the Reserve Pool once fieldingPayer has left it.
        Assert.Equal(lifeBefore - 5, state.PlayerTwo.Life);
    }
}
