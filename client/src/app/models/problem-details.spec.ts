import { messageOf } from './problem-details';

/**
 * The one place a failure becomes a sentence, and three of its four shapes were found by watching the app show
 * "that could not be saved" over something it knew the answer to.
 */
describe('messageOf', () => {
  it('reads a parsed ProblemDetails body', () => {
    expect(messageOf({ error: { title: 'That site could not be found.' } })).toBe('That site could not be found.');
  });

  // The generated client asks for `responseType: 'text'` on every endpoint that answers 204, so a failure from
  // one of those hands the body back as a string.
  it('reads a ProblemDetails body that arrived as text', () => {
    expect(messageOf({ error: JSON.stringify({ title: 'That domain is already connected.' }) }))
      .toBe('That domain is already connected.');
  });

  // A hub invocation is not HTTP and carries no ProblemDetails. Everything the backend says over the hub used to
  // come out as the fallback — including the turn rate limit, which exists to be read.
  it('reads the sentence out of a HubException', () => {
    const failure = new Error(
      "An unexpected error occurred invoking 'StartChat' on the server. HubException: "
      + 'You have made a lot of changes in the last hour. Try again shortly.');

    expect(messageOf(failure)).toBe('You have made a lot of changes in the last hour. Try again shortly.');
  });

  // Without the marker it is an unhandled server exception, whose message the server deliberately does not send.
  // Showing "An unexpected error occurred invoking 'Subscribe' on the server" to somebody naming a method they
  // have never heard of is worse than the generic sentence.
  it('keeps the fallback for anything else', () => {
    expect(messageOf(new Error('Failed to fetch'))).toBe('That could not be saved.');
    expect(messageOf(undefined)).toBe('That could not be saved.');
    expect(messageOf({ error: 'not json at all' }, 'Nothing was published.')).toBe('Nothing was published.');
  });
});
