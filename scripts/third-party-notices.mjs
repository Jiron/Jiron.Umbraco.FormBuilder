#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Generates and checks THIRD-PARTY-NOTICES.md (ROADMAP 3.5, CLAUDE.md rule 4):
 *
 *   - "Shipped to sites": the table of every package Jiron Form Builder's packages add to a site beyond Umbraco CMS
 *     (the shipped tier of scripts/lib/nuget.mjs), with licence and copyright, written between the jfb-shipped
 *     markers. A licence that scripts/check-nuget-licences.mjs refuses fails here too;
 *   - "Development only": the hand-written table must name every direct development tool, with the versions
 *     restored now and the licence their metadata gives: the devDependencies of every npm project
 *     (check-npm-licences.mjs, LOCKS) and the NuGet packages that only the tests and the demo reference. A row
 *     whose package nothing restores is stale;
 *   - the development packages that only ADR-0027's public-domain dedications allow, written between the
 *     jfb-public-domain markers.
 *
 * The nuspecs come from NuGet's packages folder: NUGET_PACKAGES when it is set, as CI sets it, and the default
 * cache otherwise (scripts/lib/nuget.mjs, packagesFolder).
 *
 *   node scripts/third-party-notices.mjs           write the generated parts
 *   node scripts/third-party-notices.mjs --check   fail when the file is not current
 *
 * Needs `dotnet restore` first. Exit codes: 0 current (or written), 1 stale or a problem, 2 usage.
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildFileTexts, readReferences } from './check-audit-suppressions.mjs';
import { LOCKS, licenceOf, needsPublicDomain, thirdParty } from './check-npm-licences.mjs';
import { describe, judge } from './check-nuget-licences.mjs';
import { packageMetadata, packagesFolder, solutionPackages } from './lib/nuget.mjs';

const repo = resolve(fileURLToPath(new URL('..', import.meta.url)));

export const NOTICES = 'THIRD-PARTY-NOTICES.md';
export const SHIPPED_START = '<!-- jfb-shipped:start -->';
export const SHIPPED_END = '<!-- jfb-shipped:end -->';
export const PUBLIC_DOMAIN_START = '<!-- jfb-public-domain:start -->';
export const PUBLIC_DOMAIN_END = '<!-- jfb-public-domain:end -->';
export const DEV_HEADING = '## Development only';

/** Replaces the text between two markers; throws when they are missing. */
export function replaceBetween(text, start, end, content) {
  const from = text.indexOf(start);
  const to = text.indexOf(end);
  if (from < 0 || to < from) throw new Error(`${NOTICES} lacks the markers ${start} ... ${end}`);
  return `${text.slice(0, from + start.length)}\n${content}\n${text.slice(to)}`;
}

const cell = (value) => String(value).replaceAll('|', '\\|') || '-';

/** The "Shipped to sites" part: a table, or the sentence that says there is nothing to list. */
export function renderShipped(rows) {
  if (rows.length === 0) {
    return "None: Jiron Form Builder's packages bring nothing to a site that Umbraco CMS does not already bring.";
  }
  const lines = ['| Package | Version | Licence | Copyright |', '| --- | --- | --- | --- |'];
  for (const row of rows) {
    const licence = row.note ? `${row.licence} (${row.note})` : row.licence;
    lines.push(`| \`${cell(row.id)}\` | ${cell(row.version)} | ${cell(licence)} | ${cell(row.copyright)} |`);
  }
  return lines.join('\n');
}

/** The ADR-0027 part: the development packages only a public-domain dedication allows. */
export function renderPublicDomain(rows) {
  if (rows.length === 0) return 'None.';
  const lines = ['| Package | Version | Licence | Where |', '| --- | --- | --- | --- |'];
  for (const row of rows) lines.push(`| \`${cell(row.name)}\` | ${cell(row.version)} | ${cell(row.licence)} | \`${cell(row.where)}\` |`);
  return lines.join('\n');
}

/** The rows of the hand-written development table: [{ names, versions, licences, line }]. */
export function readDevTable(text) {
  const from = text.search(new RegExp(`^${DEV_HEADING}`, 'm'));
  if (from < 0) throw new Error(`${NOTICES} lacks the heading "${DEV_HEADING}"`);
  const rest = text.slice(from);
  const next = rest.indexOf('\n## ');
  const section = next < 0 ? rest : rest.slice(0, next);
  const rows = [];
  for (const line of section.split(/\r?\n/)) {
    if (!line.startsWith('|') || /^\|\s*-/.test(line) || /^\|\s*Package\s*\|/.test(line)) continue;
    const cells = line.split('|').slice(1, -1).map((part) => part.trim());
    const split = (value) => (value ?? '').split(/,\s*/).filter(Boolean);
    rows.push({ names: [...cells[0].matchAll(/`([^`]+)`/g)].map((match) => match[1]), versions: split(cells[1]), licences: split(cells[2]), line });
  }
  return rows;
}

/**
 * What a row says about each of its packages: [{ name, versions: Set, licences: Set }]. A list as long as the names
 * pairs up with them; a single entry belongs to every name; a row with one name may list several. Throws otherwise.
 */
export function expandRow(row) {
  const pick = (list, index, label) => {
    if (list.length === row.names.length) return new Set([list[index]]);
    if (list.length === 1) return new Set(list);
    if (row.names.length === 1) return new Set(list);
    throw new Error(`the row "${row.line}" has ${list.length} ${label} for ${row.names.length} packages`);
  };
  if (row.names.length === 0) throw new Error(`the row "${row.line}" names no package in backticks`);
  return row.names.map((name, index) => ({ name, versions: pick(row.versions, index, 'versions'), licences: pick(row.licences, index, 'licences') }));
}

const add = (map, key, value) => map.set(key, new Set([...(map.get(key) ?? []), value]));

/**
 * What the npm projects restore: the direct development tools and, per top-level package, its versions and licences.
 * The lock files are enough; nothing needs installing.
 */
export function npmFacts(root, locks = LOCKS) {
  const direct = new Set();
  const versions = new Map();
  const licences = new Map();
  const dedicated = [];
  for (const { file, label } of locks) {
    const lockPath = join(root, file);
    if (!existsSync(lockPath)) throw new Error(`${file} is missing`);
    const lock = JSON.parse(readFileSync(lockPath, 'utf8'));
    const manifest = JSON.parse(readFileSync(join(root, dirname(file), 'package.json'), 'utf8'));
    for (const name of Object.keys(manifest.devDependencies ?? {})) direct.add(name);
    for (const pkg of thirdParty(lock)) {
      if (pkg.path === `node_modules/${pkg.name}`) {
        add(versions, pkg.name, pkg.version);
        add(licences, pkg.name, licenceOf(lock.packages[pkg.path]) || 'no licence');
      }
      if (pkg.tier === 'dev' && needsPublicDomain(pkg.licence)) dedicated.push({ name: pkg.name, version: pkg.version, licence: pkg.licence, where: label });
    }
  }
  return { direct, versions, licences, dedicated };
}

/**
 * What NuGet restores: the packages only the tests and the demo reference directly, and per package id (lower
 * case) its versions and licences, plus the shipped rows and the ADR-0027 packages.
 */
export function nugetFacts(root, cache) {
  const shippedRefs = new Set();
  const devRefs = new Map();
  for (const { file, text } of buildFileTexts(root)) {
    for (const ref of readReferences(text)) {
      if (ref.kind !== 'PackageReference') continue;
      if (file.startsWith('src/')) shippedRefs.add(ref.packageId.toLowerCase());
      else if (file.startsWith('tests/') || file.startsWith('demo/')) devRefs.set(ref.packageId.toLowerCase(), ref.packageId);
    }
  }
  const direct = new Set([...devRefs.keys()].filter((id) => !shippedRefs.has(id)));
  const versions = new Map();
  const licences = new Map();
  const shipped = [];
  const dedicated = [];
  for (const pkg of solutionPackages(root)) {
    const meta = packageMetadata(cache, pkg);
    const verdict = judge(pkg, meta);
    add(versions, pkg.id.toLowerCase(), pkg.version);
    add(licences, pkg.id.toLowerCase(), verdict.licence);
    if (pkg.tier === 'shipped') shipped.push({ ...pkg, ...verdict, copyright: meta.copyright });
    if (pkg.tier === 'dev' && meta.kind === 'expression' && needsPublicDomain(meta.value)) {
      dedicated.push({ name: pkg.id, version: pkg.version, licence: describe(meta), where: pkg.projects.join(', ') });
    }
  }
  return { direct, versions, licences, shipped, dedicated };
}

const sameSet = (a, b) => a.size === b.size && [...a].every((value) => b.has(value));
const listed = (set) => [...set].sort().join(', ');

/** What is wrong with the development table, as report lines. */
export function devTableProblems(rows, npm, nuget) {
  const problems = [];
  const named = new Set();
  for (const row of rows) {
    let entries;
    try {
      entries = expandRow(row);
    } catch (error) {
      problems.push(error.message);
      continue;
    }
    for (const { name, versions, licences } of entries) {
      named.add(name.toLowerCase());
      const facts = npm.versions.has(name)
        ? { versions: npm.versions.get(name), licences: npm.licences.get(name) }
        : nuget.versions.has(name.toLowerCase())
          ? { versions: nuget.versions.get(name.toLowerCase()), licences: nuget.licences.get(name.toLowerCase()) }
          : undefined;
      if (!facts) {
        problems.push(`${name} is listed, but nothing restores it: remove its row`);
        continue;
      }
      if (!sameSet(versions, facts.versions)) problems.push(`${name}: the table says ${listed(versions)}, restored is ${listed(facts.versions)}`);
      if (!sameSet(licences, facts.licences)) problems.push(`${name}: the table says the licence ${listed(licences)}, its metadata says ${listed(facts.licences)}`);
    }
  }
  for (const name of npm.direct) {
    if (!named.has(name.toLowerCase())) problems.push(`${name} is a development tool of an npm project but has no row`);
  }
  for (const id of nuget.direct) {
    if (!named.has(id)) problems.push(`the NuGet package ${id} is referenced by the tests or the demo but has no row`);
  }
  return problems;
}

/** Builds the current file: { text, updated, problems, shipped, dedicated }. */
export function build(root = repo, cache = packagesFolder()) {
  const text = readFileSync(join(root, NOTICES), 'utf8');
  const npm = npmFacts(root);
  const nuget = nugetFacts(root, cache);
  const shipped = nuget.shipped.sort((a, b) => a.id.localeCompare(b.id, 'en'));
  const dedicated = [...new Map([...npm.dedicated, ...nuget.dedicated].map((row) => [JSON.stringify(row), row])).values()].sort(
    (a, b) => a.name.localeCompare(b.name, 'en') || a.version.localeCompare(b.version, 'en'),
  );
  const problems = [
    ...shipped.filter((row) => !row.ok).map((row) => `${row.id} ${row.version} ships to sites with ${row.licence}, which ${row.note}`),
    ...devTableProblems(readDevTable(text), npm, nuget),
  ];
  const updated = replaceBetween(
    replaceBetween(text, SHIPPED_START, SHIPPED_END, renderShipped(shipped)),
    PUBLIC_DOMAIN_START,
    PUBLIC_DOMAIN_END,
    renderPublicDomain(dedicated),
  );
  return { text, updated, problems, shipped, dedicated };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  if (args.some((arg) => arg !== '--check')) {
    console.error('usage: node scripts/third-party-notices.mjs [--check]');
    process.exit(2);
  }
  let result;
  try {
    result = build();
  } catch (error) {
    console.error(`third-party-notices: ${error.message}`);
    process.exit(1);
  }
  const { text, updated, problems, shipped, dedicated } = result;
  for (const problem of problems) console.error(`third-party-notices: ${problem}`);
  const counted = `${shipped.length} shipped packages, ${dedicated.length} development packages through ADR-0027`;
  if (args.includes('--check')) {
    if (updated !== text) console.error(`third-party-notices: ${NOTICES} is stale; run node scripts/third-party-notices.mjs`);
    if (updated !== text || problems.length > 0) process.exit(1);
    console.log(`third-party-notices: ${NOTICES} is current (${counted}; ${packagesFolder()})`);
  } else {
    if (updated !== text) writeFileSync(join(repo, NOTICES), updated);
    console.log(`third-party-notices: ${updated !== text ? 'wrote' : 'kept'} ${NOTICES} (${counted})`);
    if (problems.length > 0) process.exit(1);
  }
}
