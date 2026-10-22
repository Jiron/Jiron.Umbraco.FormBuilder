// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/check-text.mjs. Run: node --test "scripts/tests/*.test.mjs"
// Every unusual character here is built at runtime with String.fromCharCode, so this file stays ASCII.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { checkContent, mustBeAscii, run } from '../check-text.mjs';

const char = (code) => String.fromCharCode(code);
const FORM_FEED = char(0x0c);
const BOM = char(0xfeff);
const A_UMLAUT = char(0xe4);
const CURLY_APOSTROPHE = char(0x2019);

const reasons = (problems) => problems.map((problem) => problem.reason);

describe('which files must stay ASCII', () => {
  test('code outside docs/ must stay ASCII', () => {
    for (const path of ['src/Jiron.Umbraco.FormBuilder.Core/Forms/FormService.cs', 'scripts/fix.mjs', 'scripts/ci-local.ps1', 'demo/Views/Home.cshtml', 'tests/e2e/support/host.ts']) {
      assert.equal(mustBeAscii(path), true, path);
    }
  });

  test('every client source must stay ASCII, whatever its type, except the language tables', () => {
    assert.equal(mustBeAscii('src/Jiron.Umbraco.FormBuilder.Web/Client/packages/runtime/src/index.ts'), true);
    assert.equal(mustBeAscii('src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/public/umbraco-package.json'), true);
    assert.equal(mustBeAscii('src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/src/lang/de.ts'), false);
    assert.equal(mustBeAscii('src/Jiron.Umbraco.FormBuilder.Web/Client/packages/runtime/src/i18n/fr.ts'), false);
  });

  test('docs, Markdown and data files may hold non-ASCII text', () => {
    for (const path of ['README.md', 'LICENSE.md', 'docs/ux/wireframes/designer.html', 'Directory.Build.props', 'tests/vectors/rules.json', 'src/Jiron.Umbraco.FormBuilder.Core/Texts.de.resx']) {
      assert.equal(mustBeAscii(path), false, path);
    }
  });
});

describe('checkContent', () => {
  test('a form feed is a control character, with its line and column', () => {
    const problems = checkContent('docs/notes.md', Buffer.from(`one\ntwo${FORM_FEED}three\n`));
    assert.deepEqual(
      problems.map((p) => [p.line, p.column, p.codePoint, p.reason]),
      [[2, 4, 0x0c, 'a control character']],
    );
  });

  test('tab, LF and CR are allowed', () => {
    assert.deepEqual(checkContent('scripts/a.ps1', Buffer.from('a\tb\r\nc\n')), []);
  });

  test('DEL and C1 controls are control characters', () => {
    assert.deepEqual(reasons(checkContent('docs/a.md', Buffer.from(`a${char(0x7f)}b${char(0x85)}c`))), ['a control character', 'a control character']);
  });

  test('a byte-order mark is refused at the start and in the middle', () => {
    assert.deepEqual(reasons(checkContent('README.md', Buffer.from(`${BOM}# Title\n`))), ['a byte-order mark']);
    assert.deepEqual(reasons(checkContent('README.md', Buffer.from(`# Ti${BOM}tle\n`))), ['a byte-order mark']);
  });

  test('a replacement character and invalid UTF-8 are refused', () => {
    assert.deepEqual(reasons(checkContent('docs/a.md', Buffer.from(`bad ${char(0xfffd)} text`))), ['a replacement character (the text was converted badly)']);
    assert.deepEqual(reasons(checkContent('docs/a.md', Buffer.from([0x61, 0xc3, 0x28]))), ['not valid UTF-8']);
  });

  test('a curly apostrophe that replaced an escape in code is refused, but not in Markdown', () => {
    assert.deepEqual(reasons(checkContent('src/A/Thing.cs', Buffer.from(`var s = "it${CURLY_APOSTROPHE}s";`))), ['a non-ASCII character in code']);
    assert.deepEqual(checkContent('docs/guide.md', Buffer.from(`it${CURLY_APOSTROPHE}s`)), []);
  });

  test('a name with an umlaut is fine in Markdown and props, but not in a script', () => {
    const name = `Z${A_UMLAUT}hnler`;
    assert.deepEqual(checkContent('LICENSE.md', Buffer.from(name)), []);
    assert.deepEqual(checkContent('Directory.Build.props', Buffer.from(name)), []);
    assert.equal(checkContent('scripts/x.mjs', Buffer.from(name)).length, 1);
  });

  test('the output is capped per file', () => {
    assert.equal(checkContent('docs/a.md', Buffer.from(FORM_FEED.repeat(100))).length, 20);
  });
});

describe('git modes (temporary repository)', () => {
  const repo = mkdtempSync(join(tmpdir(), 'jfb-text-'));
  const git = (...args) => execFileSync('git', args, { cwd: repo, stdio: ['ignore', 'pipe', 'pipe'] }).toString();
  git('init', '-q', '-b', 'main');
  git('config', 'user.name', 'Text Test');
  git('config', 'user.email', 'text@example.test');
  git('config', 'core.autocrlf', 'false');
  git('config', 'core.hooksPath', '.no-hooks');
  writeFileSync(join(repo, '.gitattributes'), '* text=auto eol=lf\n*.png binary\n');
  writeFileSync(join(repo, 'README.md'), `# Made by Z${A_UMLAUT}hnler\n`);
  git('add', '.gitattributes', 'README.md');
  git('commit', '-q', '-m', 'start');
  after(() => rmSync(repo, { recursive: true, force: true }));

  test('a clean repository passes in both modes', () => {
    assert.equal(run({ mode: 'tracked', cwd: repo }).exitCode, 0);
    assert.equal(run({ mode: 'staged', cwd: repo }).exitCode, 0);
  });

  test('a staged file with a form feed fails and is named', () => {
    writeFileSync(join(repo, 'broken.txt'), `a${FORM_FEED}b\n`);
    git('add', 'broken.txt');
    const result = run({ mode: 'staged', cwd: repo });
    assert.equal(result.exitCode, 1);
    assert.match(result.output.join('\n'), /broken\.txt:1:2 {2}U\+000C a control character/);
    git('rm', '-q', '--cached', 'broken.txt');
    unlinkSync(join(repo, 'broken.txt'));
  });

  test('binary files are skipped, by attribute and by content', () => {
    writeFileSync(join(repo, 'image.png'), Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0c, 0x7f]));
    writeFileSync(join(repo, 'data.bin'), Buffer.from([0x00, 0x0c, 0x7f]));
    git('add', 'image.png', 'data.bin');
    const result = run({ mode: 'staged', cwd: repo });
    assert.equal(result.exitCode, 0, result.output.join('\n'));
    assert.match(result.output[0], /0 text files/);
    git('rm', '-q', '--cached', 'image.png', 'data.bin');
    unlinkSync(join(repo, 'image.png'));
    unlinkSync(join(repo, 'data.bin'));
  });
});
