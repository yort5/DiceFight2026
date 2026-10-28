// 3D dice (2026-10-28, the parked "make the dice the star" step of the
// game-feel plan): a roll is thrown across the screen as real 3D dice,
// which settle and then fly into their tiles. Loaded on demand
// (dice3dSetting.ts) so three.js and the physics stay out of the first
// page load.
//
// The server has already rolled. simulate.ts runs the throw to rest first;
// the side that ends up on top gets the rolled face painted on it, and the
// recorded throw is played back. The caller keeps each die's tile empty
// until `onLand` (useDiceRoll's "away" spin), when the 3D die has reached it
// showing the same face. A tap anywhere skips ahead.
import { CanvasTexture, DirectionalLight, HemisphereLight, Material, Matrix4, Mesh, MeshStandardMaterial, PCFShadowMap, PerspectiveCamera, PlaneGeometry, Quaternion, Scene, ShadowMaterial, SRGBColorSpace, Texture, Vector3, WebGLRenderer } from "three";
import { RoundedBoxGeometry } from "three/examples/jsm/geometries/RoundedBoxGeometry.js";
import type { CubeFace } from "../dieFaces";
import { drawFace } from "./faces";
import { FACE_AXES, simulateThrow, type Pose } from "./simulate";

export interface Die3D {
  id: string;
  /** Six sides; [0] is the rolled face. */
  faces: CubeFace[];
  /** The rolled face's energy, from the live die. */
  energy?: { type: string; amount: number };
  mine: boolean;
}

// Which way is "up" in each side's picture, in the die's own axes - read off
// BoxGeometry's UVs (same material order as FACE_AXES).
const FACE_UP: [number, number, number][] = [
  [0, 1, 0], [0, 1, 0], [0, 0, -1], [0, 0, 1], [0, 1, 0], [0, 1, 0],
];

const HOLD_MS = 160;
const FLY_MS = 420;
const FLY_STAGGER_MS = 55;
const GIVE_UP_MS = 6000;

const easeInOut = (t: number) => (t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);

function tileOf(id: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(`[data-fly-id="die:${CSS.escape(id)}"]`);
}

/** The die drawing inside a tile, which is what the 3D die should cover. */
function targetRect(tile: HTMLElement | null): DOMRect | null {
  if (!tile || !tile.isConnected) return null;
  const rect = (tile.querySelector(".die-cube-box") ?? tile).getBoundingClientRect();
  return rect.width > 0 && rect.bottom > 0 && rect.top < window.innerHeight ? rect : null;
}

// The rotation that puts side `top` facing the viewer with its picture upright.
function restingQuaternion(top: number): Quaternion {
  const n = new Vector3(...FACE_AXES[top]);
  const up = new Vector3(...FACE_UP[top]);
  const right = new Vector3().crossVectors(up, n);
  const local = new Matrix4().makeBasis(right, up, n);
  return new Quaternion().setFromRotationMatrix(local.invert());
}

function poseAt(frames: Pose[][], die: number, ms: number, p: Vector3, q: Quaternion) {
  const f = Math.min(frames.length - 1, (ms / 1000) * 60);
  const a = frames[Math.floor(f)][die];
  const b = frames[Math.min(frames.length - 1, Math.floor(f) + 1)][die];
  const t = f - Math.floor(f);
  p.set(a.p[0] + (b.p[0] - a.p[0]) * t, a.p[1] + (b.p[1] - a.p[1]) * t, a.p[2] + (b.p[2] - a.p[2]) * t);
  q.set(...a.q).slerp(new Quaternion(...b.q), t);
}

/**
 * Throw `dice`; `onLand` fires as each one reaches its tile (or fades, if it
 * has none on screen). Resolves when all have landed. Rejects before
 * anything is shown if WebGL isn't available, so the caller can fall back.
 */
export async function roll3d(dice: Die3D[], onLand: (id: string) => void): Promise<void> {
  const W = window.innerWidth;
  const H = window.innerHeight;
  const size = Math.max(50, Math.min(84, Math.min(W, H) * 0.14));
  const from = dice.every((d) => !d.mine) ? "top" : "bottom";
  const { frames, topFace } = simulateThrow({ width: W, height: H, size, count: dice.length, from });

  // The rolled face goes on whichever side lands on top; the rest in order.
  const canvases = await Promise.all(dice.map((die, i) => {
    const others = die.faces.slice(1);
    return Promise.all(FACE_AXES.map((_, side) => {
      const face = side === topFace[i] ? die.faces[0] : others.shift() ?? die.faces[0];
      const energy = side === topFace[i]
        ? die.energy
        : face.kind === "energy" ? { type: face.icon, amount: face.amount } : undefined;
      return drawFace({ face, energy, mine: die.mine });
    }));
  }));

  const canvas = document.createElement("canvas");
  const renderer = new WebGLRenderer({ canvas, alpha: true, antialias: true });
  canvas.className = "dk-dice3d";
  canvas.style.cssText = "position:fixed;inset:0;width:100vw;height:100vh;pointer-events:none;z-index:1000";
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.setSize(W, H);
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = PCFShadowMap;

  // A camera straight above the middle of the screen, far enough back that
  // the table (z = 0) maps one world unit to one CSS pixel.
  const D = Math.max(W, H) * 1.6;
  const camera = new PerspectiveCamera((2 * Math.atan(H / 2 / D) * 180) / Math.PI, W / H, D * 0.1, D * 2);
  camera.position.set(W / 2, -H / 2, D);
  camera.lookAt(W / 2, -H / 2, 0);

  const scene = new Scene();
  // Lit so a side facing straight up shows its picture at about its own
  // colours (three.js divides diffuse light by pi), matching the 2D tile.
  scene.add(new HemisphereLight(0xffffff, 0x8a8a8a, Math.PI * 0.62));
  const sun = new DirectionalLight(0xffffff, Math.PI * 0.45);
  sun.position.set(W * 0.25, -H * 0.15, D * 0.6);
  sun.target.position.set(W / 2, -H / 2, 0);
  sun.castShadow = true;
  sun.shadow.mapSize.set(1024, 1024);
  Object.assign(sun.shadow.camera, { left: -W, right: W, top: H, bottom: -H, near: 1, far: D * 2 });
  sun.shadow.camera.updateProjectionMatrix();
  scene.add(sun, sun.target);
  const floor = new Mesh(new PlaneGeometry(W * 2, H * 2), new ShadowMaterial({ opacity: 0.3 }));
  floor.position.set(W / 2, -H / 2, 0);
  floor.receiveShadow = true;
  scene.add(floor);

  const geometry = new RoundedBoxGeometry(1, 1, 1, 4, 0.12);
  const textures: Texture[] = [];
  const meshes = dice.map((_, i) => {
    const materials = canvases[i].map((c) => {
      const texture = new CanvasTexture(c);
      texture.colorSpace = SRGBColorSpace;
      texture.anisotropy = 4;
      textures.push(texture);
      return new MeshStandardMaterial({ map: texture, roughness: 0.55, metalness: 0 });
    });
    const mesh = new Mesh(geometry, materials);
    mesh.castShadow = true;
    mesh.scale.setScalar(size);
    scene.add(mesh);
    return mesh;
  });

  // Fails here (no WebGL) before anything is hidden or shown.
  renderer.render(scene, camera);
  document.body.appendChild(canvas);

  const ids = dice.map((d) => d.id);
  const landed = new Set<string>();
  const land = (i: number) => {
    if (landed.has(ids[i])) return;
    landed.add(ids[i]);
    meshes[i].visible = false;
    onLand(ids[i]);
  };

  const simMs = ((frames.length - 1) / 60) * 1000;
  const flyAt = dice.map((_, i) => simMs + HOLD_MS + i * FLY_STAGGER_MS);
  const flyFrom: ({ p: Vector3; q: Quaternion; s: number } | null)[] = dice.map(() => null);
  const resting = topFace.map(restingQuaternion);
  const start = performance.now();

  // A tap skips the rest of the throw: every die heads for its tile now.
  const skip = () => {
    const now = performance.now() - start;
    flyAt.forEach((at, i) => { flyAt[i] = Math.min(at, now + i * 25); });
  };
  window.addEventListener("pointerdown", skip, true);

  const p = new Vector3();
  const q = new Quaternion();
  await new Promise<void>((resolve) => {
    const frame = () => {
      const t = performance.now() - start;
      dice.forEach((_, i) => {
        if (landed.has(ids[i])) return;
        const mesh = meshes[i];
        if (t < flyAt[i]) {
          poseAt(frames, i, Math.min(t, simMs), p, q);
          mesh.position.copy(p);
          mesh.quaternion.copy(q);
          return;
        }
        const fromPose = flyFrom[i] ?? (flyFrom[i] = { p: mesh.position.clone(), q: mesh.quaternion.clone(), s: mesh.scale.x });
        const u = Math.min(1, (t - flyAt[i]) / FLY_MS);
        const e = easeInOut(u);
        const rect = targetRect(tileOf(ids[i]));
        if (rect) {
          // Land so the top face covers the tile's die exactly: the face is
          // at height s, so it looks k times bigger and is pushed outwards.
          const w = rect.width;
          const s = (w * D) / (D + w);
          const k = D / (D - s);
          const x = W / 2 + (rect.left + w / 2 - W / 2) / k;
          const y = -H / 2 + (H / 2 - (rect.top + rect.height / 2)) / k;
          mesh.position.set(
            fromPose.p.x + (x - fromPose.p.x) * e,
            fromPose.p.y + (y - fromPose.p.y) * e,
            fromPose.p.z + (s / 2 - fromPose.p.z) * e + Math.sin(Math.PI * u) * size * 0.9,
          );
          mesh.scale.setScalar(fromPose.s + (s - fromPose.s) * e);
          mesh.quaternion.copy(fromPose.q).slerp(resting[i], e);
        } else {
          mesh.scale.setScalar(fromPose.s * (1 - e));
        }
        if (u >= 1) land(i);
      });
      renderer.render(scene, camera);
      if (landed.size === ids.length || t > GIVE_UP_MS) resolve();
      else requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
  });

  window.removeEventListener("pointerdown", skip, true);
  dice.forEach((_, i) => land(i));
  canvas.remove();
  geometry.dispose();
  textures.forEach((t) => t.dispose());
  meshes.forEach((m) => (m.material as Material[]).forEach((mat) => mat.dispose()));
  renderer.dispose();
}
