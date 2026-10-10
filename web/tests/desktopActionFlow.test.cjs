const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const src = path.join(__dirname, '..', 'src', 'dicekingdom');
const page = (name) => fs.readFileSync(path.join(src, name), 'utf8');

test('bot action handlers on both layouts compare against latest game state (not stale interval closure)', () => {
  for (const filename of ['DiceKingdomPage.tsx', 'DiceKingdomMobilePage.tsx']) {
    const text = page(filename);
    const start = text.indexOf('async function runBot(');
    const end = text.indexOf('async function performBotAction(', start);
    assert.ok(start > -1 && end > start, `${filename}: bot handlers found`);
    const runBot = text.slice(start, end);
    assert.match(runBot, /const previous = gameRef\.current;/, `${filename}: latest comparison state`);
    assert.doesNotMatch(runBot, /const previous = game;/, `${filename}: no stale state capture`);
  }
});

test('desktop action face selects the die and provides Use through the shared ability dispatcher', () => {
  const desktop = page('DiceKingdomPage.tsx');
  assert.match(desktop, /step === "main" \|\| step === "action-global-window"\) && die\.zone === "ReservePool" && die\.isActionFace/);
  assert.match(desktop, /actionPrompt=\{zoneName === "ReservePool"/);
  assert.match(desktop, /if \(command\) doAbility\(command\);/);
  assert.match(desktop, /\{actionPrompt && onStartAction && \(/);
});


test('desktop can switch directly from a selected Action die to a creature or a different Action die', () => {
  const desktop = page('DiceKingdomPage.tsx');
  const start = desktop.indexOf('function reservePoolClickable(');
  const end = desktop.indexOf('function renderBoard(', start);
  assert.ok(start !== -1 && end > start);
  const eligible = desktop.slice(start, end);
  assert.match(eligible, /primary\?\.zone === "ReservePool" && primary\.isActionFace/);
  assert.match(eligible, /d\.zone === "ReservePool" && rolled\(d\) &&\s*\(d\.effectiveAttack !== null \|\| !!d\.isActionFace\)/);
  assert.doesNotMatch(eligible, /primary\?\.zone === "ReservePool" && primary\.isActionFace\) return !!d\.isActionFace/);

  // Both kinds of die use the direct-replacement handler, not secondary payment.
  const selectStart = desktop.indexOf('function selectReserveDie(');
  const selectEnd = desktop.indexOf('function toggleDie(', selectStart);
  const select = desktop.slice(selectStart, selectEnd);
  assert.match(select, /die\.isActionFace[\s\S]*setSelection\(/);
  assert.match(select, /die\.effectiveAttack !== null[\s\S]*setSelection\(/);
  assert.ok(select.indexOf('isReservePaymentDie(') < select.indexOf('die.isActionFace'),
    'active payment keeps priority over switching dice');
});
