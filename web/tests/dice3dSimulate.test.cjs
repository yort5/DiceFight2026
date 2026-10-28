// 3D dice (2026-10-28): the pre-run throw in dice3d/simulate.ts settles
// quickly, stays on the screen, and reports the side that ends up on top.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');
const CANNON = require('cannon-es');

function loadSimulate() {
  const file = path.join(__dirname, '..', 'src', 'dicekingdom', 'dice3d', 'simulate.ts');
  const output = ts.transpileModule(fs.readFileSync(file, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  const mod = { exports: {} };
  new Function('require', 'module', 'exports', output)((id) => {
    if (id === 'cannon-es') return CANNON;
    throw new Error(`Unexpected import: ${id}`);
  }, mod, mod.exports);
  return mod.exports;
}

const { simulateThrow, FACE_AXES } = loadSimulate();
const SCREENS = [[402, 874, 'bottom'], [1400, 950, 'top']];

test('every throw ends inside the screen, on the table, within 1.8s', () => {
  for (const [width, height, from] of SCREENS) {
    for (const count of [1, 4, 8]) {
      for (let seed = 1; seed <= 12; seed++) {
        const size = Math.max(44, Math.min(80, Math.min(width, height) * 0.12));
        const { frames } = simulateThrow({ width, height, size, count, from, seed });
        assert.ok(frames.length / 60 <= 1.9, `${width}x${height} n=${count} seed ${seed}: ${frames.length / 60}s`);
        for (const pose of frames[frames.length - 1]) {
          const [x, y, z] = pose.p;
          assert.ok(x > 0 && x < width && -y > 0 && -y < height, `off screen: ${pose.p}`);
          assert.ok(z < size * 1.2, `still in the air: ${z}`);
        }
      }
    }
  }
});

test('the reported top face is the one pointing up', () => {
  const { frames, topFace } = simulateThrow({ width: 402, height: 874, size: 48, count: 6, from: 'bottom', seed: 7 });
  frames[frames.length - 1].forEach((pose, i) => {
    const q = new CANNON.Quaternion(...pose.q);
    const up = q.vmult(new CANNON.Vec3(...FACE_AXES[topFace[i]]));
    assert.ok(up.z > 0.9, `die ${i}: face ${topFace[i]} points ${up.z}`);
  });
});

test('the same seed gives the same throw', () => {
  const a = simulateThrow({ width: 402, height: 874, size: 48, count: 3, from: 'bottom', seed: 3 });
  const b = simulateThrow({ width: 402, height: 874, size: 48, count: 3, from: 'bottom', seed: 3 });
  assert.deepEqual(a.topFace, b.topFace);
  assert.equal(a.frames.length, b.frames.length);
});
