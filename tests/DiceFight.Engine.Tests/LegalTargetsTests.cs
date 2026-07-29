using DiceFight.Engine.Effects;
using DiceFight.Engine.Model;
using Xunit;

namespace DiceFight.Engine.Tests;

public class LegalTargetsTests
{
    private static readonly CardDef MaskCard = new()
    {
        Id = "mask-card", Name = "Mask Card", Type = CardType.Character, PurchaseCost = 3,
        EnergyTypes = [EnergyType.Mask], DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)]
    };

    private static readonly CardDef BoltCard = new()
    {
        Id = "bolt-card", Name = "Bolt Card", Type = CardType.Character, PurchaseCost = 3,
        EnergyTypes = [EnergyType.Bolt], DieLimit = 4,
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)]
    };

    private static readonly CardDef AllyCard = new()
    {
        Id = "ally-card", Name = "Ally Card", Type = CardType.Character, PurchaseCost = 2,
        EnergyTypes = [EnergyType.Shield], DieLimit = 4,
        Keywords = [new KeywordInstance("Ally")],
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)]
    };

    // Darkseid-style "while active, your Sidekicks gain [keyword]" granter.
    private static readonly CardDef GranterCard = new()
    {
        Id = "granter-card", Name = "Granter Card", Type = CardType.Character, PurchaseCost = 2,
        EnergyTypes = [EnergyType.Bolt], DieLimit = 4,
        GrantsToSidekicks = ["Swarm"],
        Levels = [new CharacterFace(FieldingCost: 0, Attack: 1, Defense: 1)]
    };

    private static GameState CreateState()
    {
        var catalog = new Dictionary<string, CardDef>
        {
            [MaskCard.Id] = MaskCard, [BoltCard.Id] = BoltCard, [AllyCard.Id] = AllyCard, [GranterCard.Id] = GranterCard,
        };
        return GameState.NewGame(catalog, new Player { Id = "p1", Name = "P1" }, new Player { Id = "p2", Name = "P2" });
    }

    private static DieInstance AddDie(GameState state, string id, string playerId, Zone zone, DieStatus status, string? cardId = null)
    {
        var die = new DieInstance { Id = id, CardId = cardId, OwnerId = playerId, ControllerId = playerId, Zone = zone, Status = status };
        state.Dice.Add(die);
        return die;
    }

    [Fact]
    public void Query_FiltersByOwnership()
    {
        var state = CreateState();
        var own = AddDie(state, "p1-own", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        var opposing = AddDie(state, "p2-opp", "p2", Zone.FieldZone, DieStatus.SidekickCharacter);

        var ownOnly = LegalTargets.Query(state, "p1", TargetSpec.CharacterDie("x", TargetOwnership.Own));
        var opposingOnly = LegalTargets.Query(state, "p1", TargetSpec.CharacterDie("x", TargetOwnership.Opposing));
        var any = LegalTargets.Query(state, "p1", TargetSpec.CharacterDie("x"));

        Assert.Equal([own.Id], ownOnly);
        Assert.Equal([opposing.Id], opposingOnly);
        Assert.Equal(2, any.Count);
    }

    [Fact]
    public void Query_DefaultZones_ExcludeDiceOutsideFieldAndAttackZone()
    {
        var state = CreateState();
        AddDie(state, "p1-bag", "p1", Zone.Bag, DieStatus.Unrolled);
        AddDie(state, "p1-reserve", "p1", Zone.ReservePool, DieStatus.SidekickCharacter);
        var field = AddDie(state, "p1-field", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        var attack = AddDie(state, "p1-attack", "p1", Zone.AttackZone, DieStatus.SidekickCharacter);

        var legal = LegalTargets.Query(state, "p2", TargetSpec.CharacterDie("x"));

        Assert.Equal(new[] { field.Id, attack.Id }.OrderBy(x => x), legal.OrderBy(x => x));
    }

    [Fact]
    public void Query_CharacterDiceOnly_ExcludesEnergyAndActionFaces()
    {
        var state = CreateState();
        var character = AddDie(state, "p1-char", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        AddDie(state, "p1-energy", "p1", Zone.FieldZone, DieStatus.Energy);

        var legal = LegalTargets.Query(state, "p2", TargetSpec.CharacterDie("x"));

        Assert.Equal([character.Id], legal);
    }

    [Fact]
    public void Query_AnyDie_IncludesNonCharacterFaces()
    {
        var state = CreateState();
        var character = AddDie(state, "p1-char", "p1", Zone.ReservePool, DieStatus.SidekickCharacter);
        var energy = AddDie(state, "p1-energy", "p1", Zone.ReservePool, DieStatus.Energy);

        var legal = LegalTargets.Query(state, "p2", TargetSpec.AnyDie("x", TargetOwnership.Any, [Zone.ReservePool]));

        Assert.Equal(new[] { character.Id, energy.Id }.OrderBy(x => x), legal.OrderBy(x => x));
    }

    [Fact]
    public void Query_RequiredEnergyType_OnlyMatchesCardsWithThatType()
    {
        var state = CreateState();
        var maskDie = AddDie(state, "p1-mask", "p1", Zone.FieldZone, DieStatus.Character, MaskCard.Id);
        AddDie(state, "p1-bolt", "p1", Zone.FieldZone, DieStatus.Character, BoltCard.Id);
        AddDie(state, "p1-sidekick", "p1", Zone.FieldZone, DieStatus.SidekickCharacter); // no card, no energy type

        var legal = LegalTargets.Query(state, "p2", TargetSpec.CharacterDie("x", energyType: EnergyType.Mask));

        Assert.Equal([maskDie.Id], legal);
    }

    [Fact]
    public void Interpreter_RejectsAChosenTargetThatIsNotLegal()
    {
        var state = CreateState();
        var illegalTarget = AddDie(state, "p1-bag", "p1", Zone.Bag, DieStatus.Unrolled); // wrong zone

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("x")),
            new EffectContext(state, "p2", SourceDieId: null, _ => [illegalTarget.Id])));

        Assert.Contains("not legal", ex.Message);
    }

    [Fact]
    public void Interpreter_RequiresChoosingUpToCount_WhenEnoughLegalTargetsExist()
    {
        var state = CreateState();
        AddDie(state, "p1-a", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        AddDie(state, "p1-b", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);

        var ex = Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("x", count: 2)),
            new EffectContext(state, "p2", SourceDieId: null, _ => []))); // chose none, but 2 are legal

        Assert.Contains("needs 2 target(s)", ex.Message);
    }

    // TargetSpec.Optional (e.g. Cosmic Cube's "you may send ANY NUMBER of
    // them") - unlike the mandatory-up-to-Count case above, choosing
    // fewer than the legal count (including zero) is never an error.
    [Fact]
    public void Interpreter_OptionalSpec_AllowsChoosingNone_EvenWhenLegalTargetsExist()
    {
        var state = CreateState();
        AddDie(state, "p1-a", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        AddDie(state, "p1-b", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);

        EffectInterpreter.Execute(
            new Ko(TargetSpec.AnyDie("x", TargetOwnership.Any, TargetSpec.DefaultZones, count: 2, optional: true)),
            new EffectContext(state, "p2", SourceDieId: null, _ => [])); // chose none - no exception

        // Neither die was targeted, so neither was KO'd.
        Assert.DoesNotContain(state.Dice, d => d.Zone == Zone.PrepArea);
    }

    [Fact]
    public void Interpreter_AllowsFewerThanCount_WhenFewerLegalTargetsExist()
    {
        var state = CreateState();
        var only = AddDie(state, "p1-a", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);

        // Spec asks for 2, but only 1 legal target exists - rule 3.3.11.
        EffectInterpreter.Execute(
            new Ko(TargetSpec.CharacterDie("x", count: 2)),
            new EffectContext(state, "p2", SourceDieId: null, _ => [only.Id]));

        Assert.Equal(Zone.PrepArea, only.Zone);
    }

    [Fact]
    public void Interpreter_NoLegalTargets_SkipsSilentlyRatherThanThrowing()
    {
        var state = CreateState(); // no dice anywhere eligible

        EffectInterpreter.Execute(
            new DealDamage(1, TargetSpec.CharacterDie("x")),
            new EffectContext(state, "p2", SourceDieId: null, _ => []));

        // No exception, no-op - rule 3.1.10.
    }

    [Fact]
    public void Query_SidekicksOnly_IncludesRealSidekicksAndFieldedAllyDice()
    {
        var state = CreateState();
        var realSidekick = AddDie(state, "p1-sidekick", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        var allyFielded = AddDie(state, "p1-ally-fielded", "p1", Zone.FieldZone, DieStatus.Character, AllyCard.Id);
        var allyAttacking = AddDie(state, "p1-ally-attacking", "p1", Zone.AttackZone, DieStatus.Character, AllyCard.Id);
        AddDie(state, "p1-nonally-char", "p1", Zone.FieldZone, DieStatus.Character, MaskCard.Id);

        var legal = LegalTargets.Query(state, "p2", TargetSpec.Sidekick("x"));

        Assert.Equal(
            new[] { realSidekick.Id, allyFielded.Id, allyAttacking.Id }.OrderBy(x => x),
            legal.OrderBy(x => x));
    }

    // Rule Appendix 1 Ally note: "They don't count as Sidekick dice while
    // in the bag, Prep Area, Used Pile, or Reserve Pool" - only the Field
    // Zone (including the Attack Zone) grants the equivalence.
    [Fact]
    public void Query_SidekicksOnly_ExcludesAllyDiceOutsideTheFieldZone()
    {
        var state = CreateState();
        var allyInBag = AddDie(state, "p1-ally-bag", "p1", Zone.Bag, DieStatus.Unrolled, AllyCard.Id);
        var allyInReserve = AddDie(state, "p1-ally-reserve", "p1", Zone.ReservePool, DieStatus.Character, AllyCard.Id);
        var allyInUsedPile = AddDie(state, "p1-ally-used", "p1", Zone.UsedPile, DieStatus.Unrolled, AllyCard.Id);
        var allyInPrep = AddDie(state, "p1-ally-prep", "p1", Zone.PrepArea, DieStatus.Unrolled, AllyCard.Id);

        // GameState.NewGame already seeds each player's Bag with real
        // physical Sidekick dice (zone-independent per DieInstance.
        // IsSidekick), so the query isn't expected to come back empty -
        // just to never include the four Ally dice planted above.
        var legal = LegalTargets.Query(
            state, "p2",
            TargetSpec.Sidekick("x", zones: [Zone.Bag, Zone.ReservePool, Zone.UsedPile, Zone.PrepArea]));

        Assert.DoesNotContain(allyInBag.Id, legal);
        Assert.DoesNotContain(allyInReserve.Id, legal);
        Assert.DoesNotContain(allyInUsedPile.Id, legal);
        Assert.DoesNotContain(allyInPrep.Id, legal);
        Assert.All(
            new[] { allyInBag, allyInReserve, allyInUsedPile, allyInPrep },
            d => Assert.False(DieStats.CountsAsSidekick(state, d)));
    }

    // Darkseid-style "while active, your Sidekicks gain [keyword]"
    // (CardDef.GrantsToSidekicks) - a static, live-recomputed grant, not
    // a discrete triggered ability. These exercise DieStats.HasKeyword's
    // grant path directly, independent of Swarm or ClearAndDraw.
    [Fact]
    public void HasKeyword_GrantedToARealSidekick_WhenGranterIsActive()
    {
        var state = CreateState();
        AddDie(state, "p1-granter", "p1", Zone.FieldZone, DieStatus.Character, GranterCard.Id);
        var sidekick = AddDie(state, "p1-sidekick", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);

        Assert.True(DieStats.HasKeyword(state, sidekick, "Swarm"));
    }

    [Fact]
    public void HasKeyword_NotGranted_WhenGranterIsNotActive()
    {
        var state = CreateState();
        AddDie(state, "p1-granter", "p1", Zone.Bag, DieStatus.Unrolled, GranterCard.Id); // not active
        var sidekick = AddDie(state, "p1-sidekick", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);

        Assert.False(DieStats.HasKeyword(state, sidekick, "Swarm"));
    }

    // The interesting case: an Ally die counts as a Sidekick while active
    // (DieStats.CountsAsSidekick), so a granter's "your Sidekicks gain
    // [keyword]" reaches it too - but only while it's actually active,
    // same as any other Sidekick-counting condition.
    [Fact]
    public void HasKeyword_GrantedToAnActiveAllyDie_ButNotWhileInTheBag()
    {
        var state = CreateState();
        AddDie(state, "p1-granter", "p1", Zone.FieldZone, DieStatus.Character, GranterCard.Id);
        var activeAlly = AddDie(state, "p1-ally-active", "p1", Zone.FieldZone, DieStatus.Character, AllyCard.Id);
        var allyInBag = AddDie(state, "p1-ally-bag", "p1", Zone.Bag, DieStatus.Unrolled, AllyCard.Id);

        Assert.True(DieStats.HasKeyword(state, activeAlly, "Swarm"));
        Assert.False(DieStats.HasKeyword(state, allyInBag, "Swarm"));
    }

    // Checking "Ally" itself must never recurse into the grant path
    // (CountsAsSidekick is how an Ally die reaches the grant path in the
    // first place) - this would stack-overflow if that guard were missing.
    [Fact]
    public void HasKeyword_CheckingAllyItself_DoesNotRecurse()
    {
        var state = CreateState();
        AddDie(state, "p1-granter", "p1", Zone.FieldZone, DieStatus.Character, GranterCard.Id);
        var activeAlly = AddDie(state, "p1-ally-active", "p1", Zone.FieldZone, DieStatus.Character, AllyCard.Id);

        Assert.True(DieStats.HasKeyword(state, activeAlly, "Ally")); // printed, resolves without recursing
    }

    // Keyword Attune's "target player or character die" - a single
    // choice between the two, not two separate targets (see TargetSpec.
    // CharacterDieOrPlayer's remarks).
    [Fact]
    public void Query_CharacterDieOrPlayer_IncludesBothMatchingDiceAndPlayerIds()
    {
        var state = CreateState();
        var ownDie = AddDie(state, "p1-char", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        var opposingDie = AddDie(state, "p2-char", "p2", Zone.FieldZone, DieStatus.SidekickCharacter);
        AddDie(state, "p1-energy", "p1", Zone.FieldZone, DieStatus.Energy); // not a character face - excluded

        var any = LegalTargets.Query(state, "p1", TargetSpec.CharacterDieOrPlayer("x"));

        Assert.Equal(
            new[] { ownDie.Id, opposingDie.Id, "p1", "p2" }.OrderBy(x => x),
            any.OrderBy(x => x));
    }

    [Fact]
    public void Query_CharacterDieOrPlayer_RespectsOwnershipForBothDiceAndThePlayerId()
    {
        var state = CreateState();
        var ownDie = AddDie(state, "p1-char", "p1", Zone.FieldZone, DieStatus.SidekickCharacter);
        AddDie(state, "p2-char", "p2", Zone.FieldZone, DieStatus.SidekickCharacter);

        var ownOnly = LegalTargets.Query(state, "p1", TargetSpec.CharacterDieOrPlayer("x", TargetOwnership.Own));

        Assert.Equal(new[] { ownDie.Id, "p1" }.OrderBy(x => x), ownOnly.OrderBy(x => x));
    }

    [Fact]
    public void Interpreter_SelfBypassesLegalTargetFiltering()
    {
        var state = CreateState();
        var sourceDie = AddDie(state, "p1-source", "p1", Zone.OutOfPlay, DieStatus.Action);

        // Self would fail every LegalTargets filter (wrong zone, not a
        // character die) if it went through the normal path.
        EffectInterpreter.Execute(
            new PrepDie(TargetSpec.Self),
            new EffectContext(state, "p1", sourceDie.Id, _ => []));

        Assert.Equal(Zone.PrepArea, sourceDie.Zone);
    }
}
