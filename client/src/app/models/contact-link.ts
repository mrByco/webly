/**
 * Whether a value somebody typed into a contact form is something the owner can act on, and the link if so.
 *
 * The Messages screen deliberately has no reply of its own: an enquiry is answered from the owner's own email,
 * where the notification already is with the visitor's address in its reply-to. That stays true, and it is
 * exactly why this exists rather than a reply box. Somebody reading the list on their phone and deciding to
 * answer *this* one has the address in front of them as text — so "reply from your own mail" costs a select, a
 * copy and an app switch, which is three steps too many for the thing the screen is for. It is not a fourth
 * verb; it is the three it has, made usable.
 *
 * **By the value, never by the label.** The agent writes the form, so the field asking for an address might be
 * called "email", "Your email", "How can we reach you" or nothing recognisable at all — while an address looks
 * like an address whatever it is called. (The server guesses the notification's reply-to by *name* instead,
 * and that is a different job: it has to pick one field out of many, where this only has to judge one.)
 *
 * **Whole value only.** An address inside a paragraph is part of what somebody wrote, and turning a phrase in
 * the middle of their message into a link is editing it.
 */
export function contactLink(value: string): string | undefined {
  const trimmed = value.trim();

  // `?` and `&` are refused deliberately. Both are legal almost nowhere in a real address, and both are how a
  // `mailto:` grows extra fields — `?bcc=`, `&subject=` — out of a string a stranger typed into a form.
  if (/^[^\s@?&]+@[^\s@?&.]+\.[^\s@?&]{2,}$/.test(trimmed)) return `mailto:${trimmed}`;

  // A phone number as people actually write one, brackets and spaces included: "07700 900123",
  // "+44 191 498 0123", "(0191) 498 0123". Seven digits is the shortest real one, and the length cap is what
  // stops a run of numbers in a message body — an order reference, a date, a price list — becoming a call.
  if (/^\+?[\d\s().-]{7,24}$/.test(trimmed) && (trimmed.match(/\d/g) ?? []).length >= 7) {
    return `tel:${trimmed.replace(/[^\d+]/g, '')}`;
  }

  return undefined;
}
