// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Git plumbing shared by the repository checks (secret scan, term guard, text check). Everything runs git
 * directly from Node (no shell, no re-encoding), so file content reaches the checks byte for byte.
 */
import { execFileSync, spawn } from 'node:child_process';
import { createInterface } from 'node:readline';

export const EMPTY_TREE = '4b825dc642cb6eb9a060e54bf8d69288fbee4904';
export const BINARY_SNIFF_BYTES = 8000;

/** Runs git and returns stdout as a Buffer. Throws on a non-zero exit. */
export function git(cwd, args, input) {
  return execFileSync('git', args, { cwd, input, maxBuffer: 1 << 30, stdio: ['pipe', 'pipe', 'pipe'] });
}

/** Runs git and returns stdout as UTF-8 text. */
export function gitText(cwd, args, input) {
  return git(cwd, args, input).toString('utf8');
}

/** The repository root that contains `cwd`. Throws outside a repository. */
export function repoRoot(cwd) {
  return gitText(cwd, ['rev-parse', '--show-toplevel']).trim();
}

/** True when the first bytes hold a NUL, the same test git uses for "binary". */
export function isBinary(buffer) {
  return buffer.subarray(0, BINARY_SNIFF_BYTES).includes(0);
}

/** Reads blobs by id with one `git cat-file --batch` call. Returns a Map of id to Buffer. */
export function readBlobs(cwd, ids) {
  const unique = [...new Set(ids)];
  const blobs = new Map();
  if (unique.length === 0) return blobs;
  const out = git(cwd, ['cat-file', '--batch'], unique.join('\n') + '\n');
  let pos = 0;
  while (pos < out.length) {
    const newline = out.indexOf(0x0a, pos);
    const [id, type, size] = out.subarray(pos, newline).toString('utf8').split(' ');
    pos = newline + 1;
    if (type === 'missing') continue;
    const length = Number(size);
    blobs.set(id, out.subarray(pos, pos + length));
    pos += length + 1;
  }
  return blobs;
}

/** Index entries `{ mode, id, path }`, gitlinks (submodules) skipped. */
export function indexEntries(cwd) {
  return gitText(cwd, ['ls-files', '--stage', '-z'])
    .split('\0')
    .filter(Boolean)
    .map((entry) => {
      const tab = entry.indexOf('\t');
      const [mode, id] = entry.slice(0, tab).split(' ');
      return { mode, id, path: entry.slice(tab + 1) };
    })
    .filter((entry) => entry.mode !== '160000');
}

/** The paths that are staged as added, copied, modified, renamed or type-changed. */
export function stagedPaths(cwd) {
  let base = EMPTY_TREE;
  try {
    git(cwd, ['rev-parse', '--verify', '--quiet', 'HEAD']);
    base = 'HEAD';
  } catch {
    // Unborn branch: everything staged is new.
  }
  return new Set(
    gitText(cwd, ['diff', '--cached', '--name-only', '-z', '--diff-filter=ACMRT', base]).split('\0').filter(Boolean),
  );
}

/**
 * The index entries to check with their content: every entry for 'tracked', the staged ones for 'staged'.
 * Returns `[{ path, content: Buffer }]`.
 */
export function indexFiles(cwd, mode) {
  const only = mode === 'staged' ? stagedPaths(cwd) : null;
  const entries = indexEntries(cwd).filter((entry) => !only || only.has(entry.path));
  const blobs = readBlobs(
    cwd,
    entries.map((entry) => entry.id),
  );
  return entries.filter((entry) => blobs.has(entry.id)).map((entry) => ({ path: entry.path, content: blobs.get(entry.id) }));
}

export const LOG_ARGS = ['--all', '--no-color', '--no-ext-diff', '--no-textconv', '--diff-merges=first-parent'];

/**
 * Streams `git log -p --all` and calls `onLine({ commit, path, line, text })` for every added line of a text
 * file (binary files have no lines in a patch). Returns the number of commits read.
 */
export async function forEachAddedLine(cwd, onLine) {
  const args = ['-c', 'core.quotePath=false', 'log', '-p', ...LOG_ARGS, '--src-prefix=a/', '--dst-prefix=b/', '--format=%x01%H'];
  const child = spawn('git', args, { cwd, stdio: ['ignore', 'pipe', 'pipe'] });
  let stderr = '';
  child.stderr.on('data', (chunk) => (stderr += chunk));
  const exited = new Promise((resolve) => child.on('close', resolve));

  let commit = '';
  let path = null;
  let inHunk = false;
  let lineNumber = 0;
  let commits = 0;
  for await (const line of createInterface({ input: child.stdout, crlfDelay: Infinity })) {
    if (line.startsWith('\x01')) {
      commit = line.slice(1, 8);
      commits++;
      path = null;
      inHunk = false;
      continue;
    }
    if (inHunk) {
      const kind = line[0];
      if (kind === '+') {
        if (path) onLine({ commit, path, line: lineNumber, text: line.slice(1) });
        lineNumber++;
        continue;
      }
      if (kind === ' ') {
        lineNumber++;
        continue;
      }
      if (kind === '-' || kind === '\\') continue;
      inHunk = false;
    }
    if (line.startsWith('diff --git ')) {
      path = null;
    } else if (line.startsWith('+++ ')) {
      let target = line.slice(4).replace(/\t$/, '');
      if (target.startsWith('"') && target.endsWith('"')) target = target.slice(1, -1);
      path = target === '/dev/null' ? null : target.replace(/^b\//, '');
    } else if (line.startsWith('@@')) {
      const start = /\+(\d+)/.exec(line.slice(2));
      lineNumber = start ? Number(start[1]) : 1;
      inHunk = true;
    }
  }
  const code = await exited;
  if (code !== 0) throw new Error(`git log failed: ${stderr.trim()}`);
  return commits;
}

/** Every path ever added, copied or renamed on any ref. */
export function historyPaths(cwd) {
  const out = gitText(cwd, ['-c', 'core.quotePath=false', 'log', ...LOG_ARGS, '--format=', '--name-only', '-z', '--diff-filter=ACR']);
  return new Set(out.split(/[\0\n]/).filter(Boolean));
}

/** Every commit on any ref with its full message: `[{ commit, message }]`. */
export function historyMessages(cwd) {
  const out = gitText(cwd, ['log', '--all', '--format=%x01%H%x00%B']);
  return out
    .split('\x01')
    .filter(Boolean)
    .map((record) => {
      const nul = record.indexOf('\0');
      return { commit: record.slice(0, 7), message: record.slice(nul + 1) };
    });
}

/** Every ref (branches, remote branches, tags, the stash) with its short name and, for annotated tags, the message. */
export function refs(cwd) {
  const out = gitText(cwd, ['for-each-ref', '--format=%(refname)%00%(objecttype)%00%(contents)%01']);
  return out
    .split('\x01')
    .map((record) => record.replace(/^\n/, ''))
    .filter(Boolean)
    .map((record) => {
      const [ref, type, contents] = record.split('\0');
      return { ref, message: type === 'tag' ? contents : '' };
    });
}
