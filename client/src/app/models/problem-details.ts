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

  if (unreachable(error)) return UNREACHABLE;

  // A 401 with nothing of ours in it is a session that ended — on another device, usually. The page is on its way to
  // the sign-in screen by then (see `sessionInterceptor`); this is the sentence for the moment before it gets there.
  if ((error as { status?: unknown })?.status === 401 && !sentenceOf(body)) return SIGNED_OUT;

  if (typeof body === 'string') {
    try {
      return sentenceOf(JSON.parse(body)) ?? fallback;
    } catch {
      return fallback;
    }
  }

  return sentenceOf(body) ?? hubMessageOf(error) ?? fallback;
}

/**
 * The title, and the detail after it when there is one.
 *
 * The API writes both for whoever is reading — "That domain is already connected to a site." and then "If it is one
 * of yours, remove it there first." — and the second half is usually the one that says what to do. Only the first
 * ever reached a screen: every `Detail` in the controllers, from "Enter it without https://" to "Delete one you no
 * longer need to make room for another", was written, sent, and dropped here. Nothing technical is ever put in a
 * detail — the one place that could have, git's stderr, deliberately is not — so all of it is for people.
 */
function sentenceOf(problem: unknown): string | undefined {
  const { title, detail } = (problem ?? {}) as { title?: unknown; detail?: unknown };

  if (typeof title !== 'string' || !title) return undefined;

  return typeof detail === 'string' && detail ? `${title} ${detail}` : title;
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

const UNREACHABLE = 'Webly cannot be reached right now. Check your connection, or try again in a moment.';

const SIGNED_OUT = 'You have been signed out. Sign in again to carry on.';

/**
 * Whether the request never got an answer from Webly itself: no response at all (status 0 — offline, or the server
 * down), or a gateway in front of it answering for it while it restarts (502, 503, 504), whose body is the proxy's
 * page rather than our ProblemDetails.
 *
 * The fallback used to cover these too, which is how an editor whose server was restarting said "That could not be
 * saved." over a page that had been trying to *load* something — the one sentence on screen, about the wrong verb,
 * blaming the person's change. The truth is shorter and tells them what to do.
 *
 * Exported because it is also the question "is this worth waiting out?" — a restart answers it with yes, and
 * anything carrying our own sentence with no.
 */
export function unreachable(error: unknown): boolean {
  const failure = error as { status?: unknown; error?: unknown };

  if (failure?.status === 0) return true;

  const gateway = failure?.status === 502 || failure?.status === 503 || failure?.status === 504;
  const ours = typeof failure?.error === 'object' && failure.error !== null && 'title' in failure.error;

  return gateway && !ours;
}
