const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');
const source = path.join(__dirname, '..', 'src', 'dicekingdom', 'selectedPayment.ts');
const js = ts.transpileModule(fs.readFileSync(source, 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText;
const moduleObj = { exports: {} };
new Function('require', 'module', 'exports', js)((id) => { throw Error(`Unexpected import ${id}`); }, moduleObj, moduleObj.exports);
const { evaluateSelectedPayment } = moduleObj.exports;
const energy = (type, amount = 1) => ({ energySymbolId: type, energyAmount: amount });

test('purchase needs typed energy (generic alone does not match)', () => {
  assert.deepEqual(evaluateSelectedPayment([energy('Generic', 3)], 3, ['Claw']), { ready: false, typeMissing: true });
  assert.equal(evaluateSelectedPayment([energy('Generic', 2), energy('Claw')], 3, ['Claw']).ready, true);
  assert.equal(evaluateSelectedPayment([energy('Wild', 1), energy('Generic', 2)], 3, ['Claw']).ready, true);
});

test('fielding accepts any energy and virtual energy', () => {
  assert.equal(evaluateSelectedPayment([energy('Shell', 1)], 2, [], 1).ready, true);
  assert.equal(evaluateSelectedPayment([], 2, [], 2).ready, true);
  assert.equal(evaluateSelectedPayment([], 2, [], 1).ready, false);
});

test('virtual energy cannot satisfy a printed energy type', () => {
  assert.equal(evaluateSelectedPayment([], 2, ['Wing'], 2).ready, false);
  assert.equal(evaluateSelectedPayment([energy('Wing')], 2, ['Wing'], 1).ready, true);
});

test('matches require the typed die to be actually consumed by the backend', () => {
  assert.equal(evaluateSelectedPayment([energy('Generic', 2), energy('Claw', 1)], 2, ['Claw']).ready, false);
  assert.equal(evaluateSelectedPayment([energy('Generic', 2), energy('Claw', 1)], 2, ['Claw']).typeMissing, true);
  assert.equal(evaluateSelectedPayment([energy('Claw', 1), energy('Generic', 2)], 2, ['Claw']).ready, true);
});

test('zero-cost actions are immediately payable', () => {
  assert.equal(evaluateSelectedPayment([], 0, ['Wing']).ready, true);
});
