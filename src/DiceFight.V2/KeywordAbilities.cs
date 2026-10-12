using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// Keywords whose rules text IS an ability (2026-10-03, Dice Kingdom). A
// card just lists the keyword, the same as Deadly or Overcrush, and the
// engine supplies the ability - so the card text stays one word, a
// granted keyword (GrantTag, TagAura) works with no extra authoring, and
// blanking switches it off through the same AbilitiesOf path as printed
// text.
public static class KeywordAbilities
{
    // Dice Masters' Intimidate: "When fielded, remove target opposing
    // character die from the Field Zone until end of turn." The die waits
    // in Zone.Intimidated - not active, so it can't block, can't be
    // targeted and its "while active" text is off - and TurnEngine.CleanUp
    // returns it on the same face. Only the Field Zone: fielding happens
    // in Main, when the Attack Zone is empty anyway.
    public static readonly TriggeredAbility Intimidate = new(TriggerKind.DieFielded,
        new MoveDie(new TargetFilter(Ownership: TargetOwnership.Opposing, Zones: [Zone.FieldZone],
            Prompt: "Intimidate - choose an opposing creature to remove from the Field until end of turn."), Zone.Intimidated));

    // Dice Masters' Infiltrate: "When this character die is unblocked,
    // you may return this die to the Field Zone and it deals your opponent
    // 1 damage." Not an EventBus trigger - no event means "unblocked" - so
    // CombatEngine.DeclareBlockers queues this for each unblocked
    // Infiltrate attacker, after the On Block abilities. InZone guards an
    // attacker something else already removed. Back on the Field it is
    // still a normal active die (targetable, in the post-block window
    // too); what it skips is combat damage and the unblocked attacker's
    // trip Out of Play.
    public static readonly MayPay InfiltrateChoice = new(Cost: null,
        Then: new Sequence([
            new MoveDie(new TargetFilter(Self: true), Zone.FieldZone),
            new DealDamage(new Fixed(1), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
            new EmitEvent(TriggerKind.InfiltrateUsed),
        ]),
        Prompt: "Infiltrate - return it to the Field and deal 1 damage to the opponent (instead of its full attack)?");

    public static readonly EffectNode InfiltrateOffer = new Conditional(new InZone(Zone.AttackZone), InfiltrateChoice);

    // "While this character is active, each time you use an action die"
    // - the shared trigger of Amplify, Attune and Obscure. The DieUsed event only
    // fires from TurnEngine.UseAction, so a Global never counts (a
    // Global-triggered keyword would be its own keyword - user, 2026-10-03).
    // "Action die" is the broad category (CardTypes.IsActionDie), so any
    // action die qualifies, Basic or not. EventBus only offers listeners
    // that are active, which is the "while active" half.
    private static readonly EventFilter YouUseAnActionDie = new(Ownership: TargetOwnership.Own);

    // Each active die with Amplify spins up when its controller uses an
    // action die. Spin handles the printed levels and emits the same
    // face-change event as any other spin, including for granted Awaken.
    public static readonly TriggeredAbility Amplify = new(TriggerKind.DieUsed,
        new Spin(new TargetFilter(Self: true), LevelDelta: 1),
        YouUseAnActionDie);

    // Dice Masters' Attune: "...this character deals 1 damage to target
    // opponent or target character die." Parameterised the way Boom Boom
    // reads as Attune 2: the keyword is "Attune" (1) or "Attune N".
    public static TriggeredAbility Attune(int amount) => new(TriggerKind.DieUsed,
        new DealDamage(new Fixed(amount), new TargetFilter(Kind: TargetKind.CharacterDieOrOpponent,
            Prompt: $"Attune - deal {amount} damage to the opponent or a target character die.")),
        YouUseAnActionDie);

    // Dice Masters' Obscure: "...this character can't be blocked this
    // turn." Used after blocks it does nothing - the die is already
    // blocked or not.
    public static readonly TriggeredAbility Obscure = new(TriggerKind.DieUsed,
        new CombatFlag(new TargetFilter(Self: true), CombatFlagKind.Unblockable),
        YouUseAnActionDie);

    // Dice Masters' Tag Out: "After blockers are declared, you may Prep
    // this die from the Field Zone to give target Character die +2A and
    // +2D until end of turn." Either player's dice, so long as they're in
    // the Field Zone (not attacking or blocking). Queued by
    // CombatEngine.DeclareBlockers after Infiltrate, so both resolve
    // before the Action/Global window. Prep is not a KO (rule 1.5.3.2).
    public static readonly MayPay TagOutChoice = new(Cost: null,
        Then: new Sequence([
            new MoveDie(new TargetFilter(Self: true), Zone.PrepArea),
            new ModifyStat(new TargetFilter(Kind: TargetKind.CharacterDie,
                Prompt: "Tag Out - choose a creature to get +2A and +2D this turn."), AtkDelta: 2, DefDelta: 2),
        ]),
        Prompt: "Tag Out - send it to your Prep Area to give a creature +2A and +2D this turn?");

    public static readonly EffectNode TagOutOffer = new Conditional(new InZone(Zone.FieldZone), TagOutChoice);

    // Dice Masters' Breath Weapon X (queued by CombatEngine.DeclareAttackers).
    // "Does not target" - it hits every opposing creature (Count: 0).
    public static EffectNode BreathWeapon(int x) => new MayPayEnergy(x,
        new Sequence([
            new DealDamage(new Fixed(x), new TargetFilter(Kind: TargetKind.Player, Ownership: TargetOwnership.Opposing)),
            new DealDamage(new Fixed(x), new TargetFilter(Ownership: TargetOwnership.Opposing, Count: 0)),
        ]),
        Prompt: $"Breath Weapon {x} - pay {x} energy to deal {x} damage to the opponent and every one of their creatures?");

    // Dice Masters' Energize: "Whenever you roll this die on one of its
    // double energy faces, you must use its Energize ability... During the
    // Roll and Reroll Step, only check at the end of the Step. The
    // character with Energize does not need to be active." The effect is
    // the card's, so this wraps it - authored as `..KeywordAbilities.
    // Energize(effect)` (first built for the DPS migration, 2026-09-01).
    // Two halves: the start-of-Main check (EventBus's own Reserve Pool
    // carve-out lets a not-yet-active die listen), and any LATER reroll -
    // ExcludeStep keeps a Roll-and-Reroll reroll from firing it a second
    // time (2026-10-03, once rerolls became interactive).
    public static IReadOnlyList<TriggeredAbility> Energize(EffectNode effect)
    {
        var checkAndRun = new Conditional(
            new CountAtLeast(new TargetFilter(Self: true, Stat: new StatThreshold(StatKind.SymbolCount, Min: 2)), 1),
            Then: effect);
        return
        [
            new TriggeredAbility(TriggerKind.TurnStepEntered, checkAndRun, Filter: new EventFilter(Step: StepIds.Main)),
            new TriggeredAbility(TriggerKind.DieFaceChanged, checkAndRun,
                Filter: new EventFilter(RequireSelf: true, ExcludeCause: FaceChangeCause.Roll, ExcludeStep: StepIds.RollAndReroll)),
        ];
    }

    // "Energy Drain 2" -> 2 for name "Energy Drain"; the bare keyword is 1.
    public static int? ParamOf(string keyword, string name) =>
        keyword == name ? 1
        : keyword.StartsWith(name + " ", StringComparison.Ordinal) && int.TryParse(keyword.AsSpan(name.Length + 1), out var n) && n > 0 ? n
        : null;

    // Highest value of a parameterised keyword on a die (Range, Energy
    // Drain: "the higher value is used - the values are not added").
    public static int? MaxParam(IReadOnlySet<string> keywords, string name) =>
        keywords.Select(k => ParamOf(k, name)).Where(n => n is not null).Max();

    // "Attune" -> 1, "Attune 3" -> 3, anything else -> null.
    public static int? AttuneAmount(string keyword) => ParamOf(keyword, "Attune");

    public static IEnumerable<TriggeredAbility> For(IReadOnlySet<string> keywords)
    {
        if (keywords.Contains("Intimidate")) yield return Intimidate;
        if (keywords.Contains("Amplify")) yield return Amplify;
        if (keywords.Contains("Obscure")) yield return Obscure;
        // Each Attune keyword triggers on its own, so a die with a printed
        // Attune and a granted one deals both.
        foreach (var keyword in keywords)
            if (AttuneAmount(keyword) is { } amount) yield return Attune(amount);
    }
}
