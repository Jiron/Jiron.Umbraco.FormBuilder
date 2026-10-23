// Calendar values on the wire (ADR-0005), the same in C#: ISO 8601 local dates and times without a zone, years 1 to
// 9999, validated as real calendar values. Dates are day numbers (days since 1970-01-01, proleptic Gregorian), times
// are seconds since midnight. Calendar arithmetic never uses Date.
import { LogicError } from './errors.ts';

const ZERO = 48;
const SECONDS_PER_DAY = 86400;

function number(text: string, start: number, length: number): number | null {
  let value = 0;
  for (let i = start; i < start + length; i++) {
    const digit = text.charCodeAt(i) - ZERO;
    if (!(digit >= 0 && digit <= 9)) return null;
    value = value * 10 + digit;
  }
  return value;
}

const isLeap = (y: number): boolean => (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0;

/** The days in the month of the year. */
export function daysInMonth(y: number, m: number): number {
  if (m === 2) return isLeap(y) ? 29 : 28;
  return m === 4 || m === 6 || m === 9 || m === 11 ? 30 : 31;
}

/** Days since 1970-01-01 for a valid Gregorian date with y >= 1 (a civil-days algorithm, no Date object). */
export function dayNumber(y: number, m: number, d: number): number {
  const year = m <= 2 ? y - 1 : y;
  const era = Math.floor(year / 400);
  const yearOfEra = year - era * 400;
  const dayOfYear = Math.floor((153 * (m > 2 ? m - 3 : m + 9) + 2) / 5) + d - 1;
  const dayOfEra = yearOfEra * 365 + Math.floor(yearOfEra / 4) - Math.floor(yearOfEra / 100) + dayOfYear;
  return era * 146097 + dayOfEra - 719468;
}

/** The year, month and day of a day number. */
export function civil(day: number): { readonly y: number; readonly m: number; readonly d: number } {
  const z = day + 719468;
  const era = Math.floor(z / 146097);
  const dayOfEra = z - era * 146097;
  const yearOfEra = Math.floor(
    (dayOfEra - Math.floor(dayOfEra / 1460) + Math.floor(dayOfEra / 36524) - Math.floor(dayOfEra / 146096)) / 365,
  );
  const dayOfYear = dayOfEra - (365 * yearOfEra + Math.floor(yearOfEra / 4) - Math.floor(yearOfEra / 100));
  const mp = Math.floor((5 * dayOfYear + 2) / 153);
  const d = dayOfYear - Math.floor((153 * mp + 2) / 5) + 1;
  const m = mp < 10 ? mp + 3 : mp - 9;
  return { y: yearOfEra + era * 400 + (m <= 2 ? 1 : 0), m, d };
}

const MIN_DAY = dayNumber(1, 1, 1);
const MAX_DAY = dayNumber(9999, 12, 31);
/** The first instant "today" takes: before 1970, time zone data differs between platforms. */
const FIRST_INSTANT = 0;
/** The last instant "today" takes: the end of 9998 in UTC, so the local date stays within year 9999. */
const LAST_INSTANT = dayNumber(9999, 1, 1) * SECONDS_PER_DAY - 1;

/** `YYYY-MM-DD`, a real calendar date in years 1 to 9999, as a day number; otherwise null. */
export function parseDate(text: string): number | null {
  if (text.length !== 10 || text[4] !== '-' || text[7] !== '-') return null;
  const y = number(text, 0, 4);
  const m = number(text, 5, 2);
  const d = number(text, 8, 2);
  if (y === null || m === null || d === null || y < 1 || m < 1 || m > 12 || d < 1 || d > daysInMonth(y, m)) {
    return null;
  }
  return dayNumber(y, m, d);
}

/** `HH:mm` or `HH:mm:ss` on a 24-hour clock, as seconds since midnight; otherwise null. */
export function parseTime(text: string): number | null {
  if ((text.length !== 5 && text.length !== 8) || text[2] !== ':' || (text.length === 8 && text[5] !== ':')) {
    return null;
  }
  const h = number(text, 0, 2);
  const min = number(text, 3, 2);
  const s = text.length === 8 ? number(text, 6, 2) : 0;
  if (h === null || min === null || s === null || h > 23 || min > 59 || s > 59) return null;
  return h * 3600 + min * 60 + s;
}

/** `YYYY-MM-DDTHH:mm` or `YYYY-MM-DDTHH:mm:ss`, local wall-clock time without a zone, as seconds; otherwise null. */
export function parseDateTime(text: string): number | null {
  if (text.length < 11 || text[10] !== 'T') return null;
  const day = parseDate(text.slice(0, 10));
  const time = parseTime(text.slice(11));
  return day === null || time === null ? null : day * SECONDS_PER_DAY + time;
}

const pad = (value: number, length: number): string => String(value).padStart(length, '0');

/** A day number as `YYYY-MM-DD`. */
export function formatDate(day: number): string {
  const { y, m, d } = civil(day);
  return pad(y, 4) + '-' + pad(m, 2) + '-' + pad(d, 2);
}

/** Seconds east of UTC for `Z`, `+HH:mm` or `-HH:mm` ending the text at `i`; otherwise null. */
function offset(text: string, i: number): number | null {
  if (i === text.length - 1 && text[i] === 'Z') return 0;
  const sign = text[i];
  if (i !== text.length - 6 || (sign !== '+' && sign !== '-') || text[i + 3] !== ':') return null;
  const hours = number(text, i + 1, 2);
  const minutes = number(text, i + 4, 2);
  if (hours === null || minutes === null || hours > 23 || minutes > 59) return null;
  const seconds = hours * 3600 + minutes * 60;
  return sign === '-' ? -seconds : seconds;
}

/**
 * The instant of evaluation on the wire: `YYYY-MM-DDTHH:mm:ss`, an optional fraction of 1 to 9 digits, and `Z` or an
 * offset `+HH:mm` or `-HH:mm`, between 1970 and the end of 9998 in UTC. Seconds since 1970 (the fraction never
 * changes a date); otherwise null.
 */
export function parseInstant(text: string): number | null {
  if (text.length < 20 || text[10] !== 'T') return null;
  const day = parseDate(text.slice(0, 10));
  const time = parseTime(text.slice(11, 19));
  if (day === null || time === null) return null;
  let i = 19;
  if (text[i] === '.') {
    let count = 0;
    while (i + 1 < text.length && number(text, i + 1, 1) !== null) {
      i++;
      count++;
    }
    if (count < 1 || count > 9) return null;
    i++;
  }
  const seconds = offset(text, i);
  if (seconds === null) return null;
  const instant = day * SECONDS_PER_DAY + time - seconds;
  return instant >= FIRST_INSTANT && instant <= LAST_INSTANT ? instant : null;
}

/** Formatters by time zone, null for a zone this runtime refuses; creating one is slow, and a site uses one zone. */
const formatters = new Map<string, Intl.DateTimeFormat | null>();
const MAX_FORMATTERS = 16;

/**
 * The formatter that reads a local date in an IANA time zone, or null when the runtime does not know the zone.
 * Offsets ("+01:00") are refused, because the server does not take them, as Windows names are refused on the server,
 * because the browser does not.
 */
function formatter(timeZone: string): Intl.DateTimeFormat | null {
  const known = formatters.get(timeZone);
  if (known !== undefined) return known;
  const first = timeZone.charCodeAt(0) | 0x20;
  let created: Intl.DateTimeFormat | null = null;
  if (first >= 97 && first <= 122) {
    try {
      created = new Intl.DateTimeFormat('en-US', {
        timeZone,
        calendar: 'gregory',
        numberingSystem: 'latn',
        year: 'numeric',
        month: 'numeric',
        day: 'numeric',
      });
    } catch {
      created = null;
    }
  }
  if (formatters.size >= MAX_FORMATTERS) formatters.clear();
  formatters.set(timeZone, created);
  return created;
}

/** The site's date: the local date of the instant `now` in the IANA time zone `timeZone`. */
export function today(now: string, timeZone: string): number {
  const instant = parseInstant(now);
  if (instant === null) throw new LogicError('invalid_context');
  const format = formatter(timeZone);
  if (format === null) throw new LogicError('unknown_time_zone');
  const parts = format.formatToParts(new Date(instant * 1000));
  const part = (type: Intl.DateTimeFormatPartTypes): number => Number(parts.find((p) => p.type === type)?.value);
  return dayNumber(part('year'), part('month'), part('day'));
}

/** One step of a relative date: a signed amount and a unit. */
export interface RelativeStep {
  readonly amount: number;
  readonly unit: 'd' | 'w' | 'm' | 'y';
}

/** Reads `([+-][0-9]{1,4}[dwmy])*`, such as "+1m+1d"; the empty text is today. Null when malformed. */
export function parseRelative(text: string): RelativeStep[] | null {
  const steps: RelativeStep[] = [];
  let i = 0;
  while (i < text.length) {
    const sign = text[i] === '+' ? 1 : text[i] === '-' ? -1 : 0;
    if (sign === 0) return null;
    let end = i + 1;
    while (end < text.length && end - i <= 4 && number(text, end, 1) !== null) end++;
    const count = end - i - 1;
    const unit = text[end];
    if (count < 1 || count > 4 || (unit !== 'd' && unit !== 'w' && unit !== 'm' && unit !== 'y')) return null;
    steps.push({ amount: sign * Number(text.slice(i + 1, end)), unit });
    i = end + 1;
  }
  return steps;
}

/** Moves by whole months, clamping the day to the end of the month; NaN outside years 1 to 9999. */
function addMonths(day: number, months: number): number {
  const { y, m, d } = civil(day);
  const total = y * 12 + (m - 1) + months;
  const year = Math.floor(total / 12);
  const month = total - year * 12 + 1;
  if (year < 1 || year > 9999) return Number.NaN;
  return dayNumber(year, month, Math.min(d, daysInMonth(year, month)));
}

/** Applies the steps in written order; months and years clamp to the end of the month. Null outside 1 to 9999. */
export function applyRelative(day: number, steps: readonly RelativeStep[]): number | null {
  let current = day;
  for (const step of steps) {
    if (step.unit === 'd') current += step.amount;
    else if (step.unit === 'w') current += step.amount * 7;
    else current = addMonths(current, step.unit === 'm' ? step.amount : step.amount * 12);
    if (!(current >= MIN_DAY && current <= MAX_DAY)) return null;
  }
  return current;
}
