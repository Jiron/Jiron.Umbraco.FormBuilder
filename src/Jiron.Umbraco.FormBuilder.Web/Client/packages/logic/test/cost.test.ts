// What the logic costs on long answers (ROADMAP 4.8f), the same as Core's LogicCostTests: numbers compare digit by
// digit as the values they are, without a BigInt being built, and a pattern's run keeps one set of buffers. A text
// condition folds each answer once and an evaluation compiles each pattern once (ROADMAP 4.8h). The shared vectors
// hold what the comparison answers and show that all of it still means what it meant.
import { describe, expect, it, vi } from 'vitest';
import { compilePattern, evaluateCondition, evaluateRule, type Condition, type Subject } from '../src/index.ts';
import * as patterns from '../src/pattern.ts';
import * as text from '../src/text.ts';

// The fold and the compiler, counted as they run.
vi.mock('../src/text.ts', async (original) => {
  const actual = await original<typeof import('../src/text.ts')>();
  return { ...actual, fold: vi.fn(actual.fold) };
});
vi.mock('../src/pattern.ts', async (original) => {
  const actual = await original<typeof import('../src/pattern.ts')>();
  return { ...actual, compilePattern: vi.fn(actual.compilePattern) };
});

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

describe('work done once', () => {
  const answer = 'A'.repeat(10_000);
  const others = Array.from({ length: 20 }, (_, i) => 'x' + String(i + 1));
  const subject: Subject = {
    fields: { name: 'text', others: 'text', phone: 'text' },
    values: { name: [answer], others, phone: ['ab'] },
    context,
  };
  const fold = vi.mocked(text.fold);
  const compile = vi.mocked(patterns.compilePattern);

  it.each([
    ['isOneOf', others],
    ['contains', { field: 'others' }],
  ] as const)('%s against 20 operands folds the answer once', (op, value) => {
    fold.mockClear();
    expect(evaluateCondition({ field: 'name', op, value }, subject)).toBe(false);
    expect(fold.mock.calls.filter(([folded]) => folded === answer)).toHaveLength(1);
    expect(fold).toHaveBeenCalledTimes(21);
  });

  it('an evaluation compiles each pattern once', () => {
    const condition: Condition = { field: 'phone', op: 'matches', value: '(ab|cd|ef){300}' };
    compile.mockClear();
    expect(evaluateCondition(condition, subject)).toBe(false);
    expect(compile).toHaveBeenCalledTimes(1);

    compile.mockClear();
    const rule = { effect: 'show', enabled: true, match: 'any', conditions: [condition, { ...condition }] } as const;
    expect(evaluateRule(rule, subject)).toBe(false);
    expect(compile).toHaveBeenCalledTimes(1);
  });
});
