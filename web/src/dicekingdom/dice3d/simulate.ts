// The throw, simulated in advance (3D dice, 2026-10-28). The server has
// already rolled; this only decides how the dice tumble. The whole throw is
// run to rest first, then played back - so by the time a die is drawn we
// know which side ends up on top, and roll3d.ts paints the rolled face on
// that side. Pure physics, no DOM: tests and tuning run it in Node.
//
// Units: CSS pixels. The table is the screen: x right, y up (so screen y is
// -y), z towards the viewer; the floor is z = 0 and the screen edges are
// walls.
import * as CANNON from "cannon-es";

export interface ThrowSetup {
  width: number;
  height: number;
  size: number; // die edge, px
  count: number;
  /** Which edge the throw comes from: your dice from the bottom, theirs from the top. */
  from: "bottom" | "top";
  seed?: number;
}

export interface Pose {
  p: [number, number, number];
  q: [number, number, number, number]; // x, y, z, w
}

export interface ThrowResult {
  /** frames[f][die]: 60 per second. */
  frames: Pose[][];
  /** The local axis (index into FACE_AXES) facing up at rest, per die. */
  topFace: number[];
}

// Local face normals in three.js BoxGeometry material order: +x, -x, +y, -y, +z, -z.
export const FACE_AXES: [number, number, number][] = [
  [1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1],
];

const STEP = 1 / 120;
const FRAME_EVERY = 2; // 60 fps playback
const MAX_SECONDS = 1.8;
const REST_STEPS = 8;

function mulberry32(seed: number) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export function simulateThrow(setup: ThrowSetup): ThrowResult {
  const { width: W, height: H, size: s, count } = setup;
  const rand = mulberry32(setup.seed ?? Math.floor(Math.random() * 2 ** 31));
  const between = (a: number, b: number) => a + (b - a) * rand();

  const world = new CANNON.World({ gravity: new CANNON.Vec3(0, 0, -s * 95) });
  world.allowSleep = false;
  const diceMat = new CANNON.Material("dice");
  const floorMat = new CANNON.Material("floor");
  const wallMat = new CANNON.Material("wall");
  world.addContactMaterial(new CANNON.ContactMaterial(diceMat, floorMat, { friction: 0.32, restitution: 0.32 }));
  world.addContactMaterial(new CANNON.ContactMaterial(diceMat, wallMat, { friction: 0.05, restitution: 0.5 }));
  world.addContactMaterial(new CANNON.ContactMaterial(diceMat, diceMat, { friction: 0.15, restitution: 0.4 }));

  const plane = (material: CANNON.Material, position: [number, number, number], euler: [number, number, number]) => {
    const body = new CANNON.Body({ mass: 0, material, shape: new CANNON.Plane() });
    body.position.set(...position);
    body.quaternion.setFromEuler(...euler);
    world.addBody(body);
  };
  plane(floorMat, [0, 0, 0], [0, 0, 0]);
  plane(wallMat, [0, 0, 0], [0, Math.PI / 2, 0]); // left, facing +x
  plane(wallMat, [W, 0, 0], [0, -Math.PI / 2, 0]); // right, facing -x
  plane(wallMat, [0, 0, 0], [Math.PI / 2, 0, 0]); // top edge (y = 0), facing -y
  plane(wallMat, [0, -H, 0], [-Math.PI / 2, 0, 0]); // bottom edge, facing +y
  plane(wallMat, [0, 0, s * 8], [Math.PI, 0, 0]); // a lid, so nothing leaves the screen

  const up = setup.from === "bottom" ? 1 : -1;
  const startY = setup.from === "bottom" ? -H + s * 1.2 : -s * 1.2;
  const spread = Math.min(W - s * 2, count * s * 1.25);
  const bodies: CANNON.Body[] = [];
  for (let i = 0; i < count; i++) {
    const body = new CANNON.Body({
      mass: 1,
      material: diceMat,
      shape: new CANNON.Box(new CANNON.Vec3(s / 2, s / 2, s / 2)),
      linearDamping: 0.25,
      angularDamping: 0.25,
    });
    const x = count === 1 ? W / 2 : W / 2 - spread / 2 + (spread * i) / (count - 1);
    body.position.set(x + between(-s * 0.2, s * 0.2), startY + between(-s * 0.3, s * 0.3), s * between(1.2, 2.2));
    body.quaternion.setFromEuler(between(0, Math.PI * 2), between(0, Math.PI * 2), between(0, Math.PI * 2));
    // Up the table and a little sideways, towards the middle.
    body.velocity.set(between(-1, 1) * W * 0.25 + (W / 2 - x) * 0.6, up * between(0.9, 1.15) * H * 1.1, s * between(4, 8));
    body.angularVelocity.set(between(-1, 1) * 22, between(-1, 1) * 22, between(-1, 1) * 10);
    world.addBody(body);
    bodies.push(body);
  }

  const frames: Pose[][] = [];
  const snapshot = () =>
    frames.push(bodies.map((b) => ({
      p: [b.position.x, b.position.y, b.position.z],
      q: [b.quaternion.x, b.quaternion.y, b.quaternion.z, b.quaternion.w],
    })));
  snapshot();
  let still = 0;
  const maxSteps = Math.ceil(MAX_SECONDS / STEP);
  for (let step = 1; step <= maxSteps; step++) {
    world.step(STEP);
    if (step % FRAME_EVERY === 0) snapshot();
    const resting = bodies.every((b) => b.velocity.length() < s * 0.2 && b.angularVelocity.length() < 1 && b.position.z < s * 0.62);
    still = resting ? still + 1 : 0;
    if (still >= REST_STEPS) break;
  }
  snapshot();

  const topFace = bodies.map((b) => {
    let best = 0;
    let bestDot = -Infinity;
    FACE_AXES.forEach((axis, i) => {
      const world = b.quaternion.vmult(new CANNON.Vec3(...axis));
      if (world.z > bestDot) {
        bestDot = world.z;
        best = i;
      }
    });
    return best;
  });
  return { frames, topFace };
}
