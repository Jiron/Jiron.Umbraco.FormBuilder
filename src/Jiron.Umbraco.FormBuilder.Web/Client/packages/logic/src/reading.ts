// The reading stage (ROADMAP 6.4a, ADR-0058, ADR-0109), the same as Core's SubmissionReader.Clean and FieldValues: a
// visitor's answer to a field cleaned (every text and part name as rules clean them, empty texts and nameless parts left
// out, parts whose names clean alike kept as one), held to the longest answer its type takes, and read by its type into
// the wire form of ADR-0005, before any rule or check sees it. An answer over the limits is `maxLength` or `invalid`,
// and one its type cannot read is `invalid`; either gives the rules no value. Only the built-in types read here: a
// site's own type reads its answers on the server (ADR-0062). The vectors of tests/vectors/reading.json hold both
// runtimes to one reading.
import type { FieldAnswers } from './checks.ts';
import { COUNTRY_CODES } from './countries.ts';
import { parseDate, parseDateTime, parseTime } from './dates.ts';
import { MAX_DIGITS } from './decimal.ts';
import type { Kind } from './rules.ts';
import { clean } from './text.ts';

/** A visitor's answer to one field as posted, before cleaning: its texts in order and its named parts in order. */
export interface PostedAnswer {
  readonly values?: readonly string[];
  readonly parts?: readonly (readonly [name: string, texts: readonly string[]])[];
}

/** A field as its reading sees it: its type, its effective public settings as the projection writes them, its grid rows. */
export interface ReadField {
  readonly type: string;
  readonly settings?: Readonly<Record<string, unknown>> | null;
  readonly rows?: readonly { readonly id: string }[];
}

/** A value read: its text in the wire form, the grid row it answers, and the text the entry stores in its place. */
export interface ReadValue {
  readonly text: string;
  readonly row?: string;
  readonly stored?: string;
}

/** A field's answer through the reading stage. */
export interface FieldReading {
  /** The answer cleaned. */
  readonly answers: FieldAnswers;
  /** `maxLength` or `invalid` when the answer is over its type's limits; it is then not read. */
  readonly limit: 'maxLength' | 'invalid' | null;
  /** The values read, none when the answer is over its limits or cannot be read. */
  readonly values: readonly ReadValue[];
  /** Whether the type cannot read the answer. */
  readonly unreadable: boolean;
}

/** How a built-in type is declared, as the served field types say (docs/api/field-types.json). */
export interface DeclaredType {
  readonly holdsValue: boolean;
  readonly storage: 'none' | 'text' | 'longText' | 'number' | 'date' | 'bool' | 'json' | 'file';
  readonly kind: Kind | null;
  readonly multipleSetting: boolean;
  /** Whether a text too long is `maxLength` rather than `invalid`. */
  readonly maxLength: boolean;
}

const declared = (
  storage: DeclaredType['storage'],
  kind: Kind | null,
  more: Partial<Pick<DeclaredType, 'holdsValue' | 'multipleSetting' | 'maxLength'>> = {},
): DeclaredType => ({ holdsValue: true, multipleSetting: false, maxLength: false, ...more, storage, kind });
const content = declared('none', null, { holdsValue: false });

/** The built-in types by alias. */
export const BUILT_IN_TYPES: Readonly<Record<string, DeclaredType>> = {
  text: declared('text', 'text', { maxLength: true }),
  textarea: declared('longText', 'text', { maxLength: true }),
  email: declared('text', 'text'),
  phone: declared('text', 'text'),
  url: declared('text', 'text'),
  number: declared('number', 'number'),
  date: declared('date', 'date'),
  time: declared('text', 'time'),
  datetime: declared('date', 'dateTime'),
  rating: declared('number', 'number'),
  scale: declared('number', 'number'),
  slider: declared('number', 'number'),
  calculation: declared('number', 'number'),
  dropdown: declared('text', 'choice', { multipleSetting: true }),
  radio: declared('text', 'choice'),
  checkboxes: declared('text', 'choices'),
  grid: declared('none', null, { multipleSetting: true }),
  ranking: declared('text', 'choices'),
  checkbox: declared('bool', 'checkbox'),
  yesno: declared('text', 'choice'),
  consent: declared('text', 'checkbox'),
  country: declared('text', 'choice'),
  name: declared('json', null),
  address: declared('json', null),
  file: declared('file', 'files'),
  signature: declared('file', 'files'),
  repeater: declared('none', null),
  hidden: declared('text', 'text'),
  heading: content,
  paragraph: content,
  divider: content,
  image: content,
  captcha: content,
};

/** The longest plain text, as a definition's plain text may be. */
const PLAIN_LENGTH = 2000;
/** An option's longest value. */
const OPTION_LENGTH = 200;
/** The most texts a type takes when it takes several: the most options a field may have where a host allows most. */
const MANY = 10_000;
/** A checkbox's longest answer: `false`, which reads as unticked, as anything but `true` does. */
const CHECKBOX_LENGTH = 5;
/** An upload token, with room to spare. */
const TOKEN_LENGTH = 1024;
/** The parts of a name and of an address, in their order. */
const NAME_PARTS = ['title', 'first', 'middle', 'last'];
const ADDRESS_PARTS = ['street', 'street2', 'postcode', 'city', 'region', 'country'];
/** The parts every address asks for. */
const ADDRESS_ALWAYS = ['street', 'postcode', 'city'];
/** A ticked checkbox's value. */
const TICKED = 'true';
const WORDING_HASH = /^[0-9a-f]{64}$/;

type Settings = Readonly<Record<string, unknown>>;
interface Limits {
  readonly longest: number;
  readonly most: number;
}

/** The answer cleaned as the pipeline cleans it, before its type reads it. */
export function cleanAnswer(posted: PostedAnswer | undefined): FieldAnswers {
  const texts = (given: readonly string[] | undefined): string[] =>
    (given ?? []).map((text) => clean(text)).filter((text) => text.length > 0);
  const parts = new Map<string, string[]>();
  for (const [name, given] of posted?.parts ?? []) {
    const cleaned = clean(name);
    if (cleaned.length === 0) continue;
    let kept = parts.get(cleaned);
    if (kept === undefined) parts.set(cleaned, (kept = []));
    kept.push(...texts(given));
  }
  // Core keeps the parts in the order of their names; the readers that read parts in order sort them so.
  const named: Record<string, readonly string[]> = Object.create(null) as Record<string, readonly string[]>;
  for (const [name, kept] of parts) if (kept.length > 0) named[name] = kept;
  return { values: texts(posted?.values), parts: named };
}

/**
 * A built-in field's answer cleaned, held to its type's limits and read; null for a type that is not built in, whose
 * answers only the server reads.
 */
export function readField(field: ReadField, posted: PostedAnswer | undefined): FieldReading | null {
  const type = Object.hasOwn(BUILT_IN_TYPES, field.type) ? BUILT_IN_TYPES[field.type] : undefined;
  if (type === undefined) return null;
  const answers = cleanAnswer(posted);
  const settings = field.settings ?? {};
  const limits = limitsOf(field, type, settings);
  if (limits === null) return { answers, limit: null, values: [], unreadable: false };
  const texts = textsOf(answers);
  if (texts.count > limits.most || texts.longest > limits.longest) {
    return {
      answers,
      limit: texts.longest > limits.longest && type.maxLength ? 'maxLength' : 'invalid',
      values: [],
      unreadable: false,
    };
  }
  const reading = new Reading();
  const reader = Object.hasOwn(READERS, field.type) ? READERS[field.type] : undefined;
  (reader ?? readDefault)(field, type, settings, answers, reading);
  return { answers, limit: null, values: reading.unreadable ? [] : reading.values, unreadable: reading.unreadable };
}

/** A repeatable group's errors for its number of rows: `rowCount` below its least or above its most. */
export function checkRows(field: ReadField, rows: number): readonly string[] {
  const settings = field.settings ?? {};
  const least = whole(settings, 'minRows') ?? 0;
  const most = whole(settings, 'maxRows') ?? 10;
  return rows < least || rows > most ? ['rowCount'] : [];
}

/**
 * A number as a visitor posts it, converted exactly to the wire form (ADR-0058), as Core's PostedNumbers: the wire form,
 * or what a browser's number input posts (an optional minus, digits with leading zeros, a point with digits, and an
 * optional exponent), without trailing zeros or "-0"; null when it is no number, or too long for a calculation to read.
 */
export function postedNumber(text: string): string | null {
  const length = text.length;
  const digit = (at: number): boolean => at < length && text.charCodeAt(at) >= 0x30 && text.charCodeAt(at) <= 0x39;
  let i = 0;
  const negative = i < length && text[i] === '-';
  if (negative) i++;
  const integerStart = i;
  while (digit(i)) i++;
  const integer = text.slice(integerStart, i);
  let fraction = '';
  if (i < length && text[i] === '.') {
    const fractionStart = ++i;
    while (digit(i)) i++;
    fraction = text.slice(fractionStart, i);
    if (fraction.length === 0) return null;
  } else if (integer.length === 0) {
    return null;
  }
  let exponent = 0;
  if (i < length && (text[i] === 'e' || text[i] === 'E')) {
    i++;
    const exponentNegative = i < length && text[i] === '-';
    if (i < length && (text[i] === '-' || text[i] === '+')) i++;
    const exponentStart = i;
    while (digit(i)) {
      // Past this the value is 0 or far too long to store either way; the count only has to stay that large.
      exponent = Math.min(exponent * 10 + (text.charCodeAt(i) - 0x30), 1_000_000);
      i++;
    }
    if (i === exponentStart) return null;
    exponent = exponentNegative ? -exponent : exponent;
  }
  if (i !== length) return null;

  // The digits without leading and trailing zeros, and the power of ten they are multiplied by.
  const digits = (integer + fraction).replace(/^0+/, '');
  const trimmed = digits.replace(/0+$/, '');
  if (trimmed.length === 0) return '0';
  const power = exponent - fraction.length + (digits.length - trimmed.length);
  const places = power < 0 ? -power : 0;
  const size = trimmed.length + (power > 0 ? power : 0);
  if (places > MAX_DIGITS || (places > 0 ? trimmed.length : size) > MAX_DIGITS) return null;
  const sign = negative ? '-' : '';
  if (power >= 0) return sign + trimmed + '0'.repeat(power);
  if (trimmed.length > places)
    return sign + trimmed.slice(0, trimmed.length - places) + '.' + trimmed.slice(trimmed.length - places);
  return sign + '0.' + '0'.repeat(places - trimmed.length) + trimmed;
}

/** The values read so far, or that the answer cannot be read. */
class Reading {
  readonly values: ReadValue[] = [];
  unreadable = false;

  add(value: ReadValue): void {
    this.values.push(value);
  }

  invalid(): void {
    this.unreadable = true;
  }
}

type Reader = (
  field: ReadField,
  type: DeclaredType,
  settings: Settings,
  answers: FieldAnswers,
  reading: Reading,
) => void;

const bool = (settings: Settings, name: string): boolean => settings[name] === true;
const whole = (settings: Settings, name: string): number | null => {
  const value = settings[name];
  return typeof value === 'number' && Number.isInteger(value) ? value : null;
};
const names = (settings: Settings, name: string): readonly string[] => {
  const value = settings[name];
  return Array.isArray(value) ? value.filter((each): each is string => typeof each === 'string') : [];
};
const part = (answers: FieldAnswers, name: string): readonly string[] =>
  answers.parts !== undefined && Object.hasOwn(answers.parts, name) ? (answers.parts[name] ?? []) : [];
const hasDuplicates = (texts: readonly string[]): boolean => new Set(texts).size !== texts.length;

/** How many texts the answer has, the parts' included, and its longest text or name of a part with texts. */
function textsOf(answers: FieldAnswers): { count: number; longest: number } {
  let count = answers.values.length;
  let longest = 0;
  for (const text of answers.values) longest = Math.max(longest, text.length);
  for (const [name, texts] of Object.entries(answers.parts ?? {})) {
    count += texts.length;
    longest = Math.max(longest, name.length, ...texts.map((text) => text.length));
  }
  return { count, longest };
}

/** Whether a type takes several values: choices and files, and a type with a `multiple` setting the field turns on. */
const takesSeveral = (type: DeclaredType, settings: Settings): boolean =>
  type.kind === 'choices' || type.kind === 'files' || (type.multipleSetting && bool(settings, 'multiple'));

/**
 * The longest answer the field takes; null for a field that takes none, whose answers are ignored. Both built-in types
 * that store files have limits of their own, so the defaults need none for files.
 */
function limitsOf(field: ReadField, type: DeclaredType, settings: Settings): Limits | null {
  const own = (longest: number, most: number): Limits => ({ longest, most });
  switch (field.type) {
    case 'text':
      return own(4000, 1);
    case 'textarea':
      return own(100_000, 1);
    case 'email':
      return own(254, bool(settings, 'confirm') ? 2 : 1);
    case 'phone':
      return own(64, 1);
    case 'url':
      return own(2048, 1);
    case 'radio':
      return bool(settings, 'other') ? own(PLAIN_LENGTH, 2) : own(OPTION_LENGTH, 1);
    case 'checkboxes':
      return bool(settings, 'other') ? own(PLAIN_LENGTH, MANY + 1) : own(OPTION_LENGTH, MANY);
    case 'grid': {
      const rows = field.rows?.length ?? 0;
      return rows === 0 ? null : own(OPTION_LENGTH, rows * (bool(settings, 'multiple') ? MANY : 1));
    }
    case 'yesno':
      return own(3, 1);
    case 'consent':
      return own(64, 2);
    case 'country':
      return own(2, 1);
    case 'calculation':
      return null;
    case 'name':
      return own(200, NAME_PARTS.length);
    case 'address':
      return own(200, ADDRESS_PARTS.length);
    case 'file':
      return own(TOKEN_LENGTH, 20);
    case 'signature':
      return own(TOKEN_LENGTH, 1);
  }
  if (!type.holdsValue || type.storage === 'none') return null;
  const most = takesSeveral(type, settings) ? MANY : 1;
  switch (type.kind) {
    case 'choice':
    case 'choices':
      return own(OPTION_LENGTH, most);
    case 'checkbox':
      return own(CHECKBOX_LENGTH, most);
    case 'number':
      return own(64, most);
    case 'date':
      return own(10, most);
    case 'time':
      return own(8, most);
    case 'dateTime':
      return own(19, most);
    default:
      return own(PLAIN_LENGTH, most);
  }
}

/** A text in the wire form of the type's kind; null when it is not one (a checkbox's anything but "true" is unticked). */
function readText(kind: Kind | null, text: string): string | null {
  switch (kind) {
    case 'checkbox':
      return text === TICKED ? TICKED : null;
    case 'number':
      return postedNumber(text);
    case 'date':
      return parseDate(text) === null ? null : text;
    case 'time':
      return parseTime(text) === null ? null : text;
    case 'dateTime':
      return parseDateTime(text) === null ? null : text;
    default:
      return text;
  }
}

/**
 * The field's own texts as its kind needs them, one unless it takes several, none twice; parts are the types' own. A
 * type that holds no value has no limits, so it never gets here.
 */
const readDefault: Reader = (_field, type, settings, answers, reading) => {
  const texts = answers.values;
  if ((texts.length > 1 && !takesSeveral(type, settings)) || hasDuplicates(texts)) return reading.invalid();
  for (const text of texts) {
    const value = readText(type.kind, text);
    if (value !== null) reading.add({ text: value });
    else if (type.kind !== 'checkbox') return reading.invalid();
  }
};

/**
 * The chosen options' values, then the "Other" text when the field offers it: after them when several may be chosen,
 * in place of one when one may. Two options where one may be chosen, two "Other" texts or a value twice cannot be read;
 * an "Other" text the field does not offer is ignored, as is one that repeats a chosen option.
 */
const readChoices =
  (several: boolean): Reader =>
  (_field, _type, settings, answers, reading) => {
    const chosen = answers.values;
    const others = bool(settings, 'other') ? part(answers, 'other') : [];
    if ((!several && chosen.length > 1) || others.length > 1 || hasDuplicates(chosen)) return reading.invalid();
    for (const value of chosen) reading.add({ text: value });
    const other = others[0];
    if (other !== undefined && (several || chosen.length === 0) && !chosen.includes(other))
      reading.add({ text: other });
  };

/** The parts the field asks for, in the type's order, as one JSON object; a part given twice cannot be read. */
function readParts(answers: FieldAnswers, asked: readonly string[], reading: Reading): void {
  const parts: [string, string][] = [];
  for (const name of asked) {
    const texts = part(answers, name);
    if (texts.length === 0) continue;
    if (texts.length > 1) return reading.invalid();
    parts.push([name, texts[0] ?? '']);
  }
  if (parts.length > 0) reading.add({ text: JSON.stringify(Object.fromEntries(parts)) });
}

/**
 * A whole number from 1 as .NET reads one without styles: ASCII digits only, then any number of NUL characters, which
 * .NET's parsing passes over; at most 2,147,483,647.
 */
function position(text: string): number | null {
  const number = text.replace(/\0+$/, '');
  if (!/^[0-9]+$/.test(number)) return null;
  const digits = number.replace(/^0+/, '');
  if (digits.length > 10 || (digits.length === 10 && digits > '2147483647')) return null;
  const value = Number(digits);
  return value >= 1 ? value : null;
}

const READERS: Readonly<Record<string, Reader>> = {
  radio: readChoices(false),
  checkboxes: readChoices(true),
  grid(field, _type, settings, answers, reading) {
    for (const row of field.rows ?? []) {
      const chosen = part(answers, row.id);
      if ((!bool(settings, 'multiple') && chosen.length > 1) || hasDuplicates(chosen)) return reading.invalid();
      for (const value of chosen) reading.add({ text: value, row: row.id });
    }
  },
  // The options in their order, or from a form without JavaScript as parts: each option's value with the position the
  // visitor gave it, ordered by it, options that share a position in the order of their values (ADR-0069).
  ranking(_field, _type, _settings, answers, reading) {
    const given = Object.keys(answers.parts ?? {}).sort();
    if (given.length === 0) {
      for (const value of answers.values) reading.add({ text: value });
      return;
    }
    const ranked: [number, string][] = [];
    for (const value of given) {
      const positions = part(answers, value);
      const at = positions.length === 1 ? position(positions[0] ?? '') : null;
      if (answers.values.length > 0 || at === null) return reading.invalid();
      ranked.push([at, value]);
    }
    for (const [, value] of ranked.sort((a, b) => a[0] - b[0])) reading.add({ text: value });
  },
  // One answer at most, by its limits.
  yesno(_field, _type, _settings, answers, reading) {
    const value = answers.values[0];
    if (value === undefined) return;
    if (value !== 'yes' && value !== 'no') return reading.invalid();
    reading.add({ text: value });
  },
  // A tick reads as "true" and stores the wording's hash; a tick without one cannot be read, anything but a tick is no
  // consent.
  consent(_field, _type, _settings, answers, reading) {
    if (answers.values.length > 1) return reading.invalid();
    if (answers.values[0] !== TICKED) return;
    // One wording at most beside the tick, by its limits.
    const hash = part(answers, 'wording')[0];
    if (hash === undefined || !WORDING_HASH.test(hash)) return reading.invalid();
    reading.add({ text: TICKED, stored: hash });
  },
  country(field, type, settings, answers, reading) {
    if (!answers.values.every((code) => COUNTRY_CODES.has(code))) return reading.invalid();
    readDefault(field, type, settings, answers, reading);
  },
  name: (_field, _type, settings, answers, reading) =>
    readParts(
      answers,
      NAME_PARTS.filter((name) => names(settings, 'parts').includes(name)),
      reading,
    ),
  address: (_field, _type, settings, answers, reading) =>
    readParts(
      answers,
      ADDRESS_PARTS.filter((name) => ADDRESS_ALWAYS.includes(name) || names(settings, 'parts').includes(name)),
      reading,
    ),
};
