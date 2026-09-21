using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// v3 "Dice Kingdom" - the animal-themed, from-scratch game described in
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
public static class DiceKingdomConfig
{
    // --- Tardigrade dice (the free-to-field basic creature; one per
    // energy type, matching v3/DESIGN_NOTES.md's locked spec exactly:
    // two L1, two L2, one Bulwark, one Surge). ---

    // Surge dropped to 1 Wild (was 2) - direct feedback (2026-09-05):
    // "it feels a little too easy to spend energy... let's change the
    // Surge die to just be one wild and see how that feels," a
    // deliberate playtesting experiment before touching monochromatic
    // teams, not a rules-accuracy fix like the other Tardigrade faces.
    private static DieDefinition TardigradeDie(string energyType) => new($"Tardigrade{energyType}",
    [
        new Face([new SymbolAmount(energyType, 2)], new CharacterFaceData(1, FieldingCost: 0, Attack: 0, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 2)], new CharacterFaceData(1, FieldingCost: 0, Attack: 0, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([new SymbolAmount(energyType, 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([], new CharacterFaceData(3, FieldingCost: 0, Attack: 1, Defense: 3), Kind: FaceKind.CharacterFace), // Bulwark
        new Face([new SymbolAmount("Wild", 1)], Kind: FaceKind.EnergyFace), // Surge - no character face at all
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
    private static DieDefinition CharacterDie(string dieId, string energyType, params (int Fielding, int Attack, int Defense)[] levels)
    {
        var faces = new List<Face>();
        for (var i = 0; i < levels.Length; i++)
        {
            var (fielding, attack, defense) = levels[i];
            faces.Add(new Face([], new CharacterFaceData(i + 1, fielding, attack, defense), Kind: FaceKind.CharacterFace));
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
        Id: "DK-CLAW-01", Name: "Honey Badger", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-01Die", energyType: "Claw", (0, 0, 2), (0, 1, 3), (1, 1, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: deal 1 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Wolverine = new(
        Id: "DK-CLAW-02", Name: "Wolverine", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-02Die", energyType: "Claw", (1, 1, 4), (1, 2, 5), (2, 3, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["Fast", "On Attack"],
        RawText: "Fast. On Attack: deal 1 damage to the opponent directly.",
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
        Id: "DK-CLAW-03", Name: "Grizzly Bear", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-03Die", energyType: "Claw", (1, 3, 6), (2, 4, 7), (2, 5, 8)),
        DieLimit: 4, Affiliations: [], Keywords: ["Overcrush"],
        RawText: "Overcrush.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef Orca = new(
        Id: "DK-CLAW-04", Name: "Orca", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-04Die", energyType: "Claw", (1, 1, 4), (1, 3, 5), (2, 3, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef PeregrineFalcon = new(
        Id: "DK-CLAW-05", Name: "Peregrine Falcon", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-05Die", energyType: "Claw", (1, 2, 4), (2, 3, 6), (3, 4, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["Fast", "On Field"],
        RawText: "Fast. On Field: deal 3 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Tiger = new(
        Id: "DK-CLAW-06", Name: "Tiger", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-06Die", energyType: "Claw", (1, 3, 5), (2, 4, 6), (2, 4, 8)),
        DieLimit: 4, Affiliations: [], Keywords: ["Overcrush", "On Attack"],
        RawText: "Overcrush. On Attack: deal 2 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef Stoat = new(
        Id: "DK-CLAW-07", Name: "Stoat", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-07Die", energyType: "Claw", (0, 0, 4), (1, 2, 6), (2, 4, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: deal 1 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef CapeBuffalo = new(
        Id: "DK-CLAW-08", Name: "Cape Buffalo", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-08Die", energyType: "Claw", (1, 1, 6), (2, 1, 9), (2, 3, 11)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 ATK.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, AtkDelta: new Fixed(1))]);

    // 2026-09-12 addition - Claw had no natural 3-cost pick among the
    // original 8 (2, 4, 4, 5, 5, 6, 6, 6), which the new pack-composition
    // rule below (CharactersByChampion) needs for every Champion's own-
    // type four. Ported rather than renumbering an existing card, per
    // direct instruction: "I'd rather find cards in the catalog to fill
    // out the decks that change the numbers on existing cards... shouldn't
    // be too hard to find another 2 or 3 cost to port." Source: Toad,
    // "Secondary Mutation" (DPS054) - dropped its own "Teamwatch: spin
    // Toad up a level" second clause (affiliation-gated; v3 doesn't use
    // Affiliations for gameplay yet, same latitude CARD_INSPIRATION.md's
    // other partial ports already take).
    public static readonly CardDef Mongoose = new(
        Id: "DK-CLAW-09", Name: "Mongoose", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-09Die", energyType: "Claw", (0, 2, 2), (1, 3, 2), (2, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Awaken"],
        RawText: "Awaken: deal 2 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDie)),
            Filter: new EventFilter(LevelIncreased: true, RequireSelf: true))],
        Continuous: []);

    // Shell

    public static readonly CardDef Hippopotamus = new(
        Id: "DK-SHELL-01", Name: "Hippopotamus", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-01Die", energyType: "Shell", (1, 0, 8), (1, 1, 9), (2, 2, 10)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Vanilla - no ability.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef MuskOx = new(
        Id: "DK-SHELL-02", Name: "Musk Ox", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-02Die", energyType: "Shell", (1, 0, 6), (1, 1, 7), (2, 1, 8)),
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
        Id: "DK-SHELL-03", Name: "Pangolin", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-03Die", energyType: "Shell", (1, 0, 4), (1, 1, 5), (1, 2, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: gain 1 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(1)))],
        Continuous: []);

    public static readonly CardDef HermitCrab = new(
        Id: "DK-SHELL-04", Name: "Hermit Crab", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-04Die", energyType: "Shell", (0, 0, 4), (0, 1, 4), (1, 1, 4)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Vanilla - no ability.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef Opossum = new(
        Id: "DK-SHELL-05", Name: "Opossum", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-05Die", energyType: "Shell", (0, 0, 2), (1, 0, 4), (1, 2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["Deadly"],
        RawText: "Deadly.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef QueenTermite = new(
        Id: "DK-SHELL-06", Name: "Queen Termite", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-06Die", energyType: "Shell", (1, 2, 4), (1, 2, 6), (2, 3, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 ATK.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, AtkDelta: new Fixed(1))]);

    public static readonly CardDef SnappingTurtle = new(
        Id: "DK-SHELL-07", Name: "Snapping Turtle", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-07Die", energyType: "Shell", (1, 0, 5), (2, 1, 7), (2, 3, 8)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef BoxTurtle = new(
        Id: "DK-SHELL-08", Name: "Box Turtle", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-08Die", energyType: "Shell", (1, 0, 3), (1, 1, 5), (1, 3, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Block"],
        RawText: "On Block: deal 1 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieBlocks,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    // Wing

    public static readonly CardDef Osprey = new(
        Id: "DK-WING-01", Name: "Osprey", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-01Die", energyType: "Wing", (1, 0, 5), (1, 1, 6), (2, 2, 8)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: move a die from your discard to your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Count: 1), Zone.PrepArea))],
        Continuous: []);

    public static readonly CardDef BarnSwallow = new(
        Id: "DK-WING-02", Name: "Barn Swallow", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-02Die", energyType: "Wing", (0, 0, 4), (1, 0, 5), (2, 2, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["Awaken"],
        RawText: "Awaken: draw a die into your Prep Area.",
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
        Id: "DK-WING-03", Name: "Hummingbird", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-03Die", energyType: "Wing", (0, 0, 3), (1, 2, 3), (1, 2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef MountainGoat = new(
        Id: "DK-WING-04", Name: "Mountain Goat", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-04Die", energyType: "Wing", (1, 0, 3), (1, 1, 4), (1, 2, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    public static readonly CardDef MonarchButterfly = new(
        Id: "DK-WING-05", Name: "Monarch Butterfly", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-05Die", energyType: "Wing", (0, 0, 4), (1, 2, 5), (2, 4, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Gets +2 ATK for each of your creatures waiting in your Prep Area.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true), AtkDelta: new PerMatch(
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.PrepArea], Count: 0), Multiplier: 2))]);

    public static readonly CardDef HomingPigeon = new(
        Id: "DK-WING-06", Name: "Homing Pigeon", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-06Die", energyType: "Wing", (1, 0, 6), (1, 1, 8), (2, 1, 10)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: gain 2 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(2)))],
        Continuous: []);

    public static readonly CardDef Greyhound = new(
        Id: "DK-WING-07", Name: "Greyhound", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-07Die", energyType: "Wing", (1, 1, 6), (1, 3, 7), (2, 3, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["Fast"],
        RawText: "Fast.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef Albatross = new(
        Id: "DK-WING-08", Name: "Albatross", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-08Die", energyType: "Wing", (1, 1, 5), (1, 3, 6), (2, 4, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: deal 2 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    // 2026-09-12 addition - Wing had no natural 2-cost pick among the
    // original 8 (3, 3, 4, 4, 4, 4, 4, 5) - see Mongoose's own remarks
    // above for why this was ported rather than a renumbering. Source:
    // Beast, "Combat Ready" (DPS098) - dropped its own "Founder: first
    // Beast die you purchase each game costs 1 extra" clause (a one-time
    // meta-cost rule with nothing to hook into for a fresh pick).
    public static readonly CardDef Swift = new(
        Id: "DK-WING-09", Name: "Swift", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-09Die", energyType: "Wing", (0, 1, 2), (1, 2, 2), (1, 3, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    // Eye

    public static readonly CardDef BarnOwl = new(
        Id: "DK-EYE-01", Name: "Barn Owl", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-01Die", energyType: "Eye", (1, 0, 7), (1, 1, 7), (2, 1, 9)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: a weak target creature (3 ATK or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Stat: new StatThreshold(StatKind.Attack, Max: 3)), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef Hyena = new(
        Id: "DK-EYE-02", Name: "Hyena", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-02Die", energyType: "Eye", (1, 1, 5), (1, 1, 6), (2, 3, 6)),
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
        Id: "DK-EYE-03", Name: "Anglerfish", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-03Die", energyType: "Eye", (1, 1, 6), (2, 3, 7), (3, 4, 10)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: every weak opposing creature (3 DEF or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Defense, Max: 3), Count: 0), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef Cowbird = new(
        Id: "DK-EYE-04", Name: "Cowbird", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-04Die", energyType: "Eye", (1, 2, 3), (1, 2, 5), (2, 4, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["Awaken"],
        RawText: "Awaken: move an opposing die from their Prep Area back to their Bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.PrepArea], Count: 1), Zone.Bag),
            Filter: new EventFilter(LevelIncreased: true, RequireSelf: true))],
        Continuous: []);

    public static readonly CardDef Magpie = new(
        Id: "DK-EYE-05", Name: "Magpie", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-05Die", energyType: "Eye", (1, 1, 2), (1, 1, 4), (1, 2, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    public static readonly CardDef Raven = new(
        Id: "DK-EYE-06", Name: "Raven", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-06Die", energyType: "Eye", (1, 0, 7), (2, 1, 8), (2, 1, 10)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: deal 2 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Elephant = new(
        Id: "DK-EYE-07", Name: "Elephant", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-07Die", energyType: "Eye", (1, 3, 9), (2, 4, 11), (3, 5, 12)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Vanilla - no ability.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef Fox = new(
        Id: "DK-EYE-08", Name: "Fox", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-08Die", energyType: "Eye", (1, 1, 6), (2, 2, 7), (2, 3, 10)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 DEF.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, DefDelta: new Fixed(1))]);

    // 2026-09-12 addition - Eye had no natural 2-cost pick among the
    // original 8 (3, 3, 4, 4, 5, 6, 6, 7) - see Mongoose's own remarks
    // above for why this was ported rather than a renumbering. Source:
    // Iceman, "Icy Interference" (DPS034) - cost discounted from its
    // printed 4 to 2 (no other close-fitting Eye pick prints at 2; same
    // "cost is a starting point, not a balanced number for this game"
    // latitude CARD_INSPIRATION.md's own Method section already claims).
    public static readonly CardDef Cuttlefish = new(
        Id: "DK-EYE-09", Name: "Cuttlefish", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-09Die", energyType: "Eye", (0, 0, 3), (1, 1, 3), (1, 1, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: spin a target opposing level 1 creature to an energy face.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new SpinToEnergy(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Level, Min: 1, Max: 1))))],
        Continuous: []);

    public static readonly IReadOnlyDictionary<string, CardDef> Catalog = new List<CardDef>
    {
        HoneyBadger, Wolverine, GrizzlyBear, Orca, PeregrineFalcon, Tiger, Stoat, CapeBuffalo, Mongoose,
        Hippopotamus, MuskOx, Pangolin, HermitCrab, Opossum, QueenTermite, SnappingTurtle, BoxTurtle,
        Osprey, BarnSwallow, Hummingbird, MountainGoat, MonarchButterfly, HomingPigeon, Greyhound, Albatross, Swift,
        BarnOwl, Hyena, Anglerfish, Cowbird, Magpie, Raven, Elephant, Fox, Cuttlefish,
    }.ToDictionary(c => c.Id);

    // Which eight Characters a team gets when it picks a Champion. API
    // layer (Phase 3) reads this to build TeamCardIds from a Champion
    // choice alone, no deckbuilding UI needed yet.
    //
    // Redesigned 2026-09-12 - was a straight per-energy-type mapping
    // (every team got all 8 of its Champion's own energy type; "one
    // energy type per team" was v3's original starting point, see
    // v3/DESIGN_NOTES.md). Direct feedback: "mix up the energies a bit...
    // four of them the same energy type as the champion, two of them a
    // 'symbiotic' energy type, and then one of each of the others,"
    // plus a cost-curve rule for the four own-type picks (at least one
    // 2-cost and one 3-cost, a mid-range, and a higher-cost "power
    // card" - the non-Champion-energy four "can be more or less
    // random"). Symbiotic pairing, confirmed with the user: Claw<->Wing
    // (aggro+tempo - hit fast) and Shell<->Eye (defense+control - grind
    // it out).
    //
    // That pairing makes the redistribution math come out even: each
    // energy pool has exactly 8 (Shell) or 9 (Claw/Wing/Eye, after the
    // three cost-curve ports above - Mongoose/Swift/Cuttlefish) cards,
    // and every Champion's 8-card pack draws 4 (own) + 2 (symbiotic
    // partner) + 1 + 1 (the other two energies) = 8 - so a 9-card pool
    // has exactly one spare left unassigned (Orca, Monarch Butterfly,
    // Anglerfish - still in Catalog, just not on any team yet; a Shell's
    // own 8 cards all get placed with none spare). Bonus: this gives the
    // Tardigrade Surge face's Wild pip (previously only useful for a
    // same-type purchase) a real reason to matter - it's now the one
    // guaranteed way to pay for an off-type splash card.
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CharactersByChampion = new Dictionary<string, IReadOnlyList<string>>
    {
        // Own 2/3/mid/power: HoneyBadger(2)/Mongoose(3)/Wolverine(4)/Tiger(6).
        ["Wolf"] = [HoneyBadger.Id, Mongoose.Id, Wolverine.Id, Tiger.Id, MountainGoat.Id, Greyhound.Id, Hippopotamus.Id, Elephant.Id],
        // Own 2/3/mid/power: HermitCrab(2)/Pangolin(3)/MuskOx(4)/SnappingTurtle(5).
        ["Armadillo"] = [HermitCrab.Id, Pangolin.Id, MuskOx.Id, SnappingTurtle.Id, Fox.Id, Cowbird.Id, CapeBuffalo.Id, Hummingbird.Id],
        // Own 2/3/mid/power: Swift(2)/BarnSwallow(3)/Osprey(4)/Albatross(5).
        ["GoldenEagle"] = [Swift.Id, BarnSwallow.Id, Osprey.Id, Albatross.Id, GrizzlyBear.Id, PeregrineFalcon.Id, BoxTurtle.Id, BarnOwl.Id],
        // Own 2/3/mid/power: Cuttlefish(2)/Magpie(3)/Hyena(4)/Raven(7).
        ["GreatHornedOwl"] = [Cuttlefish.Id, Magpie.Id, Hyena.Id, Raven.Id, Opossum.Id, QueenTermite.Id, Stoat.Id, HomingPigeon.Id],
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
        Id: "dice-kingdom",
        Name: "Dice Kingdom",
        EnergySymbols:
        [
            new SymbolDef("Claw"), new SymbolDef("Shell"), new SymbolDef("Wing"), new SymbolDef("Eye"),
            new SymbolDef("Wild", IsWild: true),
        ],
        Keywords: [new KeywordDef("Fast"), new KeywordDef("Overcrush"), new KeywordDef("Deadly"),
            new KeywordDef("On Field"), new KeywordDef("On Attack"), new KeywordDef("On Block"), new KeywordDef("Awaken")],
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
