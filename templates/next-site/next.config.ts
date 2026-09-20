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
  images: { unoptimized: true },
  // Trailing slashes keep `/about` and `/about/` the same page on a static host, which is what stops a shared
  // link from 404ing depending on how somebody typed it.
  trailingSlash: true,
};

export default config;
