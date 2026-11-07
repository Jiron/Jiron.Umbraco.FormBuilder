// The insights' words (ROADMAP 11.5, ADR-0211): English and German hold the same keys, each a text that is not blank;
// they hold every key the insights' code names, and the kinds of error the view words; and they load with the screens
// that use them, not with the backoffice's start.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import de from '../lang/insights.de';
import en from '../lang/insights.en';
import startEn from '../lang/en';
import { GROUPINGS, KINDS, PRESETS } from './insight-model';

const area = (dictionary: { jfbInsights: Record<string, unknown> }) => dictionary.jfbInsights;
const shown = (text: unknown): unknown => (typeof text === 'function' ? text('4', '5', '6') : text);
const here = dirname(fileURLToPath(import.meta.url));

describe('the insights words', () => {
  it('hold the same keys in English and German, each a text that is not blank', () => {
    expect(Object.keys(area(de)).sort()).toEqual(Object.keys(area(en)).sort());
    for (const dictionary of [en, de]) {
      for (const [key, text] of Object.entries(area(dictionary))) {
        expect(String(shown(text)).trim(), key).not.toBe('');
      }
    }
  });

  it("hold every key the insights' code names, and each range, grouping and kind of error", () => {
    const sources = [
      ...readdirSync(here).map((file) => join(here, file)),
      join(here, '..', 'designer', 'insights', 'insights.view.ts'),
      join(here, '..', 'settings', 'privacy-settings.element.ts'),
    ]
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
      .map((file) => readFileSync(file, 'utf8'));
    const named = new Set(
      sources.flatMap((source) => [...source.matchAll(/#term\('([A-Za-z_]+)'/g)].map((match) => match[1])),
    );
    for (const preset of PRESETS) named.add(`range_${preset}`);
    for (const grouping of GROUPINGS) {
      named.add(`group_${grouping}`);
      named.add(`entries_${grouping}`);
      named.add(`col_${grouping}`);
    }
    for (const kind of KINDS) named.add(`kind_${kind}`);
    const keys = new Set(Object.keys(area(en)));
    expect([...named].filter((key) => key !== undefined && !keys.has(key))).toEqual([]);
  });

  it('load with their screens, not with the start', () => {
    expect(Object.keys(startEn)).not.toContain('jfbInsights');
  });
});
