// The Vitest vector runner (ROADMAP section 4.3.3, ADR-0005): one test case per vector of every tests/vectors/*.json
// file, read in place. The NUnit runner runs the same files against Core.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  calculate,
  checkField,
  clean,
  evaluateCondition,
  evaluateForm,
  evaluateRule,
  formatDecimal,
  LogicError,
  storedNumber,
  type Calculation,
  type CheckedField,
  type Condition,
  type Context,
  type FieldAnswers,
  type FormAnswers,
  type Kind,
  type LogicForm,
  type Rule,
  type Subject,
} from '../src/index.ts';
import { repositoryRoot, vectorsFolder } from '../tools/repository.ts';

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
  readonly field?: CheckedField;
  readonly answers?: FieldAnswers;
  readonly expect: unknown;
}

/** A built-in type as the reviewed snapshot of the field types endpoint serves it (docs/api/field-types.json). */
interface ServedType {
  readonly alias: string;
  readonly ruleKind: Kind | null;
  readonly settings: readonly { readonly name: string; readonly default: unknown; readonly isPublic: boolean }[];
}

const served: readonly ServedType[] = (
  JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'field-types.json'), 'utf8')) as { types: ServedType[] }
).types;

/**
 * Holds a field to check to what both runtimes see alike (ADR-0062): its kind is its type's, it gives every public
 * setting its type has a default for, as the public projection writes them, so no vector leans on a default, and its
 * answers are cleaned as the pipeline cleans them.
 */
function heldToItsType(id: string, field: CheckedField, answers: FieldAnswers): void {
  const type = served.find((each) => each.alias === field.type);
  if (type === undefined) throw new Error(`${id}: the site has no type '${field.type}'`);
  if ((field.kind ?? null) !== type.ruleKind)
    throw new Error(`${id}: a ${field.type} field's kind is ${type.ruleKind}`);
  const leaned = type.settings.filter(
    (setting) => setting.isPublic && setting.default !== null && !(setting.name in (field.settings ?? {})),
  );
  if (leaned.length > 0)
    throw new Error(`${id}: the field leans on the defaults of ${leaned.map((s) => s.name).join(', ')}`);
  const texts = [...answers.values, ...Object.values(answers.parts ?? {}).flat()];
  if (texts.some((text) => clean(text) !== text))
    throw new Error(`${id}: an answer is not cleaned as the pipeline cleans it`);
}

interface VectorFile {
  readonly format: string;
  readonly suite: string;
  readonly defaults: { readonly context: Context };
  readonly fields: Readonly<Record<string, Kind>>;
  readonly cases: readonly VectorCase[];
}

/** The things a case can run; each case has exactly one. */
const RUNNABLE = ['condition', 'rule', 'number', 'calc', 'form', 'field'] as const;

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
    if (vector.field !== undefined) {
      const answers = vector.answers ?? { values: [] };
      heldToItsType(vector.id, vector.field, answers);
      return checkField(vector.field, answers, subject.context);
    }
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
