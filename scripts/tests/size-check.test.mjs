// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/size-check.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { after, describe, test } from 'node:test';
import { BUDGETS, check, gzipSize, measure, staticClosure } from '../size-check.mjs';

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
const chunk = (file, imports = [], dynamicImports = [], entry = false) => ({ file, entry, imports, dynamicImports });

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
          { meta: chunk('jfb-runtime.js', [], ['chunks/upload-x.js'], true), bytes: small('r') },
          { meta: chunk('chunks/upload-x.js'), bytes: upload },
        ],
      },
    });
    const results = check(paths);
    assert.equal(results.every((r) => r.ok), true);
    const core = results.find((r) => r.line.startsWith('runtime core'));
    const all = results.find((r) => r.line.startsWith('runtime with every feature'));
    assert.match(core.line, /[(]1 file: the entry/);
    assert.match(all.line, /[(]2 files[)]/);
    assert.equal(
      measure({ folder: 'WebsiteAssets/runtime' }, ['jfb-runtime.js', 'chunks/upload-x.js'], paths.webProject),
      gzipSize(small('r')) + gzipSize(upload),
    );
    assert.ok(results.some((r) => /^theme CSS: 10[.]00 KB gzip each, no theme is built yet/.test(r.line)));
    assert.ok(results.some((r) => /^element [(]1 file[)]: .* gzip, no budget/.test(r.line)));
    assert.ok(results.some((r) => /^backoffice bundle [(]1 file[)]: .* gzip, no budget yet [(]9[.]6/.test(r.line)));
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

  test('holds each theme to 10 KB once the themes are built', () => {
    const paths = project({
      ...base(),
      runtime: { folder: 'WebsiteAssets/runtime', files: [{ meta: chunk('jfb-runtime.js', [], [], true), bytes: small('r') }] },
      themes: { folder: 'WebsiteAssets/themes', files: [{ meta: chunk('default.css'), bytes: randomBytes(11 * 1024) }] },
    });
    const theme = check(paths).find((r) => r.line.startsWith('theme default.css'));
    assert.equal(theme.ok, false);
  });

  test('fails without a build, and names a file that is missing', () => {
    const empty = project({});
    assert.ok(check(empty).some((r) => !r.ok && /runtime: no chunk report/.test(r.line)));
    const paths = project({
      ...base(),
      runtime: { folder: 'WebsiteAssets/runtime', files: [{ meta: chunk('jfb-runtime.js', [], [], true), bytes: small('r') }] },
    });
    rmSync(join(paths.webProject, 'WebsiteAssets', 'runtime', 'jfb-runtime.js'));
    assert.throws(() => check(paths), /WebsiteAssets[/]runtime[/]jfb-runtime[.]js is missing/);
  });
});
