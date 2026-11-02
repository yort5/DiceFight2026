using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// Boss-only cards (2026-11-01, user: "We will likely need to create new
// cards for these. They won't have to be in Set 1 (i.e., they aren't
// attainable), and they may have to be modified in order to work with the
// Game engine"). The three boss teams (DiceKingdomBosses) are the user's
// own Dice Masters teams; their cards that Dice Kingdom already had an
// ability for reuse that card (Colossus -> Basilisk, Blob -> Pangolin, Big
// Entrance -> Spring Emergence, Eddie Guerrero -> Meerkat, Counterstrike ->
// Hibernation) and these are the rest.
//
// In no set, no rarity, not in packs, and not in the Team Builder or any
// player's team (V2GamesController refuses them) - only a boss plays them.
// They are in the catalog all the same, so a game and its card details
// can show them. Written in the source card's terms like KingdomImports;
// ToKingdomCard renames the character and the energy.
public static class BossCards
{
    // ---- Sidekicks ----

    // Moose: a bull moose gets more dangerous once it's hurt.
    public static readonly CardDef MrFixitMuscleForHire = new(
        Id: "THOR036", Name: "Mr. Fixit", Subtitle: "Muscle for Hire", Set: "THOR", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Fist"],
        Die: MigrationDice.Character("THOR036Die", "Fist", (1, 6, 5), (2, 7, 7), (3, 8, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When Mr. Fixit is damaged, he gets +XA, where X is his printed A (until end of turn).",
        Abilities: [new TriggeredAbility(TriggerKind.DieDamaged,
            new ModifyStat(new TargetFilter(Self: true), AttackBonus: new StatOf("self", StatKind.PrintedAttack)))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Moose", KingdomEnergyType: "Claw");

    // Tiger: the ambush - bought with Claw, it springs straight out.
    public static readonly CardDef SuperwomanReignOfTerror = new(
        Id: "WF129", Name: "Superwoman", Subtitle: "Reign of Terror", Set: "WF", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("WF129Die", "Bolt", (1, 4, 2), (2, 5, 3), (3, 7, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Impulse"],
        RawText: "Impulse: if you spent at least 3 Bolt energy to purchase Superwoman, you may Prep this die and deal 2 damage to each opposing character die.",
        Abilities: [new TriggeredAbility(TriggerKind.PurchaseMade,
            new MayPay(null, new Sequence([
                new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Self: true, Zones: [Zone.UsedPile, Zone.Bag]), Zone.PrepArea),
                new DealDamage(new Fixed(2), new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0)),
            ]), Prompt: "Impulse: Prep this die and deal 2 damage to each opposing creature?"),
            Filter: new EventFilter(RequireSelf: true, Payment: new PaymentFilter(PaymentPurpose.Purchase, SymbolId: "Bolt", MinSymbols: 3)))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Tiger", KingdomEnergyType: "Claw", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, WF129 (Superwoman: Reign of Terror).",
            "2 damage to each opposing character (the source: 3 to Villains or the Batman Family); the energy-converting Global dropped."]);

    // Stinging Nettle.
    public static readonly CardDef MagicMissile = BasicAction("TOA011", "Magic Missile", "TOA", 3,
        "Deal 2 damage to target character die or the opponent. Global: Pay Bolt. Deal 1 damage to target character die.",
        [
            new TriggeredAbility(TriggerKind.DieUsed, new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDieOrOpponent,
                Prompt: "deal 2 damage to the opponent or a target creature."))),
            new TriggeredAbility(TriggerKind.Global, new DealDamage(new Fixed(1), new TargetFilter(Prompt: "deal 1 damage to a target creature.")),
                EnergyCost: new EnergyCost(1, "Bolt")),
        ],
        "Stinging Nettle", "Claw", "The ** bonus (your highest-level Adventurer) dropped - Dice Kingdom has no Adventurers.");

    // ---- X-Men Colossus ----

    // Lyrebird: copies any sound it hears.
    public static readonly CardDef HopeSummersPluripotentEchopraxia = new(
        Id: "XMF020", Name: "Hope Summers", Subtitle: "Pluripotent Echopraxia", Set: "XMF", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shield"],
        Die: MigrationDice.Character("XMF020Die", "Shield", (0, 1, 2), (0, 2, 2), (1, 2, 2)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, choose another creature card on your team. Lyrebird uses its On Field ability now and has its other abilities until a Lyrebird is fielded again.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Sequence([
            new RememberCard(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own,
                Zones: [Zone.Unpurchased, Zone.Bag, Zone.UsedPile, Zone.PrepArea, Zone.ReservePool, Zone.FieldZone, Zone.AttackZone, Zone.OutOfPlay],
                Tags: new TagQuery(NoneOf: ["sidekick", "Lyrebird"]),
                Prompt: "choose another card on your team - Lyrebird copies it."), "copied"),
            new UseRememberedOnField("copied"),
        ]))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Lyrebird", KingdomEnergyType: "Eye", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, XMF020 (Hope Summers: Pluripotent Echopraxia).",
            "Copies the chosen card's abilities (its On Field once, now; the rest while active, until a Lyrebird is fielded again) but not its Globals; any card, not only X-Men."]);

    // Octopus: changes colour and shape to pass for something else - and
    // sometimes gets caught. ("Mimic Octopus" is an unfinished card already.)
    public static readonly CardDef MimicFlyingBlastingSlashing = new(
        Id: "DXM009", Name: "Mimic", Subtitle: "Flying, Blasting, Slashing", Set: "DXM", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("DXM009Die", "Mask", (0, 1, 1), (1, 1, 1), (1, 1, 2)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        // 2026-11-01 (user: Mimic "likely KOs itself and KOs an opposing
        // character"): back to the source's outcome - the target is always
        // KO'd, and Octopus goes too if either die rolls energy.
        RawText: "When fielded, reroll Octopus and target opposing creature. KO that creature. If either die rolled an energy face, KO Octopus too.",
        // No opposing creature to pick: nothing happens.
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Conditional(
            new CountAtLeast(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing), 1), new Sequence([
            new Reroll(new TargetFilter(Ownership: TargetOwnership.Opposing, BindAs: "prey",
                Prompt: "choose an opposing creature - both it and Octopus are rerolled, and it is KO'd.")),
            new Reroll(new TargetFilter(Self: true)),
            new Conditional(new OnFaceKind(FaceKind.CharacterFace, "self"),
                Then: new Conditional(new OnFaceKind(FaceKind.CharacterFace, "prey"),
                    Then: new Ko(new TargetFilter(Bound: "prey")),
                    Else: new Sequence([new Ko(new TargetFilter(Bound: "prey")), new Ko(new TargetFilter(Self: true))])),
                Else: new Sequence([new Ko(new TargetFilter(Bound: "prey")), new Ko(new TargetFilter(Self: true))])),
        ])))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Octopus", KingdomEnergyType: "Eye", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, DXM009 (Mimic: Flying, Blasting, Slashing).",
            "The copying part dropped."]);

    // Orangutan: the wise old one who brings the young along.
    public static readonly CardDef ProfessorXRecruitingYoungMutants = new(
        Id: "UXM049", Name: "Professor X", Subtitle: "Recruiting Young Mutants", Set: "UXM", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Character("UXM049Die", "Mask", (1, 1, 5), (2, 1, 7), (3, 1, 9)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, search your bag for up to 2 Sidekick dice and roll them into your Reserve Pool. Global: Pay Mask. Move up to 2 Sidekick dice from your Used Pile to your Prep Area.",
        Abilities: [
            // One Tardigrade per ability: targets are picked against a
            // snapshot taken as an ability starts, so a second pick in the
            // same ability would still see the first one in the bag.
            new TriggeredAbility(TriggerKind.DieFielded, RecruitTardigrade()),
            new TriggeredAbility(TriggerKind.DieFielded, RecruitTardigrade()),
            new TriggeredAbility(TriggerKind.Global, new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own,
                Zones: [Zone.UsedPile], Count: 2, Optional: true, Tags: new TagQuery(AnyOf: ["sidekick"]),
                Prompt: "choose up to 2 Tardigrades in your Used Pile to Prep."), Zone.PrepArea),
                EnergyCost: new EnergyCost(1, "Mask")),
        ],
        Continuous: [], IncludeInTeamBuilder: true,
        // Eye, like Velvet Worm (Beast) and Leaf Insect (Storm): all three
        // are Mask in the source team, where the cheap Mask dice feed this
        // Global every turn (user, 2026-11-02). It was Wing, which nothing
        // cheap on the team rolls.
        KingdomName: "Orangutan", KingdomEnergyType: "Eye");

    private static EffectNode RecruitTardigrade() => new Sequence([
        new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.Bag], Optional: true,
            Tags: new TagQuery(AnyOf: ["sidekick"]), BindAs: "recruit",
            Prompt: "choose a Tardigrade from your bag to roll into your Reserve Pool."), Zone.ReservePool),
        new Reroll(new TargetFilter(Kind: TargetKind.AnyDie, Bound: "recruit")),
    ]);

    // Echolocation: one click finds the target.
    public static readonly CardDef CerebroCyberneticIntelligence = new(
        Id: "UXM062", Name: "Cerebro", Subtitle: "Cybernetic Intelligence", Set: "UXM", CardType: CardType.Action,
        PurchaseCost: 5, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Action("UXM062Die", ["Mask"], 0, 1, 2),
        DieLimit: 1, Affiliations: [], Keywords: [],
        // 2026-11-01 (user: Cerebro "should _always_ be used ... every time
        // you field that character you get to knock out a die"): lasting,
        // as the source is, instead of once this turn. The source attaches
        // to an X-Men card of the player's choice; Dice Kingdom has no
        // X-Men, and this team's X-Men engine is Mimic, so it is Octopus.
        RawText: "For the rest of the game, whenever you field an Octopus die, KO target opposing creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new GrantTriggeredAbility(new TriggeredAbility(TriggerKind.DieFielded,
            new Ko(new TargetFilter(Ownership: TargetOwnership.Opposing, Prompt: "Echolocation - KO a target opposing creature.")),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, Tags: new TagQuery(AnyOf: ["Mimic"]))), Duration.Permanent))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Echolocation", KingdomEnergyType: "Eye", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, UXM062 (Cerebro: Cybernetic Intelligence).",
            "Fixed to Octopus (Mimic) - the source attaches to an X-Men card of your choice. Using it again adds nothing."]);

    // The Den: the safe place the young grow up.
    public static readonly CardDef XaviersSchoolHomeForGiftedYoungsters = new(
        Id: "XFC040", Name: "Xavier's School", Subtitle: "Home for Gifted Youngsters", Set: "XFC", CardType: CardType.Action,
        PurchaseCost: 3, EnergySymbolIds: ["Mask"],
        Die: MigrationDice.Action("XFC040Die", ["Mask"], 0, 1, 2),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Spin all of your character dice up 1 level. */** Also Prep a die from your bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
            new Spin(new TargetFilter(Ownership: TargetOwnership.Own, Count: 0), LevelDelta: 1),
            new Conditional(new OnBurstFace(BurstLevel.Single, AtLeast: true), new DrawToZone(1, Zone.PrepArea)),
        ]))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "The Den", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, XFC040 (Xavier's School: Home for Gifted Youngsters).",
            "The burst Preps the drawn die (the source fields an X-Men character for free at level 3)."]);

    // ---- Tag-Jubilee Bolt ----

    // Arctic Fox: the pounce through the snow.
    public static readonly CardDef IcemanTooCoolForWords = new(
        Id: "UXM009", Name: "Iceman", Subtitle: "Too Cool for Words", Set: "UXM", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("UXM009Die", "Bolt", (1, 2, 4), (1, 3, 6), (1, 4, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When Iceman attacks, you may pay 1 energy to double his A (until end of turn).",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new MayPayEnergy(1,
            new ModifyStat(new TargetFilter(Self: true), AttackBonus: new StatOf("self", StatKind.Attack)),
            Prompt: "pay 1 energy to double Arctic Fox's ATK?"))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Arctic Fox", KingdomEnergyType: "Claw", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, UXM009 (Iceman: Too Cool for Words).",
            "On Attack, any energy (the source: once per turn, a Bolt, at any time); the Sidekick-spinning Global dropped."]);

    // Slug: slow, and it slows whatever it touches.
    public static readonly CardDef IntellectDevourerBrainEatingMonster = new(
        Id: "TIW017", Name: "Intellect Devourer", Subtitle: "Brain Eating Monster", Set: "TIW", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Character("TIW017Die", "Bolt", (0, 1, 1), (0, 2, 2), (1, 5, 2)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, spin target character die down 1 level.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new Spin(new TargetFilter(Prompt: "spin a target creature down 1 level."), LevelDelta: -1))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Slug", KingdomEnergyType: "Eye", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, TIW017 (Intellect Devourer: Brain Eating Monster).",
            "On Field instead of Spark (Dice Kingdom dice have no bursts to trigger it); the NPC-spinning Global dropped."]);

    // Tsetse Fly: one bite and the sleeping sickness takes hold - the card
    // it names nods off. ("Mosquito" is an unfinished card already.)
    public static readonly CardDef TyphoidMaryRedRubberBoots = new(
        Id: "IG135", Name: "Typhoid Mary", Subtitle: "Red Rubber Boots", Set: "IG", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Bolt", "Mask"],
        Die: MigrationDice.Character("IG135Die", ["Bolt", "Mask"], (0, 1, 2), (1, 1, 4), (2, 4, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "When fielded, choose an opposing character card. Ignore that card's text (including Global Abilities) until your next turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new BlankCardText(
            new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing,
                Zones: [Zone.Unpurchased, Zone.Bag, Zone.UsedPile, Zone.PrepArea, Zone.ReservePool, Zone.FieldZone, Zone.AttackZone, Zone.OutOfPlay],
                Tags: new TagQuery(NoneOf: ["sidekick"]),
                Prompt: "name an opposing character - its text is ignored until your next turn."),
            Duration: Duration.UntilYourNextTurn))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Tsetse Fly", KingdomEnergyType: "Claw", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, IG135 (Typhoid Mary: Red Rubber Boots).",
            "Until your next turn (the source: while Typhoid Mary is active - a continuous text-ignoring lockout would ask itself whether it is active); one energy type, Claw (the source is Bolt/Mask); the Hand discount dropped."]);

    // Watering Hole: everyone comes down to drink.
    public static readonly CardDef YawningPortalComfortableInn = new(
        Id: "AIW024", Name: "Yawning Portal", Subtitle: "Comfortable Inn", Set: "AIW", CardType: CardType.Action,
        PurchaseCost: 2, EnergySymbolIds: ["Bolt"],
        Die: MigrationDice.Action("AIW024Die", ["Bolt"], 0, 1, 2),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Field all of your character dice in your Reserve Pool for free.",
        Abilities: [new TriggeredAbility(TriggerKind.DieUsed,
            new FieldDie(new TargetFilter(Ownership: TargetOwnership.Own, Zones: [Zone.ReservePool], Count: 0), Free: true))],
        Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: "Watering Hole", KingdomEnergyType: "Wing", HasErrata: true,
        ErrataNotes: ["Reference: BulkCards.json, AIW024 (Yawning Portal: Comfortable Inn).",
            "Fields what's in the Reserve Pool now (the source makes fielding free for the turn, discounts purchases per fielding, and bags them)."]);

    // Venus Flytrap.
    public static readonly CardDef TargetLock = BasicAction("HQ023", "Target Lock", "HQ", 3,
        "Move target die from your opponent's Prep Area to their Used Pile. Prep a die from your bag.",
        [new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.PrepArea],
                Prompt: "choose a die in your opponent's Prep Area - it goes to their Used Pile."), Zone.UsedPile),
            new DrawToZone(1, Zone.PrepArea),
        ]))],
        "Venus Flytrap", "Eye", "Acts on their Prep Area now (the source waits on a chosen card for their next Clear and Draw).");

    private static CardDef BasicAction(string id, string name, string set, int cost, string rawText,
        IReadOnlyList<TriggeredAbility> abilities, string kingdomName, string energy, string simplified) => new(
        Id: id, Name: name, Subtitle: "Basic Action", Set: set, CardType: CardType.BasicAction,
        PurchaseCost: cost, EnergySymbolIds: [],
        Die: MigrationDice.BasicAction(id + "Die", 0, 1, 2),
        DieLimit: 3, Affiliations: [], Keywords: [],
        RawText: rawText, Abilities: abilities, Continuous: [], IncludeInTeamBuilder: true,
        KingdomName: kingdomName, KingdomEnergyType: energy, HasErrata: true,
        ErrataNotes: [$"Reference: BulkCards.json, {id} ({name}).",
            $"Energy faces: Generic 2 / Generic 2 / Generic 2 -> {energy} 2 / {energy} 2 / {energy} 1; standardized to the Dice Kingdom action-die pattern.",
            simplified]);

    public static IReadOnlyList<CardDef> All =>
    [
        MrFixitMuscleForHire, SuperwomanReignOfTerror, MagicMissile,
        HopeSummersPluripotentEchopraxia, MimicFlyingBlastingSlashing, ProfessorXRecruitingYoungMutants,
        CerebroCyberneticIntelligence, XaviersSchoolHomeForGiftedYoungsters,
        IcemanTooCoolForWords, IntellectDevourerBrainEatingMonster, TyphoidMaryRedRubberBoots,
        YawningPortalComfortableInn, TargetLock,
    ];

    public static readonly IReadOnlySet<string> Ids = All.Select(card => card.Id).ToHashSet();
}
