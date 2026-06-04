const { chromium } = require('playwright');
const path = require('path');

const OUT = path.join(__dirname, 'screenshots');
const BASE = 'http://localhost:4201';

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.setViewportSize({ width: 1280, height: 800 });

  // ── 1. Homepage — list visible, no form ─────────────────────────────
  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${OUT}/01-homepage-list.png`, fullPage: false });
  console.log('✓ 01-homepage-list');

  // ── 2. Click Add Quote — login form shown ────────────────────────────
  await page.click('button.add-quote-btn');
  await page.waitForTimeout(600);
  await page.screenshot({ path: `${OUT}/02-add-quote-login-form.png`, fullPage: false });
  console.log('✓ 02-add-quote-login-form');

  // ── 3. Login with seeded credentials ────────────────────────────────
  await page.fill('#login-email', 'user@test.com');
  await page.fill('#login-password', 'password123');
  await page.click('button[type="submit"]');
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${OUT}/03-after-login-create-form.png`, fullPage: false });
  console.log('✓ 03-after-login-create-form');

  // ── 4. Empty submit — validation errors shown ────────────────────────
  // Clear pre-filled fields first
  await page.fill('#author', '');
  await page.fill('#text', '');
  await page.click('button[type="submit"]');
  await page.waitForTimeout(400);
  await page.screenshot({ path: `${OUT}/04-validation-errors.png`, fullPage: false });
  console.log('✓ 04-validation-errors');

  // ── 5. Fill in valid data ────────────────────────────────────────────
  await page.fill('#author', 'Amey');
  await page.fill('#text', 'There is no way but forward.');
  await page.screenshot({ path: `${OUT}/05-form-filled.png`, fullPage: false });
  console.log('✓ 05-form-filled');

  // ── 6. Submit — capture "Saving…" button mid-flight ──────────────────
  // Slow down network to capture in-flight state
  await page.route('**/api/quotes', async route => {
    await page.waitForTimeout(1200); // hold response so we can screenshot
    await route.continue();
  });
  await page.click('button[type="submit"]');
  await page.waitForTimeout(600);
  await page.screenshot({ path: `${OUT}/06-submitting-saving.png`, fullPage: false });
  console.log('✓ 06-submitting-saving');

  // ── 7. Wait for success state ────────────────────────────────────────
  await page.unroute('**/api/quotes');
  await page.waitForTimeout(2000);
  await page.screenshot({ path: `${OUT}/07-success.png`, fullPage: false });
  console.log('✓ 07-success');

  // ── 8. Re-open form for server-error test ────────────────────────────
  await page.waitForTimeout(2500); // wait for auto-close
  await page.click('button.add-quote-btn');
  await page.waitForTimeout(600);

  // Force a 500 to simulate server error
  await page.route('**/api/quotes', route =>
    route.fulfill({ status: 500, body: JSON.stringify({ title: 'Internal Server Error', status: 500 }) })
  );
  await page.fill('#author', 'Test Author');
  await page.fill('#text', 'This will fail on the server.');
  await page.click('button[type="submit"]');
  await page.waitForTimeout(1000);
  await page.screenshot({ path: `${OUT}/08-server-error.png`, fullPage: false });
  console.log('✓ 08-server-error');

  // ── 9. Quote detail drawer — click a quote from the list ─────────────
  await page.unroute('**/api/quotes');
  await page.keyboard.press('Escape');
  await page.click('button.add-quote-btn'); // close form
  await page.waitForTimeout(400);
  const firstQuote = page.locator('.quote-item').first();
  await firstQuote.click();
  await page.waitForTimeout(1000);
  await page.screenshot({ path: `${OUT}/09-quote-detail-drawer.png`, fullPage: false });
  console.log('✓ 09-quote-detail-drawer');

  await browser.close();
  console.log('\nAll screenshots saved to:', OUT);
})();
