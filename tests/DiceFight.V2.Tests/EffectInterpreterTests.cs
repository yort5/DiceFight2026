using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Tests;

// V2_PLAN.md Phase 5 acceptance: every effect template and condition has
// at least one passing test (a happy path, plus a "no legal target ->
// rule 3.1.10 skip" case for every template that resolves a TargetFilter).
public class EffectInterpreterTests
{
    private sealed class FixedRoller(int index) : IDiceRoller
    {
        public int Roll(DieDefinition die) => index;
    }

    private static readonly Face Level1Char = new([], new CharacterFaceData(Level: 1, FieldingCost: 0, Attack: 2, Defense: 2));
    private static readonly Face Level2Char = new([], new CharacterFaceData(Level: 2, FieldingCost: 1, Attack: 3, Defense: 3));
    private static readonly Face FistEnergy1 = new([new SymbolAmount("Fist", 1)]);
    private static readonly Face FistEnergy2 = new([new SymbolAmount("Fist", 2)]);
    private static readonly Face BurstSingle = new([], new CharacterFaceData(1, 0, 2, 2), Burst: 1);
    private static readonly Face BurstDouble = new([], new CharacterFaceData(1, 0, 2, 2), Burst: 2);

    private static GameConfig BuildConfig() => new(
        Id: "test", Name: "Test",
        EnergySymbols: [new SymbolDef("Fist")],
        Keywords: [],
        Rules: new RulesConfig(StartingLife: 10, DrawCount: 3, MaxTeamCards: 4, MaxTeamDice: 4, BasicActionCount: 0),
        BasicDicePool: [],
        BasicActionSlots: 0);

    private static readonly EffectNode StubEffect = new LifeChange(new Fixed(0));

    private static CardDef BuildCard(string id, IReadOnlyList<Face> faces, int purchaseCost = 1, IReadOnlyList<string>? affiliations = null, CardType type = CardType.Character, IReadOnlyList<TriggerKind>? reactsTo = null, IReadOnlyList<ContinuousDef>? continuous = null) => new(
        Id: id, Name: id, Subtitle: null, Set: "TEST", CardType: type,
        PurchaseCost: purchaseCost, EnergySymbolIds: ["Fist"],
        Die: new DieDefinition(id + "Die", faces),
        DieLimit: 1, Affiliations: affiliations ?? [], Keywords: [], RawText: "",
        // reactsTo wires up self-only (null Filter) TriggeredAbilities with
        // a no-op effect, purely so EventBus.Fire has something to match
        // against - EventBus.Fire itself only enqueues MATCHED listeners,
        // it doesn't enqueue the raw event, so a test proving "this
        // fires event X" needs a real listener, same as production.
        Abilities: reactsTo?.Select(t => new TriggeredAbility(t, StubEffect)).ToList() ?? [],
        Continuous: continuous ?? []);

    private static GameState BuildState(params CardDef[] cards)
    {
        var state = new GameState
        {
            Config = BuildConfig(),
            CardCatalog = cards.ToDictionary(c => c.Id),
            PlayerOne = new Player { Id = "p1", Name = "One", Life = 10 },
            PlayerTwo = new Player { Id = "p2", Name = "Two", Life = 10 },
            ActivePlayerId = "p1",
            CurrentStep = TurnStep.Attack,
        };
        // A no-op for every card in this file that declares no Continuous
        // entries, which is all of them but the static-aura test below.
        ContinuousRegistry.RegisterAll(state);
        return state;
    }

    private static DieInstance AddDie(GameState state, CardDef card, string controllerId, Zone zone, int? faceIndex, string? id = null)
    {
        var die = new DieInstance { Id = id ?? card.Id + "-die", CardId = card.Id, OwnerId = controllerId, ControllerId = controllerId, Zone = zone, CurrentFaceIndex = faceIndex };
        state.Dice.Add(die);
        return die;
    }

    private static EffectContext BuildContext(GameState state, string controllerId, string? sourceDieId = null, IDiceRoller? roller = null, TriggerKind trigger = TriggerKind.Global)
    {
        var ctx = new EffectContext { State = state, Queue = new AbilityQueue(), ControllerId = controllerId, Trigger = trigger, Roller = roller ?? new FixedRoller(0), Random = new Random(1) };
        if (sourceDieId is not null) ctx.Bind("self", sourceDieId); // Bind, not a raw write - captures stats too
        return ctx;
    }

    // --- DealDamage ---

    [Fact]
    public void DealDamage_Applies_Amount_And_Fires_DieDamaged()
    {
        var card = BuildCard("T", [Level1Char], reactsTo: [TriggerKind.DieDamaged]);
        var state = BuildState(card);
        var target = AddDie(state, card, "p2", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new DealDamage(new Fixed(1), new TargetFilter(Ownership: TargetOwnership.Opposing)), ctx);

        Assert.Equal(1, target.Damage);
        var pending = Assert.Single(ctx.Queue.Pending);
        Assert.Equal(TriggerKind.DieDamaged, pending.Trigger);
    }

    [Fact]
    public void DealDamage_Meeting_Defense_KOs_The_Target()
    {
        var card = BuildCard("T", [Level1Char], reactsTo: [TriggerKind.DieKOd]); // 2 Defense
        var state = BuildState(card);
        var target = AddDie(state, card, "p2", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new DealDamage(new Fixed(2), new TargetFilter(Ownership: TargetOwnership.Opposing)), ctx);

        Assert.Equal(Zone.PrepArea, target.Zone);
        Assert.Null(target.CurrentFaceIndex);
        Assert.Contains(ctx.Queue.Pending, pending => pending.Trigger == TriggerKind.DieKOd);
    }

    [Fact]
    public void DealDamage_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card); // no dice at all
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new DealDamage(new Fixed(2), new TargetFilter(Ownership: TargetOwnership.Opposing)), ctx);

        Assert.Empty(ctx.Queue.Pending);
        Assert.Null(state.PendingChoice);
    }

    [Fact]
    public void DealDamage_Distribute_Splits_The_Amount_Across_Repeated_Choices()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var a = AddDie(state, card, "p2", Zone.FieldZone, 0, "a");
        var b = AddDie(state, card, "p2", Zone.FieldZone, 0, "b");
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new DealDamage(new Fixed(2), new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0), Distribute: true), ctx);

        Assert.NotNull(state.PendingChoice);
        EffectInterpreter.AnswerPendingChoice(state, [a.Id]);
        Assert.NotNull(state.PendingChoice); // one point still left to assign
        EffectInterpreter.AnswerPendingChoice(state, [b.Id]);
        Assert.Null(state.PendingChoice);

        Assert.Equal(1, a.Damage);
        Assert.Equal(1, b.Damage);
    }

    // --- Ko ---

    [Fact]
    public void Ko_Moves_The_Target_To_Prep_Area_And_Fires_DieKOd()
    {
        var card = BuildCard("T", [Level1Char], reactsTo: [TriggerKind.DieKOd]);
        var state = BuildState(card);
        var target = AddDie(state, card, "p2", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new Ko(new TargetFilter(Ownership: TargetOwnership.Opposing)), ctx);

        Assert.Equal(Zone.PrepArea, target.Zone);
        Assert.Null(target.CurrentFaceIndex);
        var pending = Assert.Single(ctx.Queue.Pending);
        Assert.Equal(TriggerKind.DieKOd, pending.Trigger);
    }

    [Fact]
    public void Ko_With_TriggersKOAbilities_False_Does_Not_Fire_DieKOd()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var ctx = BuildContext(state, "p1", sourceDieId: "self");

        EffectInterpreter.Execute(new Ko(new TargetFilter(Self: true), TriggersKOAbilities: false), ctx);

        Assert.Empty(ctx.Queue.Pending);
    }

    [Fact]
    public void Ko_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new Ko(new TargetFilter(Ownership: TargetOwnership.Opposing)), ctx);

        Assert.Empty(ctx.Queue.Pending);
    }

    // --- MoveDie ---

    [Fact]
    public void MoveDie_Moves_A_Dormant_Die_Into_The_Field_And_Assigns_A_Default_Face()
    {
        var card = BuildCard("T", [Level1Char, FistEnergy1]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.UsedPile, null);
        var ctx = BuildContext(state, "p1");

        // Kind: AnyDie - a dormant Used Pile die has no current face, so
        // the default CharacterDie kind (which reads the CURRENT face,
        // matching in-play/combat semantics) would never match it; the
        // vocabulary's own Rally example (V2_VOCABULARY.md Part 2) uses
        // AnyDie for exactly this reason when reaching into dormant zones.
        EffectInterpreter.Execute(new MoveDie(new TargetFilter(Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Kind: TargetKind.AnyDie), Zone.FieldZone), ctx);

        Assert.Equal(Zone.FieldZone, die.Zone);
        Assert.NotNull(die.CurrentFaceIndex);
        Assert.NotNull(state.GetCurrentFace(die)!.Character);
    }

    [Fact]
    public void MoveDie_Leaving_The_Field_Resets_Damage_And_Modifiers()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.FieldZone, 0);
        die.Damage = 1;
        die.AppliedModifiers.Add(new AppliedModifier(1, 0, 0, "test", Duration.Permanent));
        var ctx = BuildContext(state, "p1", sourceDieId: die.Id);

        EffectInterpreter.Execute(new MoveDie(new TargetFilter(Self: true), Zone.UsedPile), ctx);

        Assert.Equal(Zone.UsedPile, die.Zone);
        Assert.Equal(0, die.Damage);
        Assert.Empty(die.AppliedModifiers);
    }

    [Fact]
    public void MoveDie_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new MoveDie(new TargetFilter(Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Kind: TargetKind.AnyDie), Zone.FieldZone), ctx);

        Assert.Empty(state.Dice);
    }

    // --- DrawToZone ---

    [Fact]
    public void DrawToZone_Landing_In_ReservePool_Rolls_The_Drawn_Dice()
    {
        var card = BuildCard("T", [Level1Char, FistEnergy1]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.Bag, null, "a");
        var ctx = BuildContext(state, "p1", roller: new FixedRoller(1));

        EffectInterpreter.Execute(new DrawToZone(1, Zone.ReservePool), ctx);

        var drawn = Assert.Single(state.DiceIn("p1", Zone.ReservePool));
        Assert.Equal(1, drawn.CurrentFaceIndex);
    }

    [Fact]
    public void DrawToZone_With_An_Empty_Source_Zone_Draws_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new DrawToZone(2, Zone.PrepArea), ctx);

        Assert.Empty(state.Dice);
    }

    // --- FieldDie ---

    [Fact]
    public void FieldDie_Moves_To_Field_At_The_Requested_Level_And_Fires_DieFielded()
    {
        var card = BuildCard("T", [Level1Char, Level2Char], reactsTo: [TriggerKind.DieFielded]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.UsedPile, null);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new FieldDie(new TargetFilter(Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Kind: TargetKind.AnyDie), Free: true, Level: 2), ctx);

        Assert.Equal(Zone.FieldZone, die.Zone);
        Assert.Equal(2, state.GetCurrentFace(die)!.Character!.Level);
        Assert.Contains("p1", state.FieldedCharacterThisTurn);
        Assert.Single(ctx.Queue.Pending);
    }

    [Fact]
    public void FieldDie_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new FieldDie(new TargetFilter(Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile]), Free: true), ctx);

        Assert.Empty(ctx.Queue.Pending);
    }

    // --- Reroll ---

    [Fact]
    public void Reroll_Landing_Non_Character_Moves_The_Die_And_Deals_Damage()
    {
        var card = BuildCard("T", [Level1Char, FistEnergy1], reactsTo: [TriggerKind.DieFaceChanged]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var ctx = BuildContext(state, "p1", sourceDieId: "self", roller: new FixedRoller(1)); // lands on the energy face

        EffectInterpreter.Execute(new Reroll(new TargetFilter(Self: true), NonCharacterMoveTo: Zone.UsedPile, DamagePerMoved: 1), ctx);

        var die = state.Dice.Single();
        Assert.Equal(Zone.UsedPile, die.Zone);
        Assert.Equal(9, state.PlayerTwo.Life);
        Assert.Single(ctx.Queue.Pending); // DieFaceChanged
    }

    [Fact]
    public void Reroll_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new Reroll(new TargetFilter(Ownership: TargetOwnership.Own)), ctx);

        Assert.Empty(state.Dice);
    }

    // --- Spin ---

    [Fact]
    public void Spin_LevelDelta_Moves_To_An_Adjacent_Level_Face()
    {
        var card = BuildCard("T", [Level1Char, Level2Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var ctx = BuildContext(state, "p1", sourceDieId: "self");

        EffectInterpreter.Execute(new Spin(new TargetFilter(Self: true), LevelDelta: 1), ctx);

        Assert.Equal(2, state.GetCurrentFace(state.Dice.Single())!.Character!.Level);
    }

    [Fact]
    public void Spin_SetLevel_Clamps_To_The_Dies_Own_Available_Levels()
    {
        var card = BuildCard("T", [Level1Char, Level2Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var ctx = BuildContext(state, "p1", sourceDieId: "self");

        EffectInterpreter.Execute(new Spin(new TargetFilter(Self: true), SetLevel: 99), ctx);

        Assert.Equal(2, state.GetCurrentFace(state.Dice.Single())!.Character!.Level); // clamped to the die's max
    }

    [Fact]
    public void Spin_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new Spin(new TargetFilter(Ownership: TargetOwnership.Own), LevelDelta: 1), ctx);

        Assert.Empty(state.Dice);
    }

    // --- SpinToEnergy ---

    [Fact]
    public void SpinToEnergy_Spins_To_The_Face_Matching_The_Requested_Amount()
    {
        var card = BuildCard("T", [Level1Char, FistEnergy1, FistEnergy2]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var ctx = BuildContext(state, "p1", sourceDieId: "self");

        EffectInterpreter.Execute(new SpinToEnergy(new TargetFilter(Self: true), Amount: 2), ctx);

        var face = state.GetCurrentFace(state.Dice.Single())!;
        Assert.Null(face.Character);
        Assert.Equal(2, face.Symbols.Single().Count);
    }

    [Fact]
    public void SpinToEnergy_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char, FistEnergy1]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new SpinToEnergy(new TargetFilter(Ownership: TargetOwnership.Own)), ctx);

        Assert.Empty(state.Dice);
    }

    // --- ModifyStat ---

    [Fact]
    public void ModifyStat_Delta_Applies_As_An_AppliedModifier()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own), AtkDelta: 2), ctx);

        Assert.Equal(4, QueryEngine.GetAttack(state, die));
    }

    [Fact]
    public void ModifyStat_SetAttack_Achieves_The_Absolute_Value()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own), SetAttack: new Fixed(9)), ctx);

        Assert.Equal(9, QueryEngine.GetAttack(state, die));
    }

    // The game's applied-vs-static distinction (user ruling, 2026-08-24):
    // a "set" replaces the die's OWN value (printed + applied), and
    // conditional static auras recompute on top of the new value.
    //
    // The user's own worked example: Lois Lane gives other SuperFriends
    // +1A while attacking. An attacking 4A SuperFriend shows 5A. Swapping
    // its attack with a 1A Sidekick leaves the SuperFriend at 2A - the
    // 1A swapped in, plus Lois's +1A again, because it is still a
    // SuperFriend and still attacking. (Computing the set against the
    // static-INCLUSIVE attack instead would land it at 1A - which is
    // exactly what this engine did until this test was written.)
    [Fact]
    public void ModifyStat_Set_Replaces_The_Dies_Own_Value_And_Static_Auras_Recompute_On_Top()
    {
        var loisLane = BuildCard("LoisLane", [Level1Char], continuous:
            [new StatAura(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Affiliations: new TagQuery(AnyOf: ["SuperFriends"])), AtkDelta: new Fixed(1))]);
        var superFriend = BuildCard("SuperFriend", [new Face([], new CharacterFaceData(1, 0, 4, 4))], affiliations: ["SuperFriends"]);
        var state = BuildState(loisLane, superFriend);
        AddDie(state, loisLane, "p1", Zone.FieldZone, 0, "lois");
        var hero = AddDie(state, superFriend, "p1", Zone.FieldZone, 0, "hero");

        Assert.Equal(5, QueryEngine.GetAttack(state, hero)); // 4 printed + 1 static from Lois
        Assert.Equal(4, QueryEngine.GetBaseAttack(state, hero)); // the die's OWN value

        // Swap in the Sidekick's 1A.
        var ctx = BuildContext(state, "p1", sourceDieId: hero.Id);
        EffectInterpreter.Execute(new ModifyStat(new TargetFilter(Self: true), SetAttack: new Fixed(1)), ctx);

        Assert.Equal(1, QueryEngine.GetBaseAttack(state, hero)); // own value really is 1 now
        Assert.Equal(2, QueryEngine.GetAttack(state, hero)); // ...and Lois's +1 applies again on top
    }

    // --- Spike B: live-value Amounts (StatOf / EventValue) ---

    // Archnemesis (DPS001)'s Global shape: "target character die has D
    // equal to its A". The binding is made by the SAME node that uses it -
    // ResolveTarget binds before the effect callback runs.
    [Fact]
    public void StatOf_Can_Set_One_Stat_From_Another_On_The_Same_Die()
    {
        var card = BuildCard("T", [new Face([], new CharacterFaceData(1, 0, 6, 2))]); // 6A/2D
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own, BindAs: "t"),
            SetDefense: new StatOf("t", StatKind.Attack)), ctx);

        Assert.Equal(6, QueryEngine.GetDefense(state, die));
    }

    // The mechanism itself, via Rogue "Mrs. X"'s swap shape. This only
    // comes out right because StatOf captures AT BIND TIME: step 1 binds
    // "other" (snapshotting 5A) and immediately overwrites it with self's
    // 2A; step 2 reads "other"'s CAPTURED 5A, not the 2A just written.
    // A use-time read would leave BOTH dice on 2A.
    [Fact]
    public void StatOf_Captures_At_Bind_Time_So_A_Two_Way_Swap_Is_Really_A_Swap()
    {
        var mine = BuildCard("Mine", [new Face([], new CharacterFaceData(1, 0, 2, 3))]); // 2A
        var theirs = BuildCard("Theirs", [new Face([], new CharacterFaceData(1, 0, 5, 4))]); // 5A
        var state = BuildState(mine, theirs);
        var self = AddDie(state, mine, "p1", Zone.FieldZone, 0, "self");
        var other = AddDie(state, theirs, "p2", Zone.FieldZone, 0, "other");
        var ctx = BuildContext(state, "p1", sourceDieId: "self");

        EffectInterpreter.Execute(new Sequence([
            new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, BindAs: "other"),
                SetAttack: new StatOf("self", StatKind.Attack)),
            new ModifyStat(new TargetFilter(Self: true), SetAttack: new StatOf("other", StatKind.Attack)),
        ]), ctx);

        Assert.Equal(5, QueryEngine.GetAttack(state, self));
        Assert.Equal(2, QueryEngine.GetAttack(state, other));
    }

    [Fact]
    public void StatOf_Against_An_Unbound_Name_Throws_Rather_Than_Reading_Zero()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        Assert.Throws<InvalidOperationException>(() => EffectInterpreter.Execute(
            new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own), SetAttack: new StatOf("nope", StatKind.Attack)), ctx));
    }

    // EventValue, through the real firing path: a die reacting to its own
    // DieDamaged deals "that much" damage onward. No migrated card can use
    // this yet (Dark Phoenix "Destructive Force" also needs damage-SOURCE
    // visibility, which no payload carries - see V2_TAIL_POLICY.md), so
    // the card here is synthetic.
    [Fact]
    public void EventValue_Reads_The_Triggering_Events_Own_Damage_Amount()
    {
        var retaliator = BuildCard("Retaliator", [new Face([], new CharacterFaceData(1, 0, 1, 99))]) with
        {
            // DealDamage at a player, not LifeChange - LifeChange's Amount
            // is signed and a positive value GAINS life, so "deal that much
            // damage" is the damage template with a Player target.
            Abilities = [new TriggeredAbility(TriggerKind.DieDamaged,
                new DealDamage(new EventValue(), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        };
        var state = BuildState(retaliator);
        var die = AddDie(state, retaliator, "p1", Zone.FieldZone, 0);
        var queue = new AbilityQueue();

        EffectInterpreter.ApplyDamage(state, queue, DamageSource.Ability, die.Id, 3);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

        // p1 controls the retaliator, so "Opposing" is p2: 10 - 3.
        Assert.Equal(7, state.PlayerTwo.Life);
    }

    [Fact]
    public void EventValue_Outside_A_Numeric_Event_Throws_Rather_Than_Reading_Zero()
    {
        var state = BuildState();
        var ctx = BuildContext(state, "p1");

        Assert.Throws<InvalidOperationException>(() =>
            EffectInterpreter.Execute(new DealDamage(new EventValue(), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)), ctx));
    }

    [Fact]
    public void ModifyStat_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new ModifyStat(new TargetFilter(Ownership: TargetOwnership.Own), AtkDelta: 2), ctx);

        Assert.Empty(state.Dice);
    }

    // --- GrantTag ---

    [Fact]
    public void GrantTag_Adds_A_Tag_That_Expires_At_CleanUp()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new GrantTag(new TargetFilter(Ownership: TargetOwnership.Own), ["Overcrush"]), ctx);
        Assert.Contains("Overcrush", QueryEngine.GetTags(state, die));

        TurnEngine.CleanUp(state, new AbilityQueue());
        Assert.DoesNotContain("Overcrush", QueryEngine.GetTags(state, die));
    }

    [Fact]
    public void GrantTag_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new GrantTag(new TargetFilter(Ownership: TargetOwnership.Own), ["Overcrush"]), ctx);

        Assert.Empty(state.Dice);
    }

    // --- GrantAbility (V2_VOCABULARY.md Parts 16, 20-21) ---

    [Fact]
    public void GrantAbility_Adds_An_Ability_That_Expires_At_CleanUp()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");
        var granted = new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(0)));

        EffectInterpreter.Execute(new GrantAbility(new TargetFilter(Ownership: TargetOwnership.Own), granted), ctx);
        Assert.Contains(granted, QueryEngine.AbilitiesOf(state, die));

        TurnEngine.CleanUp(state, new AbilityQueue());
        Assert.DoesNotContain(granted, QueryEngine.AbilitiesOf(state, die));
    }

    // Rule 3.4.5.4's reasoning, the same one GrantedTags already follows:
    // a die that leaves active play loses what was hung on it, whatever
    // duration it was granted for.
    [Fact]
    public void A_Granted_Ability_Is_Lost_When_The_Die_Leaves_Active_Play()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");
        var granted = new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(0)));

        EffectInterpreter.Execute(new GrantAbility(new TargetFilter(Ownership: TargetOwnership.Own), granted, Duration.Permanent), ctx);
        Assert.Contains(granted, QueryEngine.AbilitiesOf(state, die));

        EffectInterpreter.Execute(new Ko(new TargetFilter(Ownership: TargetOwnership.Own)), ctx);

        Assert.Empty(die.GrantedAbilities);
    }

    // --- LifeChange ---

    [Fact]
    public void LifeChange_Positive_Amount_Gains_Life_For_The_Controller()
    {
        var state = BuildState();
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new LifeChange(new Fixed(3)), ctx);

        Assert.Equal(13, state.PlayerOne.Life);
    }

    [Fact]
    public void LifeChange_Negative_Amount_Against_Opposing_Loses_Life_For_The_Opponent()
    {
        var state = BuildState();
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new LifeChange(new Fixed(-3), Whose: TargetOwnership.Opposing), ctx);

        Assert.Equal(7, state.PlayerTwo.Life);
    }

    // --- PurchaseModifier ---

    [Fact]
    public void PurchaseModifier_Discounts_And_Is_Consumed_By_The_Next_Matching_Purchase()
    {
        var card = BuildCard("T", [Level1Char], purchaseCost: 3);
        var pool = new DieDefinition("Energy2", [FistEnergy2]);
        var config = BuildConfig() with { BasicDicePool = [new BasicDicePoolEntry(pool, 1)] };
        var state = new GameState
        {
            Config = config,
            CardCatalog = new Dictionary<string, CardDef> { [card.Id] = card },
            PlayerOne = new Player { Id = "p1", Name = "One" },
            PlayerTwo = new Player { Id = "p2", Name = "Two" },
            ActivePlayerId = "p1",
            CurrentStep = TurnStep.Main,
        };
        var ctx = BuildContext(state, "p1");
        EffectInterpreter.Execute(new PurchaseModifier(Delta: -1), ctx);
        Assert.Single(state.PendingPurchaseModifiers);

        var energyDie = new DieInstance { Id = "e1", PoolDieId = pool.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        var toBuy = new DieInstance { Id = "unpurchased", CardId = card.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.Unpurchased };
        state.Dice.Add(energyDie);
        state.Dice.Add(toBuy);

        TurnEngine.Purchase(state, new AbilityQueue(), "unpurchased", ["e1"]); // costs 2 (3 - 1), covered by the 2-Fist pool die

        Assert.Equal(Zone.UsedPile, toBuy.Zone);
        Assert.Empty(state.PendingPurchaseModifiers); // consumed
    }

    // --- CombatFlag ---

    [Fact]
    public void CombatFlag_Records_The_Flag_On_The_Target_Until_CleanUp()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var die = AddDie(state, card, "p2", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new CombatFlag(new TargetFilter(Ownership: TargetOwnership.Opposing), CombatFlagKind.CantBlock), ctx);
        Assert.Contains(CombatFlagKind.CantBlock, die.CombatFlags);

        TurnEngine.CleanUp(state, new AbilityQueue());
        Assert.Empty(die.CombatFlags);
    }

    [Fact]
    public void CombatFlag_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new CombatFlag(new TargetFilter(Ownership: TargetOwnership.Opposing), CombatFlagKind.CantBlock), ctx);

        Assert.Empty(state.Dice);
    }

    // --- Sequence ---

    [Fact]
    public void Sequence_Runs_Every_Step_In_Order()
    {
        var state = BuildState();
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new Sequence([
            new LifeChange(new Fixed(1)),
            new LifeChange(new Fixed(1)),
        ]), ctx);

        Assert.Equal(12, state.PlayerOne.Life);
    }

    // --- MayPay ---

    [Fact]
    public void MayPay_Accepted_Applies_The_Cost_Then_The_Effect()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var ctx = BuildContext(state, "p1", sourceDieId: "self");

        EffectInterpreter.Execute(new MayPay(new LifeChange(new Fixed(-1)), new LifeChange(new Fixed(3))), ctx);
        Assert.NotNull(state.PendingChoice);
        EffectInterpreter.AnswerPendingChoice(state, ["self"]);

        Assert.Equal(12, state.PlayerOne.Life); // -1 then +3
    }

    [Fact]
    public void MayPay_Declined_Applies_Neither_Cost_Nor_Effect()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var ctx = BuildContext(state, "p1", sourceDieId: "self");

        EffectInterpreter.Execute(new MayPay(new LifeChange(new Fixed(-1)), new LifeChange(new Fixed(3))), ctx);
        EffectInterpreter.AnswerPendingChoice(state, []);

        Assert.Equal(10, state.PlayerOne.Life);
        Assert.Null(state.PendingChoice);
    }

    // --- Conditional ---

    [Fact]
    public void Conditional_Runs_Then_When_True_And_Else_When_False()
    {
        var state = BuildState();
        state.PlayerOne.Life = 5;
        state.PlayerTwo.Life = 10;
        var ctxTrue = BuildContext(state, "p1");
        EffectInterpreter.Execute(new Conditional(new LifeComparison(), new LifeChange(new Fixed(1)), new LifeChange(new Fixed(-1))), ctxTrue);
        Assert.Equal(6, state.PlayerOne.Life);

        state.PlayerOne.Life = 10;
        state.PlayerTwo.Life = 5;
        var ctxFalse = BuildContext(state, "p1");
        EffectInterpreter.Execute(new Conditional(new LifeComparison(), new LifeChange(new Fixed(1)), new LifeChange(new Fixed(-1))), ctxFalse);
        Assert.Equal(9, state.PlayerOne.Life);
    }

    // --- DrawAndChooseOne ---

    [Fact]
    public void DrawAndChooseOne_Sends_The_Chosen_Die_And_Rest_To_Their_Own_Zones()
    {
        var card = BuildCard("T", [Level1Char, FistEnergy1]);
        var state = BuildState(card);
        AddDie(state, card, "p2", Zone.Bag, null, "a");
        AddDie(state, card, "p2", Zone.Bag, null, "b");
        var ctx = BuildContext(state, "p1", roller: new FixedRoller(0));

        EffectInterpreter.Execute(new DrawAndChooseOne(2, TargetOwnership.Opposing, Zone.UsedPile, Zone.Bag), ctx);

        Assert.NotNull(state.PendingChoice);
        Assert.Equal("p1", state.PendingChoice!.ControllerId); // the ability's own controller chooses
        var candidate = state.PendingChoice.CandidateIds[0];
        EffectInterpreter.AnswerPendingChoice(state, [candidate]);

        var chosen = state.Dice.Single(d => d.Id == candidate);
        Assert.Equal(Zone.UsedPile, chosen.Zone);
        var rest = state.Dice.Single(d => d.Id != candidate);
        Assert.Equal(Zone.Bag, rest.Zone);
    }

    [Fact]
    public void DrawAndChooseOne_With_An_Empty_Bag_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new DrawAndChooseOne(2, TargetOwnership.Opposing, Zone.UsedPile, Zone.Bag), ctx);

        Assert.Null(state.PendingChoice);
    }

    // --- GrantCounter ---

    [Fact]
    public void GrantCounter_Adds_To_The_Targets_Own_Card_Scoped_Counter()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new GrantCounter(new TargetFilter(Ownership: TargetOwnership.Own), "Loyalty", 1), ctx);
        EffectInterpreter.Execute(new GrantCounter(new TargetFilter(Ownership: TargetOwnership.Own), "Loyalty", 1), ctx);

        Assert.Equal(2, state.Counters[("p1", "T", "Loyalty")]);
    }

    [Fact]
    public void GrantCounter_With_No_Legal_Target_Does_Nothing()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        var ctx = BuildContext(state, "p1");

        EffectInterpreter.Execute(new GrantCounter(new TargetFilter(Ownership: TargetOwnership.Own), "Loyalty", 1), ctx);

        Assert.Empty(state.Counters);
    }

    // --- Conditions (one test per kind) ---

    [Fact]
    public void CountAtLeast_Checks_A_Live_Count_Of_Matches()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p2", Zone.FieldZone, 0, "a");
        AddDie(state, card, "p2", Zone.FieldZone, 0, "b");

        var filter = new TargetFilter(Ownership: TargetOwnership.Opposing);
        Assert.True(ConditionEvaluator.Evaluate(state, "p1", new CountAtLeast(filter, 2), new Dictionary<string, string>()));
        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new CountAtLeast(filter, 3), new Dictionary<string, string>()));
    }

    [Fact]
    public void TargetWasKOd_Is_True_Only_After_The_Bound_Die_Was_Actually_KOd()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var bindings = new Dictionary<string, string> { ["self"] = "self" };

        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new TargetWasKOd("self"), bindings));

        var ctx = BuildContext(state, "p1", sourceDieId: "self");
        EffectInterpreter.Execute(new Ko(new TargetFilter(Self: true)), ctx);

        Assert.True(ConditionEvaluator.Evaluate(state, "p1", new TargetWasKOd("self"), bindings));
    }

    [Fact]
    public void OnBurstFace_Checks_The_Bound_Dies_Current_Burst_Level()
    {
        var card = BuildCard("T", [BurstSingle, BurstDouble]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var bindings = new Dictionary<string, string> { ["self"] = "self" };

        Assert.True(ConditionEvaluator.Evaluate(state, "p1", new OnBurstFace(BurstLevel.Single, "self"), bindings));
        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new OnBurstFace(BurstLevel.Double, "self"), bindings));
    }

    [Fact]
    public void LifeComparison_Checks_Own_Life_Against_The_Opponents()
    {
        var state = BuildState();
        state.PlayerOne.Life = 5;
        state.PlayerTwo.Life = 10;
        Assert.True(ConditionEvaluator.Evaluate(state, "p1", new LifeComparison(), new Dictionary<string, string>()));

        state.PlayerOne.Life = 10;
        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new LifeComparison(), new Dictionary<string, string>()));
    }

    [Fact]
    public void NoKOsThisTurn_Flips_False_Once_A_Character_Die_Is_KOd()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        Assert.True(ConditionEvaluator.Evaluate(state, "p1", new NoKOsThisTurn(KoScope.Any), new Dictionary<string, string>()));

        var ctx = BuildContext(state, "p1", sourceDieId: "self");
        EffectInterpreter.Execute(new Ko(new TargetFilter(Self: true)), ctx);

        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new NoKOsThisTurn(KoScope.Any), new Dictionary<string, string>()));
    }

    [Fact]
    public void TurnFact_PrepAreaEmpty_Reads_Live_Board_State()
    {
        var card = BuildCard("T", [Level1Char]);
        var state = BuildState(card);
        Assert.True(ConditionEvaluator.Evaluate(state, "p1", new TurnFact(TurnFactKind.PrepAreaEmpty), new Dictionary<string, string>()));

        AddDie(state, card, "p1", Zone.PrepArea, null);
        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new TurnFact(TurnFactKind.PrepAreaEmpty), new Dictionary<string, string>()));
    }

    [Fact]
    public void OnFaceKind_Checks_The_Bound_Dies_Current_Face_Kind()
    {
        var card = BuildCard("T", [Level1Char, FistEnergy1]);
        var state = BuildState(card);
        AddDie(state, card, "p1", Zone.FieldZone, 0, "self");
        var bindings = new Dictionary<string, string> { ["self"] = "self" };

        Assert.True(ConditionEvaluator.Evaluate(state, "p1", new OnFaceKind(FaceKind.CharacterFace, "self"), bindings));
        Assert.False(ConditionEvaluator.Evaluate(state, "p1", new OnFaceKind(FaceKind.EnergyFace, "self"), bindings));
    }

    // --- Ground rule 6: an ability resolved through the REAL firing path
    // (TurnEngine.Field -> EventBus -> AbilityQueue -> DrainQueue), not a
    // directly-invoked EffectContext. ---

    [Fact]
    public void An_Ability_Enqueued_By_A_Real_Fielding_Actually_Applies_Through_DrainQueue()
    {
        var watcher = BuildCard("Watcher", [Level1Char]) with
        {
            Abilities = [new TriggeredAbility(TriggerKind.DieFielded, new GrantTag(new TargetFilter(Self: true), ["Overcrush"]))],
        };
        var state = BuildState(watcher);
        state.CurrentStep = TurnStep.Main; // TurnEngine.Field requires the Main step
        var die = new DieInstance { Id = "w1", CardId = watcher.Id, OwnerId = "p1", ControllerId = "p1", Zone = Zone.ReservePool, CurrentFaceIndex = 0 };
        state.Dice.Add(die);
        var queue = new AbilityQueue();

        TurnEngine.Field(state, queue, die.Id, []);
        EffectInterpreter.DrainQueue(state, queue, new FixedRoller(0), new Random(1));

        Assert.Contains("Overcrush", QueryEngine.GetTags(state, die));
    }
}
