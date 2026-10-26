// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The validation vectors of ROADMAP 5.2b (section 4.3.3, ADR-0058, ADR-0062): each built-in type's checks of its
// settings at their edges, run by both runners over the same answers. A field starts from the public settings its
// type has defaults for, read from the reviewed snapshot of the field types endpoint (docs/api/field-types.json), as
// the public projection writes them; a case adds its own. Every expectation is written out here, never computed by
// the code under test. Special characters are built from code points, so this file stays ASCII.
import { readFileSync } from 'node:fs';

const defaults = {
  context: {
    // A Monday in Zurich.
    now: '2026-06-15T10:00:00Z',
    timeZone: 'Europe/Zurich',
    culture: 'de-CH',
    member: { signedIn: false, groups: [] },
    query: {},
    hidden: [],
  },
};

const served = JSON.parse(readFileSync(new URL('../../../docs/api/field-types.json', import.meta.url), 'utf8')).types;

/** A field of a built-in type: the public settings its type has defaults for, then the case's own. */
function field(type, settings = {}, more = {}) {
  const served1 = served.find((each) => each.alias === type);
  if (served1 === undefined) throw new Error(`no built-in type ${type}`);
  const effective = {};
  for (const setting of served1.settings) {
    if (setting.isPublic && setting.default !== null) effective[setting.name] = setting.default;
  }
  const all = { ...effective, ...settings };
  // A field with no settings has none at all, as the projection writes it.
  const given = Object.keys(all).length === 0 ? {} : { settings: all };
  return { type, ...(served1.ruleKind === null ? {} : { kind: served1.ruleKind }), ...more, ...given };
}

const u = (...codePoints) => String.fromCodePoint(...codePoints);
const NBSP = u(0xa0);
const IDEOGRAPHIC_SPACE = u(0x3000);
const ZERO_WIDTH_SPACE = u(0x200b);
const EMOJI = u(0x1f600);
const SHARP_S = u(0xdf);

/** A case: the field, its answers, and the errors expected. */
function c(id, f, values, errors, more = {}) {
  const { parts, context, description } = more;
  return {
    id,
    ...(description === undefined ? {} : { description }),
    ...(context === undefined ? {} : { context }),
    field: f,
    answers: parts === undefined ? { values } : { values, parts },
    expect: { errors, checks: [] },
  };
}

const at = (now, timeZone) => ({ now, timeZone });

// ---- Required, and the types without checks of their own ----
const required = [
  c('required-text-without-answer', field('text', {}, { required: true }), [], ['required']),
  c('optional-text-without-answer', field('text'), [], []),
  c('required-text-with-answer', field('text', {}, { required: true }), ['Anna'], []),
  c('required-date-without-answer', field('date', {}, { required: true }), [], ['required']),
  c('optional-number-without-answer-ignores-its-limits', field('number', { min: '5' }), [], []),
  c('phone-has-no-check-of-its-own', field('phone'), ['+41 44 668 18 00'], []),
  c('checkbox-has-no-check-of-its-own', field('checkbox'), ['true'], []),
  c('yesno-has-no-check-of-its-own', field('yesno'), ['no'], []),
  c('signature-has-no-check-of-its-own', field('signature'), ['token-1'], []),
  c('consent-has-no-check-of-its-own', field('consent'), ['true'], [], { parts: { wording: ['0123456789abcdef'.repeat(4)] } }),
  c('hidden-has-no-check-of-its-own', field('hidden'), ['newsletter'], []),
];

// ---- text: lengths in UTF-16 code units ----
const text = [
  c('text-within-its-lengths', field('text', { minLength: 2, maxLength: 5 }), ['abcde'], []),
  c('text-at-its-min-length', field('text', { minLength: 2, maxLength: 5 }), ['ab'], []),
  c('text-below-its-min-length', field('text', { minLength: 2, maxLength: 5 }), ['a'], ['minLength']),
  c('text-above-its-max-length', field('text', { minLength: 2, maxLength: 5 }), ['abcdef'], ['maxLength']),
  c('text-default-max-length-holds-255', field('text'), ['a'.repeat(255)], []),
  c('text-default-max-length-refuses-256', field('text'), ['a'.repeat(256)], ['maxLength']),
  c('text-an-emoji-is-two-code-units', field('text', { maxLength: 1 }), [EMOJI], ['maxLength'], {
    description: 'browsers count maxlength in UTF-16 code units, so a character outside the BMP counts twice',
  }),
  c('text-an-emoji-fits-two-code-units', field('text', { maxLength: 2 }), [EMOJI], []),
  c('text-limits-the-wrong-way-round', field('text', { minLength: 5, maxLength: 3 }), ['abcd'], ['minLength'], {
    description: 'publishing refuses limits the wrong way round (ADR-0056); the preview of a draft reports the first',
  }),
  c('text-a-combining-mark-counts', field('text', { maxLength: 1 }), ['x' + u(0x301)], ['maxLength'], {
    description: 'no character composes x with an acute, so NFC leaves two code units',
  }),
];

// ---- textarea: lengths, then words between the whitespace cleaning trims ----
const textarea = [
  c('textarea-words-within', field('textarea', { minWords: 2, maxWords: 3 }), ['one two'], []),
  c('textarea-words-at-the-most', field('textarea', { minWords: 2, maxWords: 3 }), ['one two three'], []),
  c('textarea-too-few-words', field('textarea', { minWords: 2, maxWords: 3 }), ['one'], ['minWords']),
  c('textarea-too-many-words', field('textarea', { minWords: 2, maxWords: 3 }), ['one two three four'], ['maxWords']),
  c('textarea-words-across-lines-and-tabs', field('textarea', { maxWords: 3 }), ['one\ntwo\tthree\r\nfour'], ['maxWords']),
  c('textarea-runs-of-spaces-are-one-gap', field('textarea', { minWords: 2, maxWords: 2 }), ['one   ' + NBSP + '  two'], []),
  c('textarea-the-ideographic-space-parts-words', field('textarea', { maxWords: 1 }), ['one' + IDEOGRAPHIC_SPACE + 'two'], ['maxWords']),
  c('textarea-a-no-break-space-parts-words', field('textarea', { maxWords: 1 }), ['one' + NBSP + 'two'], ['maxWords']),
  c('textarea-a-zero-width-space-parts-no-words', field('textarea', { maxWords: 1 }), ['one' + ZERO_WIDTH_SPACE + 'two'], [], {
    description: 'U+200B is not whitespace that cleaning trims, so the two halves are one word',
  }),
  c('textarea-a-hyphen-joins-a-word', field('textarea', { maxWords: 1 }), ['well-known'], []),
  c('textarea-punctuation-alone-is-a-word', field('textarea', { minWords: 3 }), ['yes - no'], []),
  c('textarea-word-limits-the-wrong-way-round', field('textarea', { minWords: 3, maxWords: 1 }), ['one two'], ['minWords'], {
    description: 'publishing refuses limits the wrong way round (ADR-0056); the preview of a draft reports the first',
  }),
  c('textarea-lengths-then-words', field('textarea', { minLength: 5, minWords: 2 }), ['abc'], ['minLength', 'minWords']),
  c('textarea-default-max-length-holds-4000', field('textarea'), ['a'.repeat(4000)], []),
  c('textarea-default-max-length-refuses-4001', field('textarea'), ['a'.repeat(4001)], ['maxLength']),
];

// ---- email: the address typed again, compared as rules compare text ----
const email = [
  c('email-without-confirm-ignores-the-part', field('email'), ['anna@example.com'], [], { parts: { confirm: ['other@example.com'] } }),
  c('email-confirmed', field('email', { confirm: true }), ['anna@example.com'], [], { parts: { confirm: ['anna@example.com'] } }),
  c('email-confirmed-in-another-case', field('email', { confirm: true }), ['Anna@Example.com'], [], { parts: { confirm: ['anna@EXAMPLE.COM'] } }),
  c('email-confirmation-missing', field('email', { confirm: true }), ['anna@example.com'], ['confirmation']),
  c('email-confirmation-differs', field('email', { confirm: true }), ['anna@example.com'], ['confirmation'], { parts: { confirm: ['ana@example.com'] } }),
  c('email-confirmation-given-twice', field('email', { confirm: true }), ['anna@example.com'], ['confirmation'], {
    parts: { confirm: ['anna@example.com', 'anna@example.com'] },
  }),
  c('email-sharp-s-never-folds-to-ss', field('email', { confirm: true }), ['stra' + SHARP_S + 'e@example.de'], ['confirmation'], {
    parts: { confirm: ['STRASSE@example.de'] },
  }),
  c('email-sharp-s-matches-itself', field('email', { confirm: true }), ['stra' + SHARP_S + 'e@example.de'], [], {
    parts: { confirm: ['STRA' + u(0x1e9e) + 'E@example.de'] },
    description: 'the capital sharp s folds to the small one',
  }),
];

// ---- url: the scheme, in ASCII lowercase, among the allowed ones ----
const url = [
  c('url-https-by-default', field('url'), ['https://example.com/path'], []),
  c('url-scheme-in-capitals', field('url'), ['HTTPS://example.com'], []),
  c('url-http-not-allowed-by-default', field('url'), ['http://example.com'], ['invalid']),
  c('url-http-allowed', field('url', { schemes: ['https', 'http'] }), ['http://example.com'], []),
  c('url-without-a-scheme', field('url'), ['example.com'], ['invalid']),
  c('url-another-scheme', field('url', { schemes: ['https', 'http'] }), ['ftp://example.com'], ['invalid']),
  c('url-scheme-before-the-first-colon', field('url'), ['mailto:https://example.com'], ['invalid']),
  c('url-a-port-after-the-scheme', field('url'), ['https://example.com:8443/path'], []),
  c('url-empty-scheme', field('url'), ['://example.com'], ['invalid']),
];

// ---- number: limits and the step, exactly ----
const number = [
  c('number-within-its-limits', field('number', { min: '1', max: '10' }), ['5'], []),
  c('number-at-its-minimum', field('number', { min: '1', max: '10' }), ['1'], []),
  c('number-at-its-maximum', field('number', { min: '1', max: '10' }), ['10'], []),
  c('number-below-its-minimum', field('number', { min: '1', max: '10', decimals: 2 }), ['0.99'], ['min']),
  c('number-above-its-maximum', field('number', { min: '1', max: '10', decimals: 2 }), ['10.01'], ['max']),
  c('number-negative-minimum', field('number', { min: '-5', decimals: 1 }), ['-5.5'], ['min']),
  c('number-negative-at-its-minimum', field('number', { min: '-5' }), ['-5'], []),
  c('number-whole-numbers-by-default', field('number'), ['2.5'], ['step'], { description: 'decimals 0 is the step 1' }),
  c('number-one-place', field('number', { decimals: 1 }), ['2.5'], []),
  c('number-more-places-than-its-decimals', field('number', { decimals: 1 }), ['2.55'], ['step']),
  c('number-places-from-a-minimum-with-places', field('number', { min: '0.5', decimals: 1 }), ['0.7'], []),
  c('number-step-from-its-minimum', field('number', { min: '1', step: '3' }), ['4'], []),
  c('number-off-the-step-from-its-minimum', field('number', { min: '1', step: '3' }), ['5'], ['step']),
  c('number-step-at-its-minimum', field('number', { min: '1', step: '3' }), ['1'], []),
  c('number-below-its-minimum-on-the-step', field('number', { min: '1', step: '3' }), ['-2'], ['min']),
  c('number-step-from-zero', field('number', { step: '0.25', decimals: 2 }), ['0.75'], []),
  c('number-off-a-step-from-zero', field('number', { step: '0.25', decimals: 2 }), ['0.8'], ['step']),
  c('number-negative-on-a-step-from-zero', field('number', { step: '0.25', decimals: 2 }), ['-0.5'], []),
  c('number-below-and-off-the-step', field('number', { min: '10', step: '5' }), ['3'], ['min', 'step']),
  c('number-limits-the-wrong-way-round', field('number', { min: '5', max: '3' }), ['4'], ['min'], {
    description: 'publishing refuses limits the wrong way round (ADR-0056); the preview of a draft reports the first',
  }),
  c('number-a-step-of-zero-limits-nothing', field('number', { step: '0' }), ['2.5'], [], {
    description: 'publishing refuses it (ADR-0056); the preview of a draft checks nothing with it',
  }),
  c('number-a-step-that-is-no-number-limits-nothing', field('number', { step: 'x' }), ['2.5'], [], {
    description: 'publishing refuses it (ADR-0056); the preview of a draft checks nothing with it',
  }),
  c('number-limits-that-are-no-numbers-limit-nothing', field('number', { min: 'x', max: 'y' }), ['-5.5'], [], {
    description: 'publishing refuses it (ADR-0056); the preview of a draft checks nothing with it',
  }),
  c('number-fifteen-digits-at-its-maximum', field('number', { max: '999999999999999' }), ['999999999999999'], []),
  c('number-places-and-a-large-step', field('number', { step: '0.001', decimals: 3 }), ['12345678901.234'], []),
];

// ---- slider: its limits and its step from its minimum ----
const slider = [
  c('slider-default-limits', field('slider'), ['50'], []),
  c('slider-default-maximum', field('slider'), ['100'], []),
  c('slider-above-its-default-maximum', field('slider'), ['101'], ['max']),
  c('slider-below-its-default-minimum', field('slider'), ['-1'], ['min']),
  c('slider-off-its-default-step', field('slider'), ['50.5'], ['step']),
  c('slider-step-from-its-minimum', field('slider', { min: '10', max: '50', step: '5' }), ['15'], []),
  c('slider-off-the-step-from-its-minimum', field('slider', { min: '10', max: '50', step: '5' }), ['17'], ['step']),
  c('slider-half-steps', field('slider', { min: '0', max: '1', step: '0.5' }), ['0.5'], []),
];

// ---- rating and scale: whole numbers in their range ----
const choices = [
  c('rating-lowest', field('rating'), ['1'], []),
  c('rating-highest-by-default', field('rating'), ['5'], []),
  c('rating-zero', field('rating'), ['0'], ['invalid']),
  c('rating-above-its-count', field('rating'), ['6'], ['invalid']),
  c('rating-a-half', field('rating'), ['2.5'], ['invalid']),
  c('rating-a-fraction-of-digits-in-range', field('rating'), ['0.5'], ['invalid'], {
    description: 'five tenths: its digits, 5, are in range, its value is not whole',
  }),
  c('rating-negative', field('rating'), ['-1'], ['invalid']),
  c('rating-three-symbols', field('rating', { count: 3 }), ['4'], ['invalid']),
  c('rating-ten-symbols', field('rating', { count: 10 }), ['10'], []),
  c('scale-from-zero', field('scale'), ['0'], []),
  c('scale-to-ten', field('scale'), ['10'], []),
  c('scale-above-its-end', field('scale'), ['11'], ['invalid']),
  c('scale-below-its-start', field('scale'), ['-1'], ['invalid']),
  c('scale-a-half', field('scale'), ['5.5'], ['invalid']),
  c('scale-a-fraction-of-digits-in-range', field('scale'), ['0.5'], ['invalid']),
  c('scale-from-one-refuses-zero', field('scale', { from: 1, to: 5 }), ['0'], ['invalid']),
  c('scale-from-one-to-five', field('scale', { from: 1, to: 5 }), ['5'], []),
  c('scale-above-five', field('scale', { from: 1, to: 5 }), ['6'], ['invalid']),
];

// ---- date: the window, weekdays and excluded dates; today is 2026-06-15, a Monday ----
const date = [
  c('date-at-its-fixed-minimum', field('date', { min: '2026-06-01', max: '2026-06-30' }), ['2026-06-01'], []),
  c('date-before-its-fixed-minimum', field('date', { min: '2026-06-01', max: '2026-06-30' }), ['2026-05-31'], ['dateWindow']),
  c('date-at-its-fixed-maximum', field('date', { min: '2026-06-01', max: '2026-06-30' }), ['2026-06-30'], []),
  c('date-after-its-fixed-maximum', field('date', { min: '2026-06-01', max: '2026-06-30' }), ['2026-07-01'], ['dateWindow']),
  c('date-at-a-minimum-two-days-ahead', field('date', { min: { today: '+2d' } }), ['2026-06-17'], []),
  c('date-before-a-minimum-two-days-ahead', field('date', { min: { today: '+2d' } }), ['2026-06-16'], ['dateWindow']),
  c('date-at-a-maximum-a-month-ahead', field('date', { max: { today: '+1m' } }), ['2026-07-15'], []),
  c('date-after-a-maximum-a-month-ahead', field('date', { max: { today: '+1m' } }), ['2026-07-16'], ['dateWindow']),
  c('date-today-at-the-minimum', field('date', { min: { today: '' } }), ['2026-06-15'], []),
  c('date-yesterday-before-today', field('date', { min: { today: '' } }), ['2026-06-14'], ['dateWindow']),
  c('date-today-in-auckland-is-tomorrow', field('date', { min: { today: '' } }), ['2026-06-15'], ['dateWindow'], {
    context: at('2026-06-15T23:30:00Z', 'Pacific/Auckland'),
    description: '23:30 UTC is 11:30 on the 16th in Auckland',
  }),
  c('date-today-in-auckland', field('date', { min: { today: '' } }), ['2026-06-16'], [], { context: at('2026-06-15T23:30:00Z', 'Pacific/Auckland') }),
  c('date-today-in-new-york-is-yesterday', field('date', { max: { today: '' } }), ['2026-06-16'], ['dateWindow'], {
    context: at('2026-06-16T02:00:00Z', 'America/New_York'),
    description: '02:00 UTC is 22:00 on the 15th in New York',
  }),
  c('date-a-month-ahead-clamps-to-the-end-of-february', field('date', { min: { today: '+1m' } }), ['2026-02-28'], [], {
    context: at('2026-01-31T12:00:00Z', 'Europe/Zurich'),
  }),
  c('date-before-a-clamped-month', field('date', { min: { today: '+1m' } }), ['2026-02-27'], ['dateWindow'], {
    context: at('2026-01-31T12:00:00Z', 'Europe/Zurich'),
  }),
  c('date-weekdays-take-a-friday', field('date', { weekdaysOnly: true }), ['2026-06-12'], []),
  c('date-weekdays-take-a-monday', field('date', { weekdaysOnly: true }), ['2026-06-15'], []),
  c('date-weekdays-refuse-a-saturday', field('date', { weekdaysOnly: true }), ['2026-06-13'], ['dateWindow']),
  c('date-weekdays-refuse-a-sunday', field('date', { weekdaysOnly: true }), ['2026-06-14'], ['dateWindow']),
  c('date-weekdays-take-the-first-day', field('date', { weekdaysOnly: true }), ['0001-01-01'], [], { description: 'a Monday' }),
  c('date-weekdays-take-the-last-day', field('date', { weekdaysOnly: true }), ['9999-12-31'], [], { description: 'a Friday' }),
  c('date-weekdays-refuse-a-sunday-in-1969', field('date', { weekdaysOnly: true }), ['1969-12-28'], ['dateWindow']),
  c('date-weekdays-refuse-a-saturday-in-1969', field('date', { weekdaysOnly: true }), ['1969-12-27'], ['dateWindow']),
  c('date-weekdays-refuse-a-saturday-in-1970', field('date', { weekdaysOnly: true }), ['1970-01-03'], ['dateWindow']),
  c('date-weekdays-take-1970-01-01', field('date', { weekdaysOnly: true }), ['1970-01-01'], [], { description: 'a Thursday' }),
  c('date-weekdays-refuse-a-saturday-in-year-one', field('date', { weekdaysOnly: true }), ['0001-01-06'], ['dateWindow']),
  c('date-excluded', field('date', { excluded: ['2026-12-24', '2026-12-25'] }), ['2026-12-25'], ['dateWindow']),
  c('date-not-excluded', field('date', { excluded: ['2026-12-24', '2026-12-25'] }), ['2026-12-26'], []),
  c('date-a-maximum-past-the-calendar-takes-nothing', field('date', { max: { today: '+9999y' } }), ['2026-06-15'], ['dateWindow']),
  c('date-a-maximum-past-the-calendar-takes-no-day-before-1970', field('date', { max: { today: '+9999y' } }), ['1969-12-31'], ['dateWindow']),
  c('date-a-relative-limit-without-an-offset-takes-nothing', field('date', { min: { today: 'soon' } }), ['2026-06-15'], ['dateWindow']),
  c('date-a-limit-of-another-shape-takes-nothing', field('date', { min: { today: 2 } }), ['2026-06-15'], ['dateWindow']),
  c('date-a-minimum-before-the-calendar-takes-nothing', field('date', { min: { today: '-9999y' } }), ['2026-06-15'], ['dateWindow']),
  c('date-a-limit-that-is-no-date-takes-nothing', field('date', { min: '2026-02-30' }), ['2026-06-15'], ['dateWindow'], {
    description: 'publishing refuses such a limit; a draft may hold it',
  }),
  c('date-a-week-back', field('date', { max: { today: '-1w' } }), ['2026-06-08'], []),
  c('date-after-a-week-back', field('date', { max: { today: '-1w' } }), ['2026-06-09'], ['dateWindow']),
  c('date-every-failure-is-one-error', field('date', { max: '2026-06-01', weekdaysOnly: true, excluded: ['2026-06-13'] }), ['2026-06-13'], ['dateWindow']),
];

// ---- time: limits to the second, and steps from the earliest time or midnight ----
const time = [
  c('time-at-its-earliest', field('time', { min: '09:00', max: '17:00' }), ['09:00'], []),
  c('time-before-its-earliest', field('time', { min: '09:00', max: '17:00' }), ['08:59'], ['min']),
  c('time-at-its-latest', field('time', { min: '09:00', max: '17:00' }), ['17:00'], []),
  c('time-at-its-latest-with-seconds', field('time', { min: '09:00', max: '17:00' }), ['17:00:00'], []),
  c('time-a-second-after-its-latest', field('time', { min: '09:00', max: '17:00' }), ['17:00:01'], ['max']),
  c('time-step-from-its-earliest', field('time', { min: '09:15', stepMinutes: 30 }), ['09:45'], []),
  c('time-off-the-step-from-its-earliest', field('time', { min: '09:15', stepMinutes: 30 }), ['10:00'], ['step']),
  c('time-before-its-earliest-on-the-step', field('time', { min: '09:15', stepMinutes: 30 }), ['08:45'], ['min']),
  c('time-step-from-midnight', field('time', { stepMinutes: 15 }), ['23:45'], []),
  c('time-midnight-on-the-step', field('time', { stepMinutes: 15 }), ['00:00'], []),
  c('time-off-a-step-from-midnight', field('time', { stepMinutes: 15 }), ['23:50'], ['step']),
  c('time-seconds-are-off-a-step', field('time', { stepMinutes: 15 }), ['10:15:30'], ['step']),
  c('time-seconds-without-a-step', field('time'), ['10:15:30'], []),
  c('time-limits-the-wrong-way-round', field('time', { min: '12:00', max: '10:00' }), ['11:00'], ['min'], {
    description: 'publishing refuses limits the wrong way round (ADR-0056); the preview of a draft reports the first',
  }),
  c('time-before-and-off-the-step', field('time', { min: '09:00', stepMinutes: 60 }), ['08:30'], ['min', 'step']),
];

// ---- datetime: the window of its date, and a step from midnight of its own day ----
const datetime = [
  c('datetime-the-last-minute-of-its-latest-day', field('datetime', { min: '2026-06-01', max: '2026-06-30' }), ['2026-06-30T23:59'], []),
  c('datetime-the-day-after-its-latest', field('datetime', { min: '2026-06-01', max: '2026-06-30' }), ['2026-07-01T00:00'], ['dateWindow']),
  c('datetime-the-last-second-before-its-earliest', field('datetime', { min: '2026-06-01', max: '2026-06-30' }), ['2026-05-31T23:59:59'], ['dateWindow']),
  c('datetime-weekdays-refuse-a-saturday', field('datetime', { weekdaysOnly: true }), ['2026-06-13T10:00'], ['dateWindow']),
  c('datetime-excluded', field('datetime', { excluded: ['2026-06-20'] }), ['2026-06-20T12:00'], ['dateWindow']),
  c('datetime-a-week-ahead', field('datetime', { min: { today: '+1w' } }), ['2026-06-22T00:00'], []),
  c('datetime-before-a-week-ahead', field('datetime', { min: { today: '+1w' } }), ['2026-06-21T23:59'], ['dateWindow']),
  c('datetime-on-a-half-hour', field('datetime', { stepMinutes: 30 }), ['2026-06-15T10:30'], []),
  c('datetime-off-a-half-hour', field('datetime', { stepMinutes: 30 }), ['2026-06-15T10:31'], ['step']),
  c('datetime-seconds-off-a-half-hour', field('datetime', { stepMinutes: 30 }), ['2026-06-15T10:30:15'], ['step']),
  c('datetime-a-step-that-misses-midnight', field('datetime', { stepMinutes: 7 }), ['2026-06-15T23:55'], [], { description: '1435 minutes is 205 steps of 7' }),
  c('datetime-each-day-starts-its-steps-again', field('datetime', { stepMinutes: 7 }), ['2026-06-16T00:07'], []),
  c('datetime-off-a-step-of-seven', field('datetime', { stepMinutes: 7 }), ['2026-06-15T23:58'], ['step']),
  c('datetime-outside-and-off-the-step', field('datetime', { max: '2026-06-30', stepMinutes: 30 }), ['2026-07-01T10:31'], ['dateWindow', 'step']),
];

export default {
  suite: 'validation',
  defaults,
  fields: {},
  cases: [...required, ...text, ...textarea, ...email, ...url, ...number, ...slider, ...choices, ...date, ...time, ...datetime],
};
