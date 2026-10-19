const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

function load() {
  const source = fs.readFileSync(path.join(__dirname, '../src/dicekingdom/customTeams.ts'), 'utf8');
  const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
  const module = { exports: {} };
  new Function('module', 'exports', js)(module, module.exports);
  return module.exports;
}

const { basicActionFits, tardigradesForChampion } = load();

test('a Champion team takes only Basic Actions of its energy; no Champion takes any', () => {
  assert.equal(basicActionFits('Claw', 'Claw'), true);
  assert.equal(basicActionFits('Shell', 'Claw'), false);
  assert.equal(basicActionFits(null, 'Claw'), false);
  assert.equal(basicActionFits('Shell', null), true);
});

test("picking a Champion moves a one-type Tardigrade mix to its energy, and leaves a mixed one alone", () => {
  assert.deepEqual(tardigradesForChampion({ Claw: 8, Shell: 0, Wing: 0, Eye: 0 }, 'Eye'), { Claw: 0, Shell: 0, Wing: 0, Eye: 8 });
  assert.deepEqual(tardigradesForChampion({ Claw: 0, Shell: 5, Wing: 0, Eye: 0 }, 'Wing'), { Claw: 0, Shell: 0, Wing: 5, Eye: 0 });
  const mixed = { Claw: 4, Shell: 0, Wing: 4, Eye: 0 };
  assert.deepEqual(tardigradesForChampion(mixed, 'Eye'), mixed);
  const none = { Claw: 0, Shell: 0, Wing: 0, Eye: 0 };
  assert.deepEqual(tardigradesForChampion(none, 'Eye'), none);
});
