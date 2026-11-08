// A saved group (ROADMAP 12.1, ADR-0217): the palette lists the API's group templates by name, read as the model reads a
// definition (the canonical form leaves out empty lists), with the validation rules of the group's fields. One inserts
// as a copy with new keys, each field with a free alias, its rules naming the copy's own fields, its calculations and
// merge tags the new aliases, and its validation rules on the copy's fields, after the group of the place given, in one
// step; the form it came from is never touched. A save sends the validation rules on the group's fields, and no others.
import { FORMULAS } from '@jfb/logic/calc';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { TemplateResponseModel } from '../api';
import { must, testHost } from '../testing';
import {
  allFields,
  validationRulesOf,
  type DesignDefinition,
  type DesignGroup,
  type DesignValidationRule,
} from './definition';
import { freshCopy, freshCopyWith, type EditorState, type FieldTraits, type ModelContext } from './editor-model';
import { addGroup } from './new-elements';
import { saveGroup, savedGroupOf } from './saved-groups';

const sent = vi.hoisted(() => ({ bodies: [] as unknown[] }));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  templateSaveGroup: async (options: { body: unknown }) => {
    sent.bodies.push(options.body);
    return { data: { rulesLeftOut: 1, checksLeftOut: 0 } };
  },
}));

const TRAITS: Record<string, FieldTraits> = {
  email: { kind: 'text', onePerForm: false, childFields: false },
  number: { kind: 'number', onePerForm: false, childFields: false },
  calculation: { kind: 'number', onePerForm: false, childFields: false },
  paragraph: { kind: 'none', onePerForm: false, childFields: false } as FieldTraits,
};

function contextOf(): ModelContext {
  let next = 0;
  return { traitsOf: (type) => TRAITS[type], newKey: () => `new${++next}`, formulas: FORMULAS };
}

const saved: DesignGroup = {
  key: 'g1',
  title: { '': 'Order' },
  fields: [
    { key: 'f1', type: 'email', alias: 'email', label: { '': 'Email' } },
    { key: 'f2', type: 'number', alias: 'count', label: { '': 'How many' } },
    {
      key: 'f3',
      type: 'calculation',
      alias: 'total',
      label: { '': 'Total' },
      settings: { formula: 'count * 12' },
      rules: [
        { key: 'r1', effect: 'show', enabled: true, match: 'all', conditions: [{ field: 'f2', op: 'isNotEmpty' }] },
      ],
    },
  ],
};

const check: DesignValidationRule = {
  key: 'c1',
  field: 'f2',
  message: { '': 'Order more than {{ field.count }} for {{field.email|upper}}' },
  match: 'all',
  conditions: [{ field: 'f2', op: 'lessThan', value: '2' }],
};

const template = (definition: unknown, more: Partial<TemplateResponseModel> = {}): TemplateResponseModel => ({
  key: 't1',
  builtIn: false,
  alias: null,
  name: 'Order block',
  description: null,
  holds: { pages: 1, fields: 3, rules: 1, outcomes: 0, actions: 0 },
  definition: definition as TemplateResponseModel['definition'],
  kind: 'group',
  ...more,
});

afterEach(() => {
  sent.bodies = [];
  document.body.replaceChildren();
});

describe('a saved group', () => {
  it('is listed by its name with the group its one page holds and its validation rules; one without a group is left out', () => {
    expect(savedGroupOf(template({ pages: [{ key: 'p', groups: [saved] }], validationRules: [check] }))).toEqual([
      { key: 't1', name: 'Order block', group: saved, checks: [check] },
    ]);
    expect(savedGroupOf(template({ pages: [] }))).toEqual([]);
    expect(savedGroupOf(template(null))).toEqual([]);
  });

  it('reads as the model reads a definition: a group without fields and a rule without conditions get empty lists', () => {
    const canonical = {
      pages: [{ key: 'p', groups: [{ key: 'g', title: { '': 'Empty' }, rules: [{ key: 'r', effect: 'show' }] }] }],
    };
    const listed = must(savedGroupOf(template(canonical))[0], 'the saved group');
    expect(listed.group.fields).toEqual([]);
    expect(listed.group.rules?.[0]?.conditions).toEqual([]);
    expect(listed.checks).toEqual([]);
    // Its copy inserts, as one with fields does.
    const definition: DesignDefinition = { pages: [{ key: 'p1', groups: [] }] };
    const copy = freshCopy(definition, listed.group, contextOf());
    expect(addGroup({ definition, selection: null }, null, 'p1', contextOf(), copy).ok).toBe(true);
  });

  it('inserts as a copy with new keys, free aliases, its rules on its own fields and its calculation repointed', () => {
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p1',
          groups: [{ key: 'a', fields: [{ key: 'x', type: 'email', alias: 'email', label: { '': 'Your email' } }] }],
        },
      ],
    };
    const state: EditorState = { definition, selection: null };
    const context = contextOf();

    const copy = freshCopy(definition, saved, context);
    const edit = addGroup(state, { container: 'x', index: 1 }, 'p1', context, copy);

    expect(edit.ok).toBe(true);
    if (!edit.ok) return;
    const groups = edit.state.definition.pages[0]?.groups ?? [];
    expect(groups.map((group) => group.key)).toEqual(['a', copy.key]);
    expect(edit.state.selection).toBe(copy.key);
    const fields = groups[1]?.fields ?? [];
    expect(fields.map((field) => field.key)).toEqual(['new2', 'new3', 'new4']);
    expect(copy.key).toBe('new1');
    expect(fields.map((field) => field.alias)).toEqual(['email2', 'count', 'total']);
    expect(fields[2]?.rules?.[0]?.conditions).toEqual([{ field: 'new3', op: 'isNotEmpty' }]);
    expect(fields[2]?.rules?.[0]?.key).toBe('new5');
    expect(fields[2]?.settings).toEqual({ formula: 'count * 12' });
    // The saved group as the API gave it stays as it was, for the next insert.
    expect(saved.fields.map((field) => field.key)).toEqual(['f1', 'f2', 'f3']);
    expect(allFields(edit.state.definition).map((field) => field.alias)).toEqual(['email', 'email2', 'count', 'total']);
  });

  it('repoints a calculation at the new alias of a field whose own was taken', () => {
    const definition: DesignDefinition = {
      pages: [{ key: 'p1', groups: [{ key: 'a', fields: [{ key: 'y', type: 'number', alias: 'count' }] }] }],
    };
    const copy = freshCopy(definition, saved, contextOf());
    expect(copy.fields.map((field) => field.alias)).toEqual(['email', 'count2', 'total']);
    expect(copy.fields[2]?.settings).toEqual({ formula: 'count2 * 12' });
  });

  it("brings its validation rules on the copy's fields, and its merge tags name the new aliases, others as they were", () => {
    const paragraph = {
      key: 'f4',
      type: 'paragraph',
      settings: {
        text: { '': 'We write to {{ field.email }} about {{field.count.label}}; see {{ field.elsewhere }}.' },
      },
    };
    const group: DesignGroup = { ...saved, fields: [...saved.fields, paragraph] };
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p1',
          groups: [
            {
              key: 'a',
              fields: [
                { key: 'x', type: 'email', alias: 'email' },
                { key: 'y', type: 'number', alias: 'count' },
              ],
            },
          ],
        },
      ],
      validationRules: [{ key: 'v0', field: 'y', conditions: [] }],
    };
    const state: EditorState = { definition, selection: null };
    const context = contextOf();

    const copy = freshCopyWith(definition, group, [check], context);
    const edit = addGroup(state, null, 'p1', context, copy.element, copy.checks);

    expect(edit.ok).toBe(true);
    if (!edit.ok) return;
    const fields = copy.element.fields;
    expect(fields.map((field) => field.alias)).toEqual(['email2', 'count2', 'total', undefined]);
    expect(fields[3]?.settings).toEqual({
      text: { '': 'We write to {{ field.email2 }} about {{field.count2.label}}; see {{ field.elsewhere }}.' },
    });
    const rules = validationRulesOf(edit.state.definition);
    expect(rules.map((rule) => rule.key)).toEqual(['v0', copy.checks[0]?.key]);
    expect(rules[1]).toEqual({
      ...check,
      key: rules[1]?.key,
      field: fields[1]?.key,
      message: { '': 'Order more than {{ field.count2 }} for {{field.email2|upper}}' },
      conditions: [{ field: fields[1]?.key, op: 'lessThan', value: '2' }],
    });
    expect(rules[1]?.key).not.toBe('c1');
    expect(check.field, 'the saved rule stays as it was').toBe('f2');
  });

  it("is saved with the validation rules on its fields, and the form's other validation rules stay home", async () => {
    const definition: DesignDefinition = {
      pages: [{ key: 'p1', groups: [saved, { key: 'b', fields: [{ key: 'z', type: 'number', alias: 'other' }] }] }],
      validationRules: [check, { key: 'c2', field: 'z', conditions: [] }],
    };
    const result = await saveGroup(testHost(), definition, saved, 'Order block', null);
    expect(result).toEqual({ leftOut: { rules: 1, checks: 0 } });
    expect(sent.bodies).toEqual([{ name: 'Order block', description: null, group: saved, validationRules: [check] }]);
  });
});
