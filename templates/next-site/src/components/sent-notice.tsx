import type { ReactNode } from 'react';

export interface SentNoticeProps {
  children: ReactNode;

  /** The first line, for a business that would rather say it differently. */
  heading?: string;

  /** The line under it. Say nothing here that nobody has promised — see `AGENTS.md`. */
  body?: string;

  /** What the way back to an empty form is called. */
  againLabel?: string;
}

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
 * **It is a fragment and a CSS rule, not a script**, and the first version got that wrong in a way worth
 * recording. It read `?sent=1` in a `useEffect`, which works and reaches everybody except the one visitor
 * this whole form was shaped around: `ContactForm` is an ordinary HTML form posting cross-origin precisely so
 * that it keeps working when scripts do not, and making the acknowledgement the one part that needs them
 * leaves that visitor watching their message disappear. Measured in a browser with JavaScript switched off —
 * the enquiry arrived, the owner was emailed, and the page said nothing at all.
 *
 * So the endpoint returns to `#sent` and `:target` does the showing. No server, no script, no hydration, and
 * it travels with this component: anywhere `SentNotice` wraps a `ContactForm` the confirmation works, which a
 * separate thank-you page would not — `ContactForm` can be dropped on any page, and a `_next` pointing at a
 * page somebody forgot to write is a 404 on a real business's website at the worst possible moment.
 *
 * **The rule is in this file rather than in `globals.css`, and that is load-bearing.** This component and
 * `contact-form.tsx` are two of the handful Webly keeps current in every site, including sites made before
 * they changed — so this file arrives in a repository whose stylesheet Webly does not own and must not edit.
 * A component whose `display: none` lived in that stylesheet would arrive without it and draw the thank-you
 * permanently, above a form nobody had used: worse than the silence it replaced. Everything it needs is here.
 * For the same reason the wording is **props with defaults** rather than text to edit in place: a site that
 * wants to say something else says it from its own contact page, and stays current.
 */
export function SentNotice({
  children,
  heading = 'Thank you — your message has been sent.',
  body = 'We have it, and we will get back to you.',
  againLabel = 'Send another message',
}: SentNoticeProps) {
  return (
    <div>
      {/*
        `precedence` is what lets React hoist this into the document head and write it once however many
        forms a page has. Deliberately three rules and no more: everything that decides how the notice
        *looks* is a class like the rest of the site, and only what decides whether it is on screen is here.
      */}
      <style href="sent-notice" precedence="default">{
        '#sent{display:none}'
        + '#sent:target{display:block}'
        + '#sent:target~.sent-form{display:none}'
      }</style>

      <div
        id="sent"
        // Focusable so that following the fragment moves the reading position here; it is ordinary page
        // content by the time anybody sees it, so it is not a live region and must not announce itself as one.
        tabIndex={-1}
        className="rounded-card border border-edge bg-brand-soft px-5 py-6"
      >
        <p className="text-lg font-semibold">{heading}</p>

        <p className="mt-2 text-ink-muted">{body}</p>

        {/* Targeting the form is what un-targets the notice, so this needs no script either. */}
        <a
          href="#form"
          className="mt-4 inline-block font-semibold text-brand underline underline-offset-4 hover:text-brand-strong"
        >
          {againLabel}
        </a>
      </div>

      <div id="form" className="sent-form">
        {children}
      </div>
    </div>
  );
}
