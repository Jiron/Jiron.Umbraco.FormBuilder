// The History view's words (ROADMAP 10.3a, ADR-0196): English and German hold the same keys, each a text that is not
// blank; the start tables hold none of them, so the backoffice's start does not grow by them; every word the view, its
// sidebar, its dialog and its words ask for is there; and they load for a user's language.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import { describe, expect, it } from 'vitest';
import de from '../../lang/history.de';
import en from '../../lang/history.en';
import startDe from '../../lang/de';
import startEn from '../../lang/en';
import { flatten } from '../actions/action-words';
import { loadHistoryWords } from './history-words';

const keysOf = (dictionary: Record<string, Record<string, unknown>>): string[] =>
  Object.entries(dictionary)
    .flatMap(([area, words]) => Object.keys(words).map((key) => `${area}_${key}`))
    .sort();

/** A word as a person reads it: a function is given sample values (a name, a number). */
const shown = (word: unknown): unknown => (typeof word === 'function' ? word('Version 12', 2, 3) : word);

/** The History area's terms the folder's files ask for: whole keys, and the stems of keys made from a value. */
function asked(): { readonly keys: Set<string>; readonly stems: Set<string> } {
  const folder = dirname(fileURLToPath(import.meta.url));
  const keys = new Set<string>();
  const stems = new Set<string>();
  for (const file of readdirSync(folder).filter((one) => one.endsWith('.ts') && !one.endsWith('.test.ts'))) {
    const text = readFileSync(join(folder, file), 'utf8');
    for (const match of text.matchAll(/#term\(\s*'([A-Za-z_]+)'/g)) keys.add(`jfbHistory_${match[1]}`);
    for (const match of text.matchAll(/'jfbHistory_([A-Za-z_]+)'/g)) keys.add(`jfbHistory_${match[1]}`);
    for (const match of text.matchAll(/#term\(\s*`([A-Za-z]+_)\$\{/g)) stems.add(`jfbHistory_${match[1]}`);
    for (const match of text.matchAll(/`jfbHistory_([A-Za-z]+_)\$\{/g)) stems.add(`jfbHistory_${match[1]}`);
  }
  return { keys, stems };
}

describe("the History view's words", () => {
  it('hold the same keys in English and German, none blank, and none in the start tables', () => {
    expect(keysOf(de)).toEqual(keysOf(en));
    for (const [code, dictionary] of [
      ['en', en],
      ['de', de],
    ] as const)
      for (const [key, word] of Object.entries(flatten(code, dictionary)).filter(([one]) => !one.startsWith('$')))
        expect(String(shown(word)).trim(), `${code} ${key}`).not.toBe('');
    const start = new Set([...keysOf(startEn), ...keysOf(startDe)]);
    expect(keysOf(en).filter((key) => start.has(key))).toEqual([]);
  });

  it('have every word the view, its sidebar, its dialog and its words ask for', () => {
    const known = new Set(keysOf(en));
    const { keys, stems } = asked();
    expect([...keys].filter((key) => !known.has(key))).toEqual([]);
    for (const stem of stems)
      expect(
        [...known].some((key) => key.startsWith(stem)),
        stem,
      ).toBe(true);
  });

  it('load for a user of German in German, with English beside it', async () => {
    await loadHistoryWords('de-DE');
    const wordIn = (code: string) =>
      (umbLocalizationManager.localizations.get(code) as Record<string, unknown> | undefined)?.['jfbHistory_compare'];
    expect([wordIn('de'), wordIn('en')]).toEqual(['Vergleichen', 'Compare']);
  });
});
