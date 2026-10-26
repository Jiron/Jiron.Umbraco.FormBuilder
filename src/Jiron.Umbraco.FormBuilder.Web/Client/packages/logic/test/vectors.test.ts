// The Vitest vector runner (ROADMAP section 4.3.3, ADR-0005): one test case per vector of every tests/vectors/*.json
// file, read in place. The NUnit runner runs the same files against Core.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  calculate,
  evaluateCondition,
  evaluateForm,
  evaluateRule,
  formatDecimal,
  LogicError,
  storedNumber,
  type Calculation,
  type Condition,
  type Context,
  type FormAnswers,
  type Kind,
  type LogicForm,
  type Rule,
  type Subject,
} from '../src/index.ts';
import { vectorsFolder } from '../tools/repository.ts';

interface VectorCase {
  readonly id: string;
  readonly description?: string;
  readonly context?: Partial<Context>;
  readonly fields?: Readonly<Record<string, Kind>>;
  readonly values?: Readonly<Record<string, readonly string[]>>;
  readonly rows?: FormAnswers['rows'];
  readonly condition?: Condition;
  readonly rule?: Rule;
  readonly number?: string;
  readonly calc?: Calculation;
  readonly form?: LogicForm;
  readonly expect: unknown;
}

interface VectorFile {
  readonly format: string;
  readonly suite: string;
  readonly defaults: { readonly context: Context };
  readonly fields: Readonly<Record<string, Kind>>;
  readonly cases: readonly VectorCase[];
}

/** The things a case can run; each case has exactly one. */
const RUNNABLE = ['condition', 'rule', 'number', 'calc', 'form'] as const;

const folder = vectorsFolder();
const names = readdirSync(folder)
  .filter((name) => name.endsWith('.json'))
  .sort();

function run(file: VectorFile, vector: VectorCase): unknown {
  const subject: Subject = {
    fields: { ...file.fields, ...vector.fields },
    values: vector.values ?? {},
    context: { ...file.defaults.context, ...vector.context },
  };
  try {
    if (vector.condition !== undefined) return evaluateCondition(vector.condition, subject);
    if (vector.rule !== undefined) return evaluateRule(vector.rule, subject);
    if (vector.number !== undefined) return storedNumber(vector.number);
    if (vector.calc !== undefined) return formatDecimal(calculate(vector.calc));
    if (vector.form !== undefined) {
      const answers: FormAnswers = { values: vector.values ?? {}, rows: vector.rows ?? {} };
      return evaluateForm(vector.form, answers, subject.context);
    }
  } catch (error) {
    if (error instanceof LogicError) return { error: error.code };
    throw error;
  }
  throw new Error(`${vector.id}: nothing to run`);
}

describe('the vector files', () => {
  it('include the spike file of 1.5', () => {
    expect(names).toContain('spike.json');
  });
});

for (const name of names) {
  const file = JSON.parse(readFileSync(join(folder, name), 'utf8')) as VectorFile;

  describe(`vectors: ${name}`, () => {
    it('is a jfb-vectors/1 file named after its suite, with unique ids and one thing to run per case', () => {
      expect(file.format).toBe('jfb-vectors/1');
      expect(`${file.suite}.json`).toBe(name);
      const ids = file.cases.map((vector) => vector.id);
      expect(new Set(ids).size).toBe(ids.length);
      for (const vector of file.cases) {
        expect(
          RUNNABLE.filter((thing) => vector[thing] !== undefined),
          vector.id,
        ).toHaveLength(1);
      }
    });

    for (const vector of file.cases) {
      it(vector.id, () => {
        expect(run(file, vector)).toEqual(vector.expect);
      });
    }
  });
}
