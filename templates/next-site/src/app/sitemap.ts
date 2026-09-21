import { readdirSync } from 'node:fs';
import path from 'node:path';
import type { MetadataRoute } from 'next';

import { siteUrl } from '@/site';

/**
 * `/sitemap.xml`, generated at build time by **looking at the pages that exist**.
 *
 * This used to be a hand-written list with a rule above it saying "add a line when you add a page", and that
 * rule is exactly the kind a busy afternoon skips: the cost of forgetting is invisible — the page works, the
 * site looks finished, and search engines simply take longer to find it, or never do. So the list is not
 * written any more. The routes are read off the folders under `src/app`, which is where Next itself gets
 * them, so the sitemap cannot disagree with the site.
 *
 * `fs` is fine here and only here: a sitemap is a **build-time** file in a static export, so this runs once on
 * the machine that builds the site and never in a browser. Do not copy the pattern into a page.
 *
 * Empty when the build does not know the site's address, because every URL in a sitemap is absolute and one
 * full of the wrong host is worse than no sitemap at all.
 */
// `output: 'export'` builds this file into a static asset, and Next asks to be told that explicitly — without
// it the build stops with "export const dynamic = force-static not configured on route".
export const dynamic = 'force-static';

/**
 * The routes this site has, as paths beginning with `/`.
 *
 * Four kinds of folder are skipped, and each is a thing Next does not turn into a page on its own:
 * `[slug]` needs data a static export does not have here, `(group)` and `@slot` are organisational, and a
 * leading underscore is Next's own "private folder" convention. Anything else holding a `page.tsx` is a page
 * somebody can visit, which is the whole test.
 */
function routes(directory: string, prefix = ''): string[] {
  const entries = readdirSync(directory, { withFileTypes: true });
  const found = entries.some(entry => entry.isFile() && /^page\.(tsx|ts|jsx|js)$/.test(entry.name))
    ? [prefix || '/']
    : [];

  for (const entry of entries) {
    if (!entry.isDirectory()) continue;
    if (/^[[(@_]/.test(entry.name)) continue;

    found.push(...routes(path.join(directory, entry.name), `${prefix}/${entry.name}`));
  }

  return found;
}

export default function sitemap(): MetadataRoute.Sitemap {
  if (!siteUrl) return [];

  // `process.cwd()` is the project's own root during `next build`, which is where `src/app` is.
  const paths = routes(path.join(process.cwd(), 'src', 'app')).sort();

  return paths.map(route => ({
    // With the trailing slash the site is actually served at (`trailingSlash: true` in next.config.ts), so
    // the sitemap names the URL a visitor lands on rather than the one that redirects to it.
    url: route === '/' ? `${siteUrl}/` : `${siteUrl}${route}/`,
    changeFrequency: route === '/' ? ('monthly' as const) : ('yearly' as const),
    priority: route === '/' ? 1 : 0.6,
  }));
}
