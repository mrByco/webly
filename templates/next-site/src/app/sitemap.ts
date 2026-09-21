import type { MetadataRoute } from 'next';

import { siteUrl } from '@/site';

/**
 * `/sitemap.xml`, generated at build time.
 *
 * **Add a line when you add a page.** A static export cannot discover routes for itself, so this list is the
 * one place that says what this site consists of — and a page missing from it is a page search engines find
 * late or not at all.
 *
 * Empty when the build does not know the site's address, because every URL in a sitemap is absolute and a
 * sitemap full of the wrong host is worse than no sitemap.
 */
// `output: 'export'` builds this file into a static asset, and Next asks to be told that explicitly — without
// it the build stops with "export const dynamic = force-static not configured on route".
export const dynamic = 'force-static';

export default function sitemap(): MetadataRoute.Sitemap {
  if (!siteUrl) return [];

  return [
    { url: `${siteUrl}/`, changeFrequency: 'monthly', priority: 1 },
    { url: `${siteUrl}/contact`, changeFrequency: 'yearly', priority: 0.5 },
  ];
}
