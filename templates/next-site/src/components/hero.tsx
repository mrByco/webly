import Link from 'next/link';

export interface HeroProps {
  headline: string;
  subheadline?: string;
  action?: { label: string; href: string };
  image?: { src: string; alt: string };
}

/**
 * The first thing a visitor sees. One call to action — two competing buttons is how a hero stops working.
 *
 * The two-column layout is conditional on there being an image, and that is not tidiness. Unconditionally, a
 * hero with no picture still gave half the width to nothing: the headline wrapped early against an empty
 * right-hand column, and the page read as one whose image had failed to load. Every site starts without an
 * image, so that was the first thing anybody saw of theirs.
 */
export function Hero({ headline, subheadline, action, image }: HeroProps) {
  return (
    <section
      className={`mx-auto grid max-w-5xl gap-10 px-5 py-16 md:py-24 ${
        image ? 'lg:grid-cols-[1.1fr_1fr] lg:items-center' : ''
      }`}
    >
      <div>
        <h1 className="text-4xl font-bold tracking-tight text-balance sm:text-5xl">{headline}</h1>

        {subheadline && <p className="mt-4 max-w-prose text-lg text-ink-muted">{subheadline}</p>}

        {action && (
          <Link
            href={action.href}
            className="mt-8 inline-flex items-center rounded-card bg-brand px-5 py-3 font-semibold text-white hover:bg-brand-strong"
          >
            {action.label}
          </Link>
        )}
      </div>

      {image && (
        // eslint-disable-next-line @next/next/no-img-element -- images are unoptimized by design (next.config.ts)
        <img src={image.src} alt={image.alt} className="w-full rounded-card" loading="eager" />
      )}
    </section>
  );
}
