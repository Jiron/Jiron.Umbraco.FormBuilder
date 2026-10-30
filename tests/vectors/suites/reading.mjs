// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The reading stage of ROADMAP 6.4a (ADR-0058, ADR-0109): a visitor's answer to one field cleaned as the pipeline
// cleans it, held to the longest answer its built-in type takes, and read by the type into the wire form of ADR-0005,
// before any rule or check sees it. A `read` case holds the field (its type, its public settings as the projection
// writes them, a grid's rows) and the answer as posted (its texts, and its parts as pairs of a name and texts, in the
// order posted), and expects `{ limit, values, unreadable }`: `maxLength` or `invalid` for an answer over the limits,
// which is then not read; each value read with the grid row it answers and the text the entry stores instead; and
// whether the type could not read it. A `rowCount` case holds a repeatable group and its number of rows, and expects
// its errors. A field starts from the public settings its type has defaults for, read from the reviewed snapshot of
// the field types endpoint (docs/api/field-types.json), as the projection writes them; a case adds its own. Every
// expectation is written out here, never computed by the code under test. Special characters are built from code
// points, so this file stays ASCII.
import { readFileSync } from 'node:fs';

const served = JSON.parse(readFileSync(new URL('../../../docs/api/field-types.json', import.meta.url), 'utf8')).types;

/** Text with code points written as {hex}, so this file stays ASCII. */
const x = (text) => text.replace(/\{([0-9a-f]{2,6})\}/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)));

/** A field of a built-in type as the form endpoint shows it: the public settings its type has defaults for, then the case's own. */
function field(type, settings = {}, rows) {
  const found = served.find((each) => each.alias === type);
  if (found === undefined) throw new Error(`no built-in type ${type}`);
  const effective = {};
  for (const setting of found.settings) {
    if (setting.isPublic && setting.default !== null) effective[setting.name] = setting.default;
  }
  return { type, settings: { ...effective, ...settings }, ...(rows === undefined ? {} : { rows: rows.map((id) => ({ id })) }) };
}

/** A read case: the field, the answer's texts and its parts as `[name, texts]` pairs. */
const read = (id, f, values, parts, expect) => ({
  id,
  read: { field: f, answer: { values, ...(parts === undefined ? {} : { parts }) } },
  expect,
});

/** Read into these values, each a text or `{ text, row?, stored? }`. */
const reads = (...values) => ({
  limit: null,
  values: values.map((value) => (typeof value === 'string' ? { text: value } : value)),
  unreadable: false,
});
const nothing = reads();
const unreadable = { limit: null, values: [], unreadable: true };
const overLimit = (limit) => ({ limit, values: [], unreadable: false });

const ROW_1 = '3f6a1c2e-0b7d-4e5a-9c18-6d2f4b8a0e11';
const ROW_2 = '3f6a1c2e-0b7d-4e5a-9c18-6d2f4b8a0e12';
const HASH = '9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08';
const many = (count, make) => Array.from({ length: count }, (_, i) => make(i));

const cleaning = [
  read('cleaning-trims-each-text', field('text'), ['  Anna  '], undefined, reads('Anna')),
  read('cleaning-drops-empty-texts', field('text'), ['   ', 'Anna', ''], undefined, reads('Anna')),
  read('cleaning-composes-to-nfc', field('text'), [x('Jose{301}')], undefined, reads(x('Jos{e9}'))),
  read('cleaning-keeps-inner-spaces', field('text'), ['Anna  Muster'], undefined, reads('Anna  Muster')),
  read('an-empty-answer-reads-nothing', field('text'), [], undefined, nothing),
  read('ranking-a-part-without-a-name-is-left-out', field('ranking'), ['a'], [['  ', ['1']]], reads('a')),
  read('ranking-a-part-without-texts-is-left-out', field('ranking'), ['a'], [['b', ['  ']]], reads('a')),
  read(
    'parts-whose-names-clean-alike-are-one-part',
    field('name', { parts: ['first', 'last'] }),
    [],
    [
      ['first ', ['Anna']],
      ['first', ['Bea']],
    ],
    unreadable,
  ),
  read(
    'a-part-without-a-name-is-left-out',
    field('name', { parts: ['first', 'last'] }),
    [],
    [
      ['  ', ['Anna']],
      ['first', ['Bea']],
    ],
    reads('{"first":"Bea"}'),
  ),
  read(
    'a-part-whose-texts-are-empty-is-left-out',
    field('email', { confirm: false }),
    ['anna@example.com'],
    [['confirm', ['  ']]],
    reads('anna@example.com'),
  ),
];

const limits = [
  read('text-at-its-longest', field('text'), ['a'.repeat(4000)], undefined, reads('a'.repeat(4000))),
  read('text-too-long-is-max-length', field('text'), ['a'.repeat(4001)], undefined, overLimit('maxLength')),
  read('text-given-twice-is-invalid', field('text'), ['Anna', 'Bea'], undefined, overLimit('invalid')),
  read('textarea-long-is-read', field('textarea'), ['a'.repeat(5000)], undefined, reads('a'.repeat(5000))),
  read('email-too-long-is-invalid', field('email'), ['a'.repeat(255)], undefined, overLimit('invalid')),
  read(
    'email-typed-twice-as-two-answers-is-unreadable',
    field('email', { confirm: true }),
    ['anna@example.com', 'bea@example.com'],
    undefined,
    unreadable,
  ),
  read('hidden-too-long-is-invalid', field('hidden'), ['a'.repeat(2001)], undefined, overLimit('invalid')),
  read(
    'email-with-its-address-typed-again',
    field('email', { confirm: true }),
    ['anna@example.com'],
    [['confirm', ['anna@example.com']]],
    reads('anna@example.com'),
  ),
  read(
    'email-typed-again-without-the-setting-is-invalid',
    field('email'),
    ['anna@example.com'],
    [['confirm', ['anna@example.com']]],
    overLimit('invalid'),
  ),
  read('phone-too-long-is-invalid', field('phone'), ['1'.repeat(65)], undefined, overLimit('invalid')),
  read('url-too-long-is-invalid', field('url'), ['https://example.com/' + 'a'.repeat(2029)], undefined, overLimit('invalid')),
  read('url-at-its-longest', field('url'), ['https://example.com/' + 'a'.repeat(2028)], undefined, reads('https://example.com/' + 'a'.repeat(2028))),
  read(
    'a-part-name-counts-to-the-longest-text',
    field('ranking'),
    [],
    [['a'.repeat(201), ['1']]],
    overLimit('invalid'),
  ),
];

const numbers = [
  read('number-as-the-wire-form', field('number'), ['12'], undefined, reads('12')),
  read('number-with-leading-zeros', field('number'), ['007'], undefined, reads('7')),
  read('number-minus-zero', field('number'), ['-0'], undefined, reads('0')),
  read('number-trailing-zeros-dropped', field('number'), ['1.50'], undefined, reads('1.5')),
  read('number-negative-with-trailing-zeros', field('number'), ['-12.340'], undefined, reads('-12.34')),
  read('number-point-first', field('number'), ['.5'], undefined, reads('0.5')),
  read('number-with-an-exponent', field('number'), ['1e3'], undefined, reads('1000')),
  read('number-with-a-plus-exponent', field('number'), ['1e+2'], undefined, reads('100')),
  read('number-with-a-negative-exponent', field('number'), ['1.5E-3'], undefined, reads('0.0015')),
  read('number-negative-with-an-exponent', field('number'), ['-0.5e1'], undefined, reads('-5')),
  read('number-zero-with-zeros', field('number'), ['0.000'], undefined, reads('0')),
  read('number-zero-with-a-huge-exponent', field('number'), ['0e999999999'], undefined, reads('0')),
  read('number-with-spaces-is-cleaned', field('number'), [' 12 '], undefined, reads('12')),
  read('number-with-a-plus-is-unreadable', field('number'), ['+5'], undefined, unreadable),
  read('number-with-a-comma-is-unreadable', field('number'), ['1,5'], undefined, unreadable),
  read('number-ending-in-a-point-is-unreadable', field('number'), ['5.'], undefined, unreadable),
  read('number-of-only-an-exponent-is-unreadable', field('number'), ['e3'], undefined, unreadable),
  read('number-with-an-empty-exponent-is-unreadable', field('number'), ['1e'], undefined, unreadable),
  read('number-with-letters-is-unreadable', field('number'), ['12abc'], undefined, unreadable),
  read('number-infinity-is-unreadable', field('number'), ['Infinity'], undefined, unreadable),
  read('number-of-sixty-digits', field('number'), ['1' + '0'.repeat(59)], undefined, reads('1' + '0'.repeat(59))),
  read('number-of-sixty-one-digits-is-unreadable', field('number'), ['1' + '0'.repeat(60)], undefined, unreadable),
  read('number-sixty-places-after-the-point', field('number'), ['1e-60'], undefined, reads('0.' + '0'.repeat(59) + '1')),
  read('number-sixty-one-places-is-unreadable', field('number'), ['1e-61'], undefined, unreadable),
  read('number-huge-exponent-is-unreadable', field('number'), ['1e999999999'], undefined, unreadable),
  read('number-too-long-as-posted-is-invalid', field('number'), ['1'.repeat(65)], undefined, overLimit('invalid')),
  read('rating-reads-as-a-number', field('rating'), ['3'], undefined, reads('3')),
  read('scale-reads-zero', field('scale'), ['0'], undefined, reads('0')),
  read('slider-reads-as-a-number', field('slider'), ['50.0'], undefined, reads('50')),
  read('calculation-takes-no-answer', field('calculation', { formula: '1' }), ['12'], undefined, nothing),
];

const dates = [
  read('date-as-the-wire-form', field('date'), ['2026-06-15'], undefined, reads('2026-06-15')),
  read('date-that-is-no-date-is-unreadable', field('date'), ['2026-02-30'], undefined, unreadable),
  read('date-without-zeros-is-unreadable', field('date'), ['2026-6-15'], undefined, unreadable),
  read('date-too-long-is-invalid', field('date'), ['2026-06-150'], undefined, overLimit('invalid')),
  read('time-to-the-minute', field('time'), ['09:30'], undefined, reads('09:30')),
  read('time-to-the-second', field('time'), ['09:30:15'], undefined, reads('09:30:15')),
  read('time-past-midnight-is-unreadable', field('time'), ['24:00'], undefined, unreadable),
  read('time-too-long-is-invalid', field('time'), ['09:30:150'], undefined, overLimit('invalid')),
  read('datetime-as-the-wire-form', field('datetime'), ['2026-06-15T09:30'], undefined, reads('2026-06-15T09:30')),
  read('datetime-with-a-space-is-unreadable', field('datetime'), ['2026-06-15 09:30'], undefined, unreadable),
  read('datetime-too-long-is-invalid', field('datetime'), ['2026-06-15T09:30:150'], undefined, overLimit('invalid')),
];

const choices = [
  read('dropdown-one-option', field('dropdown'), ['a'], undefined, reads('a')),
  read('dropdown-two-options-is-invalid', field('dropdown'), ['a', 'b'], undefined, overLimit('invalid')),
  read('dropdown-of-several-reads-each', field('dropdown', { multiple: true }), ['a', 'b'], undefined, reads('a', 'b')),
  read('dropdown-of-several-one-twice-is-unreadable', field('dropdown', { multiple: true }), ['a', 'a'], undefined, unreadable),
  read('dropdown-option-too-long-is-invalid', field('dropdown'), ['a'.repeat(201)], undefined, overLimit('invalid')),
  read('radio-one-option', field('radio'), ['a'], undefined, reads('a')),
  read('radio-other-without-the-setting-is-invalid', field('radio'), ['a'], [['other', ['Mine']]], overLimit('invalid')),
  read('radio-other-in-place-of-an-option', field('radio', { other: true }), [], [['other', ['Mine']]], reads('Mine')),
  read('radio-other-beside-an-option-is-ignored', field('radio', { other: true }), ['a'], [['other', ['Mine']]], reads('a')),
  read('radio-two-options-with-other-on-is-unreadable', field('radio', { other: true }), ['a', 'b'], undefined, unreadable),
  read('radio-other-too-long-is-invalid', field('radio', { other: true }), [], [['other', ['a'.repeat(2001)]]], overLimit('invalid')),
  read('radio-other-longer-than-an-option', field('radio', { other: true }), [], [['other', ['a'.repeat(500)]]], reads('a'.repeat(500))),
  read('checkboxes-other-longer-than-an-option', field('checkboxes', { other: true }), [], [['other', ['a'.repeat(500)]]], reads('a'.repeat(500))),
  read(
    'checkboxes-options-then-other',
    field('checkboxes', { other: true }),
    ['a', 'b'],
    [['other', ['Mine']]],
    reads('a', 'b', 'Mine'),
  ),
  read('checkboxes-other-repeating-an-option-is-ignored', field('checkboxes', { other: true }), ['a'], [['other', ['a']]], reads('a')),
  read('checkboxes-other-without-the-setting-is-ignored', field('checkboxes'), ['a'], [['other', ['Mine']]], reads('a')),
  read('checkboxes-an-option-twice-is-unreadable', field('checkboxes'), ['a', 'a'], undefined, unreadable),
  read(
    'checkboxes-two-other-texts-is-unreadable',
    field('checkboxes', { other: true }),
    [],
    [['other', ['Mine', 'Yours']]],
    unreadable,
  ),
  read(
    'grid-each-row-its-column',
    field('grid', {}, [ROW_1, ROW_2]),
    [],
    [
      [ROW_2, ['2']],
      [ROW_1, ['1']],
    ],
    reads({ text: '1', row: ROW_1 }, { text: '2', row: ROW_2 }),
  ),
  read('grid-two-columns-in-a-row-is-unreadable', field('grid', {}, [ROW_1, ROW_2]), [], [[ROW_1, ['1', '2']]], unreadable),
  read(
    'grid-of-several-reads-each-column',
    field('grid', { multiple: true }, [ROW_1, ROW_2]),
    [],
    [[ROW_1, ['1', '2']]],
    reads({ text: '1', row: ROW_1 }, { text: '2', row: ROW_1 }),
  ),
  read(
    'grid-of-several-more-columns-than-rows',
    field('grid', { multiple: true }, [ROW_1, ROW_2]),
    [],
    [[ROW_1, ['1', '2', '3']]],
    reads({ text: '1', row: ROW_1 }, { text: '2', row: ROW_1 }, { text: '3', row: ROW_1 }),
  ),
  read('grid-of-several-a-column-twice-is-unreadable', field('grid', { multiple: true }, [ROW_1]), [], [[ROW_1, ['1', '1']]], unreadable),
  read('grid-a-part-of-no-row-is-ignored', field('grid', {}, [ROW_1]), [], [['other', ['1']]], nothing),
  read('grid-own-texts-are-ignored', field('grid', {}, [ROW_1, ROW_2]), ['1'], undefined, nothing),
  read('grid-without-rows-takes-no-answer', field('grid'), ['1'], undefined, nothing),
  read('ranking-in-its-order', field('ranking'), ['b', 'a', 'c'], undefined, reads('b', 'a', 'c')),
  read(
    'ranking-places-ordered',
    field('ranking'),
    [],
    [
      ['a', ['2']],
      ['b', ['1']],
      ['c', ['3']],
    ],
    reads('b', 'a', 'c'),
  ),
  read(
    'ranking-places-shared-keep-the-values-order',
    field('ranking'),
    [],
    [
      ['b', ['1']],
      ['a', ['1']],
    ],
    reads('a', 'b'),
  ),
  read(
    'ranking-values-ordered-as-text',
    field('ranking'),
    [],
    [
      ['9', ['1']],
      ['10', ['1']],
    ],
    reads('10', '9'),
  ),
  read('ranking-place-with-leading-zeros', field('ranking'), [], [['a', ['01']]], reads('a')),
  read('ranking-place-followed-by-nul', field('ranking'), [], [['a', [x('1{00}')]]], reads('a')),
  read('ranking-place-zero-is-unreadable', field('ranking'), [], [['a', ['0']]], unreadable),
  read('ranking-place-negative-is-unreadable', field('ranking'), [], [['a', ['-1']]], unreadable),
  read('ranking-place-at-the-largest', field('ranking'), [], [['a', ['2147483647']]], reads('a')),
  read('ranking-place-at-the-largest-with-leading-zeros', field('ranking'), [], [['a', ['002147483647']]], reads('a')),
  read('ranking-place-past-the-largest-is-unreadable', field('ranking'), [], [['a', ['2147483648']]], unreadable),
  read('ranking-place-of-letters-is-unreadable', field('ranking'), [], [['a', ['first']]], unreadable),
  read('ranking-place-of-digits-then-letters-is-unreadable', field('ranking'), [], [['a', ['1st']]], unreadable),
  read('ranking-place-with-a-point-is-unreadable', field('ranking'), [], [['a', ['1.0']]], unreadable),
  read('ranking-two-places-is-unreadable', field('ranking'), [], [['a', ['1', '2']]], unreadable),
  read('ranking-order-and-places-at-once-is-unreadable', field('ranking'), ['a'], [['b', ['1']]], unreadable),
];

const boxes = [
  read('checkbox-ticked', field('checkbox'), ['true'], undefined, reads('true')),
  read('checkbox-anything-else-is-unticked', field('checkbox'), ['false'], undefined, nothing),
  read('checkbox-a-word-is-unticked', field('checkbox'), ['yes'], undefined, nothing),
  read('checkbox-too-long-is-invalid', field('checkbox'), ['truest'], undefined, overLimit('invalid')),
  read('yesno-yes', field('yesno'), ['yes'], undefined, reads('yes')),
  read('yesno-no', field('yesno'), ['no'], undefined, reads('no')),
  read('yesno-another-word-is-unreadable', field('yesno'), ['ja'], undefined, unreadable),
  read('yesno-too-long-is-invalid', field('yesno'), ['maybe'], undefined, overLimit('invalid')),
  read('consent-ticked-with-its-wording', field('consent'), ['true'], [['wording', [HASH]]], reads({ text: 'true', stored: HASH })),
  read('consent-ticked-without-its-wording-is-unreadable', field('consent'), ['true'], undefined, unreadable),
  read(
    'consent-wording-in-capitals-is-unreadable',
    field('consent'),
    ['true'],
    [['wording', [HASH.toUpperCase()]]],
    unreadable,
  ),
  read('consent-not-ticked-with-its-wording', field('consent'), [], [['wording', [HASH]]], nothing),
  read('consent-anything-but-a-tick', field('consent'), ['false'], [['wording', [HASH]]], nothing),
  read('consent-two-answers-is-unreadable', field('consent'), ['false', 'true'], undefined, unreadable),
  read('consent-wording-too-long-is-invalid', field('consent'), ['true'], [['wording', [HASH + '0']]], overLimit('invalid')),
  read('country-a-code', field('country'), ['CH'], undefined, reads('CH')),
  read('country-kosovo', field('country'), ['XK'], undefined, reads('XK')),
  read('country-in-small-letters-is-unreadable', field('country'), ['ch'], undefined, unreadable),
  read('country-a-reserved-code-is-unreadable', field('country'), ['EU'], undefined, unreadable),
  read('country-of-three-letters-is-invalid', field('country'), ['CHE'], undefined, overLimit('invalid')),
];

const parts = [
  read(
    'name-parts-in-the-types-order',
    field('name', { parts: ['first', 'last'] }),
    [],
    [
      ['last', ['Muster']],
      ['first', ['Anna']],
    ],
    reads('{"first":"Anna","last":"Muster"}'),
  ),
  read(
    'name-a-part-not-asked-for-is-ignored',
    field('name', { parts: ['first', 'last'] }),
    [],
    [
      ['title', ['Dr']],
      ['first', ['Anna']],
    ],
    reads('{"first":"Anna"}'),
  ),
  read(
    'name-every-part',
    field('name', { parts: ['last', 'middle', 'first', 'title'] }),
    [],
    [
      ['title', ['Dr']],
      ['first', ['Anna']],
      ['middle', ['Maria']],
      ['last', ['Muster']],
    ],
    reads('{"title":"Dr","first":"Anna","middle":"Maria","last":"Muster"}'),
  ),
  read('name-a-part-too-long-is-invalid', field('name', { parts: ['first', 'last'] }), [], [['first', ['a'.repeat(201)]]], overLimit('invalid')),
  read('address-a-part-too-long-is-invalid', field('address'), [], [['city', ['a'.repeat(201)]]], overLimit('invalid')),
  read('name-without-parts-reads-nothing', field('name', { parts: ['first', 'last'] }), [], [], nothing),
  read(
    'address-always-asks-street-postcode-and-city',
    field('address'),
    [],
    [
      ['city', [x('Z{fc}rich')]],
      ['region', ['ZH']],
      ['street', ['Main "A" \\ 1']],
      ['postcode', ['8000']],
    ],
    reads(x('{"street":"Main \\"A\\" \\\\ 1","postcode":"8000","city":"Z{fc}rich"}')),
  ),
  read(
    'address-with-the-parts-asked-for',
    field('address', { parts: ['country', 'region'] }),
    [],
    [
      ['country', ['CH']],
      ['region', ['ZH']],
      ['city', ['Bern']],
    ],
    reads('{"city":"Bern","region":"ZH","country":"CH"}'),
  ),
];

const others = [
  read('file-each-token', field('file', { maxFiles: 3 }), ['t1', 't2'], undefined, reads('t1', 't2')),
  read('file-a-token-twice-is-unreadable', field('file', { maxFiles: 3 }), ['t1', 't1'], undefined, unreadable),
  read('file-more-than-any-field-takes-is-invalid', field('file'), many(21, (i) => `t${i}`), undefined, overLimit('invalid')),
  read('file-token-too-long-is-invalid', field('file'), ['t'.repeat(1025)], undefined, overLimit('invalid')),
  read('signature-one-token', field('signature'), ['t1'], undefined, reads('t1')),
  read('signature-two-tokens-is-invalid', field('signature'), ['t1', 't2'], undefined, overLimit('invalid')),
  read('hidden-reads-its-text', field('hidden'), ['web'], undefined, reads('web')),
  read('heading-takes-no-answer', field('heading', { text: { '': 'Hello' } }), ['x'], undefined, nothing),
  read('heading-takes-no-answer-however-many', field('heading', { text: { '': 'Hello' } }), ['x', 'y'], undefined, nothing),
  read('repeater-takes-no-answer-of-its-own', field('repeater'), ['x'], undefined, nothing),
];

const rowCount = (id, settings, count, expect) => ({ id, rowCount: { field: field('repeater', settings), count }, expect });
const rows = [
  rowCount('rows-fewer-than-the-least', { minRows: 2, maxRows: 3 }, 1, ['rowCount']),
  rowCount('rows-at-the-least', { minRows: 2, maxRows: 3 }, 2, []),
  rowCount('rows-at-the-most', { minRows: 2, maxRows: 3 }, 3, []),
  rowCount('rows-more-than-the-most', { minRows: 2, maxRows: 3 }, 4, ['rowCount']),
  rowCount('rows-none-by-default', {}, 0, []),
  rowCount('rows-past-the-default-most', {}, 11, ['rowCount']),
];

export default {
  suite: 'reading',
  defaults: {},
  fields: {},
  cases: [...cleaning, ...limits, ...numbers, ...dates, ...choices, ...boxes, ...parts, ...others, ...rows],
};
