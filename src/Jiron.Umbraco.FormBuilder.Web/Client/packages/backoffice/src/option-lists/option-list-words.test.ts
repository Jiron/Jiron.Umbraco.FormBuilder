// The option list screens' words (ROADMAP 12.1, ADR-0214): English and German hold the same keys, each a text that is
// not blank; they hold exactly the keys the screens' code names and the library's shared screens say for every kind,
// with each kind's and each keep time's; and they load with the screens, not with the backoffice's start.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import de from '../lang/option-lists.de';
import en from '../lang/option-lists.en';
import startDe from '../lang/de';
import startEn from '../lang/en';
import { KINDS } from './option-list-model';

const area = (dictionary: { jfbOptionLists: Record<string, unknown> }) => dictionary.jfbOptionLists;
const shown = (text: unknown): unknown => (typeof text === 'function' ? text('Countries', 3) : text);
const here = dirname(fileURLToPath(import.meta.url));

describe('the option list words', () => {
  it('hold the same keys in English and German, each a text that is not blank', () => {
    expect(Object.keys(area(de)).sort()).toEqual(Object.keys(area(en)).sort());
    for (const dictionary of [en, de]) {
      for (const [key, text] of Object.entries(area(dictionary))) {
        expect(String(shown(text)).trim(), key).not.toBe('');
      }
    }
  });

  it("hold exactly the keys the screens' code names, and each kind's and keep time's", () => {
    const items = join(here, '..', 'library', 'items');
    const sources = [
      ...readdirSync(here).map((file) => join(here, file)),
      ...readdirSync(items).map((file) => join(items, file)),
    ]
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
      .map((file) => readFileSync(file, 'utf8'));
    const named = new Set(
      sources.flatMap((source) =>
        [
          ...source.matchAll(/#term\('([A-Za-z_]+)'/g),
          ...source.matchAll(/\bterm\(localize, (?:kind, )?'([A-Za-z_]+)'/g),
          ...source.matchAll(/\bthis\.term\('([A-Za-z_]+)'/g),
          ...source.matchAll(/'#jfbOptionLists_([A-Za-z_]+)'/g),
        ].map((match) => match[1] ?? ''),
      ),
    );
    for (const kind of [...KINDS, 'unknown']) named.add(`kind_${kind}`);
    for (const kind of KINDS) named.add(`kindHelp_${kind}`);
    for (const unit of ['none', 'seconds', 'minutes', 'hours', 'days']) named.add(`keep_${unit}`);
    // Only a kind that counts the uses in its forms, or that can be the site's default, says these.
    named.delete('userUses');
    named.delete('isDefault');
    expect(Object.keys(area(en)).sort()).toEqual([...named].sort());
  });

  it('say a German text that differs from the English where the words differ', () => {
    const same = Object.keys(area(en)).filter((key) => shown(area(en)[key]) === shown(area(de)[key]));
    // The names of columns and the language's label read the same in both.
    expect(same.sort()).toEqual(['columnName']);
  });

  it('load with their screens, not with the start', () => {
    expect(Object.keys(startEn)).not.toContain('jfbOptionLists');
    expect(Object.keys(startDe)).not.toContain('jfbOptionLists');
  });
});
