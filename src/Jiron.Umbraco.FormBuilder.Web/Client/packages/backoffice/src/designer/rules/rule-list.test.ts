// The form's rules, element by element (ROADMAP 9.5c, ADR-0178): an element's own rules and the validation rules on a
// field or a grid's row, every page with the elements that have rules, the outcomes', and the chips' words.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition, DesignField, DesignRule } from '../definition';
import { chipsOf, ownOptionValues, plainText, rulesByPage, ruledOutcomes, rulesOn } from './rule-list';
import type { RuleWordsContext } from './rule-words';

const SHOW: DesignRule = { key: 's', effect: 'show', conditions: [{ field: 'topic', op: 'equals', value: 'press' }] };
const OFF: DesignRule = {
  key: 'o',
  effect: 'require',
  enabled: false,
  conditions: [{ field: 'topic', op: 'isNotEmpty' }],
};
const PAGE: DesignRule = { key: 'pr', effect: 'hide', conditions: [{ context: 'memberSignedIn', op: 'isChecked' }] };

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            {
              key: 'topic',
              type: 'dropdown',
              options: { items: [{ value: 'press' }, { value: '' }, { value: 'sales' }] },
            },
            { key: 'email', type: 'email', rules: [SHOW, OFF] },
            { key: 'grid', type: 'grid', rows: [{ key: 'r1' }], options: { items: [{ value: 'yes' }] } },
          ],
        },
      ],
    },
    { key: 'p2', rules: [PAGE], groups: [{ key: 'g2', fields: [{ key: 'notes', type: 'textarea' }] }] },
    { key: 'p3', groups: [{ key: 'g3', fields: [] }] },
  ],
  validationRules: [
    {
      key: 'v',
      field: 'email',
      message: { '': 'Use your work address' },
      conditions: [{ field: 'email', op: 'endsWith', value: 'gmail.com' }],
    },
    { key: 'w', field: 'notes' },
    { key: 'x', field: 'r1', conditions: [] },
  ],
  outcomes: [
    { key: 'a', kind: 'message', message: { '': '<p>Thanks, <strong>we&#39;ll</strong>\n call you &amp; write.</p>' } },
    {
      key: 'b',
      kind: 'redirect',
      redirect: { url: '/press/thanks' },
      rules: [{ key: 'u', effect: 'use', conditions: [] }],
    },
    { key: 'c', kind: 'message', message: { '': '<p>Hello</p>' }, rules: [{ key: 'u2', effect: 'use' }] },
  ],
};

const WORDS: RuleWordsContext = {
  term: (key, ...args) => `${key}(${args.join('|')})`,
  nameOf: (key) => key,
  kindOf: () => 'text',
  optionOf: (_, value) => value,
  languageOf: (culture) => culture,
  valueOf: (_, text) => text,
  textOf: (texts) => texts?.[''] ?? '',
};

describe("the form's rules, element by element", () => {
  it("are an element's own, then the validation rules that show their message on a field, each with where it is kept", () => {
    expect(rulesOn(DEFINITION, 'email')).toEqual([
      { holder: { kind: 'element', key: 'email' }, rule: SHOW },
      { holder: { kind: 'element', key: 'email' }, rule: OFF },
      {
        holder: { kind: 'validation', field: 'email' },
        rule: {
          key: 'v',
          effect: 'message',
          message: { '': 'Use your work address' },
          conditions: [{ field: 'email', op: 'endsWith', value: 'gmail.com' }],
        },
      },
    ]);
    // A validation rule kept without conditions reads with an empty list.
    expect(rulesOn(DEFINITION, 'notes')).toEqual([
      { holder: { kind: 'validation', field: 'notes' }, rule: { key: 'w', effect: 'message', conditions: [] } },
    ]);
    // A grid's row shows its message in the grid's place.
    expect(rulesOn(DEFINITION, 'grid')).toEqual([
      { holder: { kind: 'validation', field: 'r1' }, rule: { key: 'x', effect: 'message', conditions: [] } },
    ]);
    expect(rulesOn(DEFINITION, 'p2').map(({ rule }) => rule)).toEqual([PAGE]);
    expect(rulesOn(DEFINITION, 'gone')).toEqual([]);
  });

  it('are listed by page, the page first, then its groups and fields, a page without any left out', () => {
    const pages = rulesByPage(DEFINITION);
    expect(
      pages.map(({ index, elements }) => [index, elements.map(({ key, kind, rules }) => [key, kind, rules.length])]),
    ).toEqual([
      [
        0,
        [
          ['email', 'field', 3],
          ['grid', 'field', 1],
        ],
      ],
      [
        1,
        [
          ['p2', 'page', 1],
          ['notes', 'field', 1],
        ],
      ],
    ]);
  });

  it('list the outcomes that have rules, with what each does', () => {
    expect(ruledOutcomes(DEFINITION, '')).toEqual([
      {
        index: 1,
        kind: 'redirect',
        message: '',
        url: '/press/thanks',
        toPage: false,
        rules: [{ key: 'u', effect: 'use', conditions: [] }],
      },
      {
        index: 2,
        kind: 'message',
        message: 'Hello',
        url: '',
        toPage: false,
        rules: [{ key: 'u2', effect: 'use', conditions: [] }],
      },
    ]);
    expect(plainText('<p>Thanks, <strong>we&#39;ll</strong>\n call you &amp; write.</p>')).toBe(
      "Thanks, we'll call you & write.",
    );
  });

  it('give each element in use a chip per rule in words; a rule switched off has none', () => {
    expect([...chipsOf(DEFINITION, WORDS)]).toEqual([
      [
        'email',
        [
          expect.stringMatching(/^jfbRules_when_show\(/),
          expect.stringMatching(/^jfbRules_when_message\(Use your work address\|/),
        ],
      ],
      ['grid', [expect.stringMatching(/^jfbRules_when_messageStandard\(/)]],
      ['p2', [expect.stringMatching(/^jfbRules_when_hide\(/)]],
      ['notes', [expect.stringMatching(/^jfbRules_when_messageStandard\(/)]],
    ]);
  });

  it("know a choice's own options, a grid's for its rows, and none taken from elsewhere", () => {
    expect(ownOptionValues(DEFINITION, 'topic')).toEqual(['press', 'sales']);
    expect(ownOptionValues(DEFINITION, 'r1')).toEqual(['yes']);
    expect(ownOptionValues(DEFINITION, 'notes')).toEqual([]);
  });

  it('know none for a library list, a cascading list, or a choice whose "Other" takes any text', () => {
    const items = { items: [{ value: 'near' }, { value: 'far' }] };
    const choice = (key: string, extra: Partial<DesignField>): DesignField => ({
      key,
      type: 'dropdown',
      alias: key,
      options: items,
      ...extra,
    });
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                choice('own', {}),
                choice('listed', { options: { source: 'list', list: 'l1' } }),
                choice('cascading', { options: { ...items, parent: 'own' } }),
                choice('other', { settings: { other: true } }),
              ],
            },
          ],
        },
      ],
    };
    expect(['own', 'listed', 'cascading', 'other'].map((key) => ownOptionValues(definition, key))).toEqual([
      ['near', 'far'],
      [],
      [],
      [],
    ]);
  });
});
