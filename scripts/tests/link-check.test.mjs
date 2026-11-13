// The documentation's link check (ROADMAP 17.2a): anchors as GitHub makes them, every relative link and image found,
// links in code left alone, none relative in the NuGet readme, and the backoffice's "Learn more" map pointing at pages
// and headings that exist; the repository's own pages pass it.
// Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, test } from 'node:test';
import { anchorsOf, caseMismatch, checkFile, checkLearnMore, defaultFiles, linksOf, slug } from '../link-check.mjs';

const scratch = mkdtempSync(join(tmpdir(), 'jfb-link-check-'));
after(() => rmSync(scratch, { recursive: true, force: true }));

/** Writes files under a fresh folder in the scratch space and gives its path. */
function folder(files) {
  const base = mkdtempSync(join(scratch, 'case-'));
  for (const [name, text] of Object.entries(files)) {
    mkdirSync(join(base, name, '..'), { recursive: true });
    writeFileSync(join(base, name), text);
  }
  return base;
}

test("a heading's anchor is GitHub's: the text as shown, lower case, punctuation gone, spaces as hyphens", () => {
  assert.equal(
    slug('4.5 Database schema v1 (table prefix `jfb`; all times UTC)'),
    '45-database-schema-v1-table-prefix-jfb-all-times-utc',
  );
  assert.equal(slug('What do I have to pass on?'), 'what-do-i-have-to-pass-on');
  assert.equal(slug('Using `--jfb-*` properties'), 'using---jfb--properties');
  assert.equal(slug('**Bold** and [a link](x.md) & more'), 'bold-and-a-link--more');
  assert.equal(slug('\u00dcmlaut, Gr\u00f6\u00dfe'), '\u00fcmlaut-gr\u00f6\u00dfe');
  assert.equal(slug('snake_case stays'), 'snake_case-stays');
});

test('repeated headings are numbered, explicit ids count, and headings in code do not', () => {
  const anchors = anchorsOf('# Example\n## Example\n<a id="custom"></a>\n```\n# Not a heading\n```\n');
  assert.deepEqual([...anchors].sort(), ['custom', 'example', 'example-1']);
});

test('links are found inline, as definitions and in HTML, and never in code', () => {
  const links = linksOf(
    'See [a](a.md#b) and ![img](i.png "title").\n[ref]: r.md\n<a href="h.md">h</a> `[c](code.md)`\n```\n[d](fenced.md)\n```\n',
  );
  assert.deepEqual(
    links.map((link) => link.target),
    ['a.md#b', 'i.png', 'r.md', 'h.md'],
  );
});

test('a missing file, a missing heading or a missing image is named with its line; good and outside links pass', () => {
  const base = folder({
    'docs/page.md': '# Page\n\n## Part two\n',
    'docs/guide.md': [
      '# Guide',
      '[fine](page.md#part-two) [same page](#guide) [outside](https://example.com/x.md) [mail](mailto:a@b.c)',
      '[gone](missing.md)',
      '[no heading](page.md#part-three)',
      '![no image](shot.png)',
      '',
    ].join('\n'),
  });
  assert.deepEqual(checkFile(join(base, 'docs', 'guide.md'), base), [
    'docs/guide.md:3: no such file: missing.md',
    'docs/guide.md:4: no heading "#part-three" in docs/page.md',
    'docs/guide.md:5: no such file: shot.png',
  ]);
});

test('a link whose letter case differs from the file on disk is named, as GitHub would not find it', () => {
  const base = folder({
    'docs/views/default/Field.cshtml': '@* view *@\n',
    'docs/guide.md': '# Guide\n[right](views/default/Field.cshtml) [wrong](views/Default/Field.cshtml)\n',
  });
  assert.equal(caseMismatch(join(base, 'docs', 'views', 'default'), base), null);
  assert.equal(caseMismatch(join(base, 'docs', 'views', 'Default'), base), 'Default');
  assert.equal(caseMismatch(join(base, '..', 'elsewhere'), base), null);
  assert.equal(caseMismatch(join(base, 'docs', 'views', 'missing', 'x.md'), base), null);
  assert.deepEqual(checkFile(join(base, 'docs', 'guide.md'), base), [
    'docs/guide.md:2: "Default" is spelt with other letter case on disk: views/Default/Field.cshtml',
  ]);
});

test('a link with a stray % is named rather than ending the check', () => {
  const base = folder({ 'docs/guide.md': '# Guide\n[odd](100%25-sure.md) [broken](100%-sure.md)\n' });
  assert.deepEqual(checkFile(join(base, 'docs', 'guide.md'), base), [
    'docs/guide.md:2: no such file: 100%25-sure.md',
    'docs/guide.md:2: not a valid address (a stray %): 100%-sure.md',
  ]);
});

test('the NuGet readme may only link to whole addresses, which nuget.org can follow', () => {
  const base = folder({
    'docs/README.nuget.md': '# Package\n[docs](https://example.com/docs) [licence](../LICENSE.md)\n',
    'LICENSE.md': 'terms\n',
  });
  assert.deepEqual(checkFile(join(base, 'docs', 'README.nuget.md'), base), [
    "docs/README.nuget.md:2: a relative link in the NuGet readme, which nuget.org can't resolve: ../LICENSE.md",
  ]);
});

test("a package's own NuGet readme follows the same rule, and a page that only mentions one in its name does not", () => {
  const base = folder({
    'docs/README.nuget.ai.md': '# Add-on\n[set-up](ai.md)\n',
    'docs/ai.md': '# AI\n',
    'docs/README.nuget-notes.md': '# Notes\n[set-up](ai.md)\n',
  });
  assert.deepEqual(checkFile(join(base, 'docs', 'README.nuget.ai.md'), base), [
    "docs/README.nuget.ai.md:2: a relative link in the NuGet readme, which nuget.org can't resolve: ai.md",
  ]);
  assert.deepEqual(checkFile(join(base, 'docs', 'README.nuget-notes.md'), base), []);
});

test('a Learn more page that is missing, spelt in other letter case, or whose heading is, is named with its key', () => {
  const base = folder({
    'docs/privacy.md': '# Privacy\n\n## Retention\n',
    'map.json': JSON.stringify({
      base: 'https://example.com/docs/',
      pages: {
        retention: 'privacy.md#retention',
        erase: 'privacy.md#erasing',
        scanners: 'files.md#scanners',
        shouting: 'Privacy.md#retention',
      },
    }),
  });
  assert.deepEqual(checkLearnMore(join(base, 'map.json'), base), [
    'learn-more.json: "erase" names no heading "#erasing" in docs/privacy.md',
    'learn-more.json: "scanners" names no page under docs/: files.md#scanners',
    'learn-more.json: "shouting" spells "Privacy.md" with other letter case than the file on disk: Privacy.md#retention',
  ]);
});

test("the repository's pages and the backoffice's Learn more map have no broken link", () => {
  const files = defaultFiles();
  assert.ok(files.length > 20, 'the pages are found');
  assert.ok(files.some((file) => file.endsWith('README.md')));
  assert.deepEqual(files.flatMap((file) => checkFile(file)), []);
  assert.deepEqual(checkLearnMore(), []);
});
