/**
 * Reading the page (ROADMAP 6.4b, ADR-0110): the form's controls as the browser would post them, read by their names
 * as the server reads a post without JavaScript (`readPost`, held to Web's FormPostReader by tests/vectors/posts.json),
 * then each field through 6.4a's reading stage (ADR-0109), as Core's SubmissionReader reads a submission: what its
 * rules compare, its checks hold and its messages print.
 */
import {
  BUILT_IN_TYPES,
  checkRows,
  cleanAnswer,
  readField,
  readPost,
  type FieldReading,
  type FormAnswers,
  type PostAnswer,
  type PostField,
  type ReadField,
  type ReadPost,
} from '@jfb/logic';
import type { FormModel, ModelField } from './model';

/** One field's answer read from the page. */
export interface FieldRead {
  readonly field: ModelField;
  /** The answer as the page posts it; none when the page holds nothing of it. */
  readonly posted: PostAnswer | undefined;
  /** Through the reading stage; null for a site's own type, which only the server reads. */
  readonly reading: FieldReading | null;
  /** What the rules compare: the values read in the wire form, or a site's own type's cleaned texts as they are. */
  readonly values: readonly string[];
}

/** A repeatable group's rows read from the page. */
export interface GroupRead {
  readonly field: ModelField;
  /** How many rows the page shows. */
  readonly count: number;
  /** The errors of that number of rows (`rowCount`); while there are any, the rows are not read. */
  readonly errors: readonly string[];
  /** Each row's child fields' answers by their keys. */
  readonly rows: readonly ReadonlyMap<string, FieldRead>[];
}

/** The page's answers. */
export interface PageReading {
  /** The post as the pipeline would take it. */
  readonly post: ReadPost;
  /** Each field's outside repeatable groups, by its key. */
  readonly fields: ReadonlyMap<string, FieldRead>;
  /** Each repeatable group's, by its key. */
  readonly groups: ReadonlyMap<string, GroupRead>;
  /**
   * The answers as the rules see them: each field's and grid row's values by key, and each group's rows; and the
   * fields whose answers their types could not read, which leave a calculation that reads them without a value.
   */
  readonly answers: FormAnswers;
}

/** The page's names and texts as the browser posts them; a file is the upload's to read (6.4m). */
export function entriesOf(form: HTMLFormElement): [string, string][] {
  const entries: [string, string][] = [];
  for (const [name, value] of new FormData(form)) if (typeof value === 'string') entries.push([name, value]);
  return entries;
}

/** Reads the form's answers from its controls. */
export function readPage(form: HTMLFormElement, model: FormModel): PageReading {
  const fields = fieldsOf(model);
  const post = readPost(
    fields.map((field) => postField(model, field)),
    entriesOf(form),
  );
  const read = new Map<string, FieldRead>();
  const groups = new Map<string, GroupRead>();
  const values: Record<string, string[]> = Object.create(null) as Record<string, string[]>;
  const rows: Record<string, Record<string, string[]>[]> = Object.create(null) as Record<
    string,
    Record<string, string[]>[]
  >;
  const unreadable: string[] = [];
  const unreadableRows: Record<string, string[][]> = Object.create(null) as Record<string, string[][]>;
  for (const field of fields) {
    if (isGroup(model, field)) {
      const posted = typeof field.alias === 'string' ? (post.rows.get(field.alias) ?? []) : [];
      const errors = builtIn(model, field) ? checkRows(readable(field), posted.length) : [];
      const childRows =
        errors.length > 0
          ? []
          : posted.map((row) => new Map((field.fields ?? []).map((child) => [child.key, readOne(model, child, row)])));
      groups.set(field.key, { field, count: posted.length, errors, rows: childRows });
      rows[field.key] = childRows.map((row) => {
        const texts: Record<string, string[]> = Object.create(null) as Record<string, string[]>;
        for (const child of row.values()) put(texts, child);
        return texts;
      });
      unreadableRows[field.key] = childRows.map((row) =>
        [...row.values()].filter(isUnreadable).map((child) => child.field.key),
      );
      continue;
    }
    const one = readOne(model, field, post.fields);
    read.set(field.key, one);
    put(values, one);
    if (isUnreadable(one)) unreadable.push(field.key);
  }
  return { post, fields: read, groups, answers: { values, rows, unreadable, unreadableRows } };
}

/** Every field outside repeatable groups, in the order of the form's pages and groups. */
export const fieldsOf = (model: FormModel): readonly ModelField[] =>
  model.form.pages.flatMap((page) => page.groups.flatMap((group) => group.fields));

/** A field as the post reader names it. */
function postField(model: FormModel, field: ModelField): PostField {
  return {
    alias: field.alias ?? null,
    type: field.type,
    builtIn: builtIn(model, field),
    settings: field.settings ?? null,
    fields: (field.fields ?? []).map((child) => postField(model, child)),
  };
}

function readOne(model: FormModel, field: ModelField, answers: ReadonlyMap<string, PostAnswer>): FieldRead {
  const given = typeof field.alias === 'string' ? answers.get(field.alias) : undefined;
  if (!builtIn(model, field)) {
    return { field, posted: given, reading: null, values: cleanAnswer(given).values };
  }
  // A value the server resolves is never taken from the page, unless the field is prefilled from the query.
  const declared = Object.hasOwn(BUILT_IN_TYPES, field.type) ? BUILT_IN_TYPES[field.type] : undefined;
  const taken = declared?.serverOnly === true && typeof field.prefill?.query !== 'string' ? undefined : given;
  const reading = readField(readable(field), taken);
  return {
    field,
    posted: given,
    reading,
    values:
      reading === null
        ? cleanAnswer(given).values
        : reading.values.filter((value) => value.row === undefined).map((value) => value.text),
  };
}

/** The field as the reading stage reads it: its type, its settings and a grid's rows by their keys. */
const readable = (field: ModelField): ReadField => ({
  type: field.type,
  settings: field.settings ?? null,
  ...(field.rows === undefined ? {} : { rows: field.rows.map((row) => ({ id: row.key })) }),
});

/** A field's values by its key, and a grid's by its rows' keys; none for a field without any, as Core puts them. */
function put(texts: Record<string, string[]>, read: FieldRead): void {
  if (read.values.length > 0) texts[read.field.key] = [...read.values];
  for (const value of read.reading?.values ?? []) {
    if (value.row === undefined) continue;
    (texts[value.row] ??= []).push(value.text);
  }
}

/** Whether the reading stage found an answer over its type's limits, or one its type could not read (ADR-0114). */
const isUnreadable = (read: FieldRead): boolean =>
  read.reading !== null && (read.reading.limit !== null || read.reading.unreadable);

const builtIn = (model: FormModel, field: ModelField): boolean =>
  Object.hasOwn(model.fields, field.key) && model.fields[field.key]?.builtIn === true;

/** A repeatable group: a built-in type that holds fields, or a site's own type that does. */
function isGroup(model: FormModel, field: ModelField): boolean {
  if (!builtIn(model, field)) return (field.fields?.length ?? 0) > 0;
  return Object.hasOwn(BUILT_IN_TYPES, field.type) && BUILT_IN_TYPES[field.type]?.childFields === true;
}
