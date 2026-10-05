import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { Header } from '@/components/header';
import { Footer } from '@/components/footer';
import { siteName, siteUrl } from '@/site';
import './globals.css';

// Self-hosted at build time by next/font, so a published page makes no request to a font CDN — one less
// third party between a visitor and the site.
const inter = Inter({ subsets: ['latin'], variable: '--font-inter', display: 'swap' });

const title = siteName;

// One plain sentence about the business: what a search result shows under the title and a shared link shows
// under its picture. Empty until somebody describes the business, and AGENTS.md asks for it then.
//
// It used to ship as "Tell Webly what this site is about and it will write this page for you", and that sentence
// outlived the page it stood in for: the agent rewrote the home page, the description sat in this file where
// nobody asked about it, and every search result and every link the owner shared went on telling their customers
// to tell Webly something. The owner never sees this text, which is exactly why it has to start empty rather than
// wrong. Empty leaves the tags out; a search engine then quotes the page itself.
const description = '';

export const metadata: Metadata = {
  // A template rather than a string, so that every other page's own title becomes "Contact · <the business>"
  // rather than replacing the site's name entirely. Without it a shared link, a search result and a browser tab all
  // say only what the page is and never whose it is — and the page name alone is the part a stranger cannot
  // place. `default` is what the home page and anything with no title of its own gets.
  title: { default: title, template: `%s · ${title}` },
  ...(description ? { description } : {}),

  // The address the build was told about, so that canonical links, the sitemap and anything a social network
  // reads resolve to this site rather than to a relative path nothing outside the page can follow. Absent in
  // the preview and in `npm run dev`; Next simply leaves the absolute URLs out when it is.
  ...(siteUrl ? { metadataBase: new URL(siteUrl), alternates: { canonical: '/' } } : {}),

  // What a link to this site looks like when somebody shares it. Derived from the two strings above rather
  // than written twice, because a title that drifts from its own Open Graph title is the kind of mistake
  // nobody sees until it is on somebody else's timeline.
  openGraph: { title, ...(description ? { description } : {}), type: 'website', ...(siteUrl ? { url: siteUrl } : {}) },
  twitter: { card: 'summary_large_image', title, ...(description ? { description } : {}) },
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={inter.variable}>
      <body className="flex min-h-dvh flex-col">
        <Header />
        <main className="flex-1">{children}</main>
        <Footer />
      </body>
    </html>
  );
}
