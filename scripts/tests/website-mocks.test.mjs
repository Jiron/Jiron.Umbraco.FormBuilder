// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Tests for scripts/website-mocks.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { gzipSync } from 'node:zlib';
import { pathToFileURL } from 'node:url';
import {
  contrast,
  gzipSize,
  isLargeText,
  judge,
  lintStylesheet,
  listPages,
  measure,
  parseArgs,
  parseStates,
  plannedShots,
  shotName,
  stylesheetFindings,
  tokenNames,
} from '../website-mocks.mjs';

const clean = {
  texts: [],
  controls: [],
  focus: [],
  targets: [],
  offTokens: { colour: [], corner: [], 'font size': [], shade: [] },
  offGrid: [],
  dashes: [],
  wrapped: [],
  pageOverflow: 0,
  overflows: [],
};

describe('parseArgs', () => {
  test('reads --check and one filter', () => {
    assert.deepEqual(parseArgs([]), { check: false, filter: '', widths: [] });
    assert.deepEqual(parseArgs(['--check', 'book']), { check: true, filter: 'book', widths: [] });
  });

  test('reads a width sweep, which implies --check', () => {
    assert.deepEqual(parseArgs(['--widths=768', 'fields']), { check: true, filter: 'fields', widths: [768] });
    assert.deepEqual(parseArgs(['--widths=375,768,1440']).widths, [375, 768, 1440]);
  });

  test('refuses unknown options, a second filter and bad widths', () => {
    assert.match(parseArgs(['--fast']).error, /unknown option/);
    assert.match(parseArgs(['a', 'b']).error, /one filter/);
    assert.match(parseArgs(['--widths=tablet']).error, /bad widths/);
    assert.match(parseArgs(['--widths=768,']).error, /bad widths/);
    assert.match(parseArgs(['--widths=100']).error, /bad widths/);
  });
});

describe('parseStates', () => {
  test('falls back when the page names no states', () => {
    assert.deepEqual(parseStates('<html></html>'), ['default']);
    assert.deepEqual(parseStates('<html></html>', 'jfb-forced', []), []);
  });

  test('reads the states of the named meta tag', () => {
    const html = '<meta name="jfb-shots" content="default  errors focus"><meta name="jfb-forced" content="default">';
    assert.deepEqual(parseStates(html), ['default', 'errors', 'focus']);
    assert.deepEqual(parseStates(html, 'jfb-forced', []), ['default']);
  });

  test('refuses a state that could not be a query value', () => {
    assert.throws(() => parseStates('<meta name="jfb-shots" content="default Errors">'), /bad state/);
    assert.throws(() => parseStates('<meta name="jfb-shots" content="a&b">'), /bad state/);
  });
});

describe('plannedShots', () => {
  test('a width sweep takes each state at those widths in both designs, without forced colours', () => {
    assert.deepEqual(plannedShots(['default', 'errors'], ['default'], [768]), [
      { state: 'default', width: 768, scheme: 'light', forced: false },
      { state: 'default', width: 768, scheme: 'dark', forced: false },
      { state: 'errors', width: 768, scheme: 'light', forced: false },
      { state: 'errors', width: 768, scheme: 'dark', forced: false },
    ]);
  });

  test('takes each state at each width in both designs, then the forced-colours shots', () => {
    const shots = plannedShots(['default', 'errors'], ['default']);
    assert.equal(shots.length, 2 * 3 * 2 + 1);
    assert.deepEqual(shots.slice(0, 6), [
      { state: 'default', width: 375, scheme: 'light', forced: false },
      { state: 'default', width: 375, scheme: 'dark', forced: false },
      { state: 'default', width: 768, scheme: 'light', forced: false },
      { state: 'default', width: 768, scheme: 'dark', forced: false },
      { state: 'default', width: 1440, scheme: 'light', forced: false },
      { state: 'default', width: 1440, scheme: 'dark', forced: false },
    ]);
    assert.deepEqual(shots.at(-1), { state: 'default', width: 1440, scheme: 'light', forced: true });
  });
});

describe('shotName', () => {
  test('leaves out the default state and the light design', () => {
    assert.equal(shotName('book-details', { state: 'default', width: 1440, scheme: 'light', forced: false }), 'book-details--1440.png');
    assert.equal(shotName('book-details', { state: 'errors', width: 375, scheme: 'dark', forced: false }), 'book-details--errors--375--dark.png');
    assert.equal(shotName('fields', { state: 'default', width: 1440, scheme: 'light', forced: true }), 'fields--1440--forced.png');
  });
});

describe('listPages', () => {
  test('lists the HTML pages by name, filtered', () => {
    const dir = mkdtempSync(join(tmpdir(), 'jfb-website-mocks-test-'));
    try {
      for (const f of ['book-sent.html', 'apply-room.html', 'README.md', 'book-check.html']) writeFileSync(join(dir, f), '');
      assert.deepEqual(listPages(dir), ['apply-room', 'book-check', 'book-sent']);
      assert.deepEqual(listPages(dir, 'book'), ['book-check', 'book-sent']);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});

describe('tokenNames', () => {
  test('lists the private properties the theme declares, once each', () => {
    const css = '.jfb-form { --_ink: var(--jfb-color-text, #000); --_s1:4px; color: var(--_ink); }\n.x { --_ink: red; --public: 1px; }';
    assert.deepEqual(tokenNames(css), ['--_ink', '--_s1']);
  });
});

describe('contrast and isLargeText', () => {
  test('computes the WCAG ratio', () => {
    assert.equal(contrast([0, 0, 0], [255, 255, 255]), 21);
    assert.equal(contrast([255, 255, 255], [255, 255, 255]), 1);
    assert.equal(Math.floor(contrast([0x76, 0x76, 0x76], [255, 255, 255]) * 100) / 100, 4.54);
  });

  test('treats 24 px, or 18.66 px in bold, as large', () => {
    assert.equal(isLargeText(24, 400), true);
    assert.equal(isLargeText(18.66, 700), true);
    assert.equal(isLargeText(18.66, 600), false);
    assert.equal(isLargeText(18, 700), false);
  });
});

describe('lintStylesheet', () => {
  test('finds selectors outside .jfb-form, also inside at-rules', () => {
    const css = '.jfb-form a { color: red }\nbody, .jfb-form p { margin: 0 }\n.jfb-formx { color: red }\n@media (min-width: 1px) { .x { color: red } .jfb-form .y { color: red } }';
    assert.deepEqual(lintStylesheet(css).unscoped, ['body', '.jfb-formx', '.x']);
  });

  test('finds physical left and right, and four-value shorthands whose sides differ', () => {
    const css = `.jfb-form a {
      margin-left: 1px; float: left; text-align: right; padding: 0 1px 0 2px; border-radius: 1px 2px;
      inset-inline-start: 0; float: inline-start; padding: 1px 2px 3px; margin: 1px 2px 3px 2px; border-radius: 4px; --x-left: 1px;
    }`;
    assert.deepEqual(lintStylesheet(css).physical, ['margin-left: 1px', 'float: left', 'text-align: right', 'padding: 0 1px 0 2px', 'border-radius: 1px 2px']);
  });

  test('reads a nested @starting-style as part of its rule', () => {
    const css = '.jfb-form .e { opacity: 1; @starting-style { opacity: 0; translate: 0 -4px; } }\n.jfb-form .f { color: red }';
    assert.deepEqual(lintStylesheet(css), { unscoped: [], physical: [] });
  });
});

describe('gzipSize and stylesheetFindings', () => {
  test('measures the stylesheet without its comments and spare whitespace', () => {
    const css = `/* ${'a long comment '.repeat(200)} */\n.jfb-form   a  {\n  color :  red ;\n}\n`;
    assert.equal(gzipSize(css), gzipSync(Buffer.from('.jfb-form a{color:red}')).length);
  });

  test('reports lint findings and a theme over the budget', () => {
    const big = `.jfb-form a { margin-left: 1px }\nbody { color: red }\n${Array.from({ length: 4000 }, (_, i) => `.jfb-form .c${i}{--v:${(i * 7919) % 99991}}`).join('\n')}`;
    const { findings, total } = stylesheetFindings([['assets/theme.css', big]]);
    assert.ok(total > 10240);
    assert.ok(findings.some((f) => /body/.test(f)));
    assert.ok(findings.some((f) => /margin-left/.test(f)));
    assert.ok(findings.some((f) => /over the 10240 byte budget/.test(f)));
    assert.deepEqual(stylesheetFindings([['assets/theme.css', '.jfb-form a { color: red }']]).findings, []);
  });
});

describe('judge', () => {
  test('finds nothing in a clean measurement', () => {
    assert.deepEqual(judge(clean), []);
  });

  test('reports each broken rule', () => {
    const findings = judge(
      {
        texts: [{ ratio: 2.1, needs: 4.5, text: 'Faint', where: 'p' }],
        controls: [{ ratio: 1.4, what: 'boundary', where: 'input#a' }],
        focus: ['no ring on button'],
        targets: ['button "T" 30 by 30'],
        offTokens: { colour: ['rgb(1, 2, 3) (p)'], corner: ['5px (div)'], 'font size': ['13px (p)'], shade: ['x (div)'] },
        offGrid: ['padding-top 10px (p)'],
        dashes: ['One - two'],
        wrapped: ['Two words'],
        pageOverflow: 12,
        overflows: ['div.jfb-field "wide" by 60 px'],
      },
      [{ id: 'label', help: 'Form elements must have labels', nodes: 1 }],
    );
    assert.equal(findings.length, 14);
    assert.match(findings[0], /text contrast 2\.1:1 \(needs 4\.5:1\)/);
    assert.match(findings[1], /control contrast 1\.4:1/);
    assert.ok(findings.includes('button label wraps: "Two words"'));
    assert.ok(findings.includes('the page scrolls sideways by 12 px'));
    assert.match(findings.at(-1), /axe label: .*\(1 element\)/);
  });
});

// One real render: the measurement on a small broken form, in Edge, as the script runs it.
describe('measure, in the browser', () => {
  test('finds weak contrast, missing rings, small targets, off-token values, dashes, wraps and overflows', async () => {
    const require = createRequire(import.meta.url);
    const { chromium } = require('playwright-core');
    const dir = mkdtempSync(join(tmpdir(), 'jfb-website-mocks-test-'));
    const dash = String.fromCharCode(0x2014);
    writeFileSync(join(dir, 'page.html'), `<!doctype html><html lang="en"><head><style>
      body { margin: 0; font: 16px / 1.5 sans-serif; color: #1d2433; background: #ffffff; }
      .jfb-form { --_ink: #1d2433; --_surface: #ffffff; --_border: #767c88; --_focus: #1a5fd1; display: grid; gap: 8px; padding: 16px; color: var(--_ink); }
      .jfb-form :focus { outline: 2px solid var(--_focus); outline-offset: 2px; }
      .jfb-form .jfb-button { all: unset; display: inline-block; box-sizing: border-box; min-width: 48px; min-height: 48px; padding: 8px; border: 1px solid var(--_border); }
      .jfb-form .jfb-button:focus { outline: 2px solid var(--_focus); }
      .jfb-form .jfb-button-icon { border-color: transparent; }
      .jfb-form .jfb-visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
      .jfb-form .faint { color: #b0b0b0; }
      .jfb-form .weak { box-sizing: border-box; width: 200px; height: 48px; border: 1px solid #dddddd; background: #ffffff; color: var(--_ink); font: inherit; }
      .jfb-form .jfb-button.no-ring:focus { outline: none; }
      .jfb-form .jfb-button.tiny { min-width: 0; min-height: 0; width: 30px; height: 30px; padding: 0; }
      .jfb-form .off { color: rgb(1, 2, 3); padding: 10px; }
      .jfb-form .jfb-button.narrow { width: 60px; }
      .jfb-form .ghost { box-sizing: border-box; width: 48px; height: 48px; opacity: 0; color: rgb(4, 5, 6); }
      .jfb-form .pale:focus { outline: 2px solid #cccccc; }
      .jfb-form .jfb-field { width: 100px; }
    </style></head><body>
      <form class="jfb-form">
        <p class="faint">Faint text</p>
        <label for="weak">Weak</label><input id="weak" class="weak" value="typed">
        <button type="button" class="jfb-button no-ring">Plain</button>
        <button type="button" class="jfb-button tiny">T</button>
        <p class="off">One ${dash} two</p>
        <button type="button" class="jfb-button narrow">Two words</button>
        <button type="button" class="jfb-button jfb-button-icon"><span class="jfb-visually-hidden">Remove the first file from the list</span></button>
        <label for="ghost">Ghost</label><input id="ghost" class="ghost">
        <p>Read <a href="#" class="pale">this</a> first.</p>
        <div class="jfb-field"><div style="width: 160px">wide</div></div>
        <div class="jfb-upload"><button type="button" class="jfb-button">Choose files</button></div>
      </form></body></html>`);
    const browser = await chromium.launch({ channel: 'msedge' });
    try {
      const page = await browser.newPage({ viewport: { width: 800, height: 600 } });
      await page.goto(pathToFileURL(join(dir, 'page.html')).href);
      const m = await page.evaluate(measure, ['--_ink', '--_surface', '--_border', '--_focus']);
      assert.deepEqual(m.texts.map((t) => [t.text, t.needs, t.where]), [['Faint text', 4.5, 'p.faint']]);
      assert.deepEqual(m.controls.map((c) => [c.what, c.where]), [['boundary', 'input#weak']]);
      assert.deepEqual(m.focus, ['no ring on button.jfb-button', 'ring 1.6:1 on a.pale']);
      assert.equal(m.targets.length, 1);
      assert.match(m.targets[0], /^button\.jfb-button "T" 30 by 30$/);
      assert.ok(m.offTokens.colour.some((v) => v.startsWith('rgb(1, 2, 3) (p.off)')));
      assert.ok(!m.offTokens.colour.some((v) => v.startsWith('rgb(4, 5, 6)')), 'an unseen input has no colours to check');
      assert.ok(m.offGrid.includes('padding-top 10px (p.off)'));
      assert.ok(!m.offGrid.some((v) => v.includes('8px')));
      assert.deepEqual(m.dashes, [`One ${dash} two`]);
      assert.deepEqual(m.wrapped, ['Two words'], 'text for screen readers is not a wrapped label');
      assert.deepEqual(m.overflows, ['div.jfb-field "wide" by 60 px']);
      assert.equal(m.pageOverflow, 0);
    } finally {
      await browser.close();
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
