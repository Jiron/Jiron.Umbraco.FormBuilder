#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Renders the backoffice wireframes of ROADMAP 2.2 (`docs/ux/wireframes/*.html`) to PNG with Playwright in Edge,
 * and checks them against the measurable rules of ROADMAP section 4.13 and the wireframe tokens.
 *
 *   node scripts/wireframes.mjs [filter]           render every shot into docs/ux/wireframes/png/
 *   node scripts/wireframes.mjs --check [filter]   render, then measure every shot and report the findings
 *   node scripts/wireframes.mjs --widths=1024,1920 [filter]
 *                                                  measure every state of every page at those widths, without
 *                                                  writing PNGs (the other backoffice widths of ROADMAP section 4.13)
 *
 * `filter` keeps only pages whose file name contains it. A page lists its shots in
 * `<meta name="wf-shots" content="default@1440x900 empty@1440x900 default@920x900">`; without it, the page has one
 * shot, `default@1440x900`. A shot loads the page with `?state=<state>` at that viewport.
 *
 * The check measures, per shot:
 * - at most one primary (or destructive confirm) button per layer: the workspace, each sidebar, each dialog,
 *   and each part marked `data-wf-layer`;
 * - at most three visible secondary actions in any toolbar, header, card, panel head, bulk bar or action bar
 *   ("..." menus, marked `data-wf-more`, don't count, and a segmented switch counts once);
 * - no em or en dash in visible text, no button whose label is cut off or wraps, no sideways scrolling, and no
 *   content that runs past its box, header row, bar, card, panel or dialog;
 * - the tokens: every colour, radius, font size and shadow is one of the custom properties on `:root` in
 *   `assets/kit.css`, every padding and gap is on the 3 px grid, and no text is set in capitals (no eyebrow
 *   labels). Parts marked `data-wf-sample` show a visitor's theme or an email layout, not the backoffice, so
 *   their colours, sizes and spacing are left out of the token check (axe still checks their contrast);
 * - every icon exists in the sprite;
 * - axe-core finds no WCAG 2.2 A or AA violation.
 *
 * Exit codes: 0 fine, 1 findings or a failure, 2 usage error.
 */
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
export const WIREFRAMES = join(ROOT, 'docs', 'ux', 'wireframes');
export const OUT = join(WIREFRAMES, 'png');
export const DEFAULT_SHOT = { state: 'default', width: 1440, height: 900 };
export const WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];

/** Parses the command line. Returns { check, filter, widths } or { error }. `--widths` implies `--check`. */
export function parseArgs(argv) {
  let check = false;
  let filter = '';
  let widths = [];
  for (const arg of argv) {
    if (arg === '--check') check = true;
    else if (arg.startsWith('--widths=')) {
      widths = arg.slice('--widths='.length).split(',').map(Number);
      if (!widths.length || widths.some((w) => !Number.isInteger(w) || w < 320 || w > 3840)) return { error: `bad widths in ${arg}` };
      check = true;
    } else if (arg.startsWith('-')) return { error: `unknown option ${arg}` };
    else if (filter) return { error: 'only one filter is allowed' };
    else filter = arg;
  }
  return { check, filter, widths };
}

/**
 * The shots to take: the page's own, or, with `--widths`, every state of the page at each of those widths
 * (1080 px high from 1920 px wide, 900 px otherwise).
 */
export function plannedShots(declared, widths) {
  if (!widths.length) return declared;
  const states = [...new Set(declared.map((s) => s.state))];
  return widths.flatMap((width) => states.map((state) => ({ state, width, height: width >= 1920 ? 1080 : 900 })));
}

/** Reads the shots a page declares in its `wf-shots` meta tag. */
export function parseShots(html) {
  const m = html.match(/<meta\s+name="wf-shots"\s+content="([^"]*)"/);
  if (!m) return [{ ...DEFAULT_SHOT }];
  return m[1].trim().split(/\s+/).map((token) => {
    const s = token.match(/^([a-z0-9-]+)@(\d+)x(\d+)$/);
    if (!s) throw new Error(`bad shot "${token}"`);
    return { state: s[1], width: Number(s[2]), height: Number(s[3]) };
  });
}

/** The PNG name of a shot: the page, then the state unless it is the default, then the width unless it is 1440. */
export function shotName(page, { state, width }) {
  return `${page}${state === 'default' ? '' : `--${state}`}${width === 1440 ? '' : `--${width}`}.png`;
}

/** The custom property names declared in the `:root` blocks of a stylesheet: the tokens the check allows. */
export function tokenNames(css) {
  const names = new Set();
  for (const block of css.matchAll(/:root\s*\{([^}]*)\}/g)) {
    for (const m of block[1].matchAll(/(--[a-z0-9-]+)\s*:/g)) names.add(m[1]);
  }
  return [...names];
}

/** The pages to render, sorted by name. */
export function listPages(dir, filter = '') {
  return readdirSync(dir)
    .filter((f) => f.endsWith('.html') && f.includes(filter))
    .sort()
    .map((f) => basename(f, '.html'));
}

/**
 * Turns one shot's measurement (from `measure` in the page) and its axe violations into findings, one string each.
 * Pure, so the rules are unit-tested without a browser.
 */
export function judge(m, violations = []) {
  const findings = [];
  for (const layer of m.layers) {
    if (layer.primaries.length > 1) findings.push(`${layer.name}: ${layer.primaries.length} primary buttons (${layer.primaries.join(', ')})`);
  }
  for (const group of m.groups) {
    if (group.count > 3) findings.push(`${group.name}: ${group.count} visible secondary actions (${group.items.join(', ')})`);
  }
  for (const text of m.dashes) findings.push(`a dash in "${text}"`);
  for (const label of m.cutButtons) findings.push(`label cut off or wrapped: "${label}"`);
  if (m.pageOverflow > 0) findings.push(`the page scrolls sideways by ${m.pageOverflow} px`);
  for (const where of m.overflows) findings.push(`content runs past its container: ${where}`);
  for (const [kind, values] of Object.entries(m.offTokens)) {
    for (const v of values) findings.push(`${kind} off the tokens: ${v}`);
  }
  for (const v of m.offGrid) findings.push(`spacing off the 3 px grid: ${v}`);
  for (const t of m.capitals) findings.push(`text in capitals: "${t}"`);
  for (const i of m.missingIcons) findings.push(`missing icon: ${i}`);
  for (const v of violations) findings.push(`axe ${v.id}: ${v.help} (${v.nodes} element${v.nodes === 1 ? '' : 's'})`);
  return findings;
}

/**
 * Runs in the page: measures what `judge` needs. `tokens` are the allowed custom property names. Kept free of
 * outside references, because Playwright serialises it.
 */
export function measure(tokens) {
  const visible = (el) => {
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) {
      const s = getComputedStyle(e);
      if (s.display === 'none' || s.visibility === 'hidden') return false;
    }
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  };
  const label = (el) => (el.getAttribute('aria-label') || el.textContent || '').replace(/\s+/g, ' ').trim();
  const describe = (el) => `${el.tagName.toLowerCase()}${el.className && typeof el.className === 'string' ? `.${el.className.trim().split(/\s+/)[0]}` : ''}`;

  // Layers and their primary buttons.
  const LAYER = '.wf-main, .wf-sidebar, .wf-dialog, [data-wf-layer], body[data-frame="none"]';
  const layers = [...document.querySelectorAll(LAYER)].filter(visible).map((layer) => ({
    name: layer.dataset.wfLayer || describe(layer),
    primaries: [...layer.querySelectorAll('.wf-btn--primary, .wf-btn--danger')]
      .filter((b) => visible(b) && b.parentElement.closest(LAYER) === layer)
      .map(label),
  }));

  // Groups of secondary actions.
  const GROUP = '.wf-toolbar, .wf-head, .wf-box__head, .wf-card, .wf-bulk, .wf-foot, .wf-sidebar__foot, .wf-panel__head, .wf-el__bar, [data-wf-actions]';
  const CONTROL = '.wf-btn:not(.wf-btn--primary):not(.wf-btn--danger):not([data-wf-more]):not([data-wf-frame]), .wf-seg, .wf-select:not([data-wf-field]), .wf-input--search';
  const groups = [...document.querySelectorAll(GROUP)].filter(visible).map((g) => {
    const items = [...g.querySelectorAll(CONTROL)]
      .filter((c) => visible(c) && c.closest(GROUP) === g && !c.parentElement.closest('.wf-seg'))
      .map((c) => label(c) || describe(c));
    return { name: g.dataset.wfActions || `${describe(g)} "${label(g).slice(0, 40)}"`, count: items.length, items };
  });

  // Text: dashes and capitals.
  const DASHES = new RegExp(`[${String.fromCharCode(0x2013, 0x2014)}]`); // en and em dash
  const dashes = [];
  const capitals = [];
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    const t = n.textContent;
    if (!t.trim() || !visible(n.parentElement)) continue;
    if (DASHES.test(t)) dashes.push(t.trim().slice(0, 60));
    if (getComputedStyle(n.parentElement).textTransform === 'uppercase') capitals.push(t.trim().slice(0, 40));
  }

  // One-line labels: a text node that runs over two lines, or a box narrower than its content, is cut or wrapped.
  const wraps = (el) => {
    const range = document.createRange();
    const texts = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    for (let t = texts.nextNode(); t; t = texts.nextNode()) {
      if (!t.textContent.trim()) continue;
      range.selectNodeContents(t);
      const tops = [...range.getClientRects()].filter((r) => r.width > 0).map((r) => r.top);
      if (tops.length > 1 && Math.max(...tops) - Math.min(...tops) > 4) return true;
    }
    return false;
  };
  const cutButtons = [...document.querySelectorAll('.wf-btn, .wf-chip, .wf-tab, .wf-head__name, .wf-head__main > a, .wf-step, .wf-item, .wf-pal-item, .wf-pop__item')]
    .filter(visible)
    .filter((b) => b.scrollWidth > b.clientWidth + 1 || wraps(b))
    .map(label);
  const pageOverflow = Math.max(0, document.documentElement.scrollWidth - window.innerWidth);
  // Content that runs past its box, a header row or a bar: a wide table, or header items under the view tabs.
  // Popovers float over the page and are left out while this is measured.
  const OVERFLOW = '.wf-box, .wf-head__main, .wf-toolbar, .wf-box__head, .wf-bulk, .wf-foot, .wf-card, .wf-panel, .wf-sidebar, .wf-dialog';
  const pops = [...document.querySelectorAll('.wf-pop')].map((p) => [p, p.style.display]);
  for (const [p] of pops) p.style.display = 'none';
  const overflows = [...document.querySelectorAll(OVERFLOW)]
    .filter((c) => visible(c) && c.scrollWidth > c.clientWidth + 1)
    .map((c) => `${describe(c)} "${label(c).slice(0, 40)}" by ${c.scrollWidth - c.clientWidth} px`);
  for (const [p, display] of pops) p.style.display = display;

  // Tokens: the allowed values come from the custom properties on :root.
  const probe = document.createElement('div');
  document.body.append(probe);
  const rootStyle = getComputedStyle(document.documentElement);
  const colours = new Set(['rgba(0, 0, 0, 0)']);
  const shadows = new Set(['none']);
  const fontSizes = new Set();
  for (const name of tokens) {
    const value = rootStyle.getPropertyValue(name).trim();
    probe.style.color = '';
    probe.style.color = value;
    if (probe.style.color) colours.add(getComputedStyle(probe).color);
    if (name.includes('shadow') || name === '--wf-focus') {
      probe.style.boxShadow = value;
      shadows.add(getComputedStyle(probe).boxShadow);
    }
    if (name.startsWith('--wf-type-')) fontSizes.add(value);
  }
  probe.remove();
  const RADII = new Set(['0px', '3px', '999px']);
  // A computed shadow list reads "rgb(...) x y blur spread, ..."; the third length of each shadow is its blur.
  const blurred = (list) => list.split(/,(?![^(]*\))/).some((one) => parseFloat((one.match(/-?\d+(\.\d+)?px/g) || [])[2] || '0') > 0);
  const offTokens = { colour: new Set(), radius: new Set(), 'font size': new Set(), shadow: new Set() };
  const offGrid = new Set();
  const SIDES = ['top', 'right', 'bottom', 'left'];
  for (const el of document.querySelectorAll('body *')) {
    if (el.closest('svg') || el.closest('[data-wf-sample]') || !visible(el)) continue;
    const s = getComputedStyle(el);
    const where = describe(el);
    const check = (kind, value, allowed) => { if (!allowed.has(value)) offTokens[kind].add(`${value} (${where})`); };
    check('colour', s.color, colours);
    check('colour', s.backgroundColor, colours);
    for (const side of SIDES) {
      if (parseFloat(s[`border-${side}-width`]) > 0 && s[`border-${side}-style`] !== 'none') check('colour', s[`border-${side}-color`], colours);
    }
    for (const corner of ['top-left', 'top-right', 'bottom-right', 'bottom-left']) {
      for (const part of s[`border-${corner}-radius`].split(' ')) check('radius', part, RADII);
    }
    check('font size', s.fontSize, fontSizes);
    // Rings and inset bars are borders drawn as shadows; only a blurred shadow is a shade, and it must be the one.
    if (s.boxShadow !== 'none' && !shadows.has(s.boxShadow) && blurred(s.boxShadow)) offTokens.shadow.add(`${s.boxShadow} (${where})`);
    // Margins are left out: an auto margin computes to whatever space is left.
    for (const side of SIDES) {
      const px = parseFloat(s[`padding-${side}`]);
      if (px && Math.abs(px % 3) > 0.01) offGrid.add(`padding-${side} ${px}px (${where})`);
    }
    for (const gap of [s.rowGap, s.columnGap]) {
      const px = parseFloat(gap);
      if (px && Math.abs(px % 3) > 0.01) offGrid.add(`gap ${px}px (${where})`);
    }
  }

  const missingIcons = [...new Set([...document.querySelectorAll('use')]
    .map((u) => u.getAttribute('href'))
    .filter((h) => h && h.startsWith('#') && !document.getElementById(h.slice(1))))];

  return {
    layers,
    groups,
    dashes: [...new Set(dashes)],
    capitals: [...new Set(capitals)],
    cutButtons: [...new Set(cutButtons)],
    pageOverflow,
    overflows: overflows.slice(0, 5),
    offTokens: Object.fromEntries(Object.entries(offTokens).map(([k, v]) => [k, [...v].slice(0, 5)])),
    offGrid: [...offGrid].slice(0, 5),
    missingIcons,
  };
}

async function main(argv) {
  const args = parseArgs(argv);
  if (args.error) {
    console.error(`wireframes: ${args.error}\nusage: node scripts/wireframes.mjs [--check] [filter]`);
    return 2;
  }
  const pages = listPages(WIREFRAMES, args.filter);
  if (pages.length === 0) {
    console.error(`wireframes: no page matches "${args.filter}"`);
    return 1;
  }
  const require = createRequire(import.meta.url);
  const { chromium } = require('playwright-core');
  const { default: AxeBuilder } = args.check ? await import('@axe-core/playwright') : { default: null };

  mkdirSync(OUT, { recursive: true });
  if (!args.filter && !args.widths.length) {
    // A full run replaces every PNG, so a removed shot leaves no stale picture behind.
    for (const f of readdirSync(OUT)) if (f.endsWith('.png')) rmSync(join(OUT, f));
  }
  const tokens = tokenNames(readFileSync(join(WIREFRAMES, 'assets', 'kit.css'), 'utf8'));
  const browser = await chromium.launch({ channel: 'msedge' });
  let shots = 0;
  let failed = 0;
  try {
    for (const page of pages) {
      const file = join(WIREFRAMES, `${page}.html`);
      for (const shot of plannedShots(parseShots(readFileSync(file, 'utf8')), args.widths)) {
        const context = await browser.newContext({ viewport: { width: shot.width, height: shot.height }, deviceScaleFactor: 1, reducedMotion: 'reduce' });
        const tab = await context.newPage();
        const errors = [];
        tab.on('pageerror', (e) => errors.push(e.message));
        await tab.goto(`${pathToFileURL(file).href}?state=${shot.state}`);
        await tab.waitForSelector('html[data-wf-ready="true"]', { timeout: 15000 });
        await tab.evaluate(() => document.fonts.ready);
        const name = shotName(page, shot);
        // A width sweep only measures: the committed PNGs stay the pages' own shots.
        if (!args.widths.length) await tab.screenshot({ path: join(OUT, name) });
        shots++;
        if (args.check) {
          const m = await tab.evaluate(measure, tokens);
          const axe = await new AxeBuilder({ page: tab }).withTags(WCAG_TAGS).analyze();
          const violations = axe.violations.map((v) => ({ id: v.id, help: v.help, nodes: v.nodes.length }));
          const findings = [...errors.map((e) => `script error: ${e}`), ...judge(m, violations)];
          if (findings.length) {
            failed++;
            console.log(`FAIL ${name}`);
            for (const f of findings) console.log(`  - ${f}`);
          } else {
            console.log(`ok   ${name}`);
          }
        } else if (errors.length) {
          failed++;
          console.log(`FAIL ${name}: ${errors.join('; ')}`);
        }
        await context.close();
      }
    }
  } finally {
    await browser.close();
  }
  console.log(`wireframes: ${shots} shot${shots === 1 ? '' : 's'} of ${pages.length} page${pages.length === 1 ? '' : 's'}${args.check ? `, ${failed} with findings` : ''}`);
  return failed ? 1 : 0;
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  if (!existsSync(WIREFRAMES)) {
    console.error('wireframes: docs/ux/wireframes is missing');
    process.exit(1);
  }
  main(process.argv.slice(2)).then((code) => process.exit(code), (e) => {
    console.error(`wireframes: ${e.stack || e}`);
    process.exit(1);
  });
}
