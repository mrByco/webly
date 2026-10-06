import { siteLimitReached, siteLimitSentence } from './site-limit';

describe('the site limit', () => {
  it('is reached at the number, not past it', () => {
    expect(siteLimitReached(2, 3)).toBe(false);
    expect(siteLimitReached(3, 3)).toBe(true);
  });

  // A profile that has not loaded yet says nothing about a limit, and must not hide the button meanwhile.
  it('is never reached without a number to reach', () => {
    expect(siteLimitReached(5, undefined)).toBe(false);
  });

  it('names the number and what to do', () => {
    expect(siteLimitSentence(3)).toBe(
      'You have 3 sites, the most an account can have for now. Delete one you no longer need to make room for another.');
  });
});
