// Which Chromium the agent's two ways of looking at the site use: `webly-screenshot` and the browser tools
// (`webly-browser-mcp`). One answer for both, so the picture a screenshot takes and the page the agent clicks
// through are the same browser.

import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { which } from './processes.js';

export { which };

/**
 * Where Windows installs a Chromium, which it never puts on PATH. Chrome first, then Edge — Edge is on every Windows
 * machine, is Chromium, and speaks the same DevTools protocol, so a developer there gets screenshots with nothing to
 * install.
 */
function windowsBrowsers() {
  if (process.platform !== 'win32') return [];

  const roots = [process.env.ProgramFiles, process.env['ProgramFiles(x86)'], process.env.LOCALAPPDATA].filter(Boolean);

  return [
    ['Google', 'Chrome', 'Application', 'chrome.exe'],
    ['Microsoft', 'Edge', 'Application', 'msedge.exe'],
  ].flatMap(parts => roots.map(root => join(root, ...parts)));
}

/** WEBLY_BROWSER when it is set — and then only that, so a wrong setting says so — or the first Chromium on PATH. */
export function findBrowser() {
  if (process.env.WEBLY_BROWSER) return existsSync(process.env.WEBLY_BROWSER) ? process.env.WEBLY_BROWSER : null;

  return ['chromium', 'chromium-browser', 'chrome-headless-shell', 'google-chrome', 'google-chrome-stable', 'chrome']
    .map(name => which(name)).find(Boolean)
    ?? windowsBrowsers().find(existsSync)
    ?? null;
}
