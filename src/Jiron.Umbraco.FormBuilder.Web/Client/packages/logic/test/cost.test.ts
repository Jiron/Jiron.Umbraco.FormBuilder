// What the logic costs on long answers (ROADMAP 4.8f), the same as Core's LogicCostTests: numbers compare digit by
// digit as the values they are, without a BigInt being built, and a pattern's run keeps one set of buffers. The
// shared vectors show that both still mean what they meant.
import { describe, expect, it } from 'vitest';
import { compareText } from '../src/decimal.ts';
import { compilePattern, evaluateCondition, type Condition, type Subject } from '../src/index.ts';

const context = {
  now: '2026-10-25T10:00:00Z',
  timeZone: 'UTC',
  culture: 'en',
  member: { signedIn: false, groups: [] },
  query: {},
  hidden: [],
};

describe('long answers', () => {
  it('a rule compares a number answer of 10,000 digits in under 50 ms', () => {
    const answer = '7' + '3'.repeat(9_999);
    const larger = '7' + '3'.repeat(9_998) + '4';
    const others = Array.from({ length: 19 }, (_, i) => String(i + 1) + '0'.repeat(9_999));
    const subject: Subject = { fields: { amount: 'number' }, values: { amount: [answer] }, context };
    const conditions: Condition[] = [
      { field: 'amount', op: 'equals', value: answer + '.000' },
      { field: 'amount', op: 'isOneOf', value: [...others, answer] },
      { field: 'amount', op: 'lessThan', value: larger },
      { field: 'amount', op: 'between', value: ['0.5', larger] },
      { field: 'amount', op: 'greaterThan', value: '-' + larger },
    ];
    const evaluate = (): boolean[] => conditions.map((condition) => evaluateCondition(condition, subject));

    evaluate(); // the first run compiles what it runs
    const started = performance.now();
    const results = evaluate();
    const elapsed = performance.now() - started;

    expect(results).toEqual([true, true, true, true, true]);
    expect(elapsed).toBeLessThan(50);
  });

  it('a pattern matches a long answer in time linear in its length', () => {
    const pattern = compilePattern('(a|b|c)*[a-z]{1,3}');
    const short = 'a'.repeat(10_000);
    const long = 'a'.repeat(100_000);
    pattern.test(short);
    const fastest = (value: string): number => {
      let best = Number.POSITIVE_INFINITY;
      for (let run = 0; run < 5; run++) {
        const started = performance.now();
        expect(pattern.test(value)).toBe(true);
        best = Math.min(best, performance.now() - started);
      }
      return best;
    };

    // Ten times the answer, at most about ten times the work.
    expect(fastest(long)).toBeLessThan(20 * fastest(short) + 5);
  });
});

describe('numbers in the wire form', () => {
  it.each([
    ['0', '-0', 0],
    ['0.50', '0.5', 0],
    ['-0.000', '0', 0],
    ['10', '9', 1],
    ['-10', '-9', -1],
    ['1.25', '1.3', -1],
    ['12', '12.0001', -1],
    ['-0.5', '0', -1],
    ['-1.5', '-1.25', -1],
    ['123456789012345678901234567890', '123456789012345678901234567891', -1],
  ] as const)('%s against %s is %i', (a, b, expected) => {
    expect(compareText(a, b)).toBe(expected);
    expect(compareText(b, a)).toBe(expected === 0 ? 0 : -expected);
  });

  it.each(['1e3', '01', '1.', '.5', '-', '', '+1', '1,5', '1.5x'])('%j compares with nothing', (text) => {
    expect(compareText(text, '1')).toBeNull();
    expect(compareText('1', text)).toBeNull();
  });
});
