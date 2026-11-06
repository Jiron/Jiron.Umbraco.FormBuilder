// The content action's editors' model (ROADMAP 10.2e, ADR-0192): where the item goes, and the table that sets its
// properties from a field's answer or a value written for it. Pure, so the sidebar's editors and their tests share it.
import type { PickField } from './action-editors';

/** Where the item goes: under the item picked (the content root without one), the visitor's page, or their site's root. */
export type ParentSource = 'picked' | 'page' | 'siteRoot';

/** The source a setting holds, the picked item's for anything else. */
export const parentSourceOf = (value: unknown): ParentSource =>
  value === 'page' || value === 'siteRoot' ? value : 'picked';

/** What a value written for a property must be; null where only a field's answer fits. */
export type ValueKind = 'text' | 'wholeNumber' | 'decimal' | 'date' | 'boolean';

/** A property a content action can set, as the server describes it. */
export interface ContentProperty {
  readonly alias: string;
  readonly name: string;
  readonly group: string | null;
  /** The field types whose answer it can hold. */
  readonly fieldTypes: readonly string[];
  readonly value: ValueKind | null;
}

/** A document type a content action can create, with the properties a form can set. */
export interface ContentType {
  readonly alias: string;
  readonly name: string;
  readonly properties: readonly ContentProperty[];
}

/** A document type as the sidebar has it: read, being read, one the site does not have, or one it could not read. */
export type TypeRead = ContentType | 'reading' | 'gone' | 'failed';

/** A row of the table: a property and where its value comes from, a field's answer or a value written for it. */
export interface MappingRow {
  /** The property's alias; empty while a new row has none. */
  readonly property: string;
  /** The field whose answer it takes. */
  readonly field?: string | undefined;
  /** The value written for it, which a text may fill with merge tags. */
  readonly value?: string | undefined;
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

const KINDS: ReadonlySet<string> = new Set(['text', 'wholeNumber', 'decimal', 'date', 'boolean']);

/** A value kind as the server says it; null for none or one the backoffice does not know. */
export const valueKindOf = (value: unknown): ValueKind | null =>
  typeof value === 'string' && KINDS.has(value) ? (value as ValueKind) : null;

/** A document type as the server describes it, what a property takes read as the backoffice knows it. */
export function contentTypeOf(model: {
  readonly alias: string;
  readonly name: string;
  readonly properties: readonly {
    readonly alias: string;
    readonly name: string;
    readonly group?: string | null;
    readonly fieldTypes: readonly string[];
    readonly value?: string | null;
  }[];
}): ContentType {
  return {
    alias: model.alias,
    name: model.name,
    properties: model.properties.map((property) => ({
      alias: property.alias,
      name: property.name,
      group: property.group ?? null,
      fieldTypes: property.fieldTypes,
      value: valueKindOf(property.value),
    })),
  };
}

/** The rows a setting holds, as many as it has items, an item that is no row read as an empty one. */
export function rowsOf(value: unknown): MappingRow[] {
  if (!Array.isArray(value)) return [];
  return value.map((item): MappingRow => {
    if (!isObject(item)) return { property: '' };
    const property = typeof item['property'] === 'string' ? item['property'] : '';
    if (typeof item['field'] === 'string') return { property, field: item['field'] };
    if (typeof item['value'] === 'string') return { property, value: item['value'] };
    return { property };
  });
}

/** The setting's value for rows: each row with a property and a source, in its order; none for no row. */
export function mappingOf(rows: readonly MappingRow[]): Record<string, string>[] | undefined {
  const kept = rows
    .filter((row) => row.property !== '' && (row.field !== undefined || row.value !== undefined))
    .map((row) =>
      row.field !== undefined
        ? { property: row.property, field: row.field }
        : { property: row.property, value: row.value ?? '' },
    );
  return kept.length > 0 ? kept : undefined;
}

/**
 * The fields whose answer a property can hold: the form's fields of a type it takes, each with one answer (not inside a
 * repeatable group, which has one for each row).
 */
export function fieldsFor(property: ContentProperty | undefined, fields: readonly PickField[]): PickField[] {
  if (!property) return [];
  const types = new Set(property.fieldTypes);
  return fields.filter((field) => field.top && types.has(field.type));
}

/**
 * The rows a table starts with for a document type: each property whose alias is a field's that it can hold, that
 * field's answer; a sensitive field only where the action may copy it.
 */
export function starterRows(
  type: ContentType,
  fields: readonly PickField[],
  included: readonly string[] = [],
): MappingRow[] {
  return type.properties.flatMap((property) => {
    const field = fieldsFor(property, fields).find(
      (each) => each.alias === property.alias && (!each.sensitive || included.includes(each.key)),
    );
    return field ? [{ property: property.alias, field: field.key }] : [];
  });
}

/** Whether two settings' values hold the same rows, in the same order. */
export const sameMapping = (a: unknown, b: unknown): boolean =>
  JSON.stringify(mappingOf(rowsOf(a)) ?? []) === JSON.stringify(mappingOf(rowsOf(b)) ?? []);

/** The values a value written for a yes-or-no property may be, as the server reads them. */
export const BOOLEAN_VALUES = ['true', 'false'] as const;

/**
 * A decimal as the server reads it, with a point: one written with a decimal comma, as German and many languages write
 * it, gets a point, so 1,5 stays 1.5 (ADR-0192). Anything else is kept as written, for the check to say what is wrong.
 */
export const decimalText = (text: string): string =>
  /^\s*[-+]?\d+,\d+\s*$/.test(text) ? text.trim().replace(',', '.') : text;
