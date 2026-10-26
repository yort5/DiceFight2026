// Sound effects (2026-10-26, user: "I typically play muted, but it's good to
// have the option - as long as it can be readily muted"). One switch in the
// ☰ menu (ScreenMenu), remembered per device like the theme. Files are
// Kenney's CC0 packs, public/dice-kingdom/sounds (CREDITS.txt there).
//
// Web Audio, not <audio> tags: several dice land at once, and a buffer
// source can overlap itself with no lag. Nothing loads until the first
// sound is wanted, and browsers won't start audio before a tap anyway.
import { useEffect, useRef, useSyncExternalStore } from "react";
import type { GameState } from "./types";

export type SoundName =
  | "roll" | "reroll" | "buy" | "spend" | "field" | "attack" | "block" | "ko" | "hit"
  | "yourTurn" | "packOpen" | "cardFlip" | "rare" | "superRare" | "win" | "lose";

// Variants are picked at random so repeats don't sound mechanical.
const SOUNDS: Record<SoundName, { files: string[]; volume: number }> = {
  roll: { files: ["roll-1", "roll-2", "roll-3"], volume: 0.8 },
  reroll: { files: ["reroll-1", "reroll-2", "reroll-3"], volume: 0.7 },
  buy: { files: ["buy-1", "buy-2", "buy-3"], volume: 0.6 },
  spend: { files: ["spend-1", "spend-2"], volume: 0.35 },
  field: { files: ["field-1", "field-2", "field-3"], volume: 0.6 },
  attack: { files: ["attack-1", "attack-2"], volume: 0.6 },
  block: { files: ["block-1", "block-2"], volume: 0.7 },
  ko: { files: ["ko-1", "ko-2", "ko-3"], volume: 0.8 },
  hit: { files: ["hit-1", "hit-2"], volume: 0.7 },
  yourTurn: { files: ["your-turn"], volume: 0.45 },
  packOpen: { files: ["pack-open-1", "pack-open-2"], volume: 0.8 },
  cardFlip: { files: ["card-flip-1", "card-flip-2", "card-flip-3"], volume: 0.5 },
  rare: { files: ["rare"], volume: 0.6 },
  superRare: { files: ["super-rare"], volume: 0.7 },
  win: { files: ["win"], volume: 0.7 },
  lose: { files: ["lose"], volume: 0.6 },
};

const KEY = "dk-sound";
const listeners = new Set<() => void>();

function readMuted(): boolean {
  try {
    return localStorage.getItem(KEY) === "off";
  } catch {
    return false;
  }
}
let muted = readMuted();

export function setSoundMuted(next: boolean) {
  muted = next;
  try {
    localStorage.setItem(KEY, next ? "off" : "on");
  } catch {
    // Still applies for this visit.
  }
  listeners.forEach((l) => l());
  if (!next) playSound("cardFlip"); // a tick, so turning it on is audible
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useSoundMuted(): boolean {
  return useSyncExternalStore(subscribe, () => muted, () => true);
}

let context: AudioContext | null = null;
const buffers = new Map<string, Promise<AudioBuffer | null>>();
const lastPlayed = new Map<SoundName, number>();

function audio(): AudioContext | null {
  if (context) return context;
  const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
  if (!Ctor) return null;
  context = new Ctor();
  return context;
}

function load(ctx: AudioContext, file: string): Promise<AudioBuffer | null> {
  let buffer = buffers.get(file);
  if (!buffer) {
    buffer = fetch(`/dice-kingdom/sounds/${file}.mp3`)
      .then((r) => (r.ok ? r.arrayBuffer() : Promise.reject(new Error(r.statusText))))
      .then((data) => ctx.decodeAudioData(data))
      .catch(() => null);
    buffers.set(file, buffer);
  }
  return buffer;
}

// Fire and forget. `delayMs` lines a sound up with an animation beat.
export function playSound(name: SoundName, delayMs = 0) {
  if (muted) return;
  const ctx = audio();
  if (!ctx) return;
  // The same sound twice within 60ms is one event seen twice (or a crowd).
  const now = performance.now() + delayMs;
  if (now - (lastPlayed.get(name) ?? -Infinity) < 60) return;
  lastPlayed.set(name, now);
  if (ctx.state === "suspended") void ctx.resume().catch(() => {});
  const { files, volume } = SOUNDS[name];
  const file = files[Math.floor(Math.random() * files.length)];
  void load(ctx, file).then((buffer) => {
    if (!buffer || muted) return;
    const source = ctx.createBufferSource();
    source.buffer = buffer;
    const gain = ctx.createGain();
    gain.gain.value = volume;
    source.connect(gain).connect(ctx.destination);
    source.start(ctx.currentTime + delayMs / 1000);
  });
}

// What changed between two snapshots of one game, as sounds. Rolls aren't
// here - useDiceRoll plays those with the tumble it starts.
export function gameSounds(prev: GameState, next: GameState): SoundName[] {
  const out: SoundName[] = [];
  const before = new Map(prev.dice.map((d) => [d.id, d.zone]));
  for (const die of next.dice) {
    const was = before.get(die.id);
    if (!was || was === die.zone) continue;
    if (was === "Unpurchased") out.push("buy");
    else if ((was === "FieldZone" || was === "AttackZone") && (die.zone === "PrepArea" || die.zone === "OutOfPlay")) out.push("ko");
    else if (was === "ReservePool" && die.zone === "FieldZone") out.push("field");
    else if (was === "FieldZone" && die.zone === "AttackZone") out.push("attack");
    else if (was === "ReservePool" && die.zone === "UsedPile") out.push("spend");
  }
  if ((next.blocks?.length ?? 0) > (prev.blocks?.length ?? 0)) out.push("block");
  if (next.playerOne.life < prev.playerOne.life || next.playerTwo.life < prev.playerTwo.life) out.push("hit");
  if (next.gameOver && !prev.gameOver) {
    // Hotseat (no seat of your own) or a tie: the cheerful one.
    const lost = next.yourPlayerId && next.winnerId && next.winnerId !== next.yourPlayerId;
    out.push(lost ? "lose" : "win");
  } else if (
    next.yourPlayerId &&
    next.activePlayerId === next.yourPlayerId &&
    prev.activePlayerId !== next.activePlayerId
  ) {
    out.push("yourTurn");
  }
  return [...new Set(out)];
}

// Plays gameSounds for each new state of the same game.
export function useGameSounds(game: GameState | null) {
  const prev = useRef<GameState | null>(null);
  useEffect(() => {
    const last = prev.current;
    prev.current = game;
    if (!game || !last || last.gameId !== game.gameId || last.version === game.version) return;
    gameSounds(last, game).forEach((name, i) => playSound(name, i * 90));
  }, [game]);
}
