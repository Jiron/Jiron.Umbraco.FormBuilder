// How a rule lands in the draft (ROADMAP 9.5a, ADR-0175): added, changed in place, moved to another element in one
// step, refused where an element would wait on itself, and taken off.
import { describe, expect, it } from 'vitest';
import type { DesignDefinition, DesignRule } from '../definition';
import { locate } from '../definition';
import type { EditorState, ModelContext } from '../editor-model';
import { builtInTypes } from '../preview/fixtures';
import { dropRule, landRule } from './rule-edits';

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
