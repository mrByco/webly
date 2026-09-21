import type { Metadata } from 'next';
import { Inter } from 'next/font/google';
import { Header } from '@/components/header';
import { Footer } from '@/components/footer';
import { siteUrl } from '@/site';
import './globals.css';

// Self-hosted at build time by next/font, so a published page makes no request to a font CDN — one less
// third party between a visitor and the site.
const inter = Inter({ subsets: ['latin'], variable: '--font-inter', display: 'swap' });

const title = 'New site';
const description = 'Tell Webly what this site is about and it will write this page for you.';

export const metadata: Metadata = {
  title,
  description,

  // The address the build was told about, so that canonical links, the sitemap and anything a social network
  // reads resolve to this site rather than to a relative path nothing outside the page can follow. Absent in
  // the preview and in `npm run dev`; Next simply leaves the absolute URLs out when it is.
  ...(siteUrl ? { metadataBase: new URL(siteUrl), alternates: { canonical: '/' } } : {}),

  // What a link to this site looks like when somebody shares it. Derived from the two strings above rather
  // than written twice, because a title that drifts from its own Open Graph title is the kind of mistake
  // nobody sees until it is on somebody else's timeline.
  openGraph: { title, description, type: 'website', ...(siteUrl ? { url: siteUrl } : {}) },
  twitter: { card: 'summary_large_image', title, description },
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
