// The Change type dialog's words (ROADMAP 9.4d, ADR-0174): a change's preview said in the backoffice's language. A
// setting is named by its label with the field's value, a text property with its text, a message by what it answers,
// and a condition by what holds it and its operator, so the editor can find each before the change. Pure, given the
// dictionary's terms, so it is tested without the DOM.
import type { DesignField, LocalizedText } from '../definition';
import { settingText } from '../inspector/setting-editor';
import { textIn } from '../preview/field-models';
import type { ChangeTypeWords } from './change-type.modal.token';
import type { ConditionHolder, TypeChangePreview } from './type-change';

/** What the words need from the backoffice. */
export interface WordsContext {
  /** A term of the dictionary, by its whole key: `jfbDesigner_changeTypeGoes`. */
  readonly term: (key: string, ...args: unknown[]) => string;
  /** A `#` key or a plain text, in the user's language. */
  readonly say: (text: string | null | undefined) => string;
  /** The language texts are shown in. */
  readonly culture: string;
  /** The name of a page, group or field of the form, as the designer names it. */
  readonly nameOf: (key: string) => string;
  /** The name of an action type. */
  readonly actionTypeName: (type: string) => string;
}

// The common properties a change can take, each by the words that name it; a text property shows its text too.
const PROPERTY_TERMS: Readonly<Record<string, string>> = {
  label: 'jfbInspector_property_label',
  hideLabel: 'jfbInspector_property_hideLabel',
  help: 'jfbInspector_property_help',
  placeholder: 'jfbInspector_property_placeholder',
  required: 'jfbDesigner_changeTypeRequired',
  defaultValue: 'jfbInspector_property_defaultValue',
  prefill: 'jfbInspector_property_prefill',
  autocomplete: 'jfbInspector_property_autocomplete',
  patternCheck: 'jfbInspector_property_pattern',
  matchesFieldCheck: 'jfbInspector_property_matches',
  uniquePerFormCheck: 'jfbInspector_property_unique',
  visibilityRules: 'jfbDesigner_changeTypeVisibilityRules',
  options: 'jfbInspector_property_options',
  places: 'jfbDesigner_changeTypePlaces',
  rows: 'jfbDesigner_changeTypeRows',
  childFields: 'jfbDesigner_changeTypeFields',
};

function propertyWords(context: WordsContext, field: DesignField, property: string): string {
  const name = PROPERTY_TERMS[property] ? context.term(PROPERTY_TERMS[property]) : property;
  const own = field as Readonly<Record<string, unknown>>;
  const value =
    property === 'label' || property === 'help' || property === 'placeholder'
      ? textIn(own[property] as LocalizedText | undefined, context.culture)
      : property === 'autocomplete' && typeof own['autocomplete'] === 'string'
        ? own['autocomplete']
        : property === 'patternCheck'
          ? patternOf(field)
          : '';
  return value ? context.term('jfbDesigner_changeTypeValue', name, value) : name;
}

function patternOf(field: DesignField): string {
  const checks = Array.isArray(field['validation']) ? (field['validation'] as Readonly<Record<string, unknown>>[]) : [];
  const pattern = checks.find((check) => check['kind'] === 'pattern')?.['pattern'];
  return typeof pattern === 'string' ? pattern : '';
}

// The inspector's own words for a setting's value: on and off, a date's limit, what is picked.
const wordsOf = (context: WordsContext) => ({
  say: context.say,
  term: (key: string, ...args: unknown[]) => context.term(`jfbInspector_${key}`, ...args),
  culture: context.culture,
});

function holderWords(context: WordsContext, holder: ConditionHolder, op: string): string {
  const said = context.term(`jfbRules_op_${op}`);
  switch (holder.kind) {
    case 'element':
      return context.term('jfbDesigner_changeTypeConditionElement', context.nameOf(holder.key), said);
    case 'validationRule':
      return context.term('jfbDesigner_changeTypeConditionCheck', context.nameOf(holder.field), said);
    case 'action':
      return context.term(
        'jfbDesigner_changeTypeConditionAction',
        holder.name ?? context.actionTypeName(holder.type),
        said,
      );
    case 'outcome':
      return context.term('jfbDesigner_changeTypeConditionOutcome', holder.index + 1, said);
  }
}

/**
 * A preview in words: what goes (settings with the field's values, properties, own messages), what the site sets
 * instead, what the field needs (options, settings, the conditions to change), and, where the form has entries that
 * hold the field and the new type keeps its answers elsewhere, what the entries screen does with older answers.
 */
export function changeWords(
  context: WordsContext,
  field: DesignField,
  preview: TypeChangePreview,
  hasEntries: boolean,
): ChangeTypeWords {
  const valued = (label: string, text: string) =>
    text ? context.term('jfbDesigner_changeTypeValue', context.say(label), text) : context.say(label);
  const settingWords = preview.settings.map(({ setting, value }) =>
    valued(setting.label, settingText(wordsOf(context), setting, value)),
  );
  // A hidden lock's value stays on the server, so it is named without one.
  const locked = preview.locked.map(({ setting }) => {
    const value = setting.lock?.value;
    return value === undefined || value === null
      ? context.say(setting.label)
      : valued(setting.label, settingText(wordsOf(context), setting, value));
  });
  return {
    goes: [
      ...settingWords,
      ...preview.properties.map((property) => propertyWords(context, field, property)),
      ...preview.messages.map((message) => context.term(`jfbInspector_message_${message}`)),
    ],
    locked,
    needs: [
      ...(preview.needsOptions ? [context.term('jfbDesigner_changeTypeOptions')] : []),
      ...preview.needs.map((setting) => context.say(setting.label)),
      ...preview.conditions.map(({ holder, op }) => holderWords(context, holder, op)),
    ],
    entries: hasEntries && preview.newColumns ? context.term('jfbDesigner_changeTypeEntries') : null,
  };
}
