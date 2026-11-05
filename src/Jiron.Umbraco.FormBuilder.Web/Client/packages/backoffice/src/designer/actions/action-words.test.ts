// The Actions view's words (ROADMAP 10.2a, ADR-0188): English and German hold the same keys, each a text that is not
// blank; the start tables hold none of them, so the backoffice's start does not grow by them; every word the view, its
// list and its sidebar ask for is there; German puts its comma before a clause with "wenn"; and they load for a
// user's language, English always, German for German.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import { describe, expect, it } from 'vitest';
import de from '../../lang/actions.de';
import en from '../../lang/actions.en';
import startDe from '../../lang/de';
import startEn from '../../lang/en';
import { flatten, languagesFor, loadActionWords } from './action-words';

const keysOf = (dictionary: Record<string, Record<string, unknown>>): string[] =>
  Object.entries(dictionary)
    .flatMap(([area, words]) => Object.keys(words).map((key) => `${area}_${key}`))
    .sort();

/** A word as a person reads it: a function is given sample values (a name, a number). */
const shown = (word: unknown): unknown => (typeof word === 'function' ? word('Email the team', 2, 3) : word);

/** The Actions area's terms the view's files ask for: whole keys, and the stems of keys made from a value. */
function asked(): { readonly keys: Set<string>; readonly stems: Set<string> } {
  const folder = dirname(fileURLToPath(import.meta.url));
  const keys = new Set<string>();
  const stems = new Set<string>();
  for (const file of readdirSync(folder).filter((one) => one.endsWith('.ts') && !one.endsWith('.test.ts'))) {
    const text = readFileSync(join(folder, file), 'utf8');
    for (const match of text.matchAll(/#term\(\s*'([A-Za-z_]+)'/g)) keys.add(`jfbActions_${match[1]}`);
    for (const match of text.matchAll(/'jfbActions_([A-Za-z_]+)'/g)) keys.add(`jfbActions_${match[1]}`);
    for (const match of text.matchAll(/#term\(\s*`([A-Za-z]+_)\$\{/g)) stems.add(`jfbActions_${match[1]}`);
    for (const match of text.matchAll(/term: '(summary_[A-Za-z]+)'|line\('(summary_[A-Za-z]+)'/g))
      keys.add(`jfbActions_${match[1] ?? match[2]}`);
  }
  return { keys, stems };
}

describe("the Actions view's words", () => {
  it('hold the same keys in English and German, each a text that is not blank', () => {
    expect(keysOf(de)).toEqual(keysOf(en));
    for (const dictionary of [en, de])
      for (const [area, words] of Object.entries(dictionary))
        for (const [key, word] of Object.entries(words)) {
          const value = shown(word);
          expect(typeof value === 'string' && value.trim().length > 0, `${area}_${key}`).toBe(true);
        }
  });

  it("are no words of the backoffice's start", () => {
    expect(Object.keys(startEn)).not.toContain('jfbActions');
    expect(Object.keys(startDe)).not.toContain('jfbActions');
  });

  it('hold every word the view, its list and its sidebar ask for', () => {
    const { keys, stems } = asked();
    const held = keysOf(en);
    expect(keys.size).toBeGreaterThan(40);
    expect([...keys].filter((key) => !held.includes(key))).toEqual([]);
    for (const stem of stems)
      expect(
        held.some((key) => key.startsWith(stem)),
        stem,
      ).toBe(true);
  });

  it('put the comma German needs before a clause with \u201ewenn\u201c, and none before \u201ebei jedem\u201c', () => {
    const { added, none, when_submitted, when_approvedEach } = de.jfbActions;
    expect(added('Email the team', when_submitted)).toBe(
      'Hinzugef\u00fcgt. Email the team l\u00e4uft, wenn ein Eintrag eingeht.',
    );
    expect(none(when_approvedEach)).toBe(
      'Keine Aktionen laufen bei jedem eingehenden Eintrag, da dieses Formular keine manuelle Genehmigung hat.',
    );
    expect(en.jfbActions.none(en.jfbActions.when_submitted)).toBe('No actions run when an entry comes in.');
  });

  it("are looked up as Umbraco's own: by area and key, in one language", () => {
    expect(flatten('de', { jfbActions: { add: 'Hinzuf\u00fcgen' } })).toEqual({
      $code: 'de',
      $dir: 'ltr',
      jfbActions_add: 'Hinzuf\u00fcgen',
    });
  });

  it('load English for every user, and German too for a German one', async () => {
    expect(languagesFor('en-us')).toEqual(['en']);
    expect(languagesFor('fr')).toEqual(['en']);
    expect(languagesFor('de-DE')).toEqual(['en', 'de']);
    await loadActionWords('de-de');
    const english = umbLocalizationManager.localizations.get('en') as Record<string, unknown> | undefined;
    const german = umbLocalizationManager.localizations.get('de') as Record<string, unknown> | undefined;
    expect(english?.['jfbActions_addAction']).toBe('Add action');
    expect(german?.['jfbActions_addAction']).toBe('Aktion hinzuf\u00fcgen');
    // Asked again, the words are loaded once.
    await expect(loadActionWords('de-de')).resolves.toBeUndefined();
  });
});
