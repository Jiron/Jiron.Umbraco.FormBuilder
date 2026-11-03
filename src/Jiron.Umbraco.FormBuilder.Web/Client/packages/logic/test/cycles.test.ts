// Whether a form's elements wait on one another in a circle (ADR-0016, ADR-0061): the check the designer runs before a
// move, with the same edges the form engine orders by, so a move it allows never makes a form the engine refuses.
import { describe, expect, it } from 'vitest';
import { FORMULAS } from '../src/calc.ts';
import { evaluateForm, hasCycle, LogicError, type LogicField, type LogicForm, type Rule } from '../src/index.ts';

const showWhen = (field: string): Rule => ({
  effect: 'show',
  enabled: true,
  match: 'all',
  conditions: [{ field, op: 'isNotEmpty' }],
});

const text = (id: string, rules: Rule[] = []): LogicField => ({ id, kind: 'text', alias: id, rules });

describe('a cycle', () => {
  it('is a group shown by a field it holds, as the engine refuses it', () => {
    const form: LogicForm = {
      pages: [{ id: 'p', groups: [{ id: 'g', rules: [showWhen('a')], fields: [text('a')] }] }],
    };

    expect(hasCycle(form)).toBe(true);
    expect(() =>
      evaluateForm(
        form,
        {},
        {
          now: '2026-11-03T09:00:00Z',
          timeZone: 'UTC',
          culture: 'en',
          member: { signedIn: false, groups: [] },
          query: {},
          hidden: [],
        },
        null,
      ),
    ).toThrow(LogicError);
  });

  it('is a page shown by a field on it, and two fields shown by each other', () => {
    expect(hasCycle({ pages: [{ id: 'p', rules: [showWhen('a')], groups: [{ id: 'g', fields: [text('a')] }] }] })).toBe(
      true,
    );
    expect(
      hasCycle({
        pages: [{ id: 'p', groups: [{ id: 'g', fields: [text('a', [showWhen('b')]), text('b', [showWhen('a')])] }] }],
      }),
    ).toBe(true);
  });

  it('is no rule looking at a field elsewhere, nor a require rule, nor a disabled one', () => {
    const elsewhere: LogicForm = {
      pages: [
        { id: 'p1', groups: [{ id: 'g1', fields: [text('a')] }] },
        { id: 'p2', groups: [{ id: 'g2', rules: [showWhen('a')], fields: [text('b')] }] },
      ],
    };
    const requires: Rule = { ...showWhen('a'), effect: 'require' };
    const disabled: Rule = { ...showWhen('a'), enabled: false };

    expect(hasCycle(elsewhere)).toBe(false);
    expect(hasCycle({ pages: [{ id: 'p', groups: [{ id: 'g', fields: [text('a', [requires])] }] }] })).toBe(false);
    expect(hasCycle({ pages: [{ id: 'p', groups: [{ id: 'g', rules: [disabled], fields: [text('a')] }] }] })).toBe(
      false,
    );
  });

  it("runs through a formula's fields only with the calculation language, and a formula that does not read names none", () => {
    const total: LogicField = { id: 'total', kind: 'number', alias: 'total', calculation: { formula: 'count * 2' } };
    const count: LogicField = { id: 'count', kind: 'number', alias: 'count' };
    // The group shows by the total, which reads the count the group holds.
    const form: LogicForm = {
      pages: [
        { id: 'p1', groups: [{ id: 'g1', fields: [total] }] },
        { id: 'p2', groups: [{ id: 'g2', rules: [showWhen('total')], fields: [count] }] },
      ],
    };
    const unreadable: LogicForm = {
      pages: [
        { id: 'p1', groups: [{ id: 'g1', fields: [{ ...total, calculation: { formula: 'count *' } }] }] },
        { id: 'p2', groups: [{ id: 'g2', rules: [showWhen('total')], fields: [count] }] },
      ],
    };

    expect(hasCycle(form, FORMULAS)).toBe(true);
    expect(hasCycle(form)).toBe(false);
    expect(hasCycle(unreadable, FORMULAS)).toBe(false);
  });

  it('throws for a form that is none, as the engine does', () => {
    expect(() => hasCycle({ pages: [{ id: 'p', groups: [{ id: 'p' }] }] })).toThrow(LogicError);
  });
});
