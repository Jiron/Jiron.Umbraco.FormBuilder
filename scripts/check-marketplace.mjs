#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Checks the Umbraco Marketplace listings (ROADMAP 18.1, section 3.5, ADR-0246): `umbraco-marketplace.json` for the package
 * a site installs, and `umbraco-marketplace-<package id>.json` for each other package with the `umbraco-marketplace`
 * tag (the AI add-on), as the Marketplace looks for them at the root `PackageProjectUrl` names. Each must pass:
 *
 *   - the Marketplace's own JSON schema, from a copy in the git-ignored `.local/marketplace/`, downloaded once when it
 *     is missing (`--refresh` downloads it again) and used only while its SHA-256 is the one pinned below;
 *   - Jiron Form Builder's rules: the title, the categories (there is no forms category), the author, the licence type,
 *     links into this repository that name files it holds, and for the main listing every picture of
 *     docs/screenshots once, with a caption, at the address GitHub serves it from once the repository is public.
 *
 *   npm ci --prefix scripts, then node scripts/check-marketplace.mjs [--refresh] [--ci]
 *     --refresh  downloads the schema again; a schema whose hash is not the pinned one is kept beside the reviewed
 *                copy as umbraco-marketplace-schema.new.json for review, and the reviewed copy stays as it was
 *     --ci       for CI, which has no reviewed copy: when the schema cannot be downloaded or is not the pinned one,
 *                that is a warning and the listings are held to Jiron Form Builder's rules alone, so a change on
 *                the Marketplace's site never blocks CI or a release (18.3's review); the local gate stays strict
 *
 * Exit codes: 0 fine, 1 problems.
 */
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PACKAGES } from './check-packages.mjs';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
export const SCHEMA_URL = 'https://marketplace.umbraco.com/umbraco-marketplace-schema.json';
export const SCHEMA_COPY = join(root, '.local', 'marketplace', 'umbraco-marketplace-schema.json');

/**
 * The SHA-256 of the schema the listings were checked against (downloaded 2026-11-13). Ajv compiles a schema into code,
 * so a schema the Marketplace changed is used only once someone reviewed it and set its hash here.
 */
export const SCHEMA_SHA256 = 'd0e63a3b8759df55369d0405edf18f826febc3eafdcbf231d90942819d5246b2';

const REPOSITORY = 'https://github.com/Jiron/Jiron.Umbraco.FormBuilder';
const RAW = 'https://raw.githubusercontent.com/Jiron/Jiron.Umbraco.FormBuilder/main/';
const MAIN = 'Jiron.Umbraco.FormBuilder';

/** The licence in its plain words (D2), as README.md says it; never "open source" on its own. */
export const LICENCE_WORDS =
  'Free to use and change, including commercially; no selling, no published versions of your own (free or paid, open source or not), no competing products; source-available.';

/** The listing file of a package: the main package's is the plain name, every other one's carries its id. */
export const listingFile = (id) => (id === MAIN ? 'umbraco-marketplace.json' : `umbraco-marketplace-${id.toLowerCase()}.json`);

/** The packages the Marketplace lists: those with the `umbraco-marketplace` tag (check-packages.mjs). */
export const LISTED = Object.entries(PACKAGES)
  .filter(([, spec]) => spec.tags.split(' ').includes('umbraco-marketplace'))
  .map(([id]) => id);

/** What each listing must say beyond the schema. */
export const RULES = {
  [MAIN]: { title: 'Jiron Form Builder', category: 'Editor Tools', alternate: 'Campaign & Marketing', documentation: 'docs/README.md', screenshots: true },
  'Jiron.Umbraco.FormBuilder.AI': {
    title: 'Jiron Form Builder AI',
    category: 'Artificial Intelligence',
    alternate: 'Editor Tools',
    documentation: 'docs/ai.md',
    subPackageOf: MAIN,
  },
};

/** The pictures of docs/screenshots, as the repository holds them. */
export function screenshotsOnDisk(rootDir = root) {
  const folder = join(rootDir, 'docs', 'screenshots');
  return existsSync(folder) ? readdirSync(folder).filter((file) => file.endsWith('.png')).sort() : [];
}

/** What is wrong with one listing by Jiron Form Builder's rules (the schema is checked separately). */
export function checkListing(id, listing, { screenshots = screenshotsOnDisk(), exists = (path) => existsSync(join(root, path)) } = {}) {
  const rules = RULES[id];
  if (!rules) return [`${id} has no rules for its listing`];
  const problems = [];
  const expect = (ok, message) => ok || problems.push(message);
  expect(listing.$schema === SCHEMA_URL, `$schema is not ${SCHEMA_URL}`);
  expect(listing.Title === rules.title, `the title is not "${rules.title}"`);
  expect(listing.Category === rules.category, `the category is not "${rules.category}"`);
  expect(listing.AlternateCategory === rules.alternate, `the alternate category is not "${rules.alternate}"`);
  expect(listing.PackageType === 'Package', 'the package type is not "Package"');
  // About the price, not the licence: Jiron Form Builder costs nothing (section 3.5).
  expect(JSON.stringify(listing.LicenseTypes) === '["Free"]', 'the licence types are not ["Free"]');
  // The dependency range has an upper bound, so the Marketplace reads the supported versions from it (section 3.5).
  expect(listing.VersionDependencyMode === 'Default', 'the version dependency mode is not "Default"');
  expect(!('LicenseType' in listing) && !('AlternatePackageNames' in listing), 'it uses a deprecated field');
  const description = String(listing.Description ?? '');
  expect(description.includes(LICENCE_WORDS), 'the description does not give the licence in its plain words');
  expect(!/open[ -]source/i.test(description.replace(LICENCE_WORDS, '')), 'the description calls Jiron Form Builder open source');

  const author = listing.AuthorDetails ?? {};
  expect(author.Name === 'Jiron', 'the author is not "Jiron"');
  for (const word of ['Manass\u00e9s Z\u00e4hnler', 'Ligata', 'jiron.dev', 'ligata.ch']) {
    expect(String(author.Description ?? '').includes(word), `the author's description does not name ${word}`);
  }

  expect(listing.DocumentationUrl === `${REPOSITORY}/blob/main/${rules.documentation}`, `the documentation is not ${rules.documentation} in ${REPOSITORY}`);
  expect(exists(rules.documentation), `${rules.documentation} is not in the repository`);
  expect(listing.IssueTrackerUrl === `${REPOSITORY}/issues`, `the issue tracker is not ${REPOSITORY}/issues`);
  expect(Array.isArray(listing.Tags) && listing.Tags.length > 0, 'there are no tags');

  if (rules.subPackageOf) expect(listing.IsSubPackageOf === rules.subPackageOf, `it is not a sub-package of ${rules.subPackageOf}`);
  else expect(!('IsSubPackageOf' in listing), 'the main listing is a sub-package');
  for (const other of LISTED.filter((candidate) => candidate !== id && !rules.subPackageOf)) {
    expect((listing.RelatedPackages ?? []).some((related) => related.PackageId === other), `the related packages do not name ${other}`);
  }

  const listed = listing.Screenshots ?? [];
  if (!rules.screenshots) {
    expect(listed.length === 0, 'it shows screenshots, which the main listing shows');
    return problems;
  }
  const seen = new Set();
  for (const shot of listed) {
    const file = shot.ImageUrl?.startsWith(`${RAW}docs/screenshots/`) ? shot.ImageUrl.slice(`${RAW}docs/screenshots/`.length) : null;
    if (!file || !screenshots.includes(file)) {
      problems.push(`${shot.ImageUrl} is not a picture of docs/screenshots`);
      continue;
    }
    expect(!seen.has(file), `${file} is listed twice`);
    seen.add(file);
    expect(typeof shot.Caption === 'string' && shot.Caption.trim().length > 0, `${file} has no caption`);
  }
  for (const file of screenshots) expect(seen.has(file), `docs/screenshots/${file} is missing from the screenshots`);
  return problems;
}

/** The schema's errors for a listing, as readable lines (Ajv's 2020-12 validator, MIT, dev-only). */
export async function validate(listing, schema) {
  const { default: Ajv2020 } = await import('ajv/dist/2020.js');
  // Not strict: the schema is the Marketplace's, written for its own validator, and only its rules count here. Its URL
  // pattern escapes ":" (`\:`), which a Unicode regular expression refuses, so its patterns are read without the u flag.
  const ajv = new Ajv2020({ allErrors: true, strict: false, unicodeRegExp: false });
  const check = ajv.compile(schema);
  return check(listing)
    ? []
    : check.errors.map((error) => `${error.instancePath || '(root)'} ${error.message}${error.params ? ` ${JSON.stringify(error.params)}` : ''}`);
}

/** The schema's text, once its hash is the pinned one. */
export function pinned(bytes, expected = SCHEMA_SHA256) {
  const hash = createHash('sha256').update(bytes).digest('hex');
  if (hash !== expected) {
    throw new Error(
      `the Marketplace's schema is not the one the listings were checked against (sha256 ${hash}, expected ${expected}): review ${SCHEMA_COPY}, then set SCHEMA_SHA256 in scripts/check-marketplace.mjs`,
    );
  }
  return Buffer.from(bytes).toString('utf8');
}

/** The Marketplace's schema as its site serves it now. */
export async function download(url = SCHEMA_URL) {
  const response = await fetch(url, { signal: AbortSignal.timeout(30_000) });
  if (!response.ok) throw new Error(`${url} answered ${response.status}`);
  return Buffer.from(await response.arrayBuffer());
}

/**
 * The schema: the .local copy, downloaded first when it is missing or asked for, and used only with the pinned hash.
 * A download is written over the copy only when its hash is the pinned one; another one goes beside it for review.
 */
export async function schema({ refresh = false, copy = SCHEMA_COPY, fetchSchema = download, expected = SCHEMA_SHA256 } = {}) {
  if (refresh || !existsSync(copy)) {
    const bytes = await fetchSchema();
    JSON.parse(bytes.toString('utf8'));
    mkdirSync(dirname(copy), { recursive: true });
    if (createHash('sha256').update(bytes).digest('hex') !== expected) {
      const review = copy.replace(/\.json$/, '.new.json');
      writeFileSync(review, bytes);
      pinned(bytes, expected);
    }
    writeFileSync(copy, bytes);
    console.log(`check-marketplace: downloaded the schema to ${copy}`);
  }
  return JSON.parse(pinned(readFileSync(copy), expected));
}

async function main() {
  const ci = process.argv.includes('--ci');
  let marketplace = null;
  try {
    marketplace = await schema({ refresh: process.argv.includes('--refresh') });
  } catch (error) {
    if (!ci) throw error;
    // GitHub shows a ::warning:: line on the run's page.
    console.log(`::warning::check-marketplace: ${error.message}; the listings are held to Jiron Form Builder's rules alone`);
  }
  let problems = 0;
  for (const id of LISTED) {
    const file = listingFile(id);
    const path = join(root, file);
    const found = existsSync(path)
      ? (() => {
          const listing = JSON.parse(readFileSync(path, 'utf8'));
          const errors = marketplace ? validate(listing, marketplace) : Promise.resolve([]);
          return errors.then((found) => [...found.map((error) => `schema: ${error}`), ...checkListing(id, listing)]);
        })()
      : Promise.resolve([`${file} is missing (${id} carries the umbraco-marketplace tag)`]);
    const lines = await found;
    problems += lines.length;
    for (const line of lines) console.error(`check-marketplace: ${file}: ${line}`);
    if (lines.length === 0) console.log(`check-marketplace: ${file}: valid (${id})`);
  }
  process.exit(problems ? 1 : 0);
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) await main();
