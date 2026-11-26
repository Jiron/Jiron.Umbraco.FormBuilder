// The Vitest vector runner (ROADMAP section 4.3.3, ADR-0005): one test case per vector of every tests/vectors/*.json
// file, read in place. The NUnit runner runs the same files against Core.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { hidesMarkup, isExecutable, matchesKind } from '../src/file-kinds.ts';
import { cleanName, pngWithName } from '../src/typed-name.ts';
import {
  checkField,
  checkRows,
  clean,
  defaultMessage,
  evaluateCondition,
  evaluateForm,
  evaluateRule,
  formatDecimal,
  LogicError,
  messageWordsOf,
  readField,
  readPost,
  sendRefusal,
  summaryEntry,
  summaryHeading,
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
  type PostAnswer,
  type PostedAnswer,
  type PostField,
  type ReadField,
  type RecallAnswers,
  type RecallField,
  type RecallForm,
  type Rule,
  type Subject,
  type TemplateContext,
  type VisitorError,
} from '../src/index.ts';
import { calculate, evaluateFormula, FORMULAS, storedNumber } from '../src/calc.ts';
import { recallAnswers } from '../src/recall.ts';
import { parseTemplate, renderTemplate } from '../src/template.ts';
import { today } from '../src/dates.ts';
import { conflictOf, formSight, showsAtAll, type SearchRule, type SearchScope } from '../src/search.ts';
import { MESSAGE_TABLES } from './message-tables.ts';
import { repositoryRoot, vectorsFolder } from '../tools/repository.ts';

interface VectorCase {
  readonly id: string;
  readonly description?: string;
  readonly context?: Partial<Context>;
  readonly fields?: Readonly<Record<string, Kind>>;
  readonly values?: Readonly<Record<string, readonly string[]>>;
  readonly rows?: FormAnswers['rows'];
  readonly unreadable?: readonly string[];
  readonly unreadableRows?: FormAnswers['unreadableRows'];
  readonly fileKind?: { readonly extension: string; readonly head: string };
  readonly typedName?: { readonly typed: string };
  readonly typedPng?: { readonly image: string; readonly name: string };
  readonly condition?: Condition;
  readonly rule?: Rule;
  readonly number?: string;
  readonly calc?: Calculation;
  readonly formula?: string;
  readonly form?: LogicForm;
  readonly field?: VectorField;
  readonly answers?: FieldAnswers;
  readonly template?: VectorTemplate;
  readonly recall?: VectorRecall;
  readonly message?: VectorMessage;
  readonly read?: { readonly field: ReadField; readonly answer: PostedAnswer };
  readonly rowCount?: { readonly field: ReadField; readonly count: number };
  readonly post?: { readonly fields: readonly PostField[]; readonly entries: readonly (readonly [string, string])[] };
  readonly send?: { readonly fields: readonly SendField[]; readonly entries: readonly (readonly [string, string])[] };
  readonly options?: Readonly<Record<string, readonly string[]>>;
  readonly conflict?: {
    readonly requiring: SearchRule;
    readonly hidings: readonly SearchRule[];
    readonly form?: LogicForm;
  };
  readonly shows?: {
    readonly rules: readonly SearchRule[];
    readonly unanswered: readonly string[];
    readonly several?: readonly string[];
  };
  readonly expect: unknown;
}

/** A field of a send's form: as a post names it, and whether it is required (ADR-0266). */
interface SendField extends PostField {
  readonly required?: boolean;
  readonly fields?: readonly SendField[];
}

/** The context a send's fields are checked in, as the NUnit runner's SubmissionReader reads them. */
const SEND_CONTEXT: Context = {
  now: '2026-06-15T10:00:00Z',
  timeZone: 'Europe/Zurich',
  culture: 'en-GB',
  member: { signedIn: false, groups: [] },
  query: {},
  hidden: [],
};

/**
 * A send's problems (ADR-0266): the post read, each field's answer read and checked, a repeatable group's children in
 * each row, each error by its path, and the refusal of a send with no answer at all as the runtime decides it.
 */
function sendProblems(
  id: string,
  send: NonNullable<VectorCase['send']>,
): { problems: { code: string; path: string }[] } {
  const read = readPost(send.fields, send.entries);
  const problems: { code: string; path: string }[] = [];
  const check = (field: SendField, answer: PostAnswer | undefined, path: string): void => {
    const reading = readField(field, answer);
    if (reading === null) throw new Error(`${id}: ${field.type} is no built-in type`);
    if (reading.limit !== null) {
      problems.push({ code: reading.limit, path });
      return;
    }
    const checked: CheckedField = {
      type: field.type,
      required: field.required === true,
      settings: field.settings ?? {},
    };
    for (const code of checkField(checked, reading.answers, SEND_CONTEXT).errors) problems.push({ code, path });
  };
  for (const field of send.fields) {
    const alias = field.alias;
    if (alias == null) continue;
    if (field.fields === undefined) {
      check(field, read.fields.get(alias), alias);
      continue;
    }
    (read.rows.get(alias) ?? []).forEach((row, index) => {
      for (const child of field.fields ?? [])
        if (child.alias != null) check(child, row.get(child.alias), `${alias}[${index}].${child.alias}`);
    });
  }
  const refusal = sendRefusal(read, problems);
  return { problems: refusal === 'empty' ? [{ code: 'empty', path: '' }] : [...refusal] };
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

/** A visitor message (ADR-0099): an error's default message, the summary's heading for a number of errors, or a summary link. */
interface VectorMessage {
  readonly error?: VisitorError;
  readonly heading?: number;
  readonly entry?: { readonly message: string; readonly label?: string; readonly row?: string };
}

/** The message tables by language. */
const MESSAGE_WORDS = MESSAGE_TABLES;

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
 * A field to check as a vector gives it (ADR-0077): `fromSource` marks one whose options come from a list or another
 * source, its `options` being those the source resolved, and without them a source that could not give any. The browser
 * checks either the same way; the Core runner hands a source's options to the check as the reader does.
 */
type VectorField = CheckedField & { readonly fromSource?: boolean };

/**
 * Holds a field to check to what both runtimes see alike (ADR-0062, ADR-0077): its kind is its type's, it gives every
 * public setting its type has a default for, an object's settings each in turn, as the public projection writes them,
 * so no vector leans on a default, it has options, a source and rows only when its type has them, options or a source
 * when its type cannot be answered without, and a parent only with options or a source, a field that stores files has
 * one file for each value, and its answers are cleaned as the pipeline cleans them.
 */
function heldToItsType(id: string, field: VectorField, answers: FieldAnswers): void {
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
    ((field.options !== undefined || field.fromSource !== undefined) && !type.features.includes('options')) ||
    (field.rows !== undefined && !type.features.includes('rows'))
  )
    throw new Error(`${id}: a ${field.type} field has no options or rows of its own`);
  if (type.features.includes('needsOptions') && field.options === undefined && field.fromSource !== true)
    throw new Error(`${id}: a ${field.type} field gives its options, or that they come from a source`);
  if (field.parent !== undefined && field.options === undefined && field.fromSource !== true)
    throw new Error(`${id}: only a field with options or a source has a parent`);
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
    /** What the rule search tries (ADR-0179): each choice's own options by key, the site's languages, today. */
    readonly search?: {
      readonly options: Readonly<Record<string, readonly string[]>>;
      readonly cultures: readonly string[];
      readonly today: string;
    };
  };
  readonly fields: Readonly<Record<string, Kind>>;
  readonly cases: readonly VectorCase[];
}

/** The things a case can run; each case has exactly one. */
const RUNNABLE = [
  'condition',
  'rule',
  'number',
  'calc',
  'formula',
  'form',
  'field',
  'template',
  'recall',
  'message',
  'read',
  'rowCount',
  'post',
  'send',
  'fileKind',
  'typedName',
  'typedPng',
  'conflict',
  'shows',
] as const;

const folder = vectorsFolder();
const names = readdirSync(folder)
  .filter((name) => name.endsWith('.json'))
  .sort();

/** A subject as a formula reads it: the fields' kinds and answers by alias, a hidden field without answers. */
function formulaScope(subject: Subject, unreadable: readonly string[] = []): FormulaScope {
  const own = <T>(record: Readonly<Record<string, T>>, key: string): T | undefined =>
    Object.hasOwn(record, key) ? record[key] : undefined;
  return {
    kind: (alias) => own(subject.fields, alias),
    answers: (alias) => (subject.context.hidden.includes(alias) ? undefined : own(subject.values, alias)),
    today: () => today(subject.context.now, subject.context.timeZone),
    unreadable: (alias) => unreadable.includes(alias),
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

/** A case of the rule search: a conflict as the index of its hiding rule and its answers, or whether an element shows. */
function searched(file: VectorFile, vector: VectorCase): unknown {
  const defaults = file.defaults.search;
  if (!defaults) throw new Error(`${vector.id}: the file has no search defaults`);
  const options = { ...defaults.options, ...vector.options };
  const several = vector.shows?.several ?? [];
  const form = vector.conflict?.form;
  const scope: SearchScope = {
    fields: { ...file.fields, ...vector.fields },
    options: (key) => (Object.hasOwn(options, key) ? (options[key] ?? []) : []),
    cultures: defaults.cultures,
    today: defaults.today,
    several: (key) => several.includes(key),
    ...(form !== undefined ? { hidden: formSight(form, FORMULAS) } : {}),
  };
  if (vector.shows !== undefined) return showsAtAll(vector.shows.rules, scope, vector.shows.unanswered);
  if (vector.conflict === undefined) throw new Error(`${vector.id}: nothing to search`);
  const { requiring, hidings } = vector.conflict;
  const found = conflictOf(requiring, hidings, scope);
  return found === null ? null : { hiding: hidings.indexOf(found.hiding), answers: found.answers };
}

function run(file: VectorFile, vector: VectorCase): unknown {
  const subject: Subject = {
    fields: { ...file.fields, ...vector.fields },
    values: vector.values ?? {},
    context: { ...file.defaults.context, ...vector.context },
  };
  try {
    if (vector.conflict !== undefined || vector.shows !== undefined) return searched(file, vector);
    if (vector.condition !== undefined) return evaluateCondition(vector.condition, subject);
    if (vector.rule !== undefined) return evaluateRule(vector.rule, subject);
    if (vector.number !== undefined) return storedNumber(vector.number);
    if (vector.calc !== undefined) {
      const result = calculate(vector.calc, formulaScope(subject, vector.unreadable));
      return result === null ? null : formatDecimal(result);
    }
    if (vector.formula !== undefined) return evaluateFormula(vector.formula, formulaScope(subject, vector.unreadable));
    if (vector.field !== undefined) {
      const answers = vector.answers ?? { values: [] };
      heldToItsType(vector.id, vector.field, answers);
      return checkField(vector.field, answers, subject.context, vector.values ?? {});
    }
    if (vector.form !== undefined) {
      const answers: FormAnswers = {
        values: vector.values ?? {},
        rows: vector.rows ?? {},
        unreadable: vector.unreadable ?? [],
        unreadableRows: vector.unreadableRows ?? {},
      };
      return evaluateForm(vector.form, answers, subject.context, FORMULAS);
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
    if (vector.read !== undefined) {
      const read = readField(vector.read.field, vector.read.answer);
      if (read === null) throw new Error(`${vector.id}: ${vector.read.field.type} is no built-in type`);
      return {
        limit: read.limit,
        values: read.values.map((value) => ({
          text: value.text,
          ...(value.row === undefined ? {} : { row: value.row }),
          ...(value.stored === undefined ? {} : { stored: value.stored }),
        })),
        unreadable: read.unreadable,
      };
    }
    if (vector.rowCount !== undefined) return checkRows(vector.rowCount.field, vector.rowCount.count);
    if (vector.send !== undefined) return sendProblems(vector.id, vector.send);
    if (vector.post !== undefined) {
      const read = readPost(vector.post.fields, vector.post.entries);
      const answers = (map: ReadonlyMap<string, PostAnswer>) =>
        Object.fromEntries(
          [...map].map(([alias, answer]) => [
            alias,
            { values: answer.values, ...(answer.parts === undefined ? {} : { parts: answer.parts }) },
          ]),
        );
      return {
        fields: answers(read.fields),
        rows: Object.fromEntries([...read.rows].map(([alias, rows]) => [alias, rows.map(answers)])),
        context: Object.fromEntries(read.context),
        query: Object.fromEntries(read.query),
      };
    }
    if (vector.typedName !== undefined) return { name: cleanName(vector.typedName.typed) };
    if (vector.typedPng !== undefined) {
      const image = Uint8Array.from(vector.typedPng.image.match(/../g) ?? [], (pair) => Number.parseInt(pair, 16));
      const png = pngWithName(image, new TextEncoder().encode(vector.typedPng.name));
      const view = new DataView(png.buffer);
      return {
        png: Array.from(png, (byte) => byte.toString(16).padStart(2, '0')).join(''),
        name: new TextDecoder().decode(png.subarray(33 + 8 + 'jfb:typed-name'.length + 5, 33 + 8 + view.getUint32(33))),
        width: view.getUint32(16),
        height: view.getUint32(20),
      };
    }
    if (vector.fileKind !== undefined) {
      const { extension, head } = vector.fileKind;
      const bytes = Uint8Array.from(head.match(/../g) ?? [], (pair) => Number.parseInt(pair, 16));
      return {
        executable: isExecutable(bytes),
        matches: matchesKind(extension, bytes),
        hidesMarkup: hidesMarkup(extension, bytes),
      };
    }
    if (vector.message !== undefined) {
      const name = subject.context.culture;
      const words = messageWordsOf(name, MESSAGE_WORDS);
      const { error, heading, entry } = vector.message;
      if (heading !== undefined) return summaryHeading(heading, words);
      if (entry !== undefined) return summaryEntry(entry.message, entry.label, entry.row, words);
      const culture = file.defaults.cultures?.[name];
      const display = file.defaults.words?.[name];
      if (error === undefined || culture === undefined || display === undefined)
        throw new Error(`${vector.id}: the file lacks the formats or words of ${name}, or the case its error`);
      return defaultMessage(error, words, culture, today(subject.context.now, subject.context.timeZone), display);
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
