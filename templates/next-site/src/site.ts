/**
 * What this business is called.
 *
 * Webly writes this once, from the name the owner typed when they created the site — the only fact about
 * their business it has before anybody says anything. It is a literal rather than an environment variable
 * because it is *content*: the header, the footer and every page's title read it, and it is yours to change
 * from the first message onwards. Renaming the site in Webly deliberately does not come back and change it,
 * for the same reason renaming it does not move the web address: what is on the page is the site's, not the
 * dashboard's.
 */
export const siteName = 'Your site';

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

/**
 * Where this site's forms post.
 *
 * Webly sets `NEXT_PUBLIC_FORM_ENDPOINT` both when it publishes and when it runs the editor's preview, so a
 * form behaves the same in both. It points at Webly, not at this site, and it has to: this project is built as
 * a **static export**, so there is no server here to receive a POST — no route handler, no server action, no
 * API route. Writing one would compile and then silently 404 on the published site.
 *
 * What the endpoint does with a submission: stores it against this site, emails the owner with a reply-to of
 * whatever address the visitor left, and sends the visitor back to the page named by the form's `_next` field.
 *
 * Undefined in a plain `npm run dev` outside Webly. A form built with `ContactForm` renders a short note
 * instead of a dead submit button in that case, which is the honest thing for a page somebody is previewing
 * locally — and the reason nothing here should hard-code a URL as a fallback.
 */
export const formEndpoint = process.env.NEXT_PUBLIC_FORM_ENDPOINT || undefined;
