import type { MetadataRoute } from 'next';

import { siteUrl } from '@/site';

/**
 * `/robots.txt`, generated at build time.
 *
 * Everything is allowed: this is somebody's business website and the whole point of publishing it is being
 * found. The sitemap line is what search engines look for first, and it is only written when the build knows
 * the site's address — a `Sitemap:` line pointing at the wrong host is worse than none.
 */
// `output: 'export'` builds this file into a static asset, and Next asks to be told that explicitly — without
// it the build stops with "export const dynamic = force-static not configured on route".
export const dynamic = 'force-static';

export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: '*', allow: '/' },
    ...(siteUrl ? { sitemap: `${siteUrl}/sitemap.xml` } : {}),
  };
}
