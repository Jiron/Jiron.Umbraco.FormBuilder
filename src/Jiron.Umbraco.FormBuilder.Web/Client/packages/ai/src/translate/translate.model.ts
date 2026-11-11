// The preview of a translation (ADR-0018): each text with its translation, which the editor may change or leave out;
// what Apply writes into the draft. Pure.
import type { JfbTextChange } from '../../../backoffice/public/types/jiron-form-builder';
import type { AiTranslatedTextResponseModel } from '../api';

/** One row of the preview. */
export interface TranslationRow {
  readonly path: string;
  readonly source: string;
  readonly text: string;
  readonly html: boolean;
  /** Whether Apply writes it. */
  readonly use: boolean;
}

/** The rows of a translation, each one used. */
export const rowsOf = (texts: readonly AiTranslatedTextResponseModel[]): TranslationRow[] =>
  texts.map((text) => ({ path: text.path, source: text.source, text: text.text, html: text.html, use: true }));

/** A row changed: its translation, or whether it is used. */
export function withRow(
  rows: readonly TranslationRow[],
  path: string,
  change: Partial<Pick<TranslationRow, 'text' | 'use'>>,
) {
  return rows.map((row) => (row.path === path ? { ...row, ...change } : row));
}

/** What Apply writes: each row used whose translation is not empty, in the language. */
export const changesOf = (rows: readonly TranslationRow[], culture: string): JfbTextChange[] =>
  rows
    .filter((row) => row.use && row.text.trim() !== '')
    .map((row) => ({ path: row.path, culture, text: row.text.trim() }));

/** Rich text as it reads, for the preview: its text without its tags. */
export function plainOf(html: string): string {
  const parsed = new DOMParser().parseFromString(html, 'text/html');
  return (parsed.body.textContent ?? '').replace(/\s+/g, ' ').trim();
}
