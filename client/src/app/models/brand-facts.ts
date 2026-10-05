/**
 * `content/brand.md` as its owner should read it: headings, paragraphs, bullet lists and bold, and nothing else.
 *
 * The settings screen showed the file verbatim, in a `<pre>`, so a florist checking what the assistant believes
 * about her shop read `- **Opening hours:** Monday to Saturday` under a line starting `## Facts` — Markdown syntax,
 * in a product whose promise is that nobody ever has to read code. The file is the agent's memory and the most
 * consequential text in the site, and it is written for the person; the screen was the part that was not.
 *
 * Deliberately just the shapes that file uses rather than a Markdown library: a library renders HTML, and HTML
 * built from a file a model wrote is a thing to sanitise. This builds plain values for the template to bind, so
 * whatever the file contains arrives on screen as text. What is not understood stays as text too — a stray `*`
 * is a small blemish, while a page that guessed wrongly about one could hide a fact.
 */
export interface FactsText {
  text: string;
  bold: boolean;
}

export type FactsBlock =
  | { kind: 'heading'; level: number; text: string }
  | { kind: 'paragraph'; parts: FactsText[] }
  | { kind: 'list'; items: FactsText[][] };

export function brandFacts(source: string): FactsBlock[] {
  const blocks: FactsBlock[] = [];
  let paragraph: string[] = [];
  let list: FactsText[][] | undefined;

  const endParagraph = () => {
    if (paragraph.length > 0) blocks.push({ kind: 'paragraph', parts: inline(paragraph.join(' ')) });
    paragraph = [];
  };

  const endList = () => {
    if (list) blocks.push({ kind: 'list', items: list });
    list = undefined;
  };

  // A comment is addressed to the agent, never to the owner: the template once carried one under the Name fact,
  // explaining a TypeScript constant to somebody who sells flowers.
  const text = source.replace(/\r\n?/g, '\n').replace(/<!--[\s\S]*?-->/g, '');

  for (const raw of text.split('\n')) {
    const line = raw.trim();
    const heading = /^(#{1,6})\s+(.*)$/.exec(line);
    const item = /^[-*+]\s+(.*)$/.exec(line);

    if (line.length === 0) {
      endParagraph();
      endList();
    } else if (heading) {
      endParagraph();
      endList();
      blocks.push({ kind: 'heading', level: heading[1].length, text: plain(heading[2]) });
    } else if (item) {
      endParagraph();
      (list ??= []).push(inline(item[1]));
    } else if (list && /^\s/.test(raw)) {
      // An indented line under a bullet is that bullet, wrapped.
      list[list.length - 1] = [...list[list.length - 1], { text: ` ${line}`, bold: false }];
    } else {
      endList();
      paragraph.push(line);
    }
  }

  endParagraph();
  endList();

  return blocks;
}

/** Bold runs split out; links reduced to their words; code marks dropped. */
function inline(source: string): FactsText[] {
  const parts: FactsText[] = [];
  const text = plainLinks(source).replace(/`([^`]*)`/g, '$1');
  const bold = /\*\*(.+?)\*\*|__(.+?)__/g;
  let at = 0;

  for (const match of text.matchAll(bold)) {
    if (match.index > at) parts.push({ text: text.slice(at, match.index), bold: false });
    parts.push({ text: match[1] ?? match[2], bold: true });
    at = match.index + match[0].length;
  }

  if (at < text.length) parts.push({ text: text.slice(at), bold: false });

  return parts;
}

/** A heading's words, without the marks a heading does not need. */
function plain(source: string): string {
  return plainLinks(source).replace(/\*\*|__|`/g, '');
}

/** `[the words](a url)` → the words. A link the agent wrote is not one the owner needs to follow from here. */
function plainLinks(source: string): string {
  return source.replace(/\[([^\]]*)\]\([^)]*\)/g, '$1');
}
