// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The lint of the website forms' stylesheets (ADR-0015, ADR-0106), shared by the mock checker of Phase 2
 * (scripts/website-mocks.mjs) and the size check of the shipped themes (scripts/size-check.mjs).
 *
 * A stylesheet is scoped when every selector at its top level, inside @media, @container, @supports and @layer
 * included, starts with its scope: `.jfb-form` for the base rules and the mocks, `.jfb-form:where(.jfb-theme-<alias>)`
 * for a theme's own stylesheet, so a theme styles only its own forms. A rule nested in a scoped rule is inside its
 * scope. Nothing may be physical: no left or right in a property name or as a float, clear or text-align value, no
 * four-value shorthand whose sides differ, and no border-radius shorthand with different corners, so right to left
 * mirrors every rule.
 */

/** The scope of a theme's own stylesheet. */
export function themeScope(alias) {
  return `.jfb-form:where(.jfb-theme-${alias})`;
}

/** Splits a CSS value on whitespace outside parentheses. */
export function cssWords(value) {
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

const SHORTHANDS = new Set(['margin', 'padding', 'inset', 'border-width', 'border-style', 'border-color', 'scroll-margin', 'scroll-padding']);

/**
 * Lints a stylesheet: the top-level selectors that do not start with the scope, and the physical declarations.
 * Returns { unscoped, physical }, each without repeats.
 */
export function lintStylesheet(css, scope = '.jfb-form') {
  const text = css.replace(/\/\*[\s\S]*?\*\//g, '');
  const scoped = new RegExp(`^${scope.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?![\\w-])`);
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
        for (const p of parts.map((x) => x.trim().replace(/\s+/g, ' '))) if (!scoped.test(p)) unscoped.push(p);
        stack.push('rule');
      }
    } else if (ch === '}') {
      stack.pop();
      prelude = '';
    } else if (ch === ';' && ctx !== 'rule') {
      prelude = '';
    } else prelude += ch;
  }
  for (const m of text.matchAll(/(?:^|[;{\s])([a-z-]+)\s*:\s*([^;{}]+)/g)) {
    const [prop, rawValue] = [m[1], m[2].trim()];
    if (prop.startsWith('--')) continue;
    const value = rawValue.replace(/\s*!important$/, '');
    const where = `${prop}: ${value}`;
    if (/(^|-)(left|right)(-|$)/.test(prop)) physical.push(where);
    else if (['float', 'clear', 'text-align'].includes(prop) && /^(left|right)$/.test(value)) physical.push(where);
    else if (SHORTHANDS.has(prop)) {
      const v = cssWords(value);
      if (v.length === 4 && v[1] !== v[3]) physical.push(where);
    } else if (prop === 'border-radius' && new Set(cssWords(value.split('/')[0])).size > 1) physical.push(where);
  }
  return { unscoped: [...new Set(unscoped)], physical: [...new Set(physical)] };
}
