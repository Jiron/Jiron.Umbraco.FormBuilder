// Dashed lines on the canvas run along square corners (ADR-0242). A dashed line around rounded corners is drawn as a
// path, which some graphics chips draw slowly: the dashed ring of the field under the pointer, rounded, made a move by
// the keyboard on a form of 300 fields take up to 112 ms to show, and the drag's rounded dashed slot its first moves
// 64 ms, against the designer's 100 ms per interaction. Square, both took half that.
import { describe, expect, it } from 'vitest';
import { canvasChrome } from './canvas.styles';

/** Each rule of a style text, nested or not, as its selector and its declarations. */
function rulesOf(text: string): { selector: string; block: string }[] {
  const rules: { selector: string; block: string }[] = [];
  const stripped = text.replace(/\/\*[\s\S]*?\*\//g, '');
  let at = stripped.indexOf('{');
  while (at !== -1) {
    const before = Math.max(
      stripped.lastIndexOf('}', at),
      stripped.lastIndexOf('{', at - 1),
      stripped.lastIndexOf(';', at),
    );
    const selector = stripped.slice(before + 1, at).trim();
    const next = stripped.indexOf('{', at + 1);
    const close = stripped.indexOf('}', at);
    // A block that opens another before it closes is a group of rules (@media, @container): its rules are read in turn.
    if (next === -1 || close < next) rules.push({ selector, block: stripped.slice(at + 1, close) });
    at = next;
  }
  return rules;
}

/** The classes of the elements a selector styles: those of the last part of each selector in its list. */
function subjectsOf(selector: string): string[] {
  const parts: string[] = [];
  let depth = 0;
  let current = '';
  const finish = () => {
    const last = current.trim();
    if (last) parts.push(last);
    current = '';
  };
  const lists: string[] = [];
  for (const char of selector) {
    if (char === '(') depth++;
    if (char === ')') depth--;
    if (depth === 0 && char === ',') {
      lists.push(current);
      current = '';
    } else current += char;
  }
  lists.push(current);
  for (const one of lists) {
    // The last compound: what follows the last combinator outside parentheses.
    let level = 0;
    current = '';
    for (const char of one.trim()) {
      if (char === '(') level++;
      if (char === ')') level--;
      if (level === 0 && /[\s>+~]/.test(char)) current = '';
      else current += char;
    }
    finish();
  }
  return parts.flatMap((part) => [...part.matchAll(/\.(bo-[a-z-]+)/g)].map((match) => match[1] ?? ''));
}

const DASHED = /(?:border|outline)(?:-[a-z-]+)?:\s*[^;]*\b(?:dashed|dotted)\b/;
const ROUNDED = (block: string) =>
  [...block.matchAll(/border(?:-[a-z]+)*-radius:\s*([^;]+);/g)].some((match) => match[1]?.trim() !== '0');

describe('dashed lines on the canvas', () => {
  it('reads each rule, those inside a group of rules too, and the elements a selector styles', () => {
    const rules = rulesOf('.a { color: red; } @media (hover: hover) { .b:hover { outline: 1px; } } /* .c { } */');
    expect(rules.map((rule) => rule.selector)).toEqual(['.a', '.b:hover']);
    expect(subjectsOf('.jfb-form .bo-select:hover')).toEqual(['bo-select']);
    expect(subjectsOf('.jfb-form .bo-field > :is(.bo-insert, .bo-empty-group), .bo-slot[data-sheet]')).toEqual([
      'bo-insert',
      'bo-empty-group',
      'bo-slot',
    ]);
    expect(ROUNDED('border-radius: 0;')).toBe(false);
    expect(ROUNDED('border-top-left-radius: 3px;')).toBe(true);
  });

  it('run along square corners, in every state of the element that draws them', () => {
    const rules = rulesOf(canvasChrome.cssText);
    const dashed = new Set(
      rules.filter((rule) => DASHED.test(rule.block)).flatMap((rule) => subjectsOf(rule.selector)),
    );
    // The selection's button (its ring under the pointer), the drag's slot, a hidden field's note and the places to add.
    for (const known of ['bo-select', 'bo-slot', 'bo-note', 'bo-empty-group', 'bo-add-field'])
      expect(dashed).toContain(known);
    const rounded = rules
      .filter((rule) => ROUNDED(rule.block) && subjectsOf(rule.selector).some((subject) => dashed.has(subject)))
      .map((rule) => rule.selector);
    expect(rounded).toEqual([]);
  });
});
