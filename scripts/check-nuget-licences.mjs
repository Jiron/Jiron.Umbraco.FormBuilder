#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Checks the licence of every NuGet package the solution restores against CLAUDE.md rule 4 (ROADMAP 3.5,
 * ADR-0032). It reads each package's nuspec from NuGet's packages folder (NUGET_PACKAGES when set, the default
 * cache otherwise) and judges it by its tier (scripts/lib/nuget.mjs):
 *
 *   - shipped (Jiron Form Builder's packages bring it, Umbraco does not): MIT, Apache-2.0, BSD-2-Clause,
 *     BSD-3-Clause, ISC or 0BSD, or a reviewed exception;
 *   - dev (only the tests and the demo bring it): those, the development licences of rule 4 and ADR-0027's
 *     public-domain dedications, or a reviewed exception;
 *   - platform (an Umbraco CMS package brings it): Umbraco's choice, which every site already runs. Refused only
 *     when its expression, licence file or licence URL names a family rule 4 forbids anywhere (the GPL family,
 *     SSPL, source-available terms).
 *
 * A reviewed exception names the package, the tiers it may appear in and, for a licence file, the SHA-256 of the
 * file's text, so a new version with other terms fails until someone reads it.
 *
 *   node scripts/check-nuget-licences.mjs     after `dotnet restore`
 *
 * Exit codes: 0 every licence is allowed, 1 one is not, or a restore graph or nuspec is missing.
 */
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { ALLOWED, forbiddenText, isForbiddenId, isForbiddenUrl, satisfies } from './lib/licences.mjs';
import { licenceTextHash, packageMetadata, packagesFolder, solutionPackages } from './lib/nuget.mjs';

const repo = resolve(fileURLToPath(new URL('..', import.meta.url)));

/**
 * Packages whose nuspec licence is not an allowed expression, reviewed by reading the licence. `files` holds the
 * SHA-256 of each reviewed licence file (scripts/lib/nuget.mjs, licenceTextHash).
 */
export const EXCEPTIONS = [
  {
    id: 'NUnit',
    tiers: ['dev'],
    licence: 'MIT',
    files: ['7b20c7b11c1354448b4b8fb6827e9fc7c47aa30a8dc777623e4cd60031d386d3'],
    reason: 'its nuspec names the file LICENSE.txt, which is the MIT licence (3.14.0, read 2026-10-23)',
  },
];

/** How a nuspec's licence reads in a report line. */
export function describe(meta) {
  if (meta.kind === 'expression') return meta.value;
  if (meta.kind === 'file') return `the licence file ${meta.value}`;
  if (meta.kind === 'url') return `the licence URL ${meta.value}`;
  return 'no licence';
}

/** The forbidden family a platform package's licence names, or undefined. */
function forbiddenFamily(meta) {
  if (meta.kind === 'expression') return meta.value.split(/[\s()]+/).find((id) => id && isForbiddenId(id));
  if (meta.kind === 'file') return forbiddenText(meta.text ?? '');
  if (meta.kind === 'url') return isForbiddenUrl(meta.value) ? meta.value : undefined;
  return undefined;
}

/**
 * The verdict on one package: { ok, licence, note }. `licence` is what the notices show, `note` why an exception
 * or the platform rule accepted it, or what is wrong.
 */
export function judge(pkg, meta, exceptions = EXCEPTIONS) {
  if (pkg.tier === 'platform') {
    const family = forbiddenFamily(meta);
    if (family) return { ok: false, licence: describe(meta), note: `names a family rule 4 forbids anywhere (${family})` };
    return { ok: true, licence: describe(meta), note: "Umbraco's platform" };
  }
  if (meta.kind === 'expression' && satisfies(meta.value, ALLOWED[pkg.tier])) return { ok: true, licence: meta.value, note: '' };

  const exception = exceptions.find((entry) => entry.id.toLowerCase() === pkg.id.toLowerCase());
  if (exception && exception.tiers.includes(pkg.tier)) {
    if (meta.kind === 'file' && exception.files?.includes(licenceTextHash(meta.text ?? ''))) {
      return { ok: true, licence: exception.licence, note: exception.reason };
    }
    if (meta.kind === 'file') {
      return {
        ok: false,
        licence: describe(meta),
        note: `differs from the reviewed licence file (sha256 ${licenceTextHash(meta.text ?? '')}): read it, and add its hash when it is still ${exception.licence}`,
      };
    }
  }
  return { ok: false, licence: describe(meta), note: `is not allowed for a ${pkg.tier} package (CLAUDE.md rule 4)` };
}

/** Judges every restored package; returns { ok, rows, lines }. */
export function run(root = repo, cache = packagesFolder(), exceptions = EXCEPTIONS) {
  const lines = [];
  let packages;
  try {
    packages = solutionPackages(root);
  } catch (error) {
    return { ok: false, rows: [], lines: [`check-nuget-licences: ${error.message}`] };
  }
  const rows = [];
  for (const pkg of packages) {
    try {
      rows.push({ ...pkg, ...judge(pkg, packageMetadata(cache, pkg), exceptions) });
    } catch (error) {
      rows.push({ ...pkg, ok: false, licence: 'unknown', note: error.message });
    }
  }
  const refused = rows.filter((row) => !row.ok);
  for (const row of refused) lines.push(`check-nuget-licences: ${row.id} ${row.version} (${row.tier}, ${row.projects.join(', ')}): ${row.licence} ${row.note}`);
  const count = (tier) => rows.filter((row) => row.tier === tier).length;
  const reviewed = rows.filter((row) => row.ok && row.note && row.tier !== 'platform');
  for (const row of reviewed) lines.push(`check-nuget-licences: ${row.id} ${row.version} (${row.tier}): ${row.licence}, ${row.note}`);
  lines.push(
    `check-nuget-licences: ${rows.length} packages from ${cache}: ${count('shipped')} shipped, ${count('dev')} dev, ` +
      `${count('platform')} from Umbraco's platform; ${refused.length === 0 ? 'every licence allowed' : `${refused.length} refused`}`,
  );
  return { ok: refused.length === 0, rows, lines };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const { ok, lines } = run();
  for (const line of lines) (ok ? console.log : console.error)(line);
  process.exit(ok ? 0 : 1);
}
