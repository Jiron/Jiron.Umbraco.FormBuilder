/**
 * The page's checks (ROADMAP 6.4d, ADR-0112): each shown field's errors as Core's SubmissionReader decides them, over
 * the answers the page holds (6.4b) and in the state its rules give (6.4c), through the built-in types' checks of
 * `packages/logic`. What only the server can decide is left to it: a calculation's value, a value it resolves, files,
 * options from a list or a source, the checks against other entries and a site's own type's check.
 */
import {
  BUILT_IN_TYPES,
  checkField,
  clean,
  own,
  type CheckedField,
  type CheckedOption,
  type Context,
  type FieldAnswers,
  type FormState,
  type Kind,
} from '@jfb/logic';
import type { FormModel, ModelField } from './model';
import { builtIn, fieldsOf, type FieldRead, type PageReading } from './page';

/** An error at a field, as the pipeline reports it. */
export interface PageError {
  /** The field's key; for a validation rule's message at a grid's row, the grid's. */
  readonly field: string;
  /** The row of the repeatable group the field is in, or null. */
  readonly row: number | null;
  /** The field's kind of error, `check` for one of its own checks, or `rule` for a validation rule. */
  readonly code: string;
  /** For `check`, the index of the field's own check. */
  readonly check?: number;
  /** For `rule`, the validation rule's key. */
  readonly rule?: string;
  /** For a validation rule's message at a grid's row, the row's key. */
  readonly gridRow?: string;
  /** For a name or an address, the parts the page marks invalid: the needed ones that are empty, else every part. */
  readonly parts?: readonly string[];
}

/**
 * The errors of the shown fields in the form's order, every one: a group's row count's and the validation rules' at
 * it, then each row's shown children's; for a field its type's error kinds, its own checks and the validation rules
 * whose message shows at it or at one of a grid's rows.
 */
export function errorsOf(
  model: FormModel,
  reading: PageReading,
  state: FormState,
  context: Context,
  onPage: ReadonlySet<string> = new Set(),
): PageError[] {
  const hidden = new Set(state.hidden);
  const required = new Set(state.required);
  const others = othersOf(model, reading, state, hidden);
  const errors: PageError[] = [];
  for (const field of fieldsOf(model)) {
    if (hidden.has(field.key)) continue;
    const group = reading.groups.get(field.key);
    if (group === undefined) {
      const from = errors.length;
      const read = reading.fields.get(field.key);
      check(errors, model, field, read, null, required.has(field.key), others, context, onPage);
      messages(errors, state.messages, field, null);
      withParts(errors, from, model, field, read, required.has(field.key));
      continue;
    }
    for (const code of group.errors) errors.push({ field: field.key, row: null, code });
    messages(errors, state.messages, field, null);
    const rows = state.rows[field.key] ?? [];
    group.rows.forEach((children, row) => {
      const rowState = rows[row];
      if (rowState === undefined) return;
      const rowOthers: Record<string, readonly string[]> = { ...others };
      for (const child of field.fields ?? [])
        rowOthers[child.key] = rowState.hidden.includes(child.key)
          ? []
          : valuesOf(child.key, children.get(child.key), rowState.calculations);
      for (const child of field.fields ?? []) {
        if (rowState.hidden.includes(child.key)) continue;
        const from = errors.length;
        const isRequired = rowState.required.includes(child.key);
        check(errors, model, child, children.get(child.key), row, isRequired, rowOthers, context, onPage);
        messages(errors, rowState.messages, child, row);
        withParts(errors, from, model, child, children.get(child.key), isRequired);
      }
    });
  }
  return errors;
}

/** One shown field's errors, as `SubmissionReader.CheckAsync` decides them. */
function check(
  errors: PageError[],
  model: FormModel,
  field: ModelField,
  read: FieldRead | undefined,
  row: number | null,
  isRequired: boolean,
  others: Readonly<Record<string, readonly string[]>>,
  context: Context,
  onPage: ReadonlySet<string>,
): void {
  const logic = own(model.fields, field.key);
  const builtIn = logic?.builtIn === true;
  const declared = builtIn ? own(BUILT_IN_TYPES, field.type) : undefined;
  const limit = read?.reading?.limit ?? null;
  if (limit !== null) {
    errors.push({ field: field.key, row, code: limit });
    return;
  }
  // The server's to decide: a calculation's value, a value it resolves, and the files it was sent, but for a field whose
  // files the page takes, which reads them all (ADR-0152).
  if (field.type === 'calculation' && builtIn) return;
  if (declared?.serverOnly === true || (declared?.storage === 'file' && !onPage.has(field.key))) return;
  const checked: CheckedField = {
    type: field.type,
    ...(logic?.kind == null ? {} : { kind: logic.kind as Kind }),
    required: isRequired,
    settings: field.settings ?? {},
    ...optionsOf(field),
    rows: (field.rows ?? []).map((gridRow) => ({ id: gridRow.key, required: gridRow.required === true })),
    checks: (field.validation ?? []).map((own) => ({
      kind: own.kind,
      ...(own.pattern === undefined ? {} : { pattern: own.pattern }),
      ...(own.field === undefined ? {} : { field: own.field }),
    })),
  };
  const answers: FieldAnswers = read?.reading?.answers ?? { values: read?.values ?? [] };
  const found = checkField(checked, answers, context, others);
  for (const code of found.errors) errors.push({ field: field.key, row, code });
  for (const index of found.checks) errors.push({ field: field.key, row, code: 'check', check: index });
}

/**
 * The options a field's answers are held to: its own, with the parent answers each belongs to; none for a list or a
 * source, whose answers the server holds to the options it resolves.
 */
function optionsOf(field: ModelField): { options?: readonly CheckedOption[]; parent?: string } {
  const options = field.options;
  if (options == null || options.fromSource === true) return {};
  return {
    options: (options.items ?? []).map((item) =>
      item.parentValues != null && item.parentValues.length > 0
        ? { value: item.value, parentValues: item.parentValues }
        : item.value,
    ),
    ...(options.parent == null ? {} : { parent: options.parent }),
  };
}

/**
 * The parts of a built-in name or address its errors mark, as Web's render marks them: the parts it needs (those its
 * settings list, else every part it asks for once it is required) that are empty, or every part when none is.
 */
function withParts(
  errors: PageError[],
  from: number,
  model: FormModel,
  field: ModelField,
  read: FieldRead | undefined,
  isRequired: boolean,
): void {
  if (errors.length === from || (field.type !== 'name' && field.type !== 'address')) return;
  if (!builtIn(model, field)) return;
  const asked = textsOf(field.settings?.['parts']);
  const listed = textsOf(field.settings?.['requiredParts']).filter((part) => asked.includes(part));
  const needed = listed.length > 0 ? listed : isRequired ? asked : [];
  const given = read?.reading?.answers?.parts ?? {};
  const empty = needed.filter((part) => (own(given, part) ?? []).every((text) => clean(text).length === 0));
  const parts = empty.length === 0 ? asked : empty;
  for (let at = from; at < errors.length; at++) {
    const error = errors[at];
    if (error !== undefined) errors[at] = { ...error, parts };
  }
}

const textsOf = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter((each): each is string => typeof each === 'string') : [];

/** The validation rules whose message shows at the field or at one of a grid's rows. */
function messages(
  errors: PageError[],
  shown: Readonly<Record<string, readonly string[]>>,
  field: ModelField,
  row: number | null,
): void {
  for (const key of [field.key, ...(field.rows ?? []).map((gridRow) => gridRow.key)]) {
    const rules = own(shown, key);
    for (const rule of rules ?? [])
      errors.push({ field: field.key, row, code: 'rule', rule, ...(key === field.key ? {} : { gridRow: key }) });
  }
}

/**
 * The values a check compares with, as the rules see them: each shown field's own, a calculation's as the rules work
 * it out, and a repeatable group's child's across the rows it is shown in; a hidden field has none.
 */
function othersOf(
  model: FormModel,
  reading: PageReading,
  state: FormState,
  hidden: ReadonlySet<string>,
): Record<string, readonly string[]> {
  const others: Record<string, readonly string[]> = Object.create(null) as Record<string, readonly string[]>;
  for (const field of fieldsOf(model)) {
    if (hidden.has(field.key)) continue;
    const group = reading.groups.get(field.key);
    if (group === undefined) {
      others[field.key] = valuesOf(field.key, reading.fields.get(field.key), state.calculations);
      continue;
    }
    const rows = state.rows[field.key] ?? [];
    for (const child of field.fields ?? [])
      others[child.key] = group.rows.flatMap((children, row) => {
        const rowState = rows[row];
        return rowState === undefined || rowState.hidden.includes(child.key)
          ? []
          : valuesOf(child.key, children.get(child.key), rowState.calculations);
      });
  }
  return others;
}

/** A field's values as the rules see them: a calculation's result, or what the page holds. */
function valuesOf(
  key: string,
  read: FieldRead | undefined,
  calculations: Readonly<Record<string, string | null>>,
): readonly string[] {
  if (Object.hasOwn(calculations, key)) {
    const value = calculations[key];
    return value == null ? [] : [value];
  }
  return read?.values ?? [];
}
