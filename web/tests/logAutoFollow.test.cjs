const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('../node_modules/typescript');

function makeLog() {
  const effects = [];
  const react = {
    useRef: (value) => ({ current: value }),
    useEffect: (effect) => { effects.push({ kind: 'cleanup', effect }); },
    useLayoutEffect: (effect) => { effects.push({ kind: 'layout', effect }); },
  };
  const source = fs.readFileSync(path.join(__dirname, '../src/dicekingdom/useLogAutoFollow.ts'), 'utf8');
  const code = ts.transpileModule(source, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  }).outputText;
  const mod = { exports: {} };
  new Function('require', 'module', 'exports', code)(
    (id) => { assert.equal(id, 'react'); return react; }, mod, mod.exports,
  );
  const el = { scrollHeight: 700, clientHeight: 180 };
  let position = 0;
  Object.defineProperty(el, 'scrollTop', {
    get() { return position; },
    set(value) { position = Math.max(0, Math.min(value, el.scrollHeight - el.clientHeight)); },
  });
  const handlers = mod.exports.useLogAutoFollow({ current: el }, 1);
  const followNewEntry = () => effects.find((e) => e.kind === 'layout').effect();
  followNewEntry();
  return { el, handlers, followNewEntry };
}

test('new entries follow the bottom until the user scrolls back', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const { el, handlers, followNewEntry } = makeLog();
  assert.equal(el.scrollTop, 520);
  el.scrollHeight += 100;
  followNewEntry();
  assert.equal(el.scrollTop, 620);

  el.scrollTop = 300;
  handlers.onScroll();
  el.scrollHeight += 100;
  followNewEntry();
  assert.equal(el.scrollTop, 300, 'new messages do not force the user to the bottom');
  t.mock.timers.tick(4999);
  assert.equal(el.scrollTop, 300);
  t.mock.timers.tick(1);
  assert.equal(el.scrollTop, 720, 'following resumes after 5 seconds of inactivity');
});

test('each further scroll interaction restarts the five-second pause', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const { el, handlers, followNewEntry } = makeLog();
  el.scrollTop = 200;
  handlers.onWheel();
  t.mock.timers.tick(4000);
  handlers.onKeyDown({ key: 'PageUp' });
  el.scrollHeight += 100;
  followNewEntry();
  t.mock.timers.tick(4999);
  assert.equal(el.scrollTop, 200);
  t.mock.timers.tick(1);
  assert.equal(el.scrollTop, 620);
});

test('programmatic tail-following does not pause itself', (t) => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const { el, handlers, followNewEntry } = makeLog();
  handlers.onScroll();
  el.scrollHeight += 100;
  followNewEntry();
  assert.equal(el.scrollTop, 620);
});
