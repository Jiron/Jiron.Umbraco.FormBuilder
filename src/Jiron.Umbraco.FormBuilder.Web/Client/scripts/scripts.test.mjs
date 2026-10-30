// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Tests for the client build scripts (ROADMAP 3.2, ADR-0026). The licence text comes from LICENSE.md itself, so this
// file stays ASCII.
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, relative, sep } from 'node:path';
import { pathToFileURL } from 'node:url';
import { brotliDecompressSync, gunzipSync } from 'node:zlib';
import { build } from 'vite';
import { afterEach, describe, expect, it } from 'vitest';
import { bannerProblem, licenceBanner, licensePath, requiredNotices } from './banner.mjs';
import { compressFolder, walk } from './compress.mjs';
import {
  assemblyProblems,
  check,
  inputFiles,
  inputsHash,
  OUTPUTS,
  outputFiles,
  outputProblems,
  write,
} from './stamp.mjs';
import { buildVersion, versionFromProps } from './version.mjs';
import { sharedBuild, sharedPlugins, terserPass } from './vite-shared.mjs';

const licenseText = readFileSync(licensePath, 'utf8');
const VERSION = '17.0.0-test.1';
const folders = [];

function tempFolder() {
  const dir = mkdtempSync(join(tmpdir(), 'jfb-client-test-'));
  folders.push(dir);
  return dir;
}

afterEach(() => {
  for (const dir of folders.splice(0)) rmSync(dir, { recursive: true, force: true });
});

function put(root, file, content) {
  const path = join(root, ...file.split('/'));
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, content);
}

const rel = (root, path) => relative(root, path).split(sep).join('/');

describe('version', () => {
  it('reads VersionPrefix and VersionSuffix', () => {
    expect(versionFromProps('<VersionPrefix>17.0.0</VersionPrefix><VersionSuffix>alpha.0</VersionSuffix>')).toBe(
      '17.0.0-alpha.0',
    );
    expect(versionFromProps('<VersionPrefix>17.1.0</VersionPrefix><VersionSuffix></VersionSuffix>')).toBe('17.1.0');
    expect(() => versionFromProps('<Project />')).toThrow(/VersionPrefix/);
  });

  it('prefers JFB_VERSION and refuses one that is not a semantic version', () => {
    expect(buildVersion({ JFB_VERSION: '17.0.0-dev.202610231200' })).toBe('17.0.0-dev.202610231200');
    expect(() => buildVersion({ JFB_VERSION: '17.0' })).toThrow(/semantic version/);
    expect(buildVersion({})).toMatch(/^17[.][0-9]+[.][0-9]+/);
  });
});

describe('the licence banner', () => {
  it('carries the version, the terms and every Required Notice line of LICENSE.md on a line of its own', () => {
    const notices = requiredNotices(licenseText);
    expect(notices.length).toBeGreaterThan(0);
    const lines = licenceBanner(VERSION, licenseText).split('\n');
    expect(lines[0]).toMatch(/^[/][*]! Jiron Form Builder 17[.]0[.]0-test[.]1[.] /);
    expect(lines[0]).toContain('https://polyformproject.org/licenses/perimeter/1.0.1');
    for (const notice of notices) expect(lines).toContain(notice);
    expect(lines.at(-1)).toBe('*/');
  });

  it('refuses a licence without a Required Notice line', () => {
    expect(() => requiredNotices('Some licence\nwith no notice\n')).toThrow(/Required Notice/);
  });

  it('tells a file with the banner from one without it, with another banner or with the notice left out', () => {
    const banner = licenceBanner(VERSION, licenseText);
    expect(bannerProblem(`${banner}\nconsole.log(1);`, VERSION, licenseText)).toBeNull();
    expect(bannerProblem('console.log(1);', VERSION, licenseText)).toBe('has no licence banner');
    expect(bannerProblem('/*! Jiron Form Builder */\nx', VERSION, licenseText)).toBe('has no Required Notice line');
    expect(bannerProblem(`${licenceBanner('17.0.0', licenseText)}\nx`, VERSION, licenseText)).toMatch(/not the one of/);
  });
});

describe('compression', () => {
  it('writes a Brotli and a gzip copy of every file and removes copies whose file is gone', () => {
    const dir = tempFolder();
    put(dir, 'runtime/jfb-runtime.js', 'export const a = 1;'.repeat(50));
    put(dir, 'old/gone.js.br', 'stale');
    const result = compressFolder(dir);
    expect(result.written).toEqual(['runtime/jfb-runtime.js']);
    expect(result.removed).toEqual(['old/gone.js.br']);
    const original = readFileSync(join(dir, 'runtime', 'jfb-runtime.js'));
    expect(brotliDecompressSync(readFileSync(join(dir, 'runtime', 'jfb-runtime.js.br'))).equals(original)).toBe(true);
    expect(gunzipSync(readFileSync(join(dir, 'runtime', 'jfb-runtime.js.gz'))).equals(original)).toBe(true);
    expect(compressFolder(dir).written).toEqual(['runtime/jfb-runtime.js']);
  });
});

describe('the shared build', () => {
  it('minifies, gives a feature loaded on demand its own hashed chunk, and puts the banner on every JS and CSS file', async () => {
    const dir = tempFolder();
    put(
      dir,
      'src/entry.ts',
      "import './entry.css';\nexport const load = async () => (await import('./upload')).upload;\n",
    );
    put(dir, 'src/upload.ts', "export const upload = (name: string) => 'uploading ' + name;\n");
    put(dir, 'src/entry.css', '.jfb-form {\n  margin-block: 1rem;\n}\n');
    const outDir = join(dir, 'out');
    await build({
      configFile: false,
      root: dir,
      logLevel: 'silent',
      publicDir: false,
      build: {
        ...sharedBuild({ version: VERSION, outDir }),
        lib: { entry: join(dir, 'src', 'entry.ts'), formats: ['es'], fileName: () => 'entry.js', cssFileName: 'entry' },
      },
      plugins: sharedPlugins({ version: VERSION, name: 'fixture', buildDir: join(dir, 'reports'), base: dir }),
    });

    const files = [...walk(outDir)].map((path) => rel(outDir, path));
    const chunk = files.find((file) => /^chunks[/]upload-[A-Za-z0-9_-]+[.]js$/.test(file));
    expect(files).toContain('entry.js');
    expect(chunk).toBeDefined();
    expect(files.filter((file) => file.endsWith('.css'))).toHaveLength(1);
    expect(files.filter((file) => file.endsWith('.map'))).toEqual([]);
    for (const file of files.filter((f) => /[.](js|css)$/.test(f))) {
      expect(bannerProblem(readFileSync(join(outDir, file), 'utf8'), VERSION), file).toBeNull();
    }
    const banner = licenceBanner(VERSION);
    const entryCode = readFileSync(join(outDir, 'entry.js'), 'utf8').slice(banner.length + 1);
    expect(entryCode.trim()).not.toContain('\n');

    const report = JSON.parse(readFileSync(join(dir, 'reports', 'fixture.chunks.json'), 'utf8'));
    expect(report.folder).toBe('out');
    expect(report.files.find((file) => file.file === 'entry.js')).toMatchObject({
      entry: true,
      dynamicImports: [chunk],
    });
  }, 60_000);

  it('names a chunk by its content: the same sources give the same name, a changed module a new one, and the entry keeps its own', async () => {
    const dir = tempFolder();
    put(dir, 'src/entry.ts', "export const load = async () => (await import('./count')).count;\n");
    const built = async (name, body) => {
      put(dir, 'src/count.ts', body);
      const outDir = join(dir, name);
      await build({
        configFile: false,
        root: dir,
        logLevel: 'silent',
        publicDir: false,
        build: {
          ...sharedBuild({ version: VERSION, outDir }),
          lib: { entry: join(dir, 'src', 'entry.ts'), formats: ['es'], fileName: () => 'entry.js' },
        },
        plugins: [
          ...sharedPlugins({ version: VERSION, name: 'count', buildDir: join(dir, 'reports', name), base: dir }),
        ],
      });
      return [...walk(outDir)]
        .map((path) => rel(outDir, path))
        .filter((file) => file.endsWith('.js'))
        .sort();
    };
    const first = await built('first', 'export const count = (words: string[]): number => words.length;\n');
    const again = await built('again', 'export const count = (words: string[]): number => words.length;\n');
    const changed = await built('changed', 'export const count = (words: string[]): number => words.length + 1;\n');
    expect(first).toContain('entry.js');
    expect(first.some((file) => /^chunks[/]count-[A-Za-z0-9_-]{8}[.]js$/.test(file))).toBe(true);
    expect(again).toEqual(first);
    expect(changed).toContain('entry.js');
    expect(changed.find((file) => file.startsWith('chunks/count-'))).not.toBe(
      first.find((file) => file.startsWith('chunks/count-')),
    );
  });

  it('takes a terser pass where asked, which writes no more, keeps the banner and the behaviour, and skips a build with source maps', async () => {
    const dir = tempFolder();
    put(dir, 'src/entry.ts', "export const load = async () => (await import('./count')).count;\n");
    put(
      dir,
      'src/count.ts',
      'export function count(words: readonly string[]): number {\n  let total = 0;\n  for (const word of words) {\n    if (word.length > 0) {\n      total = total + 1;\n    }\n  }\n  return total;\n}\n',
    );
    const built = async (name, plugins) => {
      const outDir = join(dir, name);
      await build({
        configFile: false,
        root: dir,
        logLevel: 'silent',
        publicDir: false,
        build: {
          ...sharedBuild({ version: VERSION, outDir }),
          lib: { entry: join(dir, 'src', 'entry.ts'), formats: ['es'], fileName: () => 'entry.js' },
        },
        plugins: [...sharedPlugins({ version: VERSION, name, buildDir: join(dir, 'reports'), base: dir }), ...plugins],
      });
      const file = [...walk(outDir)].find((path) => /count-[A-Za-z0-9_-]+[.]js$/.test(path));
      if (file === undefined) throw new Error(`no count chunk in ${name}`);
      return file;
    };
    const plain = await built('plain', []);
    const tersed = await built('tersed', [terserPass({})]);

    const code = readFileSync(tersed, 'utf8');
    expect(bannerProblem(code, VERSION)).toBeNull();
    expect(code.length).toBeLessThanOrEqual(readFileSync(plain, 'utf8').length);
    const { count } = await import(pathToFileURL(tersed).href);
    expect(count(['a', '', 'b'])).toBe(2);
    expect(terserPass({}).apply()).toBe(true);
    expect(terserPass({ JFB_SOURCEMAP: '1' }).apply()).toBe(false);
  }, 60_000);
});

describe('the stamp', () => {
  /** A client folder with one source and one test, and a Web project with a complete, current build. */
  function setup() {
    const client = tempFolder();
    put(client, 'package.json', '{}');
    put(client, 'packages/runtime/src/jfb-runtime.ts', 'export const a = 1;');
    put(client, 'packages/runtime/src/jfb-runtime.test.ts', 'test');
    const project = tempFolder();
    const banner = licenceBanner(VERSION, licenseText);
    for (const file of OUTPUTS) {
      if (file.endsWith('umbraco-package.json')) put(project, file, JSON.stringify({ version: VERSION }));
      else put(project, file, `${banner}\nexport{};`);
    }
    compressFolder(join(project, 'WebsiteAssets'));
    write({ root: client, projectRoot: project, version: VERSION, licenseText });
    return { client, project, banner };
  }

  const checkFor = ({ client, project }, packageVersion = VERSION) =>
    check({ root: client, projectRoot: project, packageVersion, licenseText });

  it('hashes the sources but not the tests', () => {
    const { client } = setup();
    expect(inputFiles(client).map((file) => rel(client, file))).toEqual([
      'package.json',
      'packages/runtime/src/jfb-runtime.ts',
    ]);
    const before = inputsHash(client);
    put(client, 'packages/runtime/src/jfb-runtime.test.ts', 'changed test');
    expect(inputsHash(client)).toBe(before);
    put(client, 'packages/runtime/src/jfb-runtime.ts', 'export const a = 2;');
    expect(inputsHash(client)).not.toBe(before);
  });

  it('lets a complete, current build of the package version through', () => {
    expect(checkFor(setup())).toBeNull();
  });

  it('refuses to pack when the client was never built', () => {
    const { client, project } = setup();
    rmSync(join(client, '.build'), { recursive: true });
    expect(checkFor({ client, project })).toMatch(/never built/);
  });

  it('refuses to pack when a source changed since the build', () => {
    const built = setup();
    put(built.client, 'packages/runtime/src/jfb-runtime.ts', 'export const a = 2;');
    expect(checkFor(built)).toMatch(/sources changed/);
  });

  it('refuses to pack assets built for another version', () => {
    expect(checkFor(setup(), '17.0.0-test.2')).toMatch(
      /built for 17[.]0[.]0-test[.]1, but the package is 17[.]0[.]0-test[.]2/,
    );
  });

  it('refuses to pack an asset without the licence notice', () => {
    const built = setup();
    put(built.project, 'WebsiteAssets/runtime/jfb-runtime.js', 'export{};');
    compressFolder(join(built.project, 'WebsiteAssets'));
    expect(checkFor(built)).toMatch(/WebsiteAssets[/]runtime[/]jfb-runtime[.]js has no licence banner/);
  });

  it('refuses to pack a banner whose Required Notice line was removed', () => {
    const built = setup();
    const withoutNotice = built.banner
      .split('\n')
      .filter((line) => !line.startsWith('Required Notice:'))
      .join('\n');
    put(built.project, 'wwwroot/App_Plugins/JironFormBuilder/jfb-backoffice.js', `${withoutNotice}\nexport{};`);
    expect(checkFor(built)).toMatch(/jfb-backoffice[.]js has no Required Notice line/);
  });

  it('refuses to pack a website asset whose compressed copy is missing or stale', () => {
    const built = setup();
    rmSync(join(built.project, 'WebsiteAssets', 'element', 'jfb-element.js.br'));
    put(built.project, 'WebsiteAssets/runtime/jfb-runtime.js.gz', 'not a copy');
    const problem = checkFor(built);
    expect(problem).toMatch(/files appeared|has no [.]br copy/);
    expect(outputProblems({ projectRoot: built.project, version: VERSION, licenseText })).toEqual(
      expect.arrayContaining([
        'WebsiteAssets/element/jfb-element.js has no .br copy',
        expect.stringMatching(/jfb-runtime[.]js[.]gz/),
      ]),
    );
  });

  it('refuses to pack a source map, and a file that appeared after the build', () => {
    const built = setup();
    put(built.project, 'wwwroot/App_Plugins/JironFormBuilder/jfb-backoffice.js.map', '{}');
    expect(checkFor(built)).toMatch(/appeared in the output after the build/);
    expect(outputProblems({ projectRoot: built.project, version: VERSION, licenseText, packing: true })).toEqual([
      expect.stringMatching(/source maps would be packed/),
    ]);
  });

  it('refuses to pack an assembly that does not embed the current website assets', () => {
    const built = setup();
    const assembly = join(built.project, 'bin', 'Jiron.Umbraco.FormBuilder.Web.dll');
    const embedAll = () =>
      put(
        built.project,
        'bin/Jiron.Umbraco.FormBuilder.Web.dll',
        Buffer.concat([
          Buffer.from('MZ header'),
          ...outputFiles(built.project)
            .filter((file) => file.startsWith('WebsiteAssets/'))
            .map((file) => readFileSync(join(built.project, file))),
        ]),
      );
    expect(
      check({ root: built.client, projectRoot: built.project, packageVersion: VERSION, licenseText, assembly }),
    ).toMatch(/does not exist: build the Web project before packing/);
    embedAll();
    expect(
      check({ root: built.client, projectRoot: built.project, packageVersion: VERSION, licenseText, assembly }),
    ).toBeNull();

    // The client was built again (another bundle), but the assembly was not (dotnet pack --no-build).
    put(built.project, 'WebsiteAssets/runtime/jfb-runtime.js', `${built.banner}\nexport const changed=1;`);
    compressFolder(join(built.project, 'WebsiteAssets'));
    expect(assemblyProblems(built.project, assembly)).toEqual([
      expect.stringMatching(
        /^the assembly does not embed the current WebsiteAssets[/]runtime[/]jfb-runtime[.]js, WebsiteAssets[/]runtime[/]jfb-runtime[.]js[.]br, WebsiteAssets[/]runtime[/]jfb-runtime[.]js[.]gz: build the Web project again/,
      ),
    ]);
    embedAll();
    expect(assemblyProblems(built.project, assembly)).toEqual([]);
  });

  it('refuses to record a build with an output missing', () => {
    const built = setup();
    rmSync(join(built.project, 'WebsiteAssets', 'element', 'jfb-element.js'));
    expect(() => write({ root: built.client, projectRoot: built.project, version: VERSION, licenseText })).toThrow(
      /missing: WebsiteAssets[/]element[/]jfb-element[.]js/,
    );
    expect(existsSync(join(built.client, '.build', 'stamp.json'))).toBe(true);
  });
});
