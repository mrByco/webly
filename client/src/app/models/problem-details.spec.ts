import { messageOf, unreachable } from './problem-details';

/**
 * The one place a failure becomes a sentence, and three of its four shapes were found by watching the app show
 * "that could not be saved" over something it knew the answer to.
 */
describe('messageOf', () => {
  it('reads a parsed ProblemDetails body', () => {
    expect(messageOf({ error: { title: 'That site could not be found.' } })).toBe('That site could not be found.');
  });

  // The detail is usually the half that says what to do, and it used to be dropped.
  it('says the detail after the title', () => {
    const problem = { title: 'That domain is already connected to a site.', detail: 'If it is one of yours, remove it there first.' };

    expect(messageOf({ error: problem }))
      .toBe('That domain is already connected to a site. If it is one of yours, remove it there first.');
    expect(messageOf({ error: JSON.stringify(problem) }))
      .toBe('That domain is already connected to a site. If it is one of yours, remove it there first.');
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

  // No answer from Webly at all — offline, or the server restarting behind a proxy that answers for it. This used
  // to read "That could not be saved." over a page that was loading, not saving.
  it('says Webly cannot be reached when nothing of ours answered', () => {
    const unreachable = 'Webly cannot be reached right now. Check your connection, or try again in a moment.';

    expect(messageOf({ status: 0, error: { type: 'error' } })).toBe(unreachable);
    expect(messageOf({ status: 502, error: '<html>Bad gateway</html>' })).toBe(unreachable);
    expect(messageOf({ status: 503, error: null })).toBe(unreachable);
  });

  // A session that ended elsewhere answers 401 with no body, and used to read as "That could not be saved."
  it('says somebody was signed out on a bare 401', () => {
    expect(messageOf({ status: 401, error: null })).toBe('You have been signed out. Sign in again to carry on.');
    expect(messageOf({ status: 401, error: { title: 'That email address and password do not match.' } }))
      .toBe('That email address and password do not match.');
  });

  // A 503 that *is* ours — the preview's own sentence, say — carries its title and keeps it.
  it('keeps our own sentence on a gateway status', () => {
    expect(messageOf({ status: 503, error: { title: 'Your preview is asleep.' } })).toBe('Your preview is asleep.');
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

/** What a caller may wait out: no answer at all, or a gateway answering for a server that is not there. */
describe('unreachable', () => {
  it('is true when nothing of ours answered', () => {
    expect(unreachable({ status: 0, error: { type: 'error' } })).toBe(true);
    expect(unreachable({ status: 502, error: '<html>Bad gateway</html>' })).toBe(true);
    expect(unreachable({ status: 504, error: null })).toBe(true);
  });

  it('is false for an answer that is ours, whatever its status', () => {
    expect(unreachable({ status: 503, error: { title: 'Your preview is asleep.' } })).toBe(false);
    expect(unreachable({ status: 404, error: { title: 'That site could not be found.' } })).toBe(false);
    expect(unreachable(undefined)).toBe(false);
  });
});
