import { test } from 'node:test';
import assert from 'node:assert/strict';
import { join } from 'node:path';
import {
  COMBOS,
  comboName,
  foldsInFolds,
  sheetHtml,
  splitAxe,
  walkDir,
  wantedCombo,
  wantedScreen,
  WINDOWS,
  wordChanges,
  type FoldNode,
} from './ux-walk.ts';

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

test('a walk looks every way, or with JFB_UX_LOOKS in the ways its looks name', () => {
  const walked = (env: NodeJS.ProcessEnv) => COMBOS.filter((combo) => wantedCombo(combo, env)).map(comboName);
  assert.equal(walked({}).length, 12);
  assert.equal(walked({ JFB_UX_LOOKS: ' , ' }).length, 12);
  assert.deepEqual(walked({ JFB_UX_LOOKS: 'de' }), [
    '375-light-de',
    '375-dark-de',
    '768-light-de',
    '768-dark-de',
    '1440-light-de',
    '1440-dark-de',
  ]);
  assert.deepEqual(walked({ JFB_UX_LOOKS: 'light-de, 1440-dark-en' }), [
    '375-light-de',
    '768-light-de',
    '1440-dark-en',
    '1440-light-de',
  ]);
  assert.deepEqual(walked({ JFB_UX_LOOKS: 'fr' }), [], 'a look no combination has walks nothing');
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

test("the name and the colours of UUI's menu item are Umbraco's in the sidebar's places too, and no other rule", () => {
  // Axe gives a node in shadow roots as the hosts down to it; the ancestry says the same with each step's place.
  const inItem = (label: string, part: string) => [
    ['umb-app', 'jfb-places', `uui-menu-item[label="${label}"][active=""]`, part],
  ];
  const ancestry = [['umb-app > umb-section-default', 'jfb-places', 'uui-menu-item:nth-child(2)', '#menu-item']];
  const split = splitAxe([
    { rule: 'label-content-name-mismatch', target: inItem('Library', '#menu-item'), ancestry },
    { rule: 'color-contrast', target: inItem('Inbox, 73 unseen', '.label:nth-child(2)'), ancestry },
    // The Inbox's name, given to the item's label slot, which UUI colours as its own label.
    {
      rule: 'color-contrast',
      target: [['umb-app', 'jfb-places', 'span[slot="label"]']],
      ancestry: [['umb-app > umb-section-default', 'jfb-places', 'nav > uui-menu-item:nth-child(1) > span']],
    },
    {
      rule: 'color-contrast',
      target: [['umb-app', 'jfb-places', 'nav']],
      ancestry: [['umb-app', 'jfb-places', 'nav']],
    },
    { rule: 'label', target: [['umb-app', 'jfb-place-tabs', 'uui-tab']], ancestry: [['umb-app', 'jfb-place-tabs']] },
    // A place we gave no name: the link inside the menu item has none, and that is ours.
    { rule: 'link-name', target: inItem('', '#label-button'), ancestry },
    { rule: 'label-content-name-mismatch', target: inItem('Library', '#label-button'), ancestry },
  ]);
  assert.deepEqual(split.ours, [
    'color-contrast: [["umb-app","jfb-places","nav"]]',
    'label: [["umb-app","jfb-place-tabs","uui-tab"]]',
    `link-name: ${JSON.stringify(inItem('', '#label-button'))}`,
    `label-content-name-mismatch: ${JSON.stringify(inItem('Library', '#label-button'))}`,
  ]);
  assert.equal(split.umbraco, 3);
});

test('a fold inside a region another fold opened is found by both names; a menu, a combobox, a tree are none', () => {
  const node = (tag: string, extra: Partial<FoldNode> = {}, ...children: FoldNode[]): FoldNode => ({
    tag,
    ...extra,
    children,
  });
  // A section open with a "More options" inside it, and a details open inside "More options"'s region.
  const nested = node(
    'body',
    {},
    node('button', { name: 'Validation', expanded: 'true', controls: ['1:validation'] }),
    node(
      'div',
      { id: '1:validation' },
      node('button', { name: 'More options', expanded: 'false', controls: ['1:more'] }),
      node('button', { name: 'Fewer', expanded: 'true', controls: ['1:fewer'] }),
      node('div', { id: '1:fewer' }, node('details', { open: true }, node('summary', { name: 'Add recipient' }))),
    ),
  );
  assert.deepEqual(foldsInFolds(nested), [
    '"More options" inside "Validation"',
    '"Fewer" inside "Validation"',
    '"Add recipient" inside "Validation"',
  ]);

  // Side by side, and closed, nothing is inside anything; a closed fold's region opens nothing.
  const sideBySide = node(
    'body',
    {},
    node('button', { name: 'Validation', expanded: 'true', controls: ['1:a'] }),
    node('div', { id: '1:a' }, node('input', { id: '1:min' })),
    node('button', { name: 'Advanced', expanded: 'false', controls: ['1:b'] }),
    node('div', { id: '1:b' }, node('button', { name: 'Inner', expanded: 'false', controls: ['1:c'] })),
  );
  assert.deepEqual(foldsInFolds(sideBySide), []);

  // An id names the element of its own shadow root: the same id in another root is not the region.
  const otherRoot = node(
    'body',
    {},
    node('button', { name: 'Open', expanded: 'true', controls: ['1:part'] }),
    node('div', { id: '2:part' }, node('button', { name: 'Inner', expanded: 'false', controls: ['2:x'] })),
  );
  assert.deepEqual(foldsInFolds(otherRoot), []);

  // Inside an open region: a menu button, a combobox, a popover's opener, one whose region is a list box, and a tree's
  // items are no folds.
  const popups = node(
    'body',
    {},
    node('button', { name: 'Section', expanded: 'true', controls: ['1:s'] }),
    node(
      'div',
      { id: '1:s' },
      node('button', { name: 'Actions', expanded: 'false', haspopup: 'menu' }),
      node('input', { role: 'combobox', name: 'Country', expanded: 'false' }),
      node('button', { name: 'Language', expanded: 'false', popovertarget: true }),
      node('button', { name: 'Pick', expanded: 'true', controls: ['1:list'] }),
      node('ul', { id: '1:list', role: 'listbox' }),
      node(
        'uui-menu-item',
        { name: 'Folder' },
        node('button', { name: 'Expand', expanded: 'true', controls: ['1:t'] }),
      ),
      node('div', { role: 'tree' }, node('div', { role: 'treeitem', expanded: 'false', name: 'Child' })),
      // A tool that opens a picker under its box: Insert merge tag.
      node(
        'jfb-merge-tag-picker',
        {},
        node('button', { name: 'Insert merge tag', expanded: 'true', controls: ['2:tags'] }),
        node('div', { id: '2:tags', role: 'group' }, node('button', { name: 'Inner', expanded: 'false' })),
      ),
    ),
  );
  assert.deepEqual(foldsInFolds(popups), []);

  // A details open inside a details.
  const details = node(
    'body',
    {},
    node(
      'details',
      { open: true },
      node('summary', { name: 'Outer' }),
      node('details', { open: false }, node('summary', { name: 'Inner' })),
    ),
  );
  assert.deepEqual(foldsInFolds(details), ['"Inner" inside "Outer"']);
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

test('two walks are compared screen by screen in the looks both have, what grew most first', () => {
  const before = {
    settings: { '1440-light-en': { words: 120 }, '1440-light-de': { words: 130 }, '375-light-en': { words: 120 } },
    access: { '1440-light-en': { words: 80 } },
    inbox: { '1440-light-en': { words: 40 } },
    people: { '1440-light-en': { error: 'not reached' } },
    old: { '1440-light-en': {} },
  };
  const after = {
    settings: { '1440-light-en': { words: 90 }, '1440-light-de': { words: 101 } },
    access: { '1440-light-en': { words: 86 } },
    inbox: { '1440-light-en': { words: 40 } },
    people: { '1440-light-en': { words: 12 } },
    old: { '1440-light-en': { words: 7 } },
    fresh: { '1440-light-en': { words: 5 } },
  };
  assert.deepEqual(wordChanges(before, after), [
    { screen: 'access', look: '1440-light-en', before: 80, after: 86 },
    { screen: 'inbox', look: '1440-light-en', before: 40, after: 40 },
    { screen: 'settings', look: '1440-light-de', before: 130, after: 101 },
    { screen: 'settings', look: '1440-light-en', before: 120, after: 90 },
  ]);
});
