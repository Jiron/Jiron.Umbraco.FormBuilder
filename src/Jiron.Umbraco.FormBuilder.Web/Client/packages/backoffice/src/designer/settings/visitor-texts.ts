// A site's or a form's own wording of the package's visitor texts (ROADMAP 14.2, ADR-0230): a text's key to its wording
// per culture. The editor writes the culture of the language it edits, never "" (every language), so rewording a text
// in German leaves it the package's in French. Pure: the element draws what these give.
import type { VisitorTextsResponseModel } from '../../api';

/** A text's key to its wording per culture. */
export type VisitorTexts = Readonly<Record<string, Readonly<Record<string, string>>>>;

/** The longest wording, as the server holds it. */
export const MOST_CHARACTERS = 500;

/** A text the editor lists: its key, its group and the package's words in the language shown. */
export interface VisitorTextRow {
  readonly key: string;
  readonly group: string;
  readonly text: string;
  readonly values: readonly string[];
  /** The wording in the culture shown; empty where it has none there. */
  readonly wording: string;
}

/** Why a wording cannot stand for its text, or null when it can. */
export type WordingProblem =
  | { readonly kind: 'line' }
  | { readonly kind: 'long'; readonly most: number }
  | { readonly kind: 'values'; readonly values: readonly string[] };

/** Whether a value is a map of texts, each a map of strings. */
export function isVisitorTexts(value: unknown): value is VisitorTexts {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  return Object.values(value).every(
    (cultures) =>
      cultures !== null &&
      typeof cultures === 'object' &&
      !Array.isArray(cultures) &&
      Object.values(cultures as object).every((text) => typeof text === 'string'),
  );
}

/** The wording a text has in a culture, exactly that culture's; empty for none. */
export function wordingIn(texts: VisitorTexts | undefined, key: string, culture: string): string {
  const cultures = texts?.[key];
  if (!cultures) return '';
  const found = Object.keys(cultures).find((name) => name.toLowerCase() === culture.toLowerCase());
  return found === undefined ? '' : (cultures[found] ?? '');
}

/**
 * The texts reworded, and those just chosen to be, in the package's order: each with its group, the package's words in
 * the language shown and its wording there. A text the package no longer has is left out.
 */
export function rowsOf(
  texts: VisitorTexts | undefined,
  culture: string,
  catalogue: VisitorTextsResponseModel,
  chosen: ReadonlySet<string>,
): VisitorTextRow[] {
  const rows: VisitorTextRow[] = [];
  for (const group of catalogue.groups)
    for (const text of group.texts)
      if ((texts && Object.hasOwn(texts, text.key)) || chosen.has(text.key))
        rows.push({
          key: text.key,
          group: group.alias,
          text: text.text,
          values: text.values,
          wording: wordingIn(texts, text.key, culture),
        });
  return rows;
}

/**
 * The texts with a wording in a culture; an empty wording takes the culture's away, and a text without any wording
 * left goes, so the package's words are back. The texts themselves are never changed.
 */
export function withWording(
  texts: VisitorTexts | undefined,
  key: string,
  culture: string,
  wording: string,
): VisitorTexts {
  const cultures: Record<string, string> = Object.fromEntries(
    Object.entries(texts?.[key] ?? {}).filter(([name]) => name.toLowerCase() !== culture.toLowerCase()),
  );
  if (wording.trim() !== '') cultures[culture] = wording;
  const entries = Object.entries(texts ?? {});
  if (Object.keys(cultures).length === 0) return Object.fromEntries(entries.filter(([name]) => name !== key));
  return Object.fromEntries(
    Object.hasOwn(texts ?? {}, key)
      ? entries.map(([name, value]) => [name, name === key ? cultures : value])
      : [...entries, [key, cultures]],
  );
}

/** The texts without a text's wording in a culture, the text gone once it has none left. */
export const withoutWording = (texts: VisitorTexts | undefined, key: string, culture: string): VisitorTexts =>
  withWording(texts, key, culture, '');

/** Whether a character breaks the line or is a control one, as the server counts them: the line and paragraph separators too. */
const isControl = (character: string): boolean => {
  const code = character.charCodeAt(0);
  return code < 0x20 || (code >= 0x7f && code < 0xa0) || code === 0x2028 || code === 0x2029;
};

/** Why a wording cannot stand for its text: on two lines, too long, or naming a value or a brace the text has not. */
export function wordingProblem(wording: string, values: readonly string[]): WordingProblem | null {
  if ([...wording].some(isControl)) return { kind: 'line' };
  if (wording.length > MOST_CHARACTERS) return { kind: 'long', most: MOST_CHARACTERS };
  const rest = wording.replace(/\{[A-Za-z0-9]+\}/g, (value) => (values.includes(value) ? '' : value));
  return rest.includes('{') || rest.includes('}') ? { kind: 'values', values } : null;
}
