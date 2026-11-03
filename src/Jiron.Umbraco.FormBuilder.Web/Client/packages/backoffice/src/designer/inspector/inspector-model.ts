// What the inspector shows for the selected element (ROADMAP 9.4a, IA 5.1, ADR-0170): its sections in order, Essentials,
// Validation, Rules, Appearance and Advanced, each with the items it holds. A field's items are the common properties
// its type's features allow, then the type's settings by their group; a setting the host locks shows read-only, one it
// hides not at all. Pure, so the sections of every built-in type are tested without the DOM.
import type { FieldSettingResponseModel, FieldTypeResponseModel } from '../../api';
import type { DesignField, LocalizedText } from '../definition';
import { keyIn } from '../preview/field-models';
import { itemsOf, rowsOf } from './option-values';

/** The inspector's sections, in their order. */
export const SECTIONS = ['essentials', 'validation', 'rules', 'appearance', 'advanced'] as const;
export type SectionId = (typeof SECTIONS)[number];

/** How many items a section shows before "More options". */
export const MOST_SHOWN = 7;

/**
 * The setting editors the inspector draws: the plain ones (9.4a), Umbraco's pickers and rich text (9.4c, ADR-0173);
 * options are an item of their own (9.4b).
 */
export const PLAIN_EDITORS: ReadonlySet<string> = new Set([
  'wholeNumber',
  'number',
  'text',
  'boolean',
  'choice',
  'localizedText',
  'list',
  'nested',
  'time',
  'dateBound',
  'formula',
  'mediaPicker',
  'contentPicker',
  'documentPicker',
  'documentTypePicker',
  'dataTypePicker',
  'localizedHtml',
]);

/** A field's texts the inspector edits in each language. */
export type TextProperty = 'label' | 'help' | 'placeholder';

/** One thing the inspector edits. */
export type Item =
  | { readonly kind: 'text'; readonly property: TextProperty | 'title'; readonly long?: boolean }
  | { readonly kind: 'switch'; readonly property: 'required' | 'hideLabel' | 'sensitive' | 'includeInEmails' }
  | { readonly kind: 'span'; readonly columns: number }
  | { readonly kind: 'columns' }
  | { readonly kind: 'style' }
  | { readonly kind: 'alias' }
  | { readonly kind: 'plain'; readonly property: 'autocomplete' | 'cssClass' }
  | { readonly kind: 'defaultValue' }
  | { readonly kind: 'prefill' }
  | { readonly kind: 'keep' }
  | { readonly kind: 'message'; readonly message: string }
  | { readonly kind: 'pattern' }
  | { readonly kind: 'unique' }
  | { readonly kind: 'matches' }
  /** Where a choice field's options come from, and its own options (9.4b). */
  | { readonly kind: 'options' }
  /** A grid's rows (9.4b). */
  | { readonly kind: 'rows' }
  | { readonly kind: 'setting'; readonly setting: FieldSettingResponseModel };

export interface Section {
  readonly id: SectionId;
  readonly items: readonly Item[];
}

const shownSettings = (type: FieldTypeResponseModel, group: string): Item[] =>
  type.settings
    .filter((setting) => setting.group === group && PLAIN_EDITORS.has(setting.editor) && !setting.lock?.hidden)
    .map((setting) => ({ kind: 'setting', setting }));

const sections = (by: Partial<Record<SectionId, Item[]>>, rules: boolean): Section[] =>
  SECTIONS.filter((id) => (id === 'rules' ? rules : (by[id]?.length ?? 0) > 0)).map((id) => ({
    id,
    items: by[id] ?? [],
  }));

/**
 * A field's sections: in Essentials its label, help, Required and placeholder; in Validation its type's checks, the
 * checks no type owns that it allows, and the messages it may say; Rules where its type takes them; in Appearance a
 * hidden label and its width in the group's columns; in Advanced its alias, default value, prefill, autocomplete, what
 * is kept of its answer, and a class.
 */
export function fieldSections(type: FieldTypeResponseModel, groupColumns: number): Section[] {
  const has = (feature: string) => type.features.includes(feature);
  const essentials: Item[] = [];
  if (has('label')) essentials.push({ kind: 'text', property: 'label' });
  if (has('help')) essentials.push({ kind: 'text', property: 'help', long: true });
  if (has('required')) essentials.push({ kind: 'switch', property: 'required' });
  if (has('placeholder')) essentials.push({ kind: 'text', property: 'placeholder' });
  // A grid's rows, then its columns, which are its options (IA 5.1: choice fields add their options to Essentials).
  if (has('rows')) essentials.push({ kind: 'rows' });
  if (has('options')) essentials.push({ kind: 'options' });
  essentials.push(...shownSettings(type, 'essentials'));

  const validation: Item[] = [...shownSettings(type, 'validation')];
  if (has('patternCheck')) validation.push({ kind: 'pattern' });
  if (has('uniquePerFormCheck')) validation.push({ kind: 'unique' });
  if (has('matchesFieldCheck')) validation.push({ kind: 'matches' });
  validation.push(...type.messages.map((message): Item => ({ kind: 'message', message })));

  const appearance: Item[] = [];
  if (has('hideLabel')) appearance.push({ kind: 'switch', property: 'hideLabel' });
  if (groupColumns > 1) appearance.push({ kind: 'span', columns: groupColumns });
  appearance.push(...shownSettings(type, 'appearance'));

  const advanced: Item[] = [];
  if (type.holdsValue) advanced.push({ kind: 'alias' });
  if (has('defaultValue')) advanced.push({ kind: 'defaultValue' });
  if (has('prefill')) advanced.push({ kind: 'prefill' });
  if (has('autocomplete')) advanced.push({ kind: 'plain', property: 'autocomplete' });
  if (type.holdsValue) {
    advanced.push({ kind: 'switch', property: 'sensitive' });
    advanced.push({ kind: 'switch', property: 'includeInEmails' });
    advanced.push({ kind: 'keep' });
  }
  advanced.push(...shownSettings(type, 'advanced'));
  advanced.push({ kind: 'plain', property: 'cssClass' });

  return sections({ essentials, validation, appearance, advanced }, has('visibilityRules'));
}

/** A group's sections: its title, columns and style, and its rules. */
export function groupSections(): Section[] {
  return sections({ essentials: [{ kind: 'text', property: 'title' }, { kind: 'columns' }, { kind: 'style' }] }, true);
}

/** A page's sections: its title, and its rules. */
export function pageSections(): Section[] {
  return sections({ essentials: [{ kind: 'text', property: 'title' }] }, true);
}

/**
 * Whether a field takes several answers, so its default value is a list: choices and files, and a type with a
 * `multiple` setting where the field turns it on, as the server reads a default.
 */
export function takesSeveral(type: FieldTypeResponseModel, field: DesignField): boolean {
  if (type.ruleKind === 'choices' || type.ruleKind === 'files') return true;
  const settings = (field['settings'] as Readonly<Record<string, unknown>> | undefined) ?? {};
  return type.multipleSetting && settings['multiple'] === true;
}

/** The items a section shows before "More options", and the rest. */
export function splitItems(items: readonly Item[]): {
  readonly shown: readonly Item[];
  readonly more: readonly Item[];
} {
  if (items.length <= MOST_SHOWN) return { shown: items, more: [] };
  return { shown: items.slice(0, MOST_SHOWN), more: items.slice(MOST_SHOWN) };
}

const hasOwnText = (texts: LocalizedText | null | undefined, culture: string) =>
  Boolean(texts) && keyIn(texts, culture) !== '';

const hasDefaultText = (texts: LocalizedText | null | undefined) =>
  Object.values(texts ?? {}).some((text) => text.trim() !== '');

/**
 * Whether a field lacks a text in a language that is not the default: one of its texts (label, help, placeholder, a
 * message, a text setting) is written, but not in that language, so visitors in it read the default's (IA 5.1).
 */
export function untranslated(field: DesignField, type: FieldTypeResponseModel | undefined, culture: string): boolean {
  const lacks = (texts: LocalizedText | null | undefined) => hasDefaultText(texts) && !hasOwnText(texts, culture);
  const own = field as Readonly<Record<string, unknown>>;
  const texts: (LocalizedText | null | undefined)[] = [
    field.label,
    own['help'] as LocalizedText | undefined,
    own['placeholder'] as LocalizedText | undefined,
    ...Object.values((own['messages'] as Readonly<Record<string, LocalizedText>> | undefined) ?? {}),
    // A field's own options, their descriptions, and a grid's rows (ADR-0172).
    ...itemsOf(field).flatMap((item) => [item.label, item.description]),
    ...rowsOf(field).map((row) => row.label),
  ];
  const settings = (own['settings'] as Readonly<Record<string, unknown>> | undefined) ?? {};
  const visit = (schema: readonly FieldSettingResponseModel[], values: Readonly<Record<string, unknown>>) => {
    for (const setting of schema) {
      const value = values[setting.name];
      if (setting.kind === 'localizedText' || setting.kind === 'localizedHtml') texts.push(value as LocalizedText);
      else if (setting.settings.length > 0 && value && typeof value === 'object')
        visit(setting.settings, value as Readonly<Record<string, unknown>>);
    }
  };
  visit(type?.settings ?? [], settings);
  return texts.some(lacks);
}
