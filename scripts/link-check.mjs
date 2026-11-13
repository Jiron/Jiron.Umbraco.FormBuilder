#!/usr/bin/env node
// Checks the documentation's links (ROADMAP 17.2a): every relative link and image in the Markdown files names a file
// that exists, spelt with the same letter case (GitHub's paths are case-sensitive, though Windows and macOS are not),
// every anchor names a heading in its file (as GitHub makes them), the NuGet readme has no relative link
// (nuget.org can't resolve one), and every page of the backoffice's "Learn more" map exists with its heading. Links to
// other sites are not fetched: the check runs offline. Exits 1 and names each broken link.
// Usage: node scripts/link-check.mjs [files...]   (no files: every Markdown file it covers, see FILES below)
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

/** The backoffice's map of "Learn more" pages, which the client imports. */
export const LEARN_MORE = join(
  root,
  'src',
  'Jiron.Umbraco.FormBuilder.Web',
  'Client',
  'packages',
  'backoffice',
  'src',
  'docs',
  'learn-more.json',
);

/** The NuGet readme, which nuget.org shows without the repository around it. */
export const NUGET_README = join(root, 'docs', 'README.nuget.md');

/**
 * The Markdown files checked by default: the repository's own pages and every page under docs/. The decision records,
 * the work log and the roadmap are history: they keep the links they had when written, and are left out.
 */
export function defaultFiles(base = root) {
  const top = readdirSync(base).filter((name) => name.endsWith('.md') && name !== 'ROADMAP.md');
  const docs = [];
  const walk = (folder) => {
    for (const name of readdirSync(folder)) {
      const path = join(folder, name);
      if (statSync(path).isDirectory()) {
        if (!['decisions', 'research', 'node_modules'].includes(name)) walk(path);
      } else if (name.endsWith('.md') && name !== 'WORKLOG.md') docs.push(path);
    }
  };
  if (existsSync(join(base, 'docs'))) walk(join(base, 'docs'));
  return [...top.map((name) => join(base, name)), ...docs];
}

/** A heading's anchor as GitHub makes it: its text as shown, lower case, without punctuation, spaces as hyphens. */
export function slug(heading) {
  const shown = heading
    .replace(/<[^>]+>/g, '')
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/(\*\*|__)(.+?)\1/g, '$2')
    .replace(/(^|[^\w])[*_](.+?)[*_](?=[^\w]|$)/g, '$1$2');
  return shown
    .trim()
    .toLowerCase()
    .replace(/[^\p{L}\p{M}\p{N}\p{Pc} -]/gu, '')
    .replace(/ /g, '-');
}

/** The lines of a Markdown file outside its fenced code blocks, each in its place, fenced lines left empty. */
function outsideFences(text) {
  let fenced = false;
  return text.split('\n').map((line) => {
    if (/^\s*(```|~~~)/.test(line)) {
      fenced = !fenced;
      return '';
    }
    return fenced ? '' : line;
  });
}

/** A line without its inline code, which holds no links. */
const withoutInlineCode = (line) => line.replace(/`[^`\n]*`/g, (code) => ' '.repeat(code.length));

/** The anchors a Markdown file's headings give, numbered as GitHub numbers repeated ones, and its explicit ids. */
export function anchorsOf(text) {
  const anchors = new Set();
  const seen = new Map();
  for (const line of outsideFences(text)) {
    const heading = /^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$/.exec(line);
    if (heading?.[1]) {
      const base = slug(heading[1]);
      const count = seen.get(base) ?? 0;
      anchors.add(count === 0 ? base : `${base}-${count}`);
      seen.set(base, count + 1);
    }
    for (const id of line.matchAll(/<a\s+(?:id|name)="([^"]+)"/g)) anchors.add(id[1]);
  }
  return anchors;
}

/** The links of a Markdown file: inline, reference definitions and HTML, each with its line. */
export function linksOf(text) {
  const links = [];
  outsideFences(text)
    .map(withoutInlineCode)
    .forEach((line, at) => {
      const add = (target) => links.push({ target: target.trim().replace(/^<|>$/g, ''), line: at + 1 });
      for (const match of line.matchAll(/!?\[(?:[^\]]|\][^(])*?\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g)) add(match[1]);
      const definition = /^ {0,3}\[[^\]]+\]:\s*(\S+)/.exec(line);
      if (definition?.[1]) add(definition[1]);
      for (const match of line.matchAll(/<(?:a|img)\s[^>]*?(?:href|src)="([^"]+)"/g)) add(match[1]);
    });
  return links;
}

const external = (target) => /^[a-z][a-z0-9+.-]*:/i.test(target);

/**
 * The part of a path inside `base` that exists on disk only in another letter case, or null when every part is spelt
 * as on disk or a part does not exist at all: a file system that ignores case finds `Default/` for `default/`, which
 * GitHub does not, and one that heeds case finds neither, so both get the same answer.
 */
export function caseMismatch(path, base = root) {
  const inside = relative(base, path);
  if (!inside || inside.startsWith('..')) return null;
  let at = base;
  for (const part of inside.split(sep).filter(Boolean)) {
    const names = existsSync(at) && statSync(at).isDirectory() ? readdirSync(at) : [];
    if (names.includes(part)) {
      at = join(at, part);
      continue;
    }
    return names.some((name) => name.toLowerCase() === part.toLowerCase()) ? part : null;
  }
  return null;
}

/** The problems of one Markdown file's links, as "file:line: message". */
export function checkFile(file, base = root) {
  const text = readFileSync(file, 'utf8');
  const problems = [];
  const name = relative(base, file).split(sep).join('/');
  const nuget = resolve(file) === resolve(join(base, 'docs', 'README.nuget.md'));
  for (const { target, line } of linksOf(text)) {
    if (external(target)) continue;
    if (nuget) {
      problems.push(`${name}:${line}: a relative link in the NuGet readme, which nuget.org can't resolve: ${target}`);
      continue;
    }
    const [path, anchor] = target.split('#', 2);
    let decoded;
    try {
      decoded = decodeURIComponent((path ?? '').split('?')[0] ?? '');
    } catch {
      problems.push(`${name}:${line}: not a valid address (a stray %): ${target}`);
      continue;
    }
    const destination = decoded ? resolve(dirname(file), decoded) : resolve(file);
    const wrong = caseMismatch(destination, base);
    if (wrong) {
      problems.push(`${name}:${line}: "${wrong}" is spelt with other letter case on disk: ${target}`);
      continue;
    }
    if (!existsSync(destination)) {
      problems.push(`${name}:${line}: no such file: ${target}`);
      continue;
    }
    if (anchor && destination.endsWith('.md') && !anchorsOf(readFileSync(destination, 'utf8')).has(anchor))
      problems.push(`${name}:${line}: no heading "#${anchor}" in ${relative(base, destination).split(sep).join('/')}`);
  }
  return problems;
}

/** The problems of the backoffice's "Learn more" map: each page must exist under docs/, with its heading. */
export function checkLearnMore(map = LEARN_MORE, base = root) {
  if (!existsSync(map)) return [`no Learn more map at ${relative(base, map).split(sep).join('/')}`];
  const { pages } = JSON.parse(readFileSync(map, 'utf8'));
  const problems = [];
  for (const [key, page] of Object.entries(pages)) {
    const [path, anchor] = String(page).split('#', 2);
    const file = join(base, 'docs', path ?? '');
    const wrong = path ? caseMismatch(file, base) : null;
    if (wrong) {
      problems.push(`learn-more.json: "${key}" spells "${wrong}" with other letter case than the file on disk: ${page}`);
      continue;
    }
    if (!path || !existsSync(file)) {
      problems.push(`learn-more.json: "${key}" names no page under docs/: ${page}`);
      continue;
    }
    if (anchor && !anchorsOf(readFileSync(file, 'utf8')).has(anchor))
      problems.push(`learn-more.json: "${key}" names no heading "#${anchor}" in docs/${path}`);
  }
  return problems;
}

function main() {
  const named = process.argv.slice(2);
  const files = named.length > 0 ? named.map((file) => resolve(file)) : defaultFiles();
  const problems = [...files.flatMap((file) => checkFile(file)), ...(named.length > 0 ? [] : checkLearnMore())];
  for (const problem of problems) console.error(`link-check: ${problem}`);
  if (problems.length > 0) {
    console.error(`link-check: ${problems.length} broken link(s) in ${files.length} file(s)`);
    process.exit(1);
  }
  console.log(`link-check: ${files.length} file(s), every relative link and anchor found, the Learn more map whole`);
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) main();
