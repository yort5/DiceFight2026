const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');

const file = path.join(__dirname, '..', 'src', 'dicekingdom', 'bot.ts');
const output = ts.transpileModule(fs.readFileSync(file, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText;
const bot = { exports: {} };
new Function('require', 'module', 'exports', output)(() => ({}), bot, bot.exports);
const { pickEnergy } = bot.exports;

const die = (id, energySymbolId, energyAmount, isTardigrade = true) => ({ id, energySymbolId, energyAmount, isTardigrade });

// Game 18d54e04 turn 9 (2026-11-24): 5 energy, buying a 3-Shell Pangolin.
const pool = [die('humm', 'Wing', 2, false), die('shell2', 'Shell', 2), die('shell1', 'Shell', 1)];

test('without lookahead the old choice is kept (exact, no Wild)', () => {
  const ids = pickEnergy(pool, 3, 'Shell');
  assert.equal(ids.length, 2);
});

test('keeps the Shell that still buys a 2-Shell Hermit Crab', () => {
  const ids = pickEnergy(pool, 3, 'Shell', 0, [{ cost: 2, matchType: 'Shell' }, { cost: 4, matchType: 'Wing' }]);
  assert.deepEqual([...ids].sort(), ['humm', 'shell1']);
});

test('a Wild is still kept when the leftovers tie', () => {
  // 3-Claw buy from Wild[1], Claw[1], Eye[2]: Eye+Claw spends no Wild, and
  // the Wild left over still pays the 1-Wing card.
  const p = [die('wild', 'Wild', 1), die('claw', 'Claw', 1), die('eye', 'Eye', 2)];
  const ids = pickEnergy(p, 3, 'Claw', 0, [{ cost: 1, matchType: 'Wing' }]);
  assert.deepEqual([...ids].sort(), ['claw', 'eye']);
});

test('an overspent die counts its spun-down leftover', () => {
  // 1-Shell buy from Shell[2] alone overspends; the leftover 1 Shell still buys a 1-Shell card.
  const ids = pickEnergy([die('s', 'Shell', 2), die('w', 'Wing', 1)], 1, 'Shell', 0, [{ cost: 2, matchType: 'Shell' }, { cost: 1, matchType: 'Shell' }]);
  assert.deepEqual(ids, ['s']);
});

test('keeps a Global payable when the purchases tie', () => {
  // 2-Shell buy from Shell[1], Shell[1], Claw[1]: either way a 1-cost buy
  // is still affordable, but only spending both Shells keeps the 1-Claw Global.
  const p = [die('c', 'Claw', 1), die('s1', 'Shell', 1), die('s2', 'Shell', 1)];
  const ids = pickEnergy(p, 2, 'Shell', 0, [{ cost: 1, matchType: null }, { cost: 1, matchType: 'Claw', global: true }]);
  assert.deepEqual([...ids].sort(), ['s1', 's2']);
});
