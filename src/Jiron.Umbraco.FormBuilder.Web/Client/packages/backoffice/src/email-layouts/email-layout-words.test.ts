// The email layout screens' words (ROADMAP 12.1, ADR-0215): English and German hold the same keys, each a text that is
// not blank; they hold exactly the keys the screens' code names, those the library's shared screens say for every kind,
// and each family the code builds a key of; and they load with the screens, not with the start.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import startDe from '../lang/de';
import de from '../lang/email-layouts.de';
import en from '../lang/email-layouts.en';
import startEn from '../lang/en';
import { COLOURS } from './email-layout-model';

const area = (dictionary: { jfbEmailLayouts: Record<string, unknown> }) => dictionary.jfbEmailLayouts;
const shown = (text: unknown): unknown => (typeof text === 'function' ? text('Brand mail', 3) : text);
const here = dirname(fileURLToPath(import.meta.url));

/** The keys the code builds of a family's name and a part. */
const FAMILIES: Record<string, readonly string[]> = {
  look: ['card', 'letter'],
  lookHelp: ['card', 'letter'],
  colour: COLOURS,
  text: ['header', 'footer', 'logoAlt'],
  problem: ['name', 'alias', 'aliasTaken', 'other', 'logoMedia', 'logoUrl', 'long', 'colour'],
};

describe('the email layout words', () => {
  it('hold the same keys in English and German, each a text that is not blank', () => {
    expect(Object.keys(area(de)).sort()).toEqual(Object.keys(area(en)).sort());
    for (const dictionary of [en, de]) {
      for (const [key, text] of Object.entries(area(dictionary))) {
        expect(String(shown(text)).trim(), key).not.toBe('');
      }
    }
  });

  it("hold exactly the keys the screens' code names, the shared screens' and each family's", () => {
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
          ...source.matchAll(/#term\(\s*'([A-Za-z_]+)'/g),
          ...source.matchAll(/\bthis\.term\(\s*'([A-Za-z_]+)'/g),
          ...source.matchAll(/\bterm\(localize, [A-Za-z_]+, '([A-Za-z_]+)'/g),
          ...source.matchAll(/'#jfbEmailLayouts_([A-Za-z_]+)'/g),
        ].map((match) => match[1] ?? ''),
      ),
    );
    for (const [family, parts] of Object.entries(FAMILIES)) for (const part of parts) named.add(`${family}_${part}`);
    // The keys the view chooses between: the scheme, the side shown, the preview's frame and the colours' help.
    for (const key of [
      'light',
      'dark',
      'showSettings',
      'showPreview',
      'previewLight',
      'previewDark',
      'coloursHelp',
      'coloursDarkHelp',
    ])
      named.add(key);
    expect(Object.keys(area(en)).sort()).toEqual([...named].sort());
  });

  it('say a German text that differs from the English where the words differ', () => {
    const same = Object.keys(area(en)).filter((key) => shown(area(en)[key]) === shown(area(de)[key]));
    // A column's name, the view's name, the text's colour and the logo read the same in both.
    expect(same.sort()).toEqual(['colour_text', 'columnName', 'logo', 'view']);
  });

  it('load with their screens, not with the start', () => {
    expect(Object.keys(startEn)).not.toContain('jfbEmailLayouts');
    expect(Object.keys(startDe)).not.toContain('jfbEmailLayouts');
  });
});
