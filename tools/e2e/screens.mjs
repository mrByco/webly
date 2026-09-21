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

/**
 * The accessibility mistakes that are worth failing a build for, and only those.
 *
 * Not an audit — a real one needs a dependency and produces a report somebody has to triage, and the whole
 * point of this file is a check that is green or red. What is here is the handful that are unambiguous, that
 * a template or a component can regress silently, and that each make the page unusable for somebody: a
 * control nothing can announce, a picture with nothing to say instead, a field with no label, and a page with
 * no heading to land on.
 *
 * `templates/next-site/AGENTS.md` tells the agent that accessibility is not optional. This is the half of
 * that sentence the product can actually check, and it checks Webly's own screens by the same rule.
 */
async function accessibility(page) {
  return page.evaluate(() => {
    const problems = [];
    const visible = element => {
      const box = element.getBoundingClientRect();

      return box.width > 0 && box.height > 0 && getComputedStyle(element).visibility !== 'hidden';
    };

    const named = element =>
      (element.textContent ?? '').trim().length > 0
      || element.getAttribute('aria-label')?.trim()
      || element.getAttribute('title')?.trim()
      || element.getAttribute('aria-labelledby');

    const describe = element => {
      const classes = (element.className?.toString?.() ?? '').trim().split(/\s+/).slice(0, 2).join('.');

      return `${element.tagName.toLowerCase()}${classes ? '.' + classes : ''}`;
    };

    for (const image of document.querySelectorAll('img')) {
      // An empty alt is a decision — "this picture says nothing a reader needs" — and a missing one is not.
      if (image.getAttribute('alt') === null) problems.push(`${describe(image)} has no alt (${image.src.slice(-40)})`);
    }

    for (const control of document.querySelectorAll('button, a[href], [role="button"]')) {
      if (!visible(control)) continue;
      if (!named(control)) problems.push(`${describe(control)} has no accessible name`);
    }

    for (const field of document.querySelectorAll('input:not([type="hidden"]), textarea, select')) {
      if (!visible(field)) continue;

      const labelled = field.labels?.length
        || field.getAttribute('aria-label')
        || field.getAttribute('aria-labelledby')
        || field.getAttribute('placeholder');

      if (!labelled) problems.push(`${describe(field)} has nothing naming it`);
    }

    const headings = [...document.querySelectorAll('h1')].filter(visible);

    if (headings.length === 0) problems.push('no h1 on the page');
    if (headings.length > 1) problems.push(`${headings.length} h1s on one page`);

    return problems.slice(0, 4);
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

/**
 * One screen: go there, let it settle, optionally press something, shoot it at both widths, and ask the
 * three questions.
 *
 * <b>The `open` step exists because opening a screen is not using it.</b> Three defects in a row were found
 * by clicking once on a screen this sweep had walked clean a dozen times — a photograph in the Code tab that
 * said "there is nothing to show", a diff clipped at the window edge, a domain that could not be removed.
 * Every one of them was behind a single click, and the sweep had never made one.
 *
 * It is deliberately one click and never a sequence: a harness that drives a flow is a test that breaks when
 * the flow changes, and this one's job is to *look*. If the thing is not there, nothing happens.
 */
async function walk(page, name, url, open) {
  current = name;

  await page.goto(url, { waitUntil: 'networkidle' }).catch(error => problems.push(`${name}: ${error.message}`));
  await page.waitForTimeout(1200);

  if (open) {
    const target = open(page).last();

    if (await target.count()) {
      await target.click().catch(error => problems.push(`${name}: could not open anything — ${error.message}`));
      await page.waitForTimeout(1500);
    }
  }

  const text = await page.locator('body').innerText().catch(() => '');

  if (text.trim().length < 20) problems.push(`${name}: the page is empty`);

  for (const width of [1400, 390]) {
    await shot(page, name, width);

    for (const pane of await sidewaysScroll(page))
      problems.push(`${name} @${width}: ${pane} — content is being cut off`);

    // Only at one width: these are questions about the markup, and asking them twice reports each answer
    // twice.
    if (width === 1400)
      for (const failing of await accessibility(page)) problems.push(`${name}: ${failing}`);
  }

  process.stdout.write(`  · ${name}\n`);
}

try {
  watch(page);

  // The signed-out screens first, because they are the product's first five minutes and because walking them
  // afterwards would mean signing out. They were missing from this sweep entirely: it went straight to the
  // login form, filled it in, and never looked at register or forgotten-password at all — nor at login
  // itself on a phone.
  for (const [name, path] of [['login', '/login'], ['register', '/register'], ['forgot', '/forgot-password']])
    await walk(page, name, `${origin}${path}`);

  // The sign-in-with-Google branch, which no development machine renders: the button and the rule beside it
  // appear only when `Authentication:Google:ClientId` is configured, and a fresh clone deliberately has no
  // credentials. So nobody had ever looked at it — and it carried the word "vagy", Hungarian for "or",
  // inherited from the reference project, on the two screens every new customer sees first.
  //
  // Answered here rather than configured: the harness stubs the one endpoint that decides, so the branch
  // renders. It never presses the button, which is the only thing a real client id would buy.
  await page.route(`${origin}/api/auth/providers`, route =>
    route.fulfill({ status: 200, contentType: 'application/json', body: '{"google":true}' }));

  for (const [name, path] of [['login-google', '/login'], ['register-google', '/register']])
    await walk(page, name, `${origin}${path}`);

  await page.unroute(`${origin}/api/auth/providers`);

  current = 'sign in';
  await page.goto(`${origin}/login`, { waitUntil: 'networkidle' });
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

  // The third entry, where there is one, is what to press once the screen has loaded. See `walk`.
  const screens = [
    ['sites', `${origin}/`],
    ['editor', `${origin}/sites/${nanoid}`],
    // Both of these screens open on something already, so the click has to reach what the default does not:
    // the *oldest* version rather than the newest, and a photograph rather than the source file it starts on.
    ['history', `${origin}/sites/${nanoid}/history`, page => page.getByText('Created from the Webly starter')],
    ['code', `${origin}/sites/${nanoid}/code`, page => page.getByText(/\.(png|jpg|jpeg|webp|gif)$/)],
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

  for (const [name, url, open] of screens) await walk(page, name, url, open);

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
