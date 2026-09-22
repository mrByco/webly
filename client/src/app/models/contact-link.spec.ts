import { describe, expect, it } from 'vitest';

import { contactLink } from './contact-link';

describe('contactLink', () => {
  it('links an address so the owner can answer from their own mail', () => {
    expect(contactLink('harriet@okonjo-builders.co.uk')).toBe('mailto:harriet@okonjo-builders.co.uk');
    expect(contactLink('  ola@sandvik-interiors.no  ')).toBe('mailto:ola@sandvik-interiors.no');
  });

  it('links a phone number the way people write one', () => {
    expect(contactLink('(0191) 498 0123')).toBe('tel:01914980123');
    expect(contactLink('+44 191 498 0123')).toBe('tel:+441914980123');
    expect(contactLink('07700 900123')).toBe('tel:07700900123');
  });

  /**
   * The case this rule exists to get right. A visitor's message is theirs, and turning a phrase inside it into
   * a link is editing what they wrote — as well as pointing the owner at the wrong address, since the one in
   * the middle of a sentence is rarely the one to reply to.
   */
  it('leaves an address inside a sentence alone', () => {
    expect(contactLink('Reach my partner at tom@sandvik-interiors.no if I am out.')).toBeUndefined();
    expect(contactLink('Call 0191 498 0123 after six, or 0191 498 0124.')).toBeUndefined();
  });

  it('leaves ordinary answers alone', () => {
    expect(contactLink('Ola Sandvik')).toBeUndefined();
    expect(contactLink('')).toBeUndefined();
    expect(contactLink('Do you make oak staircases?')).toBeUndefined();

    // Not a phone number: too few digits to be one, and a year or a house number is neither.
    expect(contactLink('1998')).toBeUndefined();
    expect(contactLink('42')).toBeUndefined();
  });

  /**
   * A `mailto:` takes its extra fields after a `?`, so an address carrying one is a way to choose who else the
   * owner's reply goes to. Nothing that shape is treated as an address.
   */
  it('refuses an address that could carry mail headers', () => {
    expect(contactLink('someone@example.com?bcc=whoever@example.net')).toBeUndefined();
    expect(contactLink('someone@example.com&subject=Re')).toBeUndefined();
  });
});
