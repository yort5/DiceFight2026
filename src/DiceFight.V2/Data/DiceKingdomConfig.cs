using System.Text.RegularExpressions;
using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2.Data;

// v3 "Dice Kingdom" - the animal-themed, from-scratch game described in
// ~/DiceFight2026/v3/DESIGN_NOTES.md, expressed as one GameConfig (the
// same "current game is just one config" proof DiceFightClassicConfig.cs
// is, for a genuinely different game rather than a variant ruleset).
//
// Deliberately small and simple, matching the brief: one Basic Action per
// Champion (ActionByChampion, 2026-09-26), one Character ability apiece using the plainest
// templates in the closed vocabulary, no win condition/deck-out wiring
// yet (scoped out of this pass - see the mellow-sparking-comet plan).
// Every number here is a first-pass placeholder for playtesting, not a
// balanced value - v3/DESIGN_NOTES.md is the source of truth for which
// numbers are actually locked vs. still moving.
public static class DiceKingdomConfig
{
    // --- Tardigrade dice (the free-to-field basic creature; one per
    // energy type). ---
    //
    // Layout (2026-10-04, user call after a simulator sweep - v3/
    // DESIGN_NOTES.md "Tardigrade face layouts"): Dice Masters' sidekick
    // could be fielded on 1 face in 6, ours on 5, which "might have been
    // too much". Now: three pure energy faces (2, 2, 1), two Wild faces
    // that are also a level 2 1/1 - field it or spend the Wild - and the
    // level 3 Bulwark at 1/2 (was 1/3), no energy. Same 7 energy per die
    // as before. Faces 4 and 5 keep their old roles (Bulwark, Wild).
    private static DieDefinition TardigradeDie(string energyType) => new($"Tardigrade{energyType}",
    [
        new Face([new SymbolAmount(energyType, 2)], Kind: FaceKind.EnergyFace),
        new Face([new SymbolAmount(energyType, 2)], Kind: FaceKind.EnergyFace),
        new Face([new SymbolAmount(energyType, 1)], Kind: FaceKind.EnergyFace),
        new Face([new SymbolAmount("Wild", 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
        new Face([], new CharacterFaceData(3, FieldingCost: 0, Attack: 1, Defense: 2), Kind: FaceKind.CharacterFace), // Bulwark
        new Face([new SymbolAmount("Wild", 1)], new CharacterFaceData(2, FieldingCost: 0, Attack: 1, Defense: 1), Kind: FaceKind.CharacterFace),
    ]);

    // The reverse of TardigradeDie's id - "TardigradeEye" -> "Eye".
    public static string TardigradeEnergyType(string dieId) =>
        dieId.StartsWith("Tardigrade") ? dieId["Tardigrade".Length..] : throw new ArgumentException($"'{dieId}' isn't a Tardigrade die.");

    // Custom teams use the same printed dice as preset Champion pools.
    public static BasicDicePoolEntry CustomTardigrade(string energyType, int count)
    {
        if (energyType is not ("Claw" or "Shell" or "Wing" or "Eye"))
            throw new ArgumentException($"Unknown Tardigrade energy type '{energyType}'.");
        return new BasicDicePoolEntry(TardigradeDie(energyType), count);
    }

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
        => StandardDie(dieId, energyType, levels.Select((level, i) =>
            new Face([], new CharacterFaceData(i + 1, level.Fielding, level.Attack, level.Defense), Kind: FaceKind.CharacterFace)));

    // One Dice Kingdom pattern for native and imported dice: printed character/action
    // faces first, then two double-energy faces and one single-energy face.
    private static DieDefinition StandardDie(string dieId, string energyType, IEnumerable<Face> printedFaces) => new(dieId,
    [
        .. printedFaces,
        new Face([new SymbolAmount(energyType, 2)], Kind: FaceKind.EnergyFace),
        new Face([new SymbolAmount(energyType, 2)], Kind: FaceKind.EnergyFace),
        new Face([new SymbolAmount(energyType, 1)], Kind: FaceKind.EnergyFace),
    ]);

    private static TargetFilter OwnCreatures => new(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Count: 0);
    private static TargetFilter WeakOpposingCreatures(int maxDefense) => new(
        Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Defense, Max: maxDefense));

    // Claw

    public static readonly CardDef HoneyBadger = new(
        Id: "DK-CLAW-01", Name: "Honey Badger", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-01Die", energyType: "Claw", (0, 1, 1), (0, 2, 2), (1, 2, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: deal 1 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef Wolverine = new(
        Id: "DK-CLAW-02", Name: "Wolverine", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-02Die", energyType: "Claw", (1, 2, 3), (1, 3, 4), (2, 4, 4)),
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
        Die: CharacterDie("DK-CLAW-03Die", energyType: "Claw", (1, 4, 5), (2, 5, 6), (2, 6, 7)),
        DieLimit: 4, Affiliations: [], Keywords: ["Overcrush"],
        RawText: "Overcrush.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef Orca = new(
        Id: "DK-CLAW-04", Name: "Orca", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-04Die", energyType: "Claw", (1, 2, 3), (1, 4, 4), (2, 5, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef PeregrineFalcon = new(
        Id: "DK-CLAW-05", Name: "Peregrine Falcon", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-05Die", energyType: "Claw", (1, 3, 3), (2, 4, 5), (3, 5, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["Fast", "On Field"],
        RawText: "Fast. On Field: deal 3 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(3), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    // Finisher (2026-09-28, user request - a top-end card affordable only
    // once the game has run long enough, meant to break the single-
    // cheap-blocker standoffs the champion-less simulator kept walling
    // into). Punishes gang-blocking specifically: MultipleOf/
    // BlockedByAtLeast are new closed-vocabulary additions made for this
    // card (see their own remarks in Model/Effects/Common.cs and
    // Condition.cs) - "double a die's own current stat" was a real,
    // previously-flagged gap (Iceman DPS114's own tailed-card comment),
    // not guessed at here. Replaces Tiger as Wolf's own-energy "power"
    // slot (was 6-cost) - user call: replace rather than grow the roster
    // past the Rules.MaxTeamCards: 8 invariant.
    //
    // Direct feedback (2026-09-28): the doubling alone made this a
    // "threat only if gang-blocked" card - a single blocker could just
    // wall it for free and dodge the whole mechanic, no different from
    // any other creature. Added Overcrush and nudged ATK above DEF
    // (glass-cannon, not a curve-matched vanilla stick) so it is a real
    // threat EITHER way: a single blocker still eats an Overcrush
    // leftover once it's gone (ATK clears most single blockers' Defense
    // in this cost range), and gang-blocking to survive that walks
    // straight into the doubling - which then blows through an even
    // bigger Overcrush leftover on top. There is no safe block.
    //
    // Scaled back 2026-09-28 (direct feedback: the first pass's L3 13A
    // was nearly double the roster's actual ceiling elsewhere - Elephant
    // and CapeBuffalo both top out at 7A). L3's 9A is still the roster's
    // new high, just not a wildly disproportionate one.
    public static readonly CardDef Silverback = new(
        Id: "DK-CLAW-06", Name: "Silverback", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 7, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-06Die", energyType: "Claw", (2, 6, 4), (3, 7, 5), (3, 9, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["Overcrush"],
        RawText: "Overcrush. While active, whenever 2 or more creatures block Silverback, its ATK doubles for that combat.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true),
            AtkDelta: new MultipleOf("self", StatKind.Attack, Multiplier: 1),
            ActiveWhen: new BlockedByAtLeast(2))]);

    public static readonly CardDef Stoat = new(
        Id: "DK-CLAW-07", Name: "Stoat", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-07Die", energyType: "Claw", (0, 2, 2), (1, 4, 4), (2, 5, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: deal 1 damage to the opponent directly.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef CapeBuffalo = new(
        Id: "DK-CLAW-08", Name: "Cape Buffalo", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-08Die", energyType: "Claw", (1, 3, 4), (2, 5, 5), (2, 7, 7)),
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
        Die: CharacterDie("DK-CLAW-09Die", energyType: "Claw", (0, 2, 2), (1, 2, 3), (2, 4, 4)),
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
        Die: CharacterDie("DK-SHELL-01Die", energyType: "Shell", (1, 3, 5), (1, 4, 6), (2, 5, 7)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Vanilla - no ability.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef MuskOx = new(
        Id: "DK-SHELL-02", Name: "Musk Ox", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-02Die", energyType: "Shell", (1, 2, 4), (1, 3, 5), (2, 3, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 DEF.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, DefDelta: new Fixed(1))]);

    // Dice Masters' Kree Captain Global, tried on Musk Ox 2026-10-04 as
    // Armadillo ramp and taken back off the same day (user: "take the
    // discount off for now and see how some humans play") once the bot's
    // Hermit Crab + Rhinoceros combo fix made Armadillo the strongest team
    // without it (v3/DESIGN_NOTES.md). Kept for the simulator
    // (SIM_MUSKOX_GLOBAL=on) and its tests.
    public const string KreeCaptainGlobalText = "Global: Pay 2 Shell. Once per turn, the next creature you purchase this turn costs 3 less (minimum 1).";
    public static readonly TriggeredAbility KreeCaptainGlobal = new(TriggerKind.Global, new PurchaseModifier(Delta: -3, CardKind: CardType.Character),
        EnergyCost: new EnergyCost(2, "Shell"), OncePerTurn: true);

    public static CardDef WithKreeCaptainGlobal(CardDef card) =>
        card with { RawText = $"{card.RawText} {KreeCaptainGlobalText}", Abilities = [.. card.Abilities, KreeCaptainGlobal] };

    // 6 more Shell picks (2026-09-06/07, "build out a full roster... 8
    // different animals"). CARD_INSPIRATION.md's own note on Shell:
    // "thinner list than the other three... may need more from-scratch
    // design" - only 5 non-basic-action picks existed beyond Hippo/Musk
    // Ox, so Box Turtle is original rather than sourced (same simple
    // template style as the rest, no new vocabulary).
    // Blob/Drax-style lockout (2026-09-29, user call - was "On Field:
    // gain 1 life", which moved to Hermit Crab): same-type interaction
    // for a defense-oriented team that isn't another damage ping. The
    // choice is card-scoped (RememberCard, ChoiceIntent.NameCard) and
    // offers any opposing Character die in any zone - bought or not - so
    // the card about to come down can be named, not just one already on
    // the board. Low stats fit the ability: KO'ing Pangolin ends the
    // lockout, and re-fielding it names a new card. Simulated roughly
    // neutral for Armadillo's win rate (v3/DESIGN_NOTES.md), so its stats
    // were left alone rather than priced into a stronger Homash band.
    public static readonly CardDef Pangolin = new(
        Id: "DK-SHELL-03", Name: "Pangolin", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-03Die", energyType: "Shell", (1, 1, 3), (1, 2, 4), (1, 3, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: name an opposing character. While Pangolin is active, your opponent can't purchase or field it.",
        Abilities:
        [
            new TriggeredAbility(TriggerKind.DieFielded, new RememberCard(
                new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing,
                    Zones: [Zone.Unpurchased, Zone.Bag, Zone.UsedPile, Zone.PrepArea, Zone.ReservePool,
                        Zone.FieldZone, Zone.AttackZone, Zone.OutOfPlay],
                    Tags: new TagQuery(NoneOf: ["sidekick", "Anger Issues", "Distraction", "Resurrection", "Mutation", "Archnemesis"]),
                    Prompt: "name an opposing character - they can't purchase or field it while Pangolin is active."),
                "locked")),
        ],
        Continuous:
        [
            new Lockout(SuppressionKind.CantPurchase, MemoryName: "locked"),
            new Lockout(SuppressionKind.CantField, MemoryName: "locked"),
        ]);

    // The only vanilla 2-drop was Armadillo's most-bought card and never
    // KO'd anything (1 ATK at every level) - swapping it out for anything,
    // even a vanilla Hippopotamus, was worth 17-30 points of win rate in the
    // simulator (2026-09-29). DEF lowered by 1 at each level so an ability
    // is paid for out of its stats: Homash 2.00 -> 1.57. Briefly took
    // Pangolin's "gain 1 life"; now (same day, user call) forces a block,
    // which is what lets Rhinoceros threaten anything - swung in against a
    // forced blocker, every point that blocker hits it for comes back at
    // the opponent (the bot plays that combo - DiceKingdomBot).
    public static readonly CardDef HermitCrab = new(
        Id: "DK-SHELL-04", Name: "Hermit Crab", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-04Die", energyType: "Shell", (0, 1, 2), (0, 1, 3), (1, 1, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: target character die must block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Zones: [Zone.FieldZone],
                Prompt: "choose a creature that must block this turn."), CombatFlagKind.MustBlock))],
        Continuous: []);

    public static readonly CardDef Opossum = new(
        Id: "DK-SHELL-05", Name: "Opossum", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-05Die", energyType: "Shell", (0, 0, 2), (1, 1, 3), (1, 2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["Deadly"],
        RawText: "Deadly.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef QueenTermite = new(
        Id: "DK-SHELL-06", Name: "Queen Termite", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-06Die", energyType: "Shell", (1, 2, 4), (1, 3, 5), (2, 3, 6)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, your creatures get +1 ATK.",
        Abilities: [],
        Continuous: [new StatAura(OwnCreatures, AtkDelta: new Fixed(1))]);

    // Finisher (2026-09-28, user request - "like Phoenix Force Colossus":
    // reflects whatever damage it takes back at the opponent). Fully
    // expressible in the existing closed vocabulary - TriggerKind.
    // DieDamaged already fires with a DamageDealtPayload (EffectInterpreter.
    // MarkDamage), and Amount.EventValue already reads it - no engine
    // changes needed for this one, unlike Silverback. A null Filter means
    // "about me" (EventBus.Matches' own default), so this only reacts to
    // damage THIS die takes, not any other Armadillo creature's. Replaces
    // SnappingTurtle as Armadillo's own-energy "power" slot (was 5-cost).
    public static readonly CardDef Rhinoceros = new(
        Id: "DK-SHELL-07", Name: "Rhinoceros", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-07Die", energyType: "Shell", (1, 1, 5), (2, 2, 7), (2, 3, 9)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, whenever Rhinoceros takes damage, it deals that much damage to the opponent.",
        Abilities: [new TriggeredAbility(TriggerKind.DieDamaged,
            new DealDamage(new EventValue(), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    public static readonly CardDef BoxTurtle = new(
        Id: "DK-SHELL-08", Name: "Box Turtle", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-08Die", energyType: "Shell", (1, 0, 3), (1, 2, 4), (1, 4, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Block"],
        RawText: "On Block: deal 1 damage to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieBlocks,
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    // Wing

    public static readonly CardDef Osprey = new(
        Id: "DK-WING-01", Name: "Osprey", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-01Die", energyType: "Wing", (1, 2, 3), (1, 3, 4), (2, 5, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: move a die from your discard to your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], Count: 1), Zone.PrepArea))],
        Continuous: []);

    public static readonly CardDef BarnSwallow = new(
        Id: "DK-WING-02", Name: "Barn Swallow", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-02Die", energyType: "Wing", (0, 2, 2), (1, 2, 3), (2, 4, 5)),
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
        Die: CharacterDie("DK-WING-03Die", energyType: "Wing", (0, 1, 2), (1, 2, 3), (1, 3, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: KO a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new Ko(new TargetFilter(Kind: TargetKind.CharacterDie)))],
        Continuous: []);

    public static readonly CardDef MountainGoat = new(
        Id: "DK-WING-04", Name: "Mountain Goat", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-04Die", energyType: "Wing", (1, 1, 2), (1, 2, 3), (1, 4, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    public static readonly CardDef MonarchButterfly = new(
        Id: "DK-WING-05", Name: "Monarch Butterfly", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-05Die", energyType: "Wing", (0, 2, 2), (1, 3, 4), (2, 4, 5)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Gets +2 ATK for each of your creatures waiting in your Prep Area.",
        Abilities: [],
        Continuous: [new StatAura(new TargetFilter(Self: true), AtkDelta: new PerMatch(
            new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.PrepArea], Count: 0), Multiplier: 2))]);

    public static readonly CardDef HomingPigeon = new(
        Id: "DK-WING-06", Name: "Homing Pigeon", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-06Die", energyType: "Wing", (1, 3, 3), (1, 4, 5), (2, 5, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: gain 2 life.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new LifeChange(new Fixed(2)))],
        Continuous: []);

    public static readonly CardDef Greyhound = new(
        Id: "DK-WING-07", Name: "Greyhound", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-07Die", energyType: "Wing", (1, 3, 4), (1, 5, 5), (2, 5, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["Fast"],
        RawText: "Fast.",
        Abilities: [],
        Continuous: []);

    // Finisher (2026-09-29, user request - "like Lantern Ring/Green
    // Lantern"): a burst that scales with banked resources rather than
    // board stats, matching GrantAbility's own motivating example in
    // Model/Effects/EffectNode.cs ("that die gains: deal 1 damage to
    // target player for each matching energy in your Reserve Pool").
    // Simplified from that quote in one deliberate way: this counts EVERY
    // energy symbol in the Reserve Pool, not just Wing-matching ones - a
    // Tardigrade die (PoolDieId, no CardId) carries no card-derived
    // EnergySymbolId tag at all (CardDef.cs's own tag-unification
    // remarks), so a Tags:AnyOf:["Wing"] filter would silently miss every
    // Tardigrade sitting in reserve, which is most of a Wing team's own
    // banked energy. Counting all of it plays true to the archetype
    // anyway - Golden Eagle's own FieldingCostDiscount passive means a
    // Wing team banks reserve energy faster than most, so "hoard instead
    // of spending it all" is a real tension this rewards. No GrantAbility
    // indirection needed either - nothing else needs to inherit this
    // text, so the card just carries the ability itself. Replaces
    // Albatross as Golden Eagle's own-energy "power" slot (was 5-cost).
    public static readonly CardDef Phoenix = new(
        Id: "DK-WING-08", Name: "Phoenix", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-08Die", energyType: "Wing", (1, 3, 4), (2, 4, 5), (2, 6, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: deal 1 damage to the opponent for each energy symbol in your Reserve Pool.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new DealDamage(
                new PerMatch(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.ReservePool]),
                    Multiplier: 1, Unit: CountUnit.EnergySymbols),
                new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)))],
        Continuous: []);

    // 2026-09-12 addition - Wing had no natural 2-cost pick among the
    // original 8 (3, 3, 4, 4, 4, 4, 4, 5) - see Mongoose's own remarks
    // above for why this was ported rather than a renumbering. Source:
    // Beast, "Combat Ready" (DPS098) - dropped its own "Elder: first
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
        Die: CharacterDie("DK-EYE-01Die", energyType: "Eye", (1, 2, 5), (1, 3, 5), (2, 4, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: a weak target creature (3 ATK or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Stat: new StatThreshold(StatKind.Attack, Max: 3)), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef Hyena = new(
        Id: "DK-EYE-02", Name: "Hyena", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-02Die", energyType: "Eye", (1, 2, 4), (1, 2, 5), (2, 3, 6)),
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
        Die: CharacterDie("DK-EYE-03Die", energyType: "Eye", (1, 2, 5), (2, 4, 6), (3, 6, 8)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Attack"],
        RawText: "On Attack: every weak opposing creature (3 DEF or less) can't block this turn.",
        Abilities: [new TriggeredAbility(TriggerKind.DieAttacks,
            new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, Stat: new StatThreshold(StatKind.Defense, Max: 3), Count: 0), CombatFlagKind.CantBlock))],
        Continuous: []);

    public static readonly CardDef Cowbird = new(
        Id: "DK-EYE-04", Name: "Cowbird", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-04Die", energyType: "Eye", (1, 1, 4), (1, 2, 5), (2, 3, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["Awaken"],
        RawText: "Awaken: move an opposing die from their Prep Area back to their Bag.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFaceChanged,
            new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Opposing, Zones: [Zone.PrepArea], Count: 1), Zone.Bag),
            Filter: new EventFilter(LevelIncreased: true, RequireSelf: true))],
        Continuous: []);

    public static readonly CardDef Magpie = new(
        Id: "DK-EYE-05", Name: "Magpie", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-05Die", energyType: "Eye", (1, 0, 3), (1, 1, 4), (1, 3, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field"],
        RawText: "On Field: draw a die into your Prep Area.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new DrawToZone(1, Zone.PrepArea, Zone.Bag))],
        Continuous: []);

    // Finisher (2026-09-28, user request - "like Colossus": an end-of-
    // turn board-wide burn that scales with how wide your board has
    // grown). Exact same template as DpsCards.ColossusPiotr (DPS103) -
    // TurnStepEntered + EventFilter{Step: CleanUp} + PerMatch damage -
    // plus a Tags: NoneOf:["sidekick"] clause DPS103 didn't need (classic
    // Sidekicks never level past 1; v3's Tardigrades do, so "non-
    // Tardigrade" has to be said explicitly here). "sidekick" is the tag
    // QueryEngine.GetTags already grants every card-less pool die - see
    // its own remarks. Replaces Raven as Owl's own-energy "power" slot
    // (was 5-cost - the roster comment above it had said 7 for a while,
    // which was never actually true of Raven's own PurchaseCost; fixed).
    public static readonly CardDef Basilisk = new(
        Id: "DK-EYE-06", Name: "Basilisk", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 7, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-06Die", energyType: "Eye", (1, 3, 6), (2, 4, 8), (2, 5, 10)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "While active, at the end of your turn, each of your non-Tardigrade level 2+ creatures deals 2 damage to the opponent.",
        Abilities: [new TriggeredAbility(TriggerKind.TurnStepEntered,
            new DealDamage(
                new PerMatch(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own,
                    Stat: new StatThreshold(StatKind.Level, Min: 2), Tags: new TagQuery(NoneOf: ["sidekick"])), Multiplier: 2),
                new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
            Filter: new EventFilter(Ownership: TargetOwnership.Own, Step: StepIds.CleanUp))],
        Continuous: []);

    public static readonly CardDef Elephant = new(
        Id: "DK-EYE-07", Name: "Elephant", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 6, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-07Die", energyType: "Eye", (1, 5, 7), (2, 6, 9), (3, 7, 10)),
        DieLimit: 4, Affiliations: [], Keywords: [],
        RawText: "Vanilla - no ability.",
        Abilities: [],
        Continuous: []);

    public static readonly CardDef Fox = new(
        Id: "DK-EYE-08", Name: "Fox", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-08Die", energyType: "Eye", (1, 2, 5), (2, 3, 6), (2, 5, 8)),
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

    // --- New keyword animals (2026-10-03, user call): in the Catalog, on
    // no Champion's team yet - "leave the current teams for now; maybe a
    // fifth Champion once we get enough new abilities." Reach them in the
    // simulator with SIM_SWAP. Both keywords are engine-built
    // (KeywordAbilities), so the cards carry no Abilities of their own,
    // same as Opossum's Deadly.

    // Intimidate - a frill display that scares a blocker off the board
    // for the turn. Priced in the medium band (~1.4, v3/COST_MODEL.md):
    // stronger than Barn Owl's "weak creature can't block" (no ATK limit,
    // and the target's "while active" text is off too), short of a KO.
    public static readonly CardDef FrilledLizard = new(
        Id: "DK-EYE-10", Name: "Frilled Lizard", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-10Die", energyType: "Eye", (1, 2, 4), (1, 3, 4), (2, 4, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["Intimidate"],
        RawText: "Intimidate (when fielded, remove a target opposing creature from the Field until end of turn).",
        Abilities: [],
        Continuous: []);

    // Infiltrate - glides in, glides back out. An unblocked attacker
    // normally hits and then leaves play, so coming home for 1 damage
    // keeps the body - worth more than the Homash model prices it (first
    // pass, 1/5/3 at L3 and Homash 1.64, lifted Golden Eagle ~5 points in
    // the simulator). Trimmed 2026-10-03 to 1/4/2 (Homash 1.45): ATK still
    // leans high (Wing) so the Infiltrate trade is a real choice, and the
    // low DEF makes swinging for it a risk - it's easy to KO if blocked.
    public static readonly CardDef FlyingSquirrel = new(
        Id: "DK-WING-10", Name: "Flying Squirrel", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-10Die", energyType: "Wing", (0, 2, 2), (1, 3, 3), (1, 4, 2)),
        DieLimit: 4, Affiliations: [], Keywords: ["Infiltrate"],
        RawText: "Infiltrate (when unblocked, you may return it to the Field and deal 1 damage to the opponent instead).",
        Abilities: [],
        Continuous: []);

    // Attune and Obscure (2026-10-03) - the two Dice Masters keywords that
    // pay off "each time you use an action die" (Global uses don't count;
    // a Global-triggered keyword would be its own keyword). Same policy as
    // the two above: catalog only for now, priced by the Homash model as
    // a weak ability. Don't read their worth off today's simulator: Dice
    // Kingdom has only four Basic Actions, none of them the cheap
    // ramp/ping/churn actions real decks buy for exactly this combo (user,
    // 2026-10-03).

    // Attune - every action used sends a jolt out. Plain "Attune" is 1
    // damage; "Attune N" deals N (Boom Boom reads as Attune 2).
    public static readonly CardDef ElectricEel = new(
        Id: "DK-CLAW-10", Name: "Electric Eel", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-10Die", energyType: "Claw", (0, 2, 1), (1, 3, 3), (1, 4, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Attune"],
        RawText: "Attune (while active, each time you use an action die, deal 1 damage to the opponent or a target character die).",
        Abilities: [],
        Continuous: []);

    // Obscure - blends into the background, slips past the blockers.
    public static readonly CardDef Chameleon = new(
        Id: "DK-EYE-11", Name: "Chameleon", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Eye"],
        Die: CharacterDie("DK-EYE-11Die", energyType: "Eye", (1, 2, 4), (1, 3, 5), (2, 5, 6)),
        DieLimit: 4, Affiliations: [], Keywords: ["Obscure"],
        RawText: "Obscure (while active, each time you use an action die, this creature can't be blocked this turn).",
        Abilities: [],
        Continuous: []);

    // Energize, Energy Drain, Tag Out, Sacrifice (2026-10-03, user's
    // keyword list) - Dice Masters rules, catalog only like the others.
    // Effects for the open-ended ones are the DM-style defaults the user
    // picked.

    // Energize - flashes when it rolls a double energy face.
    public static readonly CardDef Firefly = new(
        Id: "DK-WING-11", Name: "Firefly", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-11Die", energyType: "Wing", (0, 2, 2), (1, 3, 2), (1, 3, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["Energize"],
        RawText: "Energize: deal 2 damage to a target character die or the opponent.",
        Abilities: [.. KeywordAbilities.Energize(
            new DealDamage(new Fixed(2), new TargetFilter(Kind: TargetKind.CharacterDieOrOpponent,
                Prompt: "Energize - deal 2 damage to the opponent or a target character die.")))],
        Continuous: []);

    // Energy Drain - latches on to whatever it fights.
    public static readonly CardDef Leech = new(
        Id: "DK-SHELL-09", Name: "Leech", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-09Die", energyType: "Shell", (0, 1, 3), (1, 2, 3), (1, 2, 5)),
        DieLimit: 4, Affiliations: [], Keywords: ["Energy Drain"],
        RawText: "Energy Drain (after blockers are assigned, every creature fighting this one spins down a level).",
        Abilities: [],
        Continuous: []);

    // Tag Out - the sentry steps down and a teammate steps up.
    public static readonly CardDef Meerkat = new(
        Id: "DK-SHELL-10", Name: "Meerkat", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 2, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-10Die", energyType: "Shell", (0, 1, 2), (0, 1, 3), (1, 2, 2)),
        DieLimit: 4, Affiliations: [], Keywords: ["Tag Out"],
        RawText: "Tag Out (after blockers are declared, you may move this die from the Field to your Prep Area to give a target creature +2A and +2D this turn).",
        Abilities: [],
        Continuous: []);

    // Sacrifice - the colony throws itself at the problem.
    public static readonly CardDef ArmyAnt = new(
        Id: "DK-CLAW-11", Name: "Army Ant", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 4, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-11Die", energyType: "Claw", (1, 3, 2), (1, 4, 2), (2, 5, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["On Field", "Sacrifice"],
        RawText: "On Field: you may Sacrifice one of your creatures (it goes Out of Play, not KO'd). If you do, deal damage equal to its ATK to a target creature.",
        Abilities: [new TriggeredAbility(TriggerKind.DieFielded, new MayPay(
            Cost: new Sacrifice(new TargetFilter(Ownership: TargetOwnership.Own, Zones: [Zone.FieldZone], BindAs: "sacrificed",
                Prompt: "Sacrifice - choose one of your creatures (it goes Out of Play).")),
            Then: new DealDamage(new StatOf("sacrificed", StatKind.Attack), new TargetFilter(
                Prompt: "deal damage equal to the sacrificed creature's ATK to a target creature.")),
            Prompt: "Sacrifice one of your creatures to deal damage equal to its ATK to a target creature?"))],
        Continuous: []);

    // Range, Aftershock, Breath Weapon (2026-10-03, same list). Range and
    // Breath Weapon carry an X: the bare keyword is X = 1, "Range 2" and
    // so on for more (KeywordAbilities.ParamOf).

    // Range - knocks a target off its perch with a jet of water.
    public static readonly CardDef Archerfish = new(
        Id: "DK-WING-12", Name: "Archerfish", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Wing"],
        Die: CharacterDie("DK-WING-12Die", energyType: "Wing", (0, 2, 2), (1, 3, 2), (1, 4, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["Range"],
        RawText: "Range (when any creature with Range attacks, every active Range creature on both sides deals 1 damage to a target opposing creature, all at once).",
        Abilities: [],
        Continuous: []);

    // Aftershock - blasts whatever finally takes it out.
    public static readonly CardDef BombardierBeetle = new(
        Id: "DK-SHELL-11", Name: "Bombardier Beetle", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 3, EnergySymbolIds: ["Shell"],
        Die: CharacterDie("DK-SHELL-11Die", energyType: "Shell", (0, 1, 3), (1, 2, 3), (1, 3, 4)),
        DieLimit: 4, Affiliations: [], Keywords: ["Aftershock"],
        RawText: "Aftershock: deal 2 damage to a target character die. (Aftershock: when your opponent makes this creature leave the Field - KO, damage, removal.)",
        Abilities: [new TriggeredAbility(TriggerKind.DieRemovedByOpponent,
            new DealDamage(new Fixed(2), new TargetFilter(Prompt: "Aftershock - deal 2 damage to a target character die.")))],
        Continuous: []);

    // Breath Weapon - the dragon's bite, poisoning the whole board.
    public static readonly CardDef KomodoDragon = new(
        Id: "DK-CLAW-12", Name: "Komodo Dragon", Subtitle: null, Set: "Dice Kingdom", CardType: CardType.Character,
        PurchaseCost: 5, EnergySymbolIds: ["Claw"],
        Die: CharacterDie("DK-CLAW-12Die", energyType: "Claw", (1, 4, 3), (2, 5, 4), (2, 6, 3)),
        DieLimit: 4, Affiliations: [], Keywords: ["Breath Weapon"],
        RawText: "Breath Weapon (when this attacks, you may pay 1 energy to deal 1 damage to the opponent and every one of their creatures; once per turn however many Komodo Dragons attack).",
        Abilities: [],
        Continuous: []);

    // --- Basic Actions: one per Champion (2026-09-26, user call). Rather
    // than each team drafting two Basic Actions, each Champion brings
    // one, and both are shared (community, rule 2.1.2) - either player
    // may buy either Champion's action dice. Each doubles as that game's
    // Globals (usable by either player), costing 1 energy of the type of
    // the Champion it came with. Texts are the real Dice Masters cards
    // the user named, trimmed to the closed vocabulary; plant/nature
    // themed art (icons.tsx) marks them as statless actions. ---

    // Same shape as a Character die - 3 action faces in place of the 3
    // stat faces, plus energy faces of the Champion's own type (2, 2, 1)
    // - so buying the OPPONENT'S action die is also a way to get their
    // energy, which is what that Champion's Global costs.
    private static DieDefinition ActionDie(string dieId, string energyType) => StandardDie(dieId, energyType,
    [
        new Face([], Kind: FaceKind.ActionFace),
        new Face([], Kind: FaceKind.ActionFace),
        new Face([], Kind: FaceKind.ActionFace),
    ]);

    // Purchase costs are the real Dice Masters cards' (user call,
    // 2026-09-27; they were a flat placeholder 3): 4/4/4/3, any energy (a
    // Basic Action has no energy type of its own). 3 dice per card.
    private const int ActionDieLimit = 3;

    // Wolf (Claw).
    public static readonly CardDef AngerIssues = new(
        Id: "DK-ACT-01", Name: "Anger Issues", Subtitle: "Basic Action", Set: "Dice Kingdom", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: ActionDie("DK-ACT-01Die", "Claw"),
        DieLimit: ActionDieLimit, Affiliations: [], Keywords: [],
        RawText: "Target creature gets +3A and Overcrush this turn. Global: Pay 1 Claw. Target creature gets +1A this turn.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
                new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, BindAs: "angry",
                    Prompt: "choose a creature to get +3A and Overcrush this turn."), AtkDelta: 3),
                new GrantTag(new TargetFilter(Bound: "angry"), ["Overcrush"]),
            ])),
            new TriggeredAbility(TriggerKind.Global,
                new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, Prompt: "choose a creature to get +1A this turn."), AtkDelta: 1),
                EnergyCost: new EnergyCost(1, "Claw")),
        ],
        Continuous: []);

    // Armadillo (Shell).
    public static readonly CardDef Distraction = new(
        Id: "DK-ACT-02", Name: "Distraction", Subtitle: "Basic Action", Set: "Dice Kingdom", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: ActionDie("DK-ACT-02Die", "Shell"),
        DieLimit: ActionDieLimit, Affiliations: [], Keywords: [],
        RawText: "Your opponent chooses two of their creatures. Those creatures can't block this turn. Global: Pay 1 Shell. Move one attacker from the Attack Zone back to its Field Zone.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieUsed,
                new CombatFlag(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing,
                    Zones: [Zone.FieldZone], Count: 2, AnsweredBy: TargetOwnership.Opposing,
                    Prompt: "choose two of your creatures. They can't block this turn."), CombatFlagKind.CantBlock)),
            new TriggeredAbility(TriggerKind.Global,
                new MoveDie(new TargetFilter(Kind: TargetKind.CharacterDie, Zones: [Zone.AttackZone], AttackersOnly: true,
                    Prompt: "choose an attacker to send back to its Field Zone."), Zone.FieldZone),
                EnergyCost: new EnergyCost(1, "Shell")),
        ],
        Continuous: []);

    // Armadillo (Shell) since 2026-09-29, replacing Distraction (user call:
    // its Global - send an attacker back - worked against Armadillo's own
    // Rhinoceros + forced-block plan). The real Dice Masters Basic Action
    // (DPS001, whose migration only ever modelled the Global). "Deal damage
    // to each other" is simultaneous: both dice are bound before any damage,
    // and StatOf captures ATK at bind time, so a die the first hit KOs still
    // hits back with the ATK it had. The first step is a tagless GrantTag -
    // a no-op whose only job is to pick and bind the fighter you control.
    public static readonly CardDef Archnemesis = new(
        Id: "DK-ACT-05", Name: "Archnemesis", Subtitle: "Basic Action", Set: "Dice Kingdom", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: ActionDie("DK-ACT-05Die", "Shell"),
        DieLimit: ActionDieLimit, Affiliations: [], Keywords: [],
        RawText: "Target character die you control and target opposing character die deal damage to each other equal to their A. Global: Pay 1 Shell. Target character die has D equal to its A this turn.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
                new GrantTag(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, BindAs: "mine",
                    Prompt: "choose YOUR creature to fight with."), []),
                new DealDamage(new StatOf("mine", StatKind.Attack),
                    new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Opposing, BindAs: "theirs",
                        Prompt: "choose the opposing creature it fights.")),
                new DealDamage(new StatOf("theirs", StatKind.Attack), new TargetFilter(Bound: "mine")),
            ])),
            new TriggeredAbility(TriggerKind.Global,
                new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie, BindAs: "t",
                    Prompt: "choose a creature whose D becomes its A this turn."), SetDefense: new StatOf("t", StatKind.Attack)),
                EnergyCost: new EnergyCost(1, "Shell")),
        ],
        Continuous: []);

    // Golden Eagle (Wing). The real card's Global is "once during your
    // turn"; only the once-per-turn half is modelled (a Global usable on
    // the opponent's turn is still once per turn per player).
    public static readonly CardDef Resurrection = new(
        Id: "DK-ACT-03", Name: "Resurrection", Subtitle: "Basic Action", Set: "Dice Kingdom", CardType: CardType.BasicAction,
        PurchaseCost: 4, EnergySymbolIds: [],
        Die: ActionDie("DK-ACT-03Die", "Wing"),
        DieLimit: ActionDieLimit, Affiliations: [], Keywords: [],
        RawText: "Choose a die in your Used Pile. Roll it into your Reserve Pool. Global: Pay 1 Wing, once per turn. Draw a die from your bag into your Prep Area.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
                new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile], BindAs: "risen",
                    Prompt: "choose a die in your Used Pile to roll into your Reserve Pool."), Zone.ReservePool),
                new Reroll(new TargetFilter(Bound: "risen")),
            ])),
            new TriggeredAbility(TriggerKind.Global,
                new DrawToZone(1, Zone.PrepArea),
                EnergyCost: new EnergyCost(1, "Wing"), OncePerTurn: true),
        ],
        Continuous: []);

    // Great Horned Owl (Eye). Same two-step swap as the DPS Mutation
    // (DpsCards.cs): the Used Pile pick is snapshotted before the first
    // move, so it can't pick the die it just swapped out. Tags carry the
    // card's name, which is how Tardigrades and the four action cards are
    // kept out of the Used Pile pick without a new TargetKind.
    public static readonly CardDef Mutation = new(
        Id: "DK-ACT-04", Name: "Mutation", Subtitle: "Basic Action", Set: "Dice Kingdom", CardType: CardType.BasicAction,
        PurchaseCost: 3, EnergySymbolIds: [],
        Die: ActionDie("DK-ACT-04Die", "Eye"),
        DieLimit: ActionDieLimit, Affiliations: [], Keywords: [],
        RawText: "Swap one of your fielded creatures with a non-Tardigrade creature die in your Used Pile. It comes in at level 1 (no On Field). Global: Pay 1 Eye. Spin one of your creatures down a level to spin another creature up a level.",
        Abilities: [
            new TriggeredAbility(TriggerKind.DieUsed, new Sequence([
                new MoveDie(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own, Zones: [Zone.FieldZone],
                    Prompt: "choose one of your creatures to swap OUT (it goes to your Used Pile)."), Zone.UsedPile),
                new MoveDie(new TargetFilter(Kind: TargetKind.AnyDie, Ownership: TargetOwnership.Own, Zones: [Zone.UsedPile],
                    Tags: new TagQuery(NoneOf: ["sidekick", "Anger Issues", "Distraction", "Resurrection", "Mutation", "Archnemesis"]), BindAs: "mutant",
                    Prompt: "choose a creature from your Used Pile to swap IN at level 1."), Zone.FieldZone),
                new Spin(new TargetFilter(Bound: "mutant"), SetLevel: 1),
            ])),
            // Each pick says which it is (2026-09-27: "it wasn't clear which
            // die I was supposed to be selecting when"), and only offers
            // dice that can actually move that way (a level below / above
            // on that die - a Tardigrade only has levels 2 and 3).
            new TriggeredAbility(TriggerKind.Global, new Sequence([
                new Spin(new TargetFilter(Kind: TargetKind.CharacterDie, Ownership: TargetOwnership.Own,
                    Stat: new StatThreshold(StatKind.LevelsBelow, Min: 1),
                    Prompt: "first, choose one of YOUR creatures to spin DOWN a level."), LevelDelta: -1),
                new Spin(new TargetFilter(Kind: TargetKind.CharacterDie,
                    Stat: new StatThreshold(StatKind.LevelsAbove, Min: 1),
                    Prompt: "now choose a creature to spin UP a level."), LevelDelta: 1),
            ]), EnergyCost: new EnergyCost(1, "Eye")),
        ],
        Continuous: []);

    public static readonly IReadOnlyDictionary<string, string> ActionByChampion = new Dictionary<string, string>
    {
        ["Wolf"] = AngerIssues.Id,
        ["Armadillo"] = Archnemesis.Id, // was Distraction until 2026-09-29
        ["GoldenEagle"] = Resurrection.Id,
        ["GreatHornedOwl"] = Mutation.Id,
    };

    // One registered pool: native cards plus imports selected by their own
    // metadata. Completing an import makes it playable without another list.
    public static readonly IReadOnlyDictionary<string, CardDef> TeamBuilderCatalog = new List<CardDef>
    {
        HoneyBadger, Wolverine, GrizzlyBear, Orca, PeregrineFalcon, Silverback, Stoat, CapeBuffalo, Mongoose,
        Hippopotamus, MuskOx, Pangolin, HermitCrab, Opossum, QueenTermite, Rhinoceros, BoxTurtle,
        Osprey, BarnSwallow, Hummingbird, MountainGoat, MonarchButterfly, HomingPigeon, Greyhound, Phoenix, Swift,
        BarnOwl, Hyena, Anglerfish, Cowbird, Magpie, Basilisk, Elephant, Fox, Cuttlefish,
        FrilledLizard, FlyingSquirrel, ElectricEel, Chameleon, Firefly, Leech, Meerkat, ArmyAnt,
        Archerfish, BombardierBeetle, KomodoDragon,
        AngerIssues, Distraction, Resurrection, Mutation, Archnemesis,
    }
        .Concat(CardCatalog.BuildCatalog().Values.Where(card => card.IncludeInTeamBuilder).Select(ToKingdomCard))
        .Concat(DpsCards.All.Where(card => card.IncludeInTeamBuilder).Select(ToKingdomCard))
        .Concat(BonusCards.All.Where(card => card.IncludeInTeamBuilder).Select(ToKingdomCard))
        .Concat(KingdomImports.All.Select(ToKingdomCard))
        .ToDictionary(card => card.Id);

    public static readonly IReadOnlyDictionary<string, CardDef> Catalog = TeamBuilderCatalog.Values
        .Where(card => card.IsImplemented)
        .ToDictionary(card => card.Id);

    // One conversion for every registered adaptation. IDs, affiliations, stats,
    // printed-face bursts, and unfinished effect trees remain sourced from the card.
    // Face order and energy faces follow the same factory as native Dice Kingdom dice.
    // These cards stay blocked from game launch until their full rules are implemented.
    private static CardDef ToKingdomCard(CardDef source)
    {
        var energy = source.KingdomEnergyType;
        var name = source.KingdomName;
        if (string.IsNullOrWhiteSpace(name) ||
            energy is not ("Claw" or "Wing" or "Eye" or "Shell"))
            throw new InvalidOperationException($"Missing Dice Kingdom identity for {source.Id}.");

        var text = Regex.Replace(source.RawText, $@"\b{Regex.Escape(source.Name)}\b",
            _ => name, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(Fist|Bolt|Mask|Shield)\b|\[[FBMS]\]",
            _ => energy, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bSidekick(s?)\b|\[PAWN\]",
            match => "Tardigrade" + match.Groups[1].Value, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bSuperstar(s?)\b",
            match => "creature" + match.Groups[1].Value, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bWhen fielded\s*,?\s*", "On Field: ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, $@"\bWhen {Regex.Escape(name)} attacks\s*,?\s*", "On Attack: ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(Energize|Amplify)\s*[-–—]\s*", "$1: ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bGlobal\s+Pay\b", "Global: Pay", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bAlly\b", "Kinfolk", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bBoomerang\b", "Rebound", RegexOptions.IgnoreCase);
        return source with
        {
            Name = name,
            Subtitle = null,
            Set = "Dice Kingdom",
            EnergySymbolIds = source.CardType.IsCommunity() ? [] : [energy],
            RawText = text,
            Keywords = source.Keywords
                .Concat(source.Abilities.Any(a => a.Trigger == TriggerKind.DieFielded) ? ["On Field"] : [])
                .Concat(source.Abilities.Any(a => a.Trigger == TriggerKind.DieAttacks) ? ["On Attack"] : [])
                .Concat(source.Abilities.Any(a => a.Trigger == TriggerKind.DieBlocks) ? ["On Block"] : [])
                .Distinct().ToList(),
            Die = StandardDie(source.Die.Id, energy, source.Die.Faces
                .Where(face => face.Kind != FaceKind.EnergyFace)
                .Select(face => face with
                {
                    Symbols = face.Symbols.Select(symbol => symbol.SymbolId == "Wild"
                        ? symbol : symbol with { SymbolId = energy }).ToList(),
                })),
            Abilities = source.Abilities.Select(ability => ability with
            {
                EnergyCost = ability.EnergyCost?.RequiredSymbolId is null ? ability.EnergyCost
                    : ability.EnergyCost with { RequiredSymbolId = energy },
                Filter = ability.Filter is null ? null : ability.Filter with
                {
                    Tags = ability.Filter.Tags is null ? null : new TagQuery(
                        ability.Filter.Tags.AnyOf?.Select(tag => tag is "Fist" or "Bolt" or "Mask" or "Shield" ? energy : tag).ToList(),
                        ability.Filter.Tags.NoneOf?.Select(tag => tag is "Fist" or "Bolt" or "Mask" or "Shield" ? energy : tag).ToList()),
                    Payment = ability.Filter.Payment?.SymbolId is null ? ability.Filter.Payment
                        : ability.Filter.Payment with { SymbolId = energy },
                },
            }).ToList(),
        };
    }

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
    // Tardigrade's Wild faces (previously only useful for a
    // same-type purchase) a real reason to matter - it's now the one
    // guaranteed way to pay for an off-type splash card.
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CharactersByChampion = new Dictionary<string, IReadOnlyList<string>>
    {
        // Own 2/3/mid/power: HoneyBadger(2)/Mongoose(3)/Wolverine(4)/Silverback(7).
        // Tiger (was the 6-cost power slot) replaced 2026-09-28 by
        // Silverback, a genuine finisher tier above the old curve-topper.
        ["Wolf"] = [HoneyBadger.Id, Mongoose.Id, Wolverine.Id, Silverback.Id, MountainGoat.Id, Greyhound.Id, Hippopotamus.Id, Elephant.Id],
        // Own 2/3/mid/power: HermitCrab(2)/Pangolin(3)/MuskOx(4)/Rhinoceros(6).
        // SnappingTurtle (was the 5-cost power slot) replaced 2026-09-28.
        ["Armadillo"] = [HermitCrab.Id, Pangolin.Id, MuskOx.Id, Rhinoceros.Id, Fox.Id, Cowbird.Id, CapeBuffalo.Id, Hummingbird.Id],
        // Own 2/3/mid/power: Swift(2)/BarnSwallow(3)/Osprey(4)/Phoenix(6).
        // Albatross (was the 5-cost power slot) replaced 2026-09-29 by
        // Phoenix, a genuine finisher tier, same treatment the other
        // three Champions' power slots already got 2026-09-28.
        ["GoldenEagle"] = [Swift.Id, BarnSwallow.Id, Osprey.Id, Phoenix.Id, GrizzlyBear.Id, PeregrineFalcon.Id, BoxTurtle.Id, BarnOwl.Id],
        // Own 2/3/mid/power: Cuttlefish(2)/Magpie(3)/Hyena(4)/Basilisk(7).
        // Raven (was the 5-cost power slot, despite what this comment used
        // to claim - it was never actually 7) replaced 2026-09-28.
        ["GreatHornedOwl"] = [Cuttlefish.Id, Magpie.Id, Hyena.Id, Basilisk.Id, Opossum.Id, QueenTermite.Id, Stoat.Id, HomingPigeon.Id],
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
    // Once-per-turn powers since 2026-10-04 (ChampionPowers), replacing
    // the always-on passives (+1 ATK, +1 DEF, -1 fielding, Foresight). The
    // simulator's SIM_LEGACY_POWERS=on puts the old ones back for comparison.
    public static readonly IReadOnlyList<ChampionDef> Champions =
    [
        // Renamed from "Lion" (2026-09-06) - a real fan-art avatar exists
        // for this one now (icons.tsx's WolfIcon), so the Claw Champion
        // became the animal the art actually is.
        // +3, the user's original number. Briefly +1: against the old
        // race-everything bot +3 won 84%, but that bot never kept blockers,
        // so every pump was free face damage. With the expected-threat bot
        // (2026-10-04), +3 is the most even setting: Wolf 47, Armadillo 49,
        // Owl 60, Eagle 44 (+1: Wolf 19). See v3/DESIGN_NOTES.md.
        new("Wolf", "Wolf", "Claw", ChampionPassiveKind.PumpOneAttack, Amount: 3)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Claw"), Count: 8)],
        },
        new("Armadillo", "Armadillo", "Shell", ChampionPassiveKind.ShieldOneFromCombat, Amount: 0)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Shell"), Count: 8)],
        },
        new("GoldenEagle", "Golden Eagle", "Wing", ChampionPassiveKind.FieldOneFree, Amount: 0)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Wing"), Count: 8)],
        },
        // Foresight replaced a -1 purchase discount (2026-09-27 playtest:
        // "probably too much").
        new("GreatHornedOwl", "Great Horned Owl", "Eye", ChampionPassiveKind.SpinOne, Amount: 1)
        {
            TardigradePool = [new BasicDicePoolEntry(TardigradeDie("Eye"), Count: 8)],
        },
    ];

    // Every keyword a Dice Kingdom card may carry, for the card reference
    // (tools/CardCatalogDocs). `Original` is WizKids' Dice Masters wording,
    // copied once (2026-10-10) from https://wizkids.com/dicemasters/keywords/
    // - a fixed baseline, so it's kept even where this game later changes
    // the rule. `Description` says only what Dice Kingdom does differently
    // (null = as written), or, for our own labels, what they mean. A
    // keyword with an X (Range 2) is declared bare; bare = X of 1
    // (KeywordAbilities.ParamOf).
    public static readonly IReadOnlyList<KeywordDef> Keywords =
    [
        new("Kinfolk", Description: "This creature also counts as a Tardigrade while in the Field or Attack Zone; it keeps its other attributes."),
        new("Rebound", Description: "After using this action die, roll it. An action result goes to Prep; an energy result goes to Used."),
        new("Impulse", Description: "Use the listed Impulse ability when purchasing this die.", WrittenOnCard: true),
        // The engine supplies these - a card just lists the word.
        new("Fast", Description: null, Original: "Characters with Fast deal combat damage before other characters, all at the same time, instead of at the regular time for combat damage."),
        new("Amplify", Description: "When you use an action die, spin this creature up one level."),
        new("Overcrush", Description: "Also: any lane with two or more attackers has Overcrush, and one attacker's Overcrush covers its whole lane.", Original: "When attacking, if this character KOs all of its blockers (or they are removed for other reasons), it deals any remaining damage to your opponent."),
        new("Deadly", Description: null, Original: "At the end of the turn, character dice that were engaged with a character die that has Deadly are KO'd (even if the character with Deadly has been KO'd or leaves the Field Zone)."),
        new("Intimidate", Description: null, Original: "When fielded, remove target opposing character die from the Field Zone until end of turn. Place it next to your character cards during that turn as a reminder."),
        new("Infiltrate", Description: null, Original: "When a character die with Infiltrate attacks and is not blocked, you may choose to remove that character die from combat (immediately after blockers are declared, before Action dice or Global Abilities may be used). If you do, that character die deals 1 damage to your opponent, and the die remains in your Field Zone."),
        new("Attune", Description: "Attune X deals X; the bare keyword is 1. Only action dice count - using a Global doesn't trigger it.", Original: "While a character you control with Attune is active, when you use an action die, that character deals 1 damage to target player or character die (no matter how many of that character's dice are active)."),
        new("Obscure", Description: "Only while this creature is active.", Original: "When you use an Action die, this character is unblockable until end of turn."),
        new("Energy Drain", Description: "Energy Drain X spins down X levels; the bare keyword is 1. A creature fighting several drainers takes the biggest X, not the sum.", Original: "After blockers are assigned, spin each character engaged with a character die with Energy Drain down one level. Character dice at level 1 can't be spun down."),
        new("Tag Out", Description: null, Original: "After blockers are declared, you may Prep this die from the Field Zone to give target character die +2A and +2D until end of turn."),
        new("Range", Description: "The bare keyword is Range 1.", Original: "When one or more character dice with Range attack, each active character die with Range (on both sides) simultaneously deals damage equal to its Range value (X) to target opposing character die. Each character die with Range may choose a different target, and the defending player still resolves their Range damage even if the Range die was KO'd by the active player's Range damage. All Range damage is dealt simultaneously; the active player chooses their targets and resolves the damage, immediately followed by the inactive player choosing their targets and resolving the damage."),
        new("Breath Weapon", Description: "The bare keyword is Breath Weapon 1.", Original: "When a character die with Breath Weapon X attacks, you may pay X energy. Deal X damage to your opponent and all of their character dice. You may use multiple Breath Weapons in a turn, but only one per character (no matter how many copies of that die are attacking)."),
        // Labels - each card writes its own ability; the word names when it happens or what it costs.
        new("Elder", Description: "A keyword marker for abilities that refer to Elder creatures; it has no effect by itself.", WrittenOnCard: true),
        new("Energize", Description: null, WrittenOnCard: true, Original: "Whenever you roll this die on one of its double energy faces, you must use its Energize effect (this isn't optional). During the Roll and Reroll Step, only check at the end of the Step. The character with Energize does not need to be active for the ability to trigger."),
        new("Sacrifice", Description: null, WrittenOnCard: true, Original: "Sacrificed characters are moved from the Field Zone to the Used Pile (often as a cost or penalty). If one of your character dice is sacrificed while you are the active player, it goes Out of Play first."),
        new("Aftershock", Description: null, WrittenOnCard: true, Original: "When this character leaves the Field Zone because of your opponent (damage from their characters or actions, getting KO'd, being removed from the game, sent to the Used Pile or Reserve Pool) you must use their Aftershock effect."),
        new("Awaken", Description: "Not optional here - the Awaken ability always fires.", WrittenOnCard: true, Original: "When a die with Awaken spins up 1 or more levels, you may use its Awaken effect. You get to use the effect for each individual die that spins up, so if you have two copies of the same die active and they both spin up, you will get two instances of the effect."),
        // Dice Kingdom's own timing labels - not Dice Masters keywords.
        new("On Field", "Its ability fires when it's fielded.", WrittenOnCard: true),
        new("On Attack", "Its ability fires when it attacks.", WrittenOnCard: true),
        new("On Block", "Its ability fires when it blocks.", WrittenOnCard: true),
    ];

    public static readonly GameConfig Config = new(
        Id: "dice-kingdom",
        Name: "Dice Kingdom",
        EnergySymbols:
        [
            new SymbolDef("Claw"), new SymbolDef("Shell"), new SymbolDef("Wing"), new SymbolDef("Eye"),
            new SymbolDef("Wild", IsWild: true),
        ],
        Keywords: Keywords,
        Rules: new RulesConfig(
            StartingLife: 20,
            DrawCount: 4,
            MaxTeamCards: 8, // all 8 Characters matching the chosen Champion's energy type
            MaxTeamDice: 32, // 8 Characters x DieLimit 4
            BasicActionCount: 1), // the Champion's own (ActionByChampion)
        BasicDicePool: [], // every player has a ChampionId, so this is never actually read
        BasicActionSlots: 1)
    {
        Champions = Champions,
    };
}
