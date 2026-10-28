// Face pictures for the 3D dice (2026-10-28): each side drawn on a canvas
// the way DieCube.tsx draws the front of a 2D die - same dark face, the
// animal's silhouette in the middle, cost / attack / defense in the corners,
// energy pips bottom-left - so the hand-off from the thrown die to its tile
// doesn't jump. Unlike the CSS cube (whose other sides are blank filler),
// every side shows its real face, since you can watch them tumble past.
import { createElement, type ReactElement } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { ENERGY_ICONS } from "../icons";
import type { CubeFace } from "../dieFaces";

export const FACE_PX = 256;
const INK = "oklch(0.95 0.02 90)";

type Icon = (p: { size?: number }) => ReactElement;
const images = new Map<string, Promise<CanvasImageSource | null>>();
const iconKeys = new WeakMap<Icon, string>();
let nextIconKey = 0;

function loadImage(src: string): Promise<HTMLImageElement | null> {
  return new Promise((resolve) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => resolve(null);
    img.src = src;
  });
}

// An icon component as an image: rendered to markup once, then loaded. The
// silhouettes are currentColor SVGs (tinted here); a few are raster drawings
// (<img>, like the family's Barn Swallow duck), used as they are.
function iconImage(icon: Icon, color: string): Promise<CanvasImageSource | null> {
  let id = iconKeys.get(icon);
  if (!id) iconKeys.set(icon, (id = String(nextIconKey++)));
  const key = `${id}|${color}`;
  let image = images.get(key);
  if (!image) {
    const host = document.createElement("div");
    const root = createRoot(host);
    flushSync(() => root.render(createElement(icon, { size: FACE_PX })));
    const first = host.firstElementChild;
    const el = first instanceof HTMLImageElement || first instanceof SVGElement ? first : first?.querySelector("svg, img");
    root.unmount();
    if (el instanceof HTMLImageElement) {
      image = loadImage(el.src);
    } else if (el instanceof SVGElement) {
      const svg = el.cloneNode(true) as SVGElement;
      svg.setAttribute("xmlns", "http://www.w3.org/2000/svg");
      svg.setAttribute("color", color);
      svg.setAttribute("width", String(FACE_PX));
      svg.setAttribute("height", String(FACE_PX));
      image = loadImage(`data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg.outerHTML)}`);
    } else {
      image = Promise.resolve(null);
    }
    images.set(key, image);
  }
  return image;
}

function cssVar(name: string): string {
  const el = document.querySelector(".dicekingdom") ?? document.documentElement;
  return getComputedStyle(el).getPropertyValue(name).trim() || "#888";
}

export interface FaceSpec {
  face: CubeFace;
  /** The energy this side shows (the landed side's comes from the live die). */
  energy?: { type: string; amount: number };
  mine: boolean;
}

/** One side of a die, drawn. Resolves once its icons have loaded. */
export async function drawFace({ face, energy, mine }: FaceSpec): Promise<HTMLCanvasElement> {
  const S = FACE_PX;
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = S;
  const ctx = canvas.getContext("2d")!;
  const hue = mine ? 62 : 250;

  const avatarSize = S * (face.kind === "action" ? 0.66 : 0.48);
  const energyIcon = energy ? ENERGY_ICONS[energy.type] : undefined;
  const [avatar, energyImage] = await Promise.all([
    face.avatar ? iconImage(face.avatar, INK) : Promise.resolve(null),
    energyIcon ? iconImage(energyIcon, "#ffffff") : Promise.resolve(null),
  ]);

  const bg = ctx.createLinearGradient(0, 0, S * 0.4, S);
  bg.addColorStop(0, `oklch(0.38 0.03 ${hue})`);
  bg.addColorStop(0.58, `oklch(0.28 0.03 ${hue})`);
  bg.addColorStop(1, `oklch(0.22 0.02 ${hue})`);
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, S, S);
  // A thin rim, like the 2D face's border.
  ctx.strokeStyle = `oklch(0.5 0.05 ${hue} / 0.7)`;
  ctx.lineWidth = S * 0.02;
  ctx.strokeRect(S * 0.03, S * 0.03, S * 0.94, S * 0.94);

  if (avatar) ctx.drawImage(avatar, (S - avatarSize) / 2, (S - avatarSize) / 2, avatarSize, avatarSize);

  const font = getComputedStyle(document.body).fontFamily || "sans-serif";
  const em = S * 0.3;
  ctx.fillStyle = INK;
  if (face.kind === "character") {
    ctx.textBaseline = "top";
    ctx.globalAlpha = 0.8;
    ctx.textAlign = "left";
    ctx.font = `600 ${em * 0.78}px ${font}`;
    ctx.fillText(String(face.fieldingCost), S * 0.08, S * 0.06);
    ctx.globalAlpha = 1;
    ctx.textAlign = "right";
    ctx.font = `700 ${em}px ${font}`;
    ctx.fillText(String(face.attack), S * 0.92, S * 0.06);
    ctx.textBaseline = "bottom";
    ctx.fillText(String(face.defense), S * 0.92, S * 0.95);
  }
  if ((face.burst ?? 0) > 0) {
    ctx.textAlign = "center";
    ctx.textBaseline = "top";
    ctx.font = `800 ${em * 0.65}px ${font}`;
    ctx.fillText("✦".repeat(face.burst ?? 0), S / 2, S * 0.06);
  }

  if (energy) {
    // The pip stack: energy-coloured discs, overlapping, bottom-left.
    const d = S * 0.34;
    const colour = cssVar(energy.type === "Wild" ? "--wild" : `--${energy.type.toLowerCase()}`);
    for (let i = 0; i < Math.max(1, energy.amount); i++) {
      const cx = S * 0.04 + d / 2 + i * (d - S * 0.09);
      const cy = S * 0.96 - d / 2;
      ctx.beginPath();
      ctx.arc(cx, cy, d / 2 + S * 0.012, 0, Math.PI * 2);
      ctx.fillStyle = `oklch(0.22 0.02 ${hue})`;
      ctx.fill();
      ctx.beginPath();
      ctx.arc(cx, cy, d / 2, 0, Math.PI * 2);
      ctx.fillStyle = colour;
      ctx.fill();
      if (energyImage) {
        const g = d * 0.62;
        ctx.drawImage(energyImage, cx - g / 2, cy - g / 2, g, g);
      }
    }
  }
  return canvas;
}
