#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Renders the website form designs of ROADMAP 2.3 (`docs/ux/website/*.html`) to PNG with Playwright in Edge, at
 * 375 px (as a phone), 768 px (a tablet) and 1440 px, in the host site's light and dark design, and checks them.
 *
 *   node scripts/website-mocks.mjs [filter]          render every shot into docs/ux/website/png/
 *   node scripts/website-mocks.mjs --check [filter]  render, then check every shot and the theme's stylesheets
 *   node scripts/website-mocks.mjs --widths=480,720 [filter]
 *                                                   check every state of every page at those widths in both
 *                                                   designs, without writing PNGs (the widths between the
 *                                                   rendered ones); a width under 768 px is a touch phone
 *
 * `filter` keeps only pages whose file name contains it. A page lists its states in
 * `<meta name="jfb-shots" content="default errors">` (without it, `default`); each state is rendered at each width
 * in both designs, through `?state=<state>&scheme=light|dark`. `<meta name="jfb-forced" content="default">` adds a
 * 1440 px shot of those states with forced colours (a Windows contrast theme), rendered but not measured.
 *
 * The check measures, per shot, inside every form:
 * - contrast (WCAG 2.2 AA, 1.4.3 and 1.4.11): every text, typed value and placeholder at least 4.5:1, or 3:1 when
 *   large, against what lies behind it; every control's boundary, chosen state, icon and focus ring at least 3:1;
 * - focus: every focusable element draws a ring of at least 2 px when it gets the keyboard focus;
 * - targets: every button, input, choice and standalone link at least 44 by 44 px (ROADMAP section 4.13);
 * - the tokens: every colour, corner, font size and shade is one of the theme's tokens, and every padding and gap
 *   sits on the 4 px grid;
 * - no em or en dash in visible text, no sideways scrolling, nothing running past its container, and no button
 *   label that wraps at desktop widths;
 * - axe-core finds no WCAG 2.2 A or AA violation on the whole page.
 * Once per run it checks the theme's stylesheets: every selector under .jfb-form, no physical left or right
 * (ADR-0015), and the default theme within 10 KB gzip once minified (ROADMAP section 4.12).
 *
 * Exit codes: 0 fine, 1 findings or a failure, 2 usage error.
 */
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync } from 'node:fs';
import { createRequire } from 'node:module';
import { basename, dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { gzipSync } from 'node:zlib';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
export const WEBSITE = join(ROOT, 'docs', 'ux', 'website');
export const OUT = join(WEBSITE, 'png');
export const THEME_FILES = ['assets/base.css', 'assets/theme.css'];
export const WIDTHS = [375, 768, 1440];
export const SCHEMES = ['light', 'dark'];
export const BUDGET = 10 * 1024;
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

/** Reads a page's states from a meta tag (`jfb-shots` or `jfb-forced`); `fallback` when the tag is missing. */
export function parseStates(html, name = 'jfb-shots', fallback = ['default']) {
  const m = html.match(new RegExp(`<meta\\s+name="${name}"\\s+content="([^"]*)"`));
  if (!m) return fallback;
  const states = m[1].trim().split(/\s+/).filter(Boolean);
  for (const s of states) if (!/^[a-z0-9-]+$/.test(s)) throw new Error(`bad state "${s}"`);
  return states;
}

/**
 * The shots of a page: each state at each width in each design, then the forced-colours shots at 1440 px. With
 * `widths` (a width sweep), each state at those widths in both designs, and no forced-colours shots.
 */
export function plannedShots(states, forced = [], widths = []) {
  const shots = [];
  for (const state of states) {
    for (const width of widths.length ? widths : WIDTHS) for (const scheme of SCHEMES) shots.push({ state, width, scheme, forced: false });
  }
  if (!widths.length) for (const state of forced) shots.push({ state, width: 1440, scheme: 'light', forced: true });
  return shots;
}

/** The PNG name of a shot: the page, the state unless it is the default, the width, then "dark" or "forced". */
export function shotName(page, { state, width, scheme, forced }) {
  return `${page}${state === 'default' ? '' : `--${state}`}--${width}${forced ? '--forced' : scheme === 'dark' ? '--dark' : ''}.png`;
}

/** The pages to render, sorted by name. */
export function listPages(dir, filter = '') {
  return readdirSync(dir)
    .filter((f) => f.endsWith('.html') && f.includes(filter))
    .sort()
    .map((f) => basename(f, '.html'));
}

/** The private custom properties a stylesheet declares (`--_name:`), which the token check allows. */
export function tokenNames(css) {
  return [...new Set([...css.matchAll(/(--_[a-z0-9-]+)\s*:/g)].map((m) => m[1]))];
}

/** The WCAG contrast ratio of two opaque sRGB colours given as [r, g, b] in 0 to 255. */
export function contrast(a, b) {
  const lum = ([r, g, bl]) => {
    const lin = (c) => {
      const v = c / 255;
      return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(bl);
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** Whether text counts as large for WCAG: at least 24 px, or 18.66 px when bold (700 and up). */
export function isLargeText(px, weight) {
  return px >= 24 || (px >= 18.66 && weight >= 700);
}

/** Splits a CSS value on whitespace outside parentheses. */
function words(value) {
  const out = [];
  let depth = 0;
  let cur = '';
  for (const ch of value.trim()) {
    if (ch === '(') depth++;
    if (ch === ')') depth--;
    if (/\s/.test(ch) && depth === 0) {
      if (cur) out.push(cur);
      cur = '';
    } else cur += ch;
  }
  if (cur) out.push(cur);
  return out;
}

/**
 * Lints a theme stylesheet (ADR-0015): every selector must start with .jfb-form, and nothing may be physical:
 * no left or right in a property name or as a float, clear or text-align value, no four-value shorthand whose
 * sides differ, and no border-radius shorthand with different corners. Returns { unscoped, physical }.
 */
export function lintStylesheet(css) {
  const text = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const unscoped = [];
  const physical = [];
  const stack = ['top'];
  let prelude = '';
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    const ctx = stack[stack.length - 1];
    if (ctx === 'rule') {
      if (ch === '{') stack.push('rule');
      else if (ch === '}') stack.pop();
      continue;
    }
    if (ch === '{') {
      const head = prelude.trim();
      prelude = '';
      if (head.startsWith('@')) {
        stack.push(/^@(media|container|supports|layer)\b/.test(head) ? 'at' : 'rule');
      } else {
        let depth = 0;
        let cur = '';
        const parts = [];
        for (const c of head) {
          if (c === '(') depth++;
          if (c === ')') depth--;
          if (c === ',' && depth === 0) {
            parts.push(cur);
            cur = '';
          } else cur += c;
        }
        parts.push(cur);
        for (const p of parts.map((x) => x.trim().replace(/\s+/g, ' '))) if (!/^\.jfb-form(?![\w-])/.test(p)) unscoped.push(p);
        stack.push('rule');
      }
    } else if (ch === '}') {
      stack.pop();
      prelude = '';
    } else if (ch === ';' && ctx !== 'rule') {
      prelude = '';
    } else prelude += ch;
  }
  const SHORTHANDS = new Set(['margin', 'padding', 'inset', 'border-width', 'border-style', 'border-color', 'scroll-margin', 'scroll-padding']);
  for (const m of text.matchAll(/(?:^|[;{\s])([a-z-]+)\s*:\s*([^;{}]+)/g)) {
    const [prop, rawValue] = [m[1], m[2].trim()];
    if (prop.startsWith('--')) continue;
    const value = rawValue.replace(/\s*!important$/, '');
    const where = `${prop}: ${value}`;
    if (/(^|-)(left|right)(-|$)/.test(prop)) physical.push(where);
    else if (['float', 'clear', 'text-align'].includes(prop) && /^(left|right)$/.test(value)) physical.push(where);
    else if (SHORTHANDS.has(prop)) {
      const v = words(value);
      if (v.length === 4 && v[1] !== v[3]) physical.push(where);
    } else if (prop === 'border-radius' && new Set(words(value.split('/')[0])).size > 1) physical.push(where);
  }
  return { unscoped: [...new Set(unscoped)], physical: [...new Set(physical)] };
}

/** The gzip size of a stylesheet once its comments and spare whitespace are gone, as a minifier would leave it. */
export function gzipSize(css) {
  const min = css
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/\s+/g, ' ')
    .replace(/\s*([{}:;,>])\s*/g, '$1')
    .replace(/;}/g, '}')
    .trim();
  return gzipSync(Buffer.from(min)).length;
}

/**
 * Turns one shot's measurement (from `measure` in the page) and its axe violations into findings, one string each.
 * Pure, so the rules are unit-tested without a browser.
 */
export function judge(m, violations = []) {
  const findings = [];
  for (const t of m.texts) findings.push(`text contrast ${t.ratio}:1 (needs ${t.needs}:1): "${t.text}" (${t.where})`);
  for (const c of m.controls) findings.push(`control contrast ${c.ratio}:1 (needs 3:1): ${c.what} (${c.where})`);
  for (const f of m.focus) findings.push(`focus: ${f}`);
  for (const t of m.targets) findings.push(`target smaller than 44 by 44 px: ${t}`);
  for (const [kind, values] of Object.entries(m.offTokens)) {
    for (const v of values) findings.push(`${kind} off the tokens: ${v}`);
  }
  for (const v of m.offGrid) findings.push(`spacing off the 4 px grid: ${v}`);
  for (const text of m.dashes) findings.push(`a dash in "${text}"`);
  for (const label of m.wrapped) findings.push(`button label wraps: "${label}"`);
  if (m.pageOverflow > 0) findings.push(`the page scrolls sideways by ${m.pageOverflow} px`);
  for (const where of m.overflows) findings.push(`content runs past its container: ${where}`);
  for (const v of violations) findings.push(`axe ${v.id}: ${v.help} (${v.nodes} element${v.nodes === 1 ? '' : 's'})`);
  return findings;
}

/**
 * Runs in the page: measures what `judge` needs, for every `.jfb-form`. `tokens` are the theme's private custom
 * property names. Kept free of outside references, because Playwright serialises it. It moves the focus, so the
 * screenshot is taken before it runs.
 */
export function measure(tokens) {
  const LIMIT = 8;
  const parse = (value) => {
    const v = (value || '').trim();
    let m = v.match(/^rgba?\(([^)]+)\)$/);
    if (m) {
      const p = m[1].split(/[\s,/]+/).filter(Boolean).map(Number);
      return [p[0], p[1], p[2], p.length > 3 ? p[3] : 1];
    }
    m = v.match(/^color\(srgb\s+([^)]+)\)$/);
    if (m) {
      const p = m[1].split(/[\s/]+/).filter(Boolean).map(Number);
      return [p[0] * 255, p[1] * 255, p[2] * 255, p.length > 3 ? p[3] : 1];
    }
    return null;
  };
  const over = (top, bottom) => {
    const a = top[3] + bottom[3] * (1 - top[3]);
    if (a === 0) return [0, 0, 0, 0];
    const mix = (i) => (top[i] * top[3] + bottom[i] * bottom[3] * (1 - top[3])) / a;
    return [mix(0), mix(1), mix(2), a];
  };
  const lum = (c) => {
    const lin = (x) => {
      const v = x / 255;
      return v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * lin(c[0]) + 0.7152 * lin(c[1]) + 0.0722 * lin(c[2]);
  };
  const ratio = (a, b) => {
    const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
    return (hi + 0.05) / (lo + 0.05);
  };
  const round = (r) => Math.floor(r * 100) / 100;
  const visible = (el) => {
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) {
      const s = getComputedStyle(e);
      if (s.display === 'none' || s.visibility === 'hidden' || e.getAttribute('aria-hidden') === 'true') return false;
    }
    const r = el.getBoundingClientRect();
    return r.width > 1 && r.height > 1;
  };
  const describe = (el) => {
    const cls = typeof el.className === 'string' && el.className.trim() ? `.${el.className.trim().split(/\s+/)[0]}` : '';
    return `${el.tagName.toLowerCase()}${el.id ? `#${el.id}` : cls}`;
  };
  // The colour behind an element: its own background and its ancestors', composited over the canvas.
  const behind = (el, includeSelf = true) => {
    const layers = [];
    for (let e = includeSelf ? el : el.parentElement; e; e = e.parentElement) {
      const c = parse(getComputedStyle(e).backgroundColor);
      if (c && c[3] > 0) layers.push(c);
      if (c && c[3] >= 1) break;
    }
    let bg = [255, 255, 255, 1];
    for (const layer of layers.reverse()) bg = over(layer, bg);
    return bg;
  };
  const textOf = (el) => (el.getAttribute('aria-label') || el.textContent || '').replace(/\s+/g, ' ').trim();

  const texts = [];
  const controls = [];
  const focus = [];
  const targets = [];
  const offTokens = { colour: new Set(), corner: new Set(), 'font size': new Set(), shade: new Set() };
  const offGrid = new Set();
  const overflows = [];
  const wrapped = [];

  const addText = (fgValue, bg, el, sample) => {
    const fg = parse(fgValue);
    if (!fg || fg[3] === 0) return; // text made transparent on purpose (a busy button's label) is not read
    const s = getComputedStyle(el);
    const needs = isLarge(parseFloat(s.fontSize), Number(s.fontWeight)) ? 3 : 4.5;
    const r = ratio(over(fg, bg), bg);
    if (r < needs) texts.push({ ratio: round(r), needs, text: sample.slice(0, 50), where: describe(el) });
  };
  const isLarge = (px, weight) => px >= 24 || (px >= 18.66 && weight >= 700);
  const addControl = (r, what, el) => {
    if (r < 3) controls.push({ ratio: round(r), what, where: describe(el) });
  };
  const boundary = (el) => {
    const s = getComputedStyle(el);
    const outside = behind(el, false);
    let best = 0;
    for (const side of ['top', 'right', 'bottom', 'left']) {
      const width = parseFloat(s[`border-${side}-width`]);
      const c = parse(s[`border-${side}-color`]);
      if (width >= 1 && s[`border-${side}-style`] !== 'none' && c && c[3] > 0) best = Math.max(best, ratio(over(c, outside), outside));
    }
    const fill = parse(s.backgroundColor);
    if (fill && fill[3] > 0) best = Math.max(best, ratio(over(fill, outside), outside));
    return best;
  };

  for (const form of document.querySelectorAll('.jfb-form')) {
    const fs = getComputedStyle(form);

    // Allowed values: the computed value of each token, plus the base font size and the fixed corners.
    const probe = document.createElement('span');
    form.append(probe);
    const colours = new Set(['rgba(0, 0, 0, 0)']);
    const shades = new Set(['none']);
    for (const name of tokens) {
      const value = fs.getPropertyValue(name).trim();
      if (!value) continue;
      probe.style.color = '';
      probe.style.color = value;
      if (probe.style.color) colours.add(getComputedStyle(probe).color);
      if (name.includes('shade')) {
        probe.style.boxShadow = value;
        shades.add(getComputedStyle(probe).boxShadow);
      }
    }
    probe.style.color = '';
    const corners = new Set(['0px', '50%', '999px']);
    for (const value of ['var(--_r-panel)', 'var(--_r-card)', 'var(--_r-control)', 'var(--_r-tag)', 'calc(var(--_r-card) - 1px)']) {
      probe.style.borderRadius = value;
      corners.add(getComputedStyle(probe).borderTopLeftRadius);
    }
    const sizes = new Set([fs.fontSize]);
    for (const value of ['var(--_small)', 'var(--_large)', 'var(--_title)', 'max(1em, 16px)']) {
      probe.style.fontSize = value;
      sizes.add(getComputedStyle(probe).fontSize);
    }
    probe.remove();
    const blurred = (list) => list.split(/,(?![^(]*\))/).some((one) => parseFloat((one.match(/-?\d+(\.\d+)?px/g) || [])[2] || '0') > 0);

    // Inputs kept in place but unseen (the label is drawn instead) have no colours of their own to check.
    const all = [...form.querySelectorAll('*')].filter((el) => !el.closest('.jfb-hp, script, canvas, svg') && visible(el) && getComputedStyle(el).opacity !== '0');

    // Text: every visible text node, typed value and placeholder.
    const walker = document.createTreeWalker(form, NodeFilter.SHOW_TEXT);
    for (let n = walker.nextNode(); n; n = walker.nextNode()) {
      const el = n.parentElement;
      const t = n.textContent.replace(/\s+/g, ' ').trim();
      if (!t || !el || el.closest('.jfb-hp, script, option') || !visible(el)) continue;
      const s = getComputedStyle(el);
      if (parseFloat(s.fontSize) === 0) continue;
      addText(s.color, behind(el), el, t);
    }
    for (const el of form.querySelectorAll('input, select, textarea')) {
      if (!visible(el) || el.closest('.jfb-hp') || ['hidden', 'checkbox', 'radio', 'range'].includes(el.type) || getComputedStyle(el).opacity === '0') continue;
      const bg = behind(el);
      if (el.tagName === 'SELECT' || el.value || el.type === 'file') addText(getComputedStyle(el).color, bg, el, el.tagName === 'SELECT' ? el.selectedOptions[0]?.textContent || '' : el.value || 'file input');
      if (el.placeholder && !el.value) addText(getComputedStyle(el, '::placeholder').color, bg, el, `placeholder ${el.placeholder}`);
    }

    // Controls: their boundaries, chosen states and icons (WCAG 1.4.11).
    for (const el of form.querySelectorAll('input:not([type="hidden"]), select, textarea, .jfb-button, .jfb-choices-cards > .jfb-choice, :is(.jfb-choices-buttons, .jfb-choices-scale) > .jfb-choice > label, .jfb-signature-pad, .jfb-upload, .jfb-ranking')) {
      if (!visible(el) || el.closest('.jfb-hp') || getComputedStyle(el).opacity === '0') continue;
      const s = getComputedStyle(el);
      const isButton = el.classList.contains('jfb-button');
      const b = boundary(el);
      if (isButton && b === 0) continue; // a link or icon button: its text or icon is what shows it
      if (el.classList.contains('jfb-upload') && b === 0) continue; // no drop frame (touch screens): its buttons are the controls
      if (el.type === 'file' || el.type === 'range') continue; // the native parts are checked as text or by the UA
      addControl(b, el.checked ? 'chosen state' : 'boundary', el);
      if (el.type === 'checkbox' && el.checked) {
        const mark = getComputedStyle(el, '::before');
        const markColour = el.classList.contains('jfb-switch') ? mark.borderTopColor : mark.backgroundColor;
        const m = parse(markColour);
        if (m && m[3] > 0) addControl(ratio(m, behind(el)), 'tick or thumb', el);
      }
      if (el.type === 'checkbox' && !el.checked && el.classList.contains('jfb-switch')) {
        const m = parse(getComputedStyle(el, '::before').borderTopColor);
        if (m) addControl(ratio(m, behind(el)), 'thumb', el);
      }
      if (el.type === 'radio' && el.checked) {
        const ring = parse(s.borderTopColor);
        addControl(ratio(ring, behind(el)), 'chosen dot', el);
      }
    }
    for (const el of form.querySelectorAll('.jfb-icon')) {
      if (!visible(el)) continue;
      const c = parse(getComputedStyle(el).backgroundColor);
      if (c) addControl(ratio(over(c, behind(el, false)), behind(el, false)), 'icon', el);
    }
    for (const label of form.querySelectorAll('.jfb-choices-rating > .jfb-choice > label')) {
      if (!visible(label)) continue;
      const c = parse(getComputedStyle(label, '::before').backgroundColor);
      if (c) addControl(ratio(c, behind(label)), 'rating symbol', label);
    }
    for (const out of form.querySelectorAll('.jfb-outcome')) {
      const tint = parse(getComputedStyle(out, '::before').backgroundColor);
      const mark = parse(getComputedStyle(out, '::after').backgroundColor);
      if (tint && mark) {
        const bg = over(tint, behind(out));
        addControl(ratio(mark, bg), 'outcome mark', out);
      }
    }

    // Focus: each focusable element, focused in turn, must draw a ring of at least 2 px with 3:1.
    const focusables = [...form.querySelectorAll('a[href], button, input:not([type="hidden"]), select, textarea, [tabindex]:not([tabindex="-1"])')]
      .filter((el) => !el.disabled && el.getAttribute('tabindex') !== '-1' && !el.closest('.jfb-hp') && (visible(el) || getComputedStyle(el).opacity === '0'));
    for (const el of focusables) {
      el.focus({ preventScroll: true });
      if (document.activeElement !== el) continue;
      const candidates = [el, el.nextElementSibling, el.closest('.jfb-choice')].filter(Boolean);
      const ring = candidates.map((c) => ({ c, s: getComputedStyle(c) })).find(({ s }) => s.outlineStyle !== 'none' && parseFloat(s.outlineWidth) >= 2);
      if (!ring) {
        focus.push(`no ring on ${describe(el)}`);
        continue;
      }
      const colour = parse(ring.s.outlineColor);
      const bg = behind(ring.c, false);
      const r = ratio(over(colour, bg), bg);
      if (r < 3) focus.push(`ring ${round(r)}:1 on ${describe(el)}`);
    }
    if (document.activeElement && form.contains(document.activeElement)) document.activeElement.blur();

    // Targets: 44 by 44 px for buttons, inputs, choices and standalone links.
    const TARGET = 'button, select, textarea, input:not([type="hidden"]):not([type="checkbox"]):not([type="radio"]), .jfb-choice > label, a[href]';
    for (const el of form.querySelectorAll(TARGET)) {
      if (!visible(el) || el.closest('.jfb-hp')) continue;
      if (el.tagName === 'A' && [...el.parentElement.childNodes].some((n) => n !== el && n.textContent.trim())) continue; // a link in a sentence
      const box = (el.matches('.jfb-choices-cards > .jfb-choice > label') ? el.parentElement : el).getBoundingClientRect();
      if (box.width < 43.5 || box.height < 43.5) targets.push(`${describe(el)} "${textOf(el).slice(0, 30)}" ${Math.round(box.width)} by ${Math.round(box.height)}`);
    }

    // Tokens and the grid.
    for (const el of all) {
      const s = getComputedStyle(el);
      const where = describe(el);
      if (el.tagName === 'IMG' || el.tagName === 'PROGRESS') continue;
      const colour = (value) => { if (!colours.has(value)) offTokens.colour.add(`${value} (${where})`); };
      colour(s.color);
      colour(s.backgroundColor);
      for (const side of ['top', 'right', 'bottom', 'left']) {
        if (parseFloat(s[`border-${side}-width`]) > 0 && s[`border-${side}-style`] !== 'none') colour(s[`border-${side}-color`]);
      }
      for (const corner of ['top-left', 'top-right', 'bottom-right', 'bottom-left']) {
        for (const part of s[`border-${corner}-radius`].split(' ')) if (!corners.has(part)) offTokens.corner.add(`${part} (${where})`);
      }
      if ([...el.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim()) && !sizes.has(s.fontSize)) offTokens['font size'].add(`${s.fontSize} (${where})`);
      if (s.boxShadow !== 'none' && !shades.has(s.boxShadow) && blurred(s.boxShadow)) offTokens.shade.add(`${s.boxShadow} (${where})`);
      for (const side of ['top', 'right', 'bottom', 'left']) {
        const px = parseFloat(s[`padding-${side}`]);
        if (px && Math.abs(px % 4) > 0.01) offGrid.add(`padding-${side} ${px}px (${where})`);
      }
      for (const gap of [s.rowGap, s.columnGap]) {
        const px = parseFloat(gap);
        if (px && Math.abs(px % 4) > 0.01) offGrid.add(`gap ${px}px (${where})`);
      }
    }

    // Containers that something runs past, and button labels that wrap on a desktop.
    const BOXES = '.jfb-form, .jfb-page, .jfb-group, .jfb-fields, .jfb-field, .jfb-choices, .jfb-choice, .jfb-navigation, .jfb-progress, .jfb-progress-steps > ol > li, .jfb-error-summary, .jfb-summary dl > div, .jfb-file, .jfb-row, .jfb-upload, .jfb-matrix > fieldset, .jfb-ranking > li, .jfb-outcome, .jfb-notice, .jfb-captcha, .jfb-parts, .jfb-affix';
    for (const el of [form, ...form.querySelectorAll(BOXES)]) {
      if (visible(el) && el.scrollWidth > el.clientWidth + 1) overflows.push(`${describe(el)} "${textOf(el).slice(0, 40)}" by ${el.scrollWidth - el.clientWidth} px`);
    }
    if (window.innerWidth >= 768) {
      for (const el of form.querySelectorAll('.jfb-button, :is(.jfb-choices-buttons) > .jfb-choice > label')) {
        if (!visible(el)) continue;
        // Only the text a sighted visitor reads: text for screen readers sits in a 1 px box of its own.
        const tops = [];
        const inner = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
        for (let n = inner.nextNode(); n; n = inner.nextNode()) {
          if (!n.textContent.trim() || n.parentElement.closest('.jfb-visually-hidden')) continue;
          const range = document.createRange();
          range.selectNodeContents(n);
          for (const r of range.getClientRects()) if (r.width > 0) tops.push(Math.round(r.top));
        }
        if (tops.length && Math.max(...tops) - Math.min(...tops) > 4) wrapped.push(textOf(el));
      }
    }
  }

  // The whole page: dashes and sideways scrolling.
  const DASHES = new RegExp(`[${String.fromCharCode(0x2013, 0x2014)}]`);
  const dashes = new Set();
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  for (let n = walker.nextNode(); n; n = walker.nextNode()) {
    if (n.parentElement && n.parentElement.tagName !== 'SCRIPT' && DASHES.test(n.textContent)) dashes.add(n.textContent.trim().slice(0, 60));
  }

  const firsts = (list) => [...new Set(list)].slice(0, LIMIT);
  return {
    texts: texts.slice(0, LIMIT),
    controls: controls.slice(0, LIMIT),
    focus: firsts(focus),
    targets: firsts(targets),
    offTokens: Object.fromEntries(Object.entries(offTokens).map(([k, v]) => [k, [...v].slice(0, LIMIT)])),
    offGrid: [...offGrid].slice(0, LIMIT),
    dashes: [...dashes],
    wrapped: firsts(wrapped),
    pageOverflow: Math.max(0, document.documentElement.scrollWidth - window.innerWidth),
    overflows: firsts(overflows),
  };
}

/** The findings for the theme's stylesheets, checked once per run. */
export function stylesheetFindings(files) {
  const findings = [];
  let total = 0;
  for (const [name, css] of files) {
    const { unscoped, physical } = lintStylesheet(css);
    for (const s of unscoped) findings.push(`${name}: selector outside .jfb-form: ${s}`);
    for (const p of physical) findings.push(`${name}: physical property: ${p}`);
    total += gzipSize(css);
  }
  if (total > BUDGET) findings.push(`the theme's CSS is ${total} bytes gzip, over the ${BUDGET} byte budget`);
  return { findings, total };
}

async function main(argv) {
  const args = parseArgs(argv);
  if (args.error) {
    console.error(`website-mocks: ${args.error}\nusage: node scripts/website-mocks.mjs [--check | --widths=480,...] [filter]`);
    return 2;
  }
  const pages = listPages(WEBSITE, args.filter);
  if (pages.length === 0) {
    console.error(`website-mocks: no page matches "${args.filter}"`);
    return 1;
  }
  const require = createRequire(import.meta.url);
  const { chromium } = require('playwright-core');
  const { default: AxeBuilder } = args.check ? await import('@axe-core/playwright') : { default: null };

  let failed = 0;
  const sheets = THEME_FILES.map((f) => [f, readFileSync(join(WEBSITE, f), 'utf8')]);
  if (args.check) {
    const { findings, total } = stylesheetFindings(sheets);
    console.log(`${findings.length ? 'FAIL' : 'ok  '} theme stylesheets: ${total} bytes gzip of ${BUDGET}`);
    for (const f of findings) console.log(`  - ${f}`);
    if (findings.length) failed++;
  }
  const tokens = tokenNames(sheets.map(([, css]) => css).join('\n'));

  mkdirSync(OUT, { recursive: true });
  if (!args.filter && !args.widths.length) {
    // A full run replaces every PNG, so a removed shot leaves no stale picture behind.
    for (const f of readdirSync(OUT)) if (f.endsWith('.png')) rmSync(join(OUT, f));
  }
  const browser = await chromium.launch({ channel: 'msedge' });
  let shots = 0;
  try {
    for (const page of pages) {
      const file = join(WEBSITE, `${page}.html`);
      const html = readFileSync(file, 'utf8');
      for (const shot of plannedShots(parseStates(html), parseStates(html, 'jfb-forced', []), args.widths)) {
        const phone = shot.width < 768;
        const context = await browser.newContext({
          viewport: { width: shot.width, height: phone ? 812 : 900 },
          deviceScaleFactor: 1,
          isMobile: phone,
          hasTouch: phone,
          reducedMotion: 'reduce',
          colorScheme: shot.scheme,
          forcedColors: shot.forced ? 'active' : 'none',
        });
        const tab = await context.newPage();
        const errors = [];
        tab.on('pageerror', (e) => errors.push(e.message));
        await tab.goto(`${pathToFileURL(file).href}?state=${shot.state}&scheme=${shot.scheme}`);
        await tab.waitForSelector('html[data-mock-ready="true"]', { timeout: 15000 });
        await tab.evaluate(() => document.fonts.ready);
        // Fades that start on load (an error, a notice) run even with reduced motion: wait for them to end.
        await tab.evaluate(() => Promise.all(document.getAnimations()
          .filter((a) => a.effect && a.effect.getTiming().iterations !== Infinity)
          .map((a) => a.finished.catch(() => null))));
        const name = shotName(page, shot);
        if (args.widths.length) {
          // A width sweep only measures: the committed PNGs stay the pages' own shots.
        } else if (phone) {
          // A full-page capture drops the touch emulation, so the page would be drawn as for a mouse; a viewport as
          // tall as the page keeps (pointer: coarse) and (hover: none) true.
          const height = await tab.evaluate(() => document.documentElement.scrollHeight);
          await tab.setViewportSize({ width: shot.width, height });
          await tab.screenshot({ path: join(OUT, name) });
          await tab.setViewportSize({ width: shot.width, height: 812 });
        } else {
          await tab.screenshot({ path: join(OUT, name), fullPage: true });
        }
        shots++;
        if (args.check && !shot.forced) {
          const axe = await new AxeBuilder({ page: tab }).withTags(WCAG_TAGS).analyze();
          const violations = axe.violations.map((v) => ({ id: v.id, help: v.help, nodes: v.nodes.length }));
          const m = await tab.evaluate(measure, tokens);
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
  console.log(`website-mocks: ${shots} shot${shots === 1 ? '' : 's'} of ${pages.length} page${pages.length === 1 ? '' : 's'}${args.check ? `, ${failed} with findings` : ''}`);
  return failed ? 1 : 0;
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  if (!existsSync(WEBSITE)) {
    console.error('website-mocks: docs/ux/website is missing');
    process.exit(1);
  }
  main(process.argv.slice(2)).then((code) => process.exit(code), (e) => {
    console.error(`website-mocks: ${e.stack || e}`);
    process.exit(1);
  });
}
