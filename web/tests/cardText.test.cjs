const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const ts = require('typescript');
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');

function render(text) {
  const source = fs.readFileSync(path.join(__dirname, '../src/CardText.tsx'), 'utf8');
  const js = ts.transpileModule(source, { compilerOptions: {
    module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2020, jsx: ts.JsxEmit.ReactJSX,
  }}).outputText;
  const energy = Object.fromEntries(['fist', 'bolt', 'mask', 'shield'].map(kind => [kind, { label: kind }]));
  const exports = {};
  vm.runInNewContext(js, { exports, require: id => {
    if (id === 'react/jsx-runtime') return require(id);
    if (id === './GameIcon') return { Icon: ({ icon }) => React.createElement('i', { 'data-symbol': icon.label }) };
    if (id === './KeywordPreview') return { KeywordText: ({ text }) => text };
    if (id === './gameIcons') return { ENERGY_ICONS: energy, GENERIC_ENERGY_ICONS: { 1: { label: 'generic-1' } },
      SIDEKICK_ICON: { label: 'sidekick' }, SPLIT_ENERGY_ICONS: {}, WILD_ENERGY_ICON: { label: 'wild' } };
    if (id === './dicekingdom/icons') return {
      ENERGY_ICONS: Object.fromEntries(['Claw', 'Wing', 'Eye', 'Shell'].map(kind => [kind,
        () => React.createElement('svg', { 'data-symbol': kind })])),
      TardigradeIcon: () => React.createElement('svg', { 'data-symbol': 'Tardigrade' }),
    };
    throw new Error(`Unexpected dependency: ${id}`);
  }});
  return renderToStaticMarkup(React.createElement(exports.CardText, { text }));
}

test('Dice Kingdom rules render all four energy symbols and Tardigrades without turning a life cost into energy', () => {
  const html = render('Pay 1 Claw. Pay Wing. Spend Eye energy. Pay Shell and 1 life. Prep a Tardigrade.');
  for (const kind of ['Claw', 'Wing', 'Eye', 'Shell', 'Tardigrade']) assert.ok(html.includes(`data-symbol="${kind}"`));
  assert.ok(html.includes('1 life'));
  assert.ok(!html.includes('data-symbol="generic-1"'));
});

test('Character names containing classic or Kingdom energy words stay literal while actual energy renders', () => {
  const html = render('Iron Fist, Black Bolt, Eye of Agamotto, Tiger Claw and Sky Claw. Global: Pay Fist.');
  for (const name of ['Iron Fist', 'Black Bolt', 'Eye of Agamotto', 'Tiger Claw', 'Sky Claw']) assert.ok(html.includes(name));
  assert.equal((html.match(/data-symbol="fist"/g) || []).length, 1);
  assert.ok(!html.includes('data-symbol="Claw"'));
  assert.ok(!html.includes('data-symbol="Eye"'));
});
