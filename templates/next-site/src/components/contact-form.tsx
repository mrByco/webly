import { formEndpoint } from '@/site';

export interface ContactFormProps {
  /**
   * Which form this is, as it will appear in the owner's list of messages. Change it when a page asks for
   * something different from the contact page — "quote", "booking" — so the two are told apart.
   */
  name?: string;

  /** The page to return to after sending, relative to this one. Must stay relative: see below. */
  returnTo?: string;

  fields?: FormField[];

  submitLabel?: string;
}

export interface FormField {
  /** The label the visitor reads, and the name the owner sees above their answer. Write it for them. */
  label: string;

  /** `name` in the posted form. Include "email" in the one asking for an address: see below. */
  name: string;

  type?: 'text' | 'email' | 'tel' | 'textarea';

  required?: boolean;
}

const DEFAULT_FIELDS: FormField[] = [
  { label: 'Your name', name: 'name', required: true },
  { label: 'Your email', name: 'email', type: 'email', required: true },
  { label: 'Your phone number', name: 'phone', type: 'tel' },
  { label: 'How can we help?', name: 'message', type: 'textarea', required: true },
];

/**
 * A form a visitor can actually send, with no JavaScript involved.
 *
 * **An ordinary HTML form posting to another origin**, which browsers have always allowed without CORS and
 * which keeps working when a script fails to load, is blocked, or is switched off. A contact form is the last
 * thing on a small business's website that should depend on JavaScript: a visitor who cannot reach them is a
 * customer they never hear about.
 *
 * Three hidden fields do the talking between the form and Webly, and each matters:
 *
 * - `_form` is the name shown above the message in the owner's list.
 * - `_next` is where the visitor lands afterwards, resolved **relative to this page**. It must stay relative —
 *   an absolute URL is refused and the visitor gets Webly's own plain thank-you page instead of yours.
 * - `_ignore` is a honeypot: a field nobody can see and no person fills in, so anything in it means the sender
 *   was a bot and the submission is dropped. Keep it, keep it hidden, and do not give it a friendly label or a
 *   real-sounding name — a bot reading the markup will fill in anything that looks like a field it should.
 *
 * Name the email field something containing "email". Webly sets the notification's reply-to from it, so the
 * owner can answer from their inbox rather than copying an address out of it by hand.
 */
export function ContactForm({
  name = 'contact',
  returnTo = '?sent=1',
  fields = DEFAULT_FIELDS,
  submitLabel = 'Send',
}: ContactFormProps) {
  // Nothing to post to, so nothing that looks like it can be posted. This is the local-development case; on a
  // Webly preview and on the published site the endpoint is always set.
  if (!formEndpoint) {
    return (
      <p className="rounded-card border border-edge bg-brand-soft px-4 py-3 text-sm text-ink-muted">
        This form is connected once the site is running in Webly.
      </p>
    );
  }

  return (
    <form method="post" action={formEndpoint} className="flex flex-col gap-4">
      <input type="hidden" name="_form" value={name} />
      <input type="hidden" name="_next" value={returnTo} />

      {/* The honeypot. `hidden` rather than off-screen positioning: a screen reader skips it too, so nobody
          who is using one is asked to fill in a field that means "you are a robot". */}
      <input type="text" name="_ignore" tabIndex={-1} autoComplete="off" hidden aria-hidden="true" />

      {fields.map(field => (
        <label key={field.name} className="flex flex-col gap-1.5 text-sm font-medium">
          <span>
            {field.label}
            {field.required && <span className="text-ink-muted"> *</span>}
          </span>

          {field.type === 'textarea' ? (
            <textarea
              name={field.name}
              required={field.required}
              rows={5}
              maxLength={4000}
              className="rounded-field border border-edge bg-white px-3 py-2 font-normal outline-none focus:border-brand"
            />
          ) : (
            <input
              type={field.type ?? 'text'}
              name={field.name}
              required={field.required}
              maxLength={300}
              className="rounded-field border border-edge bg-white px-3 py-2 font-normal outline-none focus:border-brand"
            />
          )}
        </label>
      ))}

      <button
        type="submit"
        className="mt-2 inline-flex items-center justify-center rounded-card bg-brand px-5 py-3 font-semibold text-white hover:bg-brand-strong"
      >
        {submitLabel}
      </button>
    </form>
  );
}
