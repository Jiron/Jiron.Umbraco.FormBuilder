// What rules are said with in the designer (ROADMAP 9.5a, ADR-0175): each element named as the canvas names it, a grid's
// rows by the grid and the row, the field types' facts the rules model reads, and a field's options' labels, its own or
// those its source gave as the server resolved them (ADR-0172).
import type { Kind } from '@jfb/logic';
import type { FieldTypeResponseModel } from '../../api';
import { allFields, type DesignDefinition, type DesignField, type LocalizedText } from '../definition';
import { textIn, type ResolvedOption } from '../preview/field-models';
import { fieldKinds, type TypeFacts } from './rule-draft';
import type { RuleWordsContext } from './rule-words';

type Term = (key: string, ...args: unknown[]) => string;

/** A field's name as the canvas says it: its label, a heading's text, its alias, else "Untitled field". */
export function fieldName(field: DesignField, culture: string, term: Term): string {
  const heading =
    field.type === 'heading' ? textIn((field['settings'] as { text?: LocalizedText } | undefined)?.text, culture) : '';
  return textIn(field.label, culture) || heading || field.alias || term('jfbDesigner_untitledField');
}

/** The name of every page, group, field and grid row of a definition, by key; a row is "Grid: Row". */
export function elementNames(definition: DesignDefinition, culture: string, term: Term): Record<string, string> {
  const names: Record<string, string> = {};
  definition.pages.forEach((page, pageIndex) => {
    names[page.key] = textIn(page.title, culture) || term('jfbDesigner_pageNumber', pageIndex + 1);
    page.groups.forEach((group, groupIndex) => {
      names[group.key] = textIn(group.title, culture) || term('jfbDesigner_groupNumber', groupIndex + 1);
    });
  });
  for (const field of allFields(definition)) {
    const name = fieldName(field, culture, term);
    names[field.key] = name;
    (field.rows ?? []).forEach((row, index) => {
      names[row.key] = `${name}: ${textIn(row.label, culture) || term('jfbDesigner_newRow', index + 1)}`;
    });
  }
  return names;
}

/** A date or a date and time as the backoffice's language writes it, from its wire form; any other value as given. */
export function valueText(culture: string, kind: Kind, text: string): string {
  const date = kind === 'date' ? new Date(`${text}T00:00:00Z`) : kind === 'dateTime' ? new Date(`${text}Z`) : null;
  if (!date || Number.isNaN(date.getTime())) return text;
  return new Intl.DateTimeFormat(culture || 'en', {
    dateStyle: 'medium',
    ...(kind === 'dateTime' ? { timeStyle: 'short' } : {}),
    timeZone: 'UTC',
  }).format(date);
}

/** What the rules model reads of each field type. */
export function typeFactsOf(types: Readonly<Record<string, FieldTypeResponseModel>>): Record<string, TypeFacts> {
  const facts: Record<string, TypeFacts> = {};
  for (const [alias, type] of Object.entries(types))
    facts[alias] = {
      kind: (type.ruleKind ?? null) as TypeFacts['kind'],
      features: type.features,
      multipleSetting: type.multipleSetting,
      serverOnlyDefault: type.serverOnlyDefault,
      holdsValue: type.holdsValue,
    };
  return facts;
}

/** A field's own option's label, or a grid row's column's; the value itself where the field takes options elsewhere. */
export function ownOptionLabel(definition: DesignDefinition, key: string, value: string, culture: string): string {
  const field = allFields(definition).find((one) => one.key === key || (one.rows ?? []).some((row) => row.key === key));
  const items = (
    field?.['options'] as { items?: readonly { value?: unknown; label?: LocalizedText | null }[] } | undefined
  )?.items;
  const item = items?.find((one) => one.value === value);
  return (item && textIn(item.label, culture)) || value;
}

/**
 * The words of rules for the inspector: names as the canvas says them, a field's options by their labels, its own or,
 * for a field that takes them from a source, those the server resolved ("Yes", never "yes").
 */
export function inspectorRuleWords(
  definition: DesignDefinition,
  types: Readonly<Record<string, FieldTypeResponseModel>>,
  culture: string,
  languages: readonly { readonly culture: string; readonly name: string }[],
  term: Term,
  resolved: ReadonlyMap<string, readonly ResolvedOption[]> = new Map(),
): RuleWordsContext {
  const names = elementNames(definition, culture, term);
  const facts = typeFactsOf(types);
  const kinds = fieldKinds(definition, (type) => facts[type]);
  return {
    term,
    nameOf: (key) => names[key] ?? key,
    kindOf: (field) => kinds[field],
    optionOf: (field, value) => {
      const own = ownOptionLabel(definition, field, value, culture);
      return own !== value ? own : resolved.get(field)?.find((option) => option.value === value)?.label || value;
    },
    languageOf: (code) => languages.find((language) => language.culture === code)?.name ?? code,
    valueOf: (kind, text) => valueText(culture, kind, text),
    textOf: (texts) => textIn(texts, culture),
  };
}
