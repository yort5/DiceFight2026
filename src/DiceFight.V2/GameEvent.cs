using DiceFight.V2.Model;
using DiceFight.V2.Model.Effects;

namespace DiceFight.V2;

// One firing of one of the 10 frozen trigger events (V2_VOCABULARY.md
// Part 1). SubjectDie is null for events with no single die at their
// center (TurnStepEntered, PurchaseMade, DiceDrawn) - SubjectControllerId
// is always set regardless, since even a die-less event still belongs to
// a player (whose turn/purchase/draw it is).
public sealed record GameEvent(
    TriggerKind Kind,
    DieInstance? SubjectDie,
    string SubjectControllerId,
    TurnStep Step,
    EventPayload? Payload = null);
