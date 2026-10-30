// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// A page's post read by the names its controls carry (ROADMAP 6.4b, ADR-0058, ADR-0100, ADR-0110): the twin of the
// no-JS post's reader in the browser. A `post` case holds the form's fields as the page names them (alias, type,
// settings, a repeatable group's child fields) and the page's names and values in the order a browser posts them, and
// expects `{ fields, rows, context, query }`: each field's answer by its alias as `{ values, parts? }` (parts as pairs
// of a name and texts, in the order first posted), each repeatable group's rows of its children's answers, and the
// context and query values by name. Every expectation is written out here, never computed by the code under test.
// Special characters are built from code points, so this file stays ASCII.

/** Text with code points written as {hex}, so this file stays ASCII. */
const x = (text) => text.replace(/\{([0-9a-f]{2,6})\}/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)));

const field = (alias, type, settings, fields) => ({
  alias,
  type,
  ...(settings === undefined ? {} : { settings }),
  ...(fields === undefined ? {} : { fields }),
});

// The form most cases post: a field of each kind of name, and a repeatable group with a child of each kind.
const form = [
  field('name', 'text'),
  field('story', 'textarea'),
  field('topics', 'checkboxes'),
  field('fullName', 'name'),
  field('updates', 'yesno', { style: 'switch' }),
  field('answer', 'yesno', { style: 'buttons' }),
  field('terms', 'consent'),
  field('sign', 'signature'),
  field('rank', 'ranking'),
  field('fav', 'colour'),
  field(null, 'heading', { level: 2 }),
  field('guests', 'repeater', { minRows: 0, maxRows: 10 }, [
    field('guest', 'text'),
    field('who', 'name'),
    field('vegan', 'yesno', { style: 'switch' }),
    field('agree', 'consent'),
    field('pick', 'ranking'),
    field('sketch', 'signature'),
  ]),
];
// A form with no switch, so a case's expectation names only what it posts.
const plain = form.filter((each) => each.type !== 'yesno' || each.settings?.style !== 'switch').map((each) =>
  each.alias === 'guests' ? { ...each, fields: each.fields.filter((child) => child.alias !== 'vegan') } : each,
);

/** A post case over the plain form unless it gives its own fields. */
const post = (id, entries, expect, fields = plain) => ({ id, post: { fields, entries }, expect: { fields: {}, rows: {}, context: {}, query: {}, ...expect } });
const answer = (values, parts) => ({ values, ...(parts === undefined ? {} : { parts }) });
const hash = 'a'.repeat(64);
const empty = (count) => Array.from({ length: count }, () => ({}));

const texts = [
  post('a-text-by-its-alias', [['name', 'Ada']], { fields: { name: answer(['Ada']) } }),
  post('several-texts-in-the-order-posted', [['topics', 'b'], ['topics', 'a']], { fields: { topics: answer(['b', 'a']) } }),
  post(
    'two-fields-texts-interleaved',
    [['topics', 'a'], ['name', 'Ada'], ['topics', 'b']],
    { fields: { topics: answer(['a', 'b']), name: answer(['Ada']) } },
  ),
  post('an-empty-text-is-posted-for-the-pipeline-to-clean', [['name', '']], { fields: { name: answer(['']) } }),
  post('a-text-twice-is-kept-twice', [['name', 'Ada'], ['name', 'Ada']], { fields: { name: answer(['Ada', 'Ada']) } }),
  post('spaces-are-kept-for-the-pipeline-to-clean', [['name', '  Ada  ']], { fields: { name: answer(['  Ada  ']) } }),
  post(
    'each-crlf-is-sent-as-lf',
    [['story', x('One{0d}{0a}two{0d}{0a}{0d}{0a}three')]],
    { fields: { story: answer([x('One{0a}two{0a}{0a}three')]) } },
  ),
  post('a-lone-cr-and-a-lone-lf-are-kept', [['story', x('a{0d}b{0a}c')]], { fields: { story: answer([x('a{0d}b{0a}c')]) } }),
  post('a-cr-after-a-crlf-is-kept', [['story', x('a{0d}{0d}{0a}b')]], { fields: { story: answer([x('a{0d}{0a}b')]) } }),
  post('other-characters-are-kept', [['name', x('Zo{eb} {1f600}')]], { fields: { name: answer([x('Zo{eb} {1f600}')]) } }),
];

const parts = [
  post(
    'parts-by-their-names-in-the-order-posted',
    [['fullName.last', 'Lovelace'], ['fullName.first', 'Ada']],
    { fields: { fullName: answer([], [['last', ['Lovelace']], ['first', ['Ada']]]) } },
  ),
  post('a-part-twice-is-kept-twice', [['fullName.first', 'A'], ['fullName.first', 'B']], { fields: { fullName: answer([], [['first', ['A', 'B']]]) } }),
  post('a-part-after-the-first-dot-keeps-its-dots', [['fullName.a.b', 'x']], { fields: { fullName: answer([], [['a.b', ['x']]]) } }),
  post('a-name-ending-in-a-dot-is-the-fields-own-text', [['fullName.', 'dot']], { fields: { fullName: answer(['dot']) } }),
  post(
    'texts-and-parts-together',
    [['fullName', 'own'], ['fullName.first', 'Ada']],
    { fields: { fullName: answer(['own'], [['first', ['Ada']]]) } },
  ),
  post('a-part-name-is-case-sensitive', [['fullName.First', 'Ada']], { fields: { fullName: answer([], [['First', ['Ada']]]) } }),
  post('crlf-in-a-part-is-sent-as-lf', [['fullName.first', x('a{0d}{0a}b')]], { fields: { fullName: answer([], [['first', [x('a{0a}b')]]]) } }),
  post('a-sites-own-type-takes-its-texts-and-parts', [['fav', 'red'], ['fav.shade', 'dark']], { fields: { fav: answer(['red'], [['shade', ['dark']]]) } }),
];

const strangers = [
  post('a-name-of-no-field-is-left-out', [['newsletter', 'yes']], {}),
  post('an-alias-is-case-sensitive', [['Name', 'Ada']], {}),
  post('a-name-starting-with-a-dot-is-no-field', [['.first', 'x']], {}),
  post('an-empty-name-is-no-field', [['', 'x']], {}),
  post(
    'the-packages-other-names-are-left-out',
    [['jfb-form', 'k'], ['jfb-nav', 'submit'], ['jfb-revision', '3'], ['jfb-unknown', 'x'], ['jfb-', 'x'], ['ufprt', 'token']],
    {},
  ),
  post('a-repeatable-groups-own-alias-names-nothing', [['guests', 'x'], ['guests.guest', 'y']], {}),
  post('a-child-outside-its-group-is-no-field', [['guest', 'Bob']], {}),
  post('a-field-without-an-alias-posts-nothing', [['heading', 'x']], {}),
  post('a-field-as-a-group-names-nothing', [['name[0].guest', 'x']], {}),
];

const rows = [
  post(
    'rows-as-many-as-the-page-shows',
    [['jfb-rows-guests', '2'], ['guests[0].guest', 'Ann'], ['guests[1].guest', 'Bob']],
    { rows: { guests: [{ guest: answer(['Ann']) }, { guest: answer(['Bob']) }] } },
  ),
  post(
    'rows-without-answers-keep-their-places',
    [['jfb-rows-guests', '3'], ['guests[2].guest', 'Cy']],
    { rows: { guests: [{}, {}, { guest: answer(['Cy']) }] } },
  ),
  post('rows-shown-without-answers-are-empty', [['jfb-rows-guests', '2']], { rows: { guests: [{}, {}] } }),
  post(
    'a-row-past-the-count-is-left-out',
    [['jfb-rows-guests', '1'], ['guests[0].guest', 'Ann'], ['guests[1].guest', 'Bob']],
    { rows: { guests: [{ guest: answer(['Ann']) }] } },
  ),
  post(
    'without-a-count-rows-as-far-as-one-is-named',
    [['guests[2].guest', 'Cy']],
    { rows: { guests: [{}, {}, { guest: answer(['Cy']) }] } },
  ),
  post('a-count-of-none-leaves-every-row-out', [['jfb-rows-guests', '0'], ['guests[0].guest', 'Ann']], {}),
  post('a-count-past-the-most-is-the-most', [['jfb-rows-guests', '250']], { rows: { guests: empty(100) } }),
  post(
    'the-last-count-says-even-when-it-is-no-number',
    [['jfb-rows-guests', '2'], ['jfb-rows-guests', 'two'], ['guests[0].guest', 'Ann']],
    { rows: { guests: [{ guest: answer(['Ann']) }] } },
  ),
  post('the-last-count-says', [['jfb-rows-guests', 'two'], ['jfb-rows-guests', '2']], { rows: { guests: [{}, {}] } }),
  post(
    'a-count-must-be-digits-alone',
    [['jfb-rows-guests', '-1'], ['jfb-rows-guests', ' 2'], ['jfb-rows-guests', '2 '], ['jfb-rows-guests', '+2'], ['guests[0].guest', 'Ann']],
    { rows: { guests: [{ guest: answer(['Ann']) }] } },
  ),
  post('an-empty-count-is-none', [['jfb-rows-guests', ''], ['guests[1].guest', 'Bob']], { rows: { guests: [{}, { guest: answer(['Bob']) }] } }),
  post('a-count-in-other-digits-is-none', [['jfb-rows-guests', x('{662}')], ['guests[0].guest', 'Ann']], { rows: { guests: [{ guest: answer(['Ann']) }] } }),
  post('a-count-with-leading-zeros', [['jfb-rows-guests', '002']], { rows: { guests: [{}, {}] } }),
  post('a-count-with-a-trailing-nul-as-dotnet-reads-it', [['jfb-rows-guests', x('2{00}{00}')]], { rows: { guests: [{}, {}] } }),
  post('a-count-too-large-for-a-whole-number-is-none', [['jfb-rows-guests', '2147483648'], ['guests[0].guest', 'Ann']], {
    rows: { guests: [{ guest: answer(['Ann']) }] },
  }),
  post('the-largest-whole-number-is-the-most', [['jfb-rows-guests', '2147483647']], { rows: { guests: empty(100) } }),
  post('a-count-of-a-field-that-is-no-group-is-nothing', [['jfb-rows-name', '2'], ['jfb-rows-nobody', '2']], {}),
  post(
    'a-row-with-leading-zeros',
    [['jfb-rows-guests', '2'], ['guests[01].guest', 'Bob']],
    { rows: { guests: [{}, { guest: answer(['Bob']) }] } },
  ),
  post('a-row-with-a-trailing-nul', [[x('guests[1{00}].guest'), 'Bob']], { rows: { guests: [{}, { guest: answer(['Bob']) }] } }),
  post('a-row-at-the-most-is-no-row', [['guests[100].guest', 'x']], {}),
  post('a-row-below-the-most-is-one', [['guests[99].guest', 'x']], { rows: { guests: [...empty(99), { guest: answer(['x']) }] } }),
  post(
    'malformed-row-names-are-left-out',
    [
      ['guests[0]guest', 'a'],
      ['guests[0', 'b'],
      ['[0].guest', 'c'],
      ['guests[].guest', 'd'],
      ['guests[a].guest', 'e'],
      ['guests[-1].guest', 'f'],
      ['guests[ 1].guest', 'g'],
      ['guests[0].', 'h'],
      ['guests[0]', 'i'],
      ['guests[0][1].guest', 'j'],
    ],
    {},
  ),
  post(
    'a-childs-name-ending-in-a-dot-is-its-own-text',
    [['jfb-rows-guests', '1'], ['guests[0].guest.', 'Ann']],
    { rows: { guests: [{ guest: answer(['Ann']) }] } },
  ),
  post(
    'a-childs-parts-in-a-row',
    [['jfb-rows-guests', '1'], ['guests[0].who.first', 'Ada'], ['guests[0].who.last', 'King']],
    { rows: { guests: [{ who: answer([], [['first', ['Ada']], ['last', ['King']]]) }] } },
  ),
  post('a-name-of-no-child-makes-no-row', [['guests[3].nobody', 'x']], {}),
  post('a-group-name-of-no-group-is-nothing', [['story[0].guest', 'x'], ['nobody[0].guest', 'y']], {}),
  post(
    'a-row-of-crlf-texts',
    [['jfb-rows-guests', '1'], ['guests[0].guest', x('a{0d}{0a}b')]],
    { rows: { guests: [{ guest: answer([x('a{0a}b')]) }] } },
  ),
];

const consents = [
  post(
    'a-consent-ticked-with-its-wording',
    [['terms', 'true'], ['jfb-consent-terms', hash]],
    { fields: { terms: answer(['true'], [['wording', [hash]]]) } },
  ),
  post('a-consents-wording-without-a-tick', [['jfb-consent-terms', hash]], { fields: { terms: answer([], [['wording', [hash]]]) } }),
  post(
    'a-consents-wording-twice-is-kept-twice',
    [['jfb-consent-terms', hash], ['jfb-consent-terms', 'b'.repeat(64)]],
    { fields: { terms: answer([], [['wording', [hash, 'b'.repeat(64)]]]) } },
  ),
  post('a-consents-wording-with-a-part-is-nothing', [['jfb-consent-terms.x', hash]], {}),
  post('a-consents-wording-of-no-field-is-nothing', [['jfb-consent-nobody', hash]], {}),
  post('a-wordings-crlf-is-sent-as-lf', [['jfb-consent-terms', x('a{0d}{0a}b')]], { fields: { terms: answer([], [['wording', [x('a{0a}b')]]]) } }),
  post(
    'a-consent-in-a-row',
    [['jfb-rows-guests', '1'], ['guests[0].agree', 'true'], ['jfb-consent-guests[0].agree', hash]],
    { rows: { guests: [{ agree: answer(['true'], [['wording', [hash]]]) }] } },
  ),
  post(
    'a-consents-wording-names-its-row',
    [['jfb-consent-guests[1].agree', hash]],
    { rows: { guests: [{}, { agree: answer([], [['wording', [hash]]]) }] } },
  ),
];

const switches = [
  post('a-switch-left-off-is-no', [], { fields: { updates: answer(['no']) } }, form.filter((each) => each.alias === 'updates')),
  post('a-switch-turned-on', [['updates', 'yes']], { fields: { updates: answer(['yes']) } }, form.filter((each) => each.alias === 'updates')),
  post('a-switch-with-an-empty-text-is-no', [['updates', '']], { fields: { updates: answer(['no']) } }, form.filter((each) => each.alias === 'updates')),
  post(
    'a-switch-with-an-empty-text-and-yes-keeps-both',
    [['updates', ''], ['updates', 'yes']],
    { fields: { updates: answer(['', 'yes']) } },
    form.filter((each) => each.alias === 'updates'),
  ),
  post(
    'a-switch-left-off-keeps-its-parts',
    [['updates.x', '1']],
    { fields: { updates: answer(['no'], [['x', ['1']]]) } },
    form.filter((each) => each.alias === 'updates'),
  ),
  post('yes-or-no-buttons-that-post-nothing-are-nothing', [], {}, form.filter((each) => each.alias === 'answer')),
  post(
    'a-switch-in-each-row-the-page-shows',
    [['jfb-rows-guests', '2'], ['guests[0].vegan', 'yes']],
    { fields: { updates: answer(['no']) }, rows: { guests: [{ vegan: answer(['yes']) }, { vegan: answer(['no']) }] } },
    form,
  ),
  post('a-switch-in-a-group-without-rows-is-nothing', [], { fields: { updates: answer(['no']) } }, form),
  post(
    'a-switch-in-rows-named-without-a-count',
    [['guests[1].guest', 'Bob']],
    { fields: { updates: answer(['no']) }, rows: { guests: [{ vegan: answer(['no']) }, { guest: answer(['Bob']), vegan: answer(['no']) }] } },
    form,
  ),
  post(
    'a-switch-without-an-alias-posts-nothing',
    [],
    {},
    [field(null, 'yesno', { style: 'switch' })],
  ),
];

const values = [
  post(
    'the-context-values-as-posted',
    [['jfb-context-campaign', 'spring'], ['jfb-context-ref', x('a{0d}{0a}b')]],
    { context: { campaign: 'spring', ref: x('a{0d}{0a}b') } },
  ),
  post('the-last-context-value-says', [['jfb-context-campaign', 'spring'], ['jfb-context-campaign', 'autumn']], { context: { campaign: 'autumn' } }),
  post('a-context-value-may-be-empty', [['jfb-context-campaign', '']], { context: { campaign: '' } }),
  post('a-context-value-without-a-name', [['jfb-context-', 'x']], { context: { '': 'x' } }),
  post(
    'the-query-values-each-in-order',
    [['jfb-query-utm', 'a'], ['jfb-query-x', ''], ['jfb-query-utm', 'b']],
    { query: { utm: ['a', 'b'], x: [''] } },
  ),
  post('a-query-values-crlf-is-kept', [['jfb-query-q', x('a{0d}{0a}b')]], { query: { q: [x('a{0d}{0a}b')] } }),
  post('a-rankings-order-is-no-answer', [['jfb-order-rank', 'a'], ['jfb-order-rank', 'b']], {}),
  post('a-rankings-order-in-a-row-names-the-row', [['jfb-order-guests[1].pick', 'a']], { rows: { guests: [{}, {}] } }),
  post('a-rankings-order-with-a-part-is-nothing', [['jfb-order-guests[1].pick.x', 'a']], {}),
];

const signatures = [
  post('a-signatures-typed-name-is-left-out', [['sign', 'token'], ['sign.typed', 'Ada']], { fields: { sign: answer(['token']) } }),
  post('a-typed-name-alone-is-nothing', [['sign.typed', 'Ada']], {}),
  post('a-signatures-other-parts-are-kept', [['sign.other', 'x']], { fields: { sign: answer([], [['other', ['x']]]) } }),
  post(
    'a-typed-name-in-a-row-is-left-out',
    [['jfb-rows-guests', '1'], ['guests[0].sketch', 'token'], ['guests[0].sketch.typed', 'Ada']],
    { rows: { guests: [{ sketch: answer(['token']) }] } },
  ),
];

export default {
  suite: 'posts',
  defaults: {},
  fields: {},
  cases: [...texts, ...parts, ...strangers, ...rows, ...consents, ...switches, ...values, ...signatures],
};
