using DiceFight.Api;
using DiceFight.V2.Data;
using Microsoft.AspNetCore.Mvc;

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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pending_Choice_Dto_Preserves_Confirmation_Kind(bool confirmation)
    {
        var choice = new DiceFight.V2.Model.PendingChoice
        {
            ControllerId = "teamA", Description = "Optional ability", CandidateIds = ["teamA"],
            MinCount = 0, MaxCount = 1, Resolve = _ => { }, IsConfirmation = confirmation,
        };
        var dto = V2PendingChoiceDto.From(choice);
        Assert.Equal(confirmation, dto.IsConfirmation);
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dto,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
        Assert.Equal(confirmation, json.RootElement.GetProperty("isConfirmation").GetBoolean());
    }
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

    [Theory]
    [InlineData(101, 8)]
    [InlineData(20, 101)]
    [InlineData(int.MaxValue, 8)]
    public void Create_Rejects_A_Custom_Team_Over_The_Dice_Ceiling(int cardDice, int tardigrades)
    {
        var store = new V2GameStore();
        var custom = new CustomV2TeamRequest(
            [new(DiceKingdomConfig.Mongoose.Id, cardDice)],
            [new("Claw", tardigrades)]);
        var result = V2SeatedController.Anonymous(store).Create(
            new CreateV2GameRequest(PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: custom));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(store.Sessions);
    }

    [Fact]
    public void Create_Allows_A_Large_House_Format_Custom_Team()
    {
        // e.g. 12 Tardigrades and 30 dice - the builder's formats, not the server's.
        var store = new V2GameStore();
        var custom = new CustomV2TeamRequest(
            [new(DiceKingdomConfig.Mongoose.Id, 15), new(DiceKingdomConfig.AngerIssues.Id, 15)],
            [new("Claw", 6), new("Wing", 6)]);
        var created = V2SeatedController.CreatedDto(V2SeatedController.Anonymous(store).Create(
            new CreateV2GameRequest(PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: custom)));

        Assert.Equal(42, store.GetSession(created.Game.GameId).State.Dice.Count(d => d.OwnerId == "teamA"));
    }

    [Fact]
    public void Create_Accepts_All_Eight_Completed_Imports_Through_Normal_Custom_Team_Setup()
    {
        string[] ids = ["MSW020", "JLL002", "TAG003", "SKC090", "CW014", "DPS029", "DPS061", "DPS093"];
        var store = new V2GameStore();
        var custom = new CustomV2TeamRequest(
            ids.Select(id => new CustomV2CardDice(id, 1)).ToList(), [new("Wing", 8)]);
        var created = V2SeatedController.CreatedDto(V2SeatedController.Anonymous(store).Create(
            new CreateV2GameRequest(PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: custom)));
        var state = store.GetSession(created.Game.GameId).State;

        foreach (var id in ids)
        {
            Assert.True(state.CardCatalog[id].IsImplemented);
            Assert.Same(DiceKingdomConfig.TeamBuilderCatalog[id], state.CardCatalog[id]);
            Assert.Single(state.Dice, die => die.OwnerId == "teamA" && die.CardId == id);
        }
        Assert.Equal(8, state.Dice.Count(die => die.OwnerId == "teamA" && die.CardId is null));
    }

    [Fact]
    public void Protection_Imports_Are_Playable_And_Expose_Their_Typed_And_Generic_Globals()
    {
        var store = new V2GameStore();
        var controller = V2SeatedController.Anonymous(store);
        var cards = Assert.IsAssignableFrom<IReadOnlyList<V2CardDefDto>>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.Cards().Result).Value);
        var frigate = Assert.Single(cards, card => card.Id == "DPS033");
        var saiga = Assert.Single(cards, card => card.Id == "DPS113");
        var bison = Assert.Single(cards, card => card.Id == "DPS148");
        Assert.All(new[] { frigate, saiga, bison }, card => Assert.True(card.IsImplemented));
        Assert.Equal((1, "Eye"), (frigate.Global!.Cost, frigate.Global.EnergyType));
        Assert.Equal((1, "Shell"), (saiga.Global!.Cost, saiga.Global.EnergyType));
        Assert.Equal(3, bison.Global!.Cost);
        Assert.Null(bison.Global.EnergyType);
        var created = V2SeatedController.CreatedDto(controller.Create(new CreateV2GameRequest(
            PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: new CustomV2TeamRequest(
                [new("DPS033", 1), new("DPS113", 1), new("DPS148", 1)], [new("Eye", 4), new("Shell", 4)]))));
        Assert.All(new[] { "DPS033", "DPS113", "DPS148" }, id =>
            Assert.Single(created.Game.Dice, die => die.OwnerId == "teamA" && die.CardId == id));
    }

    [Theory]
    [InlineData("DPS033", "Eye")]
    [InlineData("DPS113", "Shell")]
    public void Protection_Global_Uses_Normal_Controller_Flow_And_Returns_An_Explained_Status(string cardId, string energy)
    {
        var store = new V2GameStore();
        var created = V2SeatedController.CreatedDto(V2SeatedController.Anonymous(store).Create(
            new CreateV2GameRequest(PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: new CustomV2TeamRequest(
                [new(cardId, 1), new("DPS148", 1)], [new(energy, 8)]))));
        var session = store.GetSession(created.Game.GameId);
        var state = session.State;
        state.MoveToStep(DiceFight.V2.Model.StepIds.Main);
        var target = state.Dice.First(die => die.OwnerId == "teamA" && die.CardId == "DPS148");
        target.Zone = DiceFight.V2.Model.Zone.FieldZone;
        target.CurrentFaceIndex = 2;
        var payer = state.Dice.First(die => die.OwnerId == "teamA" && die.CardId is null);
        payer.Zone = DiceFight.V2.Model.Zone.ReservePool;
        payer.CurrentFaceIndex = 2;
        DiceFight.V2.Priority.Sync(state);
        var index = state.CardCatalog[cardId].Abilities.ToList().FindIndex(a => a.Trigger == DiceFight.V2.Model.Effects.TriggerKind.Global);
        var result = V2SeatedController.Dto(V2SeatedController.For(store, session, "teamA").UseGlobal(
            session.Id, new V2UseGlobalRequest(cardId, index, [payer.Id])));
        var die = Assert.Single(result.Dice, die => die.Id == target.Id);
        var status = Assert.Single(die.Statuses!, status => status.Kind == "targetProtected");
        Assert.Equal("turn", status.Duration);
        Assert.Equal(state.CardCatalog[cardId].Name, status.Source);
        Assert.Contains("either player's", status.Description);
        Assert.False(DiceFight.V2.QueryEngine.CanBeTargeted(state, target, "teamA", DiceFight.V2.Model.Effects.ProtectionFrom.Global));
        Assert.False(DiceFight.V2.QueryEngine.CanBeTargeted(state, target, "teamB", DiceFight.V2.Model.Effects.ProtectionFrom.Global));
    }

    [Fact]
    public void Elder_Batch_Is_Playable_In_Custom_Teams_And_Uses_Elder_In_Public_Card_Data()
    {
        string[] ids = ["DPS057", "DPS114", "DPS102", "DPS142", "DPS071", "DPS038", "GOTG105", "DPS031", "DPS059", "DPS099"];
        var store = new V2GameStore(); var controller = V2SeatedController.Anonymous(store);
        var cards = Assert.IsAssignableFrom<IReadOnlyList<V2CardDefDto>>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.Cards().Result).Value);
        foreach (var id in ids) Assert.True(Assert.Single(cards, c => c.Id == id).IsImplemented);
        foreach (var chunk in ids.Chunk(8))
        {
            var created = V2SeatedController.CreatedDto(controller.Create(new CreateV2GameRequest(
                PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: new CustomV2TeamRequest(
                    chunk.Select(id => new CustomV2CardDice(id, 1)).ToList(), [new("Wing", 8)]))));
            foreach (var id in chunk) Assert.Single(created.Game.Dice, d => d.OwnerId == "teamA" && d.CardId == id);
        }
        Assert.All(cards, c => Assert.DoesNotContain("Founder", c.Keywords));
        foreach (var id in new[] { "DPS057", "DPS114", "DPS142" })
        {
            var card = Assert.Single(cards, c => c.Id == id);
            Assert.Contains("Elder", card.Keywords); Assert.Contains("Elder", card.RawText);
        }
        var secretary = Assert.Single(cards, c => c.Id == "DPS038");
        Assert.Equal((1, "Wing", true), (secretary.Global!.Cost, secretary.Global.EnergyType, secretary.Global.OncePerTurn));
    }

    [Fact]
    public void Controller_Fielding_Preserves_Partial_Payment_Reactions_And_Unused_Selected_Dice()
    {
        var store = new V2GameStore();
        var created = V2SeatedController.CreatedDto(V2SeatedController.Anonymous(store).Create(new CreateV2GameRequest(
            PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: new CustomV2TeamRequest(
                [new("DPS031", 1), new("DPS059", 2)], [new("Wing", 8)]))));
        var session = store.GetSession(created.Game.GameId); var state = session.State;
        state.MoveToStep(DiceFight.V2.Model.StepIds.Main);
        var woodpecker = state.Dice.First(d => d.OwnerId == "teamA" && d.CardId == "DPS031");
        woodpecker.Zone = DiceFight.V2.Model.Zone.ReservePool; woodpecker.CurrentFaceIndex = 0;
        var rats = state.Dice.Where(d => d.OwnerId == "teamA" && d.CardId == "DPS059").ToArray();
        foreach (var rat in rats) { rat.Zone = DiceFight.V2.Model.Zone.ReservePool; rat.CurrentFaceIndex = 3; }
        DiceFight.V2.Priority.Sync(state);
        var dto = V2SeatedController.Dto(V2SeatedController.For(store, session, "teamA").Field(
            session.Id, new V2FieldRequest(woodpecker.Id, [rats[0].Id, rats[1].Id])));
        var spent = Assert.Single(dto.Dice, d => d.Id == rats[0].Id);
        Assert.Equal("PrepArea", spent.Zone); Assert.Null(rats[0].CurrentFaceIndex);
        Assert.Equal("ReservePool", Assert.Single(dto.Dice, d => d.Id == rats[1].Id).Zone);
        Assert.Equal(2, state.GetCurrentFace(rats[1])!.SymbolCount);
        Assert.Equal(2, dto.Dice.Count(d => d.ControllerId == "teamA" && d.Zone == "PrepArea"));
        Assert.Equal("FieldZone", Assert.Single(dto.Dice, d => d.Id == woodpecker.Id).Zone);
    }

    [Fact]
    public void Controller_Purchase_Routes_An_Exclusive_Dung_Beetle_Payment_To_Prep_Once()
    {
        var store = new V2GameStore();
        var created = V2SeatedController.CreatedDto(V2SeatedController.Anonymous(store).Create(new CreateV2GameRequest(
            PlayerTwoChampionId: "Armadillo", PlayerOneCustomTeam: new CustomV2TeamRequest(
                [new("DPS099", 3)], [new("Wing", 8)]))));
        var session = store.GetSession(created.Game.GameId); var state = session.State;
        state.MoveToStep(DiceFight.V2.Model.StepIds.Main);
        var dice = state.Dice.Where(d => d.OwnerId == "teamA" && d.CardId == "DPS099").ToArray();
        dice[1].Zone = DiceFight.V2.Model.Zone.ReservePool; dice[1].CurrentFaceIndex = 3;
        dice[2].Zone = DiceFight.V2.Model.Zone.ReservePool; dice[2].CurrentFaceIndex = 5;
        DiceFight.V2.Priority.Sync(state);
        var dto = V2SeatedController.Dto(V2SeatedController.For(store, session, "teamA").Purchase(
            session.Id, new V2PurchaseRequest(dice[0].Id, [dice[1].Id, dice[2].Id])));
        Assert.Equal("PrepArea", Assert.Single(dto.Dice, d => d.Id == dice[0].Id).Zone);
        Assert.Single(dto.Dice, d => d.ControllerId == "teamA" && d.Zone == "PrepArea");
        Assert.Null(state.PendingChoice);
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

    [Fact]
    public void Cards_Endpoint_Exposes_Individual_Errata_And_Incomplete_Metadata()
    {
        var controller = V2SeatedController.Anonymous(new V2GameStore());
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.Cards().Result);
        var cards = Assert.IsAssignableFrom<IReadOnlyList<V2CardDefDto>>(result.Value);
        Assert.Equal(132, cards.Count);
        Assert.Equal(44, cards.Count(card => !card.IsImplemented));
        Assert.Equal(34, cards.Count(card => card.HasErrata));
        Assert.DoesNotContain(cards, card => card.Id == CardCatalog.CosmicCube.Id);
        Assert.DoesNotContain(cards, card => card.Id == DpsCards.TightRanks.Id);
        Assert.DoesNotContain(cards, card => card.Id == DpsCards.Radicalization.Id);
        Assert.DoesNotContain(cards, card => card.Id == DpsCards.DampeningCollar.Id);
        Assert.DoesNotContain(cards, card => card.Id == DpsCards.BlobImmovable.Id);
        Assert.DoesNotContain(cards, card => card.Id == DpsCards.EmmaFrostInfluential.Id);
        Assert.DoesNotContain(cards, card => card.Id == DpsCards.DKenMKraanCrystal.Id);
        var beast = Assert.Single(cards.Where(card => card.Id == "MSW019"));
        Assert.True(beast.HasErrata);
        Assert.False(beast.IsImplemented);
        Assert.Equal("Dice Kingdom", beast.Set);
        Assert.Equal("Axolotl", beast.Name);
        Assert.Equal(new[] { "Shell" }, beast.EnergyTypes);
        Assert.Equal("Character", beast.Type);
        Assert.Equal(CardCatalog.Beast.Die.Faces.Where(face => face.Character is not null).Select(face => face.Burst),
            beast.Levels.Select(level => level.Burst));
    }

    [Fact]
    public void Cards_Endpoint_Exposes_The_Errata_Flag_But_Never_Internal_Notes()
    {
        var controller = V2SeatedController.Anonymous(new V2GameStore());
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.Cards().Result);
        var cards = Assert.IsAssignableFrom<IReadOnlyList<V2CardDefDto>>(result.Value);
        var imported = Assert.Single(cards, card => card.Id == DpsCards.OrganicSteelPreventDamage.Id);
        Assert.True(imported.HasErrata);
        Assert.Equal("Protective Bark", imported.Name);
        Assert.DoesNotContain("gain 1 life", imported.RawText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Prevent up to 2 damage", imported.RawText);
        var wildfire = Assert.Single(cards, card => card.Id == DpsCards.Explosion.Id);
        Assert.True(wildfire.HasErrata);
        Assert.DoesNotContain("**", wildfire.RawText);
        Assert.DoesNotContain("additional damage", wildfire.RawText);
        Assert.Contains("Deal 2 damage", wildfire.RawText);
        Assert.Contains("may deal 1 damage", wildfire.RawText);
        Assert.DoesNotContain(cards, card => card.Id == DpsCards.Archnemesis.Id);
        Assert.Equal(DiceKingdomConfig.Archnemesis.Id, Assert.Single(cards, card => card.Name == "Archnemesis").Id);
        var json = System.Text.Json.JsonSerializer.Serialize(cards, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.All(document.RootElement.EnumerateArray(), card => Assert.False(card.TryGetProperty("errataNotes", out _)));
        Assert.DoesNotContain("rollinthunder_cards_with_dice", json);
        foreach (var card in DiceKingdomConfig.TeamBuilderCatalog.Values.Where(card => card.HasErrata))
            foreach (var note in card.ErrataNotes!)
                Assert.DoesNotContain(note, json);
    }

    [Fact]
    public void Incomplete_Cards_Cannot_Start_A_Custom_Game_Even_Through_A_Direct_Request()
    {
        var controller = V2SeatedController.Anonymous(new V2GameStore());
        var custom = new CustomV2TeamRequest([new("MSW019", 1)],
            [new("Claw", 8)]);
        var response = controller.Create(new CreateV2GameRequest(PlayerTwoChampionId: "Wolf", PlayerOneCustomTeam: custom));
        var error = Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(response.Result);
        var json = System.Text.Json.JsonSerializer.Serialize(error.Value);
        Assert.Contains("Incomplete", json);
    }
}
