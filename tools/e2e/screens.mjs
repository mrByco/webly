#!/usr/bin/env node
/**
 * Every screen of the running app, in a real browser, with the console watched.
 *
 * Webly's other two harnesses answer "does the product work": `run.mjs` drives the loop underneath the C#
 * and `turn.mjs` drives a turn through it. Neither of them can see the thing that has cost this repository
 * the most: a page that is served, parses, answers 200, and is *wrong to look at*.
 *
 * Five defects in one afternoon came from opening pages by hand — a published site that had rendered without
 * its stylesheet since the first publish, a mistyped address that served Webly's own dashboard instead of the
 * site's 404, a shared contact link whose title said only "Contact", form fields shaped like pills, and a
 * deleted site that carried on serving. Every one of them had been "checked" before, by reading the HTML,
 * and the HTML was perfect. This makes that check repeatable instead of a thing somebody remembered to do.
 *
 *   node tools/e2e/screens.mjs --email you@example.com --password ... [--site nanoid] [--out .run/screens]
 *
 * It needs a Chromium and `playwright-core`, which are deliberately **not** dependencies of this repository:
 *   npm i -g playwright-core && npx playwright-core install chromium
 * or, where a browser is already installed (a CI image, this project's dev container), point at it:
 *   PLAYWRIGHT_BROWSER=/opt/pw-browsers/chromium node tools/e2e/screens.mjs --email … --password …
 *
 * It exits non-zero when a page logs an error, when a request answers 5xx, or when a screen that must have
 * content comes back empty — so it is a check rather than a screenshot album, and the album is the evidence
 * it leaves behind either way.
 */
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const index = args.indexOf(`--${name}`);

  return index >= 0 ? args[index + 1] : fallback;
};

const origin = option('origin', 'https://localhost:5000');
const email = option('email');
const password = option('password');
const site = option('site');
const out = path.resolve(option('out', '.run/screens'));

if (!email || !password) {
  process.stdout.write('Usage: node tools/e2e/screens.mjs --email <address> --password <password> [--site <nanoid>]\n');
  process.exit(2);
}

// Resolved rather than imported, so that a repository without it says what to install instead of throwing a
// module-not-found at whoever ran it.
let chromium;

try {
  ({ chromium } = createRequire(import.meta.url)('playwright-core'));
} catch {
  process.stdout.write(
    'playwright-core is not installed. It is deliberately not a dependency of this repository:\n'
    + '  npm i -g playwright-core && npx playwright-core install chromium\n');
  process.exit(2);
}

mkdirSync(out, { recursive: true });

const problems = [];
let shots = 0;

/**
 * Which screen the listeners below are currently reporting about.
 *
 * Mutable, and the listeners are attached once, because a listener per screen is a listener per screen
 * *still attached* on the next one: the first version of this reported every problem as many times as there
 * were pages left to walk, which is how a tool that is meant to make a signal legible produces noise instead.
 */
let current = 'start';

/**
 * What counts as a problem worth failing for.
 *
 * A 5xx always is, and so is anything the page itself logged — an Angular template error reaches the console
 * and nowhere else.
 *
 * **A 404 on something the page asked for is one too, and that is the whole point of this file.** The defect
 * this harness was written for was a published site whose every `/_next/…` stylesheet and chunk answered 404
 * because the build had not been told what path it was served at: the document was 200, its HTML was perfect,
 * and the page rendered as unstyled text. Nothing that reads HTML can see that. A 404 on the *document* is a
 * different matter — this walk deliberately asks for a page that is not there — so only subresources count.
 */
function watch(page) {
  page.on('pageerror', error => problems.push(`${current}: page error — ${error.message}`));

  page.on('console', message => {
    if (message.type() !== 'error') return;

    const text = message.text();

    // Browser noise that says nothing about the product: a request cancelled because the page navigated, the
    // dev server's hot-reload chatter, and the generic "failed to load resource" — which carries no URL and
    // no useful status, and whose statuses the response listener below sees properly anyway.
    if (text.includes('ERR_ABORTED')
      || text.includes('ERR_TOO_MANY_RETRIES')
      || text.includes('Failed to load resource')
      || text.includes('[vite]')) return;

    problems.push(`${current}: console — ${text.slice(0, 160)}`);
  });

  page.on('response', response => {
    const status = response.status();
    const document = response.request().resourceType() === 'document';

    if (status >= 500 || (status === 404 && !document))
      problems.push(`${current}: ${status} ${response.url().slice(0, 110)}`);
  });
}

/**
 * Panes that have quietly started scrolling sideways.
 *
 * The defect this exists for: a flex item's default minimum width is its content's, so one long version
 * summary made the editor's right-hand pane wider than the space beside the chat. The History screen's title
 * was then cut off mid-word, the diff ran off the edge, and `document.scrollWidth` stayed exactly the window's
 * width — because the clipping is what hides it. Reading the page's own width proves nothing.
 *
 * What it looks for instead is a **vertical** scroll pane that has acquired a horizontal scroll. CSS gives
 * `overflow-x` the used value `auto` as soon as `overflow-y` is not visible, so the browser reports those two
 * cases identically and only the intent tells them apart — which this app writes down, in its class names.
 * So an element that says `overflow-x-auto` is a deliberate sideways scroller and is left alone (the tab strip
 * on a phone, a block of code); one that only says `overflow-y-auto` is a column that something has made too
 * wide, and that is always a bug.
 */
async function sidewaysScroll(page) {
  return page.evaluate(() => {
    const found = [];

    for (const element of document.querySelectorAll('body *')) {
      if (element.scrollWidth <= element.clientWidth + 1) continue;

      const overflow = getComputedStyle(element).overflowX;

      if (overflow !== 'auto' && overflow !== 'scroll') continue;

      const classes = (element.className?.toString?.() ?? '');

      // Meant to scroll sideways: said so in its own class, or is code.
      if (classes.includes('overflow-x-auto') || classes.includes('overflow-x-scroll')) continue;
      if (element.closest('pre, code')) continue;

      const name = classes.trim().split(/\s+/).slice(0, 3).join('.');

      found.push(
        `${element.tagName.toLowerCase()}${name ? '.' + name : ''} scrolls sideways `
        + `(${element.scrollWidth} wide in ${element.clientWidth})`);
    }

    // The outermost is the cause; anything under it is repeating the same news.
    return found.slice(0, 2);
  });
}

async function shot(page, name, width) {
  await page.setViewportSize({ width, height: width < 700 ? 844 : 950 });
  await page.waitForTimeout(600);
  await page.screenshot({ path: path.join(out, `${name}-${width}.png`) });
  shots += 1;
}

const browser = await chromium.launch({
  executablePath: process.env.PLAYWRIGHT_BROWSER || undefined,
});

const page = await browser.newPage({ viewport: { width: 1400, height: 950 }, ignoreHTTPSErrors: true });

try {
  watch(page);
  current = 'login';
  await page.goto(`${origin}/login`, { waitUntil: 'networkidle' });
  await shot(page, 'login', 1400);

  await page.setViewportSize({ width: 1400, height: 950 });
  await page.getByPlaceholder('you@example.com').fill(email);
  await page.getByPlaceholder('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.waitForURL(url => !url.pathname.includes('login'), { timeout: 30_000 });

  // The site to walk: the one named, or the first one the account has.
  const nanoid = site ?? await page.evaluate(async base => {
    const response = await fetch(`${base}/api/sites`, { credentials: 'include' });
    const sites = await response.json();

    return sites[0]?.nanoid;
  }, origin);

  if (!nanoid) {
    process.stdout.write('This account has no sites, so there is nothing to walk. Create one first.\n');
    process.exit(2);
  }

  // Whether this site has ever been published decides what the last two screens *mean*, so it is asked
  // rather than assumed. Before this, a site nobody had published reported two failures for answering the
  // 404 it is supposed to answer — and a harness that is red when the product is right is one nobody reads.
  const published = await page.evaluate(async ({ base, id }) => {
    const response = await fetch(`${base}/api/sites/${id}`, { credentials: 'include' });

    return response.ok ? Boolean((await response.json()).summary?.publishedAt) : false;
  }, { base: origin, id: nanoid });

  const screens = [
    ['sites', `${origin}/`],
    ['editor', `${origin}/sites/${nanoid}`],
    ['history', `${origin}/sites/${nanoid}/history`],
    ['code', `${origin}/sites/${nanoid}/code`],
    ['messages', `${origin}/sites/${nanoid}/messages`],
    ['domains', `${origin}/sites/${nanoid}/domains`],
    ['settings', `${origin}/sites/${nanoid}/settings`],
    ['account', `${origin}/account`],

    // The published site, which is the half that is not Webly's app — and the half where every one of the
    // defects above was. The 404 is deliberate: what it must not be is Webly's own login page.
    ...(published
      ? [
        ['published', `${origin}/published/${nanoid}/`],
        ['published-404', `${origin}/published/${nanoid}/not-a-page/`],
      ]
      : []),
  ];

  for (const [name, url] of screens) {
    current = name;
    await page.goto(url, { waitUntil: 'networkidle' }).catch(error => problems.push(`${name}: ${error.message}`));
    await page.waitForTimeout(1200);

    const text = await page.locator('body').innerText().catch(() => '');

    if (text.trim().length < 20) problems.push(`${name}: the page is empty`);

    for (const width of [1400, 390]) {
      await shot(page, name, width);

      for (const pane of await sidewaysScroll(page))
        problems.push(`${name} @${width}: ${pane} — content is being cut off`);
    }

    process.stdout.write(`  · ${name}\n`);
  }

  // Nothing published, so the assertion is the other one — and it is worth making, because it is what a
  // *deleted* site has to answer too: a 404, never Webly's own app. A request rather than a screenshot,
  // since there is no page to look at.
  if (!published) {
    for (const suffix of ['', 'not-a-page/']) {
      const url = `${origin}/published/${nanoid}/${suffix}`;
      const response = await page.request.get(url, { failOnStatusCode: false });

      if (response.status() !== 404) problems.push(`unpublished: ${response.status()} for ${url}, expected 404`);
    }

    process.stdout.write('  · published — nothing to walk; the site answers 404, as it must\n');
  }
} finally {
  await browser.close();
}

process.stdout.write(`\n${shots} screenshots in ${out}\n`);

if (problems.length > 0) {
  process.stdout.write(`\n${problems.length} problem(s):\n${problems.map(x => `  ✗ ${x}`).join('\n')}\n`);
  process.exitCode = 1;
} else {
  process.stdout.write('No page errors, no 5xx, nothing blank.\n');
}
