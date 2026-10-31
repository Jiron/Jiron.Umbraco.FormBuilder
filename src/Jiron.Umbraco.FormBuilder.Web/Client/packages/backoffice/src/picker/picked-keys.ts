// The form picker's stored value (ADR-0129): the picked forms' keys, separated by commas, in the editor's order.
// Picker/FormPickerValues.cs reads it on the server.

/** The keys of a stored value in order, each once and in lower case; none for no value. */
export function readKeys(value: unknown): string[] {
  if (typeof value !== 'string') return [];
  const keys: string[] = [];
  for (const part of value.split(',')) {
    const key = part.trim().toLowerCase();
    if (key && !keys.includes(key)) keys.push(key);
  }
  return keys;
}

/** The value that stores these keys, or undefined for none, so that a picker without forms counts as empty. */
export function writeKeys(keys: readonly string[]): string | undefined {
  return keys.length > 0 ? keys.join(',') : undefined;
}
