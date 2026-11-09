// What the form check says of each problem (ROADMAP 9.5f, ADR-0181): the element it is on, by the name the designer
// gives it, and what is wrong in a sentence of the user's language, with what the sentence names read from the form at
// the problem's path: the text it is in, a theme, a member group, an alias. The codes whose words only the server has
// keep the server's sentence. Pure.
import type { FieldTypeResponseModel } from '../../api';
import { validationRulesOf, type DesignDefinition, type DesignField } from '../definition';
import { messageFieldOf } from '../rules/rule-list';
import { parsePath, type CheckIssue, type PathStep } from './form-check';

/** What the words need: the dictionaries, the field types, and the names of the form's elements. */
export interface CheckWordsContext {
  readonly term: (key: string, ...args: unknown[]) => string;
  /** A `#` key or a plain text, in the user's language. */
  readonly say: (text: string | null | undefined) => string;
  readonly types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The name of each page, group and field of the definition checked, by key, as elementNames gives them. */
  readonly names: Readonly<Record<string, string>>;
}

/** A problem in words: the element it is on, and what is wrong. */
export interface IssueWords {
  readonly name: string;
  readonly said: string;
}

/**
 * The codes whose words only the server has, which show its sentence: a type's own check of its settings, a value's
 * bounds or kind, a redirect's problem, and a formula's, which names the character it is at.
 */
export const SERVER_WORDS: ReadonlySet<string> = new Set([
  'setting_invalid',
  'invalid_value',
  'limit_exceeded',
  'invalid_redirect',
  'invalid_formula',
  'out_of_range',
]);

/** The codes the designer says in its own words, each with its `jfbCheck_code_` sentence. */
export const OWN_WORDS: ReadonlySet<string> = new Set([
  'fields_missing',
  'label_missing',
  'lock_replaces_value',
  'unknown_type',
  'setting_unknown',
  'setting_missing',
  'unknown_path',
  'unknown_filter',
  'field_after_text',
  'source_not_for_place',
  'filter_not_for_place',
  'too_many_tags',
  'too_many_filters',
  'too_long',
  'malformed_tag',
  'unclosed_tag',
  'tag_in_markup',
  'tag_before_path',
  'invalid_argument',
  'filter_not_for_context',
  'filter_after_encoding',
  'answers_in_url',
  'captcha_not_configured',
  'no_reply_field',
  'reply_field_unset',
  'required_can_hide',
  'page_unreachable',
  'consent_missing',
  'card_number_asked',
  'password_asked',
  'file_size_unset',
  'file_size_above_site',
  'file_type_not_taken',
  'options_can_fill',
  'limit_without_entries',
  'last_outcome_has_rule',
  'theme_missing',
  'content_missing',
  'redirect_unpublished',
  'media_missing',
  'member_group_missing',
  'dictionary_text_missing',
  'options_invalid',
  'property_not_supported',
  'part_missing',
  'field_not_allowed',
  'alias_invalid',
  'alias_reserved',
  'alias_duplicate',
  'name_invalid',
  'name_duplicate',
  'option_value_invalid',
  'option_value_duplicate',
  'unknown_field',
  'invalid_reference',
  'invalid_condition',
  'unknown_operator',
  'operator_not_for_kind',
  'invalid_operand',
  'unsafe_pattern',
  'too_deep',
  'constant_formula',
  'rule_effect_invalid',
  'rule_duplicate',
  'cycle',
  'duplicate_key',
  // The content and Set state actions' own (ADR-0192).
  'content_type_missing',
  'content_type_gone',
  'content_parent_missing',
  'content_name_missing',
  'content_sensitive_unknown',
  'content_mapping_invalid',
  'content_property_gone',
  'content_property_twice',
  'content_field_gone',
  'content_field_cannot_hold',
  'content_value_cannot_hold',
  'content_group_field',
  'state_already',
  'state_loop',
]);

/** The merge-tag problems, whose sentence names the text the tag is in. */
const IN_A_TEXT: ReadonlySet<string> = new Set([
  'unknown_path',
  'unknown_filter',
  'field_after_text',
  'source_not_for_place',
  'filter_not_for_place',
  'too_many_tags',
  'too_many_filters',
  'too_long',
  'malformed_tag',
  'unclosed_tag',
  'tag_in_markup',
  'invalid_argument',
  'filter_not_for_context',
  'filter_after_encoding',
]);

/** A field's properties by the inspector's names for them. */
const PROPERTY_TERMS: Readonly<Record<string, string>> = {
  label: 'jfbInspector_property_label',
  help: 'jfbInspector_property_help',
  placeholder: 'jfbInspector_property_placeholder',
  title: 'jfbInspector_property_title',
  required: 'jfbInspector_property_required',
  hideLabel: 'jfbInspector_property_hideLabel',
  sensitive: 'jfbInspector_property_sensitive',
  includeInEmails: 'jfbInspector_property_includeInEmails',
  alias: 'jfbInspector_property_alias',
  formerAliases: 'jfbInspector_property_alias',
  autocomplete: 'jfbInspector_property_autocomplete',
  cssClass: 'jfbInspector_property_cssClass',
  defaultValue: 'jfbInspector_property_defaultValue',
  prefill: 'jfbInspector_property_prefill',
  keep: 'jfbInspector_property_keep',
  span: 'jfbInspector_property_span',
  options: 'jfbInspector_property_options',
  rows: 'jfbInspector_property_rows',
};

/** The part a `part_missing` names, by the last name of its path. */
const PARTS: Readonly<Record<string, string>> = {
  pages: 'pages',
  groups: 'groups',
  rows: 'rows',
  fields: 'fields',
  options: 'options',
  items: 'options',
  list: 'list',
  memberGroups: 'memberGroups',
  captcha: 'captcha',
  redirect: 'redirect',
};

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The value at the steps of a path, or undefined where the definition has none. */
function valueAt(definition: DesignDefinition, steps: readonly PathStep[]): unknown {
  let value: unknown = definition;
  for (const step of steps) {
    if (value === null || typeof value !== 'object') return undefined;
    value = (value as Record<string | number, unknown>)[step];
  }
  return value;
}

/** Where a problem is: the name of what it is on, the field when it is on one, and the steps after it. */
interface Where {
  readonly name: string;
  readonly top: PathStep | undefined;
  readonly field: DesignField | null;
  readonly rest: readonly PathStep[];
}

function whereOf(definition: DesignDefinition, steps: readonly PathStep[], words: CheckWordsContext): Where {
  const [top, index, ...after] = steps;
  const names = words.names;
  const form = { name: words.term('jfbCheck_name_form'), top, field: null, rest: steps.slice(1) };
  switch (top) {
    case 'pages': {
      const page = typeof index === 'number' ? definition.pages[index] : undefined;
      if (!page) return form;
      const [groupsStep, groupIndex, ...inGroup] = after;
      const group = groupsStep === 'groups' && typeof groupIndex === 'number' ? page.groups[groupIndex] : undefined;
      if (!group) return { name: names[page.key] ?? '', top, field: null, rest: after };
      let fields: readonly DesignField[] = group.fields;
      let field: DesignField | null = null;
      let rest: readonly PathStep[] = inGroup;
      // Down through the fields, and the children of repeatable groups.
      while (rest[0] === 'fields' && typeof rest[1] === 'number' && fields[rest[1]]) {
        field = fields[rest[1]] ?? null;
        fields = field?.fields ?? [];
        rest = rest.slice(2);
      }
      return field
        ? { name: names[field.key] ?? '', top, field, rest }
        : { name: names[group.key] ?? '', top, field: null, rest };
    }
    case 'validationRules': {
      const rule = typeof index === 'number' ? validationRulesOf(definition)[index] : undefined;
      const field = rule ? messageFieldOf(definition, rule.field) : undefined;
      return field ? { name: names[field.key] ?? '', top, field: null, rest: after } : form;
    }
    case 'settings':
      return { name: words.term('jfbCheck_name_settings'), top, field: null, rest: steps.slice(1) };
    case 'outcomes': {
      const outcomes = Array.isArray(definition['outcomes']) ? (definition['outcomes'] as unknown[]) : [];
      const name =
        outcomes.length > 1 && typeof index === 'number'
          ? words.term('jfbCheck_name_outcome', index + 1)
          : words.term('jfbCheck_name_outcomes');
      return { name, top, field: null, rest: after };
    }
    case 'actions': {
      const action = valueAt(definition, steps.slice(0, 2)) as { name?: unknown } | undefined;
      const name =
        typeof action?.name === 'string' && action.name.trim() !== ''
          ? action.name
          : words.term('jfbCheck_name_action', typeof index === 'number' ? index + 1 : 1);
      return { name, top, field: null, rest: after };
    }
    default:
      return form;
  }
}

/** A type's setting by its name, and the nested setting's within it, in the user's language; the name where it has none. */
function settingName(field: DesignField | null, names: readonly PathStep[], words: CheckWordsContext): string {
  const [name, inner] = names;
  if (typeof name !== 'string') return '';
  const type = field && Object.hasOwn(words.types, field.type) ? words.types[field.type] : undefined;
  const setting = type?.settings.find((one) => one.name === name);
  const nested = typeof inner === 'string' ? setting?.settings.find((one) => one.name === inner) : undefined;
  return words.say((nested ?? setting)?.label) || (typeof inner === 'string' ? inner : name);
}

/** The text or setting the steps after an element name, in words; '' where they name the element itself. */
function placeOf(where: Where, words: CheckWordsContext): string {
  const [first, second, third, fourth] = where.rest;
  if (where.top === 'pages' || where.top === 'validationRules') {
    if (first === 'settings') return settingName(where.field, where.rest.slice(1), words);
    if (first === 'messages' && typeof second === 'string') return words.term(`jfbInspector_message_${second}`);
    if (first === 'options' && second === 'items' && typeof third === 'number')
      return words.term('jfbCheck_place_option', third + 1);
    if (first === 'rows' && typeof second === 'number') return words.term('jfbCheck_place_row', second + 1);
    if (first === 'validation' && fourth === undefined && third === 'pattern')
      return words.term('jfbInspector_property_pattern');
    if (first === 'validation' || first === 'message') return words.term('jfbCheck_place_message');
    if (first === 'description') return words.term('jfbCheck_place_description');
    return typeof first === 'string' && Object.hasOwn(PROPERTY_TERMS, first)
      ? words.term(PROPERTY_TERMS[first] ?? '')
      : '';
  }
  if (where.top === 'outcomes') {
    if (first === 'message') return words.term('jfbCheck_place_message');
    if (first === 'redirect' && second === 'url') return words.term('jfbCheck_place_redirect');
    return '';
  }
  if (where.top === 'settings') {
    if (first === 'submission' && second === 'entryTitle') return words.term('jfbCheck_place_entryTitle');
    if (first === 'privacy' && second === 'ip') return words.term('jfbCheck_place_ip');
    if (where.rest.includes('closedMessage')) return words.term('jfbCheck_place_message');
    if (first === 'submission' && (second === 'submitLabel' || second === 'nextLabel' || second === 'backLabel'))
      return words.term('jfbCheck_place_button');
    return '';
  }
  // An action's settings are named as its type names them, which the designer doesn't read: by their own name.
  if (where.top === 'actions' && first === 'settings' && typeof second === 'string') return second;
  return '';
}

/** The problem's specifics its sentence names, as the arguments of its `jfbCheck_code_` term. */
function argumentsOf(
  issue: CheckIssue,
  definition: DesignDefinition,
  steps: readonly PathStep[],
  where: Where,
  words: CheckWordsContext,
): unknown[] {
  const value = valueAt(definition, steps);
  const text = typeof value === 'string' ? value : '';
  const place = placeOf(where, words);
  if (IN_A_TEXT.has(issue.code)) return [place];
  switch (issue.code) {
    case 'lock_replaces_value':
    case 'setting_missing':
      return [place || settingName(where.field, where.rest.slice(1), words)];
    case 'setting_unknown':
      return [[...steps].reverse().find((step): step is string => typeof step === 'string' && step !== '') ?? ''];
    case 'unknown_type':
      return [where.top === 'actions' ? 'action' : where.top === 'settings' ? 'captcha' : 'field', text];
    case 'page_unreachable':
      return [typeof steps[1] === 'number' ? steps[1] + 1 : 1];
    case 'captcha_not_configured':
    case 'theme_missing':
    case 'alias_invalid':
    case 'alias_reserved':
    case 'alias_duplicate':
    case 'name_duplicate':
      return [text];
    case 'content_missing':
      return [where.top === 'outcomes'];
    case 'member_group_missing':
      // A group named by its key is one the site no longer has; one named by its name is said by it.
      return [GUID.test(text) ? '' : text];
    case 'dictionary_text_missing':
      return [text.replace(/^#/, ''), place];
    case 'limit_without_entries': {
      const store = valueAt(definition, ['settings', 'submission', 'store']);
      return [where.top === 'settings' || store === 'never'];
    }
    case 'property_not_supported':
      return [place];
    case 'part_missing': {
      const last = [...steps].reverse().find((step): step is string => typeof step === 'string');
      return [last && Object.hasOwn(PARTS, last) ? PARTS[last] : ''];
    }
    case 'state_loop':
      return [loopPartner(definition, steps)];
    default:
      return [];
  }
}

/** The name of the Set state action that moves an entry back to where the action at the path moves it from. */
function loopPartner(definition: DesignDefinition, steps: readonly PathStep[]): string {
  const actions = (definition['actions'] ?? []) as readonly Record<string, unknown>[];
  const at = steps[0] === 'actions' && typeof steps[1] === 'number' ? actions[steps[1]] : undefined;
  if (!at) return '';
  const stateOf = (action: Record<string, unknown>) =>
    (action['settings'] as Record<string, unknown> | undefined)?.['state'];
  const other = actions.find(
    (action) =>
      action !== at &&
      action['enabled'] !== false &&
      action['type'] === 'setState' &&
      action['trigger'] === stateOf(at) &&
      stateOf(action) === at['trigger'],
  );
  return typeof other?.['name'] === 'string' ? other['name'] : '';
}

/** A problem in words: the element it is on, by its name, and what is wrong, in the user's language. */
export function issueWords(issue: CheckIssue, definition: DesignDefinition, words: CheckWordsContext): IssueWords {
  const steps = parsePath(issue.path) ?? [];
  const where = whereOf(definition, steps, words);
  const name = issue.code === 'fields_missing' ? words.term('jfbCheck_name_fieldsMissing') : where.name;
  const said = OWN_WORDS.has(issue.code)
    ? words.term(`jfbCheck_code_${issue.code}`, ...argumentsOf(issue, definition, steps, where, words))
    : issue.message;
  return { name: name || words.term('jfbCheck_name_form'), said };
}
