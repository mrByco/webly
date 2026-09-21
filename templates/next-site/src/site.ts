/**
 * Where this site lives, as the build was told.
 *
 * Webly sets `NEXT_PUBLIC_SITE_URL` when it publishes — the site's own address, which is its custom domain
 * once it has a verified one and its Webly subdomain otherwise. It is absent in the editor's preview and in
 * `npm run dev`, where there is no public address to speak of, so everything that uses it is written to work
 * without it rather than to guess.
 *
 * Do not hard-code a URL anywhere in the site. The address can change — a customer connects a domain — and a
 * canonical link or a sitemap pointing at the old one tells every search engine the real site is somewhere
 * it is not.
 */
export const siteUrl = process.env.NEXT_PUBLIC_SITE_URL?.replace(/\/+$/, '') || undefined;
