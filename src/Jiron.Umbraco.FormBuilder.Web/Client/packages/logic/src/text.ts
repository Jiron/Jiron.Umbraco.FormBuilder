// How the shared logic reads text (ADR-0005), the same in C#. Both runtimes spell out every step, because their
// built-in trims, case mappings and whitespace classes differ.

const REPLACEMENT = String.fromCharCode(0xfffd);
const DOTTED_CAPITAL_I = 0x130;
const DOTLESS_SMALL_I = 0x131;

/** Code units trimmed from both ends of every value and operand: the same explicit set in both runtimes. */
const TRIMMED: ReadonlySet<number> = new Set([
  9, 10, 11, 12, 13, 32, 0x85, 0xa0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008,
  0x2009, 0x200a, 0x2028, 0x2029, 0x202f, 0x205f, 0x3000, 0xfeff,
]);

/**
 * The blocks whose letters fold: Latin, Greek, Cyrillic, Armenian and the full-width Latin letters. Their case
 * mappings have been stable in every runtime for years; other scripts gained mappings recently, so runtimes with
 * older Unicode data (a server's ICU, an older browser) would fold them differently. Their letters compare exactly.
 */
const FOLDED: readonly (readonly [number, number])[] = [
  [0x0000, 0x024f],
  [0x0370, 0x03ff],
  [0x0400, 0x052f],
  [0x0530, 0x058f],
  [0x1e00, 0x1eff],
  [0x1f00, 0x1fff],
  [0xff21, 0xff3a],
  [0xff41, 0xff5a],
];

/** Noncharacters: U+FDD0 to U+FDEF, and the last two code points of every plane. */
const isNoncharacter = (codePoint: number): boolean =>
  (codePoint >= 0xfdd0 && codePoint <= 0xfdef) || (codePoint & 0xfffe) === 0xfffe;

const isHigh = (unit: number): boolean => unit >= 0xd800 && unit <= 0xdbff;
const isLow = (unit: number): boolean => unit >= 0xdc00 && unit <= 0xdfff;

/** Lone surrogates and noncharacters become U+FFFD (.NET refuses to normalise U+FFFE). */
function replaceInvalid(value: string): string {
  let out = '';
  for (let i = 0; i < value.length; i++) {
    const unit = value.charCodeAt(i);
    if (isHigh(unit) && i + 1 < value.length && isLow(value.charCodeAt(i + 1))) {
      const codePoint = (unit - 0xd800) * 0x400 + (value.charCodeAt(i + 1) - 0xdc00) + 0x10000;
      out += isNoncharacter(codePoint) ? REPLACEMENT : value.slice(i, i + 2);
      i++;
    } else {
      out += isHigh(unit) || isLow(unit) || isNoncharacter(unit) ? REPLACEMENT : value.charAt(i);
    }
  }
  return out;
}

/** Lone surrogates and noncharacters become U+FFFD, and the text is normalised to NFC. Patterns get this too. */
export function normalise(value: string): string {
  return replaceInvalid(value).normalize('NFC');
}

/**
 * Lone surrogates and noncharacters become U+FFFD, the text is normalised to NFC, and the explicit set is trimmed
 * from both ends.
 */
export function clean(value: string): string {
  const text = normalise(value);
  let start = 0;
  let end = text.length;
  while (start < end && TRIMMED.has(text.charCodeAt(start))) start++;
  while (end > start && TRIMMED.has(text.charCodeAt(end - 1))) end--;
  return text.slice(start, end);
}

/** Whether the code point lies in one of the folded blocks. */
export const folds = (codePoint: number): boolean =>
  FOLDED.some(([low, high]) => codePoint >= low && codePoint <= high);

/** Whether cleaning trims the code unit from the ends of a text: the explicit set both runtimes share. */
export const isTrimmed = (unit: number): boolean => TRIMMED.has(unit);

/**
 * The Greek letters with a ypogegrammeni (U+1F80 to U+1FAF, U+1FB3, U+1FC3, U+1FF3): their full uppercase mapping is
 * two letters, which JavaScript uses, while .NET uses the simple one, so `upper` leaves them as they are (ADR-0064).
 */
const withYpogegrammeni = (codePoint: number): boolean =>
  (codePoint >= 0x1f80 && codePoint <= 0x1faf) || codePoint === 0x1fb3 || codePoint === 0x1fc3 || codePoint === 0x1ff3;

/** A one-character mapping, or the character itself when the runtime's mapping would expand it. */
function single(mapped: string, original: string): string {
  return [...mapped].length === 1 ? mapped : original;
}

/** The words of a text: the runs of characters between the code units that cleaning trims. */
export function countWords(value: string): number {
  let words = 0;
  let inWord = false;
  for (let i = 0; i < value.length; i++) {
    const space = TRIMMED.has(value.charCodeAt(i));
    if (!space && !inWord) words++;
    inWord = !space;
  }
  return words;
}

/**
 * Folds case one character at a time, in the folded blocks only: the simple uppercase mapping, then the simple
 * lowercase mapping. So final and medial sigma fold alike, sharp s never becomes "ss", and both Turkish i's fold
 * to "i".
 */
export function fold(value: string): string {
  let out = '';
  for (const ch of value) {
    const codePoint = ch.codePointAt(0) ?? 0;
    if (!folds(codePoint)) {
      out += ch;
    } else if (codePoint === DOTTED_CAPITAL_I || codePoint === DOTLESS_SMALL_I) {
      out += 'i';
    } else {
      const upper = single(ch.toUpperCase(), ch);
      out += single(upper.toLowerCase(), upper);
    }
  }
  return out;
}

/**
 * One character's case mapping for the `upper` and `lower` filters (ADR-0064): in the folded blocks only, and only
 * when it is one character that stays in them; the Turkish i's keep their case, because the runtimes map them
 * differently.
 */
function mapCase(ch: string, mapped: string): string {
  const codePoint = ch.codePointAt(0) ?? 0;
  if (!folds(codePoint) || codePoint === DOTTED_CAPITAL_I || codePoint === DOTLESS_SMALL_I) return ch;
  const one = single(mapped, ch);
  return folds(one.codePointAt(0) ?? 0) ? one : ch;
}

/** The text in capitals, character by character, as the `upper` filter writes it (ADR-0064). */
export function upper(value: string): string {
  let out = '';
  for (const ch of value) {
    out += withYpogegrammeni(ch.codePointAt(0) ?? 0) ? ch : mapCase(ch, ch.toUpperCase());
  }
  return out;
}

/** The text in small letters, character by character, as the `lower` filter writes it (ADR-0064). */
export function lower(value: string): string {
  let out = '';
  for (const ch of value) out += mapCase(ch, ch.toLowerCase());
  return out;
}
