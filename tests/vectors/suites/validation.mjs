// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The validation vectors of ROADMAP 5.2b, 5.2c and 5.4a (section 4.3.3, ADR-0058, ADR-0062, ADR-0069): each built-in
// type's checks of its settings at their edges, the forms of an email address, a phone number and a web address, a
// number's stored digits, a grid's required rows and a ranking's positions, and a field's own pattern and
// matchesField checks, run by both runners over the same answers. A field starts from the public settings its
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

/**
 * A case: the field, its answers (values, parts and files), and the errors expected: the error kinds of its type's
 * checks and the indexes of its own failing checks. `others` are the form's other fields' values by id.
 */
function c(id, f, values, errors, more = {}) {
  const { parts, files, others, checks = [], context, description } = more;
  return {
    id,
    ...(description === undefined ? {} : { description }),
    ...(context === undefined ? {} : { context }),
    ...(others === undefined ? {} : { values: others }),
    field: f,
    answers: { values, ...(parts === undefined ? {} : { parts }), ...(files === undefined ? {} : { files }) },
    expect: { errors, checks },
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
  c('checkbox-has-no-check-of-its-own', field('checkbox'), ['true'], []),
  c('yesno-has-no-check-of-its-own', field('yesno'), ['no'], []),
  c('signature-has-no-check-of-its-own', field('signature'), ['token-1'], [], { files: [{ name: 'signature.png', size: 4096 }] }),
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
  c('text-sixteen-digits-are-text', field('text'), ['1234567890123456'], [], {
    description: 'only a field that compares as a number keeps to the digits a stored number holds',
  }),
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

// ---- email: its form (ADR-0069), then the address typed again, compared as rules compare text ----
const email = [
  c('email-the-plain-form', field('email'), ['anna@example.com'], []),
  c('email-dots-plus-and-subdomains', field('email'), ['anna.maria+news@mail.example.co.uk'], []),
  c('email-every-symbol-an-atom-takes', field('email'), ["!#$%&'*+-/=?^_`{|}~@example.com"], []),
  c('email-letters-of-other-scripts', field('email'), ['j' + u(0xf6) + 'rg@m' + u(0xfc) + 'nchen.de'], []),
  c('email-an-emoji-is-outside-ascii', field('email'), [EMOJI + '@example.com'], []),
  c('email-a-hyphen-inside-a-label', field('email'), ['anna@my-example.com'], []),
  c('email-digits-in-labels', field('email'), ['anna@123.example.com'], []),
  c('email-a-top-level-label-with-digits', field('email'), ['anna@example.xn--p1ai'], [], { description: 'only a last label of digits alone is refused' }),
  c('email-a-top-level-label-of-other-digits', field('email'), ['anna@example.' + u(0x663) + u(0x664)], [], {
    description: 'only ASCII digits count; the digits of other scripts are characters outside ASCII',
  }),
  c('email-the-letters-and-digits-at-the-ends', field('email'), ['azAZ09@zaZA90.example'], []),
  c('email-without-an-at', field('email'), ['anna.example.com'], ['invalid']),
  c('email-without-a-local-part', field('email'), ['@example.com'], ['invalid']),
  c('email-without-a-domain', field('email'), ['anna@'], ['invalid']),
  c('email-a-domain-of-one-label', field('email'), ['anna@localhost'], ['invalid']),
  c('email-two-ats', field('email'), ['anna@home@example.com'], ['invalid']),
  c('email-a-dot-first', field('email'), ['.anna@example.com'], ['invalid']),
  c('email-a-dot-last', field('email'), ['anna.@example.com'], ['invalid']),
  c('email-two-dots', field('email'), ['an..na@example.com'], ['invalid']),
  c('email-a-quoted-local-part', field('email'), ['"anna"@example.com'], ['invalid']),
  c('email-a-space', field('email'), ['an na@example.com'], ['invalid']),
  c('email-a-no-break-space', field('email'), ['an' + NBSP + 'na@example.com'], ['invalid'], {
    description: 'cleaning trims U+00A0, so it is a space and no letter',
  }),
  c('email-a-c1-control', field('email'), ['an' + u(0x80) + 'na@example.com'], ['invalid']),
  c('email-a-comma', field('email'), ['anna,maria@example.com'], ['invalid']),
  c('email-an-ip-literal', field('email'), ['anna@[192.168.0.1]'], ['invalid']),
  c('email-an-ip-address', field('email'), ['anna@192.168.0.1'], ['invalid'], { description: 'the last label is only digits' }),
  c('email-an-underscore-in-the-domain', field('email'), ['anna@my_example.com'], ['invalid']),
  c('email-a-hyphen-first-in-a-label', field('email'), ['anna@-example.com'], ['invalid']),
  c('email-a-hyphen-last-in-a-label', field('email'), ['anna@example-.com'], ['invalid']),
  c('email-an-empty-label', field('email'), ['anna@example..com'], ['invalid']),
  c('email-a-trailing-dot', field('email'), ['anna@example.com.'], ['invalid']),
  c('email-a-local-part-of-64', field('email'), ['a'.repeat(64) + '@example.com'], []),
  c('email-a-local-part-of-65', field('email'), ['a'.repeat(65) + '@example.com'], ['invalid']),
  c('email-a-label-of-63', field('email'), ['anna@' + 'b'.repeat(63) + '.com'], []),
  c('email-a-label-of-64', field('email'), ['anna@' + 'b'.repeat(64) + '.com'], ['invalid']),
  c('email-malformed-and-not-confirmed', field('email', { confirm: true }), ['anna'], ['invalid', 'confirmation']),
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

// ---- phone: a lenient international form (ADR-0069) ----
const phone = [
  c('phone-international', field('phone'), ['+41 44 668 18 00'], []),
  c('phone-a-trunk-prefix-in-brackets', field('phone'), ['+41 (0)44 668 18 00'], []),
  c('phone-national', field('phone'), ['044 668 18 00'], []),
  c('phone-an-international-prefix', field('phone'), ['0041 44 668 18 00'], []),
  c('phone-brackets-hyphens', field('phone'), ['+1 (555) 123-4567'], []),
  c('phone-dots', field('phone'), ['555.123.4567'], []),
  c('phone-a-slash', field('phone'), ['030/1234567'], []),
  c('phone-a-no-break-space', field('phone'), ['044' + NBSP + '668 18 00'], []),
  c('phone-brackets-first', field('phone'), ['(044) 668 18 00'], []),
  c('phone-four-digits', field('phone'), ['1234'], []),
  c('phone-three-digits', field('phone'), ['123'], ['invalid']),
  c('phone-seventeen-digits', field('phone'), ['12345678901234567'], []),
  c('phone-eighteen-digits', field('phone'), ['123456789012345678'], ['invalid']),
  c('phone-an-extension', field('phone'), ['+44 20 7946 0958 ext. 12'], []),
  c('phone-an-extension-in-capitals', field('phone'), ['+44 20 7946 0958 EXT 12'], []),
  c('phone-an-x-extension', field('phone'), ['+44 20 7946 0958x12'], []),
  c('phone-a-hash-extension', field('phone'), ['0800 123 456 # 99'], []),
  c('phone-an-extension-of-six', field('phone'), ['044 668 18 00 x 123456'], []),
  c('phone-an-extension-of-seven', field('phone'), ['044 668 18 00 x 1234567'], ['invalid']),
  c('phone-an-extension-without-digits', field('phone'), ['044 668 18 00 ext'], ['invalid']),
  c('phone-an-extension-of-other-digits', field('phone'), ['044 668 18 00 x ' + u(0x661) + u(0x662)], ['invalid'], {
    description: 'only ASCII digits make an extension',
  }),
  c('phone-the-extension-counts-no-digits', field('phone'), ['123 x 4567'], ['invalid'], {
    description: 'three digits before the extension are too few, whatever the extension holds',
  }),
  c('phone-letters', field('phone'), ['0800 FLOWERS'], ['invalid']),
  c('phone-a-plus-inside', field('phone'), ['41+44 668 18 00'], ['invalid']),
  c('phone-two-pluses', field('phone'), ['++41 44 668 18 00'], ['invalid']),
  c('phone-a-separator-first', field('phone'), ['-044 668 18 00'], ['invalid']),
  c('phone-a-separator-last', field('phone'), ['044 668 18 00-'], ['invalid']),
  c('phone-a-bracket-left-open', field('phone'), ['(044 668 18 00'], ['invalid']),
  c('phone-a-bracket-closed-unopened', field('phone'), ['044) 668 18 00'], ['invalid']),
  c('phone-empty-brackets', field('phone'), ['+41 ( ) 44 668 18 00'], ['invalid']),
  c('phone-two-pairs-of-brackets', field('phone'), ['(044) (668) 18 00'], ['invalid']),
  c('phone-two-opening-brackets', field('phone'), ['((044) 668 18 00'], ['invalid']),
  c('phone-a-hyphen-in-brackets', field('phone'), ['+41 (0-44) 668 18 00'], ['invalid']),
  c('phone-a-space-in-brackets', field('phone'), ['+1 ( 555 ) 123 4567'], []),
  c('phone-a-second-closing-bracket', field('phone'), ['+41 (0)44) 668 18 00'], ['invalid']),
];

// ---- url: the scheme, in ASCII lowercase, among the allowed ones, then the form of ADR-0069 ----
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
  c('url-a-path-query-and-fragment', field('url'), ['https://www.example.co.uk/a/b?c=d&e=f#g'], []),
  c('url-a-query-after-the-host', field('url'), ['https://example.com?q=1'], []),
  c('url-a-fragment-after-the-host', field('url'), ['https://example.com#top'], []),
  c('url-letters-of-other-scripts', field('url'), ['https://m' + u(0xfc) + 'nchen.de/caf' + u(0xe9)], []),
  c('url-an-encoded-space', field('url'), ['https://example.com/a%20b'], []),
  c('url-the-highest-port', field('url'), ['https://example.com:65535'], []),
  c('url-a-port-past-the-highest', field('url'), ['https://example.com:65536'], ['invalid']),
  c('url-port-zero', field('url'), ['https://example.com:0'], ['invalid']),
  c('url-an-empty-port', field('url'), ['https://example.com:/'], ['invalid']),
  c('url-a-port-of-six-digits', field('url'), ['https://example.com:000080'], ['invalid']),
  c('url-a-port-in-hexadecimal', field('url'), ['https://example.com:0x50'], ['invalid']),
  c('url-a-host-of-253', field('url'), ['https://' + ['a'.repeat(63), 'b'.repeat(63), 'c'.repeat(63), 'd'.repeat(61)].join('.') + '/'], []),
  c('url-a-host-of-254', field('url'), ['https://' + ['a'.repeat(63), 'b'.repeat(63), 'c'.repeat(63), 'd'.repeat(62)].join('.') + '/'], ['invalid']),
  c('url-without-the-slashes', field('url'), ['https:example.com'], ['invalid']),
  c('url-without-a-host', field('url'), ['https://'], ['invalid']),
  c('url-a-host-of-one-label', field('url'), ['https://localhost/'], ['invalid']),
  c('url-an-ip-address', field('url'), ['https://192.168.0.1/'], ['invalid']),
  c('url-a-trailing-dot', field('url'), ['https://example.com./'], ['invalid']),
  c('url-a-space-in-the-host', field('url'), ['https://exa mple.com'], ['invalid']),
  c('url-a-space-in-the-path', field('url'), ['https://example.com/a b'], ['invalid']),
  c('url-a-no-break-space-in-the-path', field('url'), ['https://example.com/a' + NBSP + 'b'], ['invalid']),
  c('url-delete-in-the-path', field('url'), ['https://example.com/a' + u(0x7f)], ['invalid']),
  c('url-a-c1-control-in-the-query', field('url'), ['https://example.com/?a=' + u(0x9f)], ['invalid']),
  c('url-a-control-character-in-the-fragment', field('url'), ['https://example.com/#a' + u(0x1f)], ['invalid']),
  c('url-a-user-name', field('url'), ['https://anna@example.com/'], ['invalid']),
  c('url-a-user-name-and-password', field('url'), ['https://anna:JFB_FAKE@example.com/'], ['invalid']),
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
  c('number-sixteen-digits', field('number'), ['1234567890123456'], ['digits']),
  c('number-sixteen-digits-negative', field('number'), ['-1234567890123456'], ['digits']),
  c('number-sixteen-digits-of-trailing-zeros', field('number'), ['1000000000000000'], ['digits']),
  c('number-sixteen-digits-with-places', field('number', { decimals: 15 }), ['1234567.123456789'], ['digits']),
  c('number-fifteen-places', field('number', { decimals: 15 }), ['0.000000000000001'], []),
  c('number-sixteen-places', field('number', { decimals: 15 }), ['0.0000000000000001'], ['digits']),
  c('number-sixty-digits', field('number'), ['1' + '0'.repeat(59)], ['digits']),
  c('number-digits-alone', field('number', { max: '10', step: '3' }), ['1234567890123456'], ['digits'], {
    description: 'a number a stored double cannot keep is reported alone: no limit or step is checked',
  }),
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
  c('slider-sixteen-digits-are-invalid', field('slider'), ['1234567890123456'], ['invalid'], {
    description: 'a slider has no kind of its own for too many digits, and its maximum is not checked',
  }),
];

// ---- rating and scale: whole numbers in their range ----
const ratings = [
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

// ---- 5.2c: ids in the D format, as Core's model has them ----
const PRICE_ROW = '0b6a4f1e-7c2d-4e8a-9f3b-5d1c2a7e6b40';
const STAFF_ROW = '1c7b5e2f-8d3e-4f9b-8a4c-6e2d3b8f7c51';
const UNKNOWN_ROW = '2d8c6f3a-9e4f-4a0c-9b5d-7f3e4c9a8d62';
const OTHER_FIELD = '3e9d7a4b-af5a-4b1d-8c6e-8a4f5dab9e73';

/** A file field's case: one upload token for each file, given as its name and its size in bytes. */
function uploads(id, f, files, errors, more = {}) {
  return c(id, f, files.map((_, i) => `token-${i + 1}`), errors, { ...more, files: files.map(([name, size]) => ({ name, size })) });
}

// ---- dropdown: each value an option, exactly; with multiple, the count within the selections ----
const COLOURS = ['red', 'green', 'blue', 'white'];
const dropdown = [
  c('dropdown-an-option', field('dropdown', {}, { options: COLOURS }), ['green'], []),
  c('dropdown-not-an-option', field('dropdown', {}, { options: COLOURS }), ['purple'], ['invalid']),
  c('dropdown-an-option-compares-exactly', field('dropdown', {}, { options: COLOURS }), ['Green'], ['invalid'], {
    description: 'an option value is what entries store, so it is compared exactly, not folded',
  }),
  c('dropdown-without-options-takes-nothing', field('dropdown', {}, { options: [] }), ['red'], ['invalid']),
  c('dropdown-a-source-without-its-options-takes-any', field('dropdown', {}, { fromSource: true }), ['purple'], [], {
    description: 'a list or another source that could not give its options holds the answers to none: the server takes them as given and logs the field (ADR-0077)',
  }),
  c('dropdown-several-at-their-fewest', field('dropdown', { multiple: true, minSelections: 2, maxSelections: 3 }, { options: COLOURS }), ['red', 'green'], []),
  c('dropdown-several-at-their-most', field('dropdown', { multiple: true, minSelections: 2, maxSelections: 3 }, { options: COLOURS }), ['red', 'green', 'blue'], []),
  c('dropdown-several-below-their-fewest', field('dropdown', { multiple: true, minSelections: 2 }, { options: COLOURS }), ['red'], ['selections']),
  c('dropdown-several-above-their-most', field('dropdown', { multiple: true, maxSelections: 3 }, { options: COLOURS }), ['red', 'green', 'blue', 'white'], ['selections']),
  c('dropdown-several-without-limits', field('dropdown', { multiple: true }, { options: COLOURS }), ['red', 'green', 'blue', 'white'], []),
  c('dropdown-several-not-an-option-and-too-many', field('dropdown', { multiple: true, maxSelections: 2 }, { options: COLOURS }), ['red', 'green', 'pink'], ['invalid', 'selections']),
  c('dropdown-several-from-a-source-without-its-options-still-count', field('dropdown', { multiple: true, maxSelections: 1 }, { fromSource: true }), ['red', 'green'], ['selections']),
  c('dropdown-one-choice-has-no-selections', field('dropdown', { minSelections: 2 }, { options: COLOURS }), ['red'], [], {
    description: 'publishing refuses selections without multiple (ADR-0056); a draft does not count them',
  }),
];

// ---- radio: an option, or the visitor's own "Other" text ----
const ANSWERS = ['yes', 'no'];
const radio = [
  c('radio-an-option', field('radio', {}, { options: ANSWERS }), ['no'], []),
  c('radio-not-an-option', field('radio', {}, { options: ANSWERS }), ['maybe'], ['invalid']),
  c('radio-the-other-text', field('radio', { other: true }, { options: ANSWERS }), [], [], { parts: { other: ['Now and then'] } }),
  c('radio-the-other-text-of-a-required-field', field('radio', { other: true }, { options: ANSWERS, required: true }), [], [], { parts: { other: ['Now and then'] } }),
  c('radio-the-other-text-equal-to-an-option', field('radio', { other: true }, { options: ANSWERS }), [], [], { parts: { other: ['no'] } }),
  c('radio-an-other-text-it-does-not-offer', field('radio', {}, { options: ANSWERS, required: true }), [], ['required'], {
    parts: { other: ['Now and then'] },
    description: 'without "Other" the text is no answer',
  }),
  c('radio-an-option-beside-an-other-text', field('radio', { other: true }, { options: ANSWERS }), ['yes'], [], {
    parts: { other: ['Now and then'] },
    description: 'one choice: the option is the answer, and the text is left',
  }),
  c('radio-not-an-option-beside-an-other-text', field('radio', { other: true }, { options: ANSWERS }), ['maybe'], ['invalid'], { parts: { other: ['maybe'] } }),
  c('radio-a-source-without-its-options-takes-any', field('radio', {}, { fromSource: true }), ['maybe'], []),
];

// ---- checkboxes: each an option, the count within the selections, the "Other" text as one ----
const CHANNELS = ['email', 'phone', 'post', 'visit'];
const checkboxes = [
  c('checkboxes-options', field('checkboxes', {}, { options: CHANNELS }), ['email', 'post'], []),
  c('checkboxes-not-an-option', field('checkboxes', {}, { options: CHANNELS }), ['email', 'fax'], ['invalid']),
  c('checkboxes-at-their-fewest', field('checkboxes', { minSelections: 2 }, { options: CHANNELS }), ['email', 'post'], []),
  c('checkboxes-below-their-fewest', field('checkboxes', { minSelections: 2 }, { options: CHANNELS }), ['email'], ['selections']),
  c('checkboxes-at-their-most', field('checkboxes', { maxSelections: 2 }, { options: CHANNELS }), ['email', 'phone'], []),
  c('checkboxes-above-their-most', field('checkboxes', { maxSelections: 2 }, { options: CHANNELS }), ['email', 'phone', 'post'], ['selections']),
  c('checkboxes-not-an-option-and-too-few', field('checkboxes', { minSelections: 3 }, { options: CHANNELS }), ['email', 'fax'], ['invalid', 'selections']),
  c('checkboxes-the-other-text-is-the-visitors-own', field('checkboxes', { other: true }, { options: CHANNELS }), ['email'], [], { parts: { other: ['fax'] } }),
  c('checkboxes-the-other-text-counts-as-one', field('checkboxes', { other: true, maxSelections: 2 }, { options: CHANNELS }), ['email', 'phone'], ['selections'], {
    parts: { other: ['pigeon'] },
  }),
  c('checkboxes-the-other-text-alone-at-their-fewest', field('checkboxes', { other: true, minSelections: 1 }, { options: CHANNELS }), [], [], { parts: { other: ['pigeon'] } }),
  c('checkboxes-an-other-text-repeating-an-option-counts-once', field('checkboxes', { other: true, maxSelections: 2 }, { options: CHANNELS }), ['email', 'phone'], [], {
    parts: { other: ['email'] },
  }),
  c('checkboxes-an-other-text-it-does-not-offer-is-left', field('checkboxes', { maxSelections: 2 }, { options: CHANNELS }), ['email', 'phone'], [], {
    parts: { other: ['pigeon'] },
  }),
  c('checkboxes-a-source-without-its-options-takes-any', field('checkboxes', { maxSelections: 3 }, { fromSource: true }), ['fax', 'telex'], []),
];

// ---- grid: each row's values among its columns, the scale's whole numbers or its options ----
const ROWS = [{ id: PRICE_ROW }, { id: STAFF_ROW }];
const STAFF_REQUIRED = [{ id: PRICE_ROW }, { id: STAFF_ROW, required: true }];
const AGREEMENT = ['agree', 'neutral', 'disagree'];
const grid = [
  c('grid-each-row-a-column', field('grid', {}, { options: AGREEMENT, rows: ROWS }), [], [], { parts: { [PRICE_ROW]: ['agree'], [STAFF_ROW]: ['disagree'] } }),
  c('grid-a-row-not-a-column', field('grid', {}, { options: AGREEMENT, rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['agree'], [STAFF_ROW]: ['strongly'] } }),
  c('grid-one-row-answered', field('grid', {}, { options: AGREEMENT, rows: ROWS }), [], [], { parts: { [STAFF_ROW]: ['neutral'] } }),
  c('grid-several-in-a-row', field('grid', { multiple: true }, { options: AGREEMENT, rows: ROWS }), [], [], { parts: { [PRICE_ROW]: ['agree', 'neutral'] } }),
  c('grid-several-in-a-row-one-not-a-column', field('grid', { multiple: true }, { options: AGREEMENT, rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['agree', 'maybe'] } }),
  c('grid-a-row-it-does-not-have-is-left', field('grid', {}, { options: AGREEMENT, rows: ROWS }), [], [], { parts: { [PRICE_ROW]: ['agree'], [UNKNOWN_ROW]: ['maybe'] } }),
  c('grid-only-a-row-it-does-not-have', field('grid', {}, { options: AGREEMENT, rows: ROWS, required: true }), [], ['required'], { parts: { [UNKNOWN_ROW]: ['agree'] } }),
  c('grid-required-without-answers', field('grid', {}, { options: AGREEMENT, rows: ROWS, required: true }), [], ['required']),
  c('grid-optional-without-answers', field('grid', {}, { options: AGREEMENT, rows: ROWS }), [], []),
  c('grid-the-ends-of-its-scale', field('grid', { scale: { from: 1, to: 5 } }, { rows: ROWS }), [], [], { parts: { [PRICE_ROW]: ['1'], [STAFF_ROW]: ['5'] } }),
  c('grid-above-its-scale', field('grid', { scale: { from: 1, to: 5 } }, { rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['6'] } }),
  c('grid-below-its-scale', field('grid', { scale: { from: 1, to: 5 } }, { rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['0'] } }),
  c('grid-a-scale-from-zero-to-ten', field('grid', { scale: { from: 0, to: 10 } }, { rows: ROWS }), [], [], { parts: { [PRICE_ROW]: ['0'], [STAFF_ROW]: ['10'] } }),
  c('grid-a-half-on-its-scale', field('grid', { scale: { from: 1, to: 5 } }, { rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['2.5'] } }),
  c('grid-a-fraction-of-digits-in-its-scale', field('grid', { scale: { from: 1, to: 5 } }, { rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['0.3'] } }),
  c('grid-a-word-on-its-scale', field('grid', { scale: { from: 1, to: 5 } }, { rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['good'] } }),
  c('grid-a-scale-and-options', field('grid', { scale: { from: 1, to: 3 } }, { options: AGREEMENT, rows: ROWS }), [], ['invalid'], {
    parts: { [PRICE_ROW]: ['agree'] },
    description: 'publishing refuses columns from both (ADR-0056); in a draft the scale decides',
  }),
  c('grid-a-source-without-its-options-takes-any', field('grid', {}, { fromSource: true, rows: ROWS }), [], [], { parts: { [PRICE_ROW]: ['whatever'] } }),
  c('grid-required-with-every-row-answered', field('grid', {}, { options: AGREEMENT, rows: ROWS, required: true }), [], [], {
    parts: { [PRICE_ROW]: ['agree'], [STAFF_ROW]: ['neutral'] },
  }),
  c('grid-required-with-a-row-missing', field('grid', {}, { options: AGREEMENT, rows: ROWS, required: true }), [], ['required'], {
    parts: { [PRICE_ROW]: ['agree'] },
    description: 'a required grid needs every row',
  }),
  c('grid-a-marked-row-missing', field('grid', {}, { options: AGREEMENT, rows: STAFF_REQUIRED }), [], ['required'], {
    parts: { [PRICE_ROW]: ['agree'] },
  }),
  c('grid-a-marked-row-answered-alone', field('grid', {}, { options: AGREEMENT, rows: STAFF_REQUIRED }), [], [], {
    parts: { [STAFF_ROW]: ['neutral'] },
  }),
  c('grid-a-marked-row-without-any-answer', field('grid', {}, { options: AGREEMENT, rows: STAFF_REQUIRED }), [], ['required'], {
    description: 'a row marked required needs an answer even when the grid is not required',
  }),
  c('grid-not-a-column-then-a-marked-row-missing', field('grid', {}, { options: AGREEMENT, rows: STAFF_REQUIRED }), [], ['invalid', 'required'], {
    parts: { [PRICE_ROW]: ['strongly'] },
  }),
  c('grid-required-over-a-row-marked-optional', field('grid', {}, { options: AGREEMENT, rows: [{ id: PRICE_ROW, required: false }, { id: STAFF_ROW }], required: true }), [], ['required'], {
    parts: { [STAFF_ROW]: ['agree'] },
  }),
  c('grid-a-marked-row-answered-on-its-scale', field('grid', { scale: { from: 1, to: 5 } }, { rows: STAFF_REQUIRED }), [], [], {
    parts: { [STAFF_ROW]: ['4'] },
  }),
];

// ---- ranking: each value an option, as many as top or as all the options ----
const ASPECTS = ['price', 'quality', 'service', 'speed'];
const ranking = [
  c('ranking-all-the-options', field('ranking', {}, { options: ASPECTS }), ['quality', 'price', 'speed', 'service'], []),
  c('ranking-one-option-left-out', field('ranking', {}, { options: ASPECTS }), ['quality', 'price', 'speed'], ['invalid']),
  c('ranking-not-an-option', field('ranking', {}, { options: ASPECTS }), ['quality', 'price', 'speed', 'looks'], ['invalid']),
  c('ranking-its-top-two', field('ranking', { top: 2 }, { options: ASPECTS }), ['speed', 'price'], []),
  c('ranking-three-for-its-top-two', field('ranking', { top: 2 }, { options: ASPECTS }), ['speed', 'price', 'quality'], ['invalid']),
  c('ranking-one-for-its-top-two', field('ranking', { top: 2 }, { options: ASPECTS }), ['speed'], ['invalid']),
  c('ranking-a-top-above-its-options', field('ranking', { top: 9 }, { options: ASPECTS }), ['quality', 'price', 'speed', 'service'], [], {
    description: 'publishing refuses a top above the options (ADR-0056); a draft ranks them all',
  }),
  c('ranking-a-source-without-its-options-takes-any', field('ranking', { top: 2 }, { fromSource: true }), ['looks'], []),
  c('ranking-an-option-twice', field('ranking', {}, { options: ASPECTS }), ['quality', 'quality', 'speed', 'service'], ['invalid']),
  c('ranking-an-option-twice-from-a-source-without-its-options', field('ranking', { top: 2 }, { fromSource: true }), ['looks', 'looks'], ['invalid']),
  c('ranking-positions-without-script', field('ranking', {}, { options: ASPECTS }), [], [], {
    parts: { price: ['2'], quality: ['1'], service: ['4'], speed: ['3'] },
  }),
  c('ranking-two-options-in-one-position', field('ranking', {}, { options: ASPECTS }), [], ['invalid'], {
    parts: { price: ['1'], quality: ['1'], service: ['3'], speed: ['4'] },
  }),
  c('ranking-positions-compare-as-numbers', field('ranking', { top: 2 }, { options: ASPECTS }), [], ['invalid'], {
    parts: { price: ['01'], quality: ['1'] },
    description: '01 and 1 are one position',
  }),
  c('ranking-positions-with-a-gap', field('ranking', { top: 2 }, { options: ASPECTS }), [], [], { parts: { speed: ['1'], price: ['3'] } }),
  c('ranking-positions-for-more-than-its-top', field('ranking', { top: 2 }, { options: ASPECTS }), [], ['invalid'], {
    parts: { speed: ['1'], price: ['2'], quality: ['3'] },
  }),
  c('ranking-a-position-for-no-option', field('ranking', { top: 2 }, { options: ASPECTS }), [], ['invalid'], { parts: { speed: ['1'], looks: ['2'] } }),
  c('ranking-positions-in-one-from-a-source-without-its-options', field('ranking', { top: 2 }, { fromSource: true }), [], ['invalid'], { parts: { a: ['2'], b: ['2'] } }),
];

// ---- options from a list or another source: held to those it resolved, as a field's own (ADR-0077) ----
const fromSource = (type, settings = {}, more = {}) => field(type, settings, { fromSource: true, ...more });
const sources = [
  c('source-an-option-it-resolved', fromSource('dropdown', {}, { options: COLOURS }), ['green'], []),
  c('source-an-answer-it-lacks', fromSource('dropdown', {}, { options: COLOURS }), ['purple'], ['invalid'], {
    description: 'a choice from a list holds its answers to the options the list resolved, as a field holds them to its own',
  }),
  c('source-an-option-compares-exactly', fromSource('dropdown', {}, { options: COLOURS }), ['Green'], ['invalid']),
  c('source-that-resolved-no-options-takes-nothing', fromSource('dropdown', {}, { options: [] }), ['red'], ['invalid']),
  c('source-several-one-it-lacks', fromSource('dropdown', { multiple: true }, { options: COLOURS }), ['red', 'pink'], ['invalid']),
  c('source-radio-an-answer-it-lacks', fromSource('radio', {}, { options: ANSWERS }), ['maybe'], ['invalid']),
  c('source-radio-the-other-text-is-the-visitors-own', fromSource('radio', { other: true }, { options: ANSWERS }), [], [], { parts: { other: ['maybe'] } }),
  c('source-checkboxes-options-it-resolved', fromSource('checkboxes', { maxSelections: 3 }, { options: CHANNELS }), ['email', 'post'], []),
  c('source-checkboxes-one-it-lacks', fromSource('checkboxes', {}, { options: CHANNELS }), ['email', 'fax'], ['invalid']),
  c('source-grid-a-column-it-resolved', fromSource('grid', {}, { options: AGREEMENT, rows: ROWS }), [], [], { parts: { [PRICE_ROW]: ['agree'] } }),
  c('source-grid-a-column-it-lacks', fromSource('grid', {}, { options: AGREEMENT, rows: ROWS }), [], ['invalid'], { parts: { [PRICE_ROW]: ['whatever'] } }),
  c('source-ranking-all-it-resolved', fromSource('ranking', {}, { options: ASPECTS }), ['quality', 'price', 'speed', 'service'], []),
  c('source-ranking-fewer-than-it-resolved', fromSource('ranking', {}, { options: ASPECTS }), ['quality', 'price', 'speed'], ['invalid'], {
    description: 'without a top, a ranking ranks every option its list resolved',
  }),
  c('source-ranking-its-top-two', fromSource('ranking', { top: 2 }, { options: ASPECTS }), ['speed', 'price'], []),
  c('source-ranking-one-it-lacks', fromSource('ranking', { top: 2 }, { options: ASPECTS }), ['speed', 'looks'], ['invalid']),
];

// ---- cascading: the options that belong to the parent's answers; one that names none belongs to every answer ----
const COUNTRY_FIELD = '4fa08b5c-b06b-4c2e-9d7f-9b5a6ebcaf84';
const CITIES = [
  { value: 'zurich', parentValues: ['CH'] },
  { value: 'geneva', parentValues: ['CH'] },
  { value: 'berlin', parentValues: ['DE'] },
  { value: 'border', parentValues: ['CH', 'DE'] },
  'elsewhere',
];
const city = (type = 'dropdown', more = {}) => field(type, {}, { options: CITIES, parent: COUNTRY_FIELD, ...more });
const parentAnswers = (...answers) => ({ others: { [COUNTRY_FIELD]: answers } });
const cascading = [
  c('cascading-an-option-of-the-parents-answer', city(), ['zurich'], [], parentAnswers('CH')),
  c('cascading-an-option-of-another-answer', city(), ['berlin'], ['invalid'], {
    ...parentAnswers('CH'),
    description: 'an option that names parent answers belongs to them alone',
  }),
  c('cascading-an-option-of-two-answers', city(), ['border'], [], parentAnswers('DE')),
  c('cascading-an-option-of-every-answer', city(), ['elsewhere'], [], {
    ...parentAnswers('CH'),
    description: 'an option that names no parent answer belongs to every one',
  }),
  c('cascading-without-a-parent-answer', city(), ['zurich'], ['invalid'], {
    description: 'with no parent answer, as when the rules hide the parent, only the options of every answer are offered',
  }),
  c('cascading-without-a-parent-answer-an-option-of-every-answer', city(), ['elsewhere'], []),
  c('cascading-a-parent-answer-compares-exactly', city(), ['zurich'], ['invalid'], parentAnswers('ch')),
  c('cascading-one-of-the-parents-answers', city(), ['berlin'], [], parentAnswers('CH', 'DE')),
  c('cascading-an-option-of-another-field', city(), ['zurich'], ['invalid'], {
    others: { [OTHER_FIELD]: ['CH'] },
    description: 'only the answers of the parent field count',
  }),
  c('cascading-an-option-that-lists-no-parent-answers', field('dropdown', {}, { options: [{ value: 'zurich', parentValues: [] }], parent: COUNTRY_FIELD }), ['zurich'], [], parentAnswers('DE')),
  c('cascading-an-option-without-its-parent-answers', field('dropdown', {}, { options: [{ value: 'zurich' }], parent: COUNTRY_FIELD }), ['zurich'], [], parentAnswers('DE')),
  c('cascading-checkboxes-one-of-another-answer', city('checkboxes'), ['zurich', 'berlin'], ['invalid'], parentAnswers('CH')),
  c('cascading-a-ranking-of-the-options-that-belong', city('ranking'), ['geneva', 'zurich', 'border', 'elsewhere'], [], parentAnswers('CH')),
  c('cascading-a-ranking-of-every-option', city('ranking'), ['geneva', 'zurich', 'border', 'elsewhere', 'berlin'], ['invalid'], parentAnswers('CH')),
  c('cascading-options-without-a-parent', field('dropdown', {}, { options: CITIES }), ['berlin'], [], {
    ...parentAnswers('CH'),
    description: 'only a field with a parent filters its options, whatever they name',
  }),
  c('cascading-from-a-source', fromSource('dropdown', {}, { options: CITIES, parent: COUNTRY_FIELD }), ['zurich'], [], parentAnswers('CH')),
  c('cascading-from-a-source-of-another-answer', fromSource('dropdown', {}, { options: CITIES, parent: COUNTRY_FIELD }), ['berlin'], ['invalid'], parentAnswers('CH')),
  c('cascading-from-a-source-without-its-options', fromSource('dropdown', {}, { parent: COUNTRY_FIELD }), ['berlin'], [], parentAnswers('CH')),
];

// ---- country: among the allowed ones, when the field lists some ----
const country = [
  c('country-any-without-a-list', field('country'), ['CH'], []),
  c('country-allowed', field('country', { allowed: ['AT', 'CH', 'DE'] }), ['AT'], []),
  c('country-not-allowed', field('country', { allowed: ['AT', 'CH', 'DE'] }), ['FR'], ['invalid']),
];

// ---- name: the required parts, or every part asked for when the field is required ----
const ADA = { first: ['Ada'], last: ['Lovelace'] };
const name = [
  c('name-required-with-every-part', field('name', {}, { required: true }), [], [], { parts: ADA }),
  c('name-required-without-its-last-name', field('name', {}, { required: true }), [], ['required'], { parts: { first: ['Ada'] } }),
  c('name-optional-with-one-part', field('name'), [], [], { parts: { first: ['Ada'] } }),
  c('name-required-without-answers', field('name', {}, { required: true }), [], ['required']),
  c('name-required-asking-for-a-title', field('name', { parts: ['title', 'first', 'last'] }, { required: true }), [], ['required'], { parts: ADA }),
  c('name-its-required-parts', field('name', { requiredParts: ['last'] }), [], [], { parts: { last: ['Lovelace'] } }),
  c('name-without-a-required-part-once-given', field('name', { requiredParts: ['last'] }), [], ['required'], {
    parts: { first: ['Ada'] },
    description: 'the required parts hold once the visitor gives any part',
  }),
  c('name-required-needs-only-its-required-parts', field('name', { requiredParts: ['last'] }, { required: true }), [], [], { parts: { last: ['Lovelace'] } }),
  c('name-a-required-part-it-does-not-ask-for', field('name', { requiredParts: ['middle', 'last'] }), [], [], {
    parts: ADA,
    description: 'publishing refuses it (ADR-0056); a draft needs only the parts it asks for',
  }),
  c('name-a-part-it-does-not-ask-for-is-left', field('name', {}, { required: true }), [], [], { parts: { ...ADA, middle: ['King'] } }),
  c('name-only-a-part-it-does-not-ask-for', field('name', {}, { required: true }), [], ['required'], { parts: { middle: ['King'] } }),
  c('name-only-a-part-it-does-not-ask-for-is-no-answer', field('name', { requiredParts: ['last'] }), [], [], {
    parts: { middle: ['King'] },
    description: 'a part the field does not ask for gives it no value, so its required parts do not hold',
  }),
];

// ---- address: the required parts, or the street, the postcode and the city when the field is required ----
const ZURICH = { street: ['Bahnhofstrasse 1'], postcode: ['8001'], city: ['Zurich'] };
const address = [
  c('address-required-with-the-three', field('address', {}, { required: true }), [], [], { parts: ZURICH }),
  c('address-required-without-its-city', field('address', {}, { required: true }), [], ['required'], {
    parts: { street: ['Bahnhofstrasse 1'], postcode: ['8001'] },
  }),
  c('address-required-without-a-region-it-asks-for', field('address', { parts: ['region'] }, { required: true }), [], [], {
    parts: ZURICH,
    description: 'a required address needs the street, the postcode and the city unless it lists others',
  }),
  c('address-required-without-its-street', field('address', {}, { required: true }), [], ['required'], { parts: { postcode: ['8001'], city: ['Zurich'] } }),
  c('address-optional-with-one-part', field('address'), [], [], { parts: { city: ['Bern'] } }),
  c('address-only-a-part-it-does-not-ask-for-is-no-answer', field('address', { requiredParts: ['city'] }), [], [], { parts: { region: ['ZH'] } }),
  c('address-its-required-parts', field('address', { parts: ['country'], requiredParts: ['country'] }), [], [], { parts: { country: ['CH'] } }),
  c('address-without-a-required-part-once-given', field('address', { requiredParts: ['postcode', 'city'] }), [], ['required'], {
    parts: { street: ['Bahnhofstrasse 1'], city: ['Zurich'] },
  }),
  c('address-a-required-part-it-does-not-ask-for', field('address', { requiredParts: ['region'] }), [], [], {
    parts: ZURICH,
    description: 'publishing refuses it (ADR-0056); a draft needs only the parts it asks for',
  }),
];

// ---- file: the count, each file's extension, each file's size ----
const file = [
  uploads('file-an-image', field('file'), [['photo.jpg', 204800]], []),
  uploads('file-a-pdf-by-default', field('file'), [['report.pdf', 1024]], []),
  uploads('file-a-document-by-default', field('file'), [['report.docx', 1024]], ['fileType']),
  uploads('file-an-extension-in-capitals', field('file'), [['IMG_0001.HEIC', 1024]], []),
  uploads('file-the-last-point-decides', field('file'), [['photo.jpg.exe', 1024]], ['fileType']),
  uploads('file-a-name-without-a-point', field('file'), [['jpg', 1024]], ['fileType']),
  uploads('file-a-name-ending-with-a-point', field('file'), [['photo.', 1024]], ['fileType']),
  uploads('file-svg-is-no-image', field('file'), [['logo.svg', 1024]], ['fileType']),
  uploads('file-svg-as-its-own-extension', field('file', { extensions: ['svg'] }), [['logo.svg', 1024]], []),
  uploads('file-its-own-extension', field('file', { extensions: ['dwg'] }), [['plan.dwg', 1024]], []),
  uploads('file-its-own-extensions-only', field('file', { extensions: ['dwg'] }), [['photo.jpg', 1024]], ['fileType'], {
    description: 'images and PDFs are taken only when the field lists neither types nor extensions',
  }),
  uploads('file-office-documents', field('file', { types: ['office'], maxFiles: 2 }), [['budget.xlsx', 1024], ['notes.odt', 1024]], []),
  uploads('file-office-documents-are-no-pdf', field('file', { types: ['office'] }), [['report.pdf', 1024]], ['fileType']),
  uploads('file-a-type-and-an-extension', field('file', { types: ['pdf'], extensions: ['dwg'] }), [['plan.dwg', 1024]], []),
  uploads('file-plain-text', field('file', { types: ['text'] }), [['data.csv', 1024]], []),
  uploads('file-an-archive', field('file', { types: ['archives'] }), [['backup.7z', 1024]], []),
  uploads('file-a-double-extension', field('file', { types: ['archives'] }), [['backup.tar.gz', 1024]], []),
  uploads('file-at-its-most-files', field('file', { maxFiles: 2 }), [['a.png', 1], ['b.png', 1]], []),
  uploads('file-above-its-most-files', field('file', { maxFiles: 2 }), [['a.png', 1], ['b.png', 1], ['c.png', 1]], ['fileCount']),
  uploads('file-at-its-most-bytes', field('file', { maxBytes: 1000 }), [['a.png', 1000]], []),
  uploads('file-above-its-most-bytes', field('file', { maxBytes: 1000 }), [['a.png', 1001]], ['fileSize']),
  uploads('file-one-of-two-too-big', field('file', { maxFiles: 2, maxBytes: 1000 }), [['a.png', 10], ['b.png', 1001]], ['fileSize']),
  uploads('file-any-size-without-a-limit', field('file'), [['film.png', 5368709120]], [], {
    description: "the host's own limit comes with the uploads (6.1)",
  }),
  uploads('file-an-empty-file', field('file'), [['empty.png', 0]], []),
  uploads('file-every-check-fails', field('file', { maxBytes: 1000 }), [['a.png', 10], ['b.exe', 1001]], ['fileCount', 'fileType', 'fileSize']),
  c('file-required-without-files', field('file', {}, { required: true }), [], ['required']),
];

// ---- the field's own checks: pattern, the whole of every value, after the type's checks ----
const POSTCODE = { kind: 'pattern', pattern: '[0-9]{4}' };
const pattern = [
  c('pattern-matches-the-whole-answer', field('text', {}, { checks: [POSTCODE] }), ['8001'], []),
  c('pattern-a-longer-answer', field('text', {}, { checks: [POSTCODE] }), ['80012'], [], { checks: [0] }),
  c('pattern-a-part-of-the-answer', field('text', {}, { checks: [POSTCODE] }), ['CH-8001'], [], { checks: [0] }),
  c('pattern-compares-exactly', field('text', {}, { checks: [{ kind: 'pattern', pattern: '[a-z]+' }] }), ['Abc'], [], { checks: [0] }),
  c('pattern-normalised-as-answers-are', field('text', {}, { checks: [{ kind: 'pattern', pattern: `caf${u(0x65, 0x301)}` }] }), [`caf${u(0xe9)}`], [], {
    description: 'the pattern is composed as cleaning composes the answer (ADR-0005)',
  }),
  c('pattern-after-the-types-checks', field('text', { maxLength: 3 }, { checks: [POSTCODE] }), ['8001'], ['maxLength']),
  c('pattern-and-the-types-checks-both-fail', field('text', { maxLength: 3 }, { checks: [{ kind: 'pattern', pattern: '[a-z]+' }] }), ['8001'], ['maxLength'], { checks: [0] }),
  c('pattern-not-without-an-answer', field('text', {}, { checks: [POSTCODE] }), [], []),
  c('pattern-not-for-a-required-field-without-an-answer', field('text', {}, { checks: [POSTCODE], required: true }), [], ['required']),
  c('pattern-outside-the-safe-subset-checks-nothing', field('text', {}, { checks: [{ kind: 'pattern', pattern: 'a'.repeat(501) }] }), ['b'], [], {
    description: 'publishing refuses it (ADR-0005); a draft does not fail on it',
  }),
  c('pattern-its-place-among-the-checks', field('text', {}, { checks: [{ kind: 'uniquePerForm' }, { kind: 'pattern', pattern: '[a-z]+' }] }), ['ABC'], [], {
    checks: [1],
    description: "uniquePerForm is the server's, in the submission's transaction (6.1)",
  }),
  c('pattern-two-checks-one-fails', field('text', {}, { checks: [{ kind: 'pattern', pattern: '[a-z]+' }, { kind: 'pattern', pattern: '.{5,}' }] }), ['abc'], [], { checks: [1] }),
  c('pattern-two-checks-both-fail', field('text', {}, { checks: [{ kind: 'pattern', pattern: '[a-z]+' }, { kind: 'pattern', pattern: '.{5,}' }] }), ['ABC'], [], { checks: [0, 1] }),
];

// ---- the field's own checks: matchesField, as many values, each equal as the field's kind compares ----
const SAME = { kind: 'matchesField', field: OTHER_FIELD };
const matches = [
  c('matches-the-same-text', field('text', {}, { checks: [SAME] }), ['Ada'], [], { others: { [OTHER_FIELD]: ['Ada'] } }),
  c('matches-text-folded', field('text', {}, { checks: [SAME] }), ['ada lovelace'], [], { others: { [OTHER_FIELD]: ['ADA LOVELACE'] } }),
  c('matches-text-folded-to-the-sharp-s', field('text', {}, { checks: [SAME] }), [`Stra${SHARP_S}e`], [], { others: { [OTHER_FIELD]: [`STRA${u(0x1e9e)}E`] } }),
  c('matches-text-never-folds-the-sharp-s-to-ss', field('text', {}, { checks: [SAME] }), [`Stra${SHARP_S}e`], [], { others: { [OTHER_FIELD]: ['STRASSE'] }, checks: [0] }),
  c('matches-another-text', field('text', {}, { checks: [SAME] }), ['Ada'], [], { others: { [OTHER_FIELD]: ['Bob'] }, checks: [0] }),
  c('matches-a-field-without-values', field('text', {}, { checks: [SAME] }), ['Ada'], [], {
    checks: [0],
    description: 'a hidden field, or one left empty, has no values',
  }),
  c('matches-a-field-with-more-values', field('text', {}, { checks: [SAME] }), ['Ada'], [], { others: { [OTHER_FIELD]: ['Ada', 'Bob'] }, checks: [0] }),
  c('matches-not-without-an-answer', field('text', {}, { checks: [SAME] }), [], [], { others: { [OTHER_FIELD]: ['Ada'] } }),
  c('matches-numbers-as-numbers', field('number', {}, { checks: [SAME] }), ['15'], [], { others: { [OTHER_FIELD]: ['15.0'] } }),
  c('matches-another-number', field('number', {}, { checks: [SAME] }), ['15'], [], { others: { [OTHER_FIELD]: ['16'] }, checks: [0] }),
  c('matches-not-for-a-number-past-its-digits', field('number', {}, { checks: [SAME] }), ['1234567890123456'], ['digits'], {
    others: { [OTHER_FIELD]: ['5'] },
    description: 'too many digits are reported alone, without the checks the field has',
  }),
  c('matches-a-number-not-in-the-wire-form', field('number', {}, { checks: [SAME] }), ['15'], [], { others: { [OTHER_FIELD]: ['015'] }, checks: [0] }),
  c('matches-the-same-date', field('date', {}, { checks: [SAME] }), ['2026-06-15'], [], { others: { [OTHER_FIELD]: ['2026-06-15'] } }),
  c('matches-another-date', field('date', {}, { checks: [SAME] }), ['2026-06-15'], [], { others: { [OTHER_FIELD]: ['2026-06-16'] }, checks: [0] }),
  c('matches-times-to-the-second', field('time', {}, { checks: [SAME] }), ['10:30'], [], { others: { [OTHER_FIELD]: ['10:30:00'] } }),
  c('matches-and-a-pattern-in-order', field('text', {}, { checks: [POSTCODE, SAME] }), ['8001'], [], { others: { [OTHER_FIELD]: ['8002'] }, checks: [1] }),
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
  cases: [
    ...required,
    ...text,
    ...textarea,
    ...email,
    ...phone,
    ...url,
    ...number,
    ...slider,
    ...ratings,
    ...dropdown,
    ...radio,
    ...checkboxes,
    ...grid,
    ...ranking,
    ...sources,
    ...cascading,
    ...country,
    ...name,
    ...address,
    ...file,
    ...date,
    ...time,
    ...datetime,
    ...pattern,
    ...matches,
  ],
};
