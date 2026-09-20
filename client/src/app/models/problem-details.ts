/**
 * The human-readable half of a ProblemDetails answer.
 *
 * The backend puts a Hungarian sentence in `title` and a stable code beside it, so there is nothing
 * to translate on this side — but the body does not always arrive parsed. The generated client asks
 * for `responseType: 'text'` on every endpoint that answers 204, so a failure from one of those
 * (deleting an ingredient a recipe still calls for, say) hands back the JSON as a string. Reading
 * only `error.title` there silently falls through to the generic message and hides the one sentence
 * that explains what happened.
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

  return (body as { title?: string })?.title ?? fallback;
}
