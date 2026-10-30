// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/size-check.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { BUDGETS, check, gzipSize, isHashed, isWords, measure, scopeOf, staticClosure } from '../size-check.mjs';

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
    assert.deepEqual(BUDGETS, { runtimeCore: 16 * 1024, runtimeAll: 40 * 1024, themeCss: 10 * 1024 });
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
    assert.ok(results.some((r) => /^backoffice bundle [(]1 file[)]: .* gzip, no budget yet [(]9[.]6/.test(r.line)));
  });

  test("counts one language's words, the largest, in every feature, and reports the sum with every language", () => {
    const lang = (language) => [`Client/packages/logic/src/lang/messages.${language}.ts`];
    const english = randomBytes(3000);
    const german = randomBytes(4000);
    const feature = randomBytes(34 * 1024);
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
    assert.match(every.line, new RegExp(`: ${(counted / 1024).toFixed(2)} KB gzip of 40[.]00 KB, ok$`));
    assert.equal(every.ok, true);
    const sum = gzipSize(small('r')) + gzipSize(feature) + gzipSize(english) + gzipSize(german);
    assert.ok(sum > 40 * 1024, 'every language together is over the budget, which no page loads');
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
