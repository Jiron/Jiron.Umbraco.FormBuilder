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
  evaluateFormula,
  evaluateRule,
  formatDecimal,
  LogicError,
  parseTemplate,
  recallAnswers,
  renderTemplate,
  storedNumber,
  type Calculation,
  type CheckedField,
  type Condition,
  type Context,
  type CultureFormats,
  type DisplayWords,
  type FieldAnswers,
  type FormAnswers,
  type FormulaScope,
  type Kind,
  type LogicForm,
  type MergeValue,
  type RecallAnswers,
  type RecallField,
  type RecallForm,
  type Rule,
  type Subject,
  type TemplateContext,
} from '../src/index.ts';
import { today } from '../src/dates.ts';
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
  readonly formula?: string;
  readonly form?: LogicForm;
  readonly field?: CheckedField;
  readonly answers?: FieldAnswers;
  readonly template?: VectorTemplate;
  readonly recall?: VectorRecall;
  readonly expect: unknown;
}

/** A template rendered in its context over sources given as data: each known path and its value (ADR-0064). */
interface VectorTemplate {
  readonly text: string;
  readonly context: TemplateContext;
  readonly sources: Readonly<Record<string, MergeValue>>;
}

/**
 * A visitor text rendered with answer recall (ADR-0068): a form and a set of answers of the file's defaults by name,
 * the page, group or field whose text it is, the fields the rules hid, the text and its context.
 */
interface VectorRecall {
  readonly form: string;
  readonly answers: string;
  readonly owner: string | null;
  readonly hidden: readonly string[];
  readonly text: string;
  readonly context: TemplateContext;
}

/** A field of a recall form as the vectors write it: settings as the definition stores them, the server's own extras. */
interface VectorRecallField extends Omit<RecallField, 'fields'> {
  readonly fields?: readonly VectorRecallField[];
  readonly formerAliases?: readonly string[];
}

interface VectorRecallForm {
  readonly pages: readonly {
    readonly id: string;
    readonly groups?: readonly { readonly id: string; readonly fields?: readonly VectorRecallField[] }[];
  }[];
}

/** A built-in type as the reviewed snapshot of the field types endpoint serves it (docs/api/field-types.json). */
interface ServedType {
  readonly alias: string;
  readonly ruleKind: Kind | null;
  readonly storage: string;
  readonly features: readonly string[];
  readonly settings: readonly ServedSetting[];
}

interface ServedSetting {
  readonly name: string;
  readonly default: unknown;
  readonly isPublic?: boolean;
  readonly settings: readonly ServedSetting[];
}

const served: readonly ServedType[] = (
  JSON.parse(readFileSync(join(repositoryRoot(), 'docs', 'api', 'field-types.json'), 'utf8')) as { types: ServedType[] }
).types;

/** The settings with a default that a field does not give, and those of an object it gives, by their paths. */
function leaned(
  settings: readonly ServedSetting[],
  given: Readonly<Record<string, unknown>>,
  prefix: string,
): string[] {
  return settings.flatMap((setting) => {
    if (!Object.hasOwn(given, setting.name)) return setting.default === null ? [] : [prefix + setting.name];
    const value = given[setting.name];
    return typeof value === 'object' && value !== null && !Array.isArray(value)
      ? leaned(setting.settings, value as Readonly<Record<string, unknown>>, `${prefix}${setting.name}.`)
      : [];
  });
}

/**
 * Holds a field to check to what both runtimes see alike (ADR-0062): its kind is its type's, it gives every public
 * setting its type has a default for, an object's settings each in turn, as the public projection writes them, so no
 * vector leans on a default, it has options and rows only when its type has them, a field that stores files has one
 * file for each value, and its answers are cleaned as the pipeline cleans them.
 */
function heldToItsType(id: string, field: CheckedField, answers: FieldAnswers): void {
  const type = served.find((each) => each.alias === field.type);
  if (type === undefined) throw new Error(`${id}: the site has no type '${field.type}'`);
  if ((field.kind ?? null) !== type.ruleKind)
    throw new Error(`${id}: a ${field.type} field's kind is ${type.ruleKind}`);
  const missing = leaned(
    type.settings.filter((setting) => setting.isPublic === true),
    field.settings ?? {},
    '',
  );
  if (missing.length > 0) throw new Error(`${id}: the field leans on the defaults of ${missing.join(', ')}`);
  if (
    (field.options !== undefined && !type.features.includes('options')) ||
    (field.rows !== undefined && !type.features.includes('rows'))
  )
    throw new Error(`${id}: a ${field.type} field has no options or rows of its own`);
  if ((answers.files ?? []).length !== (type.storage === 'file' ? answers.values.length : 0))
    throw new Error(`${id}: a field that stores files has one file for each value, and no other field has files`);
  const texts = [...answers.values, ...Object.values(answers.parts ?? {}).flat()];
  if (texts.some((text) => clean(text) !== text))
    throw new Error(`${id}: an answer is not cleaned as the pipeline cleans it`);
}

interface VectorFile {
  readonly format: string;
  readonly suite: string;
  readonly defaults: {
    readonly context: Context;
    readonly cultures?: Readonly<Record<string, CultureFormats>>;
    readonly words?: Readonly<Record<string, DisplayWords>>;
    readonly forms?: Readonly<Record<string, VectorRecallForm>>;
    readonly answers?: Readonly<Record<string, RecallAnswers>>;
  };
  readonly fields: Readonly<Record<string, Kind>>;
  readonly cases: readonly VectorCase[];
}

/** The things a case can run; each case has exactly one. */
const RUNNABLE = ['condition', 'rule', 'number', 'calc', 'formula', 'form', 'field', 'template', 'recall'] as const;

const folder = vectorsFolder();
const names = readdirSync(folder)
  .filter((name) => name.endsWith('.json'))
  .sort();

/** A subject as a formula reads it: the fields' kinds and answers by alias, a hidden field without answers. */
function formulaScope(subject: Subject): FormulaScope {
  const own = <T>(record: Readonly<Record<string, T>>, key: string): T | undefined =>
    Object.hasOwn(record, key) ? record[key] : undefined;
  return {
    kind: (alias) => own(subject.fields, alias),
    answers: (alias) => (subject.context.hidden.includes(alias) ? undefined : own(subject.values, alias)),
    today: () => today(subject.context.now, subject.context.timeZone),
  };
}

/**
 * A setting as the projection gives it to the browser: a text of the definition's (an object with the default culture)
 * as its text in the page's culture, and an object's settings each in turn.
 */
function resolved(value: unknown): unknown {
  if (typeof value !== 'object' || value === null) return value;
  if (Array.isArray(value)) return value.map(resolved);
  const record = value as Readonly<Record<string, unknown>>;
  if (Object.hasOwn(record, '')) return record[''];
  return Object.fromEntries(Object.entries(record).map(([name, inner]) => [name, resolved(inner)]));
}

/** A recall form's field as the browser's runtime reads it: its texts in the culture, without the server's extras. */
function recallField(field: VectorRecallField): RecallField {
  const { fields, settings, ...rest } = field;
  const own: Record<string, unknown> = { ...rest };
  delete own.formerAliases;
  return {
    ...(own as Omit<RecallField, 'fields' | 'settings'>),
    ...(settings === undefined ? {} : { settings: resolved(settings) as Readonly<Record<string, unknown>> }),
    ...(fields === undefined ? {} : { fields: fields.map(recallField) }),
  };
}

function recallForm(form: VectorRecallForm): RecallForm {
  return {
    pages: form.pages.map((page) => ({
      id: page.id,
      groups: (page.groups ?? []).map((group) => ({ id: group.id, fields: (group.fields ?? []).map(recallField) })),
    })),
  };
}

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
    if (vector.calc !== undefined) {
      const result = calculate(vector.calc, formulaScope(subject));
      return result === null ? null : formatDecimal(result);
    }
    if (vector.formula !== undefined) return evaluateFormula(vector.formula, formulaScope(subject));
    if (vector.field !== undefined) {
      const answers = vector.answers ?? { values: [] };
      heldToItsType(vector.id, vector.field, answers);
      return checkField(vector.field, answers, subject.context, vector.values ?? {});
    }
    if (vector.form !== undefined) {
      const answers: FormAnswers = { values: vector.values ?? {}, rows: vector.rows ?? {} };
      return evaluateForm(vector.form, answers, subject.context);
    }
    if (vector.template !== undefined) {
      const { text, context, sources } = vector.template;
      const culture = file.defaults.cultures?.[subject.context.culture];
      if (culture === undefined)
        throw new Error(`${vector.id}: the file has no formats for ${subject.context.culture}`);
      const parsed = parseTemplate(text, context);
      return renderTemplate(parsed, (path) => (Object.hasOwn(sources, path) ? sources[path] : undefined), culture);
    }
    if (vector.recall !== undefined) {
      const { form, answers, owner, hidden, text, context } = vector.recall;
      const name = subject.context.culture;
      const culture = file.defaults.cultures?.[name];
      const words = file.defaults.words?.[name];
      const read = file.defaults.forms?.[form];
      const given = file.defaults.answers?.[answers];
      if (culture === undefined || words === undefined || read === undefined || given === undefined)
        throw new Error(`${vector.id}: the file lacks the formats or words of ${name}, the form or the answers`);
      const recall = recallAnswers(recallForm(read), owner, given, hidden, words, culture);
      return renderTemplate(parseTemplate(text, context, recall.pathProblem), recall.values, culture);
    }
  } catch (error) {
    if (error instanceof LogicError)
      return error.at === undefined ? { error: error.code } : { error: error.code, at: error.at };
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
