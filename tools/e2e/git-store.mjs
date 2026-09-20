// The same git plumbing sequence GitSiteRepositoryStore drives, in node.
//
// It exists because this repository was written in an environment with no .NET SDK, so the C# cannot be
// run — see whats_next.md. Mirroring the sequence here is what makes "does the versioning actually work"
// answerable rather than a claim: the commands, their order and their arguments are the same, so a real
// commit, a real diff and a real restore all happen for real, against real git.
//
// It is a verification harness, not product code. When `dotnet test` can run,
// Webly.Tests/GitSiteRepositoryStoreTests.cs covers the same ground from the side that ships, and this file
// is worth deleting rather than keeping in step.

import { spawn } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { randomUUID } from 'node:crypto';

/** Arguments as arguments, never a composed command line — the rule the C# follows for the same reason. */
export function git(repository, args, { env = {}, input = null, binary = false } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn('git', ['--git-dir', repository, ...args], {
      env: { ...process.env, ...env },
      stdio: ['pipe', 'pipe', 'pipe'],
    });

    const out = [];
    let err = '';

    child.stdout.on('data', chunk => out.push(chunk));
    child.stderr.setEncoding('utf8');
    child.stderr.on('data', text => (err += text));

    child.on('error', reject);
    child.on('close', code => {
      if (code !== 0) return reject(new Error(`git ${args[0]} failed (${code}): ${err}`));

      const buffer = Buffer.concat(out);
      resolve(binary ? buffer : buffer.toString('utf8'));
    });

    if (input !== null) child.stdin.end(input);
    else child.stdin.end();
  });
}

export async function initBare(repository, branch = 'main') {
  await git(repository, ['init', '--bare', `--initial-branch=${branch}`, '--quiet']).catch(async error => {
    // `git init --bare <dir>` needs the directory as an argument, not --git-dir, on some versions.
    if (!/not a git repository|fatal/.test(String(error.message))) throw error;
    await new Promise((resolve, reject) => {
      const child = spawn('git', ['init', '--bare', `--initial-branch=${branch}`, '--quiet', repository]);
      child.on('close', code => (code === 0 ? resolve() : reject(new Error('git init failed'))));
    });
  });
}

export async function resolveHead(repository, branch = 'main') {
  try {
    return (await git(repository, ['rev-parse', `refs/heads/${branch}`])).trim();
  } catch {
    return null;
  }
}

/**
 * Hash every file into a private index, write a tree from it, and commit — the bare-repository equivalent
 * of `git add -A && git commit` with no worktree. Returns null when the tree is identical to the parent's,
 * which is what makes "the agent answered without changing anything" leave no version behind.
 */
export async function commitTree(repository, {
  branch = 'main',
  parentSha = null,
  files,
  author = { name: 'Webly Tester', email: 'tester@example.com' },
  summary,
  details = null,
}) {
  const indexFile = join(tmpdir(), `webly-index-${randomUUID().replaceAll('-', '')}`);
  const env = { GIT_INDEX_FILE: indexFile };

  try {
    for (const file of files) {
      const sha = (await git(repository, ['hash-object', '-w', '--stdin'], { env, input: file.content })).trim();

      // 100644 for everything, like the C#: a Next.js tree has no executables, and a mode arriving from a
      // sandbox would let an agent commit something the build runs.
      await git(repository, ['update-index', '--add', '--cacheinfo', `100644,${sha},${file.path}`], { env });
    }

    const treeSha = (await git(repository, ['write-tree'], { env })).trim();

    if (parentSha) {
      const parentTree = (await git(repository, ['rev-parse', `${parentSha}^{tree}`])).trim();
      if (treeSha === parentTree) return null;
    }

    const message = details ? `${summary}\n\n${details}` : summary;
    const args = ['commit-tree', treeSha];
    if (parentSha) args.push('-p', parentSha);

    const commitSha = (await git(repository, args, {
      input: message,
      env: {
        GIT_AUTHOR_NAME: author.name,
        GIT_AUTHOR_EMAIL: author.email,
        GIT_COMMITTER_NAME: author.name,
        GIT_COMMITTER_EMAIL: author.email,
      },
    })).trim();

    await git(repository, ['update-ref', `refs/heads/${branch}`, commitSha]);

    const changed = parentSha
      ? (await git(repository, ['diff-tree', '--no-commit-id', '--name-only', '-r', '-z', commitSha]))
          .split('\0').filter(Boolean).length
      : files.length;

    return { commitSha, changedFileCount: changed };
  } finally {
    rmSync(indexFile, { force: true });
  }
}

/** `ls-tree -r -l -z`, parsed the way the C# parses it. */
export async function readEntries(repository, commitSha) {
  const output = await git(repository, ['ls-tree', '-r', '-l', '-z', commitSha]);

  return output.split('\0').filter(Boolean).flatMap(record => {
    const tab = record.indexOf('\t');
    if (tab < 0) return [];

    const fields = record.slice(0, tab).split(' ').filter(Boolean);
    if (fields.length < 4 || fields[1] !== 'blob') return [];

    return [{ path: record.slice(tab + 1), sha: fields[2], size: Number(fields[3]) || 0 }];
  });
}

export async function readTree(repository, commitSha) {
  const entries = await readEntries(repository, commitSha);
  const files = [];

  for (const entry of entries) {
    files.push({ path: entry.path, content: await git(repository, ['cat-file', 'blob', entry.sha], { binary: true }) });
  }

  return files;
}

export async function diff(repository, commitSha) {
  const parents = (await git(repository, ['rev-list', '--parents', '-n', '1', commitSha])).trim().split(' ');

  // The first commit has no parent, so there is nothing to diff against — the C# answers an empty string
  // here and the client reads that as "this is where the site started".
  if (parents.length < 2) return '';

  return git(repository, ['diff', parents[1], commitSha]);
}

export function temporaryRepository(name = 'site') {
  const directory = mkdtempSync(join(tmpdir(), 'webly-e2e-'));
  return { root: directory, repository: join(directory, `${name}.git`) };
}
