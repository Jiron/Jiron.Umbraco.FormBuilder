#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The release workflow's helpers (ROADMAP 18.2, D16, ADR-0247; docs/releasing.md):
 *
 *   node scripts/release.mjs version <tag or version>   the version a tag or a manual run releases:
 *                                                       v17.0.0-alpha.1 -> 17.0.0-alpha.1, on this branch's line only
 *   node scripts/release.mjs notes <version>             that version's section of CHANGELOG.md, without its heading,
 *                                                       each paragraph and list item on one line, as a GitHub release
 *                                                       shows every line break it is given
 *   node scripts/release.mjs compare <folder> <folder>   whether two folders of packs hold the same packages with the
 *                                                       same entries, whatever their versions: the release's pack
 *                                                       steps against the gate's (18.2's dry run)
 *
 * Exit codes: 0 fine, 1 refused or different, 2 usage.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseFileName, zipEntries } from './check-packages.mjs';
import { builtVersion } from './pack.mjs';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));

/**
 * The version a tag (`v17.0.0-alpha.1`, `refs/tags/v17.0.0-alpha.1`) or a manual run (`17.0.0-alpha.1`) releases. It
 * must be a semantic version on this branch's Umbraco major (D16: the package major follows the Umbraco major), which
 * Directory.Build.props' VersionPrefix names.
 */
export function releaseVersion(input, propsText) {
  const version = String(input ?? '')
    .trim()
    .replace(/^refs\/tags\//, '')
    .replace(/^v/, '');
  // Semantic Versioning: no leading zeros in a number, nor in a numeric identifier of a prerelease.
  const identifier = '(0|[1-9]\\d*|\\d*[A-Za-z-][0-9A-Za-z-]*)';
  const semver = new RegExp(`^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(-${identifier}(\\.${identifier})*)?$`);
  if (!semver.test(version)) {
    throw new Error(`"${input}" is not a version such as v17.0.0-alpha.1`);
  }
  const major = propsText.match(/<VersionPrefix>(\d+)\./)?.[1];
  if (!major) throw new Error('Directory.Build.props has no VersionPrefix');
  if (version.split('.')[0] !== major) {
    throw new Error(`${version} is not on this branch's line ${major}.x (Directory.Build.props): the package major follows the Umbraco major`);
  }
  return version;
}

/** The notes of a version: its section of the changelog (`## [17.0.0-alpha.1] - 2026-11-13`), without the heading. */
export function releaseNotes(version, changelog) {
  const sections = new Map();
  let current = null;
  for (const line of changelog.split(/\r?\n/)) {
    const heading = line.match(/^## \[([^\]]+)\]/);
    if (heading) {
      current = heading[1];
      sections.set(current, []);
    } else if (line.startsWith('## ')) {
      current = null;
    } else if (current) {
      sections.get(current).push(line);
    }
  }
  const body = sections.get(version);
  if (!body) throw new Error(`CHANGELOG.md has no section "## [${version}]"; add one before tagging (docs/releasing.md)`);
  const text = body.join('\n').trim();
  if (!text) throw new Error(`the CHANGELOG.md section of ${version} is empty`);
  return `${unwrap(text)}\n`;
}

/**
 * Markdown whose paragraphs and list items are wrapped over several lines, each joined onto one line: a GitHub release
 * keeps every line break its notes hold. Headings, blank lines, list items, tables and code blocks stay as they are.
 */
export function unwrap(markdown) {
  const out = [];
  let fence = false;
  for (const line of markdown.split('\n')) {
    const starts = /^\s*(#|[-*+] |\d+\. |\||```|>)/.test(line) || line.trim() === '';
    if (/^\s*```/.test(line)) fence = !fence;
    const previous = out.at(-1);
    if (!fence && !starts && previous !== undefined && previous.trim() !== '' && !/^\s*(#|\||```)/.test(previous)) {
      out[out.length - 1] = `${previous} ${line.trim()}`;
    } else {
      out.push(line);
    }
  }
  return out.join('\n');
}

/** The version Directory.Build.props names: pack.mjs's reading, VersionPrefix with VersionSuffix when there is one. */
export const propsVersion = builtVersion;

/**
 * A package's entries with what differs from one pack to the next taken out: the version, and the content hash a
 * bundler gives a chunk's name (8 characters before `.js`), which changes with the version stamped into the client.
 */
export function normalisedEntries(names, version) {
  return names
    .map((name) => name.replaceAll(version, '<version>').replace(/-[A-Za-z0-9_-]{8}\.js$/, '-<hash>.js'))
    .sort();
}

/** The packages of a folder, each as its id and kind with its normalised entries. */
export function packsOf(folder, read = (file) => readFileSync(join(folder, file))) {
  const packs = new Map();
  for (const file of readdirSync(folder).filter((name) => /\.s?nupkg$/i.test(name))) {
    const parsed = parseFileName(file);
    if (!parsed) continue;
    packs.set(`${parsed.id}${parsed.symbols ? '.snupkg' : '.nupkg'}`, normalisedEntries(zipEntries(read(file)), parsed.version));
  }
  return packs;
}

/** What differs between two folders of packs: missing or extra packages, and entries one has and the other has not. */
export function comparePacks(a, b) {
  const problems = [];
  for (const key of new Set([...a.keys(), ...b.keys()])) {
    if (!a.has(key) || !b.has(key)) {
      problems.push(`${key} is only in the ${a.has(key) ? 'first' : 'second'} folder`);
      continue;
    }
    const first = a.get(key);
    const second = b.get(key);
    const count = (list) => list.reduce((map, name) => map.set(name, (map.get(name) ?? 0) + 1), new Map());
    const [left, right] = [count(first), count(second)];
    for (const name of new Set([...left.keys(), ...right.keys()])) {
      const difference = (left.get(name) ?? 0) - (right.get(name) ?? 0);
      if (difference > 0) problems.push(`${key}: ${name} is only in the first folder${difference > 1 ? ` (${difference} more)` : ''}`);
      if (difference < 0) problems.push(`${key}: ${name} is only in the second folder${difference < -1 ? ` (${-difference} more)` : ''}`);
    }
  }
  return problems;
}

function main() {
  const [command, first, second] = process.argv.slice(2);
  try {
    if (command === 'version' && first) {
      console.log(releaseVersion(first, readFileSync(join(root, 'Directory.Build.props'), 'utf8')));
    } else if (command === 'notes' && first) {
      process.stdout.write(releaseNotes(first, readFileSync(join(root, 'CHANGELOG.md'), 'utf8')));
    } else if (command === 'compare' && first && second) {
      const [a, b] = [packsOf(resolve(first)), packsOf(resolve(second))];
      const problems = comparePacks(a, b);
      for (const problem of problems) console.error(`release: ${problem}`);
      if (problems.length > 0) process.exit(1);
      const entries = [...a.values()].reduce((sum, list) => sum + list.length, 0);
      console.log(`release: the same ${a.size} files with the same ${entries} entries in ${first} and ${second}`);
    } else {
      console.error('usage: node scripts/release.mjs version <tag or version> | notes <version> | compare <folder> <folder>');
      process.exit(2);
    }
  } catch (error) {
    console.error(`release: ${error.message}`);
    process.exit(1);
  }
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) main();
