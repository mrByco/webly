import { Hero } from '@/components/hero';
import { Features } from '@/components/features';
import { Cta } from '@/components/cta';

/**
 * The home page of a site nobody has described yet.
 *
 * Every string here is obviously a placeholder, on purpose: if somebody publishes before editing, they should
 * see that they published a skeleton — not a site claiming to be a business that does not exist.
 */
export default function Home() {
  return (
    <>
      <Hero
        headline="Your new website"
        subheadline="Tell Webly what this site is about, and this page will be rewritten for you."
      />

      <Features
        title="What we do"
        items={[
          { title: 'First thing', description: 'Describe it in one sentence.' },
          { title: 'Second thing', description: 'Describe it in one sentence.' },
          { title: 'Third thing', description: 'Describe it in one sentence.' },
        ]}
      />

      <Cta
        headline="Get in touch"
        body="Say how people should reach you."
        actionLabel="Send us a message"
        actionHref="/contact"
      />
    </>
  );
}
