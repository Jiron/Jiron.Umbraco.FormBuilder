// How a rule lands in the draft (ROADMAP 9.5a, 9.5c, ADR-0175, ADR-0178): added, changed in place, moved to another
// element in one step, refused where an element would wait on itself, and taken off; a validation rule among the
// form's own, and a rule moved between an element and them.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition, DesignRule } from '../definition';
import { locate } from '../definition';
import type { EditorState, ModelContext } from '../editor-model';
import { builtInTypes } from '../preview/fixtures';
import { asBuilderRule, asValidationRule, dropBuilt, dropRule, landBuilt, landRule } from './rule-edits';

const CONTEXT: ModelContext = {
  traitsOf: (type) => {
    const found = builtInTypes[type];
    return found
      ? {
          kind: found.ruleKind ?? null,
          onePerForm: found.onePerForm,
          childFields: found.features.includes('childFields'),
        }
      : undefined;
  },
  newKey: () => 'new',
};

const SHOW: DesignRule = { key: 's', effect: 'show', conditions: [{ field: 'country', op: 'equals', value: 'other' }] };

const STATE: EditorState = {
  selection: 'country',
  definition: {
    pages: [
      {
        key: 'p',
        groups: [
          {
            key: 'g',
            fields: [
              { key: 'country', type: 'dropdown' },
              { key: 'which', type: 'text', rules: [SHOW] },
            ],
          },
          { key: 'h', fields: [{ key: 'phone', type: 'text' }] },
        ],
      },
    ],
  },
};

const rulesOf = (definition: DesignDefinition, key: string) => locate(definition, key)?.element.rules ?? [];

describe('a rule landing', () => {
  it('joins its element at the end, or takes the place of the rule with its key, the selection kept', () => {
    const require: DesignRule = { key: 'r', effect: 'require', conditions: [{ field: 'country', op: 'isNotEmpty' }] };
    const added = landRule(STATE, null, 'which', require, CONTEXT);
    if (!added.ok) throw new Error(added.refusal);
    expect(rulesOf(added.state.definition, 'which')).toEqual([SHOW, require]);
    expect(added.state.selection).toBe('country');
    const changed = landRule(STATE, 'which', 'which', { ...SHOW, effect: 'hide' }, CONTEXT);
    if (!changed.ok) throw new Error(changed.refusal);
    expect(rulesOf(changed.state.definition, 'which')).toEqual([{ ...SHOW, effect: 'hide' }]);
  });

  it('leaves the element it was on and joins its new one in one step', () => {
    const moved = landRule(STATE, 'which', 'phone', SHOW, CONTEXT);
    if (!moved.ok) throw new Error(moved.refusal);
    expect(rulesOf(moved.state.definition, 'which')).toEqual([]);
    expect(rulesOf(moved.state.definition, 'phone')).toEqual([SHOW]);
    expect(moved.state.selection).toBe('country');
  });

  it('is refused where an element would wait on itself, and says nothing changed when nothing did', () => {
    // A group shown by a field it holds waits on itself.
    const circle = landRule(STATE, null, 'g', { ...SHOW, key: 'c' }, CONTEXT);
    expect(circle).toEqual({ ok: false, refusal: 'cycle' });
    expect(landRule(STATE, 'which', 'which', SHOW, CONTEXT)).toEqual({ ok: false, refusal: 'unchanged' });
    expect(landRule(STATE, 'which', 'gone', SHOW, CONTEXT)).toEqual({ ok: false, refusal: 'missing' });
  });

  it('takes a rule off its element', () => {
    const dropped = dropRule(STATE, 'which', 's', CONTEXT);
    if (!dropped.ok) throw new Error(dropped.refusal);
    expect(rulesOf(dropped.state.definition, 'which')).toEqual([]);
    expect(dropRule(STATE, 'which', 'gone', CONTEXT)).toEqual({ ok: false, refusal: 'unchanged' });
  });
});

describe('a validation rule landing (ADR-0178)', () => {
  const MESSAGE: DesignRule = {
    key: 'v',
    effect: 'message',
    enabled: true,
    message: { '': 'Use your work address' },
    conditions: [{ field: 'phone', op: 'endsWith', value: '0' }],
  };
  const validation = (definition: DesignDefinition) => definition['validationRules'];
  const ON_PHONE = { kind: 'validation', field: 'phone' } as const;

  it("joins the form's validation rules on its field, takes the place of its own key, and goes with the list's last", () => {
    const added = landBuilt(STATE, null, 'phone', MESSAGE, CONTEXT);
    if (!added.ok) throw new Error(added.refusal);
    expect(validation(added.state.definition)).toEqual([
      {
        key: 'v',
        enabled: true,
        message: { '': 'Use your work address' },
        conditions: [{ field: 'phone', op: 'endsWith', value: '0' }],
        field: 'phone',
      },
    ]);
    expect(added.state.selection).toBe('country');
    // Its own field may be looked at; nothing waits on a message.
    const changed = landBuilt(added.state, ON_PHONE, 'which', { ...MESSAGE, enabled: false }, CONTEXT);
    if (!changed.ok) throw new Error(changed.refusal);
    expect(validation(changed.state.definition)).toMatchObject([{ key: 'v', field: 'which', enabled: false }]);
    expect(landBuilt(added.state, ON_PHONE, 'phone', MESSAGE, CONTEXT)).toEqual({
      ok: false,
      refusal: 'unchanged',
    });
    const dropped = dropBuilt(added.state, ON_PHONE, 'v', CONTEXT);
    if (!dropped.ok) throw new Error(dropped.refusal);
    // The canonical form leaves an empty list out, so a draft that gets back to none has none.
    expect('validationRules' in dropped.state.definition).toBe(false);
  });

  it('moves between an element and the validation rules in one step, keeping everything else the rule held', () => {
    const toMessage = landBuilt(
      STATE,
      { kind: 'element', key: 'which' },
      'which',
      { ...SHOW, effect: 'message' },
      CONTEXT,
    );
    if (!toMessage.ok) throw new Error(toMessage.refusal);
    expect(rulesOf(toMessage.state.definition, 'which')).toEqual([]);
    expect(validation(toMessage.state.definition)).toEqual([{ key: 's', conditions: SHOW.conditions, field: 'which' }]);
    const back = landBuilt(
      toMessage.state,
      { kind: 'validation', field: 'which' },
      'phone',
      { ...SHOW, effect: 'hide' },
      CONTEXT,
    );
    if (!back.ok) throw new Error(back.refusal);
    expect('validationRules' in back.state.definition).toBe(false);
    expect(rulesOf(back.state.definition, 'phone')).toEqual([{ ...SHOW, effect: 'hide' }]);
  });

  it("keeps a grid row's validation rule on its row, which the canvas shows as the grid", () => {
    const grid: EditorState = {
      ...STATE,
      definition: {
        ...STATE.definition,
        pages: [
          {
            key: 'p',
            groups: [
              ...STATE.definition.pages.flatMap((page) => page.groups),
              { key: 'k', fields: [{ key: 'sizes', type: 'grid', rows: [{ key: 'size' }] }] },
            ],
          },
        ],
        validationRules: [{ key: 'v', field: 'size', conditions: [] }],
      },
    };
    const changed = landBuilt(grid, { kind: 'validation', field: 'size' }, 'sizes', MESSAGE, CONTEXT);
    if (!changed.ok) throw new Error(changed.refusal);
    expect(validation(changed.state.definition)).toMatchObject([{ key: 'v', field: 'size' }]);
    // Moved to another field, it shows its message there.
    const moved = landBuilt(grid, { kind: 'validation', field: 'size' }, 'phone', MESSAGE, CONTEXT);
    if (!moved.ok) throw new Error(moved.refusal);
    expect(validation(moved.state.definition)).toMatchObject([{ key: 'v', field: 'phone' }]);
  });

  it('reads a validation rule as the builder edits it, and writes it back as the definition keeps it', () => {
    const kept = { key: 'v', field: 'phone', note: 'kept', conditions: [] };
    const built = asBuilderRule(kept);
    expect(built).toEqual({ key: 'v', note: 'kept', effect: 'message', conditions: [] });
    expect(asValidationRule('phone', built)).toEqual(kept);
  });
});
