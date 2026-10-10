const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');
const src = path.join(__dirname, '..', 'src', 'dicekingdom');

function loadTs(name, imports = {}) {
  const output = ts.transpileModule(fs.readFileSync(path.join(src, name), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  const mod = { exports: {} };
  new Function('require', 'module', 'exports', output)(id => {
    if (!(id in imports)) throw new Error(`Unexpected import: ${id}`);
    return imports[id];
  }, mod, mod.exports);
  return mod.exports;
}

const bot = loadTs('bot.ts');
const motion = loadTs('dieMotion.ts', { './bot': bot });
const die = (id, extras = {}) => ({
  id, zone: 'ReservePool', controllerId: 'p2', level: 1,
  effectiveAttack: 1, effectiveDefense: 1, energySymbolId: null,
  energyAmount: 0, isActionFace: false, ...extras,
});
const game = (step, dice) => ({ currentStepId: step, activePlayerId: 'p2', dice });

test('opponent stat/ability updates do not animate unchanged physical faces', () => {
  const prev = game('main', [die('a')]);
  const next = game('main', [die('a', { effectiveAttack: 4, effectiveDefense: 5 })]);
  assert.deepEqual(motion.remoteRolledIds(prev, next), []);
  assert.deepEqual(motion.classifyDieMotion(prev, next), { tumbles: [], flips: [] });
});

test('a genuine remote roll into Reserve tumbles, including one entering on a new face', () => {
  const prev = game('roll-and-reroll', [die('a', { zone: 'DiceFromPrep', level: null, effectiveAttack: null })]);
  const next = game('main', [die('a')]);
  assert.deepEqual(motion.remoteRolledIds(prev, next), ['a']);
  assert.deepEqual(motion.classifyDieMotion(prev, next, ['a']), { tumbles: ['a'], flips: [] });
});

test('an explicitly selected reroll still tumbles when it lands on the same face', () => {
  const prev = game('roll-and-reroll', [die('a')]);
  const next = game('main', [die('a')]);
  assert.deepEqual(motion.classifyDieMotion(prev, next, ['a']), { tumbles: ['a'], flips: [] });
});

test('spending part of a double-energy face flips without a new roll', () => {
  const prev = game('main', [die('a', { level: null, effectiveAttack: null, energySymbolId: 'Shell', energyAmount: 2 })]);
  const next = game('main', [die('a', { level: null, effectiveAttack: null, energySymbolId: 'Shell', energyAmount: 1 })]);
  assert.deepEqual(motion.remoteRolledIds(prev, next), []);
  assert.deepEqual(motion.classifyDieMotion(prev, next), { tumbles: [], flips: ['a'] });
});

test('moving an unchanged rolled die between zones does not trigger movement', () => {
  const prev = game('main', [die('a')]);
  const next = game('main', [die('a', { zone: 'FieldZone' })]);
  assert.deepEqual(motion.classifyDieMotion(prev, next), { tumbles: [], flips: [] });
});

test('non-roll actions do not get interpreted as a remote roll', () => {
  const prev = game('main', [die('a', { zone: 'PrepArea', level: null, effectiveAttack: null })]);
  const next = game('main', [die('a')]);
  assert.deepEqual(motion.remoteRolledIds(prev, next), []);
});


test('Basic Action faces count as rolled dice even with no energy and no creature stats', () => {
  const action = die('action', { level: null, effectiveAttack: null, effectiveDefense: null,
    energySymbolId: null, energyAmount: 0, isActionFace: true });
  assert.equal(bot.rolled(action), true);
  assert.equal(bot.rolled({ ...action, isActionFace: false }), false);
  assert.deepEqual(motion.classifyDieMotion(game('roll-and-reroll', [
    { ...action, zone: 'DiceFromBag', isActionFace: false },
  ]), game('roll-and-reroll', [action]), ['action']), { tumbles: ['action'], flips: [] });
});

test('remote first roll still tumbles if the polling client missed the Roll step', () => {
  const before = game('start-of-turn', [die('a', { zone: 'DiceFromBag', level: null, effectiveAttack: null })]);
  const after = game('main', [die('a')]);
  assert.deepEqual(motion.remoteRolledIds(before, after), ['a']);
});

test('remote first roll is detected from new roll log even if Draw step was missed', () => {
  const before = { ...game('main', [die('a', { zone: 'Bag', level: null, effectiveAttack: null })]), log: [{ seq: 1, playerId: 'p1', text: 'other action' }] };
  const after = { ...game('main', [die('a')]), log: [{ seq: 1, playerId: 'p1', text: 'other action' }, { seq: 2, playerId: 'p2', text: 'Opponent rolls.' }] };
  assert.deepEqual(motion.remoteRolledIds(before, after), ['a']);
});

test('non-roll move to reserve and opponent purchases do not tumble dice', () => {
  const before = { ...game('main', [die('a', { zone: 'UsedPile', level: null, effectiveAttack: null })]), log: [{ seq: 10, playerId: 'p2', text: 'Opponent rolls.' }] };
  const after = { ...game('main', [die('a')]), log: [{ seq: 10, playerId: 'p2', text: 'Opponent rolls.' }, { seq: 11, playerId: 'p2', text: 'Opponent purchases a die.' }] };
  assert.deepEqual(motion.remoteRolledIds(before, after), []);
});

test('remote Basic Action roll into reserve also tumbles', () => {
  const old = die('action', { zone: 'DiceFromPrep', level: null, effectiveAttack: null, effectiveDefense: null, energySymbolId: null, isActionFace: false });
  const now = { ...old, zone: 'ReservePool', isActionFace: true };
  const before = game('start-of-turn', [old]);
  const after = game('main', [now]);
  assert.deepEqual(motion.remoteRolledIds(before, after), ['action']);
});
