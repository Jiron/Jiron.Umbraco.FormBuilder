// The forms of an email address, a phone number and a web address (ADR-0069), the same as Core's BuiltInForms: read
// code unit by code unit, so both runtimes take the same answers. A character outside ASCII is a code unit from U+00A0
// on that cleaning does not trim: letters of every script, and no space or control character.
import { isTrimmed } from './text.ts';

/** The ASCII symbols an atom of an email address's local part may hold besides letters and digits (RFC 5322). */
const ATOM_SYMBOLS = "!#$%&'*+-/=?^_`{|}~";

const isDigit = (unit: number): boolean => unit >= 0x30 && unit <= 0x39;
const isLetterOrDigit = (unit: number): boolean =>
  isDigit(unit) || (unit >= 0x41 && unit <= 0x5a) || (unit >= 0x61 && unit <= 0x7a);
const isOutsideAscii = (unit: number): boolean => unit >= 0xa0 && !isTrimmed(unit);
const units = (text: string): number[] => Array.from({ length: text.length }, (_, i) => text.charCodeAt(i));

const isAtomUnit = (unit: number): boolean =>
  isLetterOrDigit(unit) || ATOM_SYMBOLS.includes(String.fromCharCode(unit)) || isOutsideAscii(unit);
const isLabelUnit = (unit: number): boolean => isLetterOrDigit(unit) || unit === 0x2d || isOutsideAscii(unit);

/** A separator of a phone number's digits outside its brackets: `-`, `.`, `/` or a space. */
const isSeparator = (unit: number): boolean => unit === 0x2d || unit === 0x2e || unit === 0x2f || isTrimmed(unit);

/**
 * Whether the text is a host name: at most 253 code units in at least two labels apart by dots, each of 1 to 63 ASCII
 * letters, digits, hyphens and characters outside ASCII, starting and ending with no hyphen, and the last not only
 * digits.
 */
export function isHostName(value: string): boolean {
  const labels = value.split('.');
  return (
    value.length <= 253 &&
    labels.length >= 2 &&
    labels.every(
      (label) =>
        label.length >= 1 &&
        label.length <= 63 &&
        !label.startsWith('-') &&
        !label.endsWith('-') &&
        units(label).every(isLabelUnit),
    ) &&
    !units(labels[labels.length - 1] ?? '').every(isDigit)
  );
}

/** Whether the text is an email address: a local part of 1 to 64 code units, atoms apart by single dots; one `@`; and a host name. */
export function isEmailAddress(value: string): boolean {
  const at = value.indexOf('@');
  return (
    at >= 1 &&
    at <= 64 &&
    value
      .slice(0, at)
      .split('.')
      .every((atom) => atom.length > 0 && units(atom).every(isAtomUnit)) &&
    isHostName(value.slice(at + 1))
  );
}

/** Where the run of spaces that ends at the index starts. */
function spacesBefore(value: string, index: number): number {
  let start = index;
  while (start > 0 && isTrimmed(value.charCodeAt(start - 1))) start--;
  return start;
}

/**
 * The number before an extension (optional spaces, `ext.`, `ext`, `x` or `#` in any case, optional spaces and 1 to 6
 * digits at the end), or the whole text when it has none.
 */
function withoutExtension(value: string): string {
  let digitsAt = value.length;
  while (digitsAt > 0 && isDigit(value.charCodeAt(digitsAt - 1))) digitsAt--;
  const count = value.length - digitsAt;
  if (count === 0 || count > 6) return value;
  const markerEnd = spacesBefore(value, digitsAt);
  for (const marker of ['ext.', 'ext', 'x', '#']) {
    const start = markerEnd - marker.length;
    if (start >= 0 && value.slice(start, markerEnd).replace(/[A-Z]/g, (letter) => letter.toLowerCase()) === marker) {
      return value.slice(0, spacesBefore(value, start));
    }
  }
  return value;
}

/**
 * Whether the text is a phone number in a lenient international form: digits, spaces, `-`, `.` and `/`, an optional
 * `+` first and at most one pair of brackets holding digits and spaces, at least one digit; starting with `+`, `(` or
 * a digit and ending with a digit, with 4 to 17 digits; then an optional extension.
 */
export function isPhoneNumber(value: string): boolean {
  const number = withoutExtension(value);
  // An empty text's first code unit is NaN, which starts no number.
  const first = number.charCodeAt(0);
  if (!(first === 0x2b || first === 0x28 || isDigit(first))) return false;
  if (!isDigit(number.charCodeAt(number.length - 1))) return false;
  let digits = 0;
  let bracketed = 0;
  let open = -1;
  let close = -1;
  for (let i = 0; i < number.length; i++) {
    const unit = number.charCodeAt(i);
    const inBrackets = open >= 0 && close < 0;
    if (isDigit(unit)) {
      digits++;
      if (inBrackets) bracketed++;
    } else if (unit === 0x28 && open < 0) {
      open = i;
    } else if (unit === 0x29 && inBrackets) {
      close = i;
    } else if (unit === 0x2b && i === 0) {
      // The plus that starts an international number.
    } else if (inBrackets ? !isTrimmed(unit) : !isSeparator(unit)) {
      // Inside the brackets only spaces part the digits; outside them any separator does.
      return false;
    }
  }
  return digits >= 4 && digits <= 17 && open < 0 === close < 0 && (open < 0 || bracketed > 0);
}

/**
 * Whether the text is a web address after its scheme: `//`, a host name with an optional port from 1 to 65535 and no
 * user name or password, then a path, query or fragment without control characters or characters cleaning trims. The
 * scheme itself, before the first colon, is the field's to check; a text without one is read from its start, and the
 * field refuses it for its scheme.
 */
export function isWebAddress(value: string): boolean {
  const colon = value.indexOf(':');
  if (!value.startsWith('//', colon + 1)) return false;
  const start = colon + 3;
  let end = value.length;
  for (let i = start; i < value.length; i++) {
    const unit = value.charCodeAt(i);
    if (unit === 0x2f || unit === 0x3f || unit === 0x23) {
      end = i;
      break;
    }
  }
  // A host name holds no colon, so the last one starts the port; a user name or password puts an @ in the host.
  const authority = value.slice(start, end);
  const portAt = authority.lastIndexOf(':');
  const host = portAt < 0 ? authority : authority.slice(0, portAt);
  const port = portAt < 0 ? null : authority.slice(portAt + 1);
  return (
    isHostName(host) &&
    (port === null ||
      (port.length >= 1 &&
        port.length <= 5 &&
        units(port).every(isDigit) &&
        Number(port) >= 1 &&
        Number(port) <= 65535)) &&
    !units(value.slice(end)).some((unit) => unit < 0x20 || (unit >= 0x7f && unit < 0xa0) || isTrimmed(unit))
  );
}
