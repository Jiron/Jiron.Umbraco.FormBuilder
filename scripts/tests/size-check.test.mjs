// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/size-check.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { BUDGETS, backofficeLanguage, check, gzipSize, isHashed, isWords, measure, scopeOf, staticClosure } from '../size-check.mjs';

const folders = [];
after(() => {
  for (const dir of folders) rmSync(dir, { recursive: true, force: true });
});

/** A Web project with built files and the chunk reports that describe them. */
function project(reports) {
  const web = mkdtempSync(join(tmpdir(), 'jfb-size-'));
  folders.push(web);
  const reportDir = join(web, 'Client', '.build');
  mkdirSync(reportDir, { recursive: true });
  for (const [name, { folder, files }] of Object.entries(reports)) {
    writeFileSync(join(reportDir, `${name}.chunks.json`), JSON.stringify({ folder, files: files.map((f) => f.meta) }));
    for (const file of files) {
      const path = join(web, ...folder.split('/'), ...file.meta.file.split('/'));
      mkdirSync(dirname(path), { recursive: true });
      writeFileSync(path, file.bytes);
    }
  }
  return { reports: reportDir, webProject: web };
}

const small = (name) => Buffer.from(`export const ${name} = 1;`);
const chunk = (file, imports = [], dynamicImports = [], entry = false, modules = []) => ({
  file,
  entry,
  imports,
  dynamicImports,
  modules,
});

describe('the budgets', () => {
  test('are those of ROADMAP section 4.12', () => {
    assert.deepEqual(BUDGETS, {
      runtimeCore: 16 * 1024,
      runtimeAll: 60 * 1024,
      themeCss: 10 * 1024,
      backofficeStart: 64 * 1024,
      backofficeFile: 64 * 1024,
      backofficeAll: 512 * 1024,
    });
  });
});

describe('what a page loads at once', () => {
  test('is the entry and its static imports, transitively, but no feature loaded on demand', () => {
    const report = {
      files: [
        chunk('jfb-runtime.js', ['chunks/shared-a.js'], ['chunks/upload-b.js'], true),
        chunk('chunks/shared-a.js', ['chunks/text-c.js']),
        chunk('chunks/text-c.js'),
        chunk('chunks/upload-b.js', ['chunks/shared-a.js']),
      ],
    };
    assert.deepEqual(staticClosure(report), ['chunks/shared-a.js', 'chunks/text-c.js', 'jfb-runtime.js']);
  });
});

describe('the check', () => {
  const base = () => ({
    element: { folder: 'WebsiteAssets/element', files: [{ meta: chunk('jfb-element.js', [], [], true), bytes: small('e') }] },
    themes: { folder: 'WebsiteAssets/themes', files: [{ meta: chunk('base.css'), bytes: small('c') }] },
    backoffice: {
      folder: 'wwwroot/App_Plugins/JironFormBuilder',
      files: [{ meta: chunk('jfb-backoffice.js', [], [], true), bytes: small('b') }],
    },
  });

  test('measures the core and the whole runtime, and reports the others without a budget', () => {
    const upload = randomBytes(2000);
    const paths = project({
      ...base(),
      runtime: {
        folder: 'WebsiteAssets/runtime',
        files: [
          { meta: chunk('jfb-runtime.js', [], ['chunks/upload-Ab3_x9Zq.js'], true), bytes: small('r') },
          { meta: chunk('chunks/upload-Ab3_x9Zq.js'), bytes: upload },
        ],
      },
    });
    const results = check(paths);
    assert.equal(results.every((r) => r.ok), true);
    assert.ok(results.some((r) => r.line === 'runtime chunk names: every file but the entry carries a content hash, ok'));
    const core = results.find((r) => r.line.startsWith('runtime core'));
    const all = results.find((r) => r.line.startsWith('runtime with every feature'));
    assert.match(core.line, /[(]1 file: the entry/);
    assert.match(all.line, /[(]2 files[)]/);
    assert.equal(
      measure({ folder: 'WebsiteAssets/runtime' }, ['jfb-runtime.js', 'chunks/upload-Ab3_x9Zq.js'], paths.webProject),
      gzipSize(small('r')) + gzipSize(upload),
    );
    assert.ok(results.some((r) => /^stylesheet base[.]css: .* gzip of 10[.]00 KB, ok$/.test(r.line)));
    assert.ok(results.some((r) => /^element [(]1 file[)]: .* gzip, no budget/.test(r.line)));
    assert.ok(results.some((r) => /^backoffice bundle [(]1 file[)]: .* gzip of 512[.]00 KB, ok$/.test(r.line)));
  });

  test("counts one language's words, the largest, in every feature, and reports the sum with every language", () => {
    const lang = (language) => [`Client/packages/logic/src/lang/messages.${language}.ts`];
    const english = randomBytes(3000);
    const german = randomBytes(4000);
    const feature = randomBytes(54 * 1024);
    const paths = project({
      ...base(),
      runtime: {
        folder: 'WebsiteAssets/runtime',
        files: [
          { meta: chunk('jfb-runtime.js', [], ['chunks/form-x.js'], true), bytes: small('r') },
          { meta: chunk('chunks/form-x.js', [], ['chunks/messages.en-x.js', 'chunks/messages.de-x.js'], false, ['Client/packages/runtime/src/form.ts']), bytes: feature },
          { meta: chunk('chunks/messages.en-x.js', [], [], false, lang('en')), bytes: english },
          { meta: chunk('chunks/messages.de-x.js', [], [], false, lang('de')), bytes: german },
        ],
      },
    });
    const results = check(paths);
    const every = results.find((r) => r.line.startsWith('runtime with every feature ('));
    const all = results.find((r) => r.line.startsWith('runtime with every feature and every language'));
    assert.match(every.line, /[(]2 files, and the largest of 2 languages' words[)]: /);
    const counted = gzipSize(small('r')) + gzipSize(feature) + Math.max(gzipSize(english), gzipSize(german));
    assert.match(every.line, new RegExp(`: ${(counted / 1024).toFixed(2)} KB gzip of 60[.]00 KB, ok$`));
    assert.equal(every.ok, true);
    const sum = gzipSize(small('r')) + gzipSize(feature) + gzipSize(english) + gzipSize(german);
    assert.ok(sum > 60 * 1024, 'every language together is over the budget, which no page loads');
    assert.equal(all.ok, true);
    assert.match(all.line, /[(]4 files[)]: .* gzip, more than a page loads$/);
  });

  test('takes a file for words only when every module it holds is a language\'s', () => {
    assert.equal(isWords(chunk('chunks/messages.en-x.js', [], [], false, ['Client/packages/logic/src/lang/messages.en.ts'])), true);
    assert.equal(isWords(chunk('chunks/form-x.js', [], [], false, ['Client/packages/logic/src/lang/messages.en.ts', 'Client/packages/runtime/src/form.ts'])), false);
    assert.equal(isWords(chunk('chunks/messages.en-x.js')), false, 'a report without modules counts the file as a feature');
  });

  test('fails for a chunk whose name carries no content hash, and names it; the entry keeps its own name', () => {
    const paths = project({
      ...base(),
      runtime: {
        folder: 'WebsiteAssets/runtime',
        files: [
          { meta: chunk('jfb-runtime.js', [], ['chunks/form-C6Ttrlit.js', 'chunks/upload.js'], true), bytes: small('r') },
          { meta: chunk('chunks/form-C6Ttrlit.js'), bytes: small('f') },
          { meta: chunk('chunks/upload.js'), bytes: small('u') },
        ],
      },
    });
    const names = check(paths).find((r) => r.line.startsWith('runtime chunk names'));
    assert.equal(names.ok, false);
    assert.equal(names.line, 'runtime chunk names: WITHOUT A CONTENT HASH: chunks/upload.js');
    assert.equal(isHashed('chunks/messages.de-CzJlC_Mw.js'), true);
    assert.equal(isHashed('chunks/form-short.js'), false, 'a hash has eight characters');
    assert.equal(isHashed('jfb-runtime.js'), false);
  });

  test('fails when the core is over its budget, although a feature chunk is not counted in it', () => {
    const big = randomBytes(17 * 1024);
    const paths = project({
      ...base(),
      runtime: { folder: 'WebsiteAssets/runtime', files: [{ meta: chunk('jfb-runtime.js', [], [], true), bytes: big }] },
    });
    const core = check(paths).find((r) => r.line.startsWith('runtime core'));
    assert.equal(core.ok, false);
    assert.match(core.line, /OVER BUDGET$/);
  });

  test('holds each stylesheet to 10 KB', () => {
    const paths = project({
      ...base(),
      runtime: { folder: 'WebsiteAssets/runtime', files: [{ meta: chunk('jfb-runtime.js', [], [], true), bytes: small('r') }] },
      themes: { folder: 'WebsiteAssets/themes', files: [{ meta: chunk('default.css'), bytes: randomBytes(11 * 1024) }] },
    });
    const theme = check(paths).find((r) => r.line.startsWith('stylesheet default.css'));
    assert.equal(theme.ok, false);
    assert.match(theme.line, /OVER BUDGET$/);
  });

  test("lints each stylesheet as it is served: the base rules under the form, a theme's under its own forms", () => {
    assert.equal(scopeOf('base.css'), '.jfb-form');
    assert.equal(scopeOf('default.css'), '.jfb-form:where(.jfb-theme-default)');
    const css = (text) => Buffer.from(text);
    const paths = project({
      ...base(),
      runtime: { folder: 'WebsiteAssets/runtime', files: [{ meta: chunk('jfb-runtime.js', [], [], true), bytes: small('r') }] },
      themes: {
        folder: 'WebsiteAssets/themes',
        files: [
          { meta: chunk('base.css'), bytes: css('.jfb-form [hidden]{display:none}') },
          { meta: chunk('default.css'), bytes: css('.jfb-form:where(.jfb-theme-default){color:red;& .a{margin-inline-start:0}}@keyframes jfb-turn{to{rotate:1turn}}') },
          { meta: chunk('plain.css'), bytes: css('.jfb-form .a{color:red}.jfb-form:where(.jfb-theme-plain){& .b{padding-left:1px}}') },
        ],
      },
    });
    const results = check(paths);
    const line = (file) => results.filter((r) => r.line.startsWith(`stylesheet ${file}: `) && !/gzip/.test(r.line));
    assert.deepEqual(line('base.css'), [{ ok: true, line: 'stylesheet base.css: every rule under .jfb-form, nothing physical, ok' }]);
    assert.deepEqual(line('default.css'), [{ ok: true, line: 'stylesheet default.css: every rule under .jfb-form:where(.jfb-theme-default), nothing physical, ok' }]);
    assert.deepEqual(line('plain.css'), [
      { ok: false, line: 'stylesheet plain.css: REFUSED, outside .jfb-form:where(.jfb-theme-plain): .jfb-form .a; physical: padding-left: 1px' },
    ]);
  });

  describe('the backoffice', () => {
    const runtime = { folder: 'WebsiteAssets/runtime', files: [{ meta: chunk('jfb-runtime.js', [], [], true), bytes: small('r') }] };
    const words = (language) => [`Client/packages/backoffice/src/lang/${language}.ts`, `Client/packages/backoffice/src/lang/designer.${language}.ts`];
    const backoffice = (files) => project({ ...base(), runtime, backoffice: { folder: 'wwwroot/App_Plugins/JironFormBuilder', files } });
    const line = (paths, start) => check(paths).find((r) => r.line.startsWith(start));

    test("counts at start the entry, its static imports and the words of English and of the largest other language, never a screen's chunk or Umbraco's modules", () => {
      const [entry, shared, english, german, danish, designer] = [2000, 3000, 4000, 5000, 4500, 9000].map((size) => randomBytes(size));
      const paths = backoffice([
        { meta: chunk('jfb-backoffice.js', ['chunks/api-x.js', '@umbraco-cms/backoffice/lit-element'], ['chunks/en-x.js', 'chunks/de-x.js', 'chunks/da-x.js', 'chunks/designer-x.js'], true), bytes: entry },
        { meta: chunk('chunks/api-x.js'), bytes: shared },
        { meta: chunk('chunks/en-x.js', [], [], false, words('en')), bytes: english },
        { meta: chunk('chunks/de-x.js', [], [], false, words('de')), bytes: german },
        { meta: chunk('chunks/da-x.js', [], [], false, words('da')), bytes: danish },
        { meta: chunk('chunks/designer-x.js', ['chunks/api-x.js'], [], false, ['Client/packages/backoffice/src/designer/form-design.view.ts']), bytes: designer },
      ]);
      const start = line(paths, 'backoffice at start');
      const counted = [entry, shared, english, german].reduce((sum, bytes) => sum + gzipSize(bytes), 0);
      assert.equal(
        start.line,
        `backoffice at start (4 files: the entry, its static imports, and the words of English and of the largest other language): ${(counted / 1024).toFixed(2)} KB gzip of 64.00 KB, ok`,
      );
      assert.equal(line(paths, "backoffice's largest file").line.startsWith("backoffice's largest file (chunks/designer-x.js): "), true);
      assert.match(line(paths, 'backoffice bundle').line, /^backoffice bundle [(]6 files[)]: .* gzip of 512[.]00 KB, ok$/);
    });

    test("takes a file for a language's words only when every module it holds is the backoffice's words, one of them the language's table", () => {
      assert.equal(backofficeLanguage(chunk('chunks/en-x.js', [], [], false, words('en'))), 'en');
      assert.equal(backofficeLanguage(chunk('chunks/de-x.js', [], [], false, words('de'))), 'de');
      assert.equal(backofficeLanguage(chunk('chunks/x.js', [], [], false, ['Client/packages/backoffice/src/lang/designer.en.ts'])), null, 'no table');
      assert.equal(backofficeLanguage(chunk('chunks/x.js', [], [], false, [...words('en'), 'Client/packages/backoffice/src/designer/form-design.view.ts'])), null);
      assert.equal(backofficeLanguage(chunk('chunks/x.js')), null, 'a report without modules');
    });

    test('fails at start over 64 KB, though no file is over its own budget', () => {
      const paths = backoffice([
        { meta: chunk('jfb-backoffice.js', ['chunks/api-x.js'], ['chunks/en-x.js'], true), bytes: randomBytes(33 * 1024) },
        { meta: chunk('chunks/api-x.js'), bytes: randomBytes(20 * 1024) },
        { meta: chunk('chunks/en-x.js', [], [], false, words('en')), bytes: randomBytes(12 * 1024) },
      ]);
      assert.match(line(paths, 'backoffice at start').line, /OVER BUDGET$/);
      assert.match(line(paths, "backoffice's largest file").line, /, ok$/);
    });

    test('fails for any one file over 64 KB, though it loads only on demand', () => {
      const paths = backoffice([
        { meta: chunk('jfb-backoffice.js', [], ['chunks/designer-x.js'], true), bytes: small('b') },
        { meta: chunk('chunks/designer-x.js'), bytes: randomBytes(65 * 1024) },
      ]);
      assert.match(line(paths, 'backoffice at start').line, /, ok$/);
      assert.equal(line(paths, "backoffice's largest file").ok, false);
      assert.match(line(paths, "backoffice's largest file").line, /^backoffice's largest file [(]chunks[/]designer-x[.]js[)]: .* OVER BUDGET$/);
    });

    test('fails for the whole bundle over 512 KB, though each file and the start are within theirs', () => {
      const screens = Array.from({ length: 9 }, (_, n) => ({ meta: chunk(`chunks/screen-${n}.js`), bytes: randomBytes(60 * 1024) }));
      const paths = backoffice([{ meta: chunk('jfb-backoffice.js', [], screens.map((s) => s.meta.file), true), bytes: small('b') }, ...screens]);
      assert.match(line(paths, "backoffice's largest file").line, /, ok$/);
      assert.equal(line(paths, 'backoffice bundle').ok, false);
      assert.match(line(paths, 'backoffice bundle').line, /^backoffice bundle [(]10 files[)]: .* OVER BUDGET$/);
    });
  });

  test('fails without a build, and names a file that is missing', () => {
    const empty = project({});
    assert.ok(check(empty).some((r) => !r.ok && /runtime: no chunk report/.test(r.line)));
    assert.ok(check(empty).some((r) => !r.ok && /themes: no chunk report/.test(r.line)), 'the stylesheets are always built');
    const paths = project({
      ...base(),
      runtime: { folder: 'WebsiteAssets/runtime', files: [{ meta: chunk('jfb-runtime.js', [], [], true), bytes: small('r') }] },
    });
    rmSync(join(paths.webProject, 'WebsiteAssets', 'runtime', 'jfb-runtime.js'));
    assert.throws(() => check(paths), /WebsiteAssets[/]runtime[/]jfb-runtime[.]js is missing/);
  });
});
