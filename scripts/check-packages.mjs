#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Checks the packed packages (ROADMAP 18.1, ADR-0246): every entry of each .nupkg and .snupkg must be on the allowlist
 * below, and anything else fails, a source map, a test, demo or sample assembly, an appsettings file or something from
 * .local/ first of all, with the reason. Each package's nuspec carries the metadata section 3.5 asks for, and its licence,
 * readme and icon are the repository's own files byte for byte (the licence is the owner's, D2). scripts/pack.mjs runs
 * this after every pack, so the gate, CI and the release check the same way.
 *
 *   node scripts/check-packages.mjs <folder with the packages> [--list]
 *
 * Exit codes: 0 fine, 1 problems, 2 usage.
 */
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { inflateRawSync } from 'node:zlib';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));

/** The repository's address, which every package names as its project and repository (the Marketplace reads it). */
export const PROJECT_URL = 'https://github.com/Jiron/Jiron.Umbraco.FormBuilder';

const LIB = 'lib/net10.0';

/** Entries every package has: NuGet's own parts, the licence, the readme and the icon. */
const COMMON = [
  '[Content_Types].xml',
  '_rels/.rels',
  'package/services/metadata/core-properties/*.psmdcp',
  '{id}.nuspec',
  'LICENSE.md',
  'README.md',
  'icon.png',
];

/** Entries of every symbol package. */
const SYMBOLS = ['[Content_Types].xml', '_rels/.rels', 'package/services/metadata/core-properties/*.psmdcp', '{id}.nuspec', `${LIB}/{id}.pdb`];

/** What the main package ships for headless sites besides its marker: the schemas and the visitor form's types (ADR-0125). */
export const SHIPPED_SCHEMAS = [
  'schemas/jfb-form.v1.schema.json',
  'schemas/jfb-form-export.v1.schema.json',
  'schemas/jfb-public-form.v1.schema.json',
  'schemas/jfb-public-form.v1.d.ts',
];

/** What a Razor class library's pack adds for its static web assets. */
const STATIC_WEB_ASSETS = [
  'build/{id}.props',
  'build/Microsoft.AspNetCore.StaticWebAssets.props',
  'build/Microsoft.AspNetCore.StaticWebAssetEndpoints.props',
  'buildMultiTargeting/{id}.props',
  'buildTransitive/{id}.props',
];

/** The Marketplace tags of the packages a site installs (section 3.5, 18.1). */
const INSTALLED_TAGS = 'umbraco umbraco-marketplace forms form-builder contact-form';

/**
 * The packages and what each may hold. `required` entries must be there; `allowed` patterns (`*` within a folder) may
 * be. `readme` is the repository file packed as README.md. Each code package also ships a .snupkg with its symbols.
 */
export const PACKAGES = {
  'Jiron.Umbraco.FormBuilder': {
    tags: INSTALLED_TAGS,
    symbols: false,
    readme: 'docs/README.nuget.md',
    required: [`${LIB}/_._`, ...SHIPPED_SCHEMAS],
    allowed: [],
    // Sites install this one: it brings the others at exactly its own version (ADR-0001), and Umbraco itself (section 3.5).
    dependsExactly: ['Jiron.Umbraco.FormBuilder.Core', 'Jiron.Umbraco.FormBuilder.Web'],
    dependsOn: ['Umbraco.Cms.Web.Website'],
  },
  'Jiron.Umbraco.FormBuilder.Core': {
    tags: 'umbraco',
    symbols: true,
    readme: 'docs/README.nuget.md',
    required: [`${LIB}/{id}.dll`, `${LIB}/{id}.xml`, 'appsettings-schema.Jiron.Umbraco.FormBuilder.json', 'buildTransitive/{id}.props'],
    allowed: [],
    dependsOn: ['Umbraco.Cms.Core', 'Umbraco.Cms.Infrastructure', 'HtmlSanitizer'],
  },
  'Jiron.Umbraco.FormBuilder.Web': {
    tags: 'umbraco',
    symbols: true,
    readme: 'docs/README.nuget.md',
    required: [
      `${LIB}/{id}.dll`,
      `${LIB}/{id}.xml`,
      ...STATIC_WEB_ASSETS,
      'staticwebassets/App_Plugins/JironFormBuilder/umbraco-package.json',
      'staticwebassets/App_Plugins/JironFormBuilder/jfb-backoffice.js',
      'staticwebassets/App_Plugins/JironFormBuilder/types/jiron-form-builder.d.ts',
    ],
    // The backoffice's chunks carry a content hash in their names. The website assets are embedded in the assembly.
    allowed: ['staticwebassets/App_Plugins/JironFormBuilder/chunks/*.js'],
    dependsExactly: ['Jiron.Umbraco.FormBuilder.Core'],
    dependsOn: ['Umbraco.Cms.Web.Common', 'Umbraco.Cms.Web.Website', 'Umbraco.Cms.Api.Common', 'Umbraco.Cms.Api.Management', 'Umbraco.Cms.Api.Delivery'],
  },
  'Jiron.Umbraco.FormBuilder.AI': {
    tags: `${INSTALLED_TAGS} ai`,
    symbols: true,
    readme: 'docs/README.nuget.ai.md',
    required: [
      `${LIB}/{id}.dll`,
      `${LIB}/{id}.xml`,
      ...STATIC_WEB_ASSETS,
      'staticwebassets/App_Plugins/JironFormBuilderAI/umbraco-package.json',
      'staticwebassets/App_Plugins/JironFormBuilderAI/jfb-ai.js',
    ],
    allowed: ['staticwebassets/App_Plugins/JironFormBuilderAI/chunks/*.js'],
    dependsExactly: ['Jiron.Umbraco.FormBuilder.Core', 'Jiron.Umbraco.FormBuilder.Web'],
    dependsOn: ['Umbraco.AI', 'Umbraco.Cms.Api.Management'],
  },
};

/** Entries that never belong in a package, with the reason; checked first, so the message says why. */
const FORBIDDEN = [
  [/\.map$/i, 'a source map, which carries the original sources'],
  [/(^|\/)appsettings(\.[^/]*)?\.json$/i, "an appsettings file, which may hold a site's settings or secrets"],
  [/(^|\/)Jiron\.Umbraco\.FormBuilder\.(Testing|Tests|Demo|Samples)\b[^/]*$/i, 'a test, demo or sample assembly, which must never ship'],
  [/(^|\/)\.(local|private)\//i, 'a file from .local/ or .private/, which git ignores'],
  [/(^|\/)node_modules\//i, 'a file from node_modules'],
  [/(^|\/)([^/]*-key\.txt|keys?\.(txt|json|xml)|\.env[^/]*)$/i, 'a key or environment file'],
  [/\.(pfx|snk|pem|key)$/i, 'a key or certificate'],
];

/** A pattern with `*` (any name within one folder) and `{id}` as a regular expression. */
function pattern(glob, id) {
  const text = glob.replaceAll('{id}', id);
  return new RegExp(`^${text.replace(/[.+?^${}()|[\]\\]/g, '\\$&').replaceAll('*', '[^/]*')}$`);
}

/**
 * The entries of a zip archive, a package among them, from its central directory, each with a way to read it (stored
 * or deflated; no ZIP64, which no package of ours needs).
 * @param {Buffer} buffer
 */
export function readZip(buffer) {
  let end = -1;
  for (let at = buffer.length - 22; at >= Math.max(0, buffer.length - 65_557); at--) {
    if (buffer.readUInt32LE(at) === 0x06054b50) {
      end = at;
      break;
    }
  }
  if (end < 0) throw new Error('not a zip file: no end of central directory');
  const count = buffer.readUInt16LE(end + 10);
  let at = buffer.readUInt32LE(end + 16);
  const entries = [];
  for (let i = 0; i < count; i++) {
    if (buffer.readUInt32LE(at) !== 0x02014b50) throw new Error('not a zip file: a broken central directory');
    const method = buffer.readUInt16LE(at + 10);
    const size = buffer.readUInt32LE(at + 20);
    const nameLength = buffer.readUInt16LE(at + 28);
    const local = buffer.readUInt32LE(at + 42);
    const raw = buffer.toString('utf8', at + 46, at + 46 + nameLength);
    // NuGet percent-encodes some names in the archive; a name that is no valid encoding is taken as it is.
    let name = raw;
    try {
      name = decodeURIComponent(raw);
    } catch {
      // as written
    }
    entries.push({
      name,
      read() {
        const start = local + 30 + buffer.readUInt16LE(local + 26) + buffer.readUInt16LE(local + 28);
        const data = buffer.subarray(start, start + size);
        if (method === 0) return Buffer.from(data);
        if (method === 8) return inflateRawSync(data);
        throw new Error(`${name}: compression method ${method} is not supported`);
      },
    });
    at += 46 + nameLength + buffer.readUInt16LE(at + 30) + buffer.readUInt16LE(at + 32);
  }
  return entries;
}

/** The names of a zip file's entries. */
export const zipEntries = (buffer) => readZip(buffer).map((entry) => entry.name);

/** One element's text in a nuspec, or null. */
const element = (xml, name) => xml.match(new RegExp(`<${name}(?:\\s[^>]*)?>([\\s\\S]*?)</${name}>`))?.[1]?.trim() ?? null;

/** XML's five escapes undone, for comparing a nuspec's text with a value. */
const unescapeXml = (text) =>
  text.replaceAll('&lt;', '<').replaceAll('&gt;', '>').replaceAll('&quot;', '"').replaceAll('&apos;', "'").replaceAll('&amp;', '&');

/** A version or range without spaces, as NuGet may write `[17.6.0, 18.0.0)` either way. */
const compact = (version) => String(version ?? '').replaceAll(/\s+/g, '');

/** The values the packages take from the repository's build files (ROADMAP "One source for every value"). */
export function buildValues(rootDir = root) {
  const props = readFileSync(join(rootDir, 'Directory.Build.props'), 'utf8');
  const packages = readFileSync(join(rootDir, 'Directory.Packages.props'), 'utf8');
  const versions = new Map([...packages.matchAll(/<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"/g)].map((match) => [match[1], match[2]]));
  return {
    authors: unescapeXml(props.match(/<Authors>([^<]+)<\/Authors>/)?.[1]?.trim() ?? ''),
    copyright: unescapeXml(props.match(/<Copyright>([^<]+)<\/Copyright>/)?.[1]?.trim() ?? ''),
    versions,
  };
}

/** What is wrong with the metadata of a package's nuspec. */
export function checkNuspec(id, xml, values, spec = PACKAGES[id]) {
  const problems = [];
  const expect = (ok, message) => ok || problems.push(message);
  expect(element(xml, 'id') === id, `its id is not ${id}`);
  const version = element(xml, 'version') ?? '';
  expect(/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version), 'it has no semantic version');
  for (const name of ['title', 'description']) expect(Boolean(element(xml, name)), `it has no ${name}`);
  expect((element(xml, 'description') ?? '').length <= 4000, 'its description is longer than 4000 characters');
  expect(unescapeXml(element(xml, 'authors') ?? '') === values.authors, `its authors are not "${values.authors}"`);
  expect(unescapeXml(element(xml, 'copyright') ?? '') === values.copyright, `its copyright is not "${values.copyright}"`);
  expect(element(xml, 'projectUrl') === PROJECT_URL, `its project URL is not ${PROJECT_URL}`);
  expect(new RegExp(`<repository type="git" url="${PROJECT_URL.replaceAll('.', '\\.')}"`).test(xml), `its repository is not ${PROJECT_URL}`);
  // A licence file, never an expression: PolyForm Perimeter has no SPDX id (section 3.5), and only one licence property.
  expect(/<license type="file">LICENSE\.md<\/license>/.test(xml), 'its licence is not the packed LICENSE.md');
  expect(!/<licenseUrl>/.test(xml) || /<licenseUrl>https:\/\/aka\.ms\/deprecateLicenseUrl<\/licenseUrl>/.test(xml), 'it names a licence URL');
  expect(element(xml, 'requireLicenseAcceptance') === 'true', 'it does not ask to accept the licence');
  expect(element(xml, 'readme') === 'README.md', 'its readme is not README.md');
  expect(element(xml, 'icon') === 'icon.png', 'its icon is not icon.png');
  expect(element(xml, 'tags') === spec.tags, `its tags are not "${spec.tags}"`);
  const dependencies = new Map([...xml.matchAll(/<dependency id="([^"]+)" version="([^"]+)"/g)].map((match) => [match[1], match[2]]));
  for (const sibling of spec.dependsExactly ?? []) {
    expect(dependencies.get(sibling) === `[${version}]`, `it does not depend on ${sibling} at exactly its own version`);
  }
  for (const other of spec.dependsOn ?? []) {
    const expected = values.versions.get(other);
    expect(
      dependencies.has(other) && compact(dependencies.get(other)) === compact(expected),
      `it does not depend on ${other} ${expected ?? '(not in Directory.Packages.props)'} as Directory.Packages.props says`,
    );
  }
  const known = new Set([...(spec.dependsExactly ?? []), ...(spec.dependsOn ?? [])]);
  for (const dependency of dependencies.keys()) expect(known.has(dependency), `it depends on ${dependency}, which is not expected`);
  return problems;
}

/** What is wrong with one package's entries: forbidden or unlisted ones, and required ones that are missing. */
export function checkEntries(id, names, { symbols = false } = {}) {
  const spec = PACKAGES[id];
  if (!spec) return [`${id} is not one of Jiron Form Builder's packages`];
  const required = symbols ? SYMBOLS : [...COMMON, ...spec.required];
  const allowed = [...required, ...(symbols ? [] : spec.allowed)].map((glob) => pattern(glob, id));
  const problems = [];
  for (const name of names) {
    const forbidden = FORBIDDEN.find(([test]) => test.test(name));
    if (forbidden) problems.push(`${name} is ${forbidden[1]}`);
    else if (!allowed.some((test) => test.test(name))) problems.push(`${name} is not on the allowlist`);
  }
  for (const glob of required) {
    const test = pattern(glob, id);
    if (!names.some((name) => test.test(name))) problems.push(`${glob.replaceAll('{id}', id)} is missing`);
  }
  return problems;
}

/** What is wrong with the files a package copies from the repository: each must be that file byte for byte. */
export function checkCopies(id, entries, read = (path) => readFileSync(join(root, path))) {
  const spec = PACKAGES[id];
  const problems = [];
  for (const [entry, source, what] of [
    ['LICENSE.md', 'LICENSE.md', 'the owner\'s licence (D2)'],
    ['README.md', spec.readme, 'its readme'],
    ['icon.png', 'assets/icon-128.png', 'the mark at 128 px'],
  ]) {
    const packed = entries.find((candidate) => candidate.name === entry);
    if (packed && !packed.read().equals(read(source))) problems.push(`${entry} is not ${source}, ${what}`);
  }
  return problems;
}

/** The package id, version and kind of a file name such as Jiron.Umbraco.FormBuilder.Core.17.0.0-alpha.1.snupkg. */
export function parseFileName(file) {
  const match = file.match(/^(.+?)\.(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\.(nupkg|snupkg)$/);
  return match ? { id: match[1], version: match[2], symbols: match[3] === 'snupkg' } : null;
}

/** Checks every package in a folder: the four, each code package with its symbols, one version, nothing else. */
export function checkFolder(folder, { list = false, log = console.log, values = buildValues(), read } = {}) {
  const problems = [];
  const files = readdirSync(folder)
    .filter((file) => /\.s?nupkg$/i.test(file))
    .sort();
  const versions = new Set();
  for (const file of files) {
    const parsed = parseFileName(file);
    if (!parsed) {
      problems.push(`${file}: not a package file name`);
      continue;
    }
    versions.add(parsed.version);
    const entries = readZip(readFileSync(join(folder, file)));
    const names = entries.map((entry) => entry.name);
    const found = checkEntries(parsed.id, names, { symbols: parsed.symbols });
    if (!parsed.symbols && PACKAGES[parsed.id]) {
      const nuspec = entries.find((entry) => entry.name === `${parsed.id}.nuspec`)?.read().toString('utf8') ?? '';
      found.push(...checkNuspec(parsed.id, nuspec, values));
      if (element(nuspec, 'version') !== parsed.version) found.push(`its nuspec's version is not ${parsed.version}`);
      found.push(...checkCopies(parsed.id, entries, read));
    }
    problems.push(...found.map((problem) => `${file}: ${problem}`));
    log(`${found.length ? 'FAIL' : 'ok  '} ${file} (${names.length} entries)`);
    if (list) for (const name of names) log(`       ${name}`);
  }
  for (const [id, spec] of Object.entries(PACKAGES)) {
    for (const symbols of spec.symbols ? [false, true] : [false]) {
      if (!files.some((file) => parseFileName(file)?.id === id && parseFileName(file)?.symbols === symbols)) {
        problems.push(`${id}: no ${symbols ? '.snupkg' : '.nupkg'} in the folder`);
      }
    }
  }
  if (versions.size > 1) problems.push(`more than one version: ${[...versions].join(', ')}`);
  return problems;
}

function main() {
  const args = process.argv.slice(2);
  const folder = args.find((arg) => !arg.startsWith('--'));
  if (!folder) {
    console.error('usage: node scripts/check-packages.mjs <folder with the packages> [--list]');
    process.exit(2);
  }
  const problems = checkFolder(resolve(folder), { list: args.includes('--list') });
  for (const problem of problems) console.error(`check-packages: ${problem}`);
  console.log(problems.length ? `check-packages: ${problems.length} problem(s)` : 'check-packages: every package holds only what it should');
  process.exit(problems.length ? 1 : 0);
}

if (resolve(process.argv[1] ?? '') === fileURLToPath(import.meta.url)) main();
