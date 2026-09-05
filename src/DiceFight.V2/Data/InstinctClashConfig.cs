using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// v3 "Instinct Clash" - the animal-themed, from-scratch game described in
// ~/DiceFight2026/v3/DESIGN_NOTES.md, expressed as one GameConfig (the
// same "current game is just one config" proof DiceFightClassicConfig.cs
// is, for a genuinely different game rather than a variant ruleset).
//
// Deliberately small and simple, matching the brief: no Basic Actions
// (BasicActionSlots: 0), one Character ability apiece using the plainest
// templates in the closed vocabulary, no win condition/deck-out wiring
// yet (scoped out of this pass - see the mellow-sparking-comet plan).
// Every number here is a first-pass placeholder for playtesting, not a
// balanced value - v3/DESIGN_NOTES.md is the source of truth for which
// numbers are actually locked vs. still moving.
public static class InstinctClashConfig
{
    // --- Tardigrade dice (the free-to-field basic creature; one per
    // energy type, matching v3/DESIGN_NOTES.md's locked spec exactly:
    // two L1, two L2, one Bulwark, one Surge). ---

    private static DieDefinition TardigradeDie(string energyType) => new($"Tardigrade{energyType}",
    [
        new Face([new SymbolAmount(energyType, 2)], new CharacterFaceData(1, FieldingCost: 0, Attack: 0, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 2)], new CharacterFaceData(1, FieldingCost: 0, Attack: 0, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([], new CharacterFaceData(3, FieldingCost: 0, Attack: 1, Defense: 3), Kind: FaceKind.CharacterFace), // Bulwark
        new Face([new SymbolAmount("Wild", 2)], Kind: FaceKind.EnergyFace), // Surge - no character face at all
    ]);

    // --- Characters: a small, simple-ability pool, reskinned from
    // v3/CARD_INSPIRATION.md's already-vetted "confirmed buildable"
    // picks. Face layout corrected 2026-09-07 (user call, after
    // checking real physical precedent) to match LATER Dice Masters
    // sets rather than the original run this session's earlier DPS-
    // catalog check was actually looking at: 3 stat faces (one per
    // level, not doubled) + 3 energy faces of the card's own type (two
    // double, one single) - not the classic run's "6 doubled stat
    // faces, 0 energy" layout the original 8 cards shipped with. Every
    // Character now has real odds of rolling energy instead of a body,
    // same as a Tardigrade - a genuine economy change, not a display
    // tweak. FieldingCost is per-level like before (Characters, unlike
    // Tardigrades, cost real energy to field, rule 2.6.3.2) but no
    // longer written onto the energy faces, which carry no character
    // data at all.
    private static DieDefinition CharacterDie(string dieId, string energyType, int fieldingCost, params (int Attack, int Defense)[] levels)
    {
        var faces = new List<Face>();
        for (var i = 0; i < levels.Length; i++)
        {
            var (attack, defense) = levels[i];
            faces.Add(new Face([], new CharacterFaceData(i + 1, fieldingCost, attack, defense), Kind: FaceKind.CharacterFace));
        }
        faces.Add(new Face([new SymbolAmount(energyType, 2)], Kind: FaceKind.EnergyFace));
        faces.Add(new Face([new SymbolAmount(energyType, 2)], Kind: FaceKind.EnergyFace));
        faces.Add(new Face([new SymbolAmount(energyType, 1)], Kind: FaceKind.EnergyFace));
        return new DieDefinition(dieId, faces);
    }

    private static TargetFilter OwnCreatures => new(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Count: 0);
    private static TargetFilter WeakOpposingCreatures(int maxDefense) => new(
        Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Defense, Max: maxDefense));

    // Claw

    public static readonly CardDef HoneyBadger = new(
        Id: "IC-CLAW-01", Name: "Honey Badger", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-01Die", energyType: "Claw", fieldingCost: 1, (0, 2), (0, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 1 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Wolverine = new(
        Id: "IC-CLAW-02", Name: "Wolverine", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-02Die", energyType: "Claw", fieldingCost: 2, (0, 2), (0, 2), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: deal 1 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    // 6 more Claw picks (2026-09-06, "build out a full roster... 8
    // different animals") - same v3/CARD_INSPIRATION.md sourcing pass as
    // the original two, picked for being directly buildable against the
    // same plain templates already proven above rather than the fuller
    // multi-clause/bonus-on-double-roll/Global text CARD_INSPIRATION.md
    // records as those cards' actual printed text. "Wolf" itself was
    // skipped as a pick - it's the Claw Champion's own name now.
    public static readonly CardDef GrizzlyBear = new(
        Id: "IC-CLAW-03", Name: "Grizzly Bear", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-03Die", energyType: "Claw", fieldingCost: 2, (1, 5), (2, 6), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Orca = new(
        Id: "IC-CLAW-04", Name: "Orca", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-04Die", energyType: "Claw", fieldingCost: 2, (0, 3), (1, 3), (1, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef PeregrineFalcon = new(
        Id: "IC-CLAW-05", Name: "Peregrine Falcon", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-05Die", energyType: "Claw", fieldingCost: 2, (1, 5), (2, 7), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 3 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Tiger = new(
        Id: "IC-CLAW-06", Name: "Tiger", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-06Die", energyType: "Claw", fieldingCost: 2, (1, 5), (2, 7), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: deal 2 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef Stoat = new(
        Id: "IC-CLAW-07", Name: "Stoat", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-07Die", energyType: "Claw", fieldingCost: 1, (0, 2), (1, 3), (2, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 1 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef CapeBuffalo = new(
        Id: "IC-CLAW-08", Name: "Cape Buffalo", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("IC-CLAW-08Die", energyType: "Claw", fieldingCost: 2, (1, 4), (1, 6), (2, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 ATK.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, AtkDelta: new Fixed(1))]);

    // Shell

    public static readonly CardDef Hippopotamus = new(
        Id: "IC-SHELL-01", Name: "Hippopotamus", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-01Die", energyType: "Shell", fieldingCost: 2, (0, 1), (1, 1), (2, 1)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: gain 2 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(2)))],
        Continuous: []);

    public static readonly CardDef MuskOx = new(
        Id: "IC-SHELL-02", Name: "Musk Ox", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-02Die", energyType: "Shell", fieldingCost: 2, (0, 2), (1, 2), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 DEF.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, DefDelta: new Fixed(1))]);

    // 6 more Shell picks (2026-09-06/07, "build out a full roster... 8
    // different animals"). CARD_INSPIRATION.md's own note on Shell:
    // "thinner list than the other three... may need more from-scratch
    // design" - only 5 non-basic-action picks existed beyond Hippo/Musk
    // Ox, so Box Turtle is original rather than sourced (same simple
    // template style as the rest, no new vocabulary).
    public static readonly CardDef Pangolin = new(
        Id: "IC-SHELL-03", Name: "Pangolin", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-03Die", energyType: "Shell", fieldingCost: 1, (0, 2), (1, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: gain 1 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(1)))],
        Continuous: []);

    public static readonly CardDef HermitCrab = new(
        Id: "IC-SHELL-04", Name: "Hermit Crab", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-04Die", energyType: "Shell", fieldingCost: 1, (1, 1), (0, 1), (2, 1)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: gain 2 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(2)))],
        Continuous: []);

    public static readonly CardDef Opossum = new(
        Id: "IC-SHELL-05", Name: "Opossum", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-05Die", energyType: "Shell", fieldingCost: 1, (0, 0), (0, 1), (1, 2)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: a weak target creature (3 ATK or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Stat: new StatThreshold(StatKind.Attack, Max: 3)), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef QueenTermite = new(
        Id: "IC-SHELL-06", Name: "Queen Termite", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-06Die", energyType: "Shell", fieldingCost: 2, (1, 3), (1, 4), (2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 ATK.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, AtkDelta: new Fixed(1))]);

    public static readonly CardDef SnappingTurtle = new(
        Id: "IC-SHELL-07", Name: "Snapping Turtle", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-07Die", energyType: "Shell", fieldingCost: 2, (0, 4), (1, 5), (2, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef BoxTurtle = new(
        Id: "IC-SHELL-08", Name: "Box Turtle", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("IC-SHELL-08Die", energyType: "Shell", fieldingCost: 1, (0, 1), (1, 2), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 1 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    // Wing

    public static readonly CardDef Osprey = new(
        Id: "IC-WING-01", Name: "Osprey", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-01Die", energyType: "Wing", fieldingCost: 2, (0, 4), (1, 5), (2, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: move a die from your discard to your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Count: 1), Zone.PrepArea))],
        Continuous: []);

    public static readonly CardDef BarnSwallow = new(
        Id: "IC-WING-02", Name: "Barn Swallow", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-02Die", energyType: "Wing", fieldingCost: 1, (0, 2), (0, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Whenever this levels up: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged, new DrawToZone(1, Zone.PrepArea, Zone.Bag),
            Filter: new EventFilter(LevelIncreased: true, RequireSelf: true))],
        Continuous: []);

    // 6 more Wing picks (2026-09-06/07). Several CARD_INSPIRATION.md Wing
    // cards needed Global triggers (Greyhound/Albatross as printed) or a
    // bonus-on-double-roll clause (Flying Squirrel/Jackrabbit/Monarch's
    // full text) - neither exists in the engine yet (no Global ability
    // system, no double-roll bonus hook), so those picks are simplified
    // to their closest already-buildable shape rather than skipped
    // outright, same latitude the original 8 already took with Barn
    // Owl/Hyena's own printed text.
    public static readonly CardDef Hummingbird = new(
        Id: "IC-WING-03", Name: "Hummingbird", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-03Die", energyType: "Wing", fieldingCost: 1, (0, 1), (0, 1), (1, 2)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef MountainGoat = new(
        Id: "IC-WING-04", Name: "Mountain Goat", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-04Die", energyType: "Wing", fieldingCost: 1, (0, 2), (1, 2), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    public static readonly CardDef MonarchButterfly = new(
        Id: "IC-WING-05", Name: "Monarch Butterfly", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-05Die", energyType: "Wing", fieldingCost: 1, (0, 1), (0, 2), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Gets +2 ATK for each of your creatures waiting in your Prep Area.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true), AtkDelta: new PerMatch(
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.PrepArea], Count: 0), Multiplier: 2))]);

    public static readonly CardDef HomingPigeon = new(
        Id: "IC-WING-06", Name: "Homing Pigeon", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-06Die", energyType: "Wing", fieldingCost: 2, (0, 3), (1, 3), (1, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: gain 2 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(2)))],
        Continuous: []);

    public static readonly CardDef Greyhound = new(
        Id: "IC-WING-07", Name: "Greyhound", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-07Die", energyType: "Wing", fieldingCost: 2, (1, 5), (2, 6), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: deal 1 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef Albatross = new(
        Id: "IC-WING-08", Name: "Albatross", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("IC-WING-08Die", energyType: "Wing", fieldingCost: 2, (1, 4), (2, 5), (3, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 2 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    // Eye

    public static readonly CardDef BarnOwl = new(
        Id: "IC-EYE-01", Name: "Barn Owl", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-01Die", energyType: "Eye", fieldingCost: 2, (0, 2), (0, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: a weak target creature (3 ATK or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Stat: new StatThreshold(StatKind.Attack, Max: 3)), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef Hyena = new(
        Id: "IC-EYE-02", Name: "Hyena", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-02Die", energyType: "Eye", fieldingCost: 2, (1, 3), (1, 4), (2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Gets +1 ATK for each weak opposing creature (2 DEF or less).",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true), AtkDelta: new PerMatch(WeakOpposingCreatures(maxDefense: 2), Multiplier: 1))]);

    // 6 more Eye picks (2026-09-06/07). Several CARD_INSPIRATION.md Eye
    // cards needed a Global trigger, an opponent's-turn-start trigger, or
    // a Spin-to-a-specific-energy-face effect (none of the three exist
    // in the engine yet) - simplified to the closest already-buildable
    // shape, same latitude as Wing's picks above.
    public static readonly CardDef Anglerfish = new(
        Id: "IC-EYE-03", Name: "Anglerfish", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-03Die", energyType: "Eye", fieldingCost: 2, (1, 5), (2, 7), (3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On attack: every weak opposing creature (3 DEF or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Defense, Max: 3), Count: 0), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef Cowbird = new(
        Id: "IC-EYE-04", Name: "Cowbird", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-04Die", energyType: "Eye", fieldingCost: 1, (1, 2), (2, 3), (2, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Whenever this levels up: move an opposing die from their Prep Area back to their Bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.PrepArea], Count: 1), Zone.Bag),
            Filter: new EventFilter(LevelIncreased: true, RequireSelf: true))],
        Continuous: []);

    public static readonly CardDef Magpie = new(
        Id: "IC-EYE-05", Name: "Magpie", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-05Die", energyType: "Eye", fieldingCost: 1, (1, 1), (1, 2), (2, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    public static readonly CardDef Raven = new(
        Id: "IC-EYE-06", Name: "Raven", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 7, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-06Die", energyType: "Eye", fieldingCost: 2, (0, 2), (0, 3), (1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: deal 2 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Elephant = new(
        Id: "IC-EYE-07", Name: "Elephant", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-07Die", energyType: "Eye", fieldingCost: 2, (1, 1), (2, 1), (3, 1)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "On field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Fox = new(
        Id: "IC-EYE-08", Name: "Fox", Subtitle: null, Set: "Instinct Clash", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("IC-EYE-08Die", energyType: "Eye", fieldingCost: 2, (1, 3), (1, 4), (2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 DEF.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, DefDelta: new Fixed(1))]);

    public static readonly IReadOnlyDictionary<string, CardDef> Catalog = new List<CardDef>
    {
        HoneyBadger, Wolverine, GrizzlyBear, Orca, PeregrineFalcon, Tiger, Stoat, CapeBuffalo,
        Hippopotamus, MuskOx, Pangolin, HermitCrab, Opossum, QueenTermite, SnappingTurtle, BoxTurtle,
        Osprey, BarnSwallow, Hummingbird, MountainGoat, MonarchButterfly, HomingPigeon, Greyhound, Albatross,
        BarnOwl, Hyena, Anglerfish, Cowbird, Magpie, Raven, Elephant, Fox,
    }.ToDictionary(c => c.Id);

    // Which eight Characters a team gets when it picks a Champion - the
    // v3 "one energy type per team" starting point (v3/DESIGN_NOTES.md's
    // own open question: whether Champion/Character energy must match).
    // API layer (Phase 3) reads this to build TeamCardIds from a Champion
    // choice alone, no deckbuilding UI needed yet. Bumped from 2 to 8
    // per type (2026-09-06/07, "build out a full roster... 8 different
    // animals") - see Config.Rules.MaxTeamCards/MaxTeamDice below, which
    // have to grow in lockstep.
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CharactersByEnergyType = new Dictionary<string, IReadOnlyList<string>>
    {
        ["Claw"] = [HoneyBadger.Id, Wolverine.Id, GrizzlyBear.Id, Orca.Id, PeregrineFalcon.Id, Tiger.Id, Stoat.Id, CapeBuffalo.Id],
        ["Shell"] = [Hippopotamus.Id, MuskOx.Id, Pangolin.Id, HermitCrab.Id, Opossum.Id, QueenTermite.Id, SnappingTurtle.Id, BoxTurtle.Id],
        ["Wing"] = [Osprey.Id, BarnSwallow.Id, Hummingbird.Id, MountainGoat.Id, MonarchButterfly.Id, HomingPigeon.Id, Greyhound.Id, Albatross.Id],
        ["Eye"] = [BarnOwl.Id, Hyena.Id, Anglerfish.Id, Cowbird.Id, Magpie.Id, Raven.Id, Elephant.Id, Fox.Id],
    };

    // --- Champions: no die, one flat always-on passive, plus the
    // Tardigrade pool their team draws from (GameSetup.SeedBasicDicePool
    // reads ChampionDef.TardigradePool in preference to the shared
    // Config.BasicDicePool - see that change's own remarks). ---

    // Starter-deck sizing (2026-09-05, was 4): 8 Tardigrades is enough
    // Bag depth to cover a full two turns of drawing DrawCount(4) each
    // without an early Used-Pile reshuffle, matching the "traditional
    // deck-building starter hand" the user asked for rather than a bag
    // that's already thin by turn two.
    public static readonly IReadOnlyList<ChampionDef> Champions =
    [
        // Renamed from "Lion" (2026-09-06) - a real fan-art avatar exists
        // for this one now (icons.tsx's WolfIcon), so the Claw Champion
        // became the animal the art actually is.
        new("Wolf", "Wolf", "Claw", ChampionPassiveKind.AttackBuff, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Claw"), Count: 8)],
        },
        new("Armadillo", "Armadillo", "Shell", ChampionPassiveKind.DefenseBuff, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Shell"), Count: 8)],
        },
        new("GoldenEagle", "Golden Eagle", "Wing", ChampionPassiveKind.FieldingCostDiscount, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Wing"), Count: 8)],
        },
        new("GreatHornedOwl", "Great Horned Owl", "Eye", ChampionPassiveKind.PurchaseCostDiscount, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Eye"), Count: 8)],
        },
    ];

    public static readonly GameConfig Config = new(
        Id: "instinct-clash",
        Name: "Instinct Clash",
        EnergySymbols:
        [
            new SymbolDef("Claw"), new SymbolDef("Shell"), new SymbolDef("Wing"), new SymbolDef("Eye"),
            new SymbolDef("Wild", IsWild: true),
        ],
        Keywords: [],
        Rules: new RulesConfig(
            StartingLife: 20,
            DrawCount: 4,
            MaxTeamCards: 8, // all 8 Characters matching the chosen Champion's energy type
            MaxTeamDice: 32, // 8 Characters x DieLimit 4
            BasicActionCount: 0),
        BasicDicePool: [], // every player has a ChampionId, so this is never actually read
        BasicActionSlots: 0)
    {
        Champions = Champions,
    };
}
