// The pictures on the How to Play page (2026-10-17). Dice are real
// DieCubes with real printed faces (dieFaces.ts), so they look like the
// board and change when it does. The lane fight and the stacking examples
// are fixed numbers worked through CombatEngine's rules by hand - if lane
// combat changes, recheck them.
import { DieCube } from "./DieCube";
import { cardDieFaces, tardigradeFacesOf, type CubeFace } from "./dieFaces";
import { CHARACTER_ICONS, PhaseIcon, type PhaseKey } from "./icons";
import type { CardDef } from "./types";

type Energy = { type: string; amount: number };

/** One static die showing `face`, with its energy pips where the board draws them. */
function Face({ face, size, mine = true, energy, damage }: {
  face: CubeFace; size: number; mine?: boolean; energy?: Energy; damage?: number;
}) {
  const pips = energy ?? (face.kind === "energy" ? { type: face.icon, amount: face.amount } : undefined);
  return <DieCube faces={[face]} index={0} size={size} mine={mine} energyCorner={pips} damage={damage} />;
}

/** The example creature: a real card, preferring one whose face has a cost and unequal stats. */
function exampleCreature(cards: CardDef[]) {
  const candidates = cards.filter((c) => !c.isAction && CHARACTER_ICONS[c.id] && c.levels.length === 3);
  for (const card of candidates) {
    const faces = cardDieFaces(card.id, card);
    const face = faces?.find((f) => f.kind === "character" && f.fieldingCost > 0 && f.attack !== f.defense);
    if (faces && face?.kind === "character") return { card, faces, face };
  }
  return null;
}

export function DieAnatomy({ cards }: { cards: CardDef[] | null }) {
  const example = cards ? exampleCreature(cards) : null;
  const action = cards?.find((c) => c.isAction && CHARACTER_ICONS[c.id]);
  const actionFace = action ? cardDieFaces(action.id, action)?.[0] : undefined;
  if (!example) return null;
  const { card, faces, face } = example;
  if (face.kind !== "character") return null;
  const energyFace = faces.find((f) => f.kind === "energy" && f.amount === 2);
  return (
    <figure className="dk-howto-figure">
      <div className="dk-howto-anatomy">
        <div className="dk-howto-anatomy-die">
          <span className="dk-howto-callout tl">cost to<br />field</span>
          <span className="dk-howto-callout tr">ATK</span>
          <span className="dk-howto-callout br">DEF</span>
          <Face face={face} size={80} />
        </div>
        <div className="dk-howto-anatomy-others">
        {energyFace && (
          <div className="dk-howto-anatomy-side">
            <Face face={energyFace} size={56} />
            <span>energy face</span>
          </div>
        )}
        {actionFace && (
          <div className="dk-howto-anatomy-side">
            <Face face={actionFace} size={56} />
            <span>action face</span>
          </div>
        )}
        </div>
      </div>
      <figcaption>
        {card.name}, level {face.level}: {face.fieldingCost} to field, {face.attack} ATK, {face.defense} DEF. Next to
        it, one of its energy faces (2 energy, bottom left){actionFace && <>, and an action die's face</>}.
      </figcaption>
      <div className="dk-howto-faces-row">
        <span className="dk-howto-faces-label">{card.name} - all six faces</span>
        <div className="dk-howto-faces">
          {faces.map((f, i) => (
            <Face key={i} face={f} size={40} />
          ))}
        </div>
      </div>
    </figure>
  );
}

export function TardigradeFaces({ type }: { type: string }) {
  const faces = tardigradeFacesOf(type);
  // The level 2 faces also carry a Wild (DiceKingdomConfig.TardigradeDie).
  const wild = (f: CubeFace) => (f.kind === "character" && f.level === 2 ? { type: "Wild", amount: 1 } : undefined);
  // Printed order is L2, 2, 2, 1, L3, L2; shown energy first like a card's die.
  const order = [1, 2, 3, 0, 5, 4];
  return (
    <div className="dk-howto-faces-row">
      <span className="dk-howto-faces-label">A Tardigrade - all six faces</span>
      <div className="dk-howto-faces">
        {order.map((i) => (
          <Face key={i} face={faces[i]} size={40} energy={wild(faces[i])} />
        ))}
      </div>
    </div>
  );
}

const PHASES: [PhaseKey, string][] = [
  ["clear", "Clear & Draw"],
  ["roll", "Roll & Reroll"],
  ["main", "Main"],
  ["attack", "Attack"],
  ["cleanup", "Clean Up"],
];

export function TurnStrip() {
  return (
    <ol className="dk-howto-turnstrip" aria-label="The steps of a turn">
      {PHASES.map(([key, label], i) => (
        <li key={key}>
          {i > 0 && <span className="dk-howto-turnstrip-arrow" aria-hidden="true">→</span>}
          <span className="dk-howto-turnstrip-step">
            <PhaseIcon phase={key} size={16} />
            {label}
          </span>
        </li>
      ))}
    </ol>
  );
}

const STACKS: { counts: number[]; ok: boolean; note: string }[] = [
  { counts: [2, 0, 0, 0], ok: false, note: "No other lane has 1" },
  { counts: [2, 1, 0, 0], ok: true, note: "Lane 2 has 1" },
  { counts: [3, 1, 0, 0], ok: false, note: "No other lane has 2" },
  { counts: [3, 2, 1, 0], ok: true, note: "Lane 2 has 2" },
];

export function StackingExamples() {
  return (
    <div className="dk-howto-stacks">
      {STACKS.map(({ counts, ok, note }) => (
        <figure key={counts.join()} className={`dk-howto-stack ${ok ? "ok" : "no"}`}>
          <div className="dk-howto-stack-lanes" aria-hidden="true">
            {counts.map((n, lane) => (
              <span key={lane} className="dk-howto-stack-lane">
                {Array.from({ length: n }, (_, i) => (
                  <span key={i} className="dk-howto-stack-die" />
                ))}
              </span>
            ))}
          </div>
          <figcaption>
            <b>{ok ? "✓ Allowed" : "✗ Not allowed"}</b>
            <span>{counts.join(" · ")} - {note}</span>
          </figcaption>
        </figure>
      ))}
    </div>
  );
}

// Lane 1: 3/2 (declared first) and 2/2 against a 2/3 blocker. Lane 2: a
// lone 1/1, unblocked. Worked through: lane 1's 5 ATK knocks out the 3 DEF
// blocker with 2 left over (two attackers = Overcrush); the blocker's 2
// ATK lands on the first attacker (DEF 2) and knocks it out; lane 2 hits
// for 1. Opponent takes 3.
const attacker = (cost: number, attack: number, defense: number): CubeFace =>
  ({ kind: "character", level: 1, fieldingCost: cost, attack, defense });
const FIGHT = {
  blocker: attacker(2, 2, 3),
  first: attacker(2, 3, 2),
  second: attacker(1, 2, 2),
  lone: attacker(0, 1, 1),
};

function FightDie({ face, mine, damage, ko }: { face: CubeFace; mine: boolean; damage?: number; ko?: boolean }) {
  return (
    <span className={`dk-howto-fight-die${ko ? " ko" : ""}`}>
      <Face face={face} size={46} mine={mine} damage={damage} />
      {ko && <span className="dk-howto-ko">KO</span>}
    </span>
  );
}

export function LaneFightExample() {
  return (
    <figure className="dk-howto-figure">
      <div className="dk-howto-fight" role="img"
        aria-label="Lane 1: attackers 3/2 and 2/2 against a 2/3 blocker. Lane 2: a 1/1 attacker, unblocked. Lanes 3 and 4 empty.">
        <span className="dk-howto-fight-side">Blockers</span>
        <span className="dk-howto-fight-side">Attackers</span>
        {[1, 2, 3, 4].map((lane) => (
          <div key={lane} className="dk-howto-fight-lane">
            <span className="dk-howto-fight-num">Lane {lane}</span>
            <div className="dk-howto-fight-row">
              {lane === 1 && <FightDie face={FIGHT.blocker} mine={false} damage={5} ko />}
            </div>
            <div className="dk-howto-fight-row">
              {lane === 1 && (
                <>
                  <FightDie face={FIGHT.first} mine damage={2} ko />
                  <FightDie face={FIGHT.second} mine />
                </>
              )}
              {lane === 2 && <FightDie face={FIGHT.lone} mine />}
            </div>
            {lane === 2 && <span className="dk-howto-fight-hit">−1 life</span>}
            {lane === 1 && <span className="dk-howto-fight-hit">−2 life</span>}
          </div>
        ))}
      </div>
      <figcaption>
        <b>Lane 1:</b> the attackers' 5 ATK knocks out the 3 DEF blocker, and with two attackers the lane has
        Overcrush, so the 2 left over hit your opponent. The blocker's 2 ATK goes to the first attacker you chose, which
        has 2 DEF - knocked out. The other attacker survives and goes back to your Field. <b>Lane 2:</b> unblocked, so
        it hits for 1. Your opponent loses 3 life in all.
      </figcaption>
    </figure>
  );
}

// Box positions on a 380×310 canvas; edges are drawn between them.
const BOX = { w: 100, h: 36 };
const ZONES = {
  bag: { x: 8, y: 14, label: "Bag" },
  reserve: { x: 200, y: 14, label: "Reserve Pool" },
  prep: { x: 272, y: 128, label: "Prep Area" },
  field: { x: 200, y: 250, label: "Field" },
  used: { x: 8, y: 250, label: "Used Pile" },
};
const EDGES: { d: [number, number, number, number]; label: string[]; at: [number, number]; anchor?: "start" | "middle" | "end" }[] = [
  { d: [108, 32, 198, 32], label: ["draw, roll"], at: [153, 24], anchor: "middle" },
  { d: [240, 50, 240, 248], label: ["field"], at: [247, 212] },
  { d: [290, 250, 314, 166], label: ["KO'd"], at: [308, 216] },
  { d: [322, 128, 294, 52], label: ["rolled", "next turn"], at: [314, 86] },
  { d: [205, 50, 82, 248], label: ["spent or", "not fielded"], at: [160, 138] },
  { d: [198, 268, 110, 268], label: ["unblocked", "attackers"], at: [153, 236], anchor: "middle" },
  { d: [44, 248, 44, 52], label: ["refills", "the bag"], at: [50, 146] },
];

export function ZoneMap() {
  return (
    <figure className="dk-howto-figure">
      <svg className="dk-howto-zonemap" viewBox="0 0 380 300" role="img"
        aria-label="Dice go from the Bag to the Reserve Pool, then to the Field or the Used Pile. Knocked-out creatures go to the Prep Area and are rolled next turn. The Used Pile refills the Bag.">
        <defs>
          <marker id="dk-howto-arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
            <path d="M0 0L10 5L0 10z" fill="var(--text-dim)" />
          </marker>
        </defs>
        {EDGES.map(({ d: [x1, y1, x2, y2], label, at: [x, y], anchor = "start" }) => (
          <g key={label.join()}>
            <line x1={x1} y1={y1} x2={x2} y2={y2} stroke="var(--text-dim)" strokeWidth="1.6" markerEnd="url(#dk-howto-arrow)" />
            <text x={x} y={y} textAnchor={anchor} className="dk-howto-zonemap-edge">
              {label.map((line, i) => (
                <tspan key={line} x={x} dy={i === 0 ? 0 : 13}>{line}</tspan>
              ))}
            </text>
          </g>
        ))}
        {Object.entries(ZONES).map(([key, z]) => (
          <g key={key}>
            <rect x={z.x} y={z.y} width={BOX.w} height={BOX.h} rx="8" className="dk-howto-zonemap-box" />
            <text x={z.x + BOX.w / 2} y={z.y + BOX.h / 2 + 5} textAnchor="middle" className="dk-howto-zonemap-name">{z.label}</text>
          </g>
        ))}
      </svg>
      <figcaption>
        Spent energy and unblocked attackers stop in Out of Play until the end of your turn, then go to the Used Pile.
      </figcaption>
    </figure>
  );
}
