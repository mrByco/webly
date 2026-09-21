import Link from 'next/link';
import { siteName } from '@/site';

/** The site-wide header. One nav, edited here, so no page can be missing a link. */
export function Header() {
  return (
    <header className="border-b border-edge">
      <div className="mx-auto flex max-w-5xl items-center justify-between gap-6 px-5 py-4">
        <Link href="/" className="text-lg font-bold tracking-tight">
          {siteName}
        </Link>

        <nav className="flex gap-5 text-sm font-medium text-ink-muted">
          <Link href="/" className="hover:text-ink">
            Home
          </Link>

          <Link href="/contact" className="hover:text-ink">
            Contact
          </Link>
        </nav>
      </div>
    </header>
  );
}
