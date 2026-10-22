#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Catches text that tools damaged in transit (ROADMAP 0.2, docs/research/tandem-lessons.md "text gets damaged
 * in transit"): file tools, heredocs and PowerShell strings have eaten backslashes and turned escapes into real
 * characters.
 *
 *   node scripts/check-text.mjs            check the staged files (the pre-commit hook)
 *   node scripts/check-text.mjs --tracked  check every tracked file, as it is in the index
 *
 * In every text file it refuses:
 *   - bytes that are not valid UTF-8, and the replacement character U+FFFD (a sign of a bad conversion);
 *   - a byte-order mark (U+FEFF), at the start or anywhere else;
 *   - control characters other than tab, LF and CR (C0, DEL and C1).
 * In code and in the client sources it also refuses any character outside ASCII, except in language tables
 * (folders named in LANGUAGE_TABLE_DIRS). Docs, Markdown, JSON, XML and other data may hold non-ASCII text.
 *
 * Binary files (git's "text" attribute unset, or a NUL in the first bytes) are skipped. A problem names the
 * file, line, column and code point. Exit codes: 0 clean, 1 problems, 2 usage or git error.
 */
import { pathToFileURL } from 'node:url';
import { gitText, indexFiles, isBinary, repoRoot } from './lib/git.mjs';

/** File extensions that hold code: these must stay ASCII outside the language tables. */
export const CODE_EXTENSIONS = new Set([
  'cs', 'csx', 'cshtml', 'razor', 'ts', 'tsx', 'mts', 'cts', 'js', 'jsx', 'mjs', 'cjs', 'css', 'scss', 'less',
  'html', 'htm', 'vue', 'ps1', 'psm1', 'psd1', 'sh', 'bash', 'cmd', 'bat',
]);

/** Every file under a folder with one of these names is a language table and may hold any text. */
export const LANGUAGE_TABLE_DIRS = new Set(['lang', 'langs', 'locale', 'locales', 'localization', 'localizations', 'i18n', 'translations']);

/** Client sources: everything inside a Client folder of a project under src/. */
const CLIENT_SOURCE = /^src\/[^/]+\/Client\//;

/** Whether a path must stay ASCII: code outside docs/, and client sources, unless inside a language table. */
export function mustBeAscii(path) {
  const segments = path.split('/');
  if (segments.slice(0, -1).some((segment) => LANGUAGE_TABLE_DIRS.has(segment.toLowerCase()))) return false;
  if (CLIENT_SOURCE.test(path)) return true;
  if (segments[0] === 'docs') return false;
  const name = segments[segments.length - 1];
  const dot = name.lastIndexOf('.');
  return dot > 0 && CODE_EXTENSIONS.has(name.slice(dot + 1).toLowerCase());
}

function describe(codePoint) {
  if (codePoint === 0xfeff) return 'a byte-order mark';
  if (codePoint === 0xfffd) return 'a replacement character (the text was converted badly)';
  if (codePoint < 0x20 || codePoint === 0x7f || (codePoint >= 0x80 && codePoint < 0xa0)) return 'a control character';
  return 'a non-ASCII character in code';
}

function hex(codePoint) {
  return 'U+' + codePoint.toString(16).toUpperCase().padStart(4, '0');
}

/**
 * Checks one file's content. Returns problems `{ path, line, column, codePoint, reason }`, at most `limit` of
 * them (default 20) so one damaged file cannot flood the output.
 */
export function checkContent(path, buffer, { limit = 20 } = {}) {
  let text;
  try {
    text = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(buffer);
  } catch {
    return [{ path, line: 0, column: 0, codePoint: null, reason: 'not valid UTF-8' }];
  }
  const ascii = mustBeAscii(path);
  const problems = [];
  let line = 1;
  let column = 0;
  for (const char of text) {
    const codePoint = char.codePointAt(0);
    column++;
    if (codePoint === 0x0a) {
      line++;
      column = 0;
      continue;
    }
    const control = (codePoint < 0x20 && codePoint !== 0x09 && codePoint !== 0x0d) || codePoint === 0x7f || (codePoint >= 0x80 && codePoint < 0xa0);
    const damaged = codePoint === 0xfeff || codePoint === 0xfffd;
    if (control || damaged || (ascii && codePoint > 0x7e)) {
      problems.push({ path, line, column, codePoint, reason: describe(codePoint) });
      if (problems.length >= limit) break;
    }
  }
  return problems;
}

/** The paths among `paths` whose git "text" attribute is unset (binary by .gitattributes). */
function binaryByAttributes(cwd, paths) {
  if (paths.length === 0) return new Set();
  const out = gitText(cwd, ['check-attr', '--cached', '-z', '--stdin', 'text'], paths.join('\0') + '\0').split('\0');
  const binary = new Set();
  for (let index = 0; index + 2 < out.length; index += 3) {
    if (out[index + 2] === 'unset') binary.add(out[index]);
  }
  return binary;
}

/** Runs the check. `mode` is 'staged' or 'tracked'. Returns `{ exitCode, output }`. */
export function run({ mode = 'staged', cwd = process.cwd() } = {}) {
  let files;
  let binary;
  try {
    const root = repoRoot(cwd);
    files = indexFiles(root, mode);
    binary = binaryByAttributes(
      root,
      files.map((file) => file.path),
    );
  } catch (error) {
    return { exitCode: 2, output: [`check-text: ${error.message.split('\n')[0]}`] };
  }
  const textFiles = files.filter((file) => !binary.has(file.path) && !isBinary(file.content));
  const problems = textFiles.flatMap((file) => checkContent(file.path, file.content));
  const summary = `${mode}, ${textFiles.length} text files`;
  if (problems.length === 0) return { exitCode: 0, output: [`check-text: clean (${summary})`] };
  return {
    exitCode: 1,
    output: [
      `check-text: ${problems.length} problem(s) (${summary})`,
      ...problems.map((p) => `  ${p.path}${p.line ? `:${p.line}:${p.column}` : ''}  ${p.codePoint === null ? '' : hex(p.codePoint) + ' '}${p.reason}`),
      'Fix the file with a byte-exact edit (for example a Node script that writes the escape), not with the file tools.',
    ],
  };
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const args = process.argv.slice(2);
  const mode = args.length === 0 || (args.length === 1 && args[0] === '--staged') ? 'staged' : args.length === 1 && args[0] === '--tracked' ? 'tracked' : null;
  if (!mode) {
    console.error('usage: node scripts/check-text.mjs [--staged | --tracked]');
    process.exit(2);
  }
  const { exitCode, output } = run({ mode });
  (exitCode === 0 ? console.log : console.error)(output.join('\n'));
  process.exit(exitCode);
}
