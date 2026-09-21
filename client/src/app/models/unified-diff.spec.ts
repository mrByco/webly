import { parseUnifiedDiff } from './unified-diff';

/**
 * The samples are what `git diff-tree --no-color --no-commit-id -p --root` actually printed for this product's
 * own repositories — an agent turn changing one line, and a site's first commit, where every file is new.
 */
describe('parseUnifiedDiff', () => {
  const oneLine =
    'diff --git a/src/app/page.tsx b/src/app/page.tsx\n'
    + 'index 495d88f..4bc39d8 100644\n'
    + '--- a/src/app/page.tsx\n'
    + '+++ b/src/app/page.tsx\n'
    + '@@ -12,7 +12,7 @@ export default function Home() {\n'
    + '   return (\n'
    + '     <>\n'
    + '       <Hero\n'
    + '-        headline="Say we are a bakery in Delft, open since 1931"\n'
    + '+        headline="Your new website"\n'
    + '         subheadline="Tell Webly what this site is about."\n';

  it('groups by file and counts what changed', () => {
    const [file] = parseUnifiedDiff(oneLine);

    expect(file.path).toBe('src/app/page.tsx');
    expect(file.change).toBe('changed');
    expect(file.added).toBe(1);
    expect(file.removed).toBe(1);
    expect(file.binary).toBe(false);
  });

  // The marker character is the diff's, not the file's, so it comes off — otherwise every line in the viewer
  // is indented by one and copying a line out of it gives you something that does not compile.
  it('keeps the lines without their marker', () => {
    const [file] = parseUnifiedDiff(oneLine);

    expect(file.lines.find(line => line.kind === 'added')?.text).toBe('        headline="Your new website"');
    expect(file.lines[0]).toEqual({ kind: 'hunk', text: '@@ -12,7 +12,7 @@ export default function Home() {' });
    expect(file.lines.some(line => line.text.startsWith('+') || line.text.startsWith('-'))).toBe(false);
  });

  it('reads several files out of one diff', () => {
    const two = oneLine
      + 'diff --git a/content/brand.md b/content/brand.md\n'
      + 'new file mode 100644\n'
      + 'index 0000000..e69de29\n'
      + '--- /dev/null\n'
      + '+++ b/content/brand.md\n'
      + '@@ -0,0 +1,2 @@\n'
      + '+# Delft Bakery\n'
      + '+Open since 1931.\n';

    const files = parseUnifiedDiff(two);

    expect(files.map(file => file.path)).toEqual(['src/app/page.tsx', 'content/brand.md']);
    expect(files[1].change).toBe('added');
    expect(files[1].added).toBe(2);
    expect(files[1].removed).toBe(0);
  });

  it('notices a deletion, a rename and a binary file', () => {
    const files = parseUnifiedDiff(
      'diff --git a/src/app/old.tsx b/src/app/old.tsx\n'
      + 'deleted file mode 100644\n'
      + '--- a/src/app/old.tsx\n'
      + '+++ /dev/null\n'
      + '@@ -1,1 +0,0 @@\n'
      + '-export const gone = true;\n'
      + 'diff --git a/a.tsx b/b.tsx\n'
      + 'similarity index 100%\n'
      + 'rename from a.tsx\n'
      + 'rename to b.tsx\n'
      + 'diff --git a/public/logo.png b/public/logo.png\n'
      + 'index 1111111..2222222 100644\n'
      + 'Binary files a/public/logo.png and b/public/logo.png differ\n');

    expect(files.map(file => file.change)).toEqual(['removed', 'renamed', 'changed']);
    expect(files[0].removed).toBe(1);
    expect(files[1].lines).toEqual([]);
    expect(files[2].binary).toBe(true);
  });

  // The first commit of a site has no parent, so the API answers with an empty string rather than a diff.
  it('answers nothing for nothing', () => {
    expect(parseUnifiedDiff('')).toEqual([]);
    expect(parseUnifiedDiff('   \n')).toEqual([]);
  });
});
