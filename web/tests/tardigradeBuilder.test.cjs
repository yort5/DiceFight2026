const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

function loadTs(file) {
  const src = fs.readFileSync(path.join(__dirname, '../src/dicekingdom', file), 'utf8');
  const js = ts.transpileModule(src, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020,
  } }).outputText;
  const exports = {};
  vm.runInNewContext(js, { exports });
  return exports;
}

const pool = loadTs('customTeams.ts');
const errors = loadTs('apiErrors.ts');

test('Tardigrades cannot exceed eight in any combination', () => {
  let selected = { Claw: 8, Shell: 0, Wing: 0, Eye: 0 };
  selected = pool.changeTardigradeCount(selected, 'Wing', 1);
  assert.equal(selected.Wing, 0);
  selected = pool.changeTardigradeCount(selected, 'Claw', -3);
  selected = pool.changeTardigradeCount(selected, 'Wing', 3);
  assert.equal(selected.Wing, 3);
  assert.equal(selected.Claw, 5);
  assert.equal(pool.tardigradeTotal(selected), 8);
  selected = pool.changeTardigradeCount(selected, 'Eye', 100);
  assert.equal(selected.Eye, 0);
  selected = pool.changeTardigradeCount(selected, 'Claw', -5);
  assert.equal(pool.tardigradeTotal(selected), 3);
  selected = pool.changeTardigradeCount(selected, 'Eye', 100);
  assert.equal(selected.Eye, 5);
  assert.equal(pool.tardigradeTotal(selected), 8);
});

test('Oversized and invalid imported pools are safely capped', () => {
  const selected = pool.capTardigrades({ Claw: 3, Shell: 3, Wing: 8, Eye: -10 });
  assert.deepEqual({ ...selected }, { Claw: 3, Shell: 3, Wing: 2, Eye: 0 });
  assert.deepEqual({ ...pool.capTardigrades({ Claw: NaN, Shell: Infinity, Wing: 2.5, Eye: 4 }) },
    { Claw: 0, Shell: 0, Wing: 0, Eye: 4 });
});

test('ASP.NET 400 validation describes the real problem, including an older API', () => {
  const message = errors.apiErrorMessage(400, { title: 'One or more validation errors occurred.',
    errors: { PlayerOneChampionId: ['The PlayerOneChampionId field is required.'] } });
  assert.match(message, /game server does not appear to support custom teams/i);
  assert.match(message, /PlayerOneChampionId/);
  assert.match(errors.apiErrorMessage(400, { errors: { 'cards[0].cardId': ['Invalid card.'] } }), /Invalid card/);
  assert.equal(errors.apiErrorMessage(400, { error: 'Invalid or duplicate card in custom team.' }), 'Invalid or duplicate card in custom team.');
  assert.equal(errors.apiErrorMessage(500, null), 'Request failed: 500');
});

test('Team Builder uses the capped selector and starts with it closed to expose the roster', () => {
  const src = fs.readFileSync(path.join(__dirname, '../src/TeamBuilderPage.tsx'), 'utf8');
  assert.match(src, /<details className="dk-builder-tardigrades">/);
  assert.doesNotMatch(src, /<details className="dk-builder-tardigrades" open/);
  assert.match(src, /capTardigrades\(counts\)/);
  assert.match(src, /disabled=\{tardigradeCount >= MAX_STARTING_TARDIGRADES\}/);
});
