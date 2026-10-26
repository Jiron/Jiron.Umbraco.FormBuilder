// Culture formats (ROADMAP section 4.3.3, ADR-0064), the same in C# (Core's CultureFormatting): the patterns and names
// the server writes for a culture, and how both runtimes print dates, numbers and amounts with them. Neither runtime
// uses its own culture data, so the browser prints what the server prints.
import { civil, formatDate, parseDate, parseTime } from './dates.ts';
import { formatDecimal, readNumber, round, type Decimal } from './decimal.ts';
import { LogicError } from './errors.ts';

/** The patterns of the named date formats, in the subset of .NET's custom date patterns ADR-0064 lists. */
export interface DatePatterns {
  readonly short: string;
  readonly medium: string;
  readonly long: string;
  readonly time: string;
  readonly dateTime: string;
}

/** How a culture writes a number: `negative` holds `n` for the digits and `-` for the minus sign. */
export interface NumberFormat {
  readonly decimal: string;
  readonly group: string;
  /** From the point leftwards, the last size repeating; a 0 ends the grouping (as .NET reads them). */
  readonly sizes: readonly number[];
  readonly negative: string;
}

/** How a culture writes an amount: its own currency, and patterns with `n`, `-` and `$` for the symbol. */
export interface CurrencyFormat {
  readonly code: string;
  readonly symbol: string;
  readonly decimal: string;
  readonly group: string;
  readonly sizes: readonly number[];
  readonly positive: string;
  readonly negative: string;
}

/** A culture's formats as the server writes them (ADR-0064). Names are listed January and Sunday first. */
export interface CultureFormats {
  readonly dates: DatePatterns;
  readonly months: readonly string[];
  readonly monthsShort: readonly string[];
  readonly monthsGenitive: readonly string[];
  readonly monthsShortGenitive: readonly string[];
  readonly days: readonly string[];
  readonly daysShort: readonly string[];
  readonly am: string;
  readonly pm: string;
  readonly minus: string;
  readonly number: NumberFormat;
  readonly currency: CurrencyFormat;
}

/** The formats of the `date` filter. */
export type DateFormat = keyof DatePatterns | 'iso';

export const DATE_FORMATS: ReadonlySet<string> = new Set<DateFormat>([
  'short',
  'medium',
  'long',
  'time',
  'dateTime',
  'iso',
]);

/** The places of a currency's minor unit where ISO 4217 gives other than 2. */
const MINOR_UNITS: ReadonlyMap<string, number> = new Map([
  ...[
    'BIF',
    'CLP',
    'DJF',
    'GNF',
    'ISK',
    'JPY',
    'KMF',
    'KRW',
    'PYG',
    'RWF',
    'UGX',
    'UYI',
    'VND',
    'VUV',
    'XAF',
    'XOF',
    'XPF',
  ].map((code) => [code, 0] as const),
  ...['BHD', 'IQD', 'JOD', 'KWD', 'LYD', 'OMR', 'TND'].map((code) => [code, 3] as const),
  ...['CLF', 'UYW'].map((code) => [code, 4] as const),
]);

const NO_BREAK_SPACE = String.fromCharCode(0xa0);
const BACKSLASH = String.fromCharCode(92);

const pad = (value: number, length: number): string => String(value).padStart(length, '0');

/** A name from a list the server wrote, or the number when the list is too short. */
const name = (names: readonly string[], index: number): string => names[index] ?? String(index + 1);

/** A date, a time or both, read from their wire forms (ADR-0005). */
interface CalendarValue {
  readonly day: number | null;
  readonly seconds: number | null;
}

function readCalendar(text: string): CalendarValue | null {
  if (text.length > 10 && text[10] === 'T') {
    const day = parseDate(text.slice(0, 10));
    const seconds = parseTime(text.slice(11));
    return day === null || seconds === null ? null : { day, seconds };
  }
  const day = parseDate(text);
  if (day !== null) return { day, seconds: null };
  const seconds = parseTime(text);
  return seconds === null ? null : { day: null, seconds };
}

/** The time of day as the wire writes it, with seconds. */
function isoTime(seconds: number): string {
  return pad(Math.floor(seconds / 3600), 2) + ':' + pad(Math.floor(seconds / 60) % 60, 2) + ':' + pad(seconds % 60, 2);
}

/**
 * Walks a pattern: quoted text in `'` or `"` and a character after `\` are literal, and a run of one pattern letter
 * is a token. `visit` gets each token's letter and length, or null and the literal text.
 */
function walk(pattern: string, visit: (letter: string | null, run: number, literal: string) => void): void {
  let i = 0;
  while (i < pattern.length) {
    const c = pattern.charAt(i);
    if (c === "'" || c === '"') {
      let literal = '';
      let j = i + 1;
      while (j < pattern.length && pattern.charAt(j) !== c) {
        if (pattern.charAt(j) === BACKSLASH && j + 1 < pattern.length) j++;
        literal += pattern.charAt(j);
        j++;
      }
      visit(null, 0, literal);
      i = j + 1;
    } else if (c === BACKSLASH) {
      visit(null, 0, pattern.charAt(i + 1));
      i += 2;
    } else if ('dMyhHmst'.includes(c)) {
      let j = i;
      while (j < pattern.length && pattern.charAt(j) === c) j++;
      visit(c, j - i, '');
      i = j;
    } else {
      visit(null, 0, c);
      i++;
    }
  }
}

/** Whether a pattern has the day of the month (`d` or `dd`), which makes month names genitive. */
function hasDayOfMonth(pattern: string): boolean {
  let found = false;
  walk(pattern, (letter, run) => {
    if (letter === 'd' && run <= 2) found = true;
  });
  return found;
}

/** A day number and a time of day in a pattern of ADR-0064's subset. */
function formatPattern(pattern: string, day: number, seconds: number, culture: CultureFormats): string {
  const { y, m, d } = civil(day);
  const weekday = (((day + 4) % 7) + 7) % 7;
  const hour = Math.floor(seconds / 3600);
  const minute = Math.floor(seconds / 60) % 60;
  const second = seconds % 60;
  const genitive = hasDayOfMonth(pattern);
  let out = '';
  walk(pattern, (letter, run, literal) => {
    switch (letter) {
      case null:
        out += literal;
        break;
      case 'd':
        out +=
          run === 1 ? String(d) : run === 2 ? pad(d, 2) : name(run === 3 ? culture.daysShort : culture.days, weekday);
        break;
      case 'M':
        if (run <= 2) out += pad(m, run);
        else if (run === 3) out += name(genitive ? culture.monthsShortGenitive : culture.monthsShort, m - 1);
        else out += name(genitive ? culture.monthsGenitive : culture.months, m - 1);
        break;
      case 'y':
        out += run === 1 ? String(y % 100) : run === 2 ? pad(y % 100, 2) : pad(y, run);
        break;
      case 'h':
        out += pad(hour % 12 === 0 ? 12 : hour % 12, Math.min(run, 2));
        break;
      case 'H':
        out += pad(hour, Math.min(run, 2));
        break;
      case 'm':
        out += pad(minute, Math.min(run, 2));
        break;
      case 's':
        out += pad(second, Math.min(run, 2));
        break;
      default: {
        // 't': the designator, or its first character.
        const designator = hour < 12 ? culture.am : culture.pm;
        out += run === 1 ? ([...designator][0] ?? '') : designator;
      }
    }
  });
  return out;
}

/**
 * A date, a time or a date-time in the wire form, in one of the `date` filter's formats; null when the text is none
 * of them, or lacks what the format needs (a time for `time`, a date for the date formats, both for `dateTime`).
 */
export function formatCalendar(text: string, format: DateFormat, culture: CultureFormats): string | null {
  const value = readCalendar(text);
  if (value === null) return null;
  const { day, seconds } = value;
  if (format === 'iso') {
    if (day === null) return seconds === null ? null : isoTime(seconds);
    return seconds === null ? formatDate(day) : formatDate(day) + 'T' + isoTime(seconds);
  }
  if (format === 'time' ? seconds === null : day === null || (format === 'dateTime' && seconds === null)) return null;
  return formatPattern(culture.dates[format], day ?? 0, seconds ?? 0, culture);
}

/** The integer digits with a separator between the groups, from the right, as .NET reads group sizes. */
function grouped(digits: string, separator: string, sizes: readonly number[]): string {
  const groups: string[] = [];
  let end = digits.length;
  let k = 0;
  let size = sizes[0] ?? 0;
  while (size > 0 && end > size) {
    groups.push(digits.slice(end - size, end));
    end -= size;
    if (k < sizes.length - 1) k++;
    size = sizes[k] ?? 0;
  }
  groups.push(digits.slice(0, end));
  return groups.reverse().join(separator);
}

/** A number pattern's text: `n` the digits, `-` the minus sign, `$` the symbol (with a no-break space beside it). */
function applyPattern(pattern: string, digits: string, minus: string, symbol: string | null, spaced: boolean): string {
  let out = '';
  for (let i = 0; i < pattern.length; i++) {
    const c = pattern.charAt(i);
    const next = pattern.charAt(i + 1);
    if (c === 'n') out += spaced && next === '$' ? digits + NO_BREAK_SPACE : digits;
    else if (c === '-') out += minus;
    else if (c === '$' && symbol !== null) out += spaced && next === 'n' ? symbol + NO_BREAK_SPACE : symbol;
    else out += c;
  }
  return out;
}

/** A number the filters read: the wire form within 60 digits, rounded to `places` when given; null otherwise. */
function readAmount(text: string, places: number | null): Decimal | null {
  try {
    const value = readNumber(text);
    return value === null || places === null ? value : round(value, places);
  } catch (error) {
    if (error instanceof LogicError) return null;
    throw error;
  }
}

/** The digits of a number with a culture's separators and grouping, padded to `places` when given. */
function digitsOf(
  value: Decimal,
  places: number | null,
  decimal: string,
  group: string,
  sizes: readonly number[],
): string {
  const text = formatDecimal(value.n < 0n ? { n: -value.n, s: value.s } : value);
  const point = text.indexOf('.');
  const integer = point < 0 ? text : text.slice(0, point);
  let fraction = point < 0 ? '' : text.slice(point + 1);
  if (places !== null) fraction = fraction.padEnd(places, '0');
  return grouped(integer, group, sizes) + (fraction.length > 0 ? decimal + fraction : '');
}

/**
 * A number in the wire form as the culture writes it, rounded half away from zero and padded to `places` when given,
 * with its own decimals otherwise; null when the text is not a number the filters read.
 */
export function formatNumber(text: string, places: number | null, culture: CultureFormats): string | null {
  const value = readAmount(text, places);
  if (value === null) return null;
  const { decimal, group, sizes, negative } = culture.number;
  const digits = digitsOf(value, places, decimal, group, sizes);
  return value.n < 0n ? applyPattern(negative, digits, culture.minus, null, false) : digits;
}

/** The places of a currency's minor unit: 2 unless ISO 4217 gives others. */
export const minorUnits = (code: string): number => MINOR_UNITS.get(code) ?? 2;

/**
 * An amount in the wire form in a currency, in the culture's currency pattern: its own symbol for its own currency,
 * the code for any other; rounded to `places`, or the currency's minor unit. Null when the text is not a number the
 * filters read.
 */
export function formatCurrency(
  text: string,
  code: string,
  places: number | null,
  culture: CultureFormats,
): string | null {
  const scale = places ?? minorUnits(code);
  const value = readAmount(text, scale);
  if (value === null) return null;
  const format = culture.currency;
  const own = code === format.code;
  const digits = digitsOf(value, scale, format.decimal, format.group, format.sizes);
  const pattern = value.n < 0n ? format.negative : format.positive;
  return applyPattern(pattern, digits, culture.minus, own ? format.symbol : code, !own);
}
