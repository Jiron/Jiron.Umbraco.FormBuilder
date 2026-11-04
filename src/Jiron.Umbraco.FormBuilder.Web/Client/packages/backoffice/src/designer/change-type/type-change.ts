// Changing a field's type (ROADMAP 9.4d, ADR-0059, ADR-0174): what a change of the server's map does to one field of
// the draft, and the field once changed. The preview names what the field has that goes: its values of settings the
// new type lacks or whose rules don't take them, the values a lock of the new type sets instead, the common properties
// it uses, and its own messages for errors the new type never raises; what it needs before it publishes; and the
// conditions that name it with an operator the new type doesn't take. Those conditions stay as they are, and the form
// check names them until they are changed, as publishing refuses them. Pure, so every part is tested without the DOM.
import type { FieldSettingResponseModel, FieldTypeChangeResponseModel, FieldTypeResponseModel } from '../../api';
import { allFields, type DesignDefinition, type DesignField, type DesignRule, type LocalizedText } from '../definition';
import { parseSetting, same, settingsOf, without } from '../inspector/setting-values';

type Plain = Readonly<Record<string, unknown>>;

/** A setting's value that a change takes from the field. */
export interface SettingLoss {
  /** The setting, as the type it belongs to describes it. */
  readonly setting: FieldSettingResponseModel;
  /** The field's own value. */
  readonly value: unknown;
}

/** What holds a condition: a page, group or field's rule, a validation rule, an action's or an outcome's. */
export type ConditionHolder =
  | { readonly kind: 'element'; readonly key: string }
  | { readonly kind: 'validationRule'; readonly key: string; readonly field: string }
  | { readonly kind: 'action'; readonly key: string; readonly name: string | null; readonly type: string }
  | { readonly kind: 'outcome'; readonly key: string; readonly index: number };

/** A condition that names the field with an operator the new type doesn't take. */
export interface ConditionLoss {
  readonly holder: ConditionHolder;
  readonly op: string;
}

/** What a change takes from one field and what it needs, as the change type dialog shows it. */
export interface TypeChangePreview {
  /** The type the field becomes. */
  readonly to: string;
  /** Its values of the old type's settings that go: the new type lacks them, or its rules don't take the value. */
  readonly settings: readonly SettingLoss[];
  /** Its values of settings that a lock of the new type sets instead (ADR-0060), with the new type's setting. */
  readonly locked: readonly SettingLoss[];
  /** The common properties it uses that the new type doesn't have, in the endpoint's order. */
  readonly properties: readonly string[];
  /** The error kinds whose own message it has that the new type never raises. */
  readonly messages: readonly string[];
  /** The conditions naming it whose operator the new type doesn't take; they stay until changed. */
  readonly conditions: readonly ConditionLoss[];
  /** Whether it needs options before it publishes. */
  readonly needsOptions: boolean;
  /**
   * The new type's settings it needs before it publishes: those the map names, and the required ones the change would
   * carry over that the field has no value for, or one the new type's rules don't take.
   */
  readonly needs: readonly FieldSettingResponseModel[];
  /** Whether the entries screen reads the new type's answers from other columns than the old type's (ADR-0058). */
  readonly newColumns: boolean;
}

const isRecord = (value: unknown): value is Plain =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

const checksOf = (field: DesignField): readonly Plain[] =>
  Array.isArray(field['validation']) ? (field['validation'] as Plain[]) : [];

const rulesOf = (field: DesignField): readonly DesignRule[] => field.rules ?? [];

const hasText = (value: unknown) =>
  isRecord(value) && Object.values(value).some((text) => typeof text === 'string' && text.trim() !== '');

/** A field without one of its properties. */
function withoutProperty(field: DesignField, name: string): DesignField {
  return without(field, name) as DesignField;
}

/** A field whose list keeps what `keep` takes, and without the list once it is empty. */
function keeping<T>(field: DesignField, name: string, list: readonly T[], keep: (item: T) => boolean): DesignField {
  const kept = list.filter(keep);
  if (kept.length === list.length) return field;
  return kept.length > 0 ? ({ ...field, [name]: kept } as DesignField) : withoutProperty(field, name);
}

const checkKind = (kind: string) => ({
  uses: (field: DesignField) => checksOf(field).some((check) => check['kind'] === kind),
  drop: (field: DesignField) => keeping(field, 'validation', checksOf(field), (check) => check['kind'] !== kind),
});

const plain = (name: string, uses: (value: unknown) => boolean = (value) => value !== undefined && value !== null) => ({
  uses: (field: DesignField) => uses(field[name]),
  drop: (field: DesignField) => withoutProperty(field, name),
});

/**
 * For each common property the field types endpoint names (ADR-0034), whether a field uses it and the field without
 * it, as the server writes a field that doesn't: a property at its default is left out.
 */
const PROPERTIES: Readonly<
  Record<string, { readonly uses: (field: DesignField) => boolean; readonly drop: (field: DesignField) => DesignField }>
> = {
  label: plain('label', hasText),
  hideLabel: plain('hideLabel', (value) => value === true),
  help: plain('help', hasText),
  placeholder: plain('placeholder', hasText),
  // Being required, always or under a rule that requires it.
  required: {
    uses: (field) => field['required'] === true || rulesOf(field).some((rule) => rule.effect === 'require'),
    drop: (field) =>
      keeping(withoutProperty(field, 'required'), 'rules', rulesOf(field), (rule) => rule.effect !== 'require'),
  },
  defaultValue: plain('defaultValue'),
  prefill: plain('prefill'),
  autocomplete: plain('autocomplete', (value) => typeof value === 'string' && value !== ''),
  patternCheck: checkKind('pattern'),
  matchesFieldCheck: checkKind('matchesField'),
  uniquePerFormCheck: checkKind('uniquePerForm'),
  visibilityRules: {
    uses: (field) => rulesOf(field).some((rule) => rule.effect === 'show' || rule.effect === 'hide'),
    drop: (field) =>
      keeping(field, 'rules', rulesOf(field), (rule) => rule.effect !== 'show' && rule.effect !== 'hide'),
  },
  options: plain('options'),
  // A number of places on the field's own options.
  places: {
    uses: (field) => itemsOf(field).some((item) => item['places'] !== undefined && item['places'] !== null),
    drop: (field) => {
      const options = field['options'];
      if (!isRecord(options) || !Array.isArray(options['items'])) return field;
      return { ...field, options: { ...options, items: itemsOf(field).map((item) => without(item, 'places')) } };
    },
  },
  rows: plain('rows', (value) => Array.isArray(value) && value.length > 0),
  childFields: plain('fields', (value) => Array.isArray(value) && value.length > 0),
};

function itemsOf(field: DesignField): readonly Plain[] {
  const options = field['options'];
  return isRecord(options) && Array.isArray(options['items']) ? (options['items'] as Plain[]) : [];
}

const messagesOf = (field: DesignField): Plain => (isRecord(field['messages']) ? field['messages'] : {});

/** Whether a stored value is one a setting's rules take, as the server holds a field's settings to them (ADR-0056). */
export function takes(setting: FieldSettingResponseModel, value: unknown): boolean {
  const { rules } = setting;
  if (value === undefined || value === null) return !rules.required;
  const text = (each: unknown) => typeof each === 'string' && parseSetting(setting, each).ok;
  switch (setting.kind) {
    case 'boolean':
      return typeof value === 'boolean';
    case 'wholeNumber':
      return typeof value === 'number' && Number.isInteger(value) && parseSetting(setting, String(value)).ok;
    case 'number':
    case 'text':
    case 'date':
    case 'time':
    case 'key':
      return text(value);
    case 'choice':
      return (
        typeof value === 'string' &&
        (setting.choices.length === 0 || setting.choices.some((choice) => choice.value === value)) &&
        (rules.allowedValues.length === 0 || rules.allowedValues.includes(value))
      );
    case 'localizedText':
    case 'localizedHtml': {
      if (!isRecord(value)) return false;
      const texts = Object.values(value);
      const fits = texts.every(
        (each) =>
          typeof each === 'string' &&
          (rules.maximumLength == null || each.length <= rules.maximumLength) &&
          (rules.minimumLength == null || each === '' || each.length >= rules.minimumLength),
      );
      return fits && (!rules.required || hasText(value));
    }
    case 'list':
      return (
        Array.isArray(value) &&
        value.every((item) => typeof item === 'string') &&
        parseSetting(setting, (value as string[]).join('\n')).ok
      );
    case 'nested':
      return (
        isRecord(value) &&
        Object.entries(value).every(([name, each]) => {
          const child = setting.settings.find((one) => one.name === name);
          return child !== undefined && takes(child, each);
        }) &&
        setting.settings.every((child) => !child.rules.required || Object.hasOwn(value, child.name))
      );
    default:
      // Any JSON, which only the type itself checks: what the designer can't check isn't carried over.
      return false;
  }
}

/**
 * The conditions in the definition that name a field as what they look at, with what holds each: the rules of pages,
 * groups and fields, the validation rules, the actions' and the outcomes' rules. An operand naming the field, as
 * "equals the answer of", is no condition on it.
 */
export function conditionsOn(definition: DesignDefinition, key: string): ConditionLoss[] {
  const found: ConditionLoss[] = [];
  const walk = (value: unknown, holder: ConditionHolder) => {
    if (Array.isArray(value)) {
      for (const item of value) walk(item, holder);
      return;
    }
    if (!isRecord(value)) return;
    if (value['field'] === key && typeof value['op'] === 'string') found.push({ holder, op: value['op'] });
    for (const item of Object.values(value)) walk(item, holder);
  };
  for (const page of definition.pages) {
    walk(page.rules, { kind: 'element', key: page.key });
    for (const group of page.groups) walk(group.rules, { kind: 'element', key: group.key });
  }
  for (const field of allFields(definition)) walk(field.rules, { kind: 'element', key: field.key });
  const list = (name: string): readonly Plain[] =>
    Array.isArray(definition[name]) ? (definition[name] as Plain[]) : [];
  for (const rule of list('validationRules'))
    walk(rule['conditions'], {
      kind: 'validationRule',
      key: String(rule['key']),
      field: typeof rule['field'] === 'string' ? rule['field'] : '',
    });
  for (const action of list('actions'))
    walk(action['rules'], {
      kind: 'action',
      key: String(action['key']),
      name: typeof action['name'] === 'string' && action['name'] !== '' ? action['name'] : null,
      type: String(action['type']),
    });
  list('outcomes').forEach((outcome, index) =>
    walk(outcome['rules'], { kind: 'outcome', key: String(outcome['key']), index }),
  );
  return found;
}

const settingNamed = (type: FieldTypeResponseModel | undefined, name: string) =>
  type?.settings.find((setting) => setting.name === name);

/** What a change takes from a field of the draft and what the field then needs. */
export function previewChange(
  definition: DesignDefinition,
  field: DesignField,
  from: FieldTypeResponseModel | undefined,
  to: FieldTypeResponseModel,
  change: FieldTypeChangeResponseModel,
): TypeChangePreview {
  const own = settingsOf(field);
  const settings: SettingLoss[] = [];
  for (const setting of from?.settings ?? []) {
    if (!Object.hasOwn(own, setting.name)) continue;
    const value = own[setting.name];
    const target = settingNamed(to, setting.name);
    const narrowedOut = change.narrowedSettings.includes(setting.name) && !(target && takes(target, value));
    if (change.lostSettings.includes(setting.name) || narrowedOut) settings.push({ setting, value });
  }
  const locked = change.lockedSettings.flatMap((name): SettingLoss[] => {
    const setting = settingNamed(to, name);
    if (!setting || !Object.hasOwn(own, name)) return [];
    // A lock of the very value the field has takes nothing from it; a hidden one's value stays on the server.
    const lockValue = setting.lock?.value;
    return lockValue !== undefined && lockValue !== null && same(lockValue, own[name])
      ? []
      : [{ setting, value: own[name] }];
  });
  const carried = new Set(Object.keys(settingsOf(changedField(field, to, change))));
  const unmet = [...change.keptSettings, ...change.narrowedSettings].filter(
    (name) => settingNamed(to, name)?.rules.required === true && !carried.has(name),
  );
  const needed = new Set([...change.neededSettings, ...unmet]);
  const messages = messagesOf(field);
  return {
    to: to.alias,
    settings,
    locked,
    properties: change.lostProperties.filter((property) => PROPERTIES[property]?.uses(field) === true),
    messages: change.lostMessages.filter((name) => hasText(messages[name])),
    conditions: conditionsOn(definition, field.key).filter((condition) => change.lostOperators.includes(condition.op)),
    needsOptions: change.needsOptions,
    // In the new type's order, as its inspector shows them.
    needs: to.settings.filter((setting) => needed.has(setting.name)),
    newColumns: !change.keepsColumns,
  };
}

/** Whether a preview takes nothing from the field and asks nothing of it. */
export const losesNothing = (preview: TypeChangePreview): boolean =>
  preview.settings.length === 0 &&
  preview.locked.length === 0 &&
  preview.properties.length === 0 &&
  preview.messages.length === 0 &&
  preview.conditions.length === 0 &&
  !preview.needsOptions &&
  preview.needs.length === 0;

/**
 * The field once changed: its new type; the settings the change keeps, and the narrowed ones whose value the new
 * type's rules take; without the common properties and own messages the new type lacks. A setting a lock of the new type
 * sets is left to the lock. Everything else stays as it was: the key, alias, label and help, and every condition.
 */
export function changedField(
  field: DesignField,
  to: FieldTypeResponseModel,
  change: FieldTypeChangeResponseModel,
): DesignField {
  const own = settingsOf(field);
  const settings: Record<string, unknown> = {};
  for (const name of change.keptSettings) if (Object.hasOwn(own, name)) settings[name] = own[name];
  for (const name of change.narrowedSettings) {
    const target = settingNamed(to, name);
    if (target && Object.hasOwn(own, name) && takes(target, own[name])) settings[name] = own[name];
  }
  let next = withoutProperty({ ...field, type: to.alias }, 'settings');
  if (Object.keys(settings).length > 0) next = { ...next, settings };
  for (const property of change.lostProperties) next = PROPERTIES[property]?.drop(next) ?? next;
  const messages = messagesOf(next);
  const keptMessages = Object.fromEntries(
    Object.entries(messages).filter(([name]) => !change.lostMessages.includes(name)),
  ) as Readonly<Record<string, LocalizedText>>;
  if (Object.keys(keptMessages).length !== Object.keys(messages).length)
    next =
      Object.keys(keptMessages).length > 0 ? { ...next, messages: keptMessages } : withoutProperty(next, 'messages');
  return next;
}
