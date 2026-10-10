const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');
const source = path.join(__dirname, '..', 'src', 'dicekingdom', 'reservePayment.ts');
const js = ts.transpileModule(fs.readFileSync(source, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText;
const moduleObj = { exports: {} };
new Function('require', 'module', 'exports', js)((id) => { throw Error(`Unexpected import ${id}`); }, moduleObj, moduleObj.exports);
const { isReservePaymentDie } = moduleObj.exports;
const die = (id, energyAmount, zone = 'ReservePool') => ({ id, zone, energyAmount });

test('character face with energy can pay for a character purchase', () => {
  assert.equal(isReservePaymentDie(die('character-face', 1), die('roster-card', 0, 'Unpurchased'), null), true);
});

test('character face with energy can pay another creature fielding cost after Field clicked', () => {
  assert.equal(isReservePaymentDie(die('character-face', 2), die('field-target', 0), 'field-target'), true);
});

test('fielding flow is not entered as a payment workflow until Field is clicked', () => {
  assert.equal(isReservePaymentDie(die('character-face', 1), die('field-target', 0), null), false);
});

test('creature without energy cannot be selected as payment', () => {
  assert.equal(isReservePaymentDie(die('creature-only', 0), die('roster-card', 0, 'Unpurchased'), null), false);
});

test('the creature being fielded cannot pay its own cost', () => {
  assert.equal(isReservePaymentDie(die('field-target', 1), die('field-target', 1), 'field-target'), false);
});

test('dice outside Reserve Pool cannot be used as payment', () => {
  assert.equal(isReservePaymentDie(die('character-face', 1, 'FieldZone'), die('roster-card', 0, 'Unpurchased'), null), false);
});

test('payment may be deselected by clicking the same eligible energy die again', () => {
  // The desktop click handler uses toggleDie for any such click;
  // it removes an already-selected secondary rather than replacing the primary.
  assert.equal(isReservePaymentDie(die('character-face', 1), die('roster-card', 0, 'Unpurchased'), null), true);
});
