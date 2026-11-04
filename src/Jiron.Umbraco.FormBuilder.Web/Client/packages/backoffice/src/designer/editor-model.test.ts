// Every operation of the designer's editor model (ROADMAP 9.3a, ADR-0016): selection, change, insert, move, step,
// Move to..., duplicate and remove, the moves it refuses, and that it never changes the state it is given.
import type { LogicForm } from '@jfb/logic';
import { FORMULAS } from '@jfb/logic/calc';
import { describe, expect, it } from 'vitest';
import {
  locate,
  normalise,
  type DesignDefinition,
  type DesignField,
  type DesignGroup,
  type DesignRule,
} from './definition';
import {
  change,
  duplicate,
  insert,
  logicOf,
  move,
  moveToGroup,
  referencesTo,
  removeElement,
  select,
  splitPage,
  step,
  uniqueAlias,
  type Edit,
  type EditorState,
  type FieldTraits,
  type ModelContext,
} from './editor-model';
import { builtInTypes } from './preview/fixtures';
import { typeFactsOf } from './rules/rule-names';

const TRAITS: Record<string, FieldTraits> = {
  text: { kind: 'text', onePerForm: false, childFields: false },
  email: { kind: 'text', onePerForm: false, childFields: false },
  number: { kind: 'number', onePerForm: false, childFields: false },
  calculation: { kind: 'number', onePerForm: false, childFields: false },
  repeater: { kind: null, onePerForm: false, childFields: true },
  consent: { kind: 'boolean', onePerForm: true, childFields: false },
};

function contextOf(formulas = false): ModelContext {
  let next = 0;
  return { traitsOf: (type) => TRAITS[type], newKey: () => `new${++next}`, formulas: formulas ? FORMULAS : null };
}

const showWhen = (key: string, field: string): DesignRule => ({
  key,
  effect: 'show',
  enabled: true,
  match: 'all',
  conditions: [{ field, op: 'isNotEmpty' }],
});

const field = (key: string, type = 'text', extra: Partial<DesignField> = {}): DesignField => ({
  key,
  type,
  alias: key,
  label: { '': key },
  ...extra,
});

/**
 * Page "about": "contact" (name, email, phone) and "address" (street), which shows once email has an answer. Page
 * "extras": "family" (the repeatable group kids with kidName, then consent), "costs" (age, then the calculation total)
 * and "summary" (note), which shows once total has a value.
 */
const DEFINITION: DesignDefinition = deepFreeze({
  schemaVersion: 1,
  settings: { submission: { verification: { emailField: 'email' } } },
  pages: [
    {
      key: 'about',
      title: { '': 'About you' },
      groups: [
        { key: 'contact', fields: [field('name'), field('email', 'email'), field('phone')] },
        { key: 'address', rules: [showWhen('r1', 'email')], fields: [field('street')] },
      ],
    },
    {
      key: 'extras',
      groups: [
        {
          key: 'family',
          fields: [field('kids', 'repeater', { fields: [field('kidName')] }), field('consent', 'consent')],
        },
        {
          key: 'costs',
          fields: [field('age', 'number'), field('total', 'calculation', { settings: { formula: 'age * 2' } })],
        },
        { key: 'summary', rules: [showWhen('r2', 'total')], fields: [field('note')] },
      ],
    },
  ],
  validationRules: [{ key: 'v1', field: 'phone', conditions: [] }],
  actions: [{ key: 'a1', rules: [{ key: 'r3', effect: 'run', conditions: [{ field: 'name', op: 'isNotEmpty' }] }] }],
});

const START: EditorState = { definition: DEFINITION, selection: null };

function deepFreeze<T>(value: T): T {
  if (typeof value === 'object' && value !== null) {
    Object.values(value).forEach(deepFreeze);
    Object.freeze(value);
  }
  return value;
}

/** The state of an edit that must succeed. */
function ok(edit: Edit): EditorState {
  if (!edit.ok) throw new Error(`refused: ${edit.refusal}`);
  return edit.state;
}

const refusal = (edit: Edit) => (edit.ok ? 'accepted' : edit.refusal);

/** The keys of a container's elements, in order. */
function keysIn(state: EditorState, container: string | null): string[] {
  if (container === null) return state.definition.pages.map((page) => page.key);
  const found = locate(state.definition, container);
  if (!found) throw new Error(`no ${container}`);
  const element = found.element as unknown as { groups?: { key: string }[]; fields?: { key: string }[] };
  return (element.groups ?? element.fields ?? []).map((item) => item.key);
}

describe('selecting', () => {
  it('selects an element by its key, and nothing with null', () => {
    const selected = ok(select(START, 'email'));
    expect(selected.selection).toBe('email');
    expect(selected.definition).toBe(DEFINITION);
    expect(ok(select(selected, null)).selection).toBeNull();
  });

  it('refuses a key the definition has not', () => {
    expect(refusal(select(START, 'gone'))).toBe('missing');
  });
});

describe('changing an element', () => {
  it('replaces it where it is, keeping its key, and the selection', () => {
    const state = ok(select(START, 'phone'));
    const changed = ok(
      change<DesignField>(state, 'email', (one) => ({ ...one, key: 'other', label: { '': 'Email' } }), contextOf()),
    );

    expect(locate(changed.definition, 'email')?.element.label).toEqual({ '': 'Email' });
    expect(keysIn(changed, 'contact')).toEqual(['name', 'email', 'phone']);
    expect(changed.selection).toBe('phone');
    expect(refusal(change(START, 'gone', (one) => one, contextOf()))).toBe('missing');
  });
});

describe('a change that is checked', () => {
  it('refuses a change that changes nothing', () => {
    expect(refusal(change(START, 'name', (one) => ({ ...one }), contextOf()))).toBe('unchanged');
  });

  it('refuses an alias another field has or had, in any letter case, or a reserved one, and takes a free one', () => {
    const former = ok(
      change<DesignField>(START, 'note', (one) => ({ ...one, formerAliases: ['mobile'] }), contextOf()),
    );
    const aliasOf = (alias: string) => change<DesignField>(former, 'name', (one) => ({ ...one, alias }), contextOf());

    expect(refusal(aliasOf('Email'))).toBe('aliasTaken');
    expect(refusal(aliasOf('mobile'))).toBe('aliasTaken');
    expect(refusal(aliasOf('page'))).toBe('aliasTaken');
    expect(refusal(aliasOf('fullName'))).toBe('accepted');
  });

  it('refuses a second field of a type a form has once, and a type a repeatable group around it cannot hold', () => {
    const context = contextOf();
    expect(refusal(change<DesignField>(START, 'name', (one) => ({ ...one, type: 'consent' }), context))).toBe(
      'onePerForm',
    );
    expect(
      refusal(change<DesignField>(START, 'kidName', (one) => ({ ...one, type: 'repeater', fields: [] }), context)),
    ).toBe('notInRepeater');
  });

  it('refuses a rule that would make an element wait on itself', () => {
    const rule = showWhen('r9', 'name');
    expect(refusal(change<DesignGroup>(START, 'contact', (one) => ({ ...one, rules: [rule] }), contextOf()))).toBe(
      'cycle',
    );
  });
});

describe('a definition as the server writes it', () => {
  it('gets the empty lists its canonical form leaves out, and keeps everything else', () => {
    const canonical = {
      schemaVersion: 1,
      pages: [{ key: 'p', groups: [{ key: 'g', rules: [{ key: 'r', effect: 'show' }] }] }, { key: 'q' }],
    };
    expect(normalise(canonical)).toEqual({
      schemaVersion: 1,
      pages: [
        { key: 'p', groups: [{ key: 'g', rules: [{ key: 'r', effect: 'show', conditions: [] }], fields: [] }] },
        { key: 'q', groups: [] },
      ],
    });
    expect(normalise(null)).toEqual({ pages: [] });
  });

  it("is one the model moves in, a blank form's empty group too", () => {
    const blank: EditorState = {
      definition: normalise({ pages: [{ key: 'p', groups: [{ key: 'g' }] }] }),
      selection: null,
    };
    const state = ok(insert(blank, 'field', field('first'), { container: 'g', index: 0 }, contextOf()));
    expect(keysIn(state, 'g')).toEqual(['first']);
  });
});

describe("the form engine's model of a definition", () => {
  it("takes, with the rules model's facts, the kinds the builder offers and which fields without one can be required", () => {
    const facts = typeFactsOf(builtInTypes);
    const definition = normalise({
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                { key: 'plan', type: 'grid', settings: { multiple: true }, rows: [{ key: 'meals' }] },
                { key: 'where', type: 'address' },
                { key: 'topics', type: 'dropdown', settings: { multiple: true } },
              ],
            },
          ],
        },
      ],
    });
    const fields = (model: LogicForm) =>
      (model.pages[0]?.groups?.[0]?.fields ?? []).map(({ id, kind, canBeRequired, rows }) => ({
        id,
        kind,
        canBeRequired,
        rows,
      }));
    expect(fields(logicOf(definition, { factsOf: (type) => facts[type] }))).toEqual([
      { id: 'plan', kind: null, canBeRequired: true, rows: [{ id: 'meals', kind: 'choices' }] },
      { id: 'where', kind: null, canBeRequired: true, rows: [] },
      { id: 'topics', kind: 'choices', canBeRequired: true, rows: [] },
    ]);
    // The cycle check reads the types' traits, for which a row's kind changes nothing that waits.
    expect(fields(logicOf(definition, contextOf()))[0]).toEqual({
      id: 'plan',
      kind: null,
      canBeRequired: undefined,
      rows: [{ id: 'meals', kind: 'choice' }],
    });
  });
});

describe('inserting', () => {
  it('puts a field at its place and selects it, with a free alias if its own is taken', () => {
    const state = ok(
      insert(
        START,
        'field',
        field('mobile', 'text', { alias: 'phone' }),
        { container: 'contact', index: 1 },
        contextOf(),
      ),
    );

    expect(keysIn(state, 'contact')).toEqual(['name', 'mobile', 'email', 'phone']);
    expect(state.selection).toBe('mobile');
    expect(locate(state.definition, 'mobile')?.element).toMatchObject({ alias: 'phone2' });
  });

  it('puts a group on a page, a page among the pages, and a field in a repeatable group', () => {
    const group = ok(
      insert(START, 'group', { key: 'more', fields: [] }, { container: 'about', index: 9 }, contextOf()),
    );
    const page = ok(insert(START, 'page', { key: 'last', groups: [] }, { container: null, index: 1 }, contextOf()));
    const child = ok(insert(START, 'field', field('kidAge', 'number'), { container: 'kids', index: 0 }, contextOf()));

    expect(keysIn(group, 'about')).toEqual(['contact', 'address', 'more']);
    expect(keysIn(page, null)).toEqual(['about', 'last', 'extras']);
    expect(keysIn(child, 'kids')).toEqual(['kidAge', 'kidName']);
  });

  it('gives the fields of an inserted group free aliases, and refuses one that brings a type the form has once', () => {
    const group = {
      key: 'saved',
      fields: [field('a', 'text', { alias: 'name' }), field('b', 'text', { alias: 'name2' })],
    };
    const state = ok(insert(START, 'group', group, { container: 'about', index: 0 }, contextOf()));
    const withConsent = { key: 'more', fields: [field('again', 'consent')] };

    expect(locate(state.definition, 'saved')?.element).toMatchObject({
      fields: [{ alias: 'name2' }, { alias: 'name3' }],
    });
    expect(refusal(insert(START, 'group', withConsent, { container: 'about', index: 0 }, contextOf()))).toBe(
      'onePerForm',
    );
  });

  it('refuses a place that cannot hold the element', () => {
    const context = contextOf();
    expect(refusal(insert(START, 'field', field('x'), { container: 'about', index: 0 }, context))).toBe('wrongPlace');
    expect(refusal(insert(START, 'field', field('x'), { container: 'name', index: 0 }, context))).toBe('wrongPlace');
    expect(refusal(insert(START, 'group', { key: 'g', fields: [] }, { container: 'contact', index: 0 }, context))).toBe(
      'wrongPlace',
    );
    expect(refusal(insert(START, 'page', { key: 'p', groups: [] }, { container: 'about', index: 0 }, context))).toBe(
      'wrongPlace',
    );
    expect(refusal(insert(START, 'field', field('x'), { container: 'nowhere', index: 0 }, context))).toBe('missing');
  });

  it('refuses a second field of a type a form has once, and what a repeatable group cannot hold', () => {
    const context = contextOf();
    expect(
      refusal(insert(START, 'field', field('again', 'consent'), { container: 'contact', index: 0 }, context)),
    ).toBe('onePerForm');
    expect(
      refusal(
        insert(START, 'field', field('inner', 'repeater', { fields: [] }), { container: 'kids', index: 0 }, context),
      ),
    ).toBe('notInRepeater');
  });

  it("throws for a key the definition has already, which is the caller's mistake", () => {
    expect(() => insert(START, 'field', field('email'), { container: 'contact', index: 0 }, contextOf())).toThrow();
  });
});

describe('moving', () => {
  it('moves a field within its group and into another, selects it, and leaves the given state as it was', () => {
    const within = ok(move(START, 'phone', { container: 'contact', index: 0 }, contextOf()));
    const across = ok(move(START, 'name', { container: 'address', index: 99 }, contextOf()));

    expect(keysIn(within, 'contact')).toEqual(['phone', 'name', 'email']);
    expect(within.selection).toBe('phone');
    expect(keysIn(across, 'contact')).toEqual(['email', 'phone']);
    expect(keysIn(across, 'address')).toEqual(['street', 'name']);
    expect(keysIn(START, 'contact')).toEqual(['name', 'email', 'phone']);
  });

  it('moves groups between pages and pages among themselves', () => {
    const group = ok(move(START, 'summary', { container: 'about', index: 0 }, contextOf()));
    const page = ok(move(START, 'extras', { container: null, index: 0 }, contextOf()));

    expect(keysIn(group, 'about')).toEqual(['summary', 'contact', 'address']);
    expect(keysIn(page, null)).toEqual(['extras', 'about']);
  });

  it('refuses a move to where the element is, into itself, or into the wrong kind of container', () => {
    const context = contextOf();
    expect(refusal(move(START, 'email', { container: 'contact', index: 1 }, context))).toBe('unchanged');
    expect(refusal(move(START, 'kids', { container: 'kids', index: 0 }, context))).toBe('wrongPlace');
    expect(refusal(move(START, 'about', { container: 'contact', index: 0 }, context))).toBe('wrongPlace');
    expect(refusal(move(START, 'contact', { container: 'address', index: 0 }, context))).toBe('wrongPlace');
    expect(refusal(move(START, 'gone', { container: 'contact', index: 0 }, context))).toBe('missing');
  });

  it('refuses to put the field a group shows by inside that group, which would wait on itself', () => {
    expect(refusal(move(START, 'email', { container: 'address', index: 0 }, contextOf()))).toBe('cycle');
    // Move to... refuses it the same way.
    expect(refusal(moveToGroup(START, 'email', 'address', contextOf()))).toBe('cycle');
  });

  it("refuses a move that closes a circle through a formula's fields, given the calculation language", () => {
    expect(refusal(move(START, 'age', { container: 'summary', index: 0 }, contextOf(true)))).toBe('cycle');
    expect(refusal(move(START, 'age', { container: 'summary', index: 0 }, contextOf(false)))).toBe('accepted');
  });

  it('refuses what a repeatable group cannot hold: the field the email check reads, or a type a form has once', () => {
    expect(refusal(move(START, 'email', { container: 'kids', index: 0 }, contextOf()))).toBe('notInRepeater');
    expect(refusal(move(START, 'consent', { container: 'kids', index: 0 }, contextOf()))).toBe('notInRepeater');
    expect(refusal(move(START, 'phone', { container: 'kids', index: 1 }, contextOf()))).toBe('accepted');
  });
});

describe('stepping with Alt+Up and Alt+Down', () => {
  const context = contextOf();

  it('moves one place among its siblings', () => {
    expect(keysIn(ok(step(START, 'name', 1, context)), 'contact')).toEqual(['email', 'name', 'phone']);
    expect(keysIn(ok(step(START, 'phone', -1, context)), 'contact')).toEqual(['name', 'phone', 'email']);
  });

  it("crosses into the neighbouring group at a group's end, across pages too", () => {
    const down = ok(step(START, 'phone', 1, context));
    const nextPage = ok(step(START, 'street', 1, context));
    const up = ok(step(START, 'kids', -1, context));

    expect(keysIn(down, 'address')).toEqual(['phone', 'street']);
    expect(keysIn(nextPage, 'family')).toEqual(['street', 'kids', 'consent']);
    expect(keysIn(up, 'address')).toEqual(['street', 'kids']);
    expect(up.selection).toBe('kids');
  });

  it('crosses a group into the neighbouring page, and keeps a page among the pages', () => {
    expect(keysIn(ok(step(START, 'family', -1, context)), 'about')).toEqual(['contact', 'address', 'family']);
    expect(keysIn(ok(step(START, 'address', 1, context)), 'extras')).toEqual(['address', 'family', 'costs', 'summary']);
    expect(keysIn(ok(step(START, 'about', 1, context)), null)).toEqual(['extras', 'about']);
  });

  it("refuses at the very ends, and keeps a repeatable group's child among its siblings", () => {
    expect(refusal(step(START, 'name', -1, context))).toBe('edge');
    expect(refusal(step(START, 'note', 1, context))).toBe('edge');
    expect(refusal(step(START, 'contact', -1, context))).toBe('edge');
    expect(refusal(step(START, 'extras', 1, context))).toBe('edge');
    expect(refusal(step(START, 'kidName', 1, context))).toBe('edge');
  });

  it('refuses a step that would make an element wait on itself', () => {
    const emailLast = ok(move(START, 'email', { container: 'contact', index: 2 }, context));
    expect(refusal(step(emailLast, 'email', 1, context))).toBe('cycle');
  });
});

describe('Move to...', () => {
  it("puts a field at the end of the chosen group, and refuses what isn't a group", () => {
    expect(keysIn(ok(moveToGroup(START, 'name', 'costs', contextOf())), 'costs')).toEqual(['age', 'total', 'name']);
    expect(refusal(moveToGroup(START, 'name', 'about', contextOf()))).toBe('missing');
  });
});

describe('duplicating', () => {
  it('puts a copy of a field just after it, with a fresh key and a free alias, and selects it', () => {
    const state = ok(duplicate(START, 'email', contextOf()));

    expect(keysIn(state, 'contact')).toEqual(['name', 'email', 'new1', 'phone']);
    expect(state.selection).toBe('new1');
    expect(locate(state.definition, 'new1')?.element).toMatchObject({
      type: 'email',
      alias: 'email2',
      label: { '': 'email' },
    });
  });

  it("gives everything in a copied group fresh keys, points the copy's references at the copies, and keeps the others", () => {
    const sibling = ok(
      change<DesignField>(
        START,
        'phone',
        (one) => ({
          ...one,
          rules: [showWhen('r9', 'name')],
          formerAliases: ['tel'],
        }),
        contextOf(),
      ),
    );
    const state = ok(duplicate(sibling, 'address', contextOf()));
    const copied = ok(duplicate(state, 'contact', contextOf()));
    const copy = copied.definition.pages[0]?.groups[1];

    expect(keysIn(state, 'about')).toEqual(['contact', 'address', 'new1']);
    // The address copy still shows by the original email, which it did not copy.
    expect(locate(state.definition, 'new1')?.element.rules?.[0]).toMatchObject({
      key: 'new2',
      conditions: [{ field: 'email' }],
    });
    expect(copy?.fields.map((one) => one.alias)).toEqual(['name2', 'email2', 'phone2']);
    // The phone copy shows by the name copy, and leaves the original's former alias behind.
    const phone = copy?.fields[2];
    expect(phone?.rules?.[0]?.conditions).toEqual([{ field: copy?.fields[0]?.key, op: 'isNotEmpty' }]);
    expect(phone).not.toHaveProperty('formerAliases');
    expect(new Set(copy?.fields.map((one) => one.key))).not.toContain('name');
  });

  it("points a copied calculation's formula at the copies of the fields it names, given the calculation language", () => {
    const copied = ok(duplicate(START, 'costs', contextOf(true)));
    const copy = copied.definition.pages[1]?.groups[2];
    expect(copy?.fields.map((one) => one.alias)).toEqual(['age2', 'total2']);
    expect(copy?.fields[1]?.['settings']).toEqual({ formula: 'age2 * 2' });
    // Without the language the formula stays as it was, which the form check then reports.
    const unread = ok(duplicate(START, 'costs', contextOf(false)));
    expect(unread.definition.pages[1]?.groups[2]?.fields[1]?.['settings']).toEqual({ formula: 'age * 2' });
  });

  it('refuses a copy of a type a form has once, or of anything holding one', () => {
    expect(refusal(duplicate(START, 'consent', contextOf()))).toBe('onePerForm');
    expect(refusal(duplicate(START, 'family', contextOf()))).toBe('onePerForm');
    expect(refusal(duplicate(START, 'gone', contextOf()))).toBe('missing');
  });
});

describe('removing', () => {
  it('selects the element that took its place, else the one before, else its container', () => {
    const middle = ok(removeElement(START, 'email'));
    const last = ok(removeElement(START, 'phone'));
    const only = ok(removeElement(START, 'street'));
    const page = ok(removeElement(START, 'about'));

    expect(keysIn(middle, 'contact')).toEqual(['name', 'phone']);
    expect(middle.selection).toBe('phone');
    expect(last.selection).toBe('email');
    expect(only.selection).toBe('address');
    expect(keysIn(page, null)).toEqual(['extras']);
    expect(page.selection).toBe('extras');
  });

  it('removes what an element holds with it, and keeps the last page', () => {
    const state = ok(removeElement(START, 'kids'));
    expect(locate(state.definition, 'kidName')).toBeUndefined();
    const onePage = ok(removeElement(START, 'extras'));
    expect(refusal(removeElement(onePage, 'about'))).toBe('lastPage');
  });

  it('removes the validation rules that show their message on the element or in it, and keeps those that look at it', () => {
    // v1 shows its message on phone; the list left empty goes.
    expect(ok(removeElement(START, 'phone')).definition).not.toHaveProperty('validationRules');
    expect(ok(removeElement(START, 'contact')).definition).not.toHaveProperty('validationRules');
    const looking = { key: 'v2', field: 'street', conditions: [{ field: 'phone', op: 'isEmpty' }] };
    const row = { key: 'v3', field: 'size', conditions: [{ field: 'name', op: 'isEmpty' }] };
    const grid = field('sizes', 'grid', { rows: [{ key: 'size', label: { '': 'Size' } }] });
    const definition: DesignDefinition = {
      ...DEFINITION,
      // A grid joins the first page in a group of its own.
      pages: DEFINITION.pages.map((page, at) =>
        at === 0 ? { ...page, groups: [...page.groups, { key: 'more', fields: [grid] }] } : page,
      ),
      validationRules: [...listed(DEFINITION['validationRules']), looking, row],
    };
    const state: EditorState = { definition, selection: null };
    expect(ok(removeElement(state, 'phone')).definition['validationRules']).toEqual([looking, row]);
    expect(ok(removeElement(state, 'sizes')).definition['validationRules']).toEqual([
      ...listed(DEFINITION['validationRules']),
      looking,
    ]);
  });
});

const listed = (value: unknown): unknown[] => (Array.isArray(value) ? [...(value as unknown[])] : []);

describe('references', () => {
  it("count an action's recipient field and a list of attachments, wherever the action keeps them", () => {
    const definition = {
      ...DEFINITION,
      actions: [{ key: 'mail', settings: { toField: 'phone', attachments: ['street', 'name'] } }],
    };
    expect(referencesTo(definition, 'phone')).toEqual(['mail']);
    expect(referencesTo(definition, 'street')).toEqual(['mail']);
  });

  it('lists the elements, validation rules, actions and settings outside an element that name it or what it holds', () => {
    // The settings' email check reads the email field; v1 shows its message on phone, so it goes with contact.
    expect(referencesTo(DEFINITION, 'email')).toEqual(['address', 'settings']);
    expect(referencesTo(DEFINITION, 'contact')).toEqual(['address', 'a1', 'settings']);
    expect(referencesTo(DEFINITION, 'street')).toEqual([]);
    const looking = { key: 'v2', field: 'street', conditions: [{ field: 'phone', op: 'isEmpty' }] };
    expect(referencesTo({ ...DEFINITION, validationRules: [looking] }, 'phone')).toEqual(['v2']);
  });

  it("counts a cascading list's parent and a field check's other field", () => {
    const state = ok(
      change<DesignField>(
        START,
        'street',
        (one) => ({
          ...one,
          options: { parent: 'phone' },
          validation: [{ kind: 'matchesField', field: 'name' }],
        }),
        contextOf(),
      ),
    );
    expect(referencesTo(state.definition, 'phone')).toEqual(['street']);
    expect(referencesTo(state.definition, 'name')).toEqual(['street', 'a1']);
  });
});

describe('a free alias', () => {
  it('is the base while it is free, else the base with the lowest free number', () => {
    expect(uniqueAlias(DEFINITION, 'city')).toBe('city');
    expect(uniqueAlias(DEFINITION, 'email')).toBe('email2');
    expect(uniqueAlias(DEFINITION, 'EMAIL')).toBe('field');
    expect(uniqueAlias(DEFINITION, 'email', new Set(['email2']))).toBe('email3');
  });

  it("counts letter case, former aliases and reserved names, and starts again from a number's stem", () => {
    const state = ok(
      change<DesignField>(START, 'note', (one) => ({ ...one, alias: 'Phone2', formerAliases: ['city'] }), contextOf()),
    );
    expect(uniqueAlias(state.definition, 'phone2')).toBe('phone3');
    expect(uniqueAlias(state.definition, 'city')).toBe('city2');
    expect(uniqueAlias(DEFINITION, 'page')).toBe('page2');
    expect(uniqueAlias(DEFINITION, '2fast')).toBe('field');
  });
});

describe('starting a new page at a place', () => {
  it("moves a group's fields from the place on, and the groups after it, onto a new page after it", () => {
    const split = ok(splitPage(START, { container: 'contact', index: 1 }, contextOf()));
    const added = split.selection ?? '';
    expect(keysIn(split, null)).toEqual(['about', added, 'extras']);
    expect(keysIn(split, 'about')).toEqual(['contact']);
    expect(keysIn(split, 'contact')).toEqual(['name']);
    const [moved, rest] = keysIn(split, added);
    expect(rest).toBe('address');
    expect(keysIn(split, moved ?? '')).toEqual(['email', 'phone']);
    // The moved fields' group keeps the look of the group they came from, not its title or rules.
    expect(locate(split.definition, moved ?? '')?.element).toEqual({ key: 'new1', fields: expect.any(Array) });
  });

  it('moves a whole group at its start, and gives a page left without a group an empty one', () => {
    const split = ok(splitPage(START, { container: 'contact', index: 0 }, contextOf()));
    const added = split.selection ?? '';
    expect(keysIn(split, added)).toEqual(['contact', 'address']);
    const [kept] = keysIn(split, 'about');
    expect(kept).not.toBe('contact');
    expect(keysIn(split, kept ?? '')).toEqual([]);
  });

  it('splits after a repeatable group, and moves the groups of a page from a place on the page', () => {
    const afterKids = ok(splitPage(START, { container: 'kids', index: 1 }, contextOf()));
    expect(keysIn(afterKids, 'family')).toEqual(['kids']);
    expect(keysIn(afterKids, keysIn(afterKids, afterKids.selection ?? '')[0] ?? '')).toEqual(['consent']);

    const byGroups = ok(splitPage(START, { container: 'extras', index: 2 }, contextOf()));
    expect(keysIn(byGroups, 'extras')).toEqual(['family', 'costs']);
    expect(keysIn(byGroups, byGroups.selection ?? '')).toEqual(['summary']);
  });

  it("gives a new page at the end of a page an empty group, and keeps a group's columns on the moved part", () => {
    const atEnd = ok(splitPage(START, { container: 'summary', index: 1 }, contextOf()));
    expect(keysIn(atEnd, 'extras')).toEqual(['family', 'costs', 'summary']);
    const [only, ...more] = keysIn(atEnd, atEnd.selection ?? '');
    expect([keysIn(atEnd, only ?? ''), more]).toEqual([[], []]);

    const columns: EditorState = {
      definition: {
        pages: [
          {
            key: 'p',
            groups: [{ key: 'g', columns: 2, style: 'card', title: { '': 'T' }, fields: [field('a'), field('b')] }],
          },
        ],
      },
      selection: null,
    };
    const split = ok(splitPage(columns, { container: 'g', index: 1 }, contextOf()));
    const moved = keysIn(split, split.selection ?? '')[0] ?? '';
    expect(locate(split.definition, moved)?.element).toEqual({
      key: 'new1',
      columns: 2,
      style: 'card',
      fields: [field('b')],
    });
  });

  it('refuses a place that is no container', () => {
    expect(refusal(splitPage(START, { container: null, index: 0 }, contextOf()))).toBe('wrongPlace');
    expect(refusal(splitPage(START, { container: 'gone', index: 0 }, contextOf()))).toBe('missing');
  });

  it('leaves the state it is given as it was', () => {
    ok(splitPage(START, { container: 'contact', index: 1 }, contextOf()));
    expect(keysIn(START, 'contact')).toEqual(['name', 'email', 'phone']);
  });
});
