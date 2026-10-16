const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

function load() {
  const source = fs.readFileSync(path.join(__dirname, '../src/dicekingdom/laneRule.ts'), 'utf8');
  const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
  const module = { exports: {} };
  new Function('module', 'exports', js)(module, module.exports);
  return module.exports;
}

const { laneCounts, canAddToLane, lanesAllowed, declareLane } = load();

test('a lane stacks only when another lane has as many', () => {
  assert.equal(canAddToLane([0, 0, 0, 0], 2), true);
  assert.equal(canAddToLane([1, 0, 0, 0], 0), false);
  assert.equal(canAddToLane([1, 1, 0, 0], 0), true);
  assert.equal(canAddToLane([2, 1, 0, 0], 0), false);
  assert.equal(canAddToLane([2, 1, 0, 0], 1), true);
});

test('a whole declaration: the fullest lane at most one ahead of the next', () => {
  assert.equal(lanesAllowed({ a: 0, b: 0 }), false);
  assert.equal(lanesAllowed({ a: 0, b: 0, c: 1 }), true);
  assert.equal(lanesAllowed({ a: 0, b: 0, c: 0, d: 1 }), false);
  assert.equal(lanesAllowed({ a: 0, b: 0, c: 0, d: 1, e: 1 }), true);
  assert.equal(lanesAllowed({}), true);
});

test('moving a pending die does not count its own spot', () => {
  assert.deepEqual(laneCounts({ a: 0, b: 1 }, 'a'), [0, 1, 0, 0]);
  assert.equal(canAddToLane(laneCounts({ a: 0, b: 1 }, 'a'), 1), false);
});

test('Declare falls back to the emptiest lane when the targeted one is closed', () => {
  assert.equal(declareLane([1, 0, 0, 0], 0), 1);
  assert.equal(declareLane([1, 1, 0, 0], 0), 0);
  assert.equal(declareLane([2, 1, 1, 0], 0), 3);
});
