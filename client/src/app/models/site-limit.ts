/**
 * What an account at its site limit is told, on every screen that would otherwise offer another site.
 *
 * Written once because three screens say it — the sidebar stops offering "New site", the All-sites page says this
 * where its button was, and the new-site page says it instead of its form. Before, the sidebar hid its link at the
 * limit with nothing said, and the All-sites page offered its button anyway: somebody named their fourth site,
 * pressed Create, and was refused with the first half of this sentence. The server's own refusal stays, for the
 * request that gets past all of these.
 */
export function siteLimitReached(sites: number, max: number | undefined): boolean {
  return max !== undefined && sites >= max;
}

export function siteLimitSentence(max: number | undefined): string {
  return `You have ${max ?? 'as many'} sites, the most an account can have for now. Delete one you no longer need to make room for another.`;
}
