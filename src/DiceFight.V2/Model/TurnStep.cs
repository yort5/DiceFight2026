namespace DiceFight.V2.Model;

// The rulebook's PHASES (starter rulebook TURN SUMMARY). Since Spike C
// (V2_VOCABULARY.md Part 13) this is the grouping label on a step, not
// the step itself - the engine's actual position is a cursor into
// GameConfig.Steps, an ordered flat list of TurnStepDef. Flattening the
// ORDER while keeping this as a grouping tag is what lets code still ask
// containment questions ("Main or Attack") that a pure flat list loses.
//
// StartOfTurn is new here: the TURN SUMMARY lists "any abilities that
// take place at the start of your turn" as a peer entry BEFORE Clear and
// Draw, not as a property of it - which is exactly why the flat list
// needs no before/at/after modelling.
public enum TurnStep
{
    StartOfTurn,
    ClearAndDraw,
    RollAndReroll,
    Main,
    Attack,
    CleanUp,
}

// One entry in a game's ordered step list (Spike C). Id is what an
// ability names to address this window (EventFilter.Step) and what the
// engine keys behavior off - same "engine knows behavior by Id, config
// declares which exist" contract keywords already use.
//
// NeedsInput separates the two genuinely different kinds of entry the
// TURN SUMMARY contains: decision windows where a player chooses (Main,
// the Action/Global window, selecting attackers) versus engine
// procedures that simply run (move energy to the Used Pile, return dice
// to the Field Zone, clear damage). Both are steps; only the first can
// pause, which is what Phase 9's API needs in order to know when it must
// wait for a client rather than advancing on its own.
public sealed record TurnStepDef(string Id, TurnStep Phase, bool NeedsInput = false);

// The canonical step ids the engine implements. Referenced by engine
// code and by authored cards alike, so a typo is a compile error rather
// than a step that silently never matches.
public static class StepIds
{
    public const string StartOfTurn = "start-of-turn";
    public const string ClearAndDraw = "clear-and-draw";
    public const string RollAndReroll = "roll-and-reroll";
    public const string Main = "main";
    public const string MainEnd = "main-end";
    public const string SelectAttackers = "select-attackers";
    public const string AttackEffects = "attack-effects";
    public const string AssignBlockers = "assign-blockers";
    public const string BlockEffects = "block-effects";
    public const string ActionGlobalWindow = "action-global-window";
    public const string FastDamage = "fast-damage";
    public const string NormalDamage = "normal-damage";
    public const string DamageAndKoEffects = "damage-ko-effects";
    public const string ReturnToField = "return-to-field";
    public const string CleanUp = "cleanup";
}

public static class TurnStepDefs
{
    // The steps this engine actually implements, in rulebook order.
    // Deliberately NOT the full TURN SUMMARY yet: entries whose
    // procedure isn't built (the attack-effects / block-effects /
    // damage-ko-effects windows, and the Fast/normal damage split) are
    // added when their behavior is,
    // following the same "declare it when it has a consumer" rule Phase
    // 4 used for unwired events. StepIds lists them all; this list is
    // what a game currently runs.
    //
    // Keyword windows (Range / Infiltrate / Tag Out) are likewise absent
    // until those keywords are implemented - the flat list makes them
    // expressible, which is the point of Spike C, but expressible is not
    // the same as built.
    public static readonly IReadOnlyList<TurnStepDef> Standard =
    [
        new(StepIds.StartOfTurn, TurnStep.StartOfTurn),
        new(StepIds.ClearAndDraw, TurnStep.ClearAndDraw),
        new(StepIds.RollAndReroll, TurnStep.RollAndReroll, NeedsInput: true),
        new(StepIds.Main, TurnStep.Main, NeedsInput: true),
        new(StepIds.MainEnd, TurnStep.Main),
        new(StepIds.SelectAttackers, TurnStep.Attack, NeedsInput: true),
        new(StepIds.AssignBlockers, TurnStep.Attack, NeedsInput: true),
        new(StepIds.ActionGlobalWindow, TurnStep.Attack, NeedsInput: true),
        new(StepIds.NormalDamage, TurnStep.Attack),
        new(StepIds.ReturnToField, TurnStep.Attack),
        new(StepIds.CleanUp, TurnStep.CleanUp),
    ];
}
