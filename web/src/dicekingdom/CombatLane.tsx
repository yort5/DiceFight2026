import { Fragment } from "react";
import { DieCube, type CubeSpin } from "./DieCube";
import { facesFor } from "./dieFaces";
import { characterFaceInfo, dieLabel } from "./dieHelpers";
import type { BlockAssignment, CardDef, Die } from "./types";

// Where the two mats meet - ported from ../CombatLane.tsx verbatim
// (same grid-seam approach, same divider-is-a-real-row reasoning), typed
// against V2's Die/BlockAssignment instead of v1's. The one real
// simplification: v3 has no gang-blocking (BlockPanel assigns at most
// one blocker per attacker), so each engagement has zero or one blocker,
// never several - the layout still takes an array so nothing else here
// has to special-case that.

export interface Selection {
  primary: string | null;
  secondary: string[];
}

const ATTACKER_SIZE = 50;

function cell(index: number, row: number) {
  return { gridColumn: index + 2, gridRow: row };
}

interface Engagement {
  attacker: Die;
  blockers: Die[];
}

function EngagementDie(props: {
  die: Die;
  size: number;
  cardsById: Map<string, CardDef>;
  mine: boolean;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
  spins?: Record<string, CubeSpin>;
  turnOffsets?: Record<string, number>;
}) {
  const { die, cardsById } = props;
  const face = characterFaceInfo(die);
  const selected = props.selection.primary === die.id || props.selection.secondary.includes(die.id);
  return (
    <button
      className={`lane-die${selected ? " selected" : ""}`}
      onClick={() => props.onGroupClick([die.id])}
      style={{ width: props.size + 22 }}
    >
      <DieCube
        {...facesFor(die, cardsById)}
        size={props.size}
        mine={props.mine}
        spin={props.spins?.[die.id]}
        turnOffset={props.turnOffsets?.[die.id]}
      />
      <span className="lane-die-name">{dieLabel(die, cardsById)}</span>
      {face && <span className="lane-die-stats">L{die.level} · {face.attack}A/{face.defense}D</span>}
    </button>
  );
}

export function CombatLane(props: {
  dice: Die[];
  cardsById: Map<string, CardDef>;
  assignments: BlockAssignment[];
  /** Whose dice belong on the bottom half of the lane. */
  nearPlayerId: string;
  selection: Selection;
  onGroupClick: (ids: string[]) => void;
  spins?: Record<string, CubeSpin>;
  turnOffsets?: Record<string, number>;
  /** True only for the defender during Assign Blockers - direct feedback
   *  (2026-09-05): blocking used to require a separate right-hand-pane
   *  picker ("click an attacker, then a defender to block it"); now the
   *  blocker slot itself - the zone drawn right across from each
   *  attacker - is the drop target: click your own Field Zone die, then
   *  click the slot across from whichever attacker you want it to
   *  block. Clicking an already-filled slot with nothing selected
   *  clears it back to unblocked. */
  canAssignBlockers?: boolean;
  onSlotClick?: (attackerDieId: string) => void;
}) {
  const { dice, cardsById, assignments, nearPlayerId } = props;
  const byId = new Map(dice.map((d) => [d.id, d]));
  const attackers = dice.filter((d) => d.zone === "AttackZone");
  const engagements: Engagement[] = attackers.map((attacker) => ({
    attacker,
    blockers: assignments
      .filter((a) => a.attackerDieId === attacker.id)
      .map((a) => byId.get(a.blockerDieId))
      .filter((d): d is Die => d != null),
  }));

  const dieProps = {
    cardsById,
    selection: props.selection,
    onGroupClick: props.onGroupClick,
    spins: props.spins,
    turnOffsets: props.turnOffsets,
  };

  const columns = engagements.length > 0 ? engagements : null;

  return (
    <section className="combat-lane" aria-label="Combat">
      <div
        className="lane-grid"
        style={{ gridTemplateColumns: `104px repeat(${columns?.length ?? 3}, 104px) 1fr` }}
      >
        <span className="lane-seam" aria-hidden="true" />

        <span className="lane-label-far">Their<br />attack zone</span>
        <span className="lane-label-mid">Engagements</span>
        <span className="lane-label-near">Your<br />attack zone</span>

        {columns === null
          ? [0, 1, 2].map((i) => (
              <Fragment key={i}>
                <div className="lane-slot empty" style={cell(i, 1)}>
                  <span className="lane-slot-hint">no blocker</span>
                </div>
                <div className="lane-connector" style={cell(i, 2)} />
                <div className="lane-slot empty" style={cell(i, 3)}>
                  <span className="lane-slot-hint">open slot</span>
                </div>
              </Fragment>
            ))
          : columns.map(({ attacker, blockers }, i) => {
              const attackerIsNear = attacker.ownerId === nearPlayerId;
              const face = characterFaceInfo(attacker);
              const attack = face?.attack ?? 0;
              const marker =
                blockers.length === 0
                  ? { text: `${attack} to face`, className: "lane-marker unblocked" }
                  : { text: `${attack} on blocker`, className: "lane-marker" };

              // Only the slot across from an attacker that ISN'T yours is
              // ever a real drop target - you block the opponent's
              // attackers, never your own.
              const slotClickable = !!props.canAssignBlockers && !attackerIsNear;
              const targeting = slotClickable && !!props.selection.primary;
              const blockerSlot = (
                <div
                  className={[
                    "lane-slot",
                    blockers.length ? "filled" : "empty",
                    slotClickable ? "clickable" : "",
                    targeting ? "targeting" : "",
                  ].filter(Boolean).join(" ")}
                  style={cell(i, attackerIsNear ? 1 : 3)}
                  role={slotClickable ? "button" : undefined}
                  tabIndex={slotClickable ? 0 : undefined}
                  // Capture phase, not bubble - when the slot is already
                  // filled it contains the blocker's own EngagementDie
                  // button, whose own click (a plain toggle-select) would
                  // otherwise fire too; this intercepts first so clicking
                  // anywhere in the slot always means "assign/unassign
                  // here," never both.
                  onClickCapture={
                    slotClickable
                      ? (e) => {
                          e.stopPropagation();
                          props.onSlotClick?.(attacker.id);
                        }
                      : undefined
                  }
                >
                  {blockers.length === 0 ? (
                    <span className="lane-slot-hint">{slotClickable && targeting ? "assign here" : "unblocked"}</span>
                  ) : (
                    blockers.map((blocker) => (
                      <EngagementDie
                        key={blocker.id}
                        die={blocker}
                        size={40}
                        mine={blocker.ownerId === nearPlayerId}
                        {...dieProps}
                      />
                    ))
                  )}
                </div>
              );
              const attackerSlot = (
                <div className="lane-slot filled attacker" style={cell(i, attackerIsNear ? 3 : 1)}>
                  <EngagementDie die={attacker} size={ATTACKER_SIZE} mine={attackerIsNear} {...dieProps} />
                </div>
              );

              return (
                <Fragment key={attacker.id}>
                  {attackerIsNear ? blockerSlot : attackerSlot}
                  <div className="lane-connector" style={cell(i, 2)}>
                    <span className={marker.className}>{marker.text}</span>
                  </div>
                  {attackerIsNear ? attackerSlot : blockerSlot}
                </Fragment>
              );
            })}
      </div>
    </section>
  );
}
