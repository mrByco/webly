import type { Metadata } from 'next';

import { ContactForm } from '@/components/contact-form';

export const metadata: Metadata = {
  title: 'Contact',
  description: 'Send us a message and we will get back to you.',
};

/**
 * The contact page, and the one page in this template that does something rather than saying something.
 *
 * It ships with the starter site on purpose: a business website whose whole job is to bring in enquiries should
 * be able to receive one from the first minute, and "add a contact form" is the request most likely to be
 * answered by writing a form that posts nowhere. Rewrite the copy freely; keep the form's hidden fields as
 * `ContactForm` sets them.
 *
 * `searchParams` is deliberately not read here. A static export has no server to read a query string at
 * request time, so "?sent=1" cannot change what this page renders — the confirmation the visitor sees is the
 * one Webly's endpoint shows them on the way back. Reading it would compile and then quietly do nothing.
 */
export default function Contact() {
  return (
    <section className="mx-auto max-w-2xl px-5 py-16">
      <h1 className="text-3xl font-bold tracking-tight">Get in touch</h1>

      <p className="mt-3 text-ink-muted">
        Send us a message and we will get back to you. Tell Webly what this page should say and it will be
        rewritten for you.
      </p>

      <div className="mt-10">
        <ContactForm />
      </div>
    </section>
  );
}
