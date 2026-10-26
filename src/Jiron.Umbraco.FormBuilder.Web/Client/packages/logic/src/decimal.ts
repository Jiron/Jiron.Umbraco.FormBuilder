// Exact decimal numbers (ADR-0005), the same in C#: a BigInt n and a scale s for the value n / 10^s. Neither runtime
// uses its own number type for answers: C# decimal and JavaScript doubles round differently.
import { LogicError } from './errors.ts';

/** An exact decimal n / 10^s with s >= 0 and no trailing zeros in the fraction (so equal values look alike). */
export interface Decimal {
  readonly n: bigint;
  readonly s: number;
}

/** Digits a stored number may have in all (leading zeros don't count), and after the point (ADR-0003). */
export const STORED_DIGITS = 15;
/** Places a division keeps, rounded half away from zero. */
export const DIVISION_PLACES = 20;
/** The most places `round` takes. */
export const MAX_ROUND_PLACES = 20;
/** Digits an intermediate result may have before a calculation is out of range. */
export const MAX_DIGITS = 60;

const ZERO = 48;
const NINE = 57;
const MINUS = 45;
const POINT = 46;

const pow10 = (k: number): bigint => 10n ** BigInt(k);
const abs = (n: bigint): bigint => (n < 0n ? -n : n);
const digits = (n: bigint): number => abs(n).toString().length;

function normalise(n: bigint, s: number): Decimal {
  while (s > 0 && n % 10n === 0n) {
    n /= 10n;
    s--;
  }
  return { n, s };
}

function inRange(d: Decimal): Decimal {
  if (digits(d.n) > MAX_DIGITS) throw new LogicError('out_of_range');
  return d;
}

function readDigits(text: string, start: number): number {
  let i = start;
  while (i < text.length && text.charCodeAt(i) >= ZERO && text.charCodeAt(i) <= NINE) i++;
  return i;
}

function trimZeros(digitsText: string): string {
  let end = digitsText.length;
  while (end > 0 && digitsText.charCodeAt(end - 1) === ZERO) end--;
  return digitsText.slice(0, end);
}

/**
 * Parses the wire form: an optional minus, an integer part without leading zeros, and an optional point followed
 * by at least one digit. ASCII digits only; no plus sign, exponent, group separator or culture. Null otherwise.
 */
export function parseDecimal(text: string): Decimal | null {
  const parts = readParts(text);
  if (parts === null) return null;
  // The fraction's trailing zeros are gone before the digits become a number, so a long run of them costs one pass.
  const n = BigInt(parts.integer + parts.fraction);
  return { n: parts.negative ? -n : n, s: parts.fraction.length };
}

/** The canonical wire form: no trailing fraction zeros, no "-0". */
export function formatDecimal(d: Decimal): string {
  const body = abs(d.n)
    .toString()
    .padStart(d.s + 1, '0');
  const point = body.length - d.s;
  const text = d.s === 0 ? body : body.slice(0, point) + '.' + body.slice(point);
  return d.n < 0n ? '-' + text : text;
}

/** Applies the storage limits: at most 15 digits in all (leading zeros don't count), at most 15 after the point. */
export function stored(d: Decimal): Decimal {
  if (digits(d.n) > STORED_DIGITS || d.s > STORED_DIGITS) throw new LogicError('too_many_digits');
  return d;
}

/**
 * Orders two values in the wire form as the numbers they are, without building either: by the sign, then the integer
 * digits, then the fraction's digits without its trailing zeros. The work is linear in their length, so a long answer
 * costs no more than reading it. Null when either is not in the wire form (`parseDecimal`).
 */
export function compareText(a: string, b: string): number | null {
  const x = readParts(a);
  const y = readParts(b);
  if (x === null || y === null) return null;
  if (x.negative !== y.negative) return x.negative ? -1 : 1;
  // Without leading zeros, the longer integer part is the larger; a fraction that is a prefix of another is smaller.
  let magnitude = Math.sign(x.integer.length - y.integer.length);
  if (magnitude === 0) magnitude = order(x.integer, y.integer);
  if (magnitude === 0) magnitude = order(x.fraction, y.fraction);
  return x.negative ? -magnitude : magnitude;
}

interface Parts {
  readonly negative: boolean;
  readonly integer: string;
  readonly fraction: string;
}

// The parts of a value in the wire form, the one reading of its grammar that parseDecimal and compareText share: the
// sign (never for zero, so -0 is 0), the integer digits and the fraction's digits without their trailing zeros.
function readParts(text: string): Parts | null {
  const minus = text.charCodeAt(0) === MINUS;
  const start = minus ? 1 : 0;
  const intEnd = readDigits(text, start);
  const integer = text.slice(start, intEnd);
  if (integer.length === 0 || (integer.length > 1 && integer.charCodeAt(0) === ZERO)) return null;
  let fraction = '';
  if (intEnd < text.length) {
    if (text.charCodeAt(intEnd) !== POINT) return null;
    const fractionEnd = readDigits(text, intEnd + 1);
    if (fractionEnd === intEnd + 1 || fractionEnd !== text.length) return null;
    fraction = trimZeros(text.slice(intEnd + 1, fractionEnd));
  }
  return { negative: minus && !(integer === '0' && fraction === ''), integer, fraction };
}

// Two runs of digits in code-unit order: equal lengths for integer parts; for fractions, a prefix comes first.
const order = (a: string, b: string): number => (a < b ? -1 : a > b ? 1 : 0);

/** The exact sum. */
export function add(a: Decimal, b: Decimal): Decimal {
  const s = Math.max(a.s, b.s);
  return inRange(normalise(a.n * pow10(s - a.s) + b.n * pow10(s - b.s), s));
}

/** The exact difference. */
export function subtract(a: Decimal, b: Decimal): Decimal {
  return add(a, { n: -b.n, s: b.s });
}

/** Whether a is a whole multiple of b, which is not 0: a value on a step. */
export function isMultiple(a: Decimal, b: Decimal): boolean {
  const s = Math.max(a.s, b.s);
  return (a.n * pow10(s - a.s)) % (b.n * pow10(s - b.s)) === 0n;
}

/** The exact product. */
export function multiply(a: Decimal, b: Decimal): Decimal {
  return inRange(normalise(a.n * b.n, a.s + b.s));
}

/** num / den rounded to an integer, half away from zero (BigInt division truncates toward zero). */
function roundedQuotient(num: bigint, den: bigint): bigint {
  const q = num / den;
  const r = num % den;
  if (2n * abs(r) < abs(den)) return q;
  return num < 0n !== den < 0n ? q - 1n : q + 1n;
}

/** The quotient, kept to DIVISION_PLACES places and rounded half away from zero. */
export function divide(a: Decimal, b: Decimal): Decimal {
  if (b.n === 0n) throw new LogicError('division_by_zero');
  // a / b = (a.n * 10^b.s) / (b.n * 10^a.s), kept to DIVISION_PLACES places.
  const q = roundedQuotient(a.n * pow10(b.s + DIVISION_PLACES), b.n * pow10(a.s));
  return inRange(normalise(q, DIVISION_PLACES));
}

/** Rounds to `scale` places, half away from zero. */
export function round(a: Decimal, scale: number): Decimal {
  if (a.s <= scale) return a;
  return inRange(normalise(roundedQuotient(a.n, pow10(a.s - scale)), scale));
}

/** A value read from outside (a calculation's constant or an answer): the wire form within MAX_DIGITS. */
export function operand(text: string): Decimal {
  const d = parseDecimal(text);
  if (d === null) throw new LogicError('not_a_number');
  return inRange(d);
}

/**
 * An answer a calculation reads: the wire form, or null. Past MAX_DIGITS digits it is out of range, which is found
 * before the digits become a number, so a long answer costs what reading it costs.
 */
export function readNumber(text: string): Decimal | null {
  const parts = readParts(text);
  if (parts === null) return null;
  const all = parts.integer + parts.fraction;
  let first = 0;
  while (first < all.length - 1 && all.charCodeAt(first) === ZERO) first++;
  if (all.length - first > MAX_DIGITS) throw new LogicError('out_of_range');
  const n = BigInt(all);
  return { n: parts.negative ? -n : n, s: parts.fraction.length };
}

/** A whole number. */
export function integer(value: number): Decimal {
  return { n: BigInt(value), s: 0 };
}

/** The value with the other sign; zero stays zero. */
export function negate(d: Decimal): Decimal {
  return { n: -d.n, s: d.s };
}

/** Orders two values: negative when a is the smaller, 0 when they are equal, positive when a is the larger. */
export function compare(a: Decimal, b: Decimal): number {
  const s = Math.max(a.s, b.s);
  const x = a.n * pow10(s - a.s);
  const y = b.n * pow10(s - b.s);
  return x < y ? -1 : x > y ? 1 : 0;
}
