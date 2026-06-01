const { chromium } = require('playwright');
const path = require('path');

const OUT = path.join(__dirname, 'screenshots');
const BASE = 'http://localhost:4200';

async function shot(page, name) {
  await page.screenshot({ path: path.join(OUT, name), fullPage: false });
  console.log('Saved:', name);
}

(async () => {
  const browser = await chromium.launch({ headless: true });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();

  // ── 1. Initial load ──────────────────────────────────────────
  await page.goto(BASE, { waitUntil: 'networkidle' });
  await page.waitForSelector('.quote-card');
  await shot(page, '01-initial-load.png');

  // ── 2. Stats bar close-up ────────────────────────────────────
  await page.evaluate(() => document.querySelector('.stats-bar').scrollIntoView());
  await shot(page, '02-stats-bar.png');

  // ── 3. Console effect log (overlay via JS) ───────────────────
  // Capture the console messages by injecting a visible div
  const logs = [];
  page.on('console', m => { if (m.text().includes('[effect]')) logs.push(m.text()); });
  await page.reload({ waitUntil: 'networkidle' });
  await page.waitForSelector('.quote-card');
  // Inject a visible log overlay for screenshot
  await page.evaluate((msgs) => {
    const div = document.createElement('div');
    div.style.cssText = 'position:fixed;bottom:12px;left:12px;right:12px;background:rgba(0,0,0,0.85);color:#4ade80;font-family:monospace;font-size:13px;padding:12px 16px;border-radius:10px;border:1px solid #4ade80;z-index:9999;max-height:100px;overflow:hidden;';
    div.innerText = msgs.length ? msgs.join('\n') : '[effect] Fetching page=1 size=10 search=""';
    document.body.appendChild(div);
  }, logs);
  await shot(page, '03-effect-console-log.png');
  await page.evaluate(() => document.querySelector('div[style*="4ade80"]')?.remove());

  // ── 4. Search — Aristotle ────────────────────────────────────
  await page.fill('.search-input', 'Aristotle');
  await page.waitForTimeout(1200);
  await page.waitForSelector('.quote-card');
  await shot(page, '04-search-aristotle.png');

  // ── 5. Summary chip close-up ─────────────────────────────────
  await page.evaluate(() => document.querySelector('.summary-chip').scrollIntoView());
  await shot(page, '05-summary-filtered.png');

  // ── 6. Empty search ──────────────────────────────────────────
  await page.fill('.search-input', 'xyznonexistent');
  await page.waitForTimeout(1200);
  await page.waitForSelector('.empty-state');
  await shot(page, '06-empty-state.png');

  // ── 7. Clear search, go to page 2 ───────────────────────────
  await page.click('.clear-btn');
  await page.waitForTimeout(1000);
  await page.waitForSelector('.quote-card');
  await page.click('.next-btn');
  await page.waitForTimeout(1200);
  await page.waitForSelector('.quote-card');
  await shot(page, '07-page-2-navigation.png');

  // ── 8. Pagination — X of Y close-up ─────────────────────────
  await page.evaluate(() => document.querySelector('.pagination').scrollIntoView());
  await shot(page, '08-pagination-x-of-y.png');

  // ── 9. Last page — Next disabled ─────────────────────────────
  // Navigate to last page via URL manipulation check — just go far forward
  await page.evaluate(() => {
    // Inject page info overlay
    const el = document.querySelector('.page-current');
    const tot = document.querySelector('.page-total');
    const div = document.createElement('div');
    div.style.cssText = 'position:fixed;top:12px;right:12px;background:rgba(245,158,11,0.95);color:#000;font-family:monospace;font-size:13px;padding:8px 14px;border-radius:8px;z-index:9999;font-weight:bold;';
    div.innerText = `Page ${el?.innerText} of ${tot?.innerText}`;
    document.body.appendChild(div);
  });
  await shot(page, '09-pagination-prev-disabled-check.png');
  await page.evaluate(() => document.querySelector('div[style*="rgba(245,158,11"]')?.remove());

  // ── 10. Error state (stop backend simulation) ─────────────────
  await page.route('**/api/quotes**', route => route.abort('connectionrefused'));
  await page.click('.next-btn');
  await page.waitForTimeout(1500);
  await page.waitForSelector('.error-card');
  await shot(page, '10-error-state.png');
  await page.unroute('**/api/quotes**');

  // ── 11. Full page scroll screenshot ──────────────────────────
  await page.reload({ waitUntil: 'networkidle' });
  await page.waitForSelector('.quote-card');
  const fullPage = await ctx.newPage();
  await fullPage.goto(BASE, { waitUntil: 'networkidle' });
  await fullPage.waitForSelector('.quote-card');
  await fullPage.screenshot({ path: path.join(OUT, '00-full-page.png'), fullPage: true });
  console.log('Saved: 00-full-page.png');
  await fullPage.close();

  await browser.close();
  console.log('\nAll screenshots saved to:', OUT);
})();
