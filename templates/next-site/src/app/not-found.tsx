import type { Metadata } from 'next';
import Link from 'next/link';

// Its own title, because "New site" in the tab of a page that is not there tells a visitor nothing about what
// happened. The layout's template turns this into "Page not found · New site".
export const metadata: Metadata = { title: 'Page not found' };

/**
 * The page a visitor gets for an address that is not here — a mistyped link, or one that pointed at a page
 * this site used to have. It is a real page of this site rather than the host's default, so the header and
 * footer are still there and there is a way back.
 */
export default function NotFound() {
  return (
    <section className="mx-auto max-w-2xl px-5 py-24 text-center">
      <h1 className="text-3xl font-bold tracking-tight">This page is not here</h1>

      <p className="mt-3 text-ink-muted">
        The link may be out of date, or the address may have a typo in it.
      </p>

      <Link
        href="/"
        className="mt-8 inline-flex items-center rounded-card bg-brand px-5 py-3 font-semibold text-white hover:bg-brand-strong"
      >
        Go to the home page
      </Link>
    </section>
  );
}
