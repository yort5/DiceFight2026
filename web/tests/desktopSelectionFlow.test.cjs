const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const desktop = fs.readFileSync(path.join(__dirname, '..', 'src', 'dicekingdom', 'DiceKingdomPage.tsx'), 'utf8');

test('desktop purchase and fielding share a single explicit payment stage', () => {
  assert.match(desktop, /const \[paymentForId, setPaymentForId\] = useState/);
  assert.match(desktop, /fieldButtonLabel=\{paymentForId === d\.id \? "Confirm" : "Field"\}/);
  assert.match(desktop, /label: picked && paymentForId === dieId \? "Confirm" : "Purchase"/);
  assert.match(desktop, /setPaymentForId\(dieId\)/);
  assert.match(desktop, /setPaymentForId\(d\.id\)/);
});

test('main bar no longer contains the payment submit action; task row explains payment', () => {
  const start = desktop.indexOf('{step === "main" && (', desktop.indexOf('const stepContent ='));
  const end = desktop.indexOf('{step === "select-attackers"', start);
  assert.ok(start > -1 && end > start);
  const mainControls = desktop.slice(start, end);
  assert.doesNotMatch(mainControls, /action\.run/);
  assert.match(desktop, /Select payment — cost/);
  assert.match(desktop, /paymentReady/);
});

test('purchase popover remains open for payment and roster selection replaces field selection', () => {
  assert.match(desktop, /paymentForId && gameRef\.current\?\.dice\.some/);
  assert.match(desktop, /setSelection\(\(previous\) => previous\.primary === dieId \? EMPTY_SELECTION : \{ primary: dieId, secondary: \[\] \}\)/);
  assert.match(desktop, /setOpenCardId\(\(current\) => current === openKey \? null : openKey\)/);
});
