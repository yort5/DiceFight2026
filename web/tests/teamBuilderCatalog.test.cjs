const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');

// Exercise the adapter in isolation: no browser, server, or duplicated
// Champion roster data is required for the Team Builder catalog.
function loadAdapter(kingdomCards, classicCards = []) {
  const source = fs.readFileSync(path.join(__dirname, '../src/teamBuilderCatalog.ts'), 'utf8');
  const javascript = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
  }).outputText;
  const exports = {};
  const requireMock = (id) => {
    if (id === './api') return { api: { getCards: async () => classicCards } };
    if (id === './dicekingdom/api') return { api: { getCards: async () => kingdomCards } };
    throw new Error(`Unexpected dependency: ${id}`);
  };
  vm.runInNewContext(javascript, { exports, require: requireMock });
  return exports;
}

const card = (props = {}) => ({
  id: 'TestCreature', name: 'Test Creature', subtitle: null,
  purchaseCost: 4, energyTypes: ['Claw'], dieLimit: 4,
  levels: [ { fieldingCost: 1, attack: 2, defense: 3 } ],
  rawText: 'Awaken: deal 2 damage.', keywords: ['Awaken'],
  isAction: false, actionText: null, global: null,
  ...props,
});

test('Dice Kingdom catalog is the internal default; unrostered catalog cards are retained', async () => {
  const all = [card(), card({ id: 'CatalogOnly', name: 'Catalog-only character' })];
  const adapter = loadAdapter(all, [ { id: 'LegacyCard' } ]);
  assert.equal(adapter.USE_DICE_KINGDOM_CATALOG, true);
  const loaded = await adapter.loadTeamBuilderCards();
  assert.deepEqual(Array.from(loaded, (c) => c.id), ['TestCreature', 'CatalogOnly']);
  assert.equal(loaded[1].isImplemented, true);
  assert.equal(loaded[1].set, 'DK');
});

test('V2 character stats and keyword text map into the existing builder model', () => {
  const adapter = loadAdapter([]);
  const mapped = adapter.adaptKingdomCard(card());
  assert.equal(mapped.type, 'Character');
  assert.equal(mapped.purchaseCost, 4);
  assert.equal(mapped.dieLimit, 4);
  assert.deepEqual(Array.from(mapped.energyTypes), ['Claw']);
  assert.equal(mapped.rawText, 'Awaken: deal 2 damage.');
  assert.equal(mapped.levels[0].attack, 2);
  assert.equal(mapped.levels[0].burstStars, null);
  assert.equal(mapped.isImplemented, true);
});

test('Basic Action cards are mapped as Basic Actions and retain their die limit and Global', () => {
  const adapter = loadAdapter([]);
  const mapped = adapter.adaptKingdomCard(card({
    id: 'Action', name: 'Action', levels: [], isAction: true, dieLimit: 3,
    energyTypes: [], dieEnergyType: 'Shell', rawText: '', actionText: 'Do something.',
    global: { cost: 1, energyType: 'Shell', abilityIndex: 0, text: 'Pay 1 Shell.' },
  }));
  assert.equal(mapped.type, 'BasicAction');
  assert.equal(mapped.dieLimit, 3);
  assert.equal(mapped.dieEnergyType, 'Shell');
  assert.deepEqual(Array.from(mapped.energyTypes), []);
  assert.equal(mapped.rawText, 'Do something.');
  assert.equal(mapped.globalAbilityCost.amount, 1);
  assert.equal(mapped.globalAbilityCost.requiredType, 'Shell');
});

test('Errata metadata survives catalog adaptation without becoming a game keyword', () => {
  const adapter = loadAdapter([]);
  const tagged = card({ hasErrata: true });
  const mapped = adapter.adaptKingdomCard(tagged);
  assert.equal(mapped.hasErrata, true);
  assert.deepEqual(Array.from(mapped.keywords), ['Awaken']);
  assert.equal(adapter.adaptKingdomCard(card()).hasErrata, false);
  assert.equal(adapter.adaptKingdomCard(card({ hasErrata: false })).hasErrata, false);
});

test('Registered incomplete cards preserve implementation status and original card metadata', async () => {
  const original = card({ id: 'MSW019', name: 'Beast', subtitle: 'Olympic Athleticism',
    energyTypes: ['Mask'], type: 'Character', set: 'MSW', affiliations: ['X-Men'],
    isImplemented: false, hasErrata: true,
    levels: [{ fieldingCost: 0, attack: 1, defense: 2, burst: 1 }],
  });
  const adapter = loadAdapter([card(), original]);
  const loaded = await adapter.loadTeamBuilderCards();
  assert.equal(loaded.length, 2);
  const mapped = loaded[1];
  assert.equal(mapped.id, original.id);
  assert.equal(mapped.isImplemented, false);
  assert.equal(mapped.hasErrata, true);
  assert.equal(mapped.set, 'MSW');
  assert.equal(mapped.type, 'Character');
  assert.deepEqual(Array.from(mapped.affiliations), ['X-Men']);
  assert.deepEqual(Array.from(mapped.energyTypes), ['Mask']);
  assert.equal(mapped.levels[0].burstStars, 1);
});

test('Action and Basic Action classifications remain distinct in the builder', () => {
  const mapped = loadAdapter([]).adaptKingdomCard(card({ isAction: true, type: 'Action',
    isImplemented: false, hasErrata: false, set: 'GOTG' }));
  assert.equal(mapped.type, 'Action');
  assert.equal(mapped.isImplemented, false);
  assert.equal(mapped.hasErrata, false);
});
