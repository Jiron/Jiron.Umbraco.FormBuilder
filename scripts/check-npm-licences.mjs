#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Checks the licence of every package in Jiron Form Builder's npm lock files against CLAUDE.md rule 4 (ROADMAP
 * 3.5). A package the lock marks `dev` is a development tool: rule 4's development list and ADR-0027's public-domain
 * dedications (MIT-0, Unlicense, CC0-1.0) are allowed. Any other package could ship, so only rule 4's list for
 * shipped code is: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC and 0BSD. A missing licence, npm's
 * `UNLICENSED` and the families rule 4 forbids fail everywhere. Workspace links are Jiron Form Builder's own code.
 *
 *   node scripts/check-npm-licences.mjs [package-lock.json ...]   (default: every lock file of LOCKS)
 *
 * Exit codes: 0 every licence is allowed, 1 one is not or a lock file is missing.
 */
import { existsSync, readFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ALLOWED, PUBLIC_DOMAIN, satisfies } from './lib/licences.mjs';

const repo = resolve(fileURLToPath(new URL('..', import.meta.url)));

/** Jiron Form Builder's npm projects: the lock file and the short name THIRD-PARTY-NOTICES.md uses for it. */
export const LOCKS = Object.freeze([
  { file: 'scripts/package-lock.json', label: 'scripts/' },
  { file: 'src/Jiron.Umbraco.FormBuilder.Web/Client/package-lock.json', label: 'Client/' },
  { file: 'tests/e2e/package-lock.json', label: 'tests/e2e/' },
]);

/** The lock files, relative to the repository root. */
export const LOCK_FILES = Object.freeze(LOCKS.map((lock) => lock.file));

/** A lock entry's licence as an expression; old package.json files wrote an object or an array. */
export function licenceOf(info) {
  const value = info.license ?? info.licenses;
  if (typeof value === 'string') return value;
  if (Array.isArray(value)) return value.map((item) => (typeof item === 'string' ? item : item?.type)).filter(Boolean).join(' OR ');
  if (value && typeof value === 'object') return value.type ?? '';
  return '';
}

/** The tier of a lock entry: `dev` only when the lock marks it so; anything else could ship. */
export function tierOf(info) {
  return info.dev === true ? 'dev' : 'shipped';
}

/** True when a development package's licence is acceptable only through ADR-0027's public-domain dedications. */
export function needsPublicDomain(expression) {
  const withoutThem = new Set([...ALLOWED.dev].filter((id) => !PUBLIC_DOMAIN.includes(id)));
  return satisfies(expression, ALLOWED.dev) && !satisfies(expression, withoutThem);
}

/** The lock file's third-party packages as [{ name, version, licence, tier, path }]. */
export function thirdParty(lock) {
  return Object.entries(lock.packages ?? {})
    .filter(([path, info]) => path.includes('node_modules/') && !info.link)
    .map(([path, info]) => ({
      name: path.replace(/^.*node_modules\//, ''),
      version: info.version,
      licence: licenceOf(info),
      tier: tierOf(info),
      path,
    }));
}

/** The lock file's third-party packages whose licence is not acceptable for their tier, as report lines. */
export function violations(lock) {
  return thirdParty(lock)
    .filter((pkg) => !satisfies(pkg.licence, ALLOWED[pkg.tier]))
    .map((pkg) => `${pkg.name}@${pkg.version} (${pkg.tier}): ${pkg.licence || 'no licence'}`);
}

/** Checks the given lock files; returns the report lines and whether every licence is allowed. */
export function run(files, root = repo) {
  const lines = [];
  let ok = true;
  for (const file of files) {
    const name = relative(root, resolve(file)).replaceAll('\\', '/');
    if (!existsSync(file)) {
      lines.push(`check-npm-licences: ${name} is missing`);
      ok = false;
      continue;
    }
    const lock = JSON.parse(readFileSync(file, 'utf8'));
    const bad = violations(lock);
    for (const line of bad) lines.push(`check-npm-licences: ${name}: ${line} is not allowed (CLAUDE.md rule 4)`);
    if (bad.length > 0) {
      ok = false;
      continue;
    }
    const packages = thirdParty(lock);
    const dev = packages.filter((pkg) => pkg.tier === 'dev').length;
    const dedicated = packages.filter((pkg) => pkg.tier === 'dev' && needsPublicDomain(pkg.licence)).length;
    lines.push(
      `check-npm-licences: ${name}: ${packages.length} packages (${dev} dev, ${packages.length - dev} that could ship), ` +
        `all licences allowed; ${dedicated} through ADR-0027`,
    );
  }
  return { ok, lines };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const files = args.length > 0 ? args.map((file) => resolve(file)) : LOCK_FILES.map((file) => join(repo, file));
  const { ok, lines } = run(files);
  for (const line of lines) (ok ? console.log : console.error)(line);
  process.exit(ok ? 0 : 1);
}
