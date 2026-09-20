import Link from 'next/link';

export interface CtaProps {
  headline: string;
  body?: string;
  actionLabel: string;
  actionHref: string;
}

/** The ask, at the bottom of a page somebody finished reading. */
export function Cta({ headline, body, actionLabel, actionHref }: CtaProps) {
  return (
    <section className="bg-brand-soft">
      <div className="mx-auto max-w-5xl px-5 py-16 text-center">
        <h2 className="text-2xl font-bold tracking-tight sm:text-3xl">{headline}</h2>

        {body && <p className="mx-auto mt-3 max-w-prose text-ink-muted">{body}</p>}

        <Link
          href={actionHref}
          className="mt-8 inline-flex items-center rounded-card bg-brand px-5 py-3 font-semibold text-white hover:bg-brand-strong"
        >
          {actionLabel}
        </Link>
      </div>
    </section>
  );
}
