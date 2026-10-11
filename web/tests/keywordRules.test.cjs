const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');
const source = fs.readFileSync(path.join(__dirname, '../src/keywordRules.ts'), 'utf8');
const output = ts.transpileModule(source, {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020 },
}).outputText;
const mod = { exports: {} };
new Function('module', 'exports', output)(mod, mod.exports);
const { KEYWORD_RULES, splitKeywordText } = mod.exports;

test('every keyword declared in both catalogs has a rules explanation', () => {
  const repo = path.join(__dirname, '../..');
  const bulk = JSON.parse(fs.readFileSync(path.join(repo, 'src/DiceFight.Engine/Data/BulkCards.json'), 'utf8'));
  const keywords = new Set(bulk.flatMap(card => card.keywords));
  const data = path.join(repo, 'src/DiceFight.V2/Data');
  for (const file of fs.readdirSync(data).filter(file => file.endsWith('.cs'))) {
    const text = fs.readFileSync(path.join(data, file), 'utf8');
    for (const match of text.matchAll(/Keywords:\s*\[([^\]]*)\]/g)) {
      for (const name of match[1].matchAll(/"([^"]+)"/g)) keywords.add(name[1]);
    }
  }
  for (const keyword of keywords) {
    const parts = splitKeywordText(keyword);
    assert.equal(parts.length, 1, keyword);
    assert.ok(parts[0].explanation?.length > 30, keyword);
  }
});

test('numbered and multi-word keywords retain their printed parameters and punctuation', () => {
  const text = 'Attune 2. Fabricate 2-5; Breath Weapon X. On Field: grant Energy Drain 3.';
  const parts = splitKeywordText(text);
  assert.equal(parts.map(part => part.text).join(''), text);
  assert.deepEqual(parts.filter(part => part.keyword).map(part => [part.text, part.keyword]), [
    ['Attune 2', 'Attune'], ['Fabricate 2-5', 'Fabricate'], ['Breath Weapon X', 'Breath Weapon'],
    ['On Field', 'On Field'], ['Energy Drain 3', 'Energy Drain'],
  ]);
});

test('matches whole words, handles case and aliases, and is stable across repeated renders', () => {
  const text = 'Faster allies are not keywords. OVERCRUSH. When fielded, gain Fast.';
  for (let i = 0; i < 3; i++) {
    assert.deepEqual(splitKeywordText(text).filter(part => part.keyword).map(part => part.keyword),
      ['Overcrush', 'On Field', 'Fast']);
  }
  assert.deepEqual(splitKeywordText(''), []);
});

test('every rules entry can be reached from its canonical printed name', () => {
  for (const [keyword, explanation] of Object.entries(KEYWORD_RULES)) {
    assert.deepEqual(splitKeywordText(keyword), [{ text: keyword, keyword, explanation }]);
  }
});
