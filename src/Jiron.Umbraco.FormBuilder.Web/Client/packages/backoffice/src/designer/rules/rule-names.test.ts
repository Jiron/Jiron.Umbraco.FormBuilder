// What rules are said with in the designer (ROADMAP 9.5a, ADR-0175): each element named as the canvas names it, a
// grid's rows by the grid and the row, dates in the backoffice's language, the field types' facts, a field's own
// options by their labels, and the inspector's words built from all of them.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition, DesignField } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import { elementNames, fieldName, inspectorRuleWords, ownOptionLabel, typeFactsOf, valueText } from './rule-names';

const term = (key: string, ...args: unknown[]) => [key, ...args].join(' ');

const field = (key: string, type: string, extra: Partial<DesignField> = {}): DesignField => ({ key, type, ...extra });

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      title: { '': 'About you', 'de-DE': '\u00dcber Sie' },
      groups: [
        {
          key: 'g1',
          title: { '': 'Contact' },
          fields: [
            field('country', 'dropdown', {
              alias: 'country',
              label: { '': 'Country' },
              options: { items: [{ value: 'de', label: { '': 'Germany' } }, { value: 'other' }] },
            }),
            field('intro', 'heading', { settings: { text: { '': 'Before you start' } } }),
            field('plain', 'text', { alias: 'nickname' }),
            field('nameless', 'text'),
          ],
        },
        { key: 'g2', fields: [] },
      ],
    },
    {
      key: 'p2',
      groups: [
        {
          key: 'g3',
          fields: [
            field('food', 'grid', {
              label: { '': 'Food' },
              rows: [{ key: 'r1', label: { '': 'Lunch' } }, { key: 'r2' }],
              options: { items: [{ value: 'good', label: { '': 'Good' } }] },
            }),
          ],
        },
      ],
    },
  ],
};

describe('the names rules are said with', () => {
  it('name a field by its label, a heading by its text, then its alias, else untitled', () => {
    const [country, intro, plain, nameless] = DEFINITION.pages[0]?.groups[0]?.fields ?? [];
    expect([country, intro, plain, nameless].map((one) => fieldName(one as DesignField, '', term))).toEqual([
      'Country',
      'Before you start',
      'nickname',
      'jfbDesigner_untitledField',
    ]);
  });

  it('name every page, group, field and grid row, a row by its grid and itself, in the language shown', () => {
    const names = elementNames(DEFINITION, 'de-DE', term);
    expect(names).toMatchObject({
      p1: '\u00dcber Sie',
      p2: 'jfbDesigner_pageNumber 2',
      g1: 'Contact',
      g2: 'jfbDesigner_groupNumber 2',
      country: 'Country',
      food: 'Food',
      r1: 'Food: Lunch',
      r2: 'Food: jfbDesigner_newRow 2',
    });
  });

  it('write a date, and a date and time, as the language writes them; any other value as it is', () => {
    expect(valueText('en-GB', 'date', '2026-12-01')).toBe('1 Dec 2026');
    expect(valueText('de-DE', 'date', '2026-12-01')).toBe('01.12.2026');
    expect(valueText('en-GB', 'dateTime', '2026-12-01T09:30')).toBe('1 Dec 2026, 09:30');
    expect(valueText('en-GB', 'date', 'soon')).toBe('soon');
    expect(valueText('en-GB', 'number', '2026-12-01')).toBe('2026-12-01');
  });

  it("read each field type's rule kind, features, multiple setting and whether its value is kept on the server", () => {
    const facts = typeFactsOf(builtInTypes);
    expect(facts['dropdown']).toMatchObject({ kind: 'choice', multipleSetting: true, serverOnlyDefault: false });
    expect(facts['dropdown']?.features).toEqual(builtInTypes['dropdown']?.features);
    expect([facts['checkboxes']?.multipleSetting, facts['hidden']?.serverOnlyDefault]).toEqual([false, true]);
    expect(facts['heading']?.kind).toBeNull();
  });

  it("say a field's own option, or a grid row's column, by its label, and any other value as it is", () => {
    expect(ownOptionLabel(DEFINITION, 'country', 'de', '')).toBe('Germany');
    expect(ownOptionLabel(DEFINITION, 'country', 'other', '')).toBe('other');
    expect(ownOptionLabel(DEFINITION, 'r1', 'good', '')).toBe('Good');
    expect(ownOptionLabel(DEFINITION, 'gone', 'de', '')).toBe('de');
  });

  it("give the inspector its words: names, kinds, options, languages by name, and dates in the user's language", () => {
    const words = inspectorRuleWords(DEFINITION, builtInTypes, 'en-GB', [{ culture: 'de-DE', name: 'German' }], term);
    expect(words.nameOf('r1')).toBe('Food: Lunch');
    expect(words.nameOf('gone')).toBe('gone');
    expect(words.kindOf('country')).toBe('choice');
    expect(words.optionOf('country', 'de')).toBe('Germany');
    expect([words.languageOf('de-DE'), words.languageOf('fr-FR')]).toEqual(['German', 'fr-FR']);
    expect(words.valueOf('date', '2026-12-01')).toBe('1 Dec 2026');
  });

  it("say a source's option by the label the server resolved, the field's own label first, and the value without one", () => {
    const resolved = new Map([
      [
        'gone',
        [
          { value: 'yes', label: 'Yes' },
          { value: 'notSure', label: null },
        ],
      ],
      ['country', [{ value: 'de', label: 'Deutschland' }]],
    ]);
    const words = inspectorRuleWords(DEFINITION, builtInTypes, 'en-GB', [], term, resolved);
    expect([words.optionOf('gone', 'yes'), words.optionOf('gone', 'notSure'), words.optionOf('gone', 'no')]).toEqual([
      'Yes',
      'notSure',
      'no',
    ]);
    expect(words.optionOf('country', 'de')).toBe('Germany');
  });
});
