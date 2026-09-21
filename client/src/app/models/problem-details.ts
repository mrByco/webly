/**
 * The human-readable half of a ProblemDetails answer.
 *
 * The backend puts the sentence in `title` and a stable code beside it, so there is nothing to
 * translate on this side — but the body does not always arrive parsed. The generated client asks for
 * `responseType: 'text'` on every endpoint that answers 204, so a failure from one of those (deleting
 * a site, promoting a domain) hands back the JSON as a string. Reading only `error.title` there
 * silently falls through to the generic message and hides the one sentence that explains what
 * happened.
 *
 * Written once, and shared, precisely because that is the kind of detail a copied helper gets wrong
 * in four places out of five.
 */
export function messageOf(error: unknown, fallback = 'That could not be saved.'): string {
  const body = (error as { error?: unknown })?.error;

  if (typeof body === 'string') {
    try {
      return (JSON.parse(body) as { title?: string }).title ?? fallback;
    } catch {
      return fallback;
    }
  }

  return (body as { title?: string })?.title ?? hubMessageOf(error) ?? fallback;
}

/**
 * The sentence out of a `HubException`, or nothing.
 *
 * A hub invocation is not an HTTP call and its failures carry no ProblemDetails, so everything the backend says
 * over the hub — that a site could not be found, that a run is not there, that somebody has made a great many
 * changes in the last hour — was arriving here as an `Error` with no `error` property and coming out as the
 * generic fallback. Every one of those sentences exists to be read.
 *
 * What SignalR's JavaScript client throws is one string:
 *
 *   "An unexpected error occurred invoking 'Subscribe' on the server. HubException: That run could not be found."
 *
 * Only the part after `HubException:` is ours. The prefix names a method the person has never heard of, and an
 * error *without* that marker is an unhandled server exception whose message is deliberately not sent — so this
 * returns undefined there and the caller's fallback stands.
 */
function hubMessageOf(error: unknown): string | undefined {
  const message = error instanceof Error ? error.message : undefined;
  const marker = message?.indexOf('HubException: ') ?? -1;

  return marker >= 0 ? message!.slice(marker + 'HubException: '.length).trim() : undefined;
}
