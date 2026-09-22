'use client';

import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';

/**
 * What a visitor sees after they press Send.
 *
 * **Wrap the contact form in this.** Without it the form posts, Webly records the message, the owner is
 * emailed — and the visitor is returned to an empty form with nothing on the page saying any of that happened.
 * Which is indistinguishable from the send having failed, so the next thing they do is fill it in and send it
 * again, or give up and ring somebody else. It is the worst place in a business's website to be silent.
 *
 * It went missing between two pieces of reasoning that were each right on their own. The endpoint sends the
 * visitor back to the page they came from, because a shop's customer should not land on Webly's website; and
 * the contact page could not read `?sent=1` itself, because a static export has no server to read a query
 * string at request time. Both true, and together they left the confirmation nowhere: Webly's own thank-you
 * page is only shown in the fallback case, when there is no usable `Referer` — which is the case almost
 * nobody takes.
 *
 * The browser can read a query string even when no server can, so this is a client component and reads it
 * **after mounting** rather than while rendering: the markup is the same on the server and on the first paint,
 * so there is no hydration mismatch, and a visitor with JavaScript switched off gets the form exactly as
 * before. That order is deliberate — the form itself works without scripts on purpose, and a confirmation is
 * the one part of it that can afford to be an enhancement.
 */
export function SentNotice({ children }: { children: ReactNode }) {
  const [sent, setSent] = useState(false);

  useEffect(() => {
    setSent(new URLSearchParams(window.location.search).get('sent') === '1');
  }, []);

  if (!sent) return <>{children}</>;

  return (
    <div
      // `status` rather than `alert`: a screen reader announces it when it lands without interrupting, which
      // is what an acknowledgement is. `alert` is for something that went wrong.
      role="status"
      className="rounded-card border border-edge bg-brand-soft px-5 py-6"
    >
      <p className="text-lg font-semibold">Thank you — your message has been sent.</p>

      {/* No promise about when: nobody has told this site how quickly its owner replies, and inventing one is
          the thing the editing rules exist to prevent. */}
      <p className="mt-2 text-ink-muted">We have it, and we will get back to you.</p>

      <a
        href="?"
        className="mt-4 inline-block font-semibold text-brand underline underline-offset-4 hover:text-brand-strong"
      >
        Send another message
      </a>
    </div>
  );
}
