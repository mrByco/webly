import type { NextConfig } from 'next';

/**
 * Deliberately almost empty.
 *
 * Webly builds this project in a sandbox and uploads the output, so anything that needs a service at build
 * time — remote image loaders, rewrites to an API, a custom server — would make a deployment depend on
 * something the site's owner cannot see or fix. Images are unoptimized for the same reason: Next's optimizer
 * wants a running server, and a static export must not need one.
 */
const config: NextConfig = {
  output: 'export',
  /**
   * Set only by the editor's preview, never by a build.
   *
   * The preview serves this dev server through Webly's own origin, under a path that names the site —
   * `/api/sites/{id}/preview/`. Next.js writes **absolute** URLs for its stylesheets, its chunks and its
   * hot-reload socket, so without a base path it asks for `/_next/...` at the root of that origin, which is
   * Webly's app and not this site: the preview then renders as unstyled HTML with no JavaScript, which is
   * what it did until somebody looked at it in a browser rather than reading its HTML.
   *
   * It is an environment variable rather than a constant because the path contains the site's id, and it is
   * absent everywhere else — `next build` in the publishing sandbox never sees it, so the published site is
   * served from `/` as it must be.
   */
  basePath: process.env.WEBLY_PREVIEW_BASE || undefined,
  images: { unoptimized: true },
  // Trailing slashes keep `/about` and `/about/` the same page on a static host, which is what stops a shared
  // link from 404ing depending on how somebody typed it.
  trailingSlash: true,
};

export default config;
