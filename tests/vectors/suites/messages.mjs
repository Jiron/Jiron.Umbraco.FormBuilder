// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The visitor messages of ROADMAP 6.2g (ADR-0099): the package's default message for every kind of error each built-in
// type declares, in English and in German, with the values the field's settings give printed in the culture's formats
// (ADR-0064); a site's own type, a field's own checks, a validation rule and the empty submission; and the error
// summary's heading and its links to a default message. A `message` case holds an `error` (the code, the field as the
// form endpoint shows it, the answers and what else the message reads), a `heading` (how many errors the summary
// lists) or an `entry` (a summary link: the message, the field's label and the row's legend), and expects the text.
// A field starts from the public settings its type has defaults for, read from the reviewed snapshot of the field
// types endpoint (docs/api/field-types.json), as the projection writes them; a case adds its own. The site's today is
// the defaults' Monday, 15 June 2026. Every expectation is written out here, never computed by the code under test.
// Special characters are built from code points, so this file stays ASCII.
import { readFileSync } from 'node:fs';
import { CULTURES } from '../cultures.mjs';

const defaults = {
  context: {
    // A Monday in Zurich.
    now: '2026-06-15T10:00:00Z',
    timeZone: 'Europe/Zurich',
    culture: 'en-US',
    member: { signedIn: false, groups: [] },
    query: {},
    hidden: [],
  },
  cultures: { 'en-US': CULTURES['en-US'], 'de-CH': CULTURES['de-CH'], 'fr-FR': CULTURES['fr-FR'] },
  // The culture's words for yes and no, which the form endpoint gives the browser with the page (ADR-0066).
  words: { 'en-US': { yes: 'Yes', no: 'No' }, 'de-CH': { yes: 'Ja', no: 'Nein' }, 'fr-FR': { yes: 'Yes', no: 'No' } },
};

const served = JSON.parse(readFileSync(new URL('../../../docs/api/field-types.json', import.meta.url), 'utf8')).types;

/** Text with code points written as {hex}, so this file stays ASCII. */
const x = (text) => text.replace(/\{([0-9a-f]{2,6})\}/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)));
const QUOTE = x('{2019}');
const NNBSP = x('{202f}');

/** A field of a built-in type as the form endpoint shows it: the public settings its type has defaults for, then the case's own. */
function field(type, settings = {}, options) {
  const found = served.find((each) => each.alias === type);
  if (found === undefined) throw new Error(`no built-in type ${type}`);
  const effective = {};
  for (const setting of found.settings) {
    if (setting.isPublic && setting.default !== null) effective[setting.name] = setting.default;
  }
  return { type, settings: { ...effective, ...settings }, ...(options === undefined ? {} : { options }) };
}

/** An error at a field: its code, the field, and what else the message reads. */
const at = (code, f, more = {}) => ({ code, field: f, ...more });

/** The same error in English (en-US) and in German (de-CH), the German written with {hex} for its letters. */
function both(id, error, english, german) {
  return [
    { id: `${id}-en`, message: { error }, expect: english },
    { id: `${id}-de`, context: { culture: 'de-CH' }, message: { error }, expect: x(german) },
  ];
}

const rows = (alias, values) => values.map((value) => ({ value, label: value[0].toUpperCase() + value.slice(1) + ' ' + alias }));

const required = [
  ...both('required-text', at('required', field('text')), 'Enter an answer', 'Geben Sie eine Antwort ein'),
  ...both('required-textarea', at('required', field('textarea')), 'Enter an answer', 'Geben Sie eine Antwort ein'),
  ...both('required-email', at('required', field('email')), 'Enter an email address', 'Geben Sie eine E-Mail-Adresse ein'),
  ...both('required-phone', at('required', field('phone')), 'Enter a phone number', 'Geben Sie eine Telefonnummer ein'),
  ...both('required-url', at('required', field('url')), 'Enter a web address', 'Geben Sie eine Webadresse ein'),
  ...both('required-number', at('required', field('number')), 'Enter a number', 'Geben Sie eine Zahl ein'),
  ...both('required-date', at('required', field('date')), 'Enter a date', 'Geben Sie ein Datum ein'),
  ...both('required-time', at('required', field('time')), 'Enter a time', 'Geben Sie eine Uhrzeit ein'),
  ...both('required-datetime', at('required', field('datetime')), 'Enter a date and a time', 'Geben Sie ein Datum und eine Uhrzeit ein'),
  ...both('required-rating', at('required', field('rating')), 'Choose a rating', 'W{e4}hlen Sie eine Bewertung'),
  ...both('required-scale', at('required', field('scale')), 'Choose a point on the scale', 'W{e4}hlen Sie einen Wert auf der Skala'),
  ...both('required-slider', at('required', field('slider')), 'Choose a value', 'W{e4}hlen Sie einen Wert'),
  ...both('required-dropdown', at('required', field('dropdown')), 'Choose an answer', 'W{e4}hlen Sie eine Antwort'),
  ...both(
    'required-dropdown-of-several',
    at('required', field('dropdown', { multiple: true })),
    'Choose at least one answer',
    'W{e4}hlen Sie mindestens eine Antwort',
  ),
  ...both('required-radio', at('required', field('radio')), 'Choose an answer', 'W{e4}hlen Sie eine Antwort'),
  ...both('required-checkboxes', at('required', field('checkboxes')), 'Choose at least one answer', 'W{e4}hlen Sie mindestens eine Antwort'),
  ...both('required-grid', at('required', field('grid')), 'Choose an answer in each row', 'W{e4}hlen Sie in jeder Zeile eine Antwort'),
  ...both(
    'required-grid-row',
    at('required', field('grid'), { gridRow: true }),
    'Choose an answer in this row',
    'W{e4}hlen Sie in dieser Zeile eine Antwort',
  ),
  ...both('required-ranking', at('required', field('ranking')), 'Put the options in order', 'Bringen Sie die Optionen in eine Reihenfolge'),
  ...both('required-checkbox', at('required', field('checkbox')), 'Tick the box', 'Kreuzen Sie das K{e4}stchen an'),
  ...both('required-yesno-in-the-culture-words', at('required', field('yesno')), 'Choose Yes or No', 'W{e4}hlen Sie Ja oder Nein'),
  ...both(
    'required-yesno-in-its-own-words',
    at('required', field('yesno', { yesLabel: 'I will come', noLabel: 'I cannot come' })),
    'Choose I will come or I cannot come',
    'W{e4}hlen Sie I will come oder I cannot come',
  ),
  ...both('required-consent', at('required', field('consent')), 'Tick the box to agree', 'Kreuzen Sie das K{e4}stchen an, um zuzustimmen'),
  ...both('required-country', at('required', field('country')), 'Choose a country', 'W{e4}hlen Sie ein Land'),
  ...both('required-name', at('required', field('name')), 'Enter the name in full', 'Geben Sie den vollst{e4}ndigen Namen ein'),
  ...both('required-address', at('required', field('address')), 'Enter the address in full', 'Geben Sie die vollst{e4}ndige Adresse ein'),
  ...both('required-file', at('required', field('file')), 'Choose a file', 'W{e4}hlen Sie eine Datei'),
  ...both(
    'required-signature',
    at('required', field('signature')),
    'Add your signature, or type your name instead',
    'Unterschreiben Sie, oder geben Sie stattdessen Ihren Namen ein',
  ),
];

const invalid = [
  ...both(
    'invalid-email-not-an-address',
    at('invalid', field('email'), { answers: ['mira@'] }),
    'Enter a complete email address, for example name@example.com',
    'Geben Sie eine vollst{e4}ndige E-Mail-Adresse ein, zum Beispiel name@example.com',
  ),
  ...both(
    'invalid-email-at-a-domain-the-field-refuses',
    at('invalid', field('email'), { answers: [' mira@example.org '] }),
    'Use an email address at another domain',
    'Verwenden Sie eine E-Mail-Adresse bei einer anderen Domain',
  ),
  ...both(
    'invalid-email-without-an-answer',
    at('invalid', field('email')),
    'Enter a complete email address, for example name@example.com',
    'Geben Sie eine vollst{e4}ndige E-Mail-Adresse ein, zum Beispiel name@example.com',
  ),
  ...both(
    'invalid-phone',
    at('invalid', field('phone')),
    'Enter a phone number with digits, and a + for the country code',
    'Geben Sie eine Telefonnummer mit Ziffern ein, mit einem + f{fc}r die Landesvorwahl',
  ),
  ...both(
    'invalid-url',
    at('invalid', field('url')),
    'Enter a complete web address, for example https://example.com',
    'Geben Sie eine vollst{e4}ndige Webadresse ein, zum Beispiel https://example.com',
  ),
  ...both('invalid-number-whole', at('invalid', field('number')), 'Enter a number, for example 12', 'Geben Sie eine Zahl ein, zum Beispiel 12'),
  ...both(
    'invalid-number-with-decimals',
    at('invalid', field('number', { decimals: 2 })),
    'Enter a number, for example 12.5',
    'Geben Sie eine Zahl ein, zum Beispiel 12.5',
  ),
  ...both('invalid-date', at('invalid', field('date')), 'Enter a valid date', 'Geben Sie ein g{fc}ltiges Datum ein'),
  ...both('invalid-time', at('invalid', field('time')), 'Enter a valid time', 'Geben Sie eine g{fc}ltige Uhrzeit ein'),
  ...both(
    'invalid-datetime',
    at('invalid', field('datetime')),
    'Enter a valid date and time',
    'Geben Sie ein g{fc}ltiges Datum und eine g{fc}ltige Uhrzeit ein',
  ),
  ...both('invalid-rating', at('invalid', field('rating', { count: 10 })), 'Choose a rating from 1 to 10', 'W{e4}hlen Sie eine Bewertung von 1 bis 10'),
  ...both('invalid-scale', at('invalid', field('scale', { from: 0, to: 10 })), 'Choose a point from 0 to 10', 'W{e4}hlen Sie einen Wert von 0 bis 10'),
  ...both(
    'invalid-slider',
    at('invalid', field('slider', { min: '0', max: '2500' })),
    'Choose a value from 0 to 2,500',
    'W{e4}hlen Sie einen Wert von 0 bis 2' + QUOTE + '500',
  ),
  ...both('invalid-dropdown', at('invalid', field('dropdown')), 'Choose one of the options given', 'W{e4}hlen Sie eine der angebotenen Optionen'),
  ...both('invalid-radio', at('invalid', field('radio')), 'Choose one of the options given', 'W{e4}hlen Sie eine der angebotenen Optionen'),
  ...both('invalid-checkboxes', at('invalid', field('checkboxes')), 'Choose one of the options given', 'W{e4}hlen Sie eine der angebotenen Optionen'),
  ...both('invalid-grid', at('invalid', field('grid')), 'Choose one of the options given', 'W{e4}hlen Sie eine der angebotenen Optionen'),
  ...both('invalid-ranking', at('invalid', field('ranking')), 'Choose one of the options given', 'W{e4}hlen Sie eine der angebotenen Optionen'),
  ...both('invalid-country', at('invalid', field('country')), 'Choose a country from the list', 'W{e4}hlen Sie ein Land aus der Liste'),
  ...both('invalid-name', at('invalid', field('name')), 'Check the name', 'Pr{fc}fen Sie den Namen'),
  ...both('invalid-address', at('invalid', field('address')), 'Check the address', 'Pr{fc}fen Sie die Adresse'),
  ...both(
    'invalid-signature',
    at('invalid', field('signature')),
    'Sign again, or type your name instead',
    'Unterschreiben Sie erneut, oder geben Sie stattdessen Ihren Namen ein',
  ),
];

const lengths = [
  ...both('min-length', at('minLength', field('text', { minLength: 3 })), 'Enter at least 3 characters', 'Geben Sie mindestens 3 Zeichen ein'),
  ...both('min-length-of-one', at('minLength', field('text', { minLength: 1 })), 'Enter at least 1 character', 'Geben Sie mindestens 1 Zeichen ein'),
  ...both('max-length', at('maxLength', field('text')), 'Enter at most 255 characters', 'Geben Sie h{f6}chstens 255 Zeichen ein'),
  ...both(
    'max-length-of-a-long-text-groups-its-digits',
    at('maxLength', field('textarea')),
    'Enter at most 4,000 characters',
    'Geben Sie h{f6}chstens 4' + QUOTE + '000 Zeichen ein',
  ),
  ...both('min-length-of-a-long-text', at('minLength', field('textarea', { minLength: 20 })), 'Enter at least 20 characters', 'Geben Sie mindestens 20 Zeichen ein'),
  ...both('min-words', at('minWords', field('textarea', { minWords: 5 })), 'Enter at least 5 words', 'Geben Sie mindestens 5 W{f6}rter ein'),
  ...both('min-words-of-one', at('minWords', field('textarea', { minWords: 1 })), 'Enter at least 1 word', 'Geben Sie mindestens 1 Wort ein'),
  ...both('max-words', at('maxWords', field('textarea', { maxWords: 150 })), 'Enter at most 150 words', 'Geben Sie h{f6}chstens 150 W{f6}rter ein'),
  ...both('max-words-of-one', at('maxWords', field('textarea', { maxWords: 1 })), 'Enter at most 1 word', 'Geben Sie h{f6}chstens 1 Wort ein'),
];

const ranges = [
  ...both(
    'number-min-of-both-limits',
    at('min', field('number', { min: '1', max: '20' })),
    'Enter a number from 1 to 20',
    'Geben Sie eine Zahl von 1 bis 20 ein',
  ),
  ...both(
    'number-max-of-both-limits',
    at('max', field('number', { min: '-2.5', max: '1250.75', decimals: 2 })),
    'Enter a number from -2.5 to 1,250.75',
    'Geben Sie eine Zahl von -2.5 bis 1' + QUOTE + '250.75 ein',
  ),
  ...both('number-min-only', at('min', field('number', { min: '18' })), 'Enter a number of 18 or more', 'Geben Sie eine Zahl ab 18 ein'),
  ...both('number-max-only', at('max', field('number', { max: '99' })), 'Enter a number of 99 or less', 'Geben Sie eine Zahl bis 99 ein'),
  ...both(
    'slider-max',
    at('max', field('slider', { min: '10', max: '90' })),
    'Choose a value from 10 to 90',
    'W{e4}hlen Sie einen Wert von 10 bis 90',
  ),
  ...both(
    'time-min-of-both-limits',
    at('min', field('time', { min: '09:00', max: '17:30' })),
    'Enter a time from 9:00 AM to 5:30 PM',
    'Geben Sie eine Uhrzeit von 09:00 bis 17:30 ein',
  ),
  ...both('time-min-only', at('min', field('time', { min: '08:15' })), 'Enter a time of 8:15 AM or later', 'Geben Sie eine Uhrzeit ab 08:15 ein'),
  ...both('time-max-only', at('max', field('time', { max: '22:00' })), 'Enter a time of 10:00 PM or earlier', 'Geben Sie eine Uhrzeit bis 22:00 ein'),
];

const steps = [
  ...both(
    'number-step-from-its-least',
    at('step', field('number', { min: '1', step: '2' })),
    'Enter a number in steps of 2, starting at 1',
    'Geben Sie eine Zahl in Schritten von 2 ab 1 ein',
  ),
  ...both(
    'number-step-without-a-least',
    at('step', field('number', { step: '0.25', decimals: 2 })),
    'Enter a number in steps of 0.25',
    'Geben Sie eine Zahl in Schritten von 0.25 ein',
  ),
  ...both('number-step-of-a-whole-number', at('step', field('number')), 'Enter a whole number', 'Geben Sie eine ganze Zahl ein'),
  ...both(
    'number-step-of-its-decimals',
    at('step', field('number', { decimals: 2 })),
    'Enter a number with at most 2 decimal places',
    'Geben Sie eine Zahl mit h{f6}chstens 2 Nachkommastellen ein',
  ),
  ...both(
    'number-step-of-one-decimal',
    at('step', field('number', { decimals: 1 })),
    'Enter a number with at most 1 decimal place',
    'Geben Sie eine Zahl mit h{f6}chstens 1 Nachkommastelle ein',
  ),
  ...both('slider-step', at('step', field('slider', { step: '5' })), 'Choose a value in steps of 5', 'W{e4}hlen Sie einen Wert in Schritten von 5'),
  ...both(
    'time-step-from-its-earliest',
    at('step', field('time', { min: '08:00', stepMinutes: 15 })),
    'Choose a time in steps of 15 minutes from 8:00 AM',
    'W{e4}hlen Sie eine Uhrzeit im Abstand von 15 Minuten ab 08:00',
  ),
  ...both(
    'time-step-from-midnight',
    at('step', field('time', { stepMinutes: 30 })),
    'Choose a time in steps of 30 minutes',
    'W{e4}hlen Sie eine Uhrzeit im Abstand von 30 Minuten',
  ),
  ...both(
    'datetime-step',
    at('step', field('datetime', { stepMinutes: 10 })),
    'Choose a time in steps of 10 minutes',
    'W{e4}hlen Sie eine Uhrzeit im Abstand von 10 Minuten',
  ),
  ...both('number-digits', at('digits', field('number')), 'Enter a number with fewer digits', 'Geben Sie eine Zahl mit weniger Ziffern ein'),
];

const window = [
  ...both(
    'date-window-of-two-dates',
    at('dateWindow', field('date', { min: '2026-11-01', max: '2026-11-30' }), { answers: ['2026-12-03'] }),
    'Choose a date from Nov 1, 2026 to Nov 30, 2026',
    'W{e4}hlen Sie ein Datum vom 1. Nov. 2026 bis zum 30. Nov. 2026',
  ),
  ...both(
    'date-window-from-tomorrow',
    at('dateWindow', field('date', { min: { today: '+1d' } }), { answers: ['2026-06-15'] }),
    'Choose a date on or after Jun 16, 2026',
    'W{e4}hlen Sie ein Datum ab dem 16. Juni 2026',
  ),
  ...both(
    'date-window-up-to-a-month-from-today',
    at('dateWindow', field('date', { max: { today: '+1m' } }), { answers: ['2026-08-01'] }),
    'Choose a date on or before Jul 15, 2026',
    'W{e4}hlen Sie ein Datum bis zum 15. Juli 2026',
  ),
  ...both(
    'date-window-of-weekdays-between-two-dates',
    at('dateWindow', field('date', { min: '2026-11-02', max: '2026-11-27', weekdaysOnly: true }), { answers: ['2026-11-07'] }),
    'Choose a date from Nov 2, 2026 to Nov 27, 2026, Monday to Friday',
    'W{e4}hlen Sie ein Datum vom 2. Nov. 2026 bis zum 27. Nov. 2026, Montag bis Freitag',
  ),
  ...both(
    'date-window-of-weekdays-from-today',
    at('dateWindow', field('date', { min: { today: '' }, weekdaysOnly: true }), { answers: ['2026-06-20'] }),
    'Choose a date on or after Jun 15, 2026, Monday to Friday',
    'W{e4}hlen Sie ein Datum ab dem 15. Juni 2026, Montag bis Freitag',
  ),
  ...both(
    'date-window-of-weekdays-up-to-a-date',
    at('dateWindow', field('date', { max: '2026-12-18', weekdaysOnly: true }), { answers: ['2026-12-19'] }),
    'Choose a date on or before Dec 18, 2026, Monday to Friday',
    'W{e4}hlen Sie ein Datum bis zum 18. Dez. 2026, Montag bis Freitag',
  ),
  ...both(
    'date-window-of-weekdays-alone',
    at('dateWindow', field('date', { weekdaysOnly: true }), { answers: ['2026-06-21'] }),
    'Choose a day from Monday to Friday',
    'W{e4}hlen Sie einen Tag von Montag bis Freitag',
  ),
  ...both(
    'date-window-takes-a-date-the-field-excludes',
    at('dateWindow', field('date', { min: '2026-12-01', max: '2026-12-31', excluded: ['2026-12-25'] }), { answers: ['2026-12-25'] }),
    'This date is not available. Choose another one.',
    'Dieses Datum ist nicht verf{fc}gbar. W{e4}hlen Sie ein anderes.',
  ),
  ...both(
    'date-window-without-limits-excludes-a-date',
    at('dateWindow', field('date', { excluded: ['2026-06-16'] }), { answers: ['2026-06-16'] }),
    'This date is not available. Choose another one.',
    'Dieses Datum ist nicht verf{fc}gbar. W{e4}hlen Sie ein anderes.',
  ),
  ...both(
    'date-window-with-a-limit-outside-the-calendar',
    at('dateWindow', field('date', { min: { today: '+9999y' } }), { answers: ['2026-06-16'] }),
    'This date is not available. Choose another one.',
    'Dieses Datum ist nicht verf{fc}gbar. W{e4}hlen Sie ein anderes.',
  ),
  ...both(
    'date-window-of-a-date-and-time-reads-its-date',
    at('dateWindow', field('datetime', { min: '2026-07-01' }), { answers: ['2026-06-30T09:00'] }),
    'Choose a date on or after Jul 1, 2026',
    'W{e4}hlen Sie ein Datum ab dem 1. Juli 2026',
  ),
  ...both(
    'date-window-of-a-date-and-time-it-excludes',
    at('dateWindow', field('datetime', { min: '2026-07-01', excluded: ['2026-07-03'] }), { answers: ['2026-07-03T09:00'] }),
    'This date is not available. Choose another one.',
    'Dieses Datum ist nicht verf{fc}gbar. W{e4}hlen Sie ein anderes.',
  ),
];

const choices = [
  ...both(
    'selections-exactly',
    at('selections', field('checkboxes', { minSelections: 3, maxSelections: 3 })),
    'Choose 3 answers',
    'W{e4}hlen Sie 3 Antworten',
  ),
  ...both(
    'selections-exactly-one',
    at('selections', field('dropdown', { multiple: true, minSelections: 1, maxSelections: 1 })),
    'Choose 1 answer',
    'W{e4}hlen Sie 1 Antwort',
  ),
  ...both(
    'selections-between',
    at('selections', field('checkboxes', { minSelections: 2, maxSelections: 4 })),
    'Choose from 2 to 4 answers',
    'W{e4}hlen Sie 2 bis 4 Antworten',
  ),
  ...both(
    'selections-at-least',
    at('selections', field('dropdown', { multiple: true, minSelections: 2 })),
    'Choose at least 2 answers',
    'W{e4}hlen Sie mindestens 2 Antworten',
  ),
  ...both(
    'selections-at-most',
    at('selections', field('checkboxes', { maxSelections: 3 })),
    'Choose at most 3 answers',
    'W{e4}hlen Sie h{f6}chstens 3 Antworten',
  ),
  ...both(
    'selections-at-most-one',
    at('selections', field('checkboxes', { maxSelections: 1 })),
    'Choose at most 1 answer',
    'W{e4}hlen Sie h{f6}chstens 1 Antwort',
  ),
  ...both(
    'full-names-the-option-it-holds',
    at('full', field('radio', {}, [{ value: 'bike', label: 'Bike repair basics' }, { value: 'sew', label: 'Sewing' }]), { answers: ['bike'] }),
    'Bike repair basics is now full. Choose another option.',
    'Bike repair basics ist jetzt ausgebucht. W{e4}hlen Sie eine andere Option.',
  ),
  ...both(
    'full-of-an-option-without-a-label-names-its-value',
    at('full', field('dropdown', {}, [{ value: 'morning', label: '' }]), { answers: ['morning'] }),
    'morning is now full. Choose another option.',
    'morning ist jetzt ausgebucht. W{e4}hlen Sie eine andere Option.',
  ),
  ...both(
    'full-of-several-answers',
    at('full', field('checkboxes', {}, rows('session', ['morning', 'evening'])), { answers: ['morning', 'evening'] }),
    'One of your choices is now full. Choose another option.',
    'Eine Ihrer gew{e4}hlten Optionen ist jetzt ausgebucht. W{e4}hlen Sie eine andere Option.',
  ),
  ...both(
    'confirmation',
    at('confirmation', field('email', { confirm: true })),
    'Enter the same email address twice',
    'Geben Sie zweimal dieselbe E-Mail-Adresse ein',
  ),
];

const files = [
  ...both(
    'file-type-of-groups-and-extensions',
    at('fileType', field('file', { types: ['pdf', 'text'], extensions: ['.odt', 'pdf'] })),
    'Choose a file in the format PDF, TXT, CSV or ODT',
    'W{e4}hlen Sie eine Datei im Format PDF, TXT, CSV oder ODT',
  ),
  ...both(
    'file-type-of-one-format',
    at('fileType', field('file', { extensions: ['svg'] })),
    'Choose a file in the format SVG',
    'W{e4}hlen Sie eine Datei im Format SVG',
  ),
  ...both(
    'file-type-of-a-group',
    at('fileType', field('file', { types: ['images'], extensions: [] })),
    'Choose a file in the format JPG, JPEG, PNG, GIF, WEBP, AVIF, HEIC or HEIF',
    'W{e4}hlen Sie eine Datei im Format JPG, JPEG, PNG, GIF, WEBP, AVIF, HEIC oder HEIF',
  ),
  ...both(
    'file-size-in-megabytes',
    at('fileSize', field('file', { maxBytes: 10485760 })),
    'This file is larger than 10 MB. Choose a smaller one.',
    'Diese Datei ist gr{f6}{df}er als 10 MB. W{e4}hlen Sie eine kleinere.',
  ),
  ...both(
    'file-size-rounds-to-a-tenth',
    at('fileSize', field('file', { maxBytes: 5000000 })),
    'This file is larger than 4.8 MB. Choose a smaller one.',
    'Diese Datei ist gr{f6}{df}er als 4.8 MB. W{e4}hlen Sie eine kleinere.',
  ),
  ...both(
    'file-size-in-kilobytes',
    at('fileSize', field('file', { maxBytes: 512000, maxFiles: 3 })),
    'One of the files is larger than 500 KB. Choose a smaller one.',
    'Eine der Dateien ist gr{f6}{df}er als 500 KB. W{e4}hlen Sie eine kleinere.',
  ),
  ...both(
    'file-size-groups-a-large-limit',
    at('fileSize', field('file', { maxBytes: 2147483648 })),
    'This file is larger than 2,048 MB. Choose a smaller one.',
    'Diese Datei ist gr{f6}{df}er als 2' + QUOTE + '048 MB. W{e4}hlen Sie eine kleinere.',
  ),
  ...both(
    'file-size-held-to-the-host-limit',
    at('fileSize', field('file')),
    'This file is too large. Choose a smaller one.',
    'Diese Datei ist zu gro{df}. W{e4}hlen Sie eine kleinere.',
  ),
  ...both('file-count', at('fileCount', field('file', { maxFiles: 4 })), 'Choose at most 4 files', 'W{e4}hlen Sie h{f6}chstens 4 Dateien'),
  ...both('file-count-of-one', at('fileCount', field('file')), 'Choose only 1 file', 'W{e4}hlen Sie nur 1 Datei'),
];

const groups = [
  ...both(
    'row-count-too-few',
    at('rowCount', field('repeater', { minRows: 2, maxRows: 5 }), { rows: 1 }),
    'Add at least 2 rows',
    'F{fc}gen Sie mindestens 2 Zeilen hinzu',
  ),
  ...both(
    'row-count-too-few-of-one',
    at('rowCount', field('repeater', { minRows: 1 }), { rows: 0 }),
    'Add at least 1 row',
    'F{fc}gen Sie mindestens 1 Zeile hinzu',
  ),
  ...both(
    'row-count-too-many',
    at('rowCount', field('repeater', { minRows: 2, maxRows: 5 }), { rows: 6 }),
    'Remove rows to keep at most 5',
    'Entfernen Sie Zeilen, sodass h{f6}chstens 5 bleiben',
  ),
  ...both(
    'row-count-too-many-of-one',
    at('rowCount', field('repeater', { maxRows: 1 }), { rows: 2 }),
    'Remove rows to keep at most 1',
    'Entfernen Sie Zeilen, sodass h{f6}chstens 1 bleibt',
  ),
  ...both(
    'row-count-unknown-asks-for-the-least',
    at('rowCount', field('repeater', { minRows: 3 })),
    'Add at least 3 rows',
    'F{fc}gen Sie mindestens 3 Zeilen hinzu',
  ),
  ...both(
    'row-count-unknown-without-a-least-asks-for-the-most',
    at('rowCount', field('repeater', { maxRows: 4 })),
    'Remove rows to keep at most 4',
    'Entfernen Sie Zeilen, sodass h{f6}chstens 4 bleiben',
  ),
];

const others = [
  ...both(
    'a-site-type-required',
    at('required', { type: 'acme.mood', builtIn: false, settings: { min: '1' } }),
    'Answer this question',
    'Beantworten Sie diese Frage',
  ),
  ...both(
    'a-site-type-other-errors',
    at('min', { type: 'acme.mood', builtIn: false, settings: { min: '1' } }),
    'Check this answer',
    'Pr{fc}fen Sie diese Antwort',
  ),
  ...both(
    'a-site-type-under-a-built-in-alias',
    at('required', { type: 'text', builtIn: false, settings: {} }),
    'Answer this question',
    'Beantworten Sie diese Frage',
  ),
  ...both('a-kind-a-type-does-not-use', at('minWords', field('text')), 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  // What a field's settings leave unsaid: a kind without its limit or a type without its words says the general words.
  ...both('an-error-without-its-field', { code: 'required' }, 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  ...both('required-of-a-type-without-its-words', at('required', field('hidden')), 'Answer this question', 'Beantworten Sie diese Frage'),
  ...both('a-number-limit-without-limits', at('min', field('number')), 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  ...both('a-time-limit-without-limits', at('max', field('time')), 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  ...both('a-limit-of-a-type-without-a-range', at('min', field('date')), 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  ...both('a-step-without-minutes', at('step', field('datetime')), 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  ...both(
    'a-date-window-without-limits',
    at('dateWindow', field('date')),
    'This date is not available. Choose another one.',
    'Dieses Datum ist nicht verf{fc}gbar. W{e4}hlen Sie ein anderes.',
  ),
  ...both(
    'check-pattern',
    { code: 'check', check: 'pattern', field: field('text') },
    'Enter the answer in the format asked for',
    'Geben Sie die Antwort im verlangten Format ein',
  ),
  ...both(
    'check-matches-a-field',
    { code: 'check', check: 'matchesField', other: 'Password', field: field('text') },
    'Enter the same as for Password',
    'Geben Sie dasselbe ein wie bei Password',
  ),
  ...both(
    'check-matches-a-field-without-a-label',
    { code: 'check', check: 'matchesField', other: '', field: field('text') },
    'Check this answer',
    'Pr{fc}fen Sie diese Antwort',
  ),
  ...both(
    'check-unique',
    { code: 'check', check: 'uniquePerForm', field: field('email') },
    'This answer has already been given. Enter another one.',
    'Diese Antwort wurde bereits gegeben. Geben Sie eine andere ein.',
  ),
  ...both('check-of-another-kind', { code: 'check', check: 'acme', field: field('text') }, 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  ...both('rule', { code: 'rule', field: field('number') }, 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  ...both(
    'empty',
    { code: 'empty' },
    'Answer at least one question before you send the form',
    'Beantworten Sie mindestens eine Frage, bevor Sie das Formular senden',
  ),
  ...both('an-unknown-code', at('acme', field('text')), 'Check this answer', 'Pr{fc}fen Sie diese Antwort'),
  {
    id: 'a-culture-without-words-takes-english-with-its-own-formats',
    context: { culture: 'fr-FR' },
    message: { error: at('maxLength', field('textarea')) },
    expect: 'Enter at most 4' + NNBSP + '000 characters',
  },
  {
    id: 'a-regional-culture-takes-its-language',
    context: { culture: 'de-CH' },
    message: { error: at('required', field('text')) },
    expect: 'Geben Sie eine Antwort ein',
  },
];

const summary = [
  { id: 'heading-of-one-en', message: { heading: 1 }, expect: 'This answer needs a change' },
  { id: 'heading-of-several-en', message: { heading: 3 }, expect: 'These answers need a change' },
  { id: 'heading-of-one-de', context: { culture: 'de-CH' }, message: { heading: 1 }, expect: x('Diese Antwort muss noch ge{e4}ndert werden') },
  {
    id: 'heading-of-several-de',
    context: { culture: 'de-CH' },
    message: { heading: 2 },
    expect: x('Diese Antworten m{fc}ssen noch ge{e4}ndert werden'),
  },
  { id: 'entry-names-its-field-en', message: { entry: { message: 'Enter an answer', label: 'First name' } }, expect: 'First name: Enter an answer' },
  {
    id: 'entry-names-its-field-de',
    context: { culture: 'de-CH' },
    message: { entry: { message: 'Geben Sie eine Antwort ein', label: 'Vorname' } },
    expect: 'Vorname: Geben Sie eine Antwort ein',
  },
  {
    id: 'entry-in-a-row-names-the-row',
    message: { entry: { message: 'Enter an answer', label: 'Name', row: 'Guest 2' } },
    expect: 'Name (Guest 2): Enter an answer',
  },
  { id: 'entry-without-a-label-is-the-message', message: { entry: { message: 'Enter an answer', label: ' ' } }, expect: 'Enter an answer' },
  // A label or a legend of only the code units cleaning trims shows nothing, the same set in both runtimes: a next
  // line (U+0085) and a byte order mark (U+FEFF) too.
  {
    id: 'entry-of-a-next-line-label-is-the-message',
    message: { entry: { message: 'Enter an answer', label: x('{85}') } },
    expect: 'Enter an answer',
  },
  {
    id: 'entry-of-a-byte-order-mark-label-is-the-message',
    message: { entry: { message: 'Enter an answer', label: x('{feff}') } },
    expect: 'Enter an answer',
  },
  {
    id: 'entry-of-a-next-line-row-names-no-row',
    message: { entry: { message: 'Enter an answer', label: 'Name', row: x('{85}') } },
    expect: 'Name: Enter an answer',
  },
  {
    id: 'entry-of-a-label-with-braces-keeps-them',
    message: { entry: { message: 'Enter an answer', label: 'Code {message}' } },
    expect: 'Code {message}: Enter an answer',
  },
  // A label that already ends in a question mark, an exclamation mark, a full stop or a colon takes no colon after it
  // (ROADMAP 6.3b, ADR-0106), in every language and in other scripts' forms of the marks.
  {
    id: 'entry-after-a-question-takes-no-colon-en',
    message: { entry: { message: 'Choose an answer', label: 'Where would you like to sit?' } },
    expect: 'Where would you like to sit? Choose an answer',
  },
  {
    id: 'entry-after-a-question-takes-no-colon-de',
    context: { culture: 'de-CH' },
    message: { entry: { message: x('W{e4}hlen Sie eine Antwort'), label: x('Wo m{f6}chten Sie sitzen?') } },
    expect: x('Wo m{f6}chten Sie sitzen? W{e4}hlen Sie eine Antwort'),
  },
  {
    id: 'entry-after-an-exclamation-mark-takes-no-colon',
    message: { entry: { message: 'Enter an answer', label: 'Tell us more!' } },
    expect: 'Tell us more! Enter an answer',
  },
  {
    id: 'entry-after-a-full-stop-takes-no-colon',
    message: { entry: { message: 'Enter an answer', label: 'Your name, as it should be printed.' } },
    expect: 'Your name, as it should be printed. Enter an answer',
  },
  {
    id: 'entry-after-a-colon-takes-no-second-colon',
    message: { entry: { message: 'Enter an answer', label: 'Postcode:' } },
    expect: 'Postcode: Enter an answer',
  },
  {
    id: 'entry-after-an-ellipsis-takes-no-colon',
    message: { entry: { message: 'Enter an answer', label: x('And then{2026}') } },
    expect: x('And then{2026} Enter an answer'),
  },
  {
    id: 'entry-after-a-full-width-question-mark-takes-no-colon',
    message: { entry: { message: 'Enter an answer', label: x('{304a}{540d}{524d}{ff1f}') } },
    expect: x('{304a}{540d}{524d}{ff1f} Enter an answer'),
  },
  {
    id: 'entry-after-an-arabic-question-mark-takes-no-colon',
    message: { entry: { message: 'Enter an answer', label: x('{645}{627} {627}{633}{645}{643}{61f}') } },
    expect: x('{645}{627} {627}{633}{645}{643}{61f} Enter an answer'),
  },
  {
    // The mark is read past the spaces cleaning trims; a page shows the two spaces as one.
    id: 'entry-after-a-question-and-a-space-takes-no-colon',
    message: { entry: { message: 'Choose an answer', label: 'Where would you like to sit? ' } },
    expect: 'Where would you like to sit?  Choose an answer',
  },
  {
    id: 'entry-after-a-mark-inside-brackets-keeps-its-colon',
    message: { entry: { message: 'Enter an answer', label: 'Size (in cm?)' } },
    expect: 'Size (in cm?): Enter an answer',
  },
  {
    id: 'entry-in-a-row-after-a-question-names-the-row-with-its-colon',
    message: { entry: { message: 'Enter an answer', label: 'Who is coming?', row: 'Guest 2' } },
    expect: 'Who is coming? (Guest 2): Enter an answer',
  },
];

export default {
  suite: 'messages',
  defaults,
  fields: {},
  cases: [...required, ...invalid, ...lengths, ...ranges, ...steps, ...window, ...choices, ...files, ...groups, ...others, ...summary],
};
