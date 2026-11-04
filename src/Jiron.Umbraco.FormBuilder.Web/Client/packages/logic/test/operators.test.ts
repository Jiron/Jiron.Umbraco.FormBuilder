// The operators table and the visit's kinds as the engine's check reads them (ROADMAP 9.5a, ADR-0005, ADR-0175): what
// the rules builder offers comes from here, so it can't offer what the check refuses.
import { describe, expect, it } from 'vitest';
import { contextKindOf, operatorsFor } from '../src/index.ts';

describe('the operators table', () => {
  it('lists the operators a kind takes, with what each compares with', () => {
    expect(operatorsFor('checkbox')).toEqual([
      { op: 'equals', operand: 'one' },
      { op: 'notEquals', operand: 'one' },
      { op: 'isOneOf', operand: 'list' },
      { op: 'isNoneOf', operand: 'list' },
      { op: 'isEmpty', operand: 'none' },
      { op: 'isNotEmpty', operand: 'none' },
      { op: 'countAtLeast', operand: 'count' },
      { op: 'countAtMost', operand: 'count' },
      { op: 'isChecked', operand: 'checked' },
      { op: 'isNotChecked', operand: 'checked' },
    ]);
    const text = operatorsFor('text').map((one) => one.op);
    expect(text).toEqual(expect.arrayContaining(['contains', 'notContains', 'startsWith', 'endsWith', 'matches']));
    expect(text).not.toContain('greaterThan');
    expect(operatorsFor('date').find((one) => one.op === 'between')).toEqual({ op: 'between', operand: 'pair' });
    expect(operatorsFor('choice').map((one) => one.op)).not.toContain('contains');
  });

  it('names the kind each part of the visit compares as, and none for anything else', () => {
    expect(['culture', 'memberSignedIn', 'memberGroup', 'today', 'query.ref', 'weather'].map(contextKindOf)).toEqual([
      'text',
      'checkbox',
      'choices',
      'date',
      'text',
      undefined,
    ]);
  });
});
