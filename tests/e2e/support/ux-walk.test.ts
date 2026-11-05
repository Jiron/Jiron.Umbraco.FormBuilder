import { test } from 'node:test';
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { COMBOS, comboName, sheetHtml, splitAxe, walkDir, wantedScreen, WINDOWS } from './ux-walk.ts';

test('the walk looks at every screen in twelve ways: three widths, two themes, two languages', () => {
  assert.equal(COMBOS.length, 12);
  assert.equal(new Set(COMBOS.map(comboName)).size, 12);
  assert.deepEqual(
    COMBOS.slice(0, 4).map(comboName),
    ['375-light-en', '375-dark-en', '375-light-de', '375-dark-de'],
    'widths first, so a sheet row is one width',
  );
  assert.deepEqual(
    [...new Set(COMBOS.map((combo) => combo.width))],
    WINDOWS.map((window) => window.width),
  );
});

test('a walk shoots every screen, or with JFB_UX_ONLY those its prefixes name', () => {
  assert.equal(wantedScreen('settings-more', {}), true);
  assert.equal(wantedScreen('settings-more', { JFB_UX_ONLY: 'settings' }), true);
  assert.equal(wantedScreen('design', { JFB_UX_ONLY: 'settings, publish' }), false);
  assert.equal(wantedScreen('publish-blocked', { JFB_UX_ONLY: 'settings, publish' }), true);
  assert.equal(wantedScreen('design', { JFB_UX_ONLY: ' , ' }), true);
});

test('a walk writes where JFB_UX_OUT says, or below .local/ux', () => {
  assert.equal(walkDir({ JFB_UX_OUT: 'C:/x' }), 'C:/x');
  assert.ok(walkDir({}).endsWith(join('.local', 'ux', 'latest')));
});

test("axe's findings are ours inside a jfb- element, and Umbraco's anywhere else", () => {
  const split = splitAxe([
    { rule: 'color-contrast', target: ['#a'], ancestry: [['umb-app > umb-backoffice', 'jfb-overview', 'uui-box']] },
    { rule: 'label', target: ['#b'], ancestry: ['html > body > umb-app > uui-input'] },
    { rule: 'region', target: ['#c'], ancestry: ['html > body > div.not-jfb-thing'] },
  ]);
  assert.deepEqual(split.ours, ['color-contrast: ["#a"]']);
  assert.equal(split.umbraco, 2);
});

test('a sheet has a cell for each of the twelve ways, saying why one has no shot, its names escaped', () => {
  const html = sheetHtml('<the bin>', [
    { combo: COMBOS[0]!, image: 'data:image/png;base64,AAAA' },
    { combo: COMBOS[5]!, image: null, note: 'the walk stopped: "x" & <y>' },
  ]);
  assert.equal((html.match(/<figure>/g) ?? []).length, 12);
  assert.equal((html.match(/<img /g) ?? []).length, 1);
  assert.match(html, /the walk stopped: &quot;x&quot; &amp; &lt;y&gt;/);
  assert.match(html, /<h1>&lt;the bin&gt;<\/h1>/);
  assert.equal((html.match(/no shot/g) ?? []).length, 10);
  // Rows are widths: the phone's four looks come first.
  const captions = [...html.matchAll(/<figcaption>([^<]+)<\/figcaption>/g)].map((match) => match[1]);
  assert.deepEqual(captions.slice(0, 4), ['375-light-en', '375-dark-en', '375-light-de', '375-dark-de']);
});
