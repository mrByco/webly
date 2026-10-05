// Which Chromium the agent's two ways of looking at the site use: `webly-screenshot` and the browser tools
// (`webly-browser-mcp`). One answer for both, so the picture a screenshot takes and the page the agent clicks
// through are the same browser.

import { existsSync } from 'node:fs';

/** A program on PATH, as the shell would find it, or null. */
export function which(name) {
  for (const directory of (process.env.PATH ?? '').split(':')) {
    if (directory && existsSync(`${directory}/${name}`)) return `${directory}/${name}`;
  }

  return null;
}

/** WEBLY_BROWSER when it is set — and then only that, so a wrong setting says so — or the first Chromium on PATH. */
export function findBrowser() {
  if (process.env.WEBLY_BROWSER) return existsSync(process.env.WEBLY_BROWSER) ? process.env.WEBLY_BROWSER : null;

  return ['chromium', 'chromium-browser', 'chrome-headless-shell', 'google-chrome', 'google-chrome-stable', 'chrome']
    .map(which).find(Boolean) ?? null;
}
