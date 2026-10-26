// sound.ts's gameSounds (2026-10-26): which sounds a state change plays.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

function load() {
  const source = fs.readFileSync(path.join(__dirname, '../src/dicekingdom/sound.ts'), 'utf8');
  const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 } }).outputText;
  const exports = {};
  vm.runInNewContext(js, { exports, localStorage: { getItem: () => null }, require: id => {
    if (id === 'react') return { useEffect() {}, useRef() {}, useSyncExternalStore() {} };
    throw new Error(`Unexpected dependency: ${id}`);
  }});
  return exports;
}
const loaded = load();
// Copied out of the vm realm so deepEqual compares plain arrays.
const gameSounds = (a, b) => Array.from(loaded.gameSounds(a, b));

const die = (id, zone) => ({ id, zone });
function state(dice, extra = {}) {
  return { gameId: 'g', version: 1, activePlayerId: 'p1', yourPlayerId: 'p1', dice, blocks: [],
    playerOne: { id: 'p1', life: 20 }, playerTwo: { id: 'p2', life: 20 }, ...extra };
}

test('zone moves map to buy, field, attack, spend and KO', () => {
  const before = state([die('a', 'Unpurchased'), die('b', 'ReservePool'), die('c', 'FieldZone'), die('d', 'ReservePool'), die('e', 'AttackZone')]);
  const after = state([die('a', 'UsedPile'), die('b', 'FieldZone'), die('c', 'AttackZone'), die('d', 'UsedPile'), die('e', 'PrepArea')]);
  assert.deepEqual(gameSounds(before, after).sort(), ['attack', 'buy', 'field', 'ko', 'spend']);
});

test('life loss is a hit, and each sound plays once however many dice caused it', () => {
  const before = state([die('a', 'Unpurchased'), die('b', 'Unpurchased')]);
  const after = state([die('a', 'UsedPile'), die('b', 'UsedPile')], { playerTwo: { id: 'p2', life: 17 } });
  assert.deepEqual(gameSounds(before, after), ['buy', 'hit']);
});

test('your turn chimes only for your own seat', () => {
  const theirs = state([], { activePlayerId: 'p2' });
  assert.deepEqual(gameSounds(theirs, state([])), ['yourTurn']);
  assert.deepEqual(gameSounds(theirs, state([], { yourPlayerId: null })), []);
  assert.deepEqual(gameSounds(state([]), state([], { activePlayerId: 'p2' })), []);
});

test('game over: win, lose, or (hotseat) the cheerful one', () => {
  assert.deepEqual(gameSounds(state([]), state([], { gameOver: true, winnerId: 'p1' })), ['win']);
  assert.deepEqual(gameSounds(state([]), state([], { gameOver: true, winnerId: 'p2' })), ['lose']);
  assert.deepEqual(gameSounds(state([]), state([], { gameOver: true, winnerId: 'p2', yourPlayerId: null })), ['win']);
});

test('a new block plays block', () => {
  assert.deepEqual(gameSounds(state([]), state([], { blocks: [{}] })), ['block']);
});
