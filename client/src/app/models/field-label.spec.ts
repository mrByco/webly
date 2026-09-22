import { describe, expect, it } from 'vitest';

import { fieldLabel } from './field-label';

describe('fieldLabel', () => {
  it('capitalises the plain names the template posts', () => {
    expect(fieldLabel('name')).toBe('Name');
    expect(fieldLabel('email')).toBe('Email');
    expect(fieldLabel('message')).toBe('Message');
  });

  it('breaks the two ways a field name joins words', () => {
    expect(fieldLabel('preferred-date')).toBe('Preferred date');
    expect(fieldLabel('preferred_date')).toBe('Preferred date');
    expect(fieldLabel('preferredDate')).toBe('Preferred date');
  });

  it('leaves a name that is already a sentence alone', () => {
    // The backend's own tests post labels as names, and so may an agent. Title-casing that would read as a
    // mistake, and stripping its punctuation would change what the visitor was asked.
    expect(fieldLabel('How can we help?')).toBe('How can we help?');
    expect(fieldLabel('Your name')).toBe('Your name');
  });

  it('does not invent meaning it cannot have', () => {
    // `qty` stays `Qty`: a plain word the owner can puzzle out beats a confident expansion that is wrong.
    expect(fieldLabel('qty')).toBe('Qty');
    expect(fieldLabel('POSTCODE')).toBe('Postcode');
  });

  it('survives a name that is nothing much', () => {
    expect(fieldLabel('')).toBe('');
    expect(fieldLabel('  ')).toBe('');
    expect(fieldLabel('__')).toBe('__');
  });
});
