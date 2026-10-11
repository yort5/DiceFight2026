const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');
const src = path.join(__dirname, '..', 'src', 'dicekingdom');

function loadTs(file, imports = {}) {
  const output = ts.transpileModule(fs.readFileSync(path.join(src, file), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  const module = { exports: {} };
  const localRequire = (id) => {
    if (!(id in imports)) throw new Error('Unexpected import ' + id);
    return imports[id];
  };
  new Function('require', 'module', 'exports', output)(localRequire, module, module.exports);
  return module.exports;
}

const bot = loadTs('bot.ts');
const calls = [];
const api = {
  useGlobal: (...args) => { calls.push(['global', ...args]); return Promise.resolve({}); },
  useAction: (...args) => { calls.push(['action', ...args]); return Promise.resolve({}); },
  foresight: (...args) => { calls.push(['foresight', ...args]); return Promise.resolve({}); },
};
const abilities = loadTs('sharedAbilities.ts', { './api': { api }, './bot': bot });

function fixture() {
  const cards = new Map([
    ['act', { id: 'act', name: 'Anger Issues', isAction: true, purchaseCost: 1, energyTypes: [], global: { abilityIndex: 2, cost: 1, energyType: 'Claw', text: 'Pump', oncePerTurn: false } }],
    ['creature', { id: 'creature', name: 'Creature', purchaseCost: 2, energyTypes: ['Claw'] }],
  ]);
  const game = {
    gameId: 'g1', activePlayerId: 'p1', currentStepId: 'main', priorityPlayerId: 'p1', pendingChoice: null,
    playerOne: { id: 'p1', virtualEnergy: 0, foresightAvailable: true },
    playerTwo: { id: 'p2', virtualEnergy: 0, foresightAvailable: false },
    dice: [
      { id: 'energy', cardId: 'creature', controllerId: 'p1', zone: 'ReservePool', energyAmount: 1, energySymbolId: 'Claw' },
      { id: 'ready', cardId: 'act', controllerId: 'p1', zone: 'ReservePool', energyAmount: 0, energySymbolId: null, isActionFace: true },
      { id: 'community', cardId: 'act', controllerId: 'p2', zone: 'Unpurchased' },
      { id: 'own', cardId: 'creature', controllerId: 'p1', zone: 'Unpurchased' },
      { id: 'opponent', cardId: 'creature', controllerId: 'p2', zone: 'Unpurchased' },
    ],
  };
  return { game, cards };
}

test('same ability selector returns payable Globals, action dice, and Foresight', () => {
  const { game, cards } = fixture();
  const result = abilities.getAbilityOptions(game, cards, 'p1', false);
  assert.equal(result.globals.length, 1);
  assert.equal(result.globals[0].blocked, null);
  assert.deepEqual(result.globals[0].command, { kind: 'global', cardId: 'act', abilityIndex: 2, energyDieIds: ['energy'] });
  assert.equal(result.actionDice[0].command.kind, 'action');
  assert.equal(result.foresightReady, true);
  assert.equal(result.foresightDice.length, 2);
});

test('priority, pending choices, and busy state disable shared actions', () => {
  const { game, cards } = fixture();
  game.priorityPlayerId = 'p2';
  assert.equal(abilities.getAbilityOptions(game, cards, 'p1', false).globals[0].command, null);
  game.priorityPlayerId = 'p1';
  game.pendingChoice = { controllerId: 'p1' };
  assert.equal(abilities.getAbilityOptions(game, cards, 'p1', false).actionDice[0].command, null);
  game.pendingChoice = null;
  assert.equal(abilities.getAbilityOptions(game, cards, 'p1', true).globals[0].command, null);
});

test('Basic Action community pool is buyable by either player, but opponent creature is not', () => {
  const { game, cards } = fixture();
  assert.deepEqual(abilities.purchasableDiceFor(game, cards, 'p1').map(d => d.id), ['community', 'own']);
  assert.deepEqual(abilities.basicActionStock(game, cards).map(c => [c.card.id, c.dice.length]), [['act', 1]]);
  assert.deepEqual(abilities.getPurchasePayment(game, cards.get('act'), 'p1'), ['energy']);
});

test('Globals can use virtual energy and dispatch same API calls regardless of layout', async () => {
  const { game, cards } = fixture();
  game.playerOne.virtualEnergy = 1;
  assert.deepEqual(abilities.getAbilityOptions(game, cards, 'p1', false).globals[0].command.energyDieIds, ['energy']);
  await abilities.executeAbility('g1', { kind: 'global', cardId: 'act', abilityIndex: 2, energyDieIds: ['energy'] });
  await abilities.executeAbility('g1', { kind: 'action', dieId: 'ready' });
  await abilities.executeAbility('g1', { kind: 'foresight', dieId: 'ready' });
  assert.deepEqual(calls, [['global', 'g1', 'act', 2, ['energy']], ['action', 'g1', 'ready'], ['foresight', 'g1', 'ready']]);
});

// Basic Action cards are visible even after their final die was purchased.
test('shared Basic Action stock includes opponent cards and sold-out cards', () => {
  const { game, cards } = fixture();
  cards.set('second', { id: 'second', isAction: true, name: 'Second', purchaseCost: 4, energyTypes: [] });
  game.dice.push({ id: 'other', cardId: 'second', controllerId: 'p2', zone: 'Unpurchased' });
  assert.deepEqual(abilities.basicActionStock(game, cards).map(c => [c.card.id, c.dice.length]), [['act', 1], ['second', 1]]);
  game.dice.find(d => d.id === 'community').zone = 'UsedPile';
  assert.deepEqual(abilities.basicActionStock(game, cards).map(c => [c.card.id, c.dice.length]), [['act', 0], ['second', 1]]);
});

test('either player can purchase either community Basic Action stock', () => {
  const { game, cards } = fixture();
  cards.set('second', { id: 'second', name: 'Second', isAction: true, purchaseCost: 4, energyTypes: [] });
  game.dice.push({ id: 'other-community', cardId: 'second', controllerId: 'p1', zone: 'Unpurchased' });
  assert.deepEqual(abilities.purchasableDiceFor(game, cards, 'p1').map(d => d.id), ['community', 'own', 'other-community']);
  assert.deepEqual(abilities.purchasableDiceFor(game, cards, 'p2').map(d => d.id), ['community', 'opponent', 'other-community']);
});


test('Basic Action use is available only with priority in Main and Attack action windows', async () => {
  const { game, cards } = fixture();
  const action = () => abilities.getAbilityOptions(game, cards, 'p1', false).actionDice[0];
  assert.deepEqual(action().command, { kind: 'action', dieId: 'ready' });
  game.currentStepId = 'action-global-window';
  assert.deepEqual(action().command, { kind: 'action', dieId: 'ready' });
  game.currentStepId = 'roll-and-reroll';
  assert.equal(action().command, null);
  game.currentStepId = 'main';
  game.priorityPlayerId = 'p2';
  assert.equal(action().command, null);
});
