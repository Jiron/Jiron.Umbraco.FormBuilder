// A choice field's options as the inspector edits them (ROADMAP 9.4b, ADR-0172): where they come from, a field's own
// options with what each may hold, and a grid's rows. Values are cleaned and compared as the server compares answers
// (ADR-0005), so an option the server would refuse as a repeat is never added. Pure, so it is tested without the DOM.
import { clean, fold } from '@jfb/logic';
import type { FieldTypeResponseModel } from '../../api';
import type { DesignDefinition, DesignField, LocalizedText } from '../definition';
import { settingsOf, valueOf, withKey, withText } from './setting-values';

/** Where a field's options come from, as `options.source` names it. */
export type OptionSourceKind = 'items' | 'list' | 'content' | 'dataType' | 'url' | 'countries' | 'code';

/** One of a field's own options, as the definition holds it. */
export interface DesignOption {
  readonly value: string;
  readonly label?: LocalizedText;
  readonly description?: LocalizedText;
  readonly image?: string;
  readonly places?: number;
  readonly parentValues?: readonly string[];
}

/** A grid's row: a statement rated on its columns. */
export interface DesignRow {
  readonly key: string;
  readonly label?: LocalizedText;
  readonly required?: boolean;
}

/** A field's options object, as the definition holds it. */
export interface DesignOptions {
  readonly source?: OptionSourceKind | undefined;
  readonly items?: readonly DesignOption[] | undefined;
  readonly list?: string | undefined;
  readonly config?: Readonly<Record<string, unknown>> | undefined;
  readonly parent?: string | undefined;
}

/** The longest value an entry stores. */
export const VALUE_LENGTH = 200;

/** The longest one-line text, an option's label or a row's statement among them. */
export const LINE_LENGTH = 500;

/**
 * The most options of its own a field holds, and the most rows a grid holds (rows count as a form's fields), as the
 * server's default limits have them; a host may set others, which the save then holds a draft to.
 */
export const MOST_OPTIONS = 1_000;
export const MOST_ROWS = 300;

// Line breaks as the server reads them in a one-line text: CR LF, CR, LF, NEL, LS and PS.
const LINE_BREAK = new RegExp(
  [[13, 10], [13], [10], [0x85], [0x2028], [0x2029]].map((codes) => String.fromCharCode(...codes)).join('|'),
);

/** A text cut to at most `length` UTF-16 units, as the server counts them, never inside a character. */
export function cut(text: string, length: number): string {
  if (text.length <= length) return text;
  const last = text.charCodeAt(length - 1);
  return text.slice(0, last >= 0xd800 && last <= 0xdbff ? length - 1 : length);
}

/** Pasted text as its lines, cleaned, blank ones left out, each cut to a one-line text. */
const linesOf = (text: string): string[] =>
  text
    .split(LINE_BREAK)
    .map((line) => cut(clean(line), LINE_LENGTH))
    .filter((line) => line !== '');

/** A field's options, or none. */
export const optionsOf = (field: DesignField): DesignOptions => (field['options'] as DesignOptions | undefined) ?? {};

/** Where a field's options come from: its own items unless it says otherwise. */
export const sourceOf = (field: DesignField): OptionSourceKind => optionsOf(field).source ?? 'items';

/** A field's own options. */
export const itemsOf = (field: DesignField): readonly DesignOption[] => optionsOf(field).items ?? [];

/** A grid's rows. */
export const rowsOf = (field: DesignField): readonly DesignRow[] => (field['rows'] as DesignRow[] | undefined) ?? [];

/** The field with its options object, left out when it holds nothing, as a draft writes it. */
export function withOptions(field: DesignField, options: DesignOptions): DesignField {
  const kept = Object.fromEntries(
    Object.entries(options).filter(
      ([name, value]) =>
        value !== undefined &&
        !(Array.isArray(value) && value.length === 0) &&
        !(name === 'source' && value === 'items'),
    ),
  );
  return withKey<unknown>(field, 'options', Object.keys(kept).length > 0 ? kept : undefined) as DesignField;
}

/**
 * The options of another source: a list holds only its key, a kind only its configuration, own items only themselves,
 * as the server keeps them apart (ADR-0073); the parent stays. Undo brings back what the old source held.
 */
export function withSource(field: DesignField, source: OptionSourceKind): DesignField {
  const { parent } = optionsOf(field);
  if (source === 'items') return withOptions(field, { parent });
  if (source === 'list') return withOptions(field, { source, parent });
  return withOptions(field, { source, config: {}, parent });
}

/** The field with its own options. */
export const withItems = (field: DesignField, items: readonly DesignOption[]): DesignField =>
  withOptions(field, { ...optionsOf(field), items });

/** The field with a grid's rows, left out when there are none. */
export const withRows = (field: DesignField, rows: readonly DesignRow[]): DesignField =>
  withKey<unknown>(field, 'rows', rows.length > 0 ? rows : undefined) as DesignField;

/** The list moved: the entry at `from` now at `to`. */
export function moved<T>(list: readonly T[], from: number, to: number): T[] {
  const next = [...list];
  const [taken] = next.splice(from, 1);
  if (taken === undefined) return [...list];
  next.splice(Math.max(0, Math.min(to, next.length)), 0, taken);
  return next;
}

/** The list with one entry replaced. */
export const replaced = <T>(list: readonly T[], at: number, entry: T): T[] =>
  list.map((each, index) => (index === at ? entry : each));

/** Whether a value is one an entry can store: more than spaces, as cleaning leaves it, at most 200 characters. */
export const isValue = (value: string): boolean =>
  value.length > 0 && value.length <= VALUE_LENGTH && clean(value) === value;

/** Whether a value repeats another option's once case is folded as the server folds it. */
export const repeats = (items: readonly DesignOption[], value: string, except = -1): boolean =>
  items.some((item, at) => at !== except && fold(item.value) === fold(value));

/**
 * The value a new option stores, from what it is called: the label cleaned and cut to 200 characters, or the next
 * free "option n" where the label leaves nothing or repeats another.
 */
export function valueFor(label: string, items: readonly DesignOption[]): string {
  const cleaned = cut(clean(label), VALUE_LENGTH);
  const ok = (value: string) => isValue(value) && !repeats(items, value);
  if (ok(cleaned)) return cleaned;
  for (let n = items.length + 1; ; n++) if (ok(`option${n}`)) return `option${n}`;
}

/** A new option called `label` in the language shown. */
export function newOption(
  label: string,
  items: readonly DesignOption[],
  culture: string,
  isDefault: boolean,
): DesignOption {
  const text = withText(undefined, culture, label, isDefault);
  return { value: valueFor(label, items), ...(text ? { label: text } : {}) };
}

/** What pasting lines added, and how many repeated an option already there. */
export interface Pasted {
  readonly items: readonly DesignOption[];
  readonly added: number;
  readonly repeated: number;
}

/**
 * Options from pasted text, one a line: blank lines are skipped, and a line whose value repeats an option already
 * there, or one pasted before it, is counted and left out. At most 1,000 options in all.
 */
export function pasteOptions(
  text: string,
  items: readonly DesignOption[],
  culture: string,
  isDefault: boolean,
): Pasted {
  let next = [...items];
  let added = 0;
  let repeated = 0;
  for (const label of linesOf(text)) {
    if (repeats(next, cut(label, VALUE_LENGTH))) {
      repeated++;
      continue;
    }
    if (next.length >= MOST_OPTIONS) break;
    next = [...next, newOption(label, next, culture, isDefault)];
    added++;
  }
  return { items: next, added, repeated };
}

/** Rows from pasted text, one a line, each with a new key; at most 300 in all. */
export function pasteRows(
  text: string,
  rows: readonly DesignRow[],
  culture: string,
  isDefault: boolean,
  newKey: () => string,
): DesignRow[] {
  const added = linesOf(text)
    .slice(0, Math.max(0, MOST_ROWS - rows.length))
    .map((line): DesignRow => {
      const label = withText(undefined, culture, line, isDefault);
      return { key: newKey(), ...(label ? { label } : {}) };
    });
  return [...rows, ...added];
}

/**
 * Keys for a list's rows to be drawn by: each value, a repeat of one (which only a draft written elsewhere holds, as
 * publishing refuses it) told apart by how many came before it.
 */
export function rowKeys(values: readonly string[]): string[] {
  const seen = new Map<string, number>();
  return values.map((value) => {
    const count = seen.get(value) ?? 0;
    seen.set(value, count + 1);
    return JSON.stringify([value, count]);
  });
}

/** Whether a field's options show as cards: its own layout, or the form's where it inherits, as the canvas draws it. */
export function showsCards(field: DesignField, type: FieldTypeResponseModel, definition: DesignDefinition): boolean {
  const setting = type.settings.find((each) => each.name === 'layout');
  if (!setting) return false;
  const own = valueOf(settingsOf(field), setting);
  const form = (definition['settings'] as { appearance?: { choiceLayout?: unknown } } | undefined)?.appearance;
  return (own && own !== 'inherit' ? own : (form?.choiceLayout ?? 'vertical')) === 'cards';
}

/** An object with one member set, or taken away for undefined. */
export function withMember<T extends object>(object: T, name: keyof T & string, value: unknown): T {
  const rest = Object.fromEntries(Object.entries(object).filter(([key]) => key !== name));
  return (value === undefined ? rest : { ...rest, [name]: value }) as unknown as T;
}

/** An option with one property set, or taken away for undefined or an empty list. */
export function withOptionProperty(option: DesignOption, name: keyof DesignOption, value: unknown): DesignOption {
  const empty = value === undefined || (Array.isArray(value) && value.length === 0);
  return withMember(option, name, empty ? undefined : value);
}

/**
 * The fields a field's options may depend on: the choice fields before it in reading order, which a parent must be
 * (the server refuses a cycle, and one before it can't make one), with the options a designer can tie to their answers.
 */
export function parentsBefore(
  fields: readonly DesignField[],
  key: string,
  isChoice: (field: DesignField) => boolean,
): DesignField[] {
  const index = fields.findIndex((each) => each.key === key);
  return fields.slice(0, Math.max(0, index)).filter((each) => each.key !== key && isChoice(each));
}
