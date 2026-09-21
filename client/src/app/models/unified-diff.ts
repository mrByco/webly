/**
 * A unified diff, grouped by file.
 *
 * The history used to render `git diff-tree -p` output as one long `<pre>`, which is fine for a one-file turn
 * and unreadable for a restore that moved six. A person looking at their own site's history wants to know
 * which files changed before they want to read any of them — so the diff is parsed here and the screen draws a
 * collapsible block per file with its own counts.
 *
 * Hand-written rather than a library, for the reason the colouring already was: the client has seven runtime
 * dependencies, all framework, and this is sixty lines against a format that has not changed in twenty years.
 * `unified-diff.spec.ts` pins it against output the real command produced.
 */
export type DiffLineKind = 'added' | 'removed' | 'context' | 'hunk';

export interface DiffLine {
  kind: DiffLineKind;
  text: string;
}

export interface DiffFile {
  /** What to show as the file's name: its path now, or its old path when it was deleted. */
  path: string;

  /** `added`, `removed`, `renamed` or `changed` — the word the header carries beside the path. */
  change: 'added' | 'removed' | 'renamed' | 'changed';

  added: number;
  removed: number;

  /** True when git said "Binary files differ" rather than printing lines. Nothing to show, and that is the news. */
  binary: boolean;

  lines: DiffLine[];
}

const FileHeader = /^diff --git a\/(.+?) b\/(.+)$/;

export function parseUnifiedDiff(diff: string): DiffFile[] {
  const files: DiffFile[] = [];
  let current: DiffFile | undefined;

  for (const line of (diff ?? '').split('\n')) {
    const header = FileHeader.exec(line);

    if (header) {
      const [, before, after] = header;

      current = {
        path: after,
        change: before === after ? 'changed' : 'renamed',
        added: 0,
        removed: 0,
        binary: false,
        lines: [],
      };

      files.push(current);
      continue;
    }

    if (!current) continue;

    // The mode lines say what kind of change this is; `---`/`+++` repeat the paths and say nothing the header
    // did not, except for a deletion, where `+++ /dev/null` is how git spells it.
    if (line.startsWith('new file mode')) { current.change = 'added'; continue; }
    if (line.startsWith('deleted file mode')) { current.change = 'removed'; continue; }
    if (line.startsWith('index ') || line.startsWith('similarity ')) continue;
    if (line.startsWith('rename from') || line.startsWith('rename to')) continue;
    if (line.startsWith('old mode') || line.startsWith('new mode')) continue;
    if (line.startsWith('--- ')) continue;
    if (line.startsWith('+++ ')) continue;

    if (line.startsWith('Binary files')) { current.binary = true; continue; }

    if (line.startsWith('@@')) { current.lines.push({ kind: 'hunk', text: line }); continue; }

    // `\ No newline at end of file` is a note about the line above it, not a line of the file.
    if (line.startsWith('\\')) continue;

    if (line.startsWith('+')) {
      current.added++;
      current.lines.push({ kind: 'added', text: line.slice(1) });
      continue;
    }

    if (line.startsWith('-')) {
      current.removed++;
      current.lines.push({ kind: 'removed', text: line.slice(1) });
      continue;
    }

    // A context line starts with one space; the last line of the diff is often empty and is neither.
    if (line.length > 0 || current.lines.length > 0) {
      current.lines.push({ kind: 'context', text: line.startsWith(' ') ? line.slice(1) : line });
    }
  }

  // A trailing empty context line is the split's artefact rather than the file's content.
  for (const file of files) {
    while (file.lines.at(-1)?.kind === 'context' && file.lines.at(-1)?.text === '') file.lines.pop();
  }

  return files;
}
