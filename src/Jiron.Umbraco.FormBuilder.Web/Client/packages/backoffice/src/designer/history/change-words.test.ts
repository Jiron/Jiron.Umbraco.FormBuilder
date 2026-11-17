// A comparison in words (ROADMAP 10.3a, ADR-0196): every property and enumeration value of schema v1 has a name in both
// languages; each kind of change reads as a sentence or a row, grouped into fields, settings, actions and rules; texts of
// another language, options, a type's own settings, nested settings and rules are named; and a row's summary says what
// changed in a few words.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { UmbLocalizationDictionary } from '@umbraco-cms/backoffice/localization-api';
import { describe, expect, it } from 'vitest';
import type { ActionTypeResponseModel, DefinitionChangeResponseModel } from '../../api';
import startDe from '../../lang/de';
import startEn from '../../lang/en';
import historyDe from '../../lang/history.de';
import historyEn from '../../lang/history.en';
import workspaceDe from '../../lang/workspace.de';
import workspaceEn from '../../lang/workspace.en';
import { flatten } from '../actions/action-words';
import type { DesignDefinition } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import {
  type ChangeWordsContext,
  comparisonWords,
  differenceWords,
  propertyName,
  publishWords,
  VALUE_TERMS,
  VOCABULARY,
} from './change-words';
import { summaryParts, summaryWords } from './history-model';

const ROOT = join(__dirname, '..', '..', '..', '..', '..', '..', '..', '..');
const SCHEMA = JSON.parse(readFileSync(join(ROOT, 'schemas', 'jfb-form.v1.schema.json'), 'utf8')) as {
  $defs: Record<string, { properties?: Record<string, unknown>; enum?: string[]; anyOf?: unknown[] }>;
};
const ACTION_TYPES = JSON.parse(
  readFileSync(join(ROOT, 'docs', 'api', 'action-types.json'), 'utf8'),
) as ActionTypeResponseModel[];

/** A language's words by full key; a key it lacks throws, so a test finds every word that is missing. */
function termIn(...dictionaries: UmbLocalizationDictionary[]) {
  const words: Record<string, unknown> = {};
  for (const dictionary of dictionaries) Object.assign(words, flatten('x', dictionary));
  return (key: string, ...args: unknown[]): string => {
    if (!(key in words)) throw new Error(`No word for ${key}`);
    const word = words[key];
    return typeof word === 'function' ? String((word as (...values: unknown[]) => unknown)(...args)) : String(word);
  };
}

const english = termIn(startEn, workspaceEn, historyEn);
const german = termIn(startDe, workspaceDe, historyDe);

const FIELDS = {
  name: '00000000-0000-4000-8000-000000000001',
  email: '00000000-0000-4000-8000-000000000002',
  topic: '00000000-0000-4000-8000-000000000003',
  fax: '00000000-0000-4000-8000-000000000004',
  order: '00000000-0000-4000-8000-000000000005',
};
const PAGE = '00000000-0000-4000-8000-0000000000a1';
const GROUP = '00000000-0000-4000-8000-0000000000b1';
const ACTION = '00000000-0000-4000-8000-0000000000c1';
const OUTCOME = '00000000-0000-4000-8000-0000000000d1';
const RULE = '00000000-0000-4000-8000-0000000000e1';
const CHECK = '00000000-0000-4000-8000-0000000000f1';

const topic = (labels: Record<string, string>) => ({
  key: FIELDS.topic,
  type: 'dropdown',
  alias: 'topic',
  label: { '': 'Topic' },
  options: {
    source: 'items',
    items: Object.entries(labels).map(([value, label]) => ({ value, label: { '': label } })),
  },
});

const older: DesignDefinition = {
  key: 'form',
  pages: [
    {
      key: PAGE,
      title: { '': 'Contact' },
      groups: [
        {
          key: GROUP,
          fields: [
            { key: FIELDS.name, type: 'text', alias: 'name', label: { '': 'Name' } },
            { key: FIELDS.email, type: 'email', alias: 'email', label: { '': 'Your email', de: 'E-Mail' } },
            topic({ sales: 'Sales', press: 'Press' }),
            { key: FIELDS.fax, type: 'text', alias: 'fax', label: { '': 'Fax' } },
          ],
        },
      ],
    },
  ],
};
const newer: DesignDefinition = {
  key: 'form',
  pages: [
    {
      key: PAGE,
      title: { '': 'Contact' },
      groups: [
        {
          key: GROUP,
          fields: [
            { key: FIELDS.email, type: 'email', alias: 'email', label: { '': 'Email address', de: 'E-Mail-Adresse' } },
            { key: FIELDS.name, type: 'text', alias: 'name', label: { '': 'Name' } },
            topic({ press: 'Press office', sales: 'Sales' }),
            {
              key: FIELDS.order,
              type: 'text',
              alias: 'order',
              label: { '': 'Order number' },
              rules: [
                {
                  key: RULE,
                  effect: 'require',
                  conditions: [{ field: FIELDS.topic, op: 'equals', value: 'sales' }],
                },
              ],
            },
          ],
        },
      ],
    },
  ],
  actions: [{ key: ACTION, type: 'email', name: 'Email the team', enabled: true, trigger: 'submitted' }],
  outcomes: [{ key: OUTCOME, kind: 'redirect', redirect: { url: '/press/thanks' } }],
  validationRules: [
    {
      key: CHECK,
      field: FIELDS.email,
      message: { '': 'Use your work address' },
      conditions: [{ field: FIELDS.email, op: 'endsWith', value: 'gmail.com' }],
    },
  ],
};

const context = (term = english, locale = 'en-GB'): ChangeWordsContext => ({
  term,
  say: (text) => (text?.startsWith('#') ? term(text.slice(1)) : (text ?? '')),
  culture: '',
  locale,
  languages: [
    { culture: 'en-US', name: 'English' },
    { culture: 'de', name: 'German' },
  ],
  fieldTypes: builtInTypes,
  actionTypes: Object.fromEntries(ACTION_TYPES.map((type) => [type.alias, type])),
  older,
  newer,
});

const change = (more: Partial<DefinitionChangeResponseModel>): DefinitionChangeResponseModel => ({
  kind: 'field',
  change: 'changed',
  moved: false,
  properties: [],
  ...more,
});

/** The comparison of the two definitions, as the server would send it. */
const CHANGES: DefinitionChangeResponseModel[] = [
  change({
    kind: 'settings',
    settingsGroup: 'submission',
    properties: [
      { path: ['store'], from: 'always', to: 'days' },
      { path: ['storeDays'], from: null, to: 30 },
      { path: ['verification'], from: null, to: { emailField: FIELDS.email, days: 7 } },
    ],
  }),
  change({
    key: FIELDS.email,
    label: 'Email address',
    alias: 'email',
    type: 'email',
    moved: true,
    properties: [
      { path: ['label', ''], from: 'Your email', to: 'Email address' },
      { path: ['label', 'de'], from: 'E-Mail', to: 'E-Mail-Adresse' },
      { path: ['required'], from: false, to: true },
      { path: ['messages', 'required', ''], from: null, to: 'We need your address.' },
    ],
  }),
  change({
    key: FIELDS.topic,
    label: 'Topic',
    alias: 'topic',
    type: 'dropdown',
    properties: [
      { path: ['options', 'items', 'press', 'label', ''], from: 'Press', to: 'Press office' },
      { path: ['options', 'items'], from: ['sales', 'press'], to: ['press', 'sales'] },
    ],
    before: topic({ sales: 'Sales', press: 'Press' }),
    after: topic({ press: 'Press office', sales: 'Sales' }),
  }),
  change({ key: FIELDS.order, change: 'added', label: 'Order number', alias: 'order', type: 'text' }),
  change({
    kind: 'rule',
    key: RULE,
    parentKey: FIELDS.order,
    change: 'added',
    after: { effect: 'require', conditions: [{ field: FIELDS.topic, op: 'equals', value: 'sales' }] },
  }),
  change({
    kind: 'validationRule',
    key: CHECK,
    change: 'added',
    after: {
      field: FIELDS.email,
      message: { '': 'Use your work address' },
      conditions: [{ field: FIELDS.email, op: 'endsWith', value: 'gmail.com' }],
    },
  }),
  change({ kind: 'action', key: ACTION, change: 'added', label: 'Email the team', type: 'email' }),
  change({
    kind: 'outcome',
    key: OUTCOME,
    change: 'added',
    after: { kind: 'redirect', redirect: { url: '/press/thanks' } },
  }),
  change({ key: FIELDS.fax, change: 'removed', label: 'Fax', alias: 'fax', type: 'text' }),
];

describe('a comparison in words', () => {
  it('names every property of schema v1, and every value of its enumerations, in English and German', () => {
    const elements = new Set(['key', 'rules', 'groups', 'fields']);
    for (const [object, properties] of Object.entries(VOCABULARY)) {
      const schema = SCHEMA.$defs[object]?.properties;
      expect(schema, object).toBeDefined();
      for (const property of Object.keys(schema ?? {}).filter((name) => !elements.has(name) || object === 'GridRow'))
        expect(properties[property], `${object}.${property}`).toBeDefined();
      for (const property of Object.values(properties)) {
        expect(english(property.term), property.term).not.toBe('');
        expect(german(property.term), property.term).not.toBe('');
      }
    }
    const sayRules = new Set(['RuleEffect', 'RuleMatch']);
    for (const [name, definition] of Object.entries(SCHEMA.$defs).filter(([, one]) => Array.isArray(one.enum))) {
      if (sayRules.has(name)) continue;
      for (const value of definition.enum ?? []) {
        const key = VALUE_TERMS[name]?.[value];
        expect(key, `${name}.${value}`).toBeDefined();
        expect(english(key ?? ''), `${name}.${value}`).not.toBe('');
        expect(german(key ?? ''), `${name}.${value}`).not.toBe('');
      }
    }
  });

  it('says fields added, removed and moved, then what changed of each, before After sending, the settings, actions and rules', () => {
    const parts = comparisonWords(CHANGES, context());
    // In the order the designer shows them: After sending is a part of its own (ADR-0256).
    expect(parts.map((part) => part.name)).toEqual(['fields', 'after', 'settings', 'actions', 'rules']);
    const [fields, after, settings, actions, rules] = parts;
    expect([english('jfbHistory_part_after'), german('jfbHistory_part_after')]).toEqual([
      'After sending',
      'Nach dem Senden',
    ]);
    expect(fields?.lines).toEqual(['Field moved: Email address', 'Field added: Order number', 'Field removed: Fax']);
    expect(fields?.rows).toEqual([
      { what: 'Email address, Label', older: 'Your email', newer: 'Email address' },
      { what: 'Email address, Label \u00b7 German', older: 'E-Mail', newer: 'E-Mail-Adresse' },
      { what: 'Email address, Required', older: 'No', newer: 'Yes' },
      { what: 'Email address, Message when no answer is given', older: 'none', newer: 'We need your address.' },
      { what: 'Topic, option "Press office", Label', older: 'Press', newer: 'Press office' },
      // Each side's options by the labels that side gave them.
      { what: 'Topic, order of the options', older: 'Sales, Press', newer: 'Press office, Sales' },
    ]);
    expect([after?.lines, after?.rows]).toEqual([['Outcome added: Redirect to /press/thanks'], []]);
    expect(settings?.lines).toEqual([]);
    expect(settings?.rows).toEqual([
      { what: 'Submission, Store entries', older: 'Always', newer: 'For a number of days' },
      { what: 'Submission, Days entries are kept', older: 'none', newer: '30' },
      {
        what: 'Submission, Email verification',
        older: 'none',
        newer: 'Address to confirm: Email address, Link valid for (days): 7',
      },
    ]);
    expect(actions?.lines).toEqual(['Action added: Email the team']);
    expect(rules?.lines).toEqual([
      'Rule added to Order number: Required when Topic is Sales',
      expect.stringMatching(/^Check added to Email address: .*Use your work address.*Email address/),
    ]);
  });

  it('names a nested setting, a text of another language, and a type\u2019s own setting by the label its type serves', () => {
    const name = (more: Partial<DefinitionChangeResponseModel>, path: string[]) =>
      propertyName(change(more), path, context());
    expect(name({ kind: 'settings', settingsGroup: 'submission' }, ['verification', 'days'])).toBe(
      'Email verification, Link valid for (days)',
    );
    expect(name({ kind: 'settings', settingsGroup: 'privacy' }, ['retentionDays', 'spam'])).toBe(
      'Days kept in each state, Spam (days)',
    );
    expect(name({ type: 'email' }, ['help', 'de'])).toBe('Help text \u00b7 German');
    expect(name({ kind: 'outcome' }, ['redirect', 'url'])).toBe('Redirect, Address');
    const textType = builtInTypes['text'];
    const maxLength = textType?.settings.find((one) => one.name === 'maxLength');
    expect(maxLength).toBeDefined();
    expect(name({ type: 'text' }, ['settings', 'maxLength'])).toBe(english(maxLength?.label.slice(1) ?? ''));
    // A setting no type serves keeps its own name.
    expect(name({ type: 'text' }, ['settings', 'site.custom'])).toBe('site.custom');
    const emailType = ACTION_TYPES.find((type) => type.alias === 'email');
    const first = emailType?.settings[0];
    expect(name({ kind: 'action', type: 'email' }, ['settings', first?.name ?? ''])).toBe(
      english(first?.label.slice(1) ?? ''),
    );
  });

  it('says values as the backoffice says them: yes and no, none, numbers, enumerations, rich text without markup, long texts cut', () => {
    const row = (path: string[], from: unknown, to: unknown, more: Partial<DefinitionChangeResponseModel> = {}) =>
      comparisonWords(
        [change({ key: FIELDS.name, label: 'Name', ...more, properties: [{ path, from, to }] })],
        context(),
      )[0]?.rows[0];
    // The canonical form writes every switch, number and choice: a side without one is without its object, and says none.
    expect(row(['sensitive'], false, true)).toMatchObject({ older: 'No', newer: 'Yes' });
    expect(row(['includeInEmails'], true, false)).toMatchObject({ older: 'Yes', newer: 'No' });
    expect(row(['hideLabel'], null, true)).toMatchObject({ older: 'none', newer: 'Yes' });
    expect(row(['retentionDays', 'spam'], null, 30, { kind: 'settings', settingsGroup: 'privacy', key: null })).toEqual(
      { what: 'Privacy, Days kept in each state, Spam (days)', older: 'none', newer: '30' },
    );
    expect(row(['span'], null, 1200)).toMatchObject({ older: 'none', newer: '1,200' });
    expect(row(['settings', 'maxBytes'], null, 10_485_760, { type: 'file' })).toMatchObject({ newer: '10 MB' });
    expect(row(['keep'], 'always', 'untilActionsRun')).toMatchObject({
      older: 'As long as the entry',
      newer: "Until the entry's actions have run",
    });
    expect(
      row(['closedMessage', ''], '<p>Closed &amp; <strong>full</strong>.</p>', null, {
        kind: 'settings',
        settingsGroup: 'availability',
      }),
    ).toMatchObject({ what: 'Availability, Message while closed', older: 'Closed & full.', newer: 'none' });
    // Two values that read alike say that they differ.
    expect(
      row(['closedMessage', ''], '<p>Closed.</p>', '<p><em>Closed.</em></p>', {
        kind: 'settings',
        settingsGroup: 'availability',
      }),
    ).toMatchObject({ older: 'Closed.', newer: 'Closed. (changed)' });
    const long = 'a'.repeat(400);
    expect(row(['help', ''], null, long)?.newer).toHaveLength(160);
    expect(
      row(['validation'], [], [{ kind: 'pattern', pattern: '^[0-9]+$' }, { kind: 'uniquePerForm' }]),
    ).toMatchObject({
      older: 'none',
      newer: 'Must match the pattern ^[0-9]+$; Each answer only once',
    });
  });

  it('says a list by what was added and removed, options by their labels, an outcome by its place, a page by its name', () => {
    const PAGE_KEY = '00000000-0000-4000-8000-0000000000a9';
    const outcome = (redirect: Record<string, unknown>) => ({ key: OUTCOME, kind: 'redirect', redirect });
    const words = (changes: DefinitionChangeResponseModel[], term = english) =>
      comparisonWords(changes, {
        ...context(term),
        older: { ...older, outcomes: [outcome({ url: '/press/thanks' })] },
        newer: { ...newer, outcomes: [outcome({ content: PAGE_KEY })] },
        pages: new Map([[PAGE_KEY, 'Thank you']]),
      });
    const cc = change({
      kind: 'action',
      key: ACTION,
      label: 'Email the team',
      type: 'email',
      properties: [
        { path: ['settings', 'cc'], from: ['desk@example.org'], to: ['desk@example.org', 'press@example.org'] },
        { path: ['settings', 'bcc'], from: ['old@example.org'], to: ['new@example.org'] },
      ],
    });
    const ccName = english('jfbActionSettings_emailCc');
    const bccName = english('jfbActionSettings_emailBcc');
    expect(words([cc])[0]).toEqual({
      name: 'actions',
      lines: [
        `Email the team, ${ccName}: added press@example.org`,
        `Email the team, ${bccName}: added new@example.org; removed old@example.org`,
      ],
      rows: [],
    });
    expect(words([cc], german)[0]?.lines[0]).toBe(
      `Email the team, ${german('jfbActionSettings_emailCc')}: press@example.org hinzugef${String.fromCharCode(0xfc)}gt`,
    );
    // Two options of one value can't be keyed, so the server sends the items themselves: said by their labels.
    const twins = change({
      key: FIELDS.topic,
      label: 'Topic',
      properties: [
        {
          path: ['options', 'items'],
          from: [{ value: 'a', label: { '': 'First' } }],
          to: [
            { value: 'a', label: { '': 'First' } },
            { value: 'a', label: { '': 'Second' } },
          ],
        },
      ],
    });
    expect(words([twins])[0]?.rows).toEqual([
      { what: 'Topic, order of the options', older: 'First', newer: 'First, Second' },
    ]);
    // An outcome's row, under After sending's own part, names it by its place; its page by the page's name.
    const moved = change({
      kind: 'outcome',
      key: OUTCOME,
      properties: [
        { path: ['redirect', 'url'], from: '/press/thanks', to: null },
        { path: ['redirect', 'content'], from: null, to: PAGE_KEY },
      ],
    });
    expect(words([moved]).map((part) => part.name)).toEqual(['after']);
    expect(words([moved])[0]?.rows).toEqual([
      { what: 'Outcome 1, Redirect, Address', older: '/press/thanks', newer: 'none' },
      { what: 'Outcome 1, Redirect, Page', older: 'none', newer: 'Thank you' },
    ]);
  });

  it('says a rule changed in both versions\u2019 words, and leaves out a former alias, which its alias says', () => {
    const parts = comparisonWords(
      [
        change({
          kind: 'rule',
          key: RULE,
          parentKey: FIELDS.order,
          properties: [{ path: ['conditions'], from: [], to: [] }],
          before: { effect: 'require', conditions: [{ field: FIELDS.topic, op: 'equals', value: 'press' }] },
          after: {
            effect: 'require',
            enabled: false,
            conditions: [{ field: FIELDS.topic, op: 'equals', value: 'sales' }],
          },
        }),
        change({
          key: FIELDS.name,
          label: 'Name',
          properties: [
            { path: ['alias'], from: 'fullName', to: 'name' },
            { path: ['formerAliases'], from: null, to: ['fullName'] },
          ],
        }),
      ],
      context(),
    );
    expect(parts.find((part) => part.name === 'rules')?.rows).toEqual([
      {
        what: 'Order number, rule',
        older: 'Required when Topic is Press',
        newer: 'Required when Topic is Sales (off)',
      },
    ]);
    // The former alias is neither a row nor a line: the alias's own row says it.
    expect(parts.find((part) => part.name === 'fields')).toEqual({
      name: 'fields',
      lines: [],
      rows: [{ what: 'Name, Alias', older: 'fullName', newer: 'name' }],
    });
  });

  it('reads in German with no word missing', () => {
    const parts = comparisonWords(CHANGES, context(german, 'de'));
    const said = parts.flatMap((part) => [
      ...part.lines,
      ...part.rows.flatMap((one) => [one.what, one.older, one.newer]),
    ]);
    expect(said).toContain('Feld hinzugef\u00fcgt: Order number');
    expect(said).toContain('Senden, Eintr\u00e4ge speichern');
    expect(said.join('\n')).not.toMatch(/jfb[A-Z]/);
  });

  it('says nothing for no changes', () => {
    expect(comparisonWords([], context())).toEqual([]);
  });

  it("says a row's changes in a few words: one by its name, more by their number", () => {
    const summary = (changes: Partial<DefinitionChangeResponseModel>[]) =>
      summaryWords(
        changes.map((one) => ({ kind: 'field', change: 'changed', moved: false, ...one })),
        english,
      );
    expect(summary([])).toBe('No changes');
    expect(summary([{ label: 'Email address' }])).toBe('Email address');
    expect(
      summary([
        { label: 'A' },
        { label: 'B' },
        { label: 'C' },
        { label: 'D' },
        { kind: 'outcome' },
        { kind: 'rule' },
        { kind: 'settings', settingsGroup: 'appearance' },
        { kind: 'action', label: 'Email the team' },
      ]),
    ).toBe('4 fields, Appearance, After sending, Email the team, 1 rule');
    expect(summary([{ kind: 'settings', settingsGroup: 'privacy' }, { kind: 'action' }, { kind: 'action' }])).toBe(
      'Privacy, 2 actions',
    );
  });

  it("joins a restore's changes as each language joins a list, After sending set off in German", () => {
    const changes: DefinitionChangeResponseModel[] = [
      { kind: 'field', key: 'a', change: 'changed', moved: false, label: 'A', properties: [] },
      { kind: 'field', key: 'b', change: 'changed', moved: false, label: 'B', properties: [] },
      { kind: 'outcome', key: 'o', change: 'changed', moved: false, properties: [] },
      { kind: 'rule', key: 'r', change: 'added', moved: false, properties: [] },
    ];
    const sentence = (term: typeof english) =>
      term('jfbHistory_restored', 'Version 13', term('jfbHistory_listOf', ...summaryParts(changes, term, true)));
    // No comma before "and", whichever English the backoffice speaks.
    expect(sentence(english)).toBe('Version 13 is the new draft: 2 fields, After sending and 1 rule changed.');
    expect(sentence(german)).toBe(
      'Version 13 ist der neue Entwurf: 2 Felder, \u201eNach dem Senden\u201c und 1 Regel ge\u00e4ndert.',
    );
    // A row's summary keeps the group's name as the Settings view shows it.
    expect(summaryParts(changes, german)).toContain('Nach dem Senden');
    expect(english('jfbHistory_listOf', 'A')).toBe('A');
    expect(english('jfbHistory_listOf', 'A', 'B')).toBe('A and B');
  });
});

describe('a difference of Review both versions in words (ROADMAP 10.3c, ADR-0200)', () => {
  const words = (changes: DefinitionChangeResponseModel[], term = english) => differenceWords(changes, context(term));
  const [settings, email, , order, rule, check, action, outcome, fax] = CHANGES as [
    DefinitionChangeResponseModel,
    ...DefinitionChangeResponseModel[],
  ];

  it("says what it is about, then each property's value on both sides, and its place where it moved", () => {
    expect(words([email as DefinitionChangeResponseModel])).toEqual({
      title: 'Field "Email address"',
      rows: [
        { what: 'Label', older: 'Your email', newer: 'Email address' },
        { what: 'Label \u00b7 German', older: 'E-Mail', newer: 'E-Mail-Adresse' },
        { what: 'Required', older: 'No', newer: 'Yes' },
        { what: 'Message when no answer is given', older: 'none', newer: 'We need your address.' },
        // A group without a title is named by its place.
        { what: 'Place', older: 'After "Name"', newer: 'First in "Group 1"' },
      ],
    });
    expect(words([settings]).title).toBe('Settings: Submission');
  });

  it('says a change of its place alone as the choice of its place', () => {
    const moved = { ...(email as DefinitionChangeResponseModel), properties: [] };
    expect(words([moved])).toEqual({
      title: 'Field "Email address": place',
      rows: [{ what: 'Place', older: 'After "Name"', newer: 'First in "Group 1"' }],
    });
    expect(words([moved], german).title).toBe('Feld \u201eEmail address\u201c: Platz');
  });

  it('names what an element added or removed holds, on the side that has it', () => {
    const group: DefinitionChangeResponseModel = {
      kind: 'group',
      key: 'gx',
      change: 'removed',
      moved: false,
      properties: [],
    };
    const said = words([group, fax as DefinitionChangeResponseModel, rule as DefinitionChangeResponseModel]);
    expect(said.rows).toEqual([
      { what: 'In the form', older: 'Yes', newer: 'No' },
      // A rule goes with its element, unnamed.
      { what: 'Holds', older: words([fax as DefinitionChangeResponseModel]).title, newer: 'none' },
    ]);
  });

  it('says whether an element added or removed is in the form, on both sides, whatever it holds', () => {
    expect(words([order as DefinitionChangeResponseModel, rule as DefinitionChangeResponseModel])).toEqual({
      title: 'Field "Order number"',
      rows: [{ what: 'In the form', older: 'No', newer: 'Yes' }],
    });
    expect(words([fax as DefinitionChangeResponseModel]).rows).toEqual([
      { what: 'In the form', older: 'Yes', newer: 'No' },
    ]);
    expect(words([action as DefinitionChangeResponseModel]).title).toBe('Action "Email the team"');
    expect(words([outcome as DefinitionChangeResponseModel]).title).toBe('After sending, Outcome 1');
  });

  it('says a rule and a check as the rules builder does, on each side', () => {
    expect(words([rule as DefinitionChangeResponseModel])).toEqual({
      title: 'Order number, rule',
      rows: [{ what: 'Rule', older: 'none', newer: 'Required when Topic is Sales' }],
    });
    const said = words([check as DefinitionChangeResponseModel]);
    expect([said.title, said.rows[0]?.older]).toEqual(['Check of Email address', 'none']);
  });

  it('says it in German too', () => {
    expect(words([email as DefinitionChangeResponseModel], german).title).toBe('Feld \u201eEmail address\u201c');
    expect(words([fax as DefinitionChangeResponseModel], german).rows).toEqual([
      { what: 'Im Formular', older: 'Ja', newer: 'Nein' },
    ]);
  });
});

describe('a comparison in a few words, as the publish dialog lists it (ROADMAP 10.3d)', () => {
  const said = (changes: DefinitionChangeResponseModel[], term = english) => publishWords(changes, context(term));
  const [settings, email, topic] = CHANGES as [DefinitionChangeResponseModel, ...DefinitionChangeResponseModel[]];

  it('groups the changes in their parts: what came, went or moved, then each changed element once with what changed', () => {
    const outcome = change({
      kind: 'outcome',
      key: OUTCOME,
      properties: [{ path: ['redirect', 'url'], from: '/press/thanks', to: '/thanks' }],
    });
    const parts = said([...CHANGES, outcome]);
    expect(parts.map((part) => part.name)).toEqual(['fields', 'after', 'settings', 'actions', 'rules']);
    const names = (change: DefinitionChangeResponseModel | undefined) =>
      english(
        'jfbHistory_listOf',
        ...[
          ...new Set(
            (change?.properties ?? []).map((property) =>
              propertyName(change as DefinitionChangeResponseModel, property.path, context(), false),
            ),
          ),
        ],
      );
    expect(parts[0]?.lines).toEqual([
      'Field moved: Email address',
      'Field added: Order number',
      'Field removed: Fax',
      `Email address: ${names(email)}`,
      `Topic: ${names(topic)}`,
    ]);
    // Its label changed in two languages, named once: the comparison says each.
    expect(parts[0]?.lines[3]).toBe('Email address: Label, Required and Message when no answer is given');
    // An outcome is After sending's own part, which names it by its place, not After sending again.
    expect(parts[1]?.lines).toEqual([
      expect.stringMatching(/^Outcome added: /) as unknown,
      'Outcome 1: Redirect, Address',
    ]);
    expect(parts[2]?.lines).toEqual([`Submission: ${names(settings)}`]);
    expect(parts[3]?.lines).toEqual(['Action added: Email the team']);
    expect(parts[4]?.lines.map((line) => line.slice(0, line.indexOf(':')))).toEqual([
      'Rule added to Order number',
      'Check added to Email address',
    ]);
  });

  it('says a rule or a check that changed by what it is on, and names a property once', () => {
    const rule = change({
      kind: 'rule',
      key: RULE,
      parentKey: FIELDS.order,
      before: { effect: 'require', conditions: [] },
      after: { effect: 'hide', conditions: [] },
      properties: [{ path: ['effect'], from: 'require', to: 'hide' }],
    });
    const check = change({
      kind: 'validationRule',
      key: CHECK,
      before: { field: FIELDS.email, conditions: [] },
      after: { field: FIELDS.email, conditions: [], message: { '': 'Work address' } },
      properties: [{ path: ['message', ''], from: null, to: 'Work address' }],
    });
    const twice = change({
      key: FIELDS.email,
      label: 'Email address',
      properties: [
        { path: ['required'], from: false, to: true },
        { path: ['required'], from: false, to: true },
        // A former alias comes with its alias, which says the change once.
        { path: ['formerAliases'], from: [], to: ['mail'] },
      ],
    });
    expect(said([rule, check, twice])).toEqual([
      { name: 'fields', lines: ['Email address: Required'] },
      { name: 'rules', lines: ['Rule changed on Order number', 'Check of Email address changed'] },
    ]);
  });

  it('names a few elements a line holds and counts the rest, so a line stays a line', () => {
    const many = ['A', 'B', 'C', 'D', 'E', 'F', 'G'].map((label) =>
      change({ key: label, parentKey: 'g1', change: 'added', label }),
    );
    expect(said(many)).toEqual([{ name: 'fields', lines: ['Fields added: A, B, C, D, E, 2 more'] }]);
    expect(said(many.slice(0, 5))[0]?.lines).toEqual(['Fields added: A, B, C, D, E']);
  });

  it('says it in German too, and nothing for no changes', () => {
    expect(said([email as DefinitionChangeResponseModel], german)[0]?.lines[1]).toMatch(
      /^Email address: Beschriftung, /,
    );
    expect(said([])).toEqual([]);
  });
});
