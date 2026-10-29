// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The merge-tag language (ROADMAP 5.3b, ADR-0014, ADR-0064): the grammar and every malformed tag, values and lists,
// every filter at its edges, each context's encoder and where a tag may stand in it, the explicit filters in each
// context, the culture formats, the limits and every problem. A `template` case renders a template in its context over
// sources given as data (each known path and its value), with the formats of its context's culture from the file's
// defaults, and expects the text and the problems. A problem's place is found in the template's own text, never counted
// by hand. The cultures are data written for these vectors: they pin the formatter, not any platform's culture data.

import { CULTURES } from '../cultures.mjs';

const BS = String.fromCharCode(92);
const TAB = String.fromCharCode(9);
const LF = String.fromCharCode(10);
const CR = String.fromCharCode(13);
const CRLF = CR + LF;
/** Text with code points written as {hex}, so this file stays ASCII. Never used on a template, which has braces. */
const x = (text) => text.replace(/\{([0-9a-f]{2,6})\}/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)));
const NBSP = x('{a0}');
const NNBSP = x('{202f}');
const QUOTE = x('{2019}');
const MINUS = x('{2212}');
const ELLIPSIS = x('{2026}');
const EMOJI = x('{1f600}');
const REPLACEMENT = x('{fffd}');

const defaults = {
  context: {
    now: '2026-06-15T10:00:00Z',
    timeZone: 'Europe/Zurich',
    culture: 'de-CH',
    member: { signedIn: false, groups: [] },
    query: {},
    hidden: [],
  },
};

defaults.cultures = CULTURES;

/** The sources most cases read; a case gets those whose paths its template names, and any of its own. */
const SOURCES = {
  'field.name': 'Anna',
  'field.company': 'Lund & Co',
  'field.markup': '<b class="x">Hi</b> & ' + "'you'",
  'field.note': 'line one' + CRLF + 'line two' + LF + 'three' + CR + 'four',
  'field.qty': { text: '1' + QUOTE + '234.5', number: '1234.5' },
  'field.negative': { text: '-1' + QUOTE + '234.567', number: '-1234.567' },
  'field.birth': { text: '15.06.2026', date: '2026-06-15' },
  'field.meeting': { text: '15.06.2026 09:05', date: '2026-06-15T09:05' },
  'field.start': { text: '13:30', date: '13:30' },
  'field.extras': ['Wifi', 'Parking'],
  'field.counts': [
    { text: '1', number: '1' },
    { text: '2.50', number: '2.5' },
  ],
  'field.none': [],
  'field.blanks': ['', ''],
  'field.empty': '',
  'field.space': ' ',
  'field.firstName': 'Anna',
  'field.firstname': 'other',
  'field.a_b-1': 'segment',
  'field.reserved': 'a b/c?d=e&f#g+h%i',
  'field.amp': 'a&b',
  'dictionary.Forms.Thanks': 'Danke',
  'page.up.heroTitle': 'Welcome',
  'query.ref': '',
};

/** The index of the `nth` (from 0) occurrence of `token` in `text`, or `token` itself when it is a number. */
function find(text, token, nth = 0) {
  if (typeof token === 'number') return token;
  let index = -1;
  for (let k = 0; k <= nth; k++) {
    index = text.indexOf(token, index + 1);
    if (index < 0) throw new Error(`"${token}" (${nth}) is not in "${text.slice(0, 80)}"`);
  }
  return index;
}

/**
 * A template case: its text in a context, the expected text, and its problems as `[code, token, nth]`, each at the
 * nth place the token stands in the text. `more` gives sources of its own, a culture or a description.
 */
function t(id, text, context, expected, problems = [], more = {}) {
  const { sources = {}, culture: name, description } = more;
  const picked = Object.fromEntries(Object.entries(SOURCES).filter(([path]) => text.includes(path)));
  return {
    id,
    ...(description === undefined ? {} : { description }),
    ...(name === undefined ? {} : { context: { culture: name } }),
    template: { text, context, sources: { ...picked, ...sources } },
    expect: {
      text: expected,
      problems: problems.map(([code, token, nth]) => ({ code, at: find(text, token, nth) })),
    },
  };
}

/** A case whose template renders as it is written, with one problem. */
const as = (id, text, context, code, token, more = {}) =>
  t(id, text, context, more.expected ?? text, [[code, token, more.nth ?? 0]], more);

/** A malformed tag in the text context. */
const m = (id, text, token, more = {}) => as(id, text, 'text', 'malformed_tag', token, more);

// ---- The grammar ----
const grammar = [
  t('text-only', 'Hello, world.', 'text', 'Hello, world.'),
  t('empty-template', '', 'text', ''),
  t('one-tag', 'Hi {{ field.name }}!', 'text', 'Hi Anna!'),
  t('no-spaces-inside', '{{field.name}}', 'text', 'Anna'),
  t('tabs-and-spaces-inside', '{{' + TAB + ' field.name ' + TAB + '|' + TAB + 'upper' + TAB + '}}', 'text', 'ANNA'),
  t('spaces-around-colon-and-comma', '{{ field.name | truncate : 2 , "!" }}', 'text', 'An!'),
  t('adjacent-tags', '{{ field.name }}{{ field.company }}', 'text', 'AnnaLund & Co'),
  t('a-dictionary-key-with-dots', '{{ dictionary.Forms.Thanks }}', 'text', 'Danke'),
  t('three-segments', '{{ page.up.heroTitle }}', 'text', 'Welcome'),
  t('segment-characters', '{{ field.a_b-1 }}', 'text', 'segment'),
  t('aliases-keep-their-case', '{{ field.firstName }} {{ field.firstname }}', 'text', 'Anna other'),
  t('a-source-alone', '{{ form }}', 'text', 'Contact', [], { sources: { form: 'Contact' } }),
  t('a-string', '{{ "plain" }}', 'text', 'plain'),
  t('a-string-prints-braces', '{{ "{{" }} and {{ "}}" }}', 'text', '{{ and }}'),
  t('a-string-with-escapes', '{{ "say ' + BS + '"hi' + BS + '" ' + BS + BS + ' done" }}', 'text', 'say "hi" ' + BS + ' done'),
  t('a-string-through-a-filter', '{{ "abc" | upper }}', 'text', 'ABC'),
  t('an-empty-string', '{{ "" | default: "none" }}', 'text', 'none'),
  t('a-string-is-encoded-like-a-value', '{{ "<b>" }}', 'html', '&lt;b&gt;'),
  t('a-string-with-a-line-break', '{{ "a' + LF + 'b" }}', 'text', 'a' + LF + 'b'),
  t('lone-braces-are-text', 'a { b } c }} d {', 'text', 'a { b } c }} d {'),
  t('braces-around-a-tag', '{ {{ field.name }} }', 'text', '{ Anna }'),
  t('three-braces', '{{{ field.name }}}', 'text', '{{{ field.name }}}', [['malformed_tag', 2]]),
  t('unknown-path-after-a-tab', '{{' + TAB + 'field.nope }}', 'text', '', [['unknown_path', 'field.nope']]),
  t('filter-names-keep-their-case', '{{ field.name | uPPER }}', 'text', '', [['unknown_filter', 'uPPER']]),
  t('filter-names-may-hold-digits', '{{ field.name | md5 }}', 'text', '', [['unknown_filter', 'md5']]),
  m('an-argument-in-other-digits', '{{ field.name | truncate: ' + x('{663}') + ' }}', x('{663}')),
];

// ---- Malformed and unclosed tags: they render as written ----
const malformed = [
  m('an-empty-tag', '{{}}', 2),
  m('a-blank-tag', '{{  }}', 4),
  m('a-source-in-capitals', '{{ Field.name }}', 'Field'),
  m('a-source-starting-with-a-digit', '{{ 1field }}', '1field'),
  m('a-number-instead-of-a-path', '{{ 42 }}', '42'),
  m('a-dot-without-a-segment', '{{ field. }}', ' }}'),
  m('two-dots', '{{ field..name }}', '.name'),
  m('a-space-before-a-dot', '{{ field .name }}', '.name'),
  m('a-character-outside-segments', '{{ field.na*me }}', '*'),
  m('a-letter-outside-ascii', '{{ field.n' + x('{e4}') + 'me }}', x('{e4}')),
  m('a-hyphen-in-a-source', '{{ my-source.x }}', '-source'),
  m('text-after-the-path', '{{ field.name x }}', 'x }}'),
  m('a-line-break-inside', '{{ field.name' + LF + '}}', LF),
  m('a-missing-filter', '{{ field.name | }}', '}}'),
  m('two-bars', '{{ field.name || upper }}', '| upper'),
  m('a-filter-in-capitals', '{{ field.name | Upper }}', 'Upper'),
  m('a-hyphen-in-a-filter', '{{ field.name | up-per }}', '-per'),
  m('a-colon-without-an-argument', '{{ field.name | truncate: }}', '}}'),
  m('a-comma-without-an-argument', '{{ field.name | truncate: 5, }}', '}}'),
  m('two-arguments-without-a-comma', '{{ field.name | default: "a" "b" }}', '"b"'),
  m('a-bare-word-argument', '{{ field.name | default: web }}', 'web'),
  m('a-leading-zero', '{{ field.name | truncate: 05 }}', '05'),
  m('a-negative-leading-zero', '{{ field.name | truncate: -05 }}', '05'),
  m('a-point-without-digits', '{{ field.name | number: 2. }}', ' }}'),
  m('a-minus-alone', '{{ field.name | truncate: - }}', ' }}'),
  m('an-exponent', '{{ field.name | truncate: 1e3 }}', 'e3'),
  m('a-plus-sign', '{{ field.name | truncate: +3 }}', '+3'),
  m('an-unknown-escape', '{{ "a' + BS + 'nb" }}', BS),
  m('a-string-then-text', '{{ "a"b }}', 'b }}'),
  m('a-string-that-does-not-close', '{{ "abc }} then {{ field.name }}', '"abc', { expected: '{{ "abc }} then Anna' }),
  as('an-unclosed-tag', 'Hi {{ field.name', 'text', 'unclosed_tag', '{{'),
  as('an-unclosed-tag-after-a-tag', '{{ field.name }} and {{ field.company', 'text', 'unclosed_tag', '{{', {
    nth: 1,
    expected: 'Anna and {{ field.company',
  }),
  as('a-closed-string-in-an-unclosed-tag', '{{ "a}}b" ', 'text', 'unclosed_tag', 0),
  as('braces-at-the-end', 'end {{', 'text', 'unclosed_tag', '{{'),
];

// ---- What each problem renders, and their order ----
const problems = [
  t('an-unknown-path-renders-empty', 'a{{ form.title }}b', 'text', 'ab', [['unknown_path', 'form.title']]),
  t('an-unknown-filter-renders-empty', 'a{{ field.name | shout }}b', 'text', 'ab', [['unknown_filter', 'shout']]),
  t('an-unknown-path-and-filter', '{{ form.title | shout }}', 'text', '', [
    ['unknown_path', 'form'],
    ['unknown_filter', 'shout'],
  ]),
  t('an-error-wins-over-a-warning', '{{ form.title | truncate: "x" }}', 'text', '{{ form.title | truncate: "x" }}', [
    ['unknown_path', 'form'],
    ['invalid_argument', '"x"'],
  ]),
  t('a-warning-and-an-error-of-filters', '{{ field.name | shout | truncate }}', 'text', '{{ field.name | shout | truncate }}', [
    ['unknown_filter', 'shout'],
    ['invalid_argument', 'truncate'],
  ]),
  t(
    'problems-in-the-order-of-the-template',
    '{{ field.name | x }} {{ Bad }} {{ field.name | truncate: -1 }} {{ form.title }}',
    'text',
    ' {{ Bad }} {{ field.name | truncate: -1 }} ',
    [
      ['unknown_filter', 'x }}'],
      ['malformed_tag', 'Bad'],
      ['invalid_argument', '-1'],
      ['unknown_path', 'form'],
    ],
  ),
  t('a-known-path-without-an-answer', '[{{ query.ref }}]', 'text', '[]'),
  t('a-path-unknown-under-a-known-source', '[{{ query.other | default: "web" }}]', 'text', '[]', [
    ['unknown_path', 'query'],
  ]),
  t('html-after-a-tag-with-a-warning-the-scan-reads-on', '{{ field.name | nofilter: "<script>" }}{{ field.name }}', 'html', 'Anna', [
    ['unknown_filter', 'nofilter'],
  ], { description: 'a tag with warnings only writes nothing, so the scan does not read the script in its text' }),
  as('two-filters-after-an-encoding', '{{ field.name | url | upper | lower }}', 'text', 'filter_after_encoding', 'upper'),
];

// ---- Values and lists ----
const values = [
  t('a-number-item-shows-its-text', '{{ field.qty }}', 'text', '1' + QUOTE + '234.5'),
  t('a-date-item-shows-its-text', '{{ field.birth }}', 'text', '15.06.2026'),
  t('a-list-is-joined', '{{ field.extras }}', 'text', 'Wifi, Parking'),
  t('an-empty-list-is-empty', '[{{ field.none }}]', 'text', '[]'),
  t('a-list-of-number-items', '{{ field.counts }}', 'text', '1, 2.50'),
  t('default-of-an-empty-text', '{{ field.empty | default: "n/a" }}', 'text', 'n/a'),
  t('default-of-a-text', '{{ field.name | default: "n/a" }}', 'text', 'Anna'),
  t('default-of-a-space', '[{{ field.space | default: "n/a" }}]', 'text', '[ ]'),
  t('default-of-an-empty-list', '{{ field.none | default: "none" }}', 'text', 'none'),
  t('default-of-an-empty-list-is-one-text', '{{ field.none | default: "none" | json }}', 'text', '"none"'),
  t('default-of-a-list-of-empty-texts', '{{ field.blanks | default: "none" }}', 'text', 'none'),
  t('default-of-a-list', '{{ field.extras | default: "none" }}', 'text', 'Wifi, Parking'),
  t('default-takes-a-number-as-written', '{{ field.empty | default: -1.50 }}', 'text', '-1.50'),
  t('default-after-a-filter-of-each-item', '{{ field.blanks | upper | default: "none" }}', 'text', 'none'),
  t('default-then-a-filter', '{{ field.empty | default: "n/a" | upper }}', 'text', 'N/A'),
  t('default-of-a-query-without-a-value', '{{ query.ref | default: "web" }}', 'text', 'web'),
  as('default-without-an-argument', '{{ field.empty | default }}', 'text', 'invalid_argument', 'default'),
  as('default-with-two-arguments', '{{ field.empty | default: "a", "b" }}', 'text', 'invalid_argument', 'default'),
];

// ---- upper and lower ----
const CASED = {
  'field.latin': x('Stra{df}e {e4}{f6}{fc} {ff} {b5} {17f} {1c5}'),
  'field.capitals': x('STRASSE {c4}{d6}{dc} {178} {1c5} {1e9e}'),
  'field.greek': x('{3a3}{3af}{3c3}{3c5}{3c6}{3bf}{3c2}'),
  'field.greekCapitals': x('{39f}{394}{39f}{3a3}'),
  'field.cyrillic': x('{43f}{440}{438}{432}{435}{442}'),
  'field.armenian': x('{562}{561}{580}'),
  'field.wide': x('{ff41}{ff42}{ff43}'),
  'field.turkish': x('{131} i {130} I'),
  'field.iota': x('{1fb3} {1f80} {1f88} {1fbc}'),
  'field.outside': x('{10d0} {10428} {ab70} {19b} {149}'),
  'field.digits': '12-3_4!',
};
const casing = [
  t('upper-of-latin', '{{ field.latin | upper }}', 'text', x('STRA{df}E {c4}{d6}{dc} {178} {39c} S {1c4}'), [], { sources: CASED }),
  t('lower-of-latin', '{{ field.capitals | lower }}', 'text', x('strasse {e4}{f6}{fc} {ff} {1c6} {df}'), [], { sources: CASED }),
  t('upper-of-greek', '{{ field.greek | upper }}', 'text', x('{3a3}{38a}{3a3}{3a5}{3a6}{39f}{3a3}'), [], { sources: CASED }),
  t('lower-of-greek-has-no-final-sigma', '{{ field.greekCapitals | lower }}', 'text', x('{3bf}{3b4}{3bf}{3c3}'), [], {
    sources: CASED,
  }),
  t('upper-of-cyrillic', '{{ field.cyrillic | upper }}', 'text', x('{41f}{420}{418}{412}{415}{422}'), [], { sources: CASED }),
  t('upper-of-armenian', '{{ field.armenian | upper }}', 'text', x('{532}{531}{550}'), [], { sources: CASED }),
  t('upper-of-full-width', '{{ field.wide | upper }}', 'text', x('{ff21}{ff22}{ff23}'), [], { sources: CASED }),
  t('upper-keeps-the-turkish-i', '{{ field.turkish | upper }}', 'text', x('{131} I {130} I'), [], { sources: CASED }),
  t('lower-keeps-the-turkish-i', '{{ field.turkish | lower }}', 'text', x('{131} i {130} i'), [], { sources: CASED }),
  t('upper-keeps-a-ypogegrammeni', '{{ field.iota | upper }}', 'text', x('{1fb3} {1f80} {1f88} {1fbc}'), [], { sources: CASED }),
  t('lower-of-a-prosgegrammeni', '{{ field.iota | lower }}', 'text', x('{1fb3} {1f80} {1f80} {1fb3}'), [], { sources: CASED }),
  t('upper-keeps-letters-outside-the-blocks', '{{ field.outside | upper }}', 'text', x('{10d0} {10428} {ab70} {19b} {149}'), [], {
    sources: CASED,
    description: 'Georgian, Deseret and Cherokee stay; U+019B maps outside the blocks; U+0149 would expand',
  }),
  t('upper-keeps-what-has-no-case', '{{ field.digits | upper }} {{ field.digits | lower }}', 'text', '12-3_4! 12-3_4!', [], {
    sources: CASED,
  }),
  t('upper-of-each-item', '{{ field.extras | upper }}', 'text', 'WIFI, PARKING'),
  t('lower-gives-text', '{{ field.qty | lower | json }}', 'text', '"1' + QUOTE + '234.5"'),
  as('upper-with-an-argument', '{{ field.name | upper: 1 }}', 'text', 'invalid_argument', 'upper'),
];

// ---- truncate ----
const truncating = [
  t('truncate-a-shorter-text', '{{ field.name | truncate: 10 }}', 'text', 'Anna'),
  t('truncate-at-the-length', '{{ field.name | truncate: 4 }}', 'text', 'Anna'),
  t('truncate-a-longer-text', '{{ field.company | truncate: 4 }}', 'text', 'Lund' + ELLIPSIS),
  t('truncate-drops-trailing-spaces', '{{ field.company | truncate: 5 }}', 'text', 'Lund' + ELLIPSIS),
  t('truncate-to-nothing', '{{ field.name | truncate: 0 }}', 'text', ELLIPSIS),
  t('truncate-an-empty-text', '[{{ field.empty | truncate: 0 }}]', 'text', '[]'),
  t('truncate-with-a-suffix', '{{ field.company | truncate: 6, " [more]" }}', 'text', 'Lund & [more]'),
  t('truncate-with-an-empty-suffix', '{{ field.company | truncate: 6, "" }}', 'text', 'Lund &'),
  t('truncate-counts-code-points', '{{ field.emoji | truncate: 2 }}', 'text', EMOJI + EMOJI + ELLIPSIS, [], {
    sources: { 'field.emoji': EMOJI + EMOJI + EMOJI },
  }),
  t('truncate-keeps-a-pair-whole', '{{ field.emoji | truncate: 1 }}', 'text', EMOJI + ELLIPSIS, [], {
    sources: { 'field.emoji': EMOJI + EMOJI },
  }),
  t('truncate-counts-a-combining-mark', '{{ field.accent | truncate: 1 }}', 'text', 'e' + ELLIPSIS, [], {
    sources: { 'field.accent': 'e' + x('{301}') + 'x' },
  }),
  t('truncate-each-item', '{{ field.extras | truncate: 3 }}', 'text', 'Wif' + ELLIPSIS + ', Par' + ELLIPSIS),
  t('truncate-to-the-longest-output', '{{ field.name | truncate: 100000 }}', 'text', 'Anna'),
  t('truncate-gives-text', '{{ field.qty | truncate: 50 | json }}', 'text', '"1' + QUOTE + '234.5"'),
  as('truncate-past-the-longest-output', '{{ field.name | truncate: 100001 }}', 'text', 'invalid_argument', '100001'),
  as('truncate-without-a-count', '{{ field.name | truncate }}', 'text', 'invalid_argument', 'truncate'),
  as('truncate-with-a-string-count', '{{ field.name | truncate: "4" }}', 'text', 'invalid_argument', '"4"'),
  as('truncate-with-a-fraction', '{{ field.name | truncate: 1.5 }}', 'text', 'invalid_argument', '1.5'),
  as('truncate-with-a-negative-count', '{{ field.name | truncate: -1 }}', 'text', 'invalid_argument', '-1'),
  as('truncate-with-a-number-suffix', '{{ field.name | truncate: 2, 3 }}', 'text', 'invalid_argument', '3 }}'),
  as('truncate-with-three-arguments', '{{ field.name | truncate: 2, "a", "b" }}', 'text', 'invalid_argument', 'truncate'),
];

// ---- date ----
const DATES = {
  'field.wire': '2028-02-29',
  'field.shown': '15.06.2026',
  'field.impossible': '2026-02-30',
  'field.month13': '2026-13-01',
  'field.shortTime': '9:05',
  'field.zoned': '2026-06-15T09:05:00Z',
  'field.seconds': '2026-06-15T09:05:07',
  'field.dates': ['2026-01-01', '2026-12-31'],
  'field.friday': '2026-06-05',
  'field.early': '0005-03-07',
  'field.midnight': '00:05:09',
  'field.afternoon': '13:07',
  'field.noon': '12:00',
  'field.late': '23:59',
  'field.at': '2026-06-05T09:05',
};
const d = (id, text, expected, more = {}) =>
  t(id, text, 'text', expected, [], { ...more, sources: { ...DATES, ...more.sources } });
const dating = [
  t('date-short', '{{ field.birth | date: "short" }}', 'text', '15.06.2026'),
  t('date-without-a-format-is-short', '{{ field.birth | date }}', 'text', '15.06.2026'),
  t('date-medium', '{{ field.birth | date: "medium" }}', 'text', '15. Juni 2026'),
  t('date-long', '{{ field.birth | date: "long" }}', 'text', 'Montag, 15. Juni 2026'),
  t('date-time-of-a-date-time', '{{ field.meeting | date: "time" }}', 'text', '09:05'),
  t('date-date-time', '{{ field.meeting | date: "dateTime" }}', 'text', '15.06.2026 09:05'),
  t('date-short-of-a-date-time', '{{ field.meeting | date: "short" }}', 'text', '15.06.2026'),
  t('date-time-of-a-time', '{{ field.start | date: "time" }}', 'text', '13:30'),
  t('date-iso-of-a-date', '{{ field.birth | date: "iso" }}', 'text', '2026-06-15'),
  t('date-iso-of-a-time', '{{ field.start | date: "iso" }}', 'text', '13:30:00'),
  t('date-iso-of-a-date-time', '{{ field.meeting | date: "iso" }}', 'text', '2026-06-15T09:05:00'),
  d('date-iso-keeps-seconds', '{{ field.seconds | date: "iso" }}', '2026-06-15T09:05:07'),
  t('date-time-of-a-date-passes-through', '{{ field.birth | date: "time" }}', 'text', '15.06.2026'),
  t('date-short-of-a-time-passes-through', '{{ field.start | date: "short" }}', 'text', '13:30'),
  t('date-date-time-of-a-date-passes-through', '{{ field.birth | date: "dateTime" }}', 'text', '15.06.2026'),
  d('date-of-a-wire-text', '{{ field.wire | date: "long" }}', 'Dienstag, 29. Februar 2028'),
  d('date-of-a-display-text-passes-through', '{{ field.shown | date: "long" }}', '15.06.2026'),
  d('date-of-an-impossible-day-passes-through', '{{ field.impossible | date }}', '2026-02-30'),
  d('date-of-a-thirteenth-month-passes-through', '{{ field.month13 | date }}', '2026-13-01'),
  d('date-of-a-short-time-passes-through', '{{ field.shortTime | date: "time" }}', '9:05'),
  d('date-with-a-zone-passes-through', '{{ field.zoned | date: "iso" }}', '2026-06-15T09:05:00Z'),
  d('date-of-each-item', '{{ field.dates | date }}', '01.01.2026, 31.12.2026'),
  t('date-gives-text', '{{ field.birth | date | json }}', 'text', '"15.06.2026"'),
  t('date-of-a-number-item-passes-through', '{{ field.qty | date }}', 'text', '1' + QUOTE + '234.5'),
  as('date-with-an-unknown-format', '{{ field.birth | date: "full" }}', 'text', 'invalid_argument', '"full"'),
  as('date-format-names-keep-their-case', '{{ field.birth | date: "Short" }}', 'text', 'invalid_argument', '"Short"'),
  as('date-with-a-number', '{{ field.birth | date: 1 }}', 'text', 'invalid_argument', '1 }}'),
  as('date-with-two-formats', '{{ field.birth | date: "short", "long" }}', 'text', 'invalid_argument', 'date'),
  t('date-en-us-short', '{{ field.birth | date }}', 'text', '6/15/2026', [], { culture: 'en-US' }),
  t('date-en-us-medium', '{{ field.birth | date: "medium" }}', 'text', 'Jun 15, 2026', [], { culture: 'en-US' }),
  t('date-en-us-long', '{{ field.birth | date: "long" }}', 'text', 'Monday, June 15, 2026', [], { culture: 'en-US' }),
  t('date-en-us-time', '{{ field.meeting | date: "time" }}', 'text', '9:05 AM', [], { culture: 'en-US' }),
  t('date-en-us-date-time', '{{ field.meeting | date: "dateTime" }}', 'text', '6/15/2026 9:05 AM', [], { culture: 'en-US' }),
  d('date-en-us-just-after-midnight', '{{ field.midnight | date: "time" }}', '12:05 AM', { culture: 'en-US' }),
  d('date-en-us-noon', '{{ field.noon | date: "time" }}', '12:00 PM', { culture: 'en-US' }),
  d('date-en-us-afternoon', '{{ field.afternoon | date: "time" }}', '1:07 PM', { culture: 'en-US' }),
  d('date-en-us-before-midnight', '{{ field.late | date: "time" }}', '11:59 PM', { culture: 'en-US' }),
  t('date-fr-fr-long', '{{ field.birth | date: "long" }}', 'text', 'lundi 15 juin 2026', [], { culture: 'fr-FR' }),
  t('date-pl-pl-genitive-month', '{{ field.birth | date: "long" }}', 'text', x('poniedzia{142}ek, 15 czerwca 2026'), [], {
    culture: 'pl-PL',
  }),
  t('date-pl-pl-short-month', '{{ field.birth | date: "medium" }}', 'text', '15 cze 2026', [], { culture: 'pl-PL' }),
  t('date-sv-se-short', '{{ field.birth | date }}', 'text', '2026-06-15', [], { culture: 'sv-SE' }),
  d('date-every-date-token', '{{ field.friday | date }}', '5 05 Fri Friday|6 06 Jun-g June-g|26 26 2026 2026 02026', {
    culture: 'x-patterns',
    description: 'a pattern with the day of the month takes the genitive names',
  }),
  d('date-years-before-ten', '{{ field.early | date: "medium" }}', '5 05 005|Mar March|Mon Monday', {
    culture: 'x-patterns',
    description: 'without the day of the month, the names are nominative, even beside the name of the day',
  }),
  d('date-quoted-text', '{{ field.friday | date: "long" }}', 'Day 5 of June-g, Jun-g', { culture: 'x-patterns' }),
  d('date-every-time-token-in-the-morning', '{{ field.midnight | date: "time" }}', '12 12 0 00 5 05 9 09 v vorm.', {
    culture: 'x-patterns',
  }),
  d('date-every-time-token-in-the-afternoon', '{{ field.afternoon | date: "time" }}', '1 01 13 13 7 07 0 00 n nachm.', {
    culture: 'x-patterns',
  }),
  d('date-literals-escapes-and-an-open-quote', '{{ field.at | date: "dateTime" }}', "d at 09:05 o'clock gz/:% open", {
    culture: 'x-patterns',
  }),
  d('date-weekday-of-the-first-day', '{{ field.first | date: "long" }}', 'Day 1 of January-g, Jan-g', {
    culture: 'x-patterns',
    sources: { ...DATES, 'field.first': '0001-01-01' },
  }),
  d('date-weekdays-across-the-calendar', '{{ field.days | date }}', [
    '1 01 Mon Monday|1 01 Jan-g January-g|1 01 001 0001 00001',
    '1 01 Thu Thursday|1 01 Jan-g January-g|70 70 1970 1970 01970',
    '29 29 Tue Tuesday|2 02 Feb-g February-g|0 00 2000 2000 02000',
    '31 31 Fri Friday|12 12 Dec-g December-g|99 99 9999 9999 09999',
  ].join(', '), {
    culture: 'x-patterns',
    sources: { 'field.days': ['0001-01-01', '1970-01-01', '2000-02-29', '9999-12-31'] },
  }),
  d('date-names-missing', '{{ field.friday | date: "long" }}', '6 6 6 6', { culture: 'x-incomplete' }),
  d('date-designators-missing', '{{ field.afternoon | date: "time" }}', ' |1', { culture: 'x-incomplete' }),
];

// ---- number ----
const NUMBERS = {
  'field.half': '2.5',
  'field.minusHalf': '-2.5',
  'field.eighth': '0.125',
  'field.minusEighth': '-0.125',
  'field.tiny': '-0.001',
  'field.zero': '0',
  'field.below': '0.5',
  'field.long': '1234567.1234567',
  'field.sixty': '9'.repeat(60),
  'field.sixtyOne': '9'.repeat(61),
  'field.wire': '1234.5',
  'field.shown': '1' + QUOTE + '234.5',
  'field.exponent': '1e5',
  'field.plus': '+5',
  'field.leading': '05',
  'field.padded': ' 5',
  'field.big': '12345678.9',
  'field.small': '123',
  'field.thousand': '1234',
  'field.million': '1234567',
  'field.minusMillion': '-1234567',
  'field.cents': '0.005',
  'field.minusCents': '-0.005',
};
const n = (id, text, expected, more = {}) =>
  t(id, text, 'text', expected, [], { ...more, sources: { ...NUMBERS, ...more.sources } });
const sixtyNines = '999' + ('' + QUOTE + '999').repeat(19);
const numbering = [
  t('number-keeps-its-decimals', '{{ field.qty | number }}', 'text', '1' + QUOTE + '234.5'),
  t('number-pads-its-places', '{{ field.qty | number: 2 }}', 'text', '1' + QUOTE + '234.50'),
  t('number-rounds-to-its-places', '{{ field.negative | number: 2 }}', 'text', '-1' + QUOTE + '234.57'),
  t('number-to-no-places', '{{ field.qty | number: 0 }}', 'text', '1' + QUOTE + '235'),
  n('number-rounds-a-half-away-from-zero', '{{ field.half | number: 0 }} {{ field.minusHalf | number: 0 }}', '3 -3'),
  n('number-rounds-an-eighth', '{{ field.eighth | number: 2 }} {{ field.minusEighth | number: 2 }}', '0.13 -0.13'),
  n('number-never-negative-zero', '{{ field.tiny | number: 2 }}', '0.00'),
  n('number-of-zero', '{{ field.zero | number }} {{ field.zero | number: 2 }}', '0 0.00'),
  n('number-below-one', '{{ field.below | number }}', '0.5'),
  n('number-groups-only-the-integer', '{{ field.long | number }}', '1' + QUOTE + '234' + QUOTE + '567.1234567'),
  n('number-twenty-places', '{{ field.below | number: 20 }}', '0.50000000000000000000'),
  n('number-of-sixty-digits', '{{ field.sixty | number }}', sixtyNines),
  n('number-past-sixty-digits-passes-through', '{{ field.sixtyOne | number }}', '9'.repeat(61)),
  n('number-of-a-wire-text', '{{ field.wire | number }}', '1' + QUOTE + '234.5'),
  n('number-of-a-display-text-passes-through', '{{ field.shown | number: 2 }}', '1' + QUOTE + '234.5'),
  t('number-of-a-word-passes-through', '{{ field.name | number }}', 'text', 'Anna'),
  n(
    'number-of-other-forms-passes-through',
    '{{ field.exponent | number }} {{ field.plus | number }} {{ field.leading | number }} [{{ field.padded | number }}]',
    '1e5 +5 05 [ 5]',
  ),
  t('number-of-a-date-item-passes-through', '{{ field.birth | number }}', 'text', '15.06.2026'),
  t('number-of-each-item', '{{ field.counts | number }}', 'text', '1, 2.5'),
  t('number-gives-text', '{{ field.qty | number: 2 | json }}', 'text', '"1' + QUOTE + '234.50"'),
  t('number-en-us', '{{ field.qty | number: 2 }} {{ field.negative | number: 2 }}', 'text', '1,234.50 -1,234.57', [], {
    culture: 'en-US',
  }),
  t('number-fr-fr', '{{ field.qty | number: 2 }}', 'text', '1' + NNBSP + '234,50', [], { culture: 'fr-FR' }),
  n('number-hi-in-groups-by-two', '{{ field.big | number }}', '1,23,45,678.9', { culture: 'hi-IN' }),
  n('number-hi-in-small-numbers', '{{ field.small | number }} {{ field.thousand | number }}', '123 1,234', { culture: 'hi-IN' }),
  t('number-sv-se-minus-sign', '{{ field.negative | number: 2 }}', 'text', MINUS + '1' + NBSP + '234,57', [], {
    culture: 'sv-SE',
  }),
  n('number-x-patterns-groups-once', '{{ field.million | number }}', '1234 567', { culture: 'x-patterns' }),
  n('number-x-patterns-negative-in-parentheses', '{{ field.minusMillion | number: 1 }}', '(1234 567.0)', {
    culture: 'x-patterns',
  }),
  n('number-x-incomplete-without-grouping', '{{ field.minusMillion | number }}', '1234567-', { culture: 'x-incomplete' }),
  as('number-with-too-many-places', '{{ field.qty | number: 21 }}', 'text', 'invalid_argument', '21'),
  as('number-with-a-fraction', '{{ field.qty | number: 1.5 }}', 'text', 'invalid_argument', '1.5'),
  as('number-with-a-string', '{{ field.qty | number: "2" }}', 'text', 'invalid_argument', '"2"'),
  as('number-with-two-arguments', '{{ field.qty | number: 1, 2 }}', 'text', 'invalid_argument', 'number'),
];

// ---- currency ----
const c = (id, text, expected, name, more = {}) => t(id, text, 'text', expected, [], { ...more, culture: name });
const currencies = [
  c('currency-own-de-ch', '{{ field.qty | currency: "CHF" }}', 'CHF 1' + QUOTE + '234.50'),
  c('currency-own-negative-de-ch', '{{ field.negative | currency: "CHF" }}', 'CHF-1' + QUOTE + '234.57'),
  c('currency-other-de-ch', '{{ field.qty | currency: "EUR" }}', 'EUR 1' + QUOTE + '234.50'),
  c('currency-own-en-us', '{{ field.qty | currency: "USD" }} {{ field.negative | currency: "USD" }}', '$1,234.50 -$1,234.57', 'en-US'),
  c(
    'currency-other-en-us',
    '{{ field.qty | currency: "EUR" }} {{ field.negative | currency: "EUR" }}',
    'EUR' + NBSP + '1,234.50 -EUR' + NBSP + '1,234.57',
    'en-US',
  ),
  c('currency-own-fr-fr', '{{ field.qty | currency: "EUR" }}', '1' + NNBSP + '234,50' + NBSP + x('{20ac}'), 'fr-FR'),
  c('currency-other-fr-fr', '{{ field.qty | currency: "CHF" }}', '1' + NNBSP + '234,50' + NBSP + 'CHF', 'fr-FR'),
  c('currency-own-symbol-beside-the-number', '{{ field.qty | currency: "XTS" }}', '1 234.50' + x('{a4}'), 'x-patterns'),
  c('currency-other-code-beside-the-number', '{{ field.qty | currency: "CHF" }}', '1 234.50' + NBSP + 'CHF', 'x-patterns'),
  c('currency-negative-in-parentheses', '{{ field.negative | currency: "CHF" }}', '(CHF' + NBSP + '1 234.57)', 'x-patterns'),
  c('currency-zero-places', '{{ field.qty | currency: "JPY" }}', 'JPY' + NBSP + '1,235', 'en-US'),
  c('currency-three-places', '{{ field.qty | currency: "KWD" }}', 'KWD' + NBSP + '1,234.500', 'en-US'),
  c('currency-four-places', '{{ field.qty | currency: "CLF" }}', 'CLF' + NBSP + '1,234.5000', 'en-US'),
  c('currency-an-unlisted-code-has-two-places', '{{ field.qty | currency: "XYZ" }}', 'XYZ' + NBSP + '1,234.50', 'en-US'),
  c('currency-with-places', '{{ field.qty | currency: "JPY", 2 }}', 'JPY' + NBSP + '1,234.50', 'en-US'),
  c('currency-with-no-places', '{{ field.qty | currency: "CHF", 0 }}', 'CHF 1' + QUOTE + '235'),
  c(
    'currency-rounds-a-half-away-from-zero',
    '{{ field.cents | currency: "CHF" }} {{ field.minusCents | currency: "CHF" }}',
    'CHF 0.01 CHF-0.01',
    undefined,
    { sources: NUMBERS },
  ),
  c('currency-of-a-word-passes-through', '{{ field.name | currency: "CHF" }}', 'Anna'),
  c('currency-of-each-item', '{{ field.counts | currency: "CHF" }}', 'CHF 1.00, CHF 2.50'),
  c('currency-own-without-grouping-or-symbol', '{{ field.qty | currency: "XXX" }}', '1234.50', 'x-incomplete'),
  c('currency-other-without-grouping', '{{ field.qty | currency: "CHF" }}', 'CHF' + NBSP + '1234.50', 'x-incomplete'),
  as('currency-code-in-small-letters', '{{ field.qty | currency: "chf" }}', 'text', 'invalid_argument', '"chf"'),
  as('currency-code-too-long', '{{ field.qty | currency: "CHFX" }}', 'text', 'invalid_argument', '"CHFX"'),
  as('currency-code-too-short', '{{ field.qty | currency: "CH" }}', 'text', 'invalid_argument', '"CH"'),
  as('currency-code-with-a-digit', '{{ field.qty | currency: "C1F" }}', 'text', 'invalid_argument', '"C1F"'),
  as('currency-code-as-a-number', '{{ field.qty | currency: 756 }}', 'text', 'invalid_argument', '756'),
  as('currency-without-a-code', '{{ field.qty | currency }}', 'text', 'invalid_argument', 'currency'),
  as('currency-with-too-many-places', '{{ field.qty | currency: "CHF", 21 }}', 'text', 'invalid_argument', '21'),
  as('currency-with-string-places', '{{ field.qty | currency: "CHF", "2" }}', 'text', 'invalid_argument', '"2"'),
  as('currency-with-three-arguments', '{{ field.qty | currency: "CHF", 2, 2 }}', 'text', 'invalid_argument', 'currency'),
];

// ---- join ----
const joining = [
  t('join-with-a-comma', '{{ field.extras | join }}', 'text', 'Wifi, Parking'),
  t('join-with-a-separator', '{{ field.extras | join: " / " }}', 'text', 'Wifi / Parking'),
  t('join-with-nothing', '{{ field.extras | join: "" }}', 'text', 'WifiParking'),
  t('join-one-item', '{{ field.name | join: "-" }}', 'text', 'Anna'),
  t('join-an-empty-list', '[{{ field.none | join: "-" }}]', 'text', '[]'),
  t('join-then-a-filter', '{{ field.extras | join: " " | upper }}', 'text', 'WIFI PARKING'),
  t('join-gives-text', '{{ field.counts | join | json }}', 'text', '"1, 2.50"'),
  as('join-with-a-number', '{{ field.extras | join: 1 }}', 'text', 'invalid_argument', '1 }}'),
  as('join-with-two-arguments', '{{ field.extras | join: "a", "b" }}', 'text', 'invalid_argument', 'join'),
  t('join-of-one-item-keeps-it', '{{ field.qty | join | json }}', 'text', '1234.5'),
];

// ---- The encoding filters ----
const encoding = [
  t('html-escapes', '{{ field.markup | html }}', 'html', '&lt;b class=&quot;x&quot;&gt;Hi&lt;/b&gt; &amp; &#39;you&#39;'),
  t('html-keeps-line-breaks', '{{ field.note | html }}', 'html', 'line one<br>line two<br>three<br>four'),
  t('html-two-line-breaks', '{{ field.breaks | html }}', 'html', 'a<br><br>b<br><br>c', [], {
    sources: { 'field.breaks': 'a' + CRLF + CRLF + 'b' + LF + CR + 'c' },
  }),
  t('html-of-each-item', '{{ field.items | html }}', 'html', 'a&lt;b, c', [], { sources: { 'field.items': ['a<b', 'c'] } }),
  t('html-context-alone-keeps-line-breaks-as-they-are', '{{ field.note }}', 'html', SOURCES['field.note']),
  as('html-with-an-argument', '{{ field.name | html: 1 }}', 'html', 'invalid_argument', 'html'),
  t('url-encodes-a-component', '{{ field.reserved | url }}', 'text', 'a%20b%2Fc%3Fd%3De%26f%23g%2Bh%25i'),
  t('url-keeps-the-unreserved', '{{ field.plain | url }}', 'text', 'AZaz09-._~', [], { sources: { 'field.plain': 'AZaz09-._~' } }),
  t('url-encodes-marks', '{{ field.marks | url }}', 'text', '%21%2A%27%28%29', [], { sources: { 'field.marks': "!*'()" } }),
  t('url-encodes-utf8', '{{ field.wide | url }}', 'text', '%C3%A9%E2%82%AC%F0%9F%98%80', [], {
    sources: { 'field.wide': x('{e9}{20ac}') + EMOJI },
  }),
  t('url-of-each-item-and-its-separator', '{{ field.items | url }}', 'text', 'a%20b%2C%20c', [], {
    sources: { 'field.items': ['a b', 'c'] },
  }),
  as('url-with-an-argument', '{{ field.name | url: "x" }}', 'text', 'invalid_argument', 'url'),
  t('json-of-a-text', '{{ field.name | json }}', 'text', '"Anna"'),
  t('json-keeps-a-surrogate-pair', '{{ field.emoji | json }}', 'text', '"' + EMOJI + '"', [], { sources: { 'field.emoji': EMOJI } }),
  t(
    'json-escapes',
    '{{ field.tricky | json }}',
    'text',
    '"' +
      [BS + '"q' + BS + '" ', BS + BS, ' ', BS + 'n', BS + 't', BS + 'r', BS + 'b', BS + 'f', BS + 'u0001', BS + 'u001f'].join('') +
      ' ' +
      BS +
      'u003ca' +
      BS +
      'u003e ' +
      BS +
      'u0026 ' +
      BS +
      'u2028' +
      BS +
      'u2029' +
      x('{7f}{e9}') +
      '"',
    [],
    {
      sources: {
        'field.tricky':
          '"q" ' + BS + ' ' + LF + TAB + CR + x('{08}{0c}{01}{1f}') + ' <a> & ' + x('{2028}{2029}{7f}{e9}'),
      },
    },
  ),
  t('json-of-a-number-item', '{{ field.qty | json }}', 'text', '1234.5'),
  t('json-of-a-number-in-its-canonical-form', '{{ field.n | json }} {{ field.z | json }}', 'text', '1.5 0', [], {
    sources: { 'field.n': { text: '1.50', number: '1.50' }, 'field.z': { text: '0', number: '-0' } },
  }),
  t('json-of-a-number-that-does-not-read', '{{ field.n | json }}', 'text', '"about 3"', [], {
    sources: { 'field.n': { text: 'about 3', number: 'about 3' } },
  }),
  t('json-of-a-date-item-is-its-text', '{{ field.birth | json }}', 'text', '"15.06.2026"'),
  t('json-of-a-list', '{{ field.extras | json }}', 'text', '["Wifi","Parking"]'),
  t('json-of-an-empty-list', '{{ field.none | json }}', 'text', '[]'),
  t('json-of-a-list-of-number-items', '{{ field.counts | json }}', 'text', '[1,2.5]'),
  t('json-of-an-empty-text', '{{ field.empty | json }}', 'text', '""'),
  as('json-with-an-argument', '{{ field.name | json: 2 }}', 'text', 'invalid_argument', 'json'),
  as('a-filter-after-html', '{{ field.name | html | upper }}', 'html', 'filter_after_encoding', 'upper'),
  as('a-filter-after-url', '{{ field.name | url | default: "x" }}', 'text', 'filter_after_encoding', 'default'),
  as('json-after-json', '{{ field.name | json | json }}', 'text', 'filter_after_encoding', 'json', { nth: 1 }),
  as('two-filters-after-encoding', '{{ field.name | json | upper | lower }}', 'text', 'filter_after_encoding', 'upper'),
  t('an-unknown-filter-after-encoding', '{{ field.name | url | shout }}', 'text', '{{ field.name | url | shout }}', [
    ['filter_after_encoding', 'shout'],
    ['unknown_filter', 'shout'],
  ]),
  as('an-encoding-filter-after-encoding', '{{ field.name | url | html }}', 'html', 'filter_after_encoding', 'html'),
  t('url-encodes-other-digits', '{{ field.digits | url }}', 'text', '%D9%A3', [], { sources: { 'field.digits': x('{663}') } }),
  t('url-encodes-each-utf-8-length-at-its-edges', '{{ field.edges | url }}', 'text', '%7F%C2%80%DF%BF%E0%A0%80%EF%BF%BF%F0%90%80%80', [], {
    sources: { 'field.edges': x('{7f}{80}{7ff}{800}{ffff}{10000}') },
  }),
];

// ---- The explicit filters in each context, over "a&b" ----
const JSON_AMP = '"a' + BS + 'u0026b"';
const explicit = [];
// Each context: the text around the tag, and what html, url and json give there (null: refused there).
const contexts = {
  html: ['', '', 'a&amp;b', 'a%26b', '&quot;a' + BS + 'u0026b&quot;'],
  text: ['', '', null, 'a%26b', JSON_AMP],
  header: ['', '', null, 'a%26b', JSON_AMP],
  url: ['https://x.test/?q=', '', null, 'a%26b', '%22a%5Cu0026b%22'],
  form: ['q=', '', null, 'a%2526b', '%22a%5Cu0026b%22'],
  jsonString: ['', '', null, 'a%26b', null],
  xml: ['<n>', '</n>', null, 'a%26b', '&quot;a' + BS + 'u0026b&quot;'],
};
for (const [context, [before, after, ...outputs]] of Object.entries(contexts)) {
  ['html', 'url', 'json'].forEach((filter, k) => {
    const text = before + '{{ field.amp | ' + filter + ' }}' + after;
    const output = outputs[k];
    const id = 'explicit-' + filter + '-in-' + context;
    explicit.push(
      output === null
        ? as(id, text, context, 'filter_not_for_context', filter)
        : t(id, text, context, before + output + after),
    );
  });
}
explicit.push(
  as('explicit-html-in-json', '{"a": {{ field.amp | html }}}', 'json', 'filter_not_for_context', 'html'),
  t('explicit-url-outside-a-json-string', '{"a": {{ field.amp | url }}}', 'json', '{"a": "a%26b"}'),
  t('explicit-json-outside-a-json-string', '{"a": {{ field.amp | json }}}', 'json', '{"a": ' + JSON_AMP + '}'),
  t('explicit-url-inside-a-json-string', '{"a": "{{ field.amp | url }}"}', 'json', '{"a": "a%26b"}'),
  as('explicit-json-inside-a-json-string', '{"a": "{{ field.amp | json }}"}', 'json', 'filter_not_for_context', 'json'),
  as('explicit-html-inside-a-json-string', '{"a": "{{ field.amp | html }}"}', 'json', 'filter_not_for_context', 'html'),
);

// ---- Each context's encoder ----
const XML_UNSAFE = x('a{00}{01}{08}') + TAB + LF + CR + x('{0b}{0c}{0e}{1f}{fffe}{ffff}{fffd}') + EMOJI + 'b';
const R = REPLACEMENT;
const encoders = [
  t('html-context-escapes-each', '{{ field.markup }}', 'html', '&lt;b class=&quot;x&quot;&gt;Hi&lt;/b&gt; &amp; &#39;you&#39;'),
  t('html-context-leaves-the-template-alone', '<p class="a">{{ field.company }} &amp;</p>', 'html', '<p class="a">Lund &amp; Co &amp;</p>'),
  t('xml-context-escapes-each', '<a>{{ field.markup }}</a>', 'xml', '<a>&lt;b class=&quot;x&quot;&gt;Hi&lt;/b&gt; &amp; &apos;you&apos;</a>'),
  t('xml-context-replaces-what-xml-cannot-carry', '<a>{{ field.unsafe }}</a>', 'xml', '<a>a' + R + R + R + TAB + LF + CR + R + R + R + R + R + R + R + EMOJI + 'b</a>', [], {
    sources: { 'field.unsafe': XML_UNSAFE },
  }),
  t('text-context-writes-values-as-they-are', '{{ field.markup }}', 'text', SOURCES['field.markup']),
  t('header-context-joins-lines', 'Re: {{ field.note }}', 'header', 'Re: line one line two three four'),
  t('header-context-trims-each-value', '[{{ field.padded }}]', 'header', '[a  b]', [], {
    sources: { 'field.padded': NBSP + ' a' + TAB + ' b' + LF + ' ' },
  }),
  t('header-context-other-breaks-and-controls', '{{ field.controls }}', 'header', 'a b c d e f g h', [], {
    sources: { 'field.controls': x('a{85}b{2028}c{2029}d{00}e{7f}f{9f}g{0b}h') },
  }),
  t('header-context-two-breaks', '{{ field.breaks }}', 'header', 'a b  c', [], { sources: { 'field.breaks': 'a' + CRLF + 'b' + LF + CR + 'c' } }),
  t('url-context-encodes-each', 'https://x.test/?q={{ field.reserved }}', 'url', 'https://x.test/?q=a%20b%2Fc%3Fd%3De%26f%23g%2Bh%25i'),
  t('form-context-encodes-each', 'q={{ field.formy }}', 'form', 'q=a+b*c%7Ed-e.f_g%2Bh%26i%3Dj%C3%A9', [], {
    sources: { 'field.formy': 'a b*c~d-e.f_g+h&i=j' + x('{e9}') },
  }),
  t('json-string-context-escapes', '{{ field.markup }}', 'jsonString', BS + 'u003cb class=' + BS + '"x' + BS + '"' + BS + 'u003eHi' + BS + 'u003c/b' + BS + 'u003e ' + BS + "u0026 'you'"),
  t('json-context-outside-a-string', '{"n": {{ field.name }}}', 'json', '{"n": "Anna"}'),
  t('json-context-inside-a-string', '{"n": "Hi {{ field.company }}"}', 'json', '{"n": "Hi Lund ' + BS + 'u0026 Co"}'),
];

// ---- Where a tag may stand ----
const placed = (id, text, context, expected) => t(id, text, context, expected);
const refused = (id, text, context, code, nth = 0, expected = text) => as(id, text, context, code, '{{', { nth, expected });
const markup = (id, text, nth = 0, expected = text) => refused(id, text, 'html', 'tag_in_markup', nth, expected);
const places = [
  placed('html-tag-in-text', '<p>{{ field.name }}</p>', 'html', '<p>Anna</p>'),
  markup('html-tag-in-a-double-quoted-attribute', '<a title="{{ field.name }}">'),
  markup('html-tag-in-a-single-quoted-attribute', "<a title='{{ field.name }}'>"),
  markup('html-tag-in-an-unquoted-attribute', '<a title={{ field.name }}>'),
  markup('html-tag-as-an-attribute', '<a {{ field.name }}>'),
  markup('html-tag-in-an-address', '<a href="{{ field.name }}">x</a>'),
  markup('html-tag-as-an-element-name', '<{{ field.name }}>'),
  markup('html-tag-as-an-end-tag-name', '</{{ field.name }}>'),
  placed('html-a-less-than-before-a-space-is-text', 'a < {{ field.name }}', 'html', 'a < Anna'),
  placed('html-a-less-than-before-a-digit-is-text', '1 <2 {{ field.name }}', 'html', '1 <2 Anna'),
  placed('html-tag-between-elements', '<br/>{{ field.name }}<hr>', 'html', '<br/>Anna<hr>'),
  markup('html-a-greater-than-in-a-quoted-attribute', '<a title="x>y">{{ field.name }}</a><a title="x>{{ field.name }}">', 1, '<a title="x>y">Anna</a><a title="x>{{ field.name }}">'),
  markup('html-tag-in-a-comment', '<!-- {{ field.name }} -->{{ field.name }}', 0, '<!-- {{ field.name }} -->Anna'),
  placed('html-a-comment-with-dashes', '<!-- a -- b --->{{ field.name }}', 'html', '<!-- a -- b --->Anna'),
  placed('html-an-empty-comment', '<!---->{{ field.name }}', 'html', '<!---->Anna'),
  placed('html-a-comment-closed-right-after-it-opens', '<!-->{{ field.name }}-->', 'html', '<!-->Anna-->'),
  markup('html-tag-in-a-script', '<script>var a = 1;{{ field.name }}</script>{{ field.name }}', 0, '<script>var a = 1;{{ field.name }}</script>Anna'),
  markup('html-tag-in-a-script-in-capitals', '<SCRIPT type="x">{{ field.name }}</Script>{{ field.name }}', 0, '<SCRIPT type="x">{{ field.name }}</Script>Anna'),
  markup('html-a-near-end-tag-does-not-end-a-script', '<script></scripts>{{ field.name }}</script >{{ field.name }}', 0, '<script></scripts>{{ field.name }}</script >Anna'),
  placed('html-a-less-than-inside-a-script', '<script>a<b</script>{{ field.name }}', 'html', '<script>a<b</script>Anna'),
  markup('html-tag-in-a-style', '<style>{{ field.name }}</style>'),
  markup('html-tag-in-a-textarea', '<textarea>{{ field.name }}</textarea>'),
  markup('html-tag-in-a-title', '<title>{{ field.name }}</title>'),
  markup('html-tag-after-plaintext', '<plaintext></plaintext>{{ field.name }}'),
  placed('html-tag-after-a-doctype', '<!DOCTYPE html>{{ field.name }}', 'html', '<!DOCTYPE html>Anna'),
  markup('html-tag-in-a-doctype', '<!DOCTYPE {{ field.name }}>'),
  markup('html-tag-in-a-question-mark-declaration', '<?x {{ field.name }}?>{{ field.name }}', 0, '<?x {{ field.name }}?>Anna'),
  placed('html-a-cdata-section-is-a-declaration', '<![CDATA[ a ]]>{{ field.name }}', 'html', '<![CDATA[ a ]]>Anna'),
  markup('html-tag-right-after-a-bang', '<!{{ field.name }}>'),
  markup('html-after-a-refused-tag', '<{{ field.name }} {{ field.name }}', 0, '<{{ field.name }} Anna'),
  markup('html-a-quote-in-an-attribute-name-opens-no-value', '<a x" onclick="if(a>b){{ field.name }}">'),
  markup('html-a-quote-after-an-attribute-without-a-value', '<a b c="x>{{ field.name }}">'),
  markup('html-a-quoted-value-after-spaces', '<a title = "x>{{ field.name }}">'),
  placed('html-a-quote-in-an-element-name', '<a"b>{{ field.name }}', 'html', '<a"b>Anna'),
  placed('html-a-quote-in-an-unquoted-value', '<p a=x"y>{{ field.name }}</p>', 'html', '<p a=x"y>Anna</p>'),
  placed('html-a-longer-name-is-not-a-script', '<script"x>{{ field.name }}', 'html', '<script"x>Anna'),
  markup('html-a-self-closing-script-still-is-one', '<script/>{{ field.name }}</script>{{ field.name }}', 0, '<script/>{{ field.name }}</script>Anna'),
  t('html-after-a-written-tag-that-opens-a-script', '{{ field.name | truncate: "<script>" }}{{ field.name }}', 'html', '{{ field.name | truncate: "<script>" }}{{ field.name }}', [
    ['invalid_argument', '"<script>"'],
    ['tag_in_markup', '{{', 1],
  ], { description: 'a tag with an error is written as it stands, so the scan reads the script it opens' }),
  placed('html-a-comment-closed-after-one-dash', '<!--->{{ field.name }}', 'html', '<!--->Anna'),
  markup('html-a-bang-right-after-a-comment-opens', '<!--!>{{ field.name }}-->'),
  markup('html-a-bang-after-one-opening-dash', '<!---!>{{ field.name }}-->'),
  placed('html-a-comment-closed-with-a-bang', '<!-- a --!>{{ field.name }}', 'html', '<!-- a --!>Anna'),
  markup('html-a-bang-and-one-dash-do-not-close', '<!-- a --!->{{ field.name }}-->'),
  placed('html-a-bang-and-two-dashes-close', '<!-- a --!-->{{ field.name }}', 'html', '<!-- a --!-->Anna'),
  markup('html-a-tag-after-a-comment-that-closed-at-once', '<!--><a title="-->{{ field.name }}">'),
  placed('html-a-slash-and-a-space-open-a-comment', '</ a="b>{{ field.name }}', 'html', '</ a="b>Anna'),
  placed('html-a-slash-and-a-digit-open-a-comment', '</1 a="b>{{ field.name }}', 'html', '</1 a="b>Anna'),
  markup('html-a-tag-after-a-slash-comment', '</ a="x><a title=">{{ field.name }}">'),
  placed('html-a-slash-and-a-greater-than-are-nothing', '</>{{ field.name }}', 'html', '</>Anna'),
  markup('html-an-escape-in-a-script-has-no-end', '<script><!--</script>{{ field.name }}'),
  markup('html-an-escape-after-two-less-thans', '<script><<!--</script>{{ field.name }}'),
  placed('html-half-an-escape-in-a-script', '<script><!-</script>{{ field.name }}', 'html', '<script><!-</script>Anna'),
  placed('html-an-escape-start-in-a-style-is-text', '<style><!--</style>{{ field.name }}', 'html', '<style><!--</style>Anna'),
  placed('html-a-near-end-tag-before-a-less-than', '<script></script<</script>{{ field.name }}', 'html', '<script></script<</script>Anna'),
  markup('html-an-escape-right-after-a-near-end-tag', '<script></script<!--</script>{{ field.name }}'),
  placed('html-a-script-end-tag-with-a-slash', '<script></script/>{{ field.name }}', 'html', '<script></script/>Anna'),
  markup('html-a-style-in-svg-may-hold-no-less-than', '<svg><style><a title="</style>{{ field.name }}">'),
  placed('html-a-style-in-svg-without-markup', '<svg><style>a{}</style></svg>{{ field.name }}', 'html', '<svg><style>a{}</style></svg>Anna'),
  markup('html-svg-leaves-doubt-to-the-end', '<svg></svg><style>a<b</style>{{ field.name }}'),
  placed('html-a-less-than-in-a-style-without-svg', '<style>a<b</style>{{ field.name }}', 'html', '<style>a<b</style>Anna'),
  markup('html-a-title-in-math-may-hold-no-less-than', '<math><title><</title>{{ field.name }}'),
  markup('html-a-raw-element-after-select', '<select><style>a<b</style>{{ field.name }}'),
  markup('html-a-raw-element-after-frameset', '<FRAMESET><style>a<b</style>{{ field.name }}'),
  markup('html-a-noscript-may-hold-no-less-than', '<noscript><p>x</p></noscript>{{ field.name }}'),
  placed('html-a-noscript-without-markup', '<noscript>x</noscript>{{ field.name }}', 'html', '<noscript>x</noscript>Anna'),
  markup('html-a-near-end-tag-in-svg', '<svg><style></stylex></style>{{ field.name }}'),
  placed('html-a-near-end-tag-without-svg', '<style></stylex></style>{{ field.name }}', 'html', '<style></stylex></style>Anna'),
  markup('html-a-cdata-section-after-svg', '<svg><![CDATA[ a ]]></svg>{{ field.name }}'),
  placed('html-half-a-cdata-start-after-svg', '<svg><![CDATA></svg>{{ field.name }}', 'html', '<svg><![CDATA></svg>Anna'),
  placed('html-a-less-than-before-an-underscore-is-text', '<_a b="{{ field.name }}">', 'html', '<_a b="Anna">'),
  markup('html-tag-in-a-slash-comment', '</ x {{ field.name }}>'),
  markup('html-one-dash-does-not-close-a-comment', '<!-- a -> {{ field.name }} -->'),
  markup('html-one-dash-and-a-bang-do-not-close', '<!-- a -!>{{ field.name }}-->'),
  markup('html-dashes-then-text-do-not-close', '<!-- a -- b>{{ field.name }}-->'),
  markup('html-a-greater-than-in-a-comment', '<!-- a > b {{ field.name }} -->'),
  markup('html-a-less-than-before-a-tag', '<<a title="{{ field.name }}">'),
  markup('html-tag-in-a-raw-end-tag', '<script></script a="{{ field.name }}">'),
  placed('html-two-less-thans-before-a-script-end-tag', '<script><</script>{{ field.name }}', 'html', '<script><</script>Anna'),
  placed('html-a-longer-name-is-not-plaintext', '<plaintexts>{{ field.name }}', 'html', '<plaintexts>Anna'),
  placed('html-a-slash-before-an-equals-sign', '<a / ="x>{{ field.name }}', 'html', '<a / ="x>Anna'),
  placed('html-an-equals-sign-after-spaces-names-an-attribute', '<a  = "x>{{ field.name }}', 'html', '<a  = "x>Anna'),
  placed('html-a-slash-ends-an-attribute-name', '<a b/="x>{{ field.name }}', 'html', '<a b/="x>Anna'),
  markup('html-the-other-quote-in-a-quoted-value', '<p a="x' + "'" + '>{{ field.name }}">'),
  markup('html-a-greater-than-in-a-single-quoted-attribute', "<a title='x>{{ field.name }}'>"),
  placed('html-an-equals-sign-without-a-value', '<a b=>{{ field.name }}', 'html', '<a b=>Anna'),
  markup('html-a-quoted-value-after-an-unquoted-one', '<a b=c d="x>{{ field.name }}">'),
  placed('html-an-empty-bang', '<!>{{ field.name }}', 'html', '<!>Anna'),
  placed('html-a-stray-end-tag-of-a-raw-element', '</title>{{ field.name }}', 'html', '</title>Anna'),
  markup('html-tag-in-an-xmp', '<xmp>{{ field.name }}</xmp>'),
  markup('html-tag-in-an-iframe', '<iframe>{{ field.name }}</iframe>'),
  markup('html-tag-in-a-noembed', '<noembed>{{ field.name }}</noembed>'),
  markup('html-tag-in-a-noframes', '<noframes>{{ field.name }}</noframes>'),
  placed('html-a-question-mark-declaration-ends-at-the-first-greater-than', '<?x > {{ field.name }}', 'html', '<?x > Anna'),
  markup('html-a-script-that-starts-with-its-name-after-a-comment', '<!-- --><script>script>{{ field.name }}</script>'),
  placed('html-an-attribute-without-a-value', '<input disabled>{{ field.name }}', 'html', '<input disabled>Anna'),
  markup('html-a-tab-is-a-space', '<a' + TAB + 'title="x>{{ field.name }}">'),
  markup('html-a-line-feed-is-a-space', '<a' + LF + 'title="x>{{ field.name }}">'),
  markup('html-a-form-feed-is-a-space', '<a' + x('{0c}') + 'title="x>{{ field.name }}">'),
  markup('html-a-carriage-return-is-a-space', '<a' + CR + 'title="x>{{ field.name }}">'),
  placed('xml-tag-in-text', '<n>{{ field.name }}</n>', 'xml', '<n>Anna</n>'),
  refused('xml-tag-in-an-attribute', '<n a="{{ field.name }}"/>', 'xml', 'tag_in_markup'),
  refused('xml-tag-in-a-comment', '<!-- {{ field.name }} -->', 'xml', 'tag_in_markup'),
  refused('xml-tag-in-cdata', '<![CDATA[ {{ field.name }} ]]>{{ field.name }}', 'xml', 'tag_in_markup', 0, '<![CDATA[ {{ field.name }} ]]>Anna'),
  refused('xml-a-greater-than-in-cdata', '<![CDATA[ a > b {{ field.name }} ]]>', 'xml', 'tag_in_markup'),
  placed('xml-tag-after-a-declaration', '<?xml version="1.0"?><n>{{ field.name }}</n>', 'xml', '<?xml version="1.0"?><n>Anna</n>'),
  refused('xml-tag-in-a-processing-instruction', '<?pi a>b {{ field.name }} ?>{{ field.name }}', 'xml', 'tag_in_markup', 0, '<?pi a>b {{ field.name }} ?>Anna'),
  placed('xml-a-script-is-an-element-like-any', '<script>{{ field.name }}</script>', 'xml', '<script>Anna</script>'),
  placed('xml-tag-after-a-doctype', '<!DOCTYPE n><n>{{ field.name }}</n>', 'xml', '<!DOCTYPE n><n>Anna</n>'),
  refused('xml-any-other-less-than-opens-a-tag', '<_a b="{{ field.name }}"/>', 'xml', 'tag_in_markup'),
  placed('xml-a-greater-than-in-a-doctype-literal', '<!DOCTYPE n SYSTEM "a>b"><n>{{ field.name }}</n>', 'xml', '<!DOCTYPE n SYSTEM "a>b"><n>Anna</n>'),
  placed('xml-a-bracket-in-a-doctype-literal', "<!DOCTYPE n SYSTEM '['><n>{{ field.name }}</n>", 'xml', "<!DOCTYPE n SYSTEM '['><n>Anna</n>"),
  refused('xml-tag-after-an-internal-subset', '<!DOCTYPE n [<!ENTITY a "b">]><n>{{ field.name }}</n>', 'xml', 'tag_in_markup'),
  refused('xml-a-greater-than-right-after-a-comment-opens', '<!-->{{ field.name }}-->', 'xml', 'tag_in_markup'),
  refused('xml-a-bang-does-not-end-a-comment', '<!-- a --!>{{ field.name }}', 'xml', 'tag_in_markup'),
  placed('xml-a-slash-and-a-space', '</ a="b>{{ field.name }}', 'xml', '</ a="b>Anna'),
  refused('xml-a-declaration-may-start-with-a-literal', '<n><!"a>{{ field.name }}"></n>', 'xml', 'tag_in_markup'),
  refused('xml-tag-in-a-doctype-literal', '<!DOCTYPE n SYSTEM "a>{{ field.name }}">', 'xml', 'tag_in_markup'),
  placed('xml-an-empty-declaration', '<!><n>{{ field.name }}</n>', 'xml', '<!><n>Anna</n>'),
  placed('xml-a-doctype-after-a-quoted-attribute', '<a b="c"/><!DOCTYPE n><n>{{ field.name }}</n>', 'xml', '<a b="c"/><!DOCTYPE n><n>Anna</n>'),
  placed('xml-a-cut-cdata-start', '<![CDATA><n>{{ field.name }}</n>', 'xml', '<![CDATA><n>Anna</n>'),
  placed('json-tags-outside-strings', '[{{ field.name }}, {{ field.qty }}, {{ field.extras }}, {{ field.none }}]', 'json', '["Anna", 1234.5, ["Wifi","Parking"], []]'),
  placed('json-a-list-inside-a-string', '{"a": "{{ field.extras }}"}', 'json', '{"a": "Wifi, Parking"}'),
  placed('json-a-number-item-inside-a-string', '{"q": "{{ field.qty }}"}', 'json', '{"q": "1' + QUOTE + '234.5"}'),
  refused('json-tag-after-a-backslash', '{"a": "x' + BS + '{{ field.name }}"}', 'json', 'tag_in_escape'),
  placed('json-tag-after-an-escaped-quote', '{"a": "x' + BS + '"{{ field.name }}"}', 'json', '{"a": "x' + BS + '"Anna"}'),
  placed('json-tag-after-an-escaped-backslash', '{"a": "x' + BS + BS + '{{ field.name }}"}', 'json', '{"a": "x' + BS + BS + 'Anna"}'),
  placed('json-a-string-ends-at-its-quote', '{"a": "x", "b": {{ field.qty }}}', 'json', '{"a": "x", "b": 1234.5}'),
  t('json-after-a-written-tag-the-scan-reads-its-quotes', '{"a": "{{ field.name | default: "' + BS + '"" | json }}", "b": {{ field.qty }}}', 'json', '{"a": "{{ field.name | default: "' + BS + '"" | json }}", "b": 1' + QUOTE + '234.5}', [
    ['filter_not_for_context', 'json'],
  ], { description: 'the written tag leaves its three quotes in the output, so the tag after it is inside a string' }),
  refused('json-string-tag-after-a-backslash', 'x' + BS + '{{ field.name }}', 'jsonString', 'tag_in_escape'),
  placed('json-string-after-its-own-quote', 'x", "y": {{ field.qty }}, "z": "{{ field.name }}', 'jsonString', 'x", "y": 1234.5, "z": "Anna'),
  placed('url-tag-in-the-path', 'https://x.test/a/{{ field.name }}', 'url', 'https://x.test/a/Anna'),
  placed('url-tag-in-the-query', 'https://x.test/?n={{ field.company }}', 'url', 'https://x.test/?n=Lund%20%26%20Co'),
  placed('url-tag-in-the-fragment', 'https://x.test#{{ field.name }}', 'url', 'https://x.test#Anna'),
  placed('url-tag-in-a-query-without-a-path', 'https://x.test?{{ field.name }}', 'url', 'https://x.test?Anna'),
  refused('url-tag-in-the-host', 'https://{{ field.name }}.x.test/', 'url', 'tag_before_path'),
  refused('url-tag-at-the-end-of-the-host', 'https://x.test{{ field.name }}/', 'url', 'tag_before_path'),
  refused('url-tag-in-the-port', 'https://x.test:{{ field.name }}/', 'url', 'tag_before_path'),
  refused('url-tag-in-the-user', 'https://{{ field.name }}@x.test/', 'url', 'tag_before_path'),
  refused('url-tag-in-the-scheme', 'ht{{ field.name }}tps://x.test/', 'url', 'tag_before_path'),
  refused('url-tag-at-the-start', '{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-after-spaces-at-the-start', '  {{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-after-a-root-slash', '/{{ field.name }}', 'url', 'tag_before_path'),
  placed('url-tag-in-a-root-path', '/thanks/{{ field.name }}', 'url', '/thanks/Anna'),
  placed('url-tag-after-the-first-character-of-the-path', '/t{{ field.name }}', 'url', '/tAnna'),
  placed('url-tag-in-a-query-alone', '?n={{ field.name }}', 'url', '?n=Anna'),
  placed('url-tag-in-a-fragment-alone', '#{{ field.name }}', 'url', '#Anna'),
  placed('url-tag-in-a-relative-path', 'thanks/{{ field.name }}', 'url', 'thanks/Anna'),
  refused('url-tag-in-a-relative-name', 'thanks{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-in-a-relative-name-with-other-characters', 'th!nks{{ field.name }}', 'url', 'tag_before_path'),
  placed('url-tag-after-a-scheme-relative-host', '//x.test/{{ field.name }}', 'url', '//x.test/Anna'),
  refused('url-tag-in-a-scheme-relative-host', '//{{ field.name }}/', 'url', 'tag_before_path'),
  placed('url-tag-after-backslashes', 'https:' + BS + BS + 'x.test' + BS + '{{ field.name }}', 'url', 'https:' + BS + BS + 'x.test' + BS + 'Anna'),
  refused('url-tag-in-a-host-after-backslashes', 'https:' + BS + BS + '{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-in-a-host-after-one-slash', 'https:/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-in-a-host-after-the-colon', 'https:{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-in-a-host-after-three-slashes', 'https:///{{ field.name }}', 'url', 'tag_before_path'),
  placed('url-a-scheme-in-capitals', 'HTTPS://x.test/{{ field.name }}', 'url', 'HTTPS://x.test/Anna'),
  placed('url-http', 'http://x.test/{{ field.name }}', 'url', 'http://x.test/Anna'),
  refused('url-another-scheme', 'ftp://x.test/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-script-address', 'javascript:{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-script-address-with-a-slash', 'javascript:void(0)/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-mail-address', 'mailto:{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-longer-scheme', 'https2://x.test/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-tab-inside-the-scheme', 'java' + TAB + 'script:{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-line-break-inside-the-scheme', 'ht' + LF + 'tps://{{ field.name }}', 'url', 'tag_before_path'),
  placed('url-leading-spaces-and-controls', ' ' + x('{01}') + 'https://x.test/{{ field.name }}', 'url', ' ' + x('{01}') + 'https://x.test/Anna'),
  refused('url-tag-after-a-slash-a-tab-and-a-slash', '/' + TAB + '/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-after-a-slash-a-line-feed-and-a-slash', '/' + LF + '/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-after-a-slash-a-carriage-return-and-a-slash', '/' + CR + '/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-in-a-host-after-a-space', ' //{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-in-a-host-after-two-backslashes', BS + BS + '{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-scheme-with-a-plus', 'h+ttp://x.test/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-scheme-with-a-dash', 'h-ttp://x.test/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-scheme-with-a-point', 'h.ttp://x.test/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-scheme-with-a-digit', 'h1ttp://x.test/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-a-host-without-slashes', 'https:x.test/{{ field.name }}', 'url', 'tag_before_path'),
  placed('url-tag-in-a-query-after-two-slashes', '//?{{ field.name }}', 'url', '//?Anna'),
  refused('url-tag-after-a-backslash-and-a-slash', 'https://' + BS + '/{{ field.name }}', 'url', 'tag_before_path'),
  refused('url-tag-after-a-slash-and-a-backslash', '/' + BS + '{{ field.name }}', 'url', 'tag_before_path'),
  t('url-after-a-refused-tag', 'https://{{ field.name }}/{{ field.name }}', 'url', 'https://{{ field.name }}/Anna', [
    ['tag_before_path', '{{'],
  ], { description: 'a refused tag is written as it stands, so the scan reads it as the host and the slash after it starts the path' }),
  placed('text-context-anywhere', '<a href="{{ field.name }}">', 'text', '<a href="Anna">'),
  placed('header-context-anywhere', '<{{ field.name }}>', 'header', '<Anna>'),
  placed('form-context-anywhere', '{{ field.name }}={{ field.company }}', 'form', 'Anna=Lund+%26+Co'),
];

// ---- Lists in each context ----
const LISTS = { 'field.items': ['a<b c', 'd&e'] };
const lists = [
  t('a-list-in-html', '{{ field.items }}', 'html', 'a&lt;b c, d&amp;e', [], { sources: LISTS }),
  t('a-list-in-xml', '<n>{{ field.items }}</n>', 'xml', '<n>a&lt;b c, d&amp;e</n>', [], { sources: LISTS }),
  t('a-list-in-a-header', '{{ field.items }}', 'header', 'a<b c, d&e', [], { sources: LISTS }),
  t('a-list-in-a-url', '/?q={{ field.items }}', 'url', '/?q=a%3Cb%20c%2C%20d%26e', [], { sources: LISTS }),
  t('a-list-through-url-in-a-url', '/?q={{ field.items | url }}', 'url', '/?q=a%3Cb%20c%2C%20d%26e', [], { sources: LISTS }),
  t('a-list-in-a-form', 'q={{ field.items }}', 'form', 'q=a%3Cb+c%2C+d%26e', [], { sources: LISTS }),
  t('a-list-in-a-json-string', '{{ field.items }}', 'jsonString', 'a' + BS + 'u003cb c, d' + BS + 'u0026e', [], { sources: LISTS }),
  t('a-list-outside-a-json-string', '{{ field.items }}', 'json', '["a' + BS + 'u003cb c","d' + BS + 'u0026e"]', [], { sources: LISTS }),
  t('a-joined-list-outside-a-json-string', '{{ field.items | join: "+" }}', 'json', '"a' + BS + 'u003cb c+d' + BS + 'u0026e"', [], { sources: LISTS }),
];

// ---- The limits ----
const twoHundred = '{{ field.name }}'.repeat(200);
const big = 'a'.repeat(99999);
const limits = [
  t('two-hundred-tags', twoHundred, 'text', 'Anna'.repeat(200)),
  t('the-two-hundred-and-first-tag-ends-the-output', twoHundred + 'tail {{ field.name }} more', 'text', 'Anna'.repeat(200) + 'tail ', [
    ['too_many_tags', '{{', 200],
  ]),
  t(
    'broken-tags-count-too',
    '{{ Bad }}'.repeat(200) + '{{ field.name }}',
    'text',
    '{{ Bad }}'.repeat(200),
    [...Array.from({ length: 200 }, (_, k) => ['malformed_tag', 'Bad', k]), ['too_many_tags', '{{', 200]],
  ),
  t('ten-filters', '{{ field.name' + ' | upper | lower'.repeat(5) + ' }}', 'text', 'anna'),
  t('the-eleventh-filter-ends-the-output', 'a {{ field.name' + ' | upper'.repeat(11) + ' }} b', 'text', 'a ', [['too_many_filters', '|', 10]]),
  t('the-longest-output-ends-where-it-is-reached', '{{ field.big }}{{ field.one }}{{ field.one }} {{ form.x }}', 'text', big + 'b', [
    ['too_long', '{{', 2],
  ], {
    sources: { 'field.big': big, 'field.one': 'b' },
    description: 'the second tag fills the output exactly; the third finds no room, and nothing after it renders',
  }),
  t('the-longest-output-never-splits-a-pair', big + EMOJI + 'b', 'text', big, [['too_long', 0]]),
  t('a-written-tag-at-the-limit-keeps-the-order-of-the-problems', '{{ field.big }}{{ field.name | upper: 2 }}', 'text', big + '{', [
    ['too_long', '{{', 1],
    ['invalid_argument', 'upper'],
  ], {
    sources: { 'field.big': big },
    description: 'a tag written as its text is cut at the limit too, after its own problem, which stands later in the template',
  }),
];

// ---- Answer recall (ROADMAP 5.3f, ADR-0068) ----
// A `recall` case renders a visitor text over the answers given so far, as the browser's runtime does with
// `recallAnswers` and the server with the visitor-text place: a form of the file's defaults (each field as both
// runtimes read it: its type, whether it is the built-in one, its kind, alias, label, whether it holds a value or
// parts, how recall shows it, its settings as the definition stores them, a text as an object of its cultures, and its
// options' and rows' labels in the culture), a set of answers of the file's defaults, the page, group or field whose
// text it is, the fields the rules hid, the text and its context. Fields, pages and groups have keys made from names.

/** FNV-1a over a text's code points, from a seed. */
function fnv(text, seed) {
  let hash = seed >>> 0;
  for (const character of text) {
    hash ^= character.codePointAt(0);
    hash = Math.imul(hash, 16777619) >>> 0;
  }
  return hash;
}

const keysByName = new Map();
/** The key of a field, page or group by its name: the same name, the same key, and no two names alike. */
function key(name) {
  const known = keysByName.get(name);
  if (known !== undefined) return known;
  const hex = (fnv(name, 2166136261).toString(16).padStart(8, '0') + fnv(name, 84696351).toString(16).padStart(8, '0')).slice(0, 12);
  const made = `00000000-0000-4000-8000-${hex}`;
  if ([...keysByName.values()].includes(made)) throw new Error(`the key of "${name}" is taken`);
  keysByName.set(name, made);
  return made;
}

/** The kind each built-in type's answers compare as (docs/api/field-types.json); none for the others. */
const KINDS = {
  text: 'text', textarea: 'text', email: 'text', phone: 'text', url: 'text', hidden: 'text',
  number: 'number', rating: 'number', scale: 'number', slider: 'number', calculation: 'number',
  date: 'date', time: 'time', datetime: 'dateTime',
  dropdown: 'choice', radio: 'choice', yesno: 'choice', country: 'choice',
  checkboxes: 'choices', ranking: 'choices', checkbox: 'checkbox', consent: 'checkbox',
  file: 'files', signature: 'files',
};
const WITH_PARTS = new Set(['name', 'address']);
const WITHOUT_VALUE = new Set(['heading', 'paragraph', 'divider', 'image', 'captcha']);

/** A built-in field by its alias and type, as the projection writes it; `more` gives its label, settings and the rest. */
function rf(alias, type, more = {}) {
  return {
    id: key(alias),
    type,
    builtIn: true,
    kind: KINDS[type] ?? null,
    alias,
    holdsValue: !WITHOUT_VALUE.has(type),
    ...(WITH_PARTS.has(type) ? { parts: true } : {}),
    ...(type === 'hidden' ? { recall: 'none' } : {}),
    ...more,
  };
}
const option = (value, label) => ({ value, label });
const gridRow = (name, label) => ({ id: key(name), label });
/** A text setting as the definition stores it: an object of cultures, the default one only. */
const text = (value) => ({ '': value });

defaults.words = {
  'de-CH': { yes: 'Ja', no: 'Nein' },
  'en-US': { yes: 'Yes', no: 'No' },
};

defaults.forms = {
  library: {
    pages: [
      {
        id: key('page-about'),
        groups: [
          {
            id: key('group-you'),
            fields: [
              rf('intro', 'heading', { alias: null }),
              rf('name', 'name', { label: 'Your name', settings: { parts: ['first', 'last'] } }),
              rf('email', 'email', { label: 'Email' }),
              rf('budget', 'number', { label: 'Budget', settings: { prefix: text(' CHF '), suffix: text(' net ') } }),
              rf('topic', 'radio', {
                label: 'Topic',
                settings: { other: true },
                options: [option('events', 'Events'), option('books', 'Books')],
              }),
              rf('interests', 'checkboxes', {
                label: 'Interests',
                options: [option('reading', 'Reading'), option('music', 'Music'), option('games', 'Games')],
              }),
              rf('card', 'text', { label: 'Library card', recall: 'masked' }),
              rf('campaign', 'hidden', { label: 'Campaign' }),
              rf('stamp', 'stamp', { builtIn: false, kind: 'choice', label: 'Stamp', options: [option('gold', 'Gold')] }),
              rf('weight', 'measure', { builtIn: false, kind: 'number', label: 'Weight' }),
              rf('badge', 'badge', { builtIn: false, parts: true, label: 'Badge' }),
            ],
          },
          {
            id: key('group-visit'),
            fields: [
              rf('day', 'date', { label: 'Day' }),
              rf('arrival', 'time', { label: 'Arrival' }),
              rf('start', 'datetime', { label: 'Start' }),
              rf('member', 'yesno', { label: 'Member', settings: { yesLabel: text('I am a member') } }),
              rf('newsletter', 'checkbox', { label: 'Newsletter' }),
              rf('country', 'country', { label: 'Country', options: [option('CH', 'Schweiz'), option('DE', 'Deutschland')] }),
              rf('address', 'address', { label: 'Address', options: [option('CH', 'Schweiz')] }),
              rf('service', 'grid', {
                label: 'Service',
                options: [option('good', 'Good'), option('poor', 'Poor')],
                rows: [gridRow('staff', 'Staff'), gridRow('rooms', 'Rooms')],
              }),
              rf('scores', 'grid', { label: 'Scores', settings: { scale: { from: 1, to: 5 } }, rows: [gridRow('food', 'Food')] }),
              rf('stars', 'rating'),
              rf('loudness', 'slider', { label: 'Loudness', settings: { unit: text('%') } }),
              rf('total', 'calculation', {
                label: 'Total',
                settings: { formula: 'budget * 2', format: { decimals: 0, prefix: text('CHF') } },
              }),
              rf('plain', 'calculation', { label: 'Plain', settings: { formula: 'budget / 3' } }),
              rf('files', 'file', { label: 'Files' }),
              rf('ranking', 'ranking', { label: 'Ranking', options: [option('events', 'Events'), option('books', 'Books')] }),
              rf('agree', 'consent', { label: 'Consent' }),
              rf('guests', 'repeater', {
                label: 'Guests',
                fields: [
                  rf('guestName', 'text', { label: 'Guest' }),
                  rf('age', 'number', { label: 'Age' }),
                  rf('ticket', 'text', { label: 'Ticket', recall: 'masked' }),
                ],
              }),
            ],
          },
        ],
      },
      {
        id: key('page-more'),
        groups: [{ id: key('group-later'), fields: [rf('remarks', 'textarea', { label: 'Remarks', formerAliases: ['notes'] })] }],
      },
    ],
  },
  english: {
    pages: [
      {
        id: key('page-english'),
        groups: [
          {
            id: key('group-english'),
            fields: [
              rf('cost', 'number', { label: 'Cost', settings: { prefix: text('USD') } }),
              rf('date', 'date', { label: 'Date' }),
              rf('at', 'time', { label: 'At' }),
              rf('when', 'datetime', { label: 'When' }),
              rf('optIn', 'checkbox', { label: 'Opt in' }),
              rf('club', 'yesno', { label: 'Club' }),
            ],
          },
        ],
      },
    ],
  },
  // A site whose own yes/no type replaces the built-in one, whose display the browser does not have.
  site: {
    pages: [
      {
        id: key('page-site'),
        groups: [
          {
            id: key('group-site'),
            fields: [rf('regular', 'yesno', { builtIn: false, label: 'Regular', settings: { yesLabel: text('A regular') } })],
          },
        ],
      },
    ],
  },
};

/** An answer of values in the wire form; `files` for a field that stores files. */
const answer = (...values) => ({ values: values.map((value) => (typeof value === 'string' ? { text: value } : value)) });
const NAME = '{"first":"Anna","last":"Muster"}';

const library = {
  [key('name')]: answer(NAME),
  [key('email')]: answer('anna@example.com'),
  [key('budget')]: answer('1234.5'),
  [key('topic')]: answer('events'),
  [key('interests')]: answer('reading', 'games'),
  [key('card')]: answer('4111'),
  [key('campaign')]: answer('spring'),
  [key('stamp')]: answer('gold'),
  [key('weight')]: answer('1234.5'),
  [key('badge')]: answer(NAME),
  [key('day')]: answer('2026-03-07'),
  [key('arrival')]: answer('14:30'),
  [key('start')]: answer('2026-03-07T09:05'),
  [key('member')]: answer('yes'),
  [key('newsletter')]: answer('true'),
  [key('country')]: answer('CH'),
  [key('address')]: answer('{"street":"Bahnhofstrasse 1","postcode":"8001","city":"Zurich","country":"CH"}'),
  [key('service')]: answer({ text: 'good', row: key('staff') }, { text: 'poor', row: key('rooms') }),
  [key('scores')]: answer({ text: '4', row: key('food') }),
  [key('stars')]: answer('4'),
  [key('loudness')]: answer('75'),
  [key('total')]: answer('2469'),
  [key('plain')]: answer('411.5'),
  [key('files')]: { ...answer('token-1'), files: [{ name: 'plan.pdf', size: 1200 }] },
  [key('ranking')]: answer('books', 'events'),
  [key('agree')]: answer('true'),
  [key('remarks')]: answer('Nothing'),
};
const guests = [
  { [key('guestName')]: answer('Ben'), [key('age')]: answer('7'), [key('ticket')]: answer('T-1') },
  {},
  { [key('guestName')]: answer('Cleo') },
];

defaults.answers = {
  library: { fields: library, rows: { [key('guests')]: guests } },
  other: {
    fields: {
      [key('name')]: answer('{"first":"Tom & Jerry"}'),
      [key('topic')]: answer('Tom & Jerry'),
      [key('member')]: answer('no'),
      [key('newsletter')]: answer('false'),
    },
  },
  none: {},
  english: {
    fields: {
      [key('cost')]: answer('1234.5'),
      [key('date')]: answer('2026-03-07'),
      [key('at')]: answer('14:30'),
      [key('when')]: answer('2026-03-07T21:05'),
      [key('optIn')]: answer('true'),
      [key('club')]: answer('no'),
    },
  },
  site: { fields: { [key('regular')]: answer('yes') } },
  // Values with parts as a client may store them but Core never writes: a name twice, names that look like numbers,
  // and an escape that decodes to a lone surrogate.
  parts: {
    fields: {
      [key('name')]: answer('{"first":"Anna","first":"Berta","last":"Muster"}'),
      [key('badge')]: answer(
        '{"b":"Bee","2":"Two","1":"One"}',
        '{"":"Empty"}',
        '{"y":""}',
        '{"n":1,"b":"B"}',
        '{}',
        '["x"]',
        x('{ {09}"w" :{0a}"W" , "v":"V" }'),
        x('{"q":"say {5c}"hi{5c}""}'),
        x('{"r":"R",{0d}"s":"S"}'),
        x('{"t":"x{5c}{5c}"}'),
        x('{"e":"{5c}ud83d{5c}ude00"}'),
        x('{"l":"{5c}udc00"}'),
        x('{"{5c}ud800":"x"}'),
      ),
      [key('address')]: answer(x('{"street":"Main 1","city":"{5c}ud800"}')),
    },
  },
};

const MASK = x('{2022}{2022}{2022}');

/**
 * A recall case: a visitor text, the expected text and its problems as `[code, token, nth]`; `more` gives the text's
 * owner by name (none for a text after every field), the names of the fields the rules hid, the form and answers by
 * name, the context, a culture or a description.
 */
function r(id, template, expected, problems = [], more = {}) {
  const { owner = null, hidden = [], form = 'library', answers = 'library', context = 'text', culture: name, description } = more;
  return {
    id,
    ...(description === undefined ? {} : { description }),
    ...(name === undefined ? {} : { context: { culture: name } }),
    recall: { form, answers, owner: owner === null ? null : key(owner), hidden: hidden.map(key), text: template, context },
    expect: {
      text: expected,
      problems: problems.map(([code, token, nth]) => ({ code, at: find(template, token, nth) })),
    },
  };
}

/** A case whose text recalls one tag after every field, and shows what it gives. */
const shows = (id, tag, expected, more = {}) => r(id, `[{{ ${tag} }}]`, `[${expected}]`, [], more);

const recalling = [
  // Where a text may look: fields before it in the order of the form.
  r('recall-a-field-above', 'Thanks, {{ field.name.first }}. Your email?', 'Thanks, Anna. Your email?', [], { owner: 'email' }),
  r('recall-a-field-on-an-earlier-page', '{{ field.name }} from {{ field.country }}', 'Anna Muster from Schweiz', [], {
    owner: 'remarks',
  }),
  r('recall-a-field-the-rules-hid-is-empty', 'Mail: {{ field.email }}.', 'Mail: .', [], { owner: 'remarks', hidden: ['email'] }),
  r('recall-a-later-field-is-refused', 'Hi {{ field.remarks }}!', 'Hi {{ field.remarks }}!', [['field_after_text', 'field.remarks']], {
    owner: 'email',
    description: 'written as it stands, as any tag with an error, in both runtimes',
  }),
  r('recall-the-field-itself-is-refused', 'Is {{ field.email }} right?', 'Is {{ field.email }} right?', [['field_after_text', 'field.email']], {
    owner: 'email',
  }),
  r('recall-a-later-fields-label-is-refused-too', '{{ field.remarks.label }}', '{{ field.remarks.label }}', [['field_after_text', 'field.remarks']], {
    owner: 'email',
  }),
  r(
    'recall-before-and-after-in-one-text',
    '{{ field.name.first }} / {{ field.remarks }} / {{ field.email }}',
    'Anna / {{ field.remarks }} / anna@example.com',
    [['field_after_text', 'field.remarks']],
    { owner: 'day' },
  ),
  r('recall-a-text-block-takes-the-fields-above-it', '{{ field.name }}', '{{ field.name }}', [['field_after_text', 'field.name']], {
    owner: 'intro',
    description: 'the heading comes before every field of its group',
  }),
  r('recall-a-page-title-takes-the-earlier-pages', '{{ field.name.first }}, one more page', 'Anna, one more page', [], {
    owner: 'page-more',
  }),
  r('recall-the-first-page-title-takes-nothing', '{{ field.name.first }}', '{{ field.name.first }}', [['field_after_text', 'field.name']], {
    owner: 'page-about',
  }),
  r(
    'recall-a-group-title-takes-the-groups-before-it',
    '{{ field.email }} / {{ field.day }}',
    'anna@example.com / {{ field.day }}',
    [['field_after_text', 'field.day']],
    { owner: 'group-visit' },
  ),
  r('recall-a-child-takes-a-child-before-it-across-the-rows', '{{ field.guestName }}', 'Ben, Cleo', [], { owner: 'age' }),
  r('recall-a-child-refuses-its-own-group', '{{ field.guests }}', '{{ field.guests }}', [['field_after_text', 'field.guests']], {
    owner: 'age',
    description: 'the repeatable group holds the text, so it does not come before it',
  }),
  r('recall-a-group-refuses-its-children', '{{ field.guests.age }}', '{{ field.guests.age }}', [['field_after_text', 'field.guests.age']], {
    owner: 'guests',
  }),
  r('recall-after-every-field', '{{ field.remarks }}', 'Nothing'),
  r('recall-an-owner-the-form-lacks-is-after-every-field', '{{ field.remarks }}', 'Nothing', [], { owner: 'nowhere' }),
  r('recall-names-fields-by-their-current-aliases', '[{{ field.notes }}]', '[]', [['unknown_path', 'field.notes']], {
    description: 'the former alias "notes" of remarks is not in the projection, so neither runtime resolves it',
  }),
  r('recall-an-unknown-field', '[{{ field.nope }}]', '[]', [['unknown_path', 'field.nope']]),
  r('recall-a-part-named-twice-reads-as-no-parts', '[{{ field.name }}][{{ field.name.first }}]', '[{"first":"Anna","first":"Berta","last":"Muster"}][]', [], {
    answers: 'parts',
  }),
  r(
    'recall-a-site-types-parts-as-core-reads-them',
    '[{{ field.badge }}][{{ field.badge.1 }}]',
    x('[Bee, Two, One, {"":"Empty"}, {"y":""}, {"n":1,"b":"B"}, {}, ["x"], W, V, say "hi", R, S, x{5c}, {1f600}, {"l":"{5c}udc00"}, {"{5c}ud800":"x"}][One]'),
    [],
    {
      answers: 'parts',
      description: 'names in their written order, those like numbers too; an empty name or text, a number, no part, an array, an escape that decodes to a lone surrogate in a text or a name: no parts, so the text as it is; JSON whitespace (a carriage return among it) and escapes (an escaped backslash before a closing quote among them) read',
    },
  ),
  r('recall-a-lone-surrogate-reads-as-no-parts', '[{{ field.address }}][{{ field.address.street }}]', x('[{"street":"Main 1","city":"{5c}ud800"}][]'), [], {
    answers: 'parts',
  }),
  r('recall-the-field-source-without-an-alias', '[{{ field }}][{{ field | upper }}]', '[][]', [
    ['unknown_path', 'field'],
    ['unknown_path', 'field', 1],
  ]),
  r('recall-a-path-a-field-lacks', '[{{ field.email.first }}][{{ field.guests.value }}][{{ field.name.first.x }}]', '[][][]', [
    ['unknown_path', 'field.email.first'],
    ['unknown_path', 'field.guests.value'],
    ['unknown_path', 'field.name.first.x'],
  ]),

  // What a field shows: masked, nothing, the rules' hiding.
  shows('recall-a-masked-field-once-answered', 'field.card', MASK),
  shows('recall-a-masked-field-without-an-answer-is-empty', 'field.card', '', { answers: 'other' }),
  shows('recall-a-masked-fields-value-is-masked-too', 'field.card.value', MASK),
  shows('recall-a-masked-child-once-answered', 'field.guests.ticket', MASK),
  shows('recall-a-masked-child-without-an-answer-is-empty', 'field.ticket', '', { answers: 'other' }),
  shows('recall-a-hidden-fields-value-stays-on-the-server', 'field.campaign', ''),
  shows('recall-a-hidden-fields-label-shows', 'field.campaign.label', 'Campaign'),
  shows('recall-a-group-the-rules-hid-hides-its-children', 'field.guestName', '', { hidden: ['guests'] }),
  shows('recall-a-group-the-rules-hid-is-no-list', 'field.guests | json', '""', { hidden: ['guests'] }),
  shows('recall-a-group-the-rules-hid-is-empty', 'field.guests', '', { hidden: ['guests'] }),
  shows('recall-a-child-the-rules-hid-is-empty', 'field.guests', 'Guest: Ben; Ticket: ' + MASK + ', Guest: Cleo', { hidden: ['age'] }),
  shows('recall-an-unanswered-field-is-empty', 'field.email', '', { answers: 'none' }),

  // The answers as their types write them.
  shows('recall-a-name-by-its-parts', 'field.name', 'Anna Muster'),
  shows('recall-a-part', 'field.name.last', 'Muster'),
  shows('recall-a-part-not-given-is-empty', 'field.name.middle', ''),
  shows('recall-a-text-as-it-is', 'field.email', 'anna@example.com'),
  shows('recall-a-number-between-its-prefix-and-suffix', 'field.budget', 'CHF 1' + QUOTE + '234.5 net', {
    description: 'the prefix and the suffix are trimmed, then each is apart from the number by a space',
  }),
  shows('recall-a-numbers-wire-form-behind-it', 'field.budget | number: 0', '1' + QUOTE + '235'),
  shows('recall-a-numbers-value', 'field.budget.value', '1234.5'),
  shows('recall-a-numbers-value-with-the-number-behind', 'field.budget.value | number: 2', '1' + QUOTE + '234.50'),
  shows('recall-a-numbers-value-is-a-number-in-json', 'field.budget.value | json', '1234.5'),
  shows('recall-a-choice-by-its-label', 'field.topic', 'Events'),
  shows('recall-a-visitors-other-text', 'field.topic', 'Tom & Jerry', { answers: 'other' }),
  shows('recall-choices-as-a-list', 'field.interests', 'Reading, Games'),
  shows('recall-choices-joined', 'field.interests | join: " + "', 'Reading + Games'),
  shows('recall-choices-values', 'field.interests.value | json', '["reading","games"]'),
  shows('recall-a-site-type-by-its-kind', 'field.stamp', 'Gold', {
    description: "the server's own display of the site's type is not used where the browser renders too",
  }),
  shows('recall-a-site-types-number-by-its-kind', 'field.weight', '1' + QUOTE + '234.5'),
  shows('recall-a-site-types-parts-by-its-kind', 'field.badge', 'Anna, Muster'),
  shows('recall-a-site-type-of-a-built-in-alias-by-its-kind', 'field.regular', 'yes', {
    form: 'site',
    answers: 'site',
    description: "a site's type that replaces a built-in one shows as a choice by default: the label of its option, here the value",
  }),
  shows('recall-a-date-in-the-short-format', 'field.day', '07.03.2026'),
  shows('recall-a-dates-wire-form-behind-it', 'field.day | date: "long"', 'Samstag, 7. M' + x('{e4}') + 'rz 2026'),
  shows('recall-a-dates-value-with-the-date-behind', 'field.day.value | date: "long"', 'Samstag, 7. M' + x('{e4}') + 'rz 2026'),
  shows('recall-a-time', 'field.arrival', '14:30'),
  shows('recall-a-date-and-time', 'field.start', '07.03.2026 09:05'),
  shows('recall-a-yes-by-its-label', 'field.member', 'I am a member'),
  shows('recall-a-no-without-a-label-by-the-word', 'field.member', 'Nein', { answers: 'other' }),
  shows('recall-a-ticked-box', 'field.newsletter', 'Ja'),
  shows('recall-an-unticked-box', 'field.newsletter', 'Nein', { answers: 'other' }),
  shows('recall-a-consent', 'field.agree', 'Ja'),
  shows('recall-a-country-by-its-name', 'field.country', 'Schweiz'),
  shows('recall-an-address-on-one-line', 'field.address', 'Bahnhofstrasse 1, 8001 Zurich, Schweiz'),
  shows('recall-a-grid-by-its-rows', 'field.service', 'Staff: Good, Rooms: Poor'),
  shows('recall-a-grid-of-a-scale', 'field.scores', 'Food: 4'),
  shows('recall-a-grid-of-one-row-is-a-list', 'field.scores | json', '["Food: 4"]'),
  shows('recall-a-rating', 'field.stars', '4'),
  shows('recall-a-label-or-the-alias', 'field.stars.label', 'stars'),
  shows('recall-a-slider-with-its-unit', 'field.loudness', '75 %'),
  shows('recall-a-calculation-in-its-format', 'field.total', 'CHF 2' + QUOTE + '469'),
  shows('recall-a-calculation-with-two-places', 'field.plain', '411.50'),
  shows('recall-a-file-by-its-name', 'field.files', 'plan.pdf'),
  shows('recall-a-ranking-in-its-order', 'field.ranking', 'Books, Events'),
  shows('recall-a-repeatable-group-by-its-rows', 'field.guests', 'Guest: Ben; Age: 7; Ticket: ' + MASK + ', Guest: Cleo'),
  shows('recall-a-child-across-the-rows', 'field.guests.guestName', 'Ben, Cleo'),
  shows('recall-a-child-by-its-own-alias', 'field.age', '7'),
  shows('recall-a-groups-label', 'field.guests.label', 'Guests'),
  shows('recall-a-childs-label', 'field.guests.age.label', 'Age'),
  r('recall-escaped-in-html', '<p>{{ field.topic }} and {{ field.name }}</p>', '<p>Tom &amp; Jerry and Tom &amp; Jerry</p>', [], {
    answers: 'other',
    context: 'html',
  }),
  shows('recall-in-another-culture-a-number', 'field.cost', 'USD 1,234.5', { form: 'english', answers: 'english', culture: 'en-US' }),
  shows('recall-in-another-culture-a-date', 'field.date', '3/7/2026', { form: 'english', answers: 'english', culture: 'en-US' }),
  shows('recall-in-another-culture-a-time', 'field.at', '2:30 PM', { form: 'english', answers: 'english', culture: 'en-US' }),
  shows('recall-in-another-culture-a-date-and-time', 'field.when', '3/7/2026 9:05 PM', {
    form: 'english',
    answers: 'english',
    culture: 'en-US',
  }),
  shows('recall-in-another-culture-the-words', 'field.optIn', 'Yes', { form: 'english', answers: 'english', culture: 'en-US' }),
  shows('recall-in-another-culture-a-no', 'field.club', 'No', { form: 'english', answers: 'english', culture: 'en-US' }),
];

export default {
  suite: 'merge-tags',
  defaults,
  fields: {},
  cases: [
    ...grammar,
    ...malformed,
    ...problems,
    ...values,
    ...casing,
    ...truncating,
    ...dating,
    ...numbering,
    ...currencies,
    ...joining,
    ...encoding,
    ...explicit,
    ...encoders,
    ...places,
    ...lists,
    ...limits,
    ...recalling,
  ],
};
