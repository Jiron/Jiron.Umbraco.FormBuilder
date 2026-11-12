// The theme screens' words (ROADMAP 12.1, ADR-0215): English and German hold the same keys, each a text that is not
// blank; they hold exactly the keys the screens' code names, those the library's shared screens say for every kind and
// the site's default, and each family the code builds a key of; and they load with the screens, not with the start.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import startDe from '../lang/de';
import startEn from '../lang/en';
import de from '../lang/themes.de';
import en from '../lang/themes.en';

const area = (dictionary: { jfbThemes: Record<string, unknown> }) => dictionary.jfbThemes;
const shown = (text: unknown): unknown => (typeof text === 'function' ? text('Autumn', 3) : text);
const here = dirname(fileURLToPath(import.meta.url));

/** The keys the code builds of a family's name and a part. */
const FAMILIES: Record<string, readonly string[]> = {
  section: ['colours', 'type', 'spacing', 'buttons', 'other', 'more'],
  group: ['lines', 'signals', 'more', 'buttons'],
  colour: [
    'text',
    'textMuted',
    'surface',
    'border',
    'rule',
    'accent',
    'onAccent',
    'focus',
    'error',
    'button',
    'onButton',
    'buttonHover',
  ],
  font: ['site', 'sans', 'serif', 'mono', 'custom'],
  scale: ['compact', 'default', 'generous', 'custom'],
  spacing: ['compact', 'default', 'roomy', 'custom'],
  buttons: ['filled', 'outlined'],
  removed: ['import', 'atRule', 'nested', 'address', 'script', 'fontFace', 'outside', 'escape'],
  against: ['surface', 'accent', 'button', 'onButton'],
  problem: ['alias', 'aliasTaken', 'name', 'token', 'unknown', 'darkOnly', 'long', 'base', 'other'],
};

describe('the theme words', () => {
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
          ...source.matchAll(/'#jfbThemes_([A-Za-z_]+)'/g),
        ].map((match) => match[1] ?? ''),
      ),
    );
    for (const [family, parts] of Object.entries(FAMILIES)) for (const part of parts) named.add(`${family}_${part}`);
    // The keys the view chooses between: the scheme, the side shown, the preview's frame and what a check needs.
    for (const key of [
      'light',
      'dark',
      'showSettings',
      'showPreview',
      'previewLight',
      'previewDark',
      'checkFailText',
      'checkFailControl',
    ])
      named.add(key);
    // Only a kind that counts the uses in its forms says this.
    named.delete('userUses');
    expect(Object.keys(area(en)).sort()).toEqual([...named].sort());
  });

  it('say a German text that differs from the English where the words differ', () => {
    const same = Object.keys(area(en)).filter((key) => shown(area(en)[key]) === shown(area(de)[key]));
    // A column's name, the view's name, the text's colour, the sizes in pixels and a summary of a font and a size read
    // the same in both.
    expect(same.sort()).toEqual(['colour_text', 'columnName', 'cornersPx', 'ringPx', 'sizePx', 'summaryType', 'view']);
  });

  it('load with their screens, not with the start', () => {
    expect(Object.keys(startEn)).not.toContain('jfbThemes');
    expect(Object.keys(startDe)).not.toContain('jfbThemes');
  });
});
