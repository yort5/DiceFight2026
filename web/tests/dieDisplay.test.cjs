const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');
const src = path.join(__dirname, '..', 'src', 'dicekingdom');

function loadTs(name, imports = {}) {
  const output = ts.transpileModule(fs.readFileSync(path.join(src, name), 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const mod = { exports: {} };
  new Function('require', 'module', 'exports', output)(id => {
    if (!(id in imports)) throw new Error(`Unexpected import: ${id}`);
    return imports[id];
  }, mod, mod.exports);
  return mod.exports;
}

const bot = loadTs('bot.ts');
const display = loadTs('dieDisplay.ts', { './bot': bot });
const cards = new Map([
  ['Pangolin', { energyTypes: ['Shell'] }],
  ['Action', { isAction: true, energyTypes: [], dieEnergyType: 'Eye' }],
]);
const die = (extras = {}) => ({ id: 'p1-TardigradeClaw-1', cardId: null,
  isTardigrade: true, zone: 'ReservePool', level: 2,
  effectiveAttack: 1, effectiveDefense: 1, energySymbolId: 'Wild', energyAmount: 1,
  ...extras });

test('identity badges use the physical Tardigrade type, including Wild stat faces and no face', () => {
  for (const type of ['Claw', 'Shell', 'Wing', 'Eye']) {
    const d = die({ id: `teamA-Tardigrade${type}-3` });
    assert.equal(display.dieEnergyType(d, cards), type);
    assert.equal(display.dieEnergyType({ ...d, level: null, energySymbolId: null, energyAmount: 0 }, cards), type);
  }
});

test('character and action identity badges come from the card, not stale rolled energy', () => {
  assert.equal(display.dieEnergyType(die({ cardId: 'Pangolin', isTardigrade: false }), cards), 'Shell');
  assert.equal(display.dieEnergyType(die({ cardId: 'Action', isTardigrade: false }), cards), 'Eye');
  assert.equal(display.dieEnergyType(die({ id: 'unknown' }), cards), 'Wild');
});

test('piles and unrolled tray dice never expose retained stats or energy faces', () => {
  for (const zone of ['Bag', 'UsedPile', 'OutOfPlay', 'PrepArea', 'DiceFromBag', 'DiceFromPrep', 'Unpurchased']) {
    assert.equal(display.displayModeFor(die({ zone })), '2d', zone);
  }
  assert.equal(display.displayModeFor(die({ level: null, effectiveAttack: null,
    effectiveDefense: null, energySymbolId: null, energyAmount: 0 })), '2d');
});

test('reserve and in-play zones show rolled creature, energy and action faces in 3D', () => {
  for (const zone of ['ReservePool', 'FieldZone', 'AttackZone', 'Intimidated']) {
    assert.equal(display.displayModeFor(die({ zone })), '3d');
    assert.equal(display.displayModeFor(die({ zone, level: null, effectiveAttack: null,
      effectiveDefense: null, energySymbolId: 'Shell', energyAmount: 2 })), '3d');
    assert.equal(display.displayModeFor(die({ zone, level: null, effectiveAttack: null,
      effectiveDefense: null, energySymbolId: null, energyAmount: 0, isActionFace: true })), '3d');
  }
});

test('stack keys ignore old faces but keep different physical energy types separate', () => {
  assert.equal(display.dieIdentityKey(die(), cards), display.dieIdentityKey(die({ id: 'p1-TardigradeClaw-2', level: 3, energySymbolId: null }), cards));
  assert.notEqual(display.dieIdentityKey(die(), cards), display.dieIdentityKey(die({ id: 'p1-TardigradeShell-1' }), cards));
});

const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');
const renderImports = { 'react': React, 'react/jsx-runtime': require('react/jsx-runtime'),
  './bot': bot, './dieDisplay': display,
  './assets/wolf-champion.jpg': 'wolf.jpg', './assets/tardigrade-sketch.jpg': 'tardigrade.jpg',
  './assets/barn-swallow-duck.jpg': 'swallow.jpg' };
for (const name of ['icons', 'dieFaces', 'statusCues', 'DieCube', 'SpinFlash', 'Die2DTile', 'DieVisual']) {
  renderImports[`./${name}`] = loadTs(`${name}.${['dieFaces', 'statusCues'].includes(name) ? 'ts' : 'tsx'}`, renderImports);
}
const render = (d, extras = {}) => renderToStaticMarkup(React.createElement(renderImports['./DieVisual'].DieVisual,
  { die: d, cardsById: cards, size: 36, ...extras }));

test('side tiles draw exactly one matching energy badge, independent of stack count or stale face', () => {
  for (const zone of ['Bag', 'UsedPile', 'PrepArea', 'OutOfPlay']) {
    const html = render(die({ zone }), { count: 4 });
    assert.equal((html.match(/class="energy-badge"/g) ?? []).length, 1, zone);
    assert.match(html, /data-energy-type="Claw"/);
    assert.match(html, /×4/);
    assert.doesNotMatch(html, /die-cube-face|die-cube-attack/);
  }
});

test('unrolled tray tiles do not receive the new side-zone energy badge', () => {
  for (const zone of ['DiceFromBag', 'DiceFromPrep']) {
    const html = render(die({ zone }));
    assert.match(html, /data-display-mode="2d"/);
    assert.doesNotMatch(html, /energy-badge|dk-die-type-badge/);
  }
});

test('3D dice keep rolled energy amounts, without adding the identity badge to cube faces', () => {
  const html = render(die({ energySymbolId: 'Shell', energyAmount: 2 }));
  assert.match(html, /data-display-mode="3d"/);
  assert.equal((html.match(/class="energy-badge"/g) ?? []).length, 2);
  assert.doesNotMatch(html, /dk-die-type-badge|dk-die-2d/);
  const noEnergy = render(die({ energySymbolId: null, energyAmount: 0 }));
  assert.doesNotMatch(noEnergy, /energy-badge/);
});
