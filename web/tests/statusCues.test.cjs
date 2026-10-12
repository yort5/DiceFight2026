const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');

const code = ts.transpileModule(fs.readFileSync(path.join(__dirname, '../src/dicekingdom/statusCues.ts'), 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const mod = { exports: {} };
new Function('require', 'module', 'exports', code)((id) => {
  assert.equal(id, './dieFaces');
  return { printedStats: () => null };
}, mod, mod.exports);
const { tileCues, explainRows } = mod.exports;

test('targeting protection shows the common cue and the server-defined restriction', () => {
  const description = "Can't be targeted by either player's action dice or Global abilities.";
  const die = { zone: 'FieldZone', effectiveAttack: 1, effectiveDefense: 1, damage: 0,
    statuses: [{ kind: 'targetProtected', source: 'Saiga', duration: 'turn', description }] };
  assert.equal(tileCues(die, new Map(), true).granted, 'TP');
  assert.equal(tileCues(die, new Map(), false).word, 'TARGET PROTECTED');
  for (const mine of [true, false]) {
    const [row] = explainRows(die, mine);
    assert.equal(row.duration, 'This turn');
    assert.ok(row.body.includes(description));
    assert.ok(row.body.includes('Saiga'));
    assert.ok(!row.body.includes('Combat damage'));
  }
});

test('combat protection keeps its own distinct explanation and cue', () => {
  const die = { zone: 'FieldZone', effectiveAttack: 1, effectiveDefense: 1, damage: 0,
    statuses: [{ kind: 'protected', source: 'Armadillo', duration: 'turn' }] };
  assert.equal(tileCues(die, new Map(), true).granted, 'SH');
  assert.match(explainRows(die, true)[0].body, /Combat damage/);
});
