/**
 * A posted form field's name, as the owner should read it.
 *
 * What arrives is the `name` attribute from the visitor's form — `message`, `preferred-date`,
 * `preferredDate` — because that is what a browser posts. The `label` the visitor read is never sent, and
 * for a long time three comments in this codebase said otherwise: the messages screen called these "the
 * visitor's own labels", and `ContactForm` told the agent writing the form that its `label` was "the name
 * the owner sees above their answer". It is not. Found by sending the form on a published site and reading
 * the inbox, which showed `name`, `email`, `phone`, `message` in lower case.
 *
 * So this tidies the name instead, and deliberately only as far as a name can be tidied: separators become
 * spaces, a camelCase hump becomes a break, and the first letter is capitalised. It does not retitle,
 * expand or translate — it cannot know that `qty` means quantity, and a confident wrong guess above
 * somebody's enquiry is worse than a plain word. A name already written as prose is left exactly as it is,
 * because a form whose names *are* its labels is allowed and is what the backend's own tests post.
 */
export function fieldLabel(name: string): string {
  const trimmed = name.trim();

  // Already prose: the form named its fields the way it labelled them, so there is nothing to improve and
  // every chance of damaging it. `How can we help?` is not an identifier.
  if (trimmed.length === 0 || /\s/.test(trimmed)) return trimmed;

  const words = trimmed
    .replace(/[-_.]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .split(' ')
    .filter(word => word.length > 0);

  if (words.length === 0) return trimmed;

  return `${words[0][0].toUpperCase()}${words[0].slice(1)}${words.length > 1 ? ` ${words.slice(1).join(' ')}` : ''}`;
}
