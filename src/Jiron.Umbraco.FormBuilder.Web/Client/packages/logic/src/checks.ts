// The built-in field types' checks (ROADMAP section 4.3.3, ADR-0062), the same as each built-in type's Validate in
// Core (FieldTypes/BuiltIn); the vectors of tests/vectors/validation.json hold both runtimes to one behaviour.
import { applyRelative, parseDate, parseRelative, parseTime, today as todayIn } from './dates.ts';
import { compareText, isMultiple, parseDecimal, subtract, type Decimal } from './decimal.ts';
import type { Context, Kind } from './rules.ts';
import { countWords, fold } from './text.ts';

/** A field as its checks read it: its type, its rule kind, whether it is required, and its effective settings. */
export interface CheckedField {
  readonly type: string;
  readonly kind?: Kind;
  /** Whether the field is required, by its own setting or by a rule. */
  readonly required?: boolean;
  /** The public settings with their effective values, as the public projection writes them; a missing one sets no limit. */
  readonly settings?: Readonly<Record<string, unknown>>;
}

/** What a visitor gave one field: its values in the wire form and its named parts (ADR-0058), cleaned. */
export interface FieldAnswers {
  readonly values: readonly string[];
  readonly parts?: Readonly<Record<string, readonly string[]>>;
}

/** A field's errors: the error kinds of its type's checks in the order they are reported, and its own failing checks. */
export interface FieldErrors {
  readonly errors: readonly string[];
  readonly checks: readonly number[];
}

/** What a type's check reads, and where it reports. */
interface Validation {
  readonly value: string;
  readonly answers: FieldAnswers;
  readonly settings: Readonly<Record<string, unknown>>;
  readonly today: () => number;
  fail(kind: string): void;
}

const number = (settings: Readonly<Record<string, unknown>>, name: string): number | undefined =>
  typeof settings[name] === 'number' ? (settings[name] as number) : undefined;
const text = (settings: Readonly<Record<string, unknown>>, name: string): string | undefined =>
  typeof settings[name] === 'string' ? (settings[name] as string) : undefined;
const texts = (settings: Readonly<Record<string, unknown>>, name: string): readonly string[] =>
  Array.isArray(settings[name])
    ? (settings[name] as unknown[]).filter((item): item is string => typeof item === 'string')
    : [];

/** A text with its ASCII capitals made small and every other character left as it is. */
const asciiLower = (value: string): string =>
  value.replace(/[A-Z]/g, (letter) => String.fromCharCode(letter.charCodeAt(0) + 32));

/** Fails minLength or maxLength for a text whose UTF-16 code units fall outside the limits. */
function length(v: Validation): void {
  const min = number(v.settings, 'minLength');
  const max = number(v.settings, 'maxLength');
  if (min !== undefined && v.value.length < min) v.fail('minLength');
  else if (max !== undefined && v.value.length > max) v.fail('maxLength');
}

/** Fails minWords or maxWords for a text whose words fall outside the limits. */
function words(v: Validation): void {
  const count = countWords(v.value);
  const min = number(v.settings, 'minWords');
  const max = number(v.settings, 'maxWords');
  if (min !== undefined && count < min) v.fail('minWords');
  else if (max !== undefined && count > max) v.fail('maxWords');
}

/**
 * Fails min or max for a number outside its limits, and step for one off its step, counted from the minimum or 0. A
 * limit or a step that is no number, or a step of 0, limits nothing: publishing refuses them, and a draft's preview
 * must not fail on them.
 */
function range(v: Validation, step: string | undefined): void {
  const min = text(v.settings, 'min');
  const max = text(v.settings, 'max');
  if (min !== undefined && (compareText(v.value, min) ?? 0) < 0) v.fail('min');
  else if (max !== undefined && (compareText(v.value, max) ?? 0) > 0) v.fail('max');
  const value = parseDecimal(v.value);
  const from = parseDecimal(min ?? '0');
  const by = step === undefined ? null : parseDecimal(step);
  if (value === null || from === null || by === null || by.n === 0n) return;
  if (!isMultiple(subtract(value, from), by)) v.fail('step');
}

/** The step of a number field without one: 10 to the power of minus its decimals, so 1 without decimals. */
const stepOfPlaces = (decimals: number): string => (decimals === 0 ? '1' : `0.${'0'.repeat(decimals - 1)}1`);

/** Whether a number in the wire form is a whole number from low to high. */
function isWholeNumber(value: string, low: number, high: number): boolean {
  const d: Decimal | null = parseDecimal(value);
  return d !== null && d.s === 0 && d.n >= BigInt(low) && d.n <= BigInt(high);
}

/** The day number of a limit: undefined for none, null for one outside the calendar or no limit at all. */
function bound(limit: unknown, today: () => number): number | null | undefined {
  if (limit === undefined || limit === null) return undefined;
  if (typeof limit === 'string') return parseDate(limit);
  if (typeof limit === 'object' && typeof (limit as { today?: unknown }).today === 'string') {
    const steps = parseRelative((limit as { today: string }).today);
    return steps === null ? null : applyRelative(today(), steps);
  }
  return null;
}

/** Fails dateWindow for a date outside the limits, on a weekend when the field takes weekdays only, or excluded. */
function dateWindow(v: Validation, day: number): void {
  const low = bound(v.settings['min'], v.today);
  const high = bound(v.settings['max'], v.today);
  // Day 0 is 1970-01-01, a Thursday: 0 is Sunday and 6 Saturday.
  const weekday = (((day + 4) % 7) + 7) % 7;
  const fits =
    low !== null &&
    high !== null &&
    (low === undefined || day >= low) &&
    (high === undefined || day <= high) &&
    !(v.settings['weekdaysOnly'] === true && (weekday === 0 || weekday === 6)) &&
    !texts(v.settings, 'excluded').some((date) => parseDate(date) === day);
  if (!fits) v.fail('dateWindow');
}

/** Whether a time, in seconds since midnight, keeps a step of minutes counted from a start; any time without one. */
const onMinuteStep = (seconds: number, from: number, stepMinutes: number | undefined): boolean =>
  stepMinutes === undefined || (seconds - from) % (stepMinutes * 60) === 0;

/** Each built-in type's check of a shown field's readable answers, in the order Core's reports. */
const CHECKS: Readonly<Record<string, (v: Validation) => void>> = {
  text: length,
  textarea(v) {
    length(v);
    words(v);
  },
  email(v) {
    // The domains stay on the server, which alone refuses them (ADR-0062).
    if (v.settings['confirm'] !== true) return;
    const again = v.answers.parts?.['confirm'] ?? [];
    if (again.length !== 1 || fold(again[0] ?? '') !== fold(v.value)) v.fail('confirmation');
  },
  url(v) {
    const colon = v.value.indexOf(':');
    const scheme = colon < 0 ? null : asciiLower(v.value.slice(0, colon));
    if (Array.isArray(v.settings['schemes']) && !texts(v.settings, 'schemes').some((allowed) => allowed === scheme)) {
      v.fail('invalid');
    }
  },
  number(v) {
    const decimals = number(v.settings, 'decimals');
    range(v, text(v.settings, 'step') ?? (decimals === undefined ? undefined : stepOfPlaces(decimals)));
  },
  slider(v) {
    range(v, text(v.settings, 'step'));
  },
  rating(v) {
    const count = number(v.settings, 'count');
    if (count !== undefined && !isWholeNumber(v.value, 1, count)) v.fail('invalid');
  },
  scale(v) {
    const from = number(v.settings, 'from');
    const to = number(v.settings, 'to');
    if (from !== undefined && to !== undefined && !isWholeNumber(v.value, from, to)) v.fail('invalid');
  },
  date(v) {
    const day = parseDate(v.value);
    if (day !== null) dateWindow(v, day);
  },
  time(v) {
    const seconds = parseTime(v.value);
    if (seconds === null) return;
    const min = text(v.settings, 'min');
    const max = text(v.settings, 'max');
    const earliest = min === undefined ? null : parseTime(min);
    const latest = max === undefined ? null : parseTime(max);
    if (earliest !== null && seconds < earliest) v.fail('min');
    else if (latest !== null && seconds > latest) v.fail('max');
    if (!onMinuteStep(seconds, earliest ?? 0, number(v.settings, 'stepMinutes'))) v.fail('step');
  },
  datetime(v) {
    const day = parseDate(v.value.slice(0, 10));
    const seconds = parseTime(v.value.slice(11));
    if (day === null || seconds === null) return;
    dateWindow(v, day);
    if (!onMinuteStep(seconds, 0, number(v.settings, 'stepMinutes'))) v.fail('step');
  },
};

/**
 * The errors of a field the rules leave visible (ADR-0058, ADR-0062): `required` when it has no answer and is
 * required, otherwise what its type's check reports, each kind once. A type without a check here has none in the
 * browser, and the server decides. Today comes from the context's instant and time zone, read only by a date check.
 */
export function checkField(field: CheckedField, answers: FieldAnswers, context: Context): FieldErrors {
  if (answers.values.length === 0) return { errors: field.required === true ? ['required'] : [], checks: [] };
  const errors: string[] = [];
  let day: number | undefined;
  CHECKS[field.type]?.({
    value: answers.values[0] ?? '',
    answers,
    settings: field.settings ?? {},
    today: () => (day ??= todayIn(context.now, context.timeZone)),
    fail(kind) {
      if (!errors.includes(kind)) errors.push(kind);
    },
  });
  return { errors, checks: [] };
}
