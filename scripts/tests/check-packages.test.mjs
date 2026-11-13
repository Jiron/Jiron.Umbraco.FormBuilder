// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/check-packages.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { deflateRawSync } from 'node:zlib';
import {
  buildValues,
  checkCopies,
  checkEntries,
  checkFolder,
  checkNuspec,
  PACKAGES,
  parseFileName,
  PROJECT_URL,
  readZip,
  SHIPPED_SCHEMAS,
  zipEntries,
} from '../check-packages.mjs';

const VERSION = '17.0.0-alpha.1';
const VALUES = {
  authors: 'Jiron',
  copyright: 'Copyright (c) 2026 Someone',
  versions: new Map([
    ['Umbraco.Cms.Core', '[17.6.0, 18.0.0)'],
    ['Umbraco.Cms.Infrastructure', '[17.6.0, 18.0.0)'],
    ['Umbraco.Cms.Web.Common', '[17.6.0, 18.0.0)'],
    ['Umbraco.Cms.Web.Website', '[17.6.0, 18.0.0)'],
    ['Umbraco.Cms.Api.Common', '[17.6.0, 18.0.0)'],
    ['Umbraco.Cms.Api.Management', '[17.6.0, 18.0.0)'],
    ['Umbraco.Cms.Api.Delivery', '[17.6.0, 18.0.0)'],
    ['Umbraco.AI', '[17.5.2, 18.0.0)'],
    ['HtmlSanitizer', '9.2.1039'],
  ]),
};

/** A zip with these entries ([name, content], stored, or deflated when asked), as NuGet's pack writes one. */
function zipOf(entries, { deflate = false } = {}) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const [name, content = ''] of entries) {
    const bytes = Buffer.from(name, 'utf8');
    const raw = Buffer.from(content);
    const data = deflate ? deflateRawSync(raw) : raw;
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(deflate ? 8 : 0, 8);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(raw.length, 22);
    local.writeUInt16LE(bytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(deflate ? 8 : 0, 10);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(raw.length, 24);
    central.writeUInt16LE(bytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, bytes, data);
    centrals.push(central, bytes);
    offset += local.length + bytes.length + data.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.length, 8);
  end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

/** A nuspec as NuGet's pack writes it for a package, with any part replaced. */
function nuspecOf(id, overrides = {}) {
  const spec = PACKAGES[id];
  const dependencies = [
    ...(spec.dependsExactly ?? []).map((sibling) => [sibling, `[${VERSION}]`]),
    ...(spec.dependsOn ?? []).map((other) => [other, VALUES.versions.get(other)]),
  ];
  const parts = {
    id,
    version: VERSION,
    title: 'Jiron Form Builder',
    authors: 'Jiron',
    license: '<license type="file">LICENSE.md</license>\n    <licenseUrl>https://aka.ms/deprecateLicenseUrl</licenseUrl>',
    acceptance: 'true',
    icon: 'icon.png',
    readme: 'README.md',
    projectUrl: PROJECT_URL,
    description: 'A form builder for Umbraco 17.',
    copyright: 'Copyright (c) 2026 Someone',
    tags: spec.tags,
    repository: `<repository type="git" url="${PROJECT_URL}" commit="0123456789abcdef0123456789abcdef01234567" />`,
    dependencies: dependencies.map(([name, version]) => `<dependency id="${name}" version="${version}" exclude="Build,Analyzers" />`).join('\n'),
    ...overrides,
  };
  return `<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>${parts.id}</id>
    <version>${parts.version}</version>
    <title>${parts.title}</title>
    <authors>${parts.authors}</authors>
    <requireLicenseAcceptance>${parts.acceptance}</requireLicenseAcceptance>
    ${parts.license}
    <icon>${parts.icon}</icon>
    <readme>${parts.readme}</readme>
    <projectUrl>${parts.projectUrl}</projectUrl>
    <description>${parts.description}</description>
    <copyright>${parts.copyright}</copyright>
    <tags>${parts.tags}</tags>
    ${parts.repository}
    <dependencies><group targetFramework="net10.0">
${parts.dependencies}
    </group></dependencies>
  </metadata>
</package>`;
}

const COMMON = ['[Content_Types].xml', '_rels/.rels', 'package/services/metadata/core-properties/0a1b.psmdcp', 'LICENSE.md', 'README.md', 'icon.png'];
const MAIN = ['Jiron.Umbraco.FormBuilder.nuspec', ...COMMON, 'lib/net10.0/_._', ...SHIPPED_SCHEMAS];
const CORE_SYMBOLS = ['[Content_Types].xml', '_rels/.rels', 'package/services/metadata/core-properties/0a1b.psmdcp', 'Jiron.Umbraco.FormBuilder.Core.nuspec', 'lib/net10.0/Jiron.Umbraco.FormBuilder.Core.pdb'];

describe('reading a package', () => {
  test('lists the entries of a zip by its central directory and reads stored and deflated ones', () => {
    const stored = readZip(zipOf([['lib/net10.0/_._'], ['README.md', '# Jiron Form Builder']]));
    assert.deepEqual(stored.map((entry) => entry.name), ['lib/net10.0/_._', 'README.md']);
    assert.equal(stored[1].read().toString('utf8'), '# Jiron Form Builder');
    const deflated = readZip(zipOf([['README.md', 'a readme '.repeat(50)]], { deflate: true }));
    assert.equal(deflated[0].read().toString('utf8'), 'a readme '.repeat(50));
    assert.deepEqual(zipEntries(zipOf([['schemas/jfb-public-form.v1.d.ts']])), ['schemas/jfb-public-form.v1.d.ts']);
  });

  test('decodes the names NuGet percent-encodes, keeps one that is no valid encoding, and refuses what is no zip', () => {
    assert.deepEqual(zipEntries(zipOf([['chunks/a%2Bb.js'], ['100%.txt']])), ['chunks/a+b.js', '100%.txt']);
    assert.throws(() => readZip(Buffer.from('not a zip at all, but long enough to look for an end record')), /no end of central directory/);
  });

  test('names a package file by its id, version and kind', () => {
    assert.deepEqual(parseFileName(`Jiron.Umbraco.FormBuilder.Core.${VERSION}.snupkg`), { id: 'Jiron.Umbraco.FormBuilder.Core', version: VERSION, symbols: true });
    assert.deepEqual(parseFileName('Jiron.Umbraco.FormBuilder.17.0.0.nupkg'), { id: 'Jiron.Umbraco.FormBuilder', version: '17.0.0', symbols: false });
    assert.equal(parseFileName('notes.txt'), null);
  });
});

describe("a package's entries", () => {
  test("the main package holds its marker, the schemas and the visitor form's types, and nothing else (ADR-0125)", () => {
    assert.deepEqual(checkEntries('Jiron.Umbraco.FormBuilder', MAIN), []);
    assert.deepEqual(checkEntries('Jiron.Umbraco.FormBuilder', MAIN.filter((name) => !name.endsWith('.d.ts'))), ['schemas/jfb-public-form.v1.d.ts is missing']);
    assert.deepEqual(checkEntries('Jiron.Umbraco.FormBuilder', MAIN.filter((name) => name !== 'icon.png')), ['icon.png is missing']);
  });

  test('a forbidden entry is named with its reason, before the allowlist', () => {
    const web = (extra) =>
      checkEntries('Jiron.Umbraco.FormBuilder.Web', [
        'Jiron.Umbraco.FormBuilder.Web.nuspec',
        ...COMMON,
        'lib/net10.0/Jiron.Umbraco.FormBuilder.Web.dll',
        'lib/net10.0/Jiron.Umbraco.FormBuilder.Web.xml',
        'build/Jiron.Umbraco.FormBuilder.Web.props',
        'build/Microsoft.AspNetCore.StaticWebAssets.props',
        'build/Microsoft.AspNetCore.StaticWebAssetEndpoints.props',
        'buildMultiTargeting/Jiron.Umbraco.FormBuilder.Web.props',
        'buildTransitive/Jiron.Umbraco.FormBuilder.Web.props',
        'staticwebassets/App_Plugins/JironFormBuilder/umbraco-package.json',
        'staticwebassets/App_Plugins/JironFormBuilder/jfb-backoffice.js',
        'staticwebassets/App_Plugins/JironFormBuilder/types/jiron-form-builder.d.ts',
        'staticwebassets/App_Plugins/JironFormBuilder/chunks/designer-Cd3f9a1b.js',
        ...extra,
      ]);
    assert.deepEqual(web([]), []);
    assert.deepEqual(web(['staticwebassets/App_Plugins/JironFormBuilder/chunks/designer-Cd3f9a1b.js.map']), [
      'staticwebassets/App_Plugins/JironFormBuilder/chunks/designer-Cd3f9a1b.js.map is a source map, which carries the original sources',
    ]);
    assert.match(web(['appsettings.Development.json'])[0], /an appsettings file/);
    assert.match(web(['lib/net10.0/Jiron.Umbraco.FormBuilder.Testing.dll'])[0], /a test, demo or sample assembly/);
    assert.match(web(['.local/credentials.json'])[0], /from .local\/ or .private\//);
    assert.match(web(['content/site.pfx'])[0], /a key or certificate/);
    assert.deepEqual(web(['staticwebassets/App_Plugins/JironFormBuilder/src/main.ts']), ['staticwebassets/App_Plugins/JironFormBuilder/src/main.ts is not on the allowlist']);
  });

  test("the Core package's appsettings schema is no appsettings file", () => {
    const core = [
      'Jiron.Umbraco.FormBuilder.Core.nuspec',
      ...COMMON,
      'lib/net10.0/Jiron.Umbraco.FormBuilder.Core.dll',
      'lib/net10.0/Jiron.Umbraco.FormBuilder.Core.xml',
      'appsettings-schema.Jiron.Umbraco.FormBuilder.json',
      'buildTransitive/Jiron.Umbraco.FormBuilder.Core.props',
    ];
    assert.deepEqual(checkEntries('Jiron.Umbraco.FormBuilder.Core', core), []);
  });

  test('a symbol package holds the pdb and NuGet parts only, and an unknown package is named', () => {
    assert.deepEqual(checkEntries('Jiron.Umbraco.FormBuilder.Core', CORE_SYMBOLS, { symbols: true }), []);
    assert.deepEqual(checkEntries('Jiron.Umbraco.FormBuilder.Core', [...CORE_SYMBOLS, 'README.md'], { symbols: true }), ['README.md is not on the allowlist']);
    assert.deepEqual(checkEntries('Jiron.Umbraco.FormBuilder.Demo', []), ["Jiron.Umbraco.FormBuilder.Demo is not one of Jiron Form Builder's packages"]);
  });
});

describe("a package's nuspec (section 3.5)", () => {
  test('passes with the metadata every package carries, for each of the four', () => {
    for (const id of Object.keys(PACKAGES)) assert.deepEqual(checkNuspec(id, nuspecOf(id), VALUES), [], id);
  });

  test('names a wrong licence, readme, icon, tags, author, copyright or project', () => {
    const problems = (overrides) => checkNuspec('Jiron.Umbraco.FormBuilder', nuspecOf('Jiron.Umbraco.FormBuilder', overrides), VALUES);
    assert.deepEqual(problems({ license: '<license type="expression">MIT</license>' }), ['its licence is not the packed LICENSE.md']);
    assert.deepEqual(problems({ license: '<license type="file">LICENSE.md</license>\n<licenseUrl>https://example.test/licence</licenseUrl>' }), ['it names a licence URL']);
    assert.deepEqual(problems({ acceptance: 'false' }), ['it does not ask to accept the licence']);
    assert.deepEqual(problems({ readme: 'readme.txt' }), ['its readme is not README.md']);
    assert.deepEqual(problems({ icon: 'logo.png' }), ['its icon is not icon.png']);
    assert.deepEqual(problems({ tags: 'umbraco forms' }), [`its tags are not "${PACKAGES['Jiron.Umbraco.FormBuilder'].tags}"`]);
    assert.deepEqual(problems({ authors: 'Someone' }), ['its authors are not "Jiron"']);
    assert.deepEqual(problems({ copyright: 'Copyright (c) 2025 Someone' }), ['its copyright is not "Copyright (c) 2026 Someone"']);
    assert.deepEqual(problems({ projectUrl: 'https://example.test' }), [`its project URL is not ${PROJECT_URL}`]);
    assert.deepEqual(problems({ description: 'x'.repeat(4001) }), ['its description is longer than 4000 characters']);
  });

  test('the Marketplace tags belong to the packages a site installs; Core and Web carry "umbraco" alone', () => {
    assert.match(PACKAGES['Jiron.Umbraco.FormBuilder'].tags, /^umbraco umbraco-marketplace forms form-builder contact-form$/);
    assert.match(PACKAGES['Jiron.Umbraco.FormBuilder.AI'].tags, /^umbraco umbraco-marketplace forms form-builder contact-form ai$/);
    assert.equal(PACKAGES['Jiron.Umbraco.FormBuilder.Core'].tags, 'umbraco');
    assert.equal(PACKAGES['Jiron.Umbraco.FormBuilder.Web'].tags, 'umbraco');
  });

  test("depends on its siblings at exactly its version and on Umbraco's range as Directory.Packages.props says", () => {
    const main = 'Jiron.Umbraco.FormBuilder';
    const loose = nuspecOf(main).replace(`id="Jiron.Umbraco.FormBuilder.Core" version="[${VERSION}]"`, `id="Jiron.Umbraco.FormBuilder.Core" version="${VERSION}"`);
    assert.deepEqual(checkNuspec(main, loose, VALUES), ['it does not depend on Jiron.Umbraco.FormBuilder.Core at exactly its own version']);
    const floor = nuspecOf(main).replace('version="[17.6.0, 18.0.0)"', 'version="17.6.0"');
    assert.deepEqual(checkNuspec(main, floor, VALUES), ['it does not depend on Umbraco.Cms.Web.Website [17.6.0, 18.0.0) as Directory.Packages.props says']);
    const spaced = nuspecOf(main).replace('version="[17.6.0, 18.0.0)"', 'version="[17.6.0,18.0.0)"');
    assert.deepEqual(checkNuspec(main, spaced, VALUES), []);
    const extra = nuspecOf(main, { dependencies: `${nuspecOf(main).match(/<dependency[\s\S]*\/>/)[0]}\n<dependency id="Newtonsoft.Json" version="13.0.3" />` });
    assert.deepEqual(checkNuspec(main, extra, VALUES), ['it depends on Newtonsoft.Json, which is not expected']);
  });

  test('reads the authors, the copyright and the versions from the build files', () => {
    const values = buildValues();
    assert.equal(values.authors, 'Jiron');
    assert.match(values.copyright, /^Copyright \(c\) \d{4} /);
    assert.match(values.versions.get('Umbraco.Cms.Web.Website'), /^\[17\.\d+\.\d+, 18\.0\.0\)$/);
  });
});

describe('the files a package copies from the repository', () => {
  const files = { 'LICENSE.md': 'the licence', 'docs/README.nuget.md': 'the readme', 'docs/README.nuget.ai.md': 'the AI readme', 'assets/icon-128.png': 'the icon' };
  const read = (path) => Buffer.from(files[path]);
  const entries = (pairs) => pairs.map(([name, content]) => ({ name, read: () => Buffer.from(content) }));

  test('are the licence, the readme and the icon byte for byte', () => {
    assert.deepEqual(checkCopies('Jiron.Umbraco.FormBuilder', entries([['LICENSE.md', 'the licence'], ['README.md', 'the readme'], ['icon.png', 'the icon']]), read), []);
    assert.deepEqual(checkCopies('Jiron.Umbraco.FormBuilder.AI', entries([['LICENSE.md', 'the licence'], ['README.md', 'the AI readme'], ['icon.png', 'the icon']]), read), []);
  });

  test("names a changed licence, the wrong package's readme and another icon", () => {
    assert.deepEqual(
      checkCopies('Jiron.Umbraco.FormBuilder.AI', entries([['LICENSE.md', 'the licence, edited'], ['README.md', 'the readme'], ['icon.png', 'a logo']]), read),
      [
        "LICENSE.md is not LICENSE.md, the owner's licence (D2)",
        'README.md is not docs/README.nuget.ai.md, its readme',
        'icon.png is not assets/icon-128.png, the mark at 128 px',
      ],
    );
  });
});

describe('a folder of packages', () => {
  test('needs every package with its symbols, in one version', () => {
    const dir = mkdtempSync(join(tmpdir(), 'jfb-check-packages-'));
    try {
      const id = 'Jiron.Umbraco.FormBuilder';
      const copies = { 'LICENSE.md': 'L', 'docs/README.nuget.md': 'R', 'assets/icon-128.png': 'I' };
      const main = MAIN.map((name) => [name, name === `${id}.nuspec` ? nuspecOf(id) : name === 'LICENSE.md' ? 'L' : name === 'README.md' ? 'R' : name === 'icon.png' ? 'I' : '']);
      writeFileSync(join(dir, `${id}.${VERSION}.nupkg`), zipOf(main));
      writeFileSync(join(dir, `Jiron.Umbraco.FormBuilder.Core.17.0.0-alpha.2.snupkg`), zipOf(CORE_SYMBOLS.map((name) => [name])));
      const lines = [];
      const problems = checkFolder(dir, { log: (line) => lines.push(line), values: VALUES, read: (path) => Buffer.from(copies[path]) });
      assert.deepEqual(lines, [`ok   ${id}.${VERSION}.nupkg (${MAIN.length} entries)`, `ok   Jiron.Umbraco.FormBuilder.Core.17.0.0-alpha.2.snupkg (5 entries)`]);
      assert.deepEqual(problems, [
        'Jiron.Umbraco.FormBuilder.Core: no .nupkg in the folder',
        'Jiron.Umbraco.FormBuilder.Web: no .nupkg in the folder',
        'Jiron.Umbraco.FormBuilder.Web: no .snupkg in the folder',
        'Jiron.Umbraco.FormBuilder.AI: no .nupkg in the folder',
        'Jiron.Umbraco.FormBuilder.AI: no .snupkg in the folder',
        `more than one version: ${VERSION}, 17.0.0-alpha.2`,
      ]);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
