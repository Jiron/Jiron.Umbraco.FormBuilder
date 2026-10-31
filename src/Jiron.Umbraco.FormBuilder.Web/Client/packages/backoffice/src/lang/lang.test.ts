// The backoffice's dictionaries (ADR-0057): English and German hold the same keys, each a text that is not blank (or
// a function of the values that gives one); they hold every `#` key the management API serves for the built-in field
// types, as docs/api/field-types.json (the snapshot an integration test holds the endpoint to) has them; they hold
// every `#jfbPicker_` key the form picker's checks send (ADR-0129), and its area no other; the form picker's screens
// (ADR-0134) hold the keys their elements use, and no other; and the field types' areas hold no key nothing uses.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import de from './de';
import en from './en';

type Dictionary = Record<string, Record<string, unknown>>;

/** The repository root: the nearest folder above this file that holds the solution. */
function repositoryRoot(): string {
  let folder = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) {
    const parent = dirname(folder);
    if (parent === folder) throw new Error('No folder above the dictionaries holds the solution');
    folder = parent;
  }
  return folder;
}

/** Every key of a dictionary as a term names it, `<area>_<key>`, sorted. */
const termsOf = (dictionary: Dictionary): string[] =>
  Object.entries(dictionary)
    .flatMap(([area, texts]) => Object.keys(texts).map((key) => `${area}_${key}`))
    .sort();

/** Every string of a JSON value that is a `#` key, without its `#`. */
function keysIn(value: unknown, found = new Set<string>()): Set<string> {
  if (typeof value === 'string' && value.startsWith('#')) found.add(value.slice(1));
  else if (Array.isArray(value)) value.forEach((item) => keysIn(item, found));
  else if (value !== null && typeof value === 'object') Object.values(value).forEach((item) => keysIn(item, found));
  return found;
}

const served = keysIn(JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'field-types.json'), 'utf8')));

/** The `#jfbPicker_` keys the form picker's checks send, read from their source. */
const pickerKeys = new Set(
  [
    ...readFileSync(
      join(repositoryRoot(), 'src', 'Jiron.Umbraco.FormBuilder.Web', 'Picker', 'FormPickerEditors.cs'),
      'utf8',
    ).matchAll(/"#(jfbPicker_[A-Za-z]+)"/g),
  ].map((match) => match[1]),
);

/** The `jfbFormPicker_` keys the form picker's elements use, read from their sources. */
const pickerFolder = join(dirname(fileURLToPath(import.meta.url)), '..', 'picker');
const screenKeys = new Set(
  readdirSync(pickerFolder)
    .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
    .flatMap((file) => [...readFileSync(join(pickerFolder, file), 'utf8').matchAll(/'(jfbFormPicker_[A-Za-z]+)'/g)])
    .map((match) => match[1]),
);

/** A dictionary's text as a person reads it: a function is given sample values (a name, a number). */
const shown = (text: unknown): unknown => (typeof text === 'function' ? text('Contact us', 2) : text);

/** The areas of the field types' texts. */
const fieldTypeAreas = [
  'jfbFieldTypes',
  'jfbFieldPalette',
  'jfbFieldSettingGroups',
  'jfbFieldSettings',
  'jfbFieldChoices',
  'jfbFieldTypeAvailability',
];

/** Keys the server sends that the built-in types' snapshot cannot show: FieldTypeAvailabilityService.CheckFailed. */
const servedOtherwise = ['jfbFieldTypeAvailability_checkFailed'];

describe('the dictionaries', () => {
  it('hold the same keys in English and German, each a text that is not blank', () => {
    expect(termsOf(de)).toEqual(termsOf(en));
    for (const dictionary of [en, de] as Dictionary[]) {
      for (const [area, texts] of Object.entries(dictionary)) {
        for (const [key, text] of Object.entries(texts)) {
          const value = shown(text);
          expect(typeof value === 'string' && value.trim().length > 0, `${area}_${key}`).toBe(true);
        }
      }
    }
  });

  it('hold every key the built-in field types are served with', () => {
    expect(served.size).toBeGreaterThan(150);
    expect([...served].filter((key) => !termsOf(en).includes(key))).toEqual([]);
  });

  it("hold every key the form picker's checks send, and no other in its area", () => {
    expect(pickerKeys.size).toBeGreaterThanOrEqual(5);
    expect(termsOf(en).filter((term) => term.startsWith('jfbPicker_'))).toEqual([...pickerKeys].sort());
  });

  it("hold the form picker screens' keys that their elements use, and no other in that area", () => {
    expect(screenKeys.size).toBeGreaterThanOrEqual(20);
    expect(termsOf(en).filter((term) => term.startsWith('jfbFormPicker_'))).toEqual([...screenKeys].sort());
  });

  it('hold no key of the field types that nothing serves', () => {
    const unused = termsOf(en).filter(
      (term) =>
        fieldTypeAreas.includes(term.slice(0, term.indexOf('_'))) &&
        !served.has(term) &&
        !servedOtherwise.includes(term),
    );
    expect(unused).toEqual([]);
    expect(termsOf(en)).toEqual(expect.arrayContaining(servedOtherwise));
  });

  it('give each culture a different text where the words differ', () => {
    // A copied English text would pass the key checks; most labels differ between the languages.
    const same = termsOf(en).filter((term) => {
      const [area, key] = [term.slice(0, term.indexOf('_')), term.slice(term.indexOf('_') + 1)];
      return shown((en as Dictionary)[area]?.[key]) === shown((de as Dictionary)[area]?.[key]);
    });
    expect(same.length).toBeLessThan(termsOf(en).length / 10);
  });
});
