// What the designer adds (ROADMAP 9.3b): a new field of each kind with what it starts with, a group, a page, where a
// pick from the palette goes, several fields as one edit, and the questions a paste becomes with their guessed types.
import { describe, expect, it } from 'vitest';
import { allFields, locate, type DesignDefinition, type DesignField } from './definition';
import type { EditorState, FieldTraits, ModelContext } from './editor-model';
import {
  addFields,
  addGroup,
  aliasOf,
  guessType,
  MOST_PASTED,
  newField,
  newGroup,
  newPage,
  placeFor,
  questionsOf,
  type NewFieldType,
} from './new-elements';

const TRAITS: Record<string, FieldTraits> = {
  text: { kind: 'text', onePerForm: false, childFields: false },
  email: { kind: 'text', onePerForm: false, childFields: false },
  repeater: { kind: null, onePerForm: false, childFields: true },
  captcha: { kind: null, onePerForm: true, childFields: false },
};

function contextOf(): ModelContext {
  let next = 0;
  return { traitsOf: (type) => TRAITS[type], newKey: () => `k${++next}` };
}

const WORDS = { option: (n: number) => `Option ${n}`, row: (n: number) => `Row ${n}` };
const type = (alias: string, features: string[] = [], holdsValue = true): NewFieldType => ({
  alias,
  holdsValue,
  features,
});
const field = (key: string, kind = 'text'): DesignField => ({ key, type: kind, alias: key, label: { '': key } });

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'one',
      groups: [
        { key: 'g1', fields: [field('a'), field('b')] },
        { key: 'g2', fields: [field('c')] },
      ],
    },
    { key: 'two', groups: [{ key: 'g3', fields: [field('kids', 'repeater')] }] },
    { key: 'three', groups: [] },
  ],
};
const START: EditorState = { definition: DEFINITION, selection: null };

const keys = (state: EditorState, container: string) => {
  const found = locate(state.definition, container)?.element as unknown as {
    fields?: readonly { key: string }[];
    groups?: readonly { key: string }[];
  };
  return (found.fields ?? found.groups ?? []).map((each) => each.key);
};

describe('an alias from a label', () => {
  it('is its words in camel case, letters and digits only, accents dropped', () => {
    expect(aliasOf('Email address', 'email')).toBe('emailAddress');
    expect(aliasOf('Do you need step-free access?', 'yesno')).toBe('doYouNeedStepFreeAccess');
    expect(aliasOf('Stra\u00dfe und Hausnummer', 'text')).toBe('strasseUndHausnummer');
    expect(aliasOf('Pr\u00e9nom', 'text')).toBe('prenom');
  });

  it("is the type's alias when nothing usable is left, and never starts with a digit", () => {
    expect(aliasOf('???', 'text')).toBe('text');
    expect(aliasOf('', 'email')).toBe('email');
    expect(aliasOf('2 guests', 'number')).toBe('guests');
  });
});

describe('a new field', () => {
  let n = 0;
  const key = () => `key${++n}`;

  it('takes its label and an alias from it', () => {
    expect(newField(type('email'), 'Email address', key, WORDS)).toEqual({
      key: expect.any(String),
      type: 'email',
      alias: 'emailAddress',
      label: { '': 'Email address' },
    });
  });

  it('of a choice type starts with three options, a grid with two rows as well', () => {
    const dropdown = newField(type('dropdown', ['options', 'needsOptions']), 'Dropdown', key, WORDS);
    expect(dropdown['options']).toEqual({
      source: 'items',
      items: [
        { value: 'option1', label: { '': 'Option 1' } },
        { value: 'option2', label: { '': 'Option 2' } },
        { value: 'option3', label: { '': 'Option 3' } },
      ],
    });
    const grid = newField(type('grid', ['options', 'rows']), 'Grid', key, WORDS);
    expect(grid.rows?.map((row) => row.label)).toEqual([{ '': 'Row 1' }, { '': 'Row 2' }]);
    expect(new Set(grid.rows?.map((row) => row.key)).size).toBe(2);
  });

  it('of a repeatable group holds no field yet; a heading shows its label as its text; content has no label', () => {
    expect(newField(type('repeater', ['childFields']), 'Guests', key, WORDS).fields).toEqual([]);
    const heading = newField(type('heading', [], false), 'Heading', key, WORDS);
    expect(heading).toEqual({ key: expect.any(String), type: 'heading', settings: { text: { '': 'Heading' } } });
    expect(newField(type('divider', [], false), 'Divider', key, WORDS)).toEqual({
      key: expect.any(String),
      type: 'divider',
    });
  });
});

describe('a new group and page', () => {
  it('are empty, a page holding the empty group every page holds', () => {
    const context = contextOf();
    expect(newGroup(context.newKey)).toEqual({ key: 'k1', fields: [] });
    expect(newPage(context.newKey)).toEqual({ key: 'k2', groups: [{ key: 'k3', fields: [] }] });
  });
});

describe('where a pick from the palette goes', () => {
  it('is the open insert point first', () => {
    expect(placeFor(DEFINITION, { container: 'g1', index: 0 }, 'c', 1)).toEqual({ container: 'g1', index: 0 });
  });

  it('is after the selected field, at the end of the selected group, or at the end of the selected page', () => {
    expect(placeFor(DEFINITION, null, 'a', 0)).toEqual({ container: 'g1', index: 1 });
    expect(placeFor(DEFINITION, null, 'g1', 0)).toEqual({ container: 'g1', index: 2 });
    expect(placeFor(DEFINITION, null, 'one', 1)).toEqual({ container: 'g2', index: 1 });
  });

  it('is the end of the page shown without a selection, and nowhere on a page without a group', () => {
    expect(placeFor(DEFINITION, null, null, 0)).toEqual({ container: 'g2', index: 1 });
    expect(placeFor(DEFINITION, null, null, 2)).toBeNull();
  });
});

describe('adding fields', () => {
  it('puts them at the place in their order as one edit, and selects the last', () => {
    const added = addFields(START, [field('x'), field('y')], { container: 'g1', index: 1 }, 'one', contextOf());
    if (!added.ok) throw new Error(added.refusal);
    expect(keys(added.state, 'g1')).toEqual(['a', 'x', 'y', 'b']);
    expect(added.state.selection).toBe('y');
  });

  it('puts them in a new group at the end of a page without a place', () => {
    const added = addFields(START, [field('x')], null, 'three', contextOf());
    if (!added.ok) throw new Error(added.refusal);
    expect(keys(added.state, 'three')).toEqual(['k1']);
    expect(keys(added.state, 'k1')).toEqual(['x']);
  });

  it('makes their aliases unique', () => {
    const named = (key: string): DesignField => ({ ...field(key), alias: 'a' });
    const added = addFields(START, [named('x1'), named('x2')], { container: 'g2', index: 1 }, 'one', contextOf());
    if (!added.ok) throw new Error(added.refusal);
    expect(allFields(added.state.definition).map((each) => each.alias)).toEqual(['a', 'b', 'c', 'a2', 'a3', 'kids']);
  });

  it('refuses what the model refuses, and nothing to add', () => {
    const once = addFields(
      START,
      [field('x', 'captcha'), field('y', 'captcha')],
      { container: 'g1', index: 0 },
      'one',
      contextOf(),
    );
    expect(once.ok ? 'accepted' : once.refusal).toBe('onePerForm');
    const none = addFields(START, [], null, 'one', contextOf());
    expect(none.ok ? 'accepted' : none.refusal).toBe('unchanged');
  });
});

describe('adding a group', () => {
  it("goes after the place's group, after a repeatable group's group, or at the end of the page", () => {
    const after = addGroup(START, { container: 'g1', index: 1 }, 'one', contextOf());
    const repeater = addGroup(START, { container: 'kids', index: 0 }, 'two', contextOf());
    const end = addGroup(START, null, 'three', contextOf());
    if (!after.ok || !repeater.ok || !end.ok) throw new Error('refused');
    expect(keys(after.state, 'one')).toEqual(['g1', 'k1', 'g2']);
    expect(after.state.selection).toBe('k1');
    expect(keys(repeater.state, 'two')).toEqual(['g3', 'k1']);
    expect(keys(end.state, 'three')).toEqual(['k1']);
  });
});

describe('pasted questions', () => {
  const all = new Set([
    'text',
    'textarea',
    'email',
    'phone',
    'url',
    'date',
    'time',
    'number',
    'yesno',
    'consent',
    'file',
  ]);

  it('are one per line, without list markers, empty lines left out', () => {
    expect(questionsOf('1. Your name\n\n  - Email address  \r\n* Phone\n\u2022 Notes\n(no marker)', all)).toEqual([
      { label: 'Your name', type: 'text' },
      { label: 'Email address', type: 'email' },
      { label: 'Phone', type: 'phone' },
      { label: 'Notes', type: 'textarea' },
      { label: '(no marker)', type: 'text' },
    ]);
  });

  it(`are the first ${MOST_PASTED} lines at most, and a short text where the guessed type is not offered`, () => {
    const many = Array.from({ length: MOST_PASTED + 5 }, (_, at) => `Question ${at}`).join('\n');
    expect(questionsOf(many, all)).toHaveLength(MOST_PASTED);
    expect(questionsOf('Email address', new Set(['text']))).toEqual([{ label: 'Email address', type: 'text' }]);
  });

  it('get their type from their words, the more particular first, in English and German', () => {
    const guesses = {
      'Your name': 'text',
      'Email address': 'email',
      'E-Mail-Adresse': 'email',
      'Phone number': 'phone',
      Telefonnummer: 'phone',
      'Your website': 'url',
      'Date of birth': 'date',
      Geburtsdatum: 'date',
      'What time suits you?': 'time',
      'Postal address': 'address',
      Anschrift: 'address',
      Country: 'country',
      'How many guests?': 'number',
      'Wie viele Personen?': 'number',
      'Do you need step-free access?': 'yesno',
      'M\u00f6chten Sie den Newsletter?': 'yesno',
      'I agree to the terms': 'consent',
      'Upload your CV': 'file',
      'Your signature': 'signature',
      'How satisfied were you?': 'rating',
      'Anything else we should know?': 'textarea',
      'Ihre Nachricht': 'textarea',
      'Which workshop would you like to join?': 'text',
    };
    for (const [question, guessed] of Object.entries(guesses))
      expect([question, guessType(question)]).toEqual([question, guessed]);
  });
});
