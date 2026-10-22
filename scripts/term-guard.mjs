#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Blocks the owner's private term list from tracked files and commit metadata (ROADMAP 0.2, CLAUDE.md rule 2).
 *
 *   node scripts/term-guard.mjs                    scan the staged files (the pre-commit hook)
 *   node scripts/term-guard.mjs --message <file>   scan a commit message file (the commit-msg hook)
 *   node scripts/term-guard.mjs --tracked          scan every tracked file, as it is in the index
 *   node scripts/term-guard.mjs --history          scan every line ever added on any ref, every path, every
 *                                                  commit message, every ref name and every tag message
 *   --terms <file>                                 use another list instead of .private/terms.txt
 *
 * The list holds one case-insensitive JavaScript regular expression per line; blank lines and lines starting
 * with "#" are ignored. Text is matched line by line, after Unicode NFC normalisation.
 *
 * A hit names the file and line (or the commit) and the pattern's number in the list, never the term or the
 * matched text. A path that itself matches a pattern is shown as "<redacted path sha256:...>", where the hash
 * is the first 12 hex digits of the SHA-256 of the path. Without the default list file (other clones, CI) the
 * guard prints "term list not present, skipped" and exits 0.
 *
 * Exit codes: 0 clean or skipped, 1 hits, 2 usage, list or git error.
 */
import { createHash } from 'node:crypto';
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { forEachAddedLine, historyMessages, historyPaths, indexFiles, isBinary, refs, repoRoot } from './lib/git.mjs';

export const DEFAULT_TERMS_FILE = join('.private', 'terms.txt');

/**
 * Reads a term list. Returns `{ patterns: RegExp[] }` or `{ error }`. The error never contains the pattern.
 */
export function parseTerms(text) {
  const patterns = [];
  const lines = text.replace(/^\uFEFF/, '').split(/\r?\n/);
  for (let index = 0; index < lines.length; index++) {
    const source = lines[index].trim();
    if (source === '' || source.startsWith('#')) continue;
    try {
      patterns.push(new RegExp(source.normalize('NFC'), 'i'));
    } catch {
      return { error: `pattern ${patterns.length + 1} (line ${index + 1} of the list) is not a valid regular expression` };
    }
  }
  return { patterns };
}

/** The 1-based numbers of the patterns that match one line of text. */
export function matchingPatterns(patterns, text) {
  const normalised = text.normalize('NFC');
  const numbers = [];
  patterns.forEach((pattern, index) => {
    if (pattern.test(normalised)) numbers.push(index + 1);
  });
  return numbers;
}

/** Scans a text line by line. Returns `[{ line, pattern }]` with 1-based numbers. */
export function scanText(patterns, text) {
  const hits = [];
  text.split(/\r?\n/).forEach((line, index) => {
    for (const pattern of matchingPatterns(patterns, line)) hits.push({ line: index + 1, pattern });
  });
  return hits;
}

/** A path as it may be printed: itself, or a short hash when the path matches a pattern. */
export function displayPath(patterns, path) {
  if (matchingPatterns(patterns, path).length === 0) return path;
  return `<redacted path sha256:${createHash('sha256').update(path, 'utf8').digest('hex').slice(0, 12)}>`;
}

function pathHits(patterns, paths, where) {
  const hits = [];
  for (const path of paths) {
    for (const pattern of matchingPatterns(patterns, path)) hits.push(`  ${where(displayPath(patterns, path))}  pattern #${pattern}`);
  }
  return hits;
}

function scanFiles(patterns, files) {
  const hits = pathHits(
    patterns,
    files.map((file) => file.path),
    (shown) => `the file path ${shown}`,
  );
  for (const file of files) {
    if (isBinary(file.content)) continue;
    const shown = displayPath(patterns, file.path);
    for (const hit of scanText(patterns, file.content.toString('utf8'))) hits.push(`  ${shown}:${hit.line}  pattern #${hit.pattern}`);
  }
  return hits;
}

async function scanHistory(patterns, root) {
  const hits = pathHits(patterns, historyPaths(root), (shown) => `the path ${shown} (added on some ref)`);
  const commits = await forEachAddedLine(root, ({ commit, path, line, text }) => {
    for (const pattern of matchingPatterns(patterns, text)) {
      hits.push(`  commit ${commit} ${displayPath(patterns, path)}:${line}  pattern #${pattern}`);
    }
  });
  const messages = historyMessages(root);
  for (const { commit, message } of messages) {
    for (const hit of scanText(patterns, message)) hits.push(`  commit ${commit} message line ${hit.line}  pattern #${hit.pattern}`);
  }
  refs(root).forEach(({ ref, message }, index) => {
    const name = `ref #${index + 1}`;
    for (const pattern of matchingPatterns(patterns, ref)) hits.push(`  the name of ${name}  pattern #${pattern}`);
    for (const hit of scanText(patterns, message)) hits.push(`  the tag message of ${name} line ${hit.line}  pattern #${hit.pattern}`);
  });
  return { hits, summary: `${commits} commits, ${messages.length} messages` };
}

/**
 * Runs the guard. `mode` is 'staged', 'tracked', 'history' or 'message' (with `messageFile`). `termsFile`
 * names another list; without it, the default list is used when it exists. Returns `{ exitCode, output }`.
 */
export async function run({ mode = 'staged', cwd = process.cwd(), termsFile, messageFile } = {}) {
  let root;
  try {
    root = repoRoot(cwd);
  } catch {
    return { exitCode: 2, output: ['term-guard: not inside a git repository'] };
  }

  const listFile = termsFile ? resolve(cwd, termsFile) : join(root, DEFAULT_TERMS_FILE);
  if (!existsSync(listFile)) {
    if (termsFile) return { exitCode: 2, output: ['term-guard: the --terms file does not exist'] };
    return { exitCode: 0, output: [`term-guard: term list not present, skipped (${mode}, 0 patterns loaded)`] };
  }
  const { patterns, error } = parseTerms(readFileSync(listFile, 'utf8'));
  if (error) return { exitCode: 2, output: [`term-guard: ${error}`] };

  let hits;
  let summary;
  try {
    if (mode === 'message') {
      const text = readFileSync(resolve(cwd, messageFile), 'utf8');
      hits = scanText(patterns, text).map((hit) => `  commit message line ${hit.line}  pattern #${hit.pattern}`);
      summary = 'commit message';
    } else if (mode === 'history') {
      ({ hits, summary } = await scanHistory(patterns, root));
    } else {
      const files = indexFiles(root, mode);
      hits = scanFiles(patterns, files);
      summary = `${files.length} files`;
    }
  } catch (failure) {
    return { exitCode: 2, output: [`term-guard: ${failure.message}`] };
  }

  const counts = `${mode}, ${summary}, ${patterns.length} patterns loaded`;
  if (hits.length === 0) return { exitCode: 0, output: [`term-guard: clean (${counts})`] };
  return { exitCode: 1, output: [`term-guard: ${hits.length} hit(s) (${counts})`, ...hits] };
}

/** Parses the command line: `{ mode, termsFile, messageFile }`, or null on bad usage. */
export function parseArgs(args) {
  let mode = null;
  let termsFile;
  let messageFile;
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    const setMode = (value) => {
      if (mode) throw new Error('one mode only');
      mode = value;
    };
    try {
      if (arg === '--staged') setMode('staged');
      else if (arg === '--tracked') setMode('tracked');
      else if (arg === '--history') setMode('history');
      else if (arg === '--message') {
        setMode('message');
        messageFile = args[++index];
        if (!messageFile) return null;
      } else if (arg === '--terms') {
        termsFile = args[++index];
        if (!termsFile) return null;
      } else return null;
    } catch {
      return null;
    }
  }
  return { mode: mode ?? 'staged', termsFile, messageFile };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const options = parseArgs(process.argv.slice(2));
  if (!options) {
    console.error('usage: node scripts/term-guard.mjs [--staged | --tracked | --history | --message <file>] [--terms <file>]');
    process.exit(2);
  }
  const { exitCode, output } = await run(options);
  (exitCode === 0 ? console.log : console.error)(output.join('\n'));
  process.exit(exitCode);
}
