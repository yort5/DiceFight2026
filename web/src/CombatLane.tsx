import { Fragment } from "react";
import { DieCube, type CubeSpin } from "./DieCube";
import { facesFor } from "./dieFaces";
import { characterFaceInfo, dieLabel } from "./dieHelpers";
import type { Selection } from "./PlayerBoard";
import type { BlockAssignment, CardDef, Die } from "./types";

// Where the two mats meet. Each attack is a column: the attacker on its
// owner's side of the divider, the dice blocking it on the other, and a
// marker between them saying what the damage does.
//
// Rule 2.7.2.2 - one attacker per line, but any number of dice may gang
// up to block it, so the blocker slot takes a variable count and the
// dice step down in size to fit rather than the slot growing.
//
// The divider is a real row of the grid rather than an absolutely
// positioned overlay at a fixed offset (which is how the design
// prototype does it, and which its own notes call out as the fragile
// choice): the markers sit in it, so nothing has to be recomputed if a
// slot's height ever changes.

const ATTACKER_SIZE = 50;

// Every cell is placed explicitly rather than auto-flowed: the seam spans
// the whole of row 2, and auto placement would refuse to put the markers
// on top of it. Column 1 is the label column, so engagement i is i + 2.
function cell(index: number, row: number) {
  return { gridColumn: index + 2, gridRow: row };
}

function blockerSize(count: number): number {
  if (count <= 1) return 46;
  return count === 2 ? 38 : 30;
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
  const face = characterFaceInfo(die, cardsById);
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
        damage={die.damage}
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
      {/* One grid for the whole lane rather than a grid per column: the
          three rows then line up across every engagement, which is what
          lets the divider seam be a single element spanning all of them
          instead of a stripe repeated under each marker. */}
      <div
        className="lane-grid"
        // The trailing 1fr is what lets the seam span the full lane
        // rather than stopping after the last attack.
        style={{ gridTemplateColumns: `104px repeat(${columns?.length ?? 3}, 104px) 1fr` }}
      >
        <span className="lane-seam" aria-hidden="true" />

        <span className="lane-label-far">Their<br />attack zone</span>
        <span className="lane-label-mid">Engagements</span>
        <span className="lane-label-near">Your<br />attack zone</span>

        {columns === null
          ? // No attack declared - the lane still shows its shape, so it
            // reads as a place attacks will appear rather than as a gap.
            [0, 1, 2].map((i) => (
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
              const face = characterFaceInfo(attacker, cardsById);
              const attack = face?.attack ?? 0;
              const size = blockerSize(blockers.length);
              const marker =
                blockers.length === 0
                  ? { text: `${attack} to face`, className: "lane-marker unblocked" }
                  : blockers.length === 1
                    ? { text: `${attack} on blocker`, className: "lane-marker" }
                    : // The split is the attacking player's choice (rule
                      // 2.7.4.3.4) - this shows the even default the damage
                      // panel starts from, not a decision made here.
                      { text: `split ${splitLabel(attack, blockers.length)}`, className: "lane-marker" };

              const blockerSlot = (
                <div
                  className={`lane-slot${blockers.length ? " filled" : " empty"}`}
                  style={cell(i, attackerIsNear ? 1 : 3)}
                >
                  {blockers.length === 0 ? (
                    <span className="lane-slot-hint">unblocked</span>
                  ) : (
                    blockers.map((blocker) => (
                      <EngagementDie
                        key={blocker.id}
                        die={blocker}
                        size={size}
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

// The even split the damage step defaults to: earlier blockers take the
// extra point when it does not divide.
function splitLabel(attack: number, blockers: number): string {
  const base = Math.floor(attack / blockers);
  const extra = attack % blockers;
  return Array.from({ length: blockers }, (_, i) => base + (i < extra ? 1 : 0)).join(" / ");
}
