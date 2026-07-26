namespace DiceFight.Engine.Model;

// Rule 1.5 - Play Areas. AttackZone is a subset of FieldZone (1.5.6.1) but
// tracked separately here since attacker/blocker state needs to be queryable.
public enum Zone
{
    Bag,
    PrepArea,
    ReservePool,
    FieldZone,
    AttackZone,
    UsedPile,
    OutOfPlay
}

// Rule 1.4.2 / 1.4.3. Wild and Generic are resolved at spend-time, not
// stored as a die's fixed type - a die's face determines what it produces.
public enum EnergyType
{
    Fist,
    Bolt,
    Mask,
    Shield
}

public enum Alignment
{
    Good,
    Neutral,
    Evil
}

// Rule 1.2.2 / 1.2.3 - Basic/Epic Basic Action are subsets of Action with
// extra constraints, not a different shape of card.
public enum CardType
{
    Character,
    Action,
    BasicAction,
    EpicBasicAction
}

// Rule 1.6.4/1.6.5 - what a rolled die's face represents. Unrolled dice
// (1.6.3) have no status; this only applies once a die is in a rolled zone.
public enum DieStatus
{
    Unrolled,
    Energy,
    Character,
    SidekickCharacter,
    Action
}

// Rule 3.4 - the trigger points abilities can hook. Named after the rule's
// own terms so AbilityDef.Trigger reads the same as the card text.
public enum TriggerType
{
    WhenFielded,
    WhileActive,
    WhenAttacks,
    WhenBlocks,
    WhenBlocked,
    WhenEngaged,
    WhenDamaged,
    WhenKOd,
    Global,
    Burst1,
    Burst2
}

// Rule 2.2.3 - the five steps of a turn, in fixed order (2.2.4 - cannot
// return to a completed step).
public enum TurnStep
{
    ClearAndDraw,
    RollAndReroll,
    Main,
    Attack,
    CleanUp
}

// Rule 2.7.0.1 - the six sub-steps of the Attack Step, stricter and
// one-way (2.7.0.3) even relative to the outer TurnStep sequence.
// NotInAttack/Done are engine bookkeeping states, not rulebook sub-steps.
public enum AttackSubStep
{
    NotInAttack,
    DeclareAttackers,
    DeclareBlockers,
    ActionAndGlobalWindow,
    AssignCombatDamage,
    WhenDamagedAbilities,
    ResolveDamageAndWhenKOd,
    Done
}
