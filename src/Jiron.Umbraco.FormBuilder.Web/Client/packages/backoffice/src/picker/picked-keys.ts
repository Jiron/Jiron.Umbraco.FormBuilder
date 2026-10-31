// The form picker's stored value (ADR-0129): the picked forms' keys, separated by commas, in the editor's order.
// Picker/FormPickerValues.cs reads it on the server, in every format .NET reads a key; here each is turned into the
// hyphenated one the endpoints answer with, so that a key in another format is named and marked chosen, and what the
// backoffice writes is hyphenated.

const HYPHENATED = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const DIGITS = /^[0-9a-f]{32}$/;
const HEX_GROUPS =
  /^\{0x([0-9a-f]{1,8}),0x([0-9a-f]{1,4}),0x([0-9a-f]{1,4}),\{((?:0x[0-9a-f]{1,2},){7}0x[0-9a-f]{1,2})\}\}$/;

/** The 32 digits of a key written as hexadecimal groups, each group padded to its width. */
function digitsOfGroups(groups: RegExpExecArray): string {
  const bytes = (groups[4] ?? '').split(',').map((byte) => byte.slice(2).padStart(2, '0'));
  return (
    (groups[1] ?? '').padStart(8, '0') +
    (groups[2] ?? '').padStart(4, '0') +
    (groups[3] ?? '').padStart(4, '0') +
    bytes.join('')
  );
}

/**
 * A key in lower case with hyphens, from any format .NET reads: hyphenated, digits only, in braces or parentheses, or
 * as hexadecimal groups. Text that is no key comes back trimmed and in lower case, to show as not available.
 */
export function normalKey(text: string): string {
  const key = text.trim().toLowerCase();
  const inner = /^\{(.*)\}$/.exec(key)?.[1] ?? /^\((.*)\)$/.exec(key)?.[1] ?? key;
  if (HYPHENATED.test(inner)) return inner;
  const groups = HEX_GROUPS.exec(key.replace(/\s/g, ''));
  const digits = groups ? digitsOfGroups(groups) : key;
  if (!DIGITS.test(digits)) return key;
  return [digits.slice(0, 8), digits.slice(8, 12), digits.slice(12, 16), digits.slice(16, 20), digits.slice(20)].join(
    '-',
  );
}

/** The keys of a stored value in order, each once, in the hyphenated format; none for no value. */
export function readKeys(value: unknown): string[] {
  if (typeof value !== 'string') return [];
  const keys: string[] = [];
  for (const part of value.split(',')) {
    const key = normalKey(part);
    if (key && !keys.includes(key)) keys.push(key);
  }
  return keys;
}

/** The value that stores these keys, or undefined for none, so that a picker without forms counts as empty. */
export function writeKeys(keys: readonly string[]): string | undefined {
  return keys.length > 0 ? keys.join(',') : undefined;
}
