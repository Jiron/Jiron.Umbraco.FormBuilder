// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Tests for scripts/wireframes.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { pathToFileURL } from 'node:url';
import { judge, listPages, measure, parseArgs, parseShots, plannedShots, shotName, tokenNames } from '../wireframes.mjs';

const clean = {
  layers: [{ name: 'main.wf-main', primaries: ['Publish\u2026'] }],
  groups: [{ name: 'toolbar', count: 3, items: ['search', 'Filter', 'Export\u2026'] }],
  dashes: [],
  capitals: [],
  cutButtons: [],
  pageOverflow: 0,
  overflows: [],
  offTokens: { colour: [], radius: [], 'font size': [], shadow: [] },
  offGrid: [],
  missingIcons: [],
};

describe('parseArgs', () => {
  test('reads --check and one filter', () => {
    assert.deepEqual(parseArgs([]), { check: false, filter: '', widths: [] });
    assert.deepEqual(parseArgs(['--check', 'designer']), { check: true, filter: 'designer', widths: [] });
  });

  test('reads --widths, which implies --check', () => {
    assert.deepEqual(parseArgs(['--widths=1024,1920', 'inbox']), { check: true, filter: 'inbox', widths: [1024, 1920] });
  });

  test('refuses unknown options, a second filter and bad widths', () => {
    assert.match(parseArgs(['--fast']).error, /unknown option/);
    assert.match(parseArgs(['a', 'b']).error, /one filter/);
    assert.match(parseArgs(['--widths=wide']).error, /bad widths/);
    assert.match(parseArgs(['--widths=1024,']).error, /bad widths/);
  });
});

describe('plannedShots', () => {
  const declared = [
    { state: 'default', width: 1440, height: 900 },
    { state: 'empty', width: 1440, height: 900 },
    { state: 'default', width: 920, height: 900 },
  ];

  test('keeps the declared shots without widths', () => {
    assert.equal(plannedShots(declared, []), declared);
  });

  test('takes every state once per width, 1080 px high from 1920 px', () => {
    assert.deepEqual(plannedShots(declared, [1024, 1920]), [
      { state: 'default', width: 1024, height: 900 },
      { state: 'empty', width: 1024, height: 900 },
      { state: 'default', width: 1920, height: 1080 },
      { state: 'empty', width: 1920, height: 1080 },
    ]);
  });
});

describe('parseShots', () => {
  test('defaults to one shot at 1440 by 900', () => {
    assert.deepEqual(parseShots('<html></html>'), [{ state: 'default', width: 1440, height: 900 }]);
  });

  test('reads every shot of the meta tag', () => {
    const html = '<meta name="wf-shots" content="default@1440x900  empty@1440x900 default@920x900">';
    assert.deepEqual(parseShots(html), [
      { state: 'default', width: 1440, height: 900 },
      { state: 'empty', width: 1440, height: 900 },
      { state: 'default', width: 920, height: 900 },
    ]);
  });

  test('refuses a malformed shot', () => {
    assert.throws(() => parseShots('<meta name="wf-shots" content="default@wide">'), /bad shot/);
  });
});

describe('shotName', () => {
  test('leaves out the default state and the default width', () => {
    assert.equal(shotName('inbox', { state: 'default', width: 1440 }), 'inbox.png');
    assert.equal(shotName('inbox', { state: 'empty', width: 1440 }), 'inbox--empty.png');
    assert.equal(shotName('designer', { state: 'default', width: 920 }), 'designer--920.png');
    assert.equal(shotName('designer', { state: 'phone', width: 920 }), 'designer--phone--920.png');
  });
});

describe('tokenNames', () => {
  test('lists the custom properties of every :root block and nothing else', () => {
    const css = ':root { --a: 1px; /* note */ --b-c:#fff; }\n.x { --not-a-token: 2px; }\n:root{--d : red}';
    assert.deepEqual(tokenNames(css), ['--a', '--b-c', '--d']);
  });
});

describe('listPages', () => {
  test('lists the HTML pages by name, filtered', () => {
    const dir = mkdtempSync(join(tmpdir(), 'jfb-wireframes-test-'));
    try {
      for (const f of ['b.html', 'a.html', 'README.md', 'ab-designer.html']) writeFileSync(join(dir, f), '');
      assert.deepEqual(listPages(dir), ['a', 'ab-designer', 'b']);
      assert.deepEqual(listPages(dir, 'designer'), ['ab-designer']);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});

describe('judge', () => {
  test('finds nothing in a clean measurement', () => {
    assert.deepEqual(judge(clean), []);
  });

  test('reports each broken rule', () => {
    const findings = judge(
      {
        ...clean,
        layers: [{ name: 'aside.wf-sidebar', primaries: ['Save', 'Publish'] }],
        groups: [{ name: 'toolbar', count: 4, items: ['a', 'b', 'c', 'd'] }],
        dashes: ['Contact us - Form'],
        capitals: ['FORMS'],
        cutButtons: ['Create a form block type\u2026'],
        pageOverflow: 12,
        overflows: ['div.wf-box "Translators" by 40 px'],
        offTokens: { colour: ['rgb(1, 2, 3) (div)'], radius: ['8px (div)'], 'font size': ['21px (h2)'], shadow: ['x (div)'] },
        offGrid: ['padding-top 10px (div)'],
        missingIcons: ['#nope'],
      },
      [{ id: 'color-contrast', help: 'Elements must meet minimum color contrast ratio thresholds', nodes: 2 }],
    );
    assert.equal(findings.length, 14);
    assert.match(findings[0], /2 primary buttons/);
    assert.match(findings[1], /4 visible secondary actions/);
    assert.match(findings.at(-1), /axe color-contrast.*2 elements/);
  });
});

// One real render: the measurement on a small page, in Edge, as the script runs it.
describe('measure, in the browser', () => {
  test('counts primaries per layer, secondary actions per group, dashes, overflows and off-token values', async () => {
    const require = createRequire(import.meta.url);
    const { chromium } = require('playwright-core');
    const dir = mkdtempSync(join(tmpdir(), 'jfb-wireframes-test-'));
    const dash = String.fromCharCode(0x2014);
    writeFileSync(join(dir, 'page.html'), `<!doctype html><html><head><style>
      :root { --wf-type-body: 14px; --c-ink: #060606; --c-paper: #ffffff; }
      body { margin: 0; font-size: 14px; color: #060606; background: #ffffff; }
      button { all: unset; display: inline-block; width: 80px; height: 30px; }
      .off { color: rgb(1, 2, 3); padding: 10px; }
      .ring { box-shadow: 0 0 0 1px #060606; }
      .shade { box-shadow: 0 2px 4px #060606; }
      .wf-box { width: 200px; }
    </style></head><body>
      <main class="wf-main">
        <div class="wf-toolbar"><button class="wf-btn">A</button><button class="wf-btn">B</button><button class="wf-btn">C</button><button class="wf-btn">D</button><button class="wf-btn" data-wf-more>more</button></div>
        <button class="wf-btn wf-btn--primary">Save</button><button class="wf-btn wf-btn--primary">Publish</button>
        <p class="off">One ${dash} two</p>
        <p class="ring">ring</p><p class="shade">shade</p>
        <div class="wf-box"><div style="width: 260px">wide</div></div>
        <div data-wf-sample><p style="color: rgb(9, 8, 7); padding: 5px">a visitor's theme</p></div>
      </main></body></html>`);
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
      const page = await browser.newPage({ viewport: { width: 800, height: 600 } });
      await page.goto(pathToFileURL(join(dir, 'page.html')).href);
      const m = await page.evaluate(measure, ['--wf-type-body', '--c-ink', '--c-paper']);
      assert.deepEqual(m.layers, [{ name: 'main.wf-main', primaries: ['Save', 'Publish'] }]);
      assert.equal(m.groups[0].count, 4);
      assert.deepEqual(m.dashes, [`One ${dash} two`]);
      assert.deepEqual(m.overflows, ['div.wf-box "wide" by 60 px']);
      assert.ok(m.offTokens.colour.some((v) => v.startsWith('rgb(1, 2, 3)')));
      assert.ok(m.offGrid.some((v) => v.startsWith('padding-top 10px')));
      assert.equal(m.offTokens.shadow.length, 1, 'a ring is a border drawn as a shadow; only the blurred shade is off');
      assert.match(m.offTokens.shadow[0], /p\.shade/);
      assert.ok(!m.offTokens.colour.some((v) => v.startsWith('rgb(9, 8, 7)')), 'samples are left out of the token check');
      assert.ok(!m.offGrid.some((v) => v.startsWith('padding-top 5px')));
    } finally {
      await browser.close();
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
