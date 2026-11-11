// The mobile launch page (2026-11-10, user: "the home page leaves much to
// be desired. Can we re-imagine the launch page to be more engaging,
// eye-catching?"). It used to be a form: a paragraph of instructions, a row
// of text links, a "Play vs Computer" checkbox and two columns of Champion
// buttons. Now it's a lobby in two steps:
//
//   MobileHome  - key art (a fan of the card paintings, two dice to roll),
//                 who you are and your coins, a game to continue, then one
//                 tile per way to play and per place to go.
//   MobileSetup - the Champion pick for the way you chose, at ?play=<mode>
//                 so the phone's back button returns to the home.
//
// DiceKingdomMobilePage.tsx still owns the game; these two only decide
// what to start. The desktop page keeps the old shared pickers.
import { useEffect, useRef, useState, useSyncExternalStore, type CSSProperties, type ReactNode } from "react";
import { navigate, type Route } from "../router";
import { ScreenMenu } from "../ScreenMenu";
import { hasPainting, paintingSrc } from "./cardArt";
import { OPPONENT_PICKS, PLAYSTYLES, championLabel, usePowerText, type ChampionChoice } from "./ChampionPicker";
import { CUSTOM_TEAM_PICK, pendingCustomTeam, type CustomTeam } from "./customTeams";
import { CHAMPION_ICONS, ENERGY_ICONS, EnergyBadge, TardigradeIcon, type IconComponent } from "./icons";
import { ago, useResumeGames, type Resumed } from "./lobby";
import { players, type BossLadder, type Collection, type Me } from "./player";
import { playSound } from "./sound";
import type { CardDef } from "./types";
import "./mobileHome.css";

const HOME: Route = "/dice-kingdom/mobile";
const FROM = "?from=mobile";

// ---- Which step: the home, or one way to play's setup ----

export type PlayMode = "computer" | "friend" | "local";

function readPlayMode(): PlayMode | null {
  const play = new URLSearchParams(window.location.search).get("play");
  return play === "computer" || play === "friend" || play === "local" ? play : null;
}

function subscribeToHistory(callback: () => void) {
  window.addEventListener("popstate", callback);
  return () => window.removeEventListener("popstate", callback);
}

function announce() {
  // pushState/replaceState fire no popstate themselves (see router.ts).
  window.dispatchEvent(new PopStateEvent("popstate"));
}

/** The setup step in the URL (?play=computer), or null on the home. */
export function usePlayMode(): PlayMode | null {
  return useSyncExternalStore(subscribeToHistory, readPlayMode);
}

function openSetup(mode: PlayMode) {
  window.history.pushState({ dkSetup: true }, "", `${HOME}?play=${mode}`);
  announce();
}

/** Back to the home: the history entry under this one if we pushed it, else in place (a reload, or a link straight here). */
export function closeSetup() {
  if ((window.history.state as { dkSetup?: boolean } | null)?.dkSetup) {
    window.history.back();
    return;
  }
  window.history.replaceState(null, "", HOME);
  announce();
}

/** The match is starting: leave no ?play behind, so leaving the game lands on the home. */
export function clearPlayMode() {
  if (!readPlayMode()) return;
  window.history.replaceState(null, "", HOME);
  announce();
}

// ---- Small line icons (24 grid, currentColor) ----

function Line({ size = 22, children }: { size?: number; children: ReactNode }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {children}
    </svg>
  );
}
const DieIcon = ({ size }: { size?: number }) => (
  <Line size={size}>
    <rect x="3.5" y="3.5" width="17" height="17" rx="4.5" />
    {[[8.4, 8.4], [15.6, 8.4], [12, 12], [8.4, 15.6], [15.6, 15.6]].map(([x, y]) => (
      <circle key={`${x}-${y}`} cx={x} cy={y} r="1.25" fill="currentColor" stroke="none" />
    ))}
  </Line>
);
const BuilderIcon = ({ size }: { size?: number }) => (
  <Line size={size}>
    <rect x="3.5" y="3.5" width="7" height="7" rx="2" />
    <rect x="13.5" y="3.5" width="7" height="7" rx="2" />
    <rect x="3.5" y="13.5" width="7" height="7" rx="2" />
    <path d="M17 14v6M14 17h6" />
  </Line>
);
const BookIcon = ({ size }: { size?: number }) => (
  <Line size={size}>
    <path d="M12 6.8C10 5.3 7 4.8 3.5 5.2v13c3.5-.4 6.5.1 8.5 1.6 2-1.5 5-2 8.5-1.6v-13C17 4.8 14 5.3 12 6.8zM12 6.8v13" />
  </Line>
);
const BackIcon = () => <Line><path d="M14.5 5 7.5 12l7 7" /></Line>;
const CheckIcon = () => <Line size={14}><path d="M5 12.5 9.5 17 19 7.5" /></Line>;
const CloseIcon = () => <Line size={14}><path d="M6 6l12 12M18 6 6 18" /></Line>;
const PlayIcon = ({ size = 20 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true"><path d="M8 5.2v13.6L19.5 12z" fill="currentColor" /></svg>
);
const PawIcon = () => (
  <svg width={18} height={18} viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <ellipse cx="6" cy="10.5" rx="2" ry="2.6" />
    <ellipse cx="10" cy="6.5" rx="2" ry="2.7" />
    <ellipse cx="14.5" cy="6.5" rx="2" ry="2.7" />
    <ellipse cx="18.3" cy="10.5" rx="2" ry="2.6" />
    <path d="M12.2 11.5c-3 0-5.7 3.3-5.7 5.8 0 1.6 1.2 2.5 2.7 2.5 1.2 0 2-.6 3-.6s1.8.6 3 .6c1.5 0 2.7-.9 2.7-2.5 0-2.5-2.7-5.8-5.7-5.8z" />
  </svg>
);
const CoinIcon = ({ size = 18 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" aria-hidden="true">
    <circle cx="12" cy="12" r="10" fill="currentColor" />
    <circle cx="12" cy="12" r="6.6" fill="none" stroke="rgba(0,0,0,0.28)" strokeWidth="1.6" />
    <path d="M12 8.3l1.1 2.3 2.5.3-1.8 1.7.5 2.5-2.3-1.2-2.3 1.2.5-2.5-1.8-1.7 2.5-.3z" fill="rgba(0,0,0,0.3)" />
  </svg>
);

const tone = (energy: string): CSSProperties => ({ ["--tone" as string]: `var(--${energy.toLowerCase()})` });

function useReducedMotion(): boolean {
  const [reduced] = useState(() => window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false);
  return reduced;
}

// ---- Key art: a fan of the card paintings ----

// The showiest of the vintage paintings (cardArt.tsx). Not the family
// photos - those are a Super Rare's reward, not the front door.
const HERO_CARDS: { id: string; name: string }[] = [
  { id: "DPS056", name: "Caracal" },
  { id: "DOOM013", name: "Cassowary" },
  { id: "DK-WING-03", name: "Hummingbird" },
  { id: "CW056", name: "Peacock" },
  { id: "MSW020", name: "Puffin" },
  { id: "DPS031", name: "Woodpecker" },
  { id: "DK-EYE-07", name: "Elephant" },
  { id: "DK-CLAW-03", name: "Grizzly Bear" },
  { id: "DK-WING-01", name: "Osprey" },
  { id: "DK-WING-08", name: "Phoenix" },
  { id: "CW054", name: "Red Panda" },
  { id: "DK-EYE-01", name: "Barn Owl" },
  { id: "DPS086", name: "Lionfish" },
  { id: "DPS152", name: "Starfish" },
  { id: "DK-SHELL-02", name: "Musk Ox" },
  { id: "DPS148", name: "Bison" },
  { id: "WOL071", name: "Polar Bear" },
  { id: "GOTG021", name: "Chipmunk" },
  { id: "DK-EYE-05", name: "Magpie" },
  { id: "DK-WING-05", name: "Monarch Butterfly" },
  { id: "DOOM017", name: "Platypus" },
  { id: "AI031", name: "Raven" },
];
const FAN = [-2, -1, 0, 1, 2];
const FLIP_MS = 520;
const AUTO_FLIP_MS = 3400;

function dealHand(): number[] {
  const deck = HERO_CARDS.map((_, i) => i);
  for (let i = deck.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [deck[i], deck[j]] = [deck[j], deck[i]];
  }
  return deck.slice(0, FAN.length);
}

function HeroFan({ cardsById }: { cardsById: Map<string, CardDef> }) {
  const [hand, setHand] = useState(dealHand);
  const [flipping, setFlipping] = useState<number | null>(null);
  const reduced = useReducedMotion();
  const handRef = useRef(hand);
  const busyRef = useRef(false);
  const timers = useRef<number[]>([]);
  useEffect(() => {
    handRef.current = hand;
  }, [hand]);

  // Turn one card of the fan over to an animal that isn't showing. The
  // next painting loads first, so the turn never lands on a blank card.
  function turn(slot: number) {
    if (busyRef.current) return;
    const showing = handRef.current;
    const rest = HERO_CARDS.map((_, i) => i).filter((i) => !showing.includes(i));
    const next = rest[Math.floor(Math.random() * rest.length)];
    busyRef.current = true;
    const img = new Image();
    img.onload = () => {
      setFlipping(slot);
      timers.current.push(
        window.setTimeout(() => setHand((h) => h.map((card, i) => (i === slot ? next : card))), FLIP_MS / 2),
        window.setTimeout(() => {
          setFlipping(null);
          busyRef.current = false;
        }, FLIP_MS),
      );
    };
    img.onerror = () => {
      busyRef.current = false;
    };
    img.src = paintingSrc(HERO_CARDS[next].id)!;
  }

  useEffect(() => {
    const pending = timers.current;
    const auto = reduced
      ? null
      : window.setInterval(() => {
          if (!document.hidden) turn(Math.floor(Math.random() * FAN.length));
        }, AUTO_FLIP_MS);
    return () => {
      if (auto !== null) window.clearInterval(auto);
      pending.forEach((t) => window.clearTimeout(t));
      pending.length = 0;
      busyRef.current = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [reduced]);

  return (
    <div className="dkh-fan">
      {FAN.map((offset, slot) => {
        const card = HERO_CARDS[hand[slot]];
        const def = cardsById.get(card.id);
        const energy = def?.energyTypes[0];
        return (
          <button
            key={slot}
            type="button"
            className={`dkh-card${flipping === slot ? " is-flipping" : ""}`}
            style={{ ["--i" as string]: offset, zIndex: 3 - Math.abs(offset) }}
            aria-label={`${def?.name ?? card.name} - tap for another animal`}
            onClick={() => {
              playSound("cardFlip");
              turn(slot);
            }}
          >
            <span className="dkh-card-bob">
              <span className="dkh-card-face">
                <img src={paintingSrc(card.id)!} alt="" draggable={false} />
                <span className="dkh-card-name">{def?.name ?? card.name}</span>
                {energy && ENERGY_ICONS[energy] && (
                  <span className="dkh-card-energy"><EnergyBadge type={energy} size={20} /></span>
                )}
              </span>
            </span>
          </button>
        );
      })}
    </div>
  );
}

// ---- Key art: two dice to roll ----

const DIE_FACES: IconComponent[] = [
  ENERGY_ICONS.Claw, ENERGY_ICONS.Shell, ENERGY_ICONS.Wing, ENERGY_ICONS.Eye, ENERGY_ICONS.Wild, TardigradeIcon,
];
// The cube's turn that brings each face (front, back, right, left, top, bottom) to the front.
const FACE_TURN: [number, number][] = [[0, 0], [0, 180], [0, -90], [0, 90], [-90, 0], [90, 0]];

function HeroDie({ energy, side, startFace }: { energy: string; side: "left" | "right"; startFace: number }) {
  const [roll, setRoll] = useState({ face: startFace, turns: 0 });
  const reduced = useReducedMotion();

  function throwDie() {
    setRoll((r) => {
      const face = (r.face + 1 + Math.floor(Math.random() * 5)) % 6;
      return { face, turns: r.turns + 1 };
    });
  }

  // One roll of its own as the page settles, so it reads as a die and not a logo.
  useEffect(() => {
    if (reduced) return;
    const timer = window.setTimeout(throwDie, side === "left" ? 1100 : 1350);
    return () => window.clearTimeout(timer);
  }, [reduced, side]);

  const [x, y] = FACE_TURN[roll.face];
  return (
    <button
      type="button"
      className={`dkh-die is-${side}`}
      style={tone(energy)}
      aria-label="Roll the die"
      onClick={() => {
        playSound("roll");
        throwDie();
      }}
    >
      {/* Alternating names restart the hop without remounting the cube mid-turn. */}
      <span className={`dkh-die-hop${roll.turns === 0 ? "" : roll.turns % 2 ? " is-odd" : " is-even"}`}>
        <span className="dkh-die-tilt">
          <span className="dkh-die-cube" style={{ transform: `rotateX(${x + roll.turns * 360}deg) rotateY(${y + roll.turns * 720}deg)` }}>
            {DIE_FACES.map((Face, i) => (
              <span key={i} className={`dkh-die-face is-f${i}`}><Face size={24} /></span>
            ))}
          </span>
        </span>
      </span>
    </button>
  );
}

function Sky() {
  return (
    <div className="dkh-sky" aria-hidden="true">
      <span className="dkh-stars" />
      <span className="dkh-rays" />
    </div>
  );
}

// ---- The logo ----

/** A filled crown: on the logo's K, the boss tile, the ladder's rungs. */
function Crown({ className }: { className?: string }) {
  return (
    <svg className={`dkh-crown${className ? ` ${className}` : ""}`} viewBox="-2 -2 48 36" aria-hidden="true">
      <path className="dkh-crown-body" d="M6 30 3 9l10 8L22 3l9 14 10-8-3 21z" />
      <path className="dkh-crown-band" d="M9 24.5h26" />
      <circle cx="3" cy="9" r="2.6" />
      <circle cx="22" cy="3" r="2.6" />
      <circle cx="41" cy="9" r="2.6" />
    </svg>
  );
}

// Four letters, four energies: DICE is spelled on a die of each.
const LOGO_DICE: [string, string][] = [["D", "Claw"], ["I", "Shell"], ["C", "Wing"], ["E", "Eye"]];
const LOGO_WORD = [..."KINGDOM"];

// 2026-11-11, user: "I'd like to try something different/better for the
// words 'Dice Kingdom'" - it was one line of gradient type. Each KINGDOM
// letter is two layers (a dark outline with its drop, the gold face over
// it) on an arch; --i is its place from the middle.
function Wordmark({ small = false }: { small?: boolean }) {
  return (
    <h1 className={`dkh-logo${small ? " is-small" : ""}`}>
      <span className="dkh-visually-hidden">Dice Kingdom</span>
      <span className="dkh-logo-dice" aria-hidden="true">
        {LOGO_DICE.map(([letter, energy], n) => (
          <span key={letter} className="dkh-logo-die" style={{ ...tone(energy), ["--n" as string]: n }}>
            <b>{letter}</b>
          </span>
        ))}
      </span>
      <span className="dkh-logo-word" aria-hidden="true">
        {LOGO_WORD.map((letter, n) => (
          <span key={n} className="dkh-logo-ch" data-ch={letter} style={{ ["--i" as string]: n - 3 }}>
            {letter}
            {n === 0 && <Crown className="dkh-logo-crown" />}
          </span>
        ))}
      </span>
    </h1>
  );
}

/** The key-art band alone, for the screens between the home and a game (waiting for an opponent, an invite's pick). */
export function MobileBrand() {
  return (
    <header className="dkh-hero is-compact">
      <Sky />
      <Wordmark small />
    </header>
  );
}

// ---- The home ----

function Go({ to, search = FROM, className, style, children }: {
  to: Route;
  search?: string;
  className: string;
  style?: CSSProperties;
  children: ReactNode;
}) {
  return (
    <a
      href={to + search}
      className={className}
      style={style}
      onClick={(e) => {
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        e.preventDefault();
        navigate(to, search);
      }}
    >
      {children}
    </a>
  );
}

// ---- The tiles' art (2026-11-11, user: "a bit more pizzaz ... instead of
// just being a bunch of boxes with text in them") ----

// The card that stands for each boss on its tile - one from its team (the
// server's DiceKingdomBosses.cs). A boss not listed shows its first painted card.
const BOSS_FACES: Record<string, string> = {
  "tag-jubilee": "DPS027", // Tasmanian Devil
  "xmen-colossus": "DK-EYE-06", // Basilisk
  sidekicks: "THOR036", // Moose
};

/** A paper plane and the loop it flew. */
function PlaneArt() {
  return (
    <svg className="dkh-art dkh-art-plane" viewBox="0 0 120 92" aria-hidden="true">
      <path className="dkh-art-trail" d="M2 68c12 4 28 1 34-12 5-11-6-20-13-13-7 8 7 19 21 15 8-2 14-4 20-6" />
      <g className="dkh-art-float">
        <path d="M112 8 44 34l26 16z" fill="#fff8ec" />
        <path d="M112 8 70 50l14 28z" fill="#cfe4f8" />
        <path d="M70 50l2 20 9-9z" fill="#9cc3ea" />
      </g>
    </svg>
  );
}

/** Player 1's and Player 2's chips. */
function ChipsArt() {
  return (
    <span className="dkh-art dkh-art-chips" aria-hidden="true">
      <i className="is-two">2</i>
      <i className="is-one">1</i>
    </span>
  );
}

/** A foil pack with two cards on their way out of it. */
function PackArt() {
  return (
    <span className="dkh-art dkh-art-pack" aria-hidden="true">
      <img className="is-a" src={paintingSrc("DK-WING-08")!} alt="" loading="lazy" />
      <img className="is-b" src={paintingSrc("CW054")!} alt="" loading="lazy" />
      <span className="dkh-pack"><PawIcon /></span>
      <i className="dkh-spark is-a" />
      <i className="dkh-spark is-b" />
      <i className="dkh-spark is-c" />
    </span>
  );
}

function teamSummary(team: CustomTeam): string {
  const dice = team.cards.reduce((n, c) => n + c.diceCount, 0);
  const tardigrades = team.tardigrades.reduce((n, d) => n + d.diceCount, 0);
  return `${team.championId ? championLabel(team.championId) : "No Champion"} · ${dice} card dice · ${tardigrades} Tardigrades`;
}

export function MobileHome({ cardsById, error, onResume }: {
  cardsById: Map<string, CardDef>;
  error: string | null;
  onResume: (r: Resumed) => void;
}) {
  // undefined while loading (so a named player doesn't flash "Guest"); null if it failed.
  const [me, setMe] = useState<Me | null | undefined>(undefined);
  const [collection, setCollection] = useState<Collection | null>(null);
  const [ladder, setLadder] = useState<BossLadder | null>(null);
  const [showAll, setShowAll] = useState(false);
  const { games, note, resume, forget } = useResumeGames(onResume);
  const team = pendingCustomTeam();

  useEffect(() => {
    let live = true;
    // The player first: whichever request arrives first is the one told
    // about today's coins, and the top bar is where they're shown.
    players
      .me()
      .then(
        (m) => live && setMe(m),
        () => live && setMe(null),
      )
      .then(() => {
        if (!live) return;
        players.collection().then((c) => live && setCollection(c), () => {});
        players.bosses().then((l) => live && setLadder(l), () => {});
      });
    return () => {
      live = false;
    };
  }, []);

  const coins = collection?.coins ?? me?.coins;
  const daily = me?.dailyCoins ?? 0;
  const wins = me?.games.filter((g) => g.result === "win").length ?? 0;
  const newPlayer = !!me && me.games.length === 0 && games.length === 0;
  const packReady = !!collection && collection.coins >= collection.packPrice;
  const owned = collection ? Object.keys(collection.dice).length : 0;
  const beaten = ladder?.bosses.filter((b) => b.status === "beaten").length ?? 0;
  const nextBoss = ladder?.bosses.find((b) => b.status === "open");
  // The boss to show: the one to fight next, else the last one beaten.
  const faceBoss = nextBoss ?? ladder?.bosses.findLast((b) => b.status === "beaten");
  const bossCard = faceBoss ? (BOSS_FACES[faceBoss.id] ?? Object.keys(faceBoss.cards).find(hasPainting)) : undefined;
  const shown = showAll ? games : games.slice(0, 2);

  return (
    <div className="dkh">
      <header className="dkh-hero">
        <Sky />
        <div className="dkh-top">
          <Go to="/dice-kingdom/account" className="dkh-player">
            <span className="dkh-avatar">{me?.name ? me.name.trim().charAt(0).toUpperCase() : <PawIcon />}</span>
            <span className="dkh-player-text">
              <b>{me === undefined ? " " : (me?.name ?? "Guest")}</b>
              <small>
                {me === undefined ? " " : me?.name ? `${wins} ${wins === 1 ? "win" : "wins"}` : "Make an account"}
              </small>
            </span>
          </Go>
          <span className="dkh-top-gap" />
          {coins !== undefined && (
            <Go to="/dice-kingdom/packs" className="dkh-coins">
              <CoinIcon />
              <b>{coins}</b>
              <span className="dkh-visually-hidden"> coins</span>
              {daily > 0 && <span className="dkh-coins-plus">+{daily}</span>}
            </Go>
          )}
          <ScreenMenu screen="start" startRoute={HOME} />
        </div>
        {daily > 0 && <p className="dkh-toast" role="status"><CoinIcon size={16} /> Daily bonus: +{daily} coins</p>}
        <div className="dkh-stage">
          <HeroFan cardsById={cardsById} />
          <HeroDie energy="Claw" side="left" startFace={0} />
          <HeroDie energy="Eye" side="right" startFace={3} />
        </div>
        <Wordmark />
        <p className="dkh-tagline">Roll the dice. Rally the wild.</p>
      </header>

      <main className="dkh-body">
        {error && <p className="dkm-error dkh-error">{error}</p>}
        {note && <p className="dkh-note">{note}</p>}

        {shown.map((g) => (
          <div key={g.gameId} className="dkh-continue">
            <button type="button" className="dkh-continue-main" onClick={() => resume(g)}>
              <span className="dkh-continue-dot" aria-hidden="true" />
              <span className="dkh-continue-text">
                <small>Continue</small>
                <b>{g.label ?? `Game ${g.gameId}`}</b>
                <span>
                  {g.vsComputer ? "vs computer · " : g.seats.length > 1 ? "both seats · " : ""}
                  {ago(g.savedAt)}
                </span>
              </span>
              <span className="dkh-continue-go"><PlayIcon size={18} /></span>
            </button>
            <button type="button" className="dkh-continue-forget" aria-label="Forget this game" title="Forget this game"
              onClick={() => forget(g)}>
              <CloseIcon />
            </button>
          </div>
        ))}
        {games.length > shown.length && (
          <button type="button" className="dkh-more" onClick={() => setShowAll(true)}>
            {games.length - shown.length} more unfinished {games.length - shown.length === 1 ? "game" : "games"}
          </button>
        )}

        {team && (
          <p className="dkh-team">
            <b>Custom team ready</b>
            <span>{teamSummary(team)}. Pick a way to play it.</span>
          </p>
        )}

        <h2 className="dkh-label">Play</h2>
        <button type="button" className="dkh-quick" onClick={() => openSetup("computer")}>
          <span className="dkh-quick-mark" aria-hidden="true"><ENERGY_ICONS.Claw size={120} /></span>
          <span className="dkh-quick-text">
            <b>Quick Match</b>
            <span>You against the computer</span>
          </span>
          <span className="dkh-quick-go"><PlayIcon size={24} /></span>
        </button>

        <Go to="/dice-kingdom/bosses" className="dkh-tile is-wide is-boss">
          <span className="dkh-art dkh-art-boss" aria-hidden="true">
            <span className="dkh-boss-card">
              {bossCard && <img src={paintingSrc(bossCard)!} alt="" loading="lazy" />}
            </span>
            <Crown />
          </span>
          <b>Boss Ladder</b>
          <span>
            {!ladder ? "Beat the bosses, win their dice"
              : nextBoss ? `Next: ${nextBoss.name}`
              : beaten === ladder.bosses.length ? "Every boss beaten!" : "Beat the bosses, win their dice"}
          </span>
          {ladder && ladder.bosses.length > 0 && (
            <span className="dkh-rungs" role="img" aria-label={`${beaten} of ${ladder.bosses.length} bosses beaten`}>
              {ladder.bosses.map((b) => <Crown key={b.id} className={`is-${b.status}`} />)}
            </span>
          )}
        </Go>

        <div className="dkh-grid">
          <button type="button" className="dkh-tile is-friend" onClick={() => openSetup("friend")}>
            <PlaneArt />
            <b>Play a Friend</b>
            <span>Online - send them an invite link</span>
          </button>
          <button type="button" className="dkh-tile is-local" onClick={() => openSetup("local")}>
            <ChipsArt />
            <b>Pass &amp; Play</b>
            <span>Two players, one device</span>
          </button>
        </div>

        <Go to="/dice-kingdom/packs" className={`dkh-tile is-wide is-gold${packReady ? " is-ready" : ""}`}>
          <PackArt />
          <b>Card Packs</b>
          <span>
            {!collection ? "Spend coins on new dice"
              : packReady ? "A pack is ready to open!"
              : `${collection.packPrice - collection.coins} more coins to the next pack`}
          </span>
          {collection && !packReady && (
            <span className="dkh-meter" aria-hidden="true">
              <i style={{ width: `${Math.round((100 * collection.coins) / collection.packPrice)}%` }} />
            </span>
          )}
          {collection && <span className="dkh-tile-foot">{owned} {owned === 1 ? "card" : "cards"} collected</span>}
        </Go>

        <h2 className="dkh-label">More</h2>
        <div className="dkh-grid">
          <Go to="/teambuilder" search="" className="dkh-tile is-slim">
            <span className="dkh-tile-icon"><BuilderIcon size={20} /></span>
            <b>Team Builder</b>
          </Go>
          <Go to="/dice-kingdom/how-to-play" className="dkh-tile is-slim">
            <span className="dkh-tile-icon"><BookIcon size={20} /></span>
            <b>How to Play</b>
            {newPlayer && <em className="dkh-tag">Start here</em>}
          </Go>
        </div>
      </main>
    </div>
  );
}

// ---- The setup step ----

/** The computer's "Surprise me": a Champion picked when the match starts. */
const RANDOM_PICK = "__random__";
const LAST_CHAMPION_KEY = "dicekingdom:lastChampion";

function lastChampion(champions: ChampionChoice[]): string | null {
  try {
    const id = localStorage.getItem(LAST_CHAMPION_KEY);
    return champions.some((c) => c.id === id) ? id : null;
  } catch {
    return null;
  }
}

const MODES: Record<PlayMode, { title: string; dek: string; start: string }> = {
  computer: { title: "Quick Match", dek: "You against the computer", start: "Start Match" },
  friend: { title: "Play a Friend", dek: "Online, each on your own device", start: "Create Invite Link" },
  local: { title: "Pass & Play", dek: "Two players, one device", start: "Start Match" },
};

function ChampionCards({ champions, value, onPick, compact = false, lead }: {
  champions: ChampionChoice[];
  value: string | null;
  onPick: (id: string) => void;
  /** Small tiles, no power text - the computer's Champion. */
  compact?: boolean;
  lead?: ReactNode;
}) {
  const powers = usePowerText();
  const picked = champions.find((c) => c.id === value);
  return (
    <>
      <div className={`dks-champs${compact ? " is-compact" : ""}`}>
        {lead}
        {champions.map((c) => {
          const Icon = CHAMPION_ICONS[c.id];
          const on = value === c.id;
          return (
            <button key={c.id} type="button" className={`dks-champ${on ? " is-on" : ""}`} style={tone(c.energy)}
              aria-pressed={on} onClick={() => onPick(c.id)}>
              <span className="dks-champ-art"><Icon size={compact ? 26 : 54} /></span>
              <span className="dks-champ-name">{championLabel(c.id)}</span>
              {!compact && (
                <span className="dks-champ-meta">
                  <EnergyBadge type={c.energy} size={16} />
                  {PLAYSTYLES[c.id]?.split(":")[0] ?? c.energy}
                </span>
              )}
              {on && <span className="dks-check"><CheckIcon /></span>}
            </button>
          );
        })}
      </div>
      {picked && !compact && (
        <div className="dks-power" style={tone(picked.energy)}>
          <b>{championLabel(picked.id)}'s power</b>
          <p>{powers[picked.id] ?? " "}</p>
          {PLAYSTYLES[picked.id] && <p className="dks-style">{PLAYSTYLES[picked.id]}</p>}
        </div>
      )}
    </>
  );
}

export function MobileSetup({ mode, champions, busy, error, onBack, onStart }: {
  mode: PlayMode;
  champions: ChampionChoice[];
  busy: boolean;
  error: string | null;
  /** Leaving for the home by the page's own back arrow or menu (the URL is this component's to change). */
  onBack: () => void;
  /** `mine` may be CUSTOM_TEAM_PICK; `theirs` may be OPPONENT_PICKS. */
  onStart: (mine: string, theirs: string, vsComputer: boolean) => void;
}) {
  const team = pendingCustomTeam();
  const [mine, setMine] = useState<string | null>(() => (team ? CUSTOM_TEAM_PICK : lastChampion(champions)));
  const [theirs, setTheirs] = useState<string | null>(() => (mode === "computer" ? RANDOM_PICK : null));
  const { title, dek, start } = MODES[mode];
  const ready = !!mine && (mode === "friend" || !!theirs);
  const back = () => {
    onBack();
    closeSetup();
  };

  function begin() {
    if (!mine || busy) return;
    if (mine !== CUSTOM_TEAM_PICK) {
      try {
        localStorage.setItem(LAST_CHAMPION_KEY, mine);
      } catch {
        // Storage blocked - it just won't be remembered.
      }
    }
    if (mode === "friend") return onStart(mine, OPPONENT_PICKS, false);
    if (!theirs) return;
    // A surprise is anyone but your own Champion - a mirror match can still be picked on purpose.
    const others = champions.filter((c) => c.id !== mine);
    const rival = theirs === RANDOM_PICK ? others[Math.floor(Math.random() * others.length)].id : theirs;
    onStart(mine, rival, mode === "computer");
  }

  return (
    <div className="dks">
      <header className="dks-head">
        <button type="button" className="dks-back" aria-label="Back" onClick={back}><BackIcon /></button>
        <div className="dks-head-text">
          <h1>{title}</h1>
          <p>{dek}</p>
        </div>
        <ScreenMenu screen="start" startRoute={HOME} onStartGame={back} />
      </header>

      <main className="dks-body">
        {error && <p className="dkm-error dkh-error">{error}</p>}

        <h2 className="dkh-label">{mode === "local" ? "Player 1" : "Your Champion"}</h2>
        {team && (
          <button type="button" className={`dks-team${mine === CUSTOM_TEAM_PICK ? " is-on" : ""}`}
            aria-pressed={mine === CUSTOM_TEAM_PICK} onClick={() => setMine(CUSTOM_TEAM_PICK)}>
            <span className="dks-team-icon"><BuilderIcon /></span>
            <span className="dks-team-text">
              <b>Your custom team</b>
              <span>{teamSummary(team)}</span>
            </span>
            {mine === CUSTOM_TEAM_PICK && <span className="dks-check"><CheckIcon /></span>}
          </button>
        )}
        {team && <p className="dks-or">or play a Champion's own team</p>}
        <ChampionCards champions={champions} value={mine} onPick={setMine} />

        {mode === "computer" && (
          <>
            <h2 className="dkh-label">Opponent</h2>
            <ChampionCards
              compact
              champions={champions}
              value={theirs}
              onPick={setTheirs}
              lead={
                <button type="button" className={`dks-champ is-lead${theirs === RANDOM_PICK ? " is-on" : ""}`}
                  style={tone("Wild")} aria-pressed={theirs === RANDOM_PICK} onClick={() => setTheirs(RANDOM_PICK)}>
                  <span className="dks-champ-art"><DieIcon size={24} /></span>
                  <span className="dks-champ-name">Surprise me</span>
                  {theirs === RANDOM_PICK && <span className="dks-check"><CheckIcon /></span>}
                </button>
              }
            />
            <p className="dks-hint">
              The computer plays by simple rules - its strongest attacker attacks, it buys the best die it can afford. A
              sparring partner, not a mastermind.
            </p>
          </>
        )}

        {mode === "local" && (
          <>
            <h2 className="dkh-label">Player 2</h2>
            <ChampionCards champions={champions} value={theirs} onPick={setTheirs} />
          </>
        )}

        {mode === "friend" && (
          <ol className="dks-steps">
            <li><span><b>Pick your Champion</b> above.</span></li>
            <li><span><b>Send the invite link</b> - it's made, and copied, when you tap the button.</span></li>
            <li><span><b>Your friend picks theirs</b> and the match starts.</span></li>
          </ol>
        )}
      </main>

      <div className="dks-cta">
        <button type="button" className="dks-start" disabled={!ready || busy} onClick={begin}>
          {!mine ? "Pick your Champion" : !ready ? "Pick Player 2's Champion" : start}
        </button>
      </div>
    </div>
  );
}
