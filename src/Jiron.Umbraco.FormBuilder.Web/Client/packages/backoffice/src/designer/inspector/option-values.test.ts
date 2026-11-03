// A choice field's options as the inspector edits them (ROADMAP 9.4b, ADR-0172): a source's change keeps only what the
// new source holds, values come from labels and are compared as the server compares them, pasting skips repeats, a
// grid's rows get keys, and a parent is a choice field before the field.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition, DesignField } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import {
  cut,
  itemsOf,
  LINE_LENGTH,
  MOST_OPTIONS,
  MOST_ROWS,
  moved,
  newOption,
  parentsBefore,
  pasteOptions,
  pasteRows,
  repeats,
  rowKeys,
  rowsOf,
  showsCards,
  sourceOf,
  valueFor,
  withItems,
  withOptionProperty,
  withRows,
  withSource,
  type DesignOption,
} from './option-values';

const field: DesignField = {
  key: 'f',
  type: 'dropdown',
  alias: 'colour',
  options: { items: [{ value: 'red', label: { '': 'Red' } }], parent: 'p' },
};

describe("a field's options", () => {
  it('come from its own items unless it says otherwise, and a new source keeps only what it holds and the parent', () => {
    expect([sourceOf(field), itemsOf(field)]).toEqual(['items', [{ value: 'red', label: { '': 'Red' } }]]);
    expect(withSource(field, 'list')['options']).toEqual({ source: 'list', parent: 'p' });
    expect(withSource(field, 'code')['options']).toEqual({ source: 'code', config: {}, parent: 'p' });
    expect(withSource(withSource(field, 'code'), 'items')['options']).toEqual({ parent: 'p' });
    const plain: DesignField = { key: 'g', type: 'radio', alias: 'g' };
    expect(withSource(withSource(plain, 'list'), 'items')).toEqual(plain);
  });

  it('take a value from the label, as entries store it, and the next free "option n" where it repeats or is empty', () => {
    const items: DesignOption[] = [{ value: 'Red' }, { value: 'option3' }];
    expect(valueFor('  Blue  ', items)).toBe('Blue');
    expect(valueFor('red', items)).toBe('option4');
    expect(valueFor('   ', [])).toBe('option1');
    expect(valueFor('x'.repeat(250), [])).toHaveLength(200);
    expect(repeats([{ value: 'STRASSE' }], 'strasse')).toBe(true);
    // A sharp s folds to itself, not to ss, as the server folds it.
    expect(repeats([{ value: `Stra${String.fromCharCode(0xdf)}e` }], 'strasse')).toBe(false);
  });

  it('write a new label in the language shown, the default kept empty in another', () => {
    expect(newOption('Red', [], 'en-US', true)).toEqual({ value: 'Red', label: { '': 'Red' } });
    expect(newOption('Rot', [], 'de-DE', false)).toEqual({ value: 'Rot', label: { '': '', 'de-DE': 'Rot' } });
  });

  it('are pasted one a line, blank lines skipped and repeats counted, never added twice', () => {
    const pasted = pasteOptions('Red\n\nGreen\r\ngreen\nBlue\nred', itemsOf(field), 'en-US', true);
    expect(pasted.items.map((item) => item.value)).toEqual(['red', 'Green', 'Blue']);
    // "Red" and "red" repeat the option there, "green" the line before it.
    expect([pasted.added, pasted.repeated]).toEqual([2, 3]);
  });

  it('are cut as the server counts, never inside a character, and pasted at every line break it knows', () => {
    // A face is two UTF-16 units: a cut through it leaves it out whole.
    const face = String.fromCodePoint(0x1f600);
    expect(cut('ab' + face, 3)).toBe('ab');
    expect(cut('ab' + face, 4)).toBe('ab' + face);
    expect(valueFor('x'.repeat(199) + face, [])).toBe('x'.repeat(199));
    const breaks = [0x85, 0x2028, 0x2029].map((code) => String.fromCharCode(code));
    const pasted = pasteOptions(['One', 'Two', 'Three', 'Four'].join(breaks.join('')), [], 'en-US', true);
    expect(pasted.items.map((item) => item.value)).toEqual(['One', 'Two', 'Three', 'Four']);
    const long = pasteOptions('y'.repeat(LINE_LENGTH + 20), [], 'en-US', true).items[0];
    expect(long?.label?.['']).toHaveLength(LINE_LENGTH);
    expect(long?.value).toHaveLength(200);
  });

  it("stop at the server's default limits: 1,000 options of a field's own and 300 rows of a grid", () => {
    const lines = Array.from({ length: MOST_OPTIONS + 5 }, (_, at) => `Option ${at}`).join(String.fromCharCode(10));
    const pasted = pasteOptions(lines, [{ value: 'first' }], 'en-US', true);
    expect([pasted.items.length, pasted.added]).toEqual([MOST_OPTIONS, MOST_OPTIONS - 1]);
    let n = 0;
    const rows = pasteRows(lines, [{ key: 'r0' }], 'en-US', true, () => `k${++n}`);
    expect(rows).toHaveLength(MOST_ROWS);
  });

  it('are drawn by keys of their own, a repeated value told apart', () => {
    const keys = rowKeys(['red', 'blue', 'red']);
    expect(new Set(keys).size).toBe(3);
    expect(rowKeys(['red', 'blue', 'red'])).toEqual(keys);
  });

  it("show as cards by their own layout, or the form's where they inherit", () => {
    const radio = builtInTypes['radio'];
    if (!radio) throw new Error('no radio type');
    const form = (choiceLayout?: string): DesignDefinition => ({
      pages: [],
      ...(choiceLayout ? { settings: { appearance: { choiceLayout } } } : {}),
    });
    const of = (layout?: string): DesignField => ({
      key: 'r',
      type: 'radio',
      ...(layout ? { settings: { layout } } : {}),
    });
    expect(showsCards(of('cards'), radio, form())).toBe(true);
    expect(showsCards(of(), radio, form('cards'))).toBe(true);
    expect(showsCards(of('inherit'), radio, form('cards'))).toBe(true);
    expect(showsCards(of('vertical'), radio, form('cards'))).toBe(false);
    expect(showsCards(of(), radio, form())).toBe(false);
    const ranking = builtInTypes['ranking'];
    if (!ranking) throw new Error('no ranking type');
    expect(showsCards({ key: 'k', type: 'ranking', settings: { layout: 'cards' } }, ranking, form('cards'))).toBe(
      false,
    );
  });

  it('move, change one property, and leave an empty list out', () => {
    expect(moved(['a', 'b', 'c'], 0, 2)).toEqual(['b', 'c', 'a']);
    expect(moved(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b']);
    expect(moved(['a'], 3, 0)).toEqual(['a']);
    expect(withOptionProperty({ value: 'a', places: 3 }, 'places', undefined)).toEqual({ value: 'a' });
    expect(withOptionProperty({ value: 'a' }, 'parentValues', [])).toEqual({ value: 'a' });
    expect(withItems(field, [])['options']).toEqual({ parent: 'p' });
  });

  it("give a grid's pasted rows keys of their own, and leave no rows out of a field without any", () => {
    let n = 0;
    const rows = pasteRows('Food\n\nService', [], 'en-US', true, () => `k${++n}`);
    expect(rows).toEqual([
      { key: 'k1', label: { '': 'Food' } },
      { key: 'k2', label: { '': 'Service' } },
    ]);
    expect(rowsOf(withRows(field, rows))).toEqual(rows);
    expect(Object.hasOwn(withRows(withRows(field, rows), []), 'rows')).toBe(false);
  });

  it('depend only on a choice field before them', () => {
    const fields: DesignField[] = [
      { key: 'a', type: 'radio', alias: 'a' },
      { key: 'b', type: 'text', alias: 'b' },
      { key: 'f', type: 'dropdown', alias: 'f' },
      { key: 'c', type: 'radio', alias: 'c' },
    ];
    const choice = (each: DesignField) => each.type === 'radio' || each.type === 'dropdown';
    expect(parentsBefore(fields, 'f', choice).map((each) => each.key)).toEqual(['a']);
    expect(parentsBefore(fields, 'missing', choice)).toEqual([]);
  });
});
