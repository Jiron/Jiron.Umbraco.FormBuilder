// The Entries view's words (ROADMAP 11.1, ADR-0207): English and German hold the same keys, each a text that is not
// blank; the start tables hold none of them, so the backoffice's start does not grow by them; every word the view, its
// dialogs and its model ask for is there; and they load for a user's language.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import { describe, expect, it } from 'vitest';
import { flatten } from '../designer/actions/action-words';
import startDe from '../lang/de';
import de from '../lang/entries.de';
import en from '../lang/entries.en';
import startEn from '../lang/en';
import { BULK_ORDER } from './entry-list-model';
import { loadEntryWords } from './entry-words';

const keysOf = (dictionary: Record<string, Record<string, unknown>>): string[] =>
  Object.entries(dictionary)
    .flatMap(([area, words]) => Object.keys(words).map((key) => `${area}_${key}`))
    .sort();

/** A word as a person reads it: a function is given sample values (a name, a number). */
const shown = (word: unknown): unknown => (typeof word === 'function' ? word('Person 0042', 2, 3) : word);

/**
 * The Entries area's terms the shared folder and the workspace's view ask for: whole keys (as a term, either side of a
 * choice, a refusal, a range's labels, a shortcut's line and a model's whole key), and the stems of keys made from a value.
 */
function asked(): { readonly keys: Set<string>; readonly stems: Set<string> } {
  const here = dirname(fileURLToPath(import.meta.url));
  const keys = new Set<string>(BULK_ORDER.map((action) => `jfbEntries_${action}`));
  const stems = new Set<string>();
  for (const folder of [here, join(here, '..', 'designer', 'entries')])
    for (const file of readdirSync(folder).filter((one) => one.endsWith('.ts') && !one.endsWith('.test.ts'))) {
      const text = readFileSync(join(folder, file), 'utf8');
      const add = (pattern: RegExp) => {
        for (const match of text.matchAll(pattern))
          for (const key of match.slice(1)) if (key) keys.add(`jfbEntries_${key}`);
      };
      add(/#term\(\s*'([A-Za-z_]+)'/g);
      add(/#term\([^()]*\?\s*'([A-Za-z_]+)'\s*:\s*'([A-Za-z_]+)'\)/g);
      add(/#refuse\('([A-Za-z_]+)'\)/g);
      add(/#pair\([^()]*'([A-Za-z_]+)', '([A-Za-z_]+)'/g);
      add(/does: '([A-Za-z_]+)'/g);
      add(/'jfbEntries_([A-Za-z_]+)'/g);
      for (const match of text.matchAll(/#term\(\s*`([A-Za-z]+_)\$\{/g)) stems.add(`jfbEntries_${match[1]}`);
      for (const match of text.matchAll(/`jfbEntries_([A-Za-z]+_)\$\{/g)) stems.add(`jfbEntries_${match[1]}`);
    }
  return { keys, stems };
}

describe("the Entries view's words", () => {
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

  it('have every word the view, its dialogs and its model ask for', () => {
    const known = new Set(keysOf(en));
    const { keys, stems } = asked();
    expect(keys.size).toBeGreaterThan(60);
    expect([...keys].filter((key) => !known.has(key))).toEqual([]);
    expect(stems.size).toBeGreaterThan(5);
    for (const stem of stems)
      expect(
        [...known].some((key) => key.startsWith(stem)),
        stem,
      ).toBe(true);
  });

  it('load for a user of German in German, with English beside it', async () => {
    await loadEntryWords('de-DE');
    const wordIn = (code: string) =>
      (umbLocalizationManager.localizations.get(code) as Record<string, unknown> | undefined)?.['jfbEntries_approve'];
    expect([wordIn('de'), wordIn('en')]).toEqual(['Genehmigen', 'Approve']);
  });
});
