const { chromium } = require('playwright');
const { checkA11y, injectAxe } = require('@axe-core/playwright');
const path = require('path');
const fs   = require('fs');

const OUT  = path.join(__dirname, 'screenshots');
const BASE = 'http://localhost:4201';

function printResult(label, violations) {
  console.log(`\n${'─'.repeat(60)}`);
  console.log(`STATE: ${label}`);
  console.log(`${'─'.repeat(60)}`);
  if (violations.length === 0) {
    console.log('✅  0 violations');
  } else {
    violations.forEach((v, i) => {
      console.log(`\n[${i + 1}] ${v.id} — ${v.impact?.toUpperCase()} — ${v.description}`);
      console.log(`    Help: ${v.helpUrl}`);
      v.nodes.slice(0, 2).forEach(n => {
        console.log(`    Node: ${n.html.slice(0, 120)}`);
        console.log(`    Fix:  ${n.failureSummary?.split('\n')[0]}`);
      });
    });
  }
  return violations.length;
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page    = await browser.newPage();
  await page.setViewportSize({ width: 1280, height: 800 });

  const summary = [];

  // ── State 1: Empty (homepage, no form open) ──────────────────────────
  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.waitForTimeout(1000);
  await injectAxe(page);
  const r1 = await page.evaluate(() =>
    window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'best-practice'] })
  );
  const c1 = printResult('Empty — homepage, no form', r1.violations);
  summary.push({ state: 'Empty', violations: c1 });
  await page.screenshot({ path: `${OUT}/axe-01-empty.png` });

  // ── State 2: Login form open ─────────────────────────────────────────
  await page.click('button.add-quote-btn');
  await page.waitForTimeout(600);
  await injectAxe(page);
  const r2 = await page.evaluate(() =>
    window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'best-practice'] })
  );
  const c2 = printResult('Login form open', r2.violations);
  summary.push({ state: 'Login form', violations: c2 });
  await page.screenshot({ path: `${OUT}/axe-02-login-form.png` });

  // ── State 3: After login — create form ───────────────────────────────
  await page.fill('#login-email', 'user@test.com');
  await page.fill('#login-password', 'password123');
  await page.click('button[type="submit"]');
  await page.waitForTimeout(1500);
  await injectAxe(page);
  const r3 = await page.evaluate(() =>
    window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'best-practice'] })
  );
  const c3 = printResult('Create-quote form (clean)', r3.violations);
  summary.push({ state: 'Create form (clean)', violations: c3 });
  await page.screenshot({ path: `${OUT}/axe-03-create-form.png` });

  // ── State 4: Invalid — submit empty to trigger all errors ────────────
  await page.fill('#author', '');
  await page.fill('#text', '');
  await page.click('button[type="submit"]');
  await page.waitForTimeout(400);
  await injectAxe(page);
  const r4 = await page.evaluate(() =>
    window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'best-practice'] })
  );
  const c4 = printResult('Invalid — validation errors visible', r4.violations);
  summary.push({ state: 'Invalid (errors shown)', violations: c4 });
  await page.screenshot({ path: `${OUT}/axe-04-invalid.png` });

  // ── State 5: Server error ─────────────────────────────────────────────
  await page.route('**/api/quotes', route =>
    route.fulfill({ status: 500, body: JSON.stringify({ title: 'Internal Server Error' }) })
  );
  await page.fill('#author', 'Axe Test');
  await page.fill('#text', 'Testing accessibility in error state.');
  await page.click('button[type="submit"]');
  await page.waitForTimeout(800);
  await injectAxe(page);
  const r5 = await page.evaluate(() =>
    window.axe.run(document, { runOnly: ['wcag2a', 'wcag2aa', 'best-practice'] })
  );
  const c5 = printResult('Server-error banner visible', r5.violations);
  summary.push({ state: 'Server-error', violations: c5 });
  await page.screenshot({ path: `${OUT}/axe-05-server-error.png` });
  await page.unroute('**/api/quotes');

  // ── Summary ───────────────────────────────────────────────────────────
  console.log(`\n${'═'.repeat(60)}`);
  console.log('AXE SUMMARY — WCAG 2.0 A/AA + best-practice');
  console.log('═'.repeat(60));
  summary.forEach(s => {
    const icon = s.violations === 0 ? '✅' : '❌';
    console.log(`${icon}  ${s.state.padEnd(30)} ${s.violations} violation(s)`);
  });
  const total = summary.reduce((a, s) => a + s.violations, 0);
  console.log(`${'─'.repeat(60)}`);
  console.log(`   TOTAL: ${total} violation(s) across all states`);

  // Save summary JSON
  fs.writeFileSync(
    path.join(__dirname, 'screenshots', 'axe-summary.json'),
    JSON.stringify({ tested: new Date().toISOString(), states: summary, totalViolations: total }, null, 2)
  );
  console.log('\nScreenshots + axe-summary.json saved to screenshots/');

  await browser.close();
  process.exit(total > 0 ? 1 : 0);
})();
