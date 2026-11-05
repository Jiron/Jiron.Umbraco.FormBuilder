// The form's actions as the Actions view edits them (ROADMAP 10.2a, IA 5.3, ADR-0188): one list in the definition, in
// the order they run, shown by trigger; whether a trigger runs at all on the form; what each action does in one line;
// the choices of what a new action does, from the site's action types; and an action as its sidebar edits it and as Add
// or Update writes it back the way the server keeps it. Pure.
import type { ActionTypeResponseModel } from '../../api';
import type { DesignDefinition, DesignRule } from '../definition';

/** An action as the definition holds it; what the editor does not know about stays as it came. */
export interface DesignAction {
  readonly key: string;
  readonly type: string;
  readonly name?: string | null;
  readonly enabled?: boolean | null;
  readonly trigger?: string | null;
  readonly stopOnFailure?: boolean | null;
  readonly rules?: readonly DesignRule[] | null;
  readonly settings?: Readonly<Record<string, unknown>> | null;
  readonly [other: string]: unknown;
}

/** What starts an action, in the order the view shows them (copy.md: Submitted, Approved, Rejected, Verified). */
export const TRIGGERS = ['submitted', 'approved', 'rejected', 'verified'] as const;

export type Trigger = (typeof TRIGGERS)[number];

/** The setting a trigger needs on the form before it runs: Rejected manual approval, Verified email verification. */
export type TriggerNeed = 'approval' | 'verification';

const isObject = (value: unknown): value is Readonly<Record<string, unknown>> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

const isText = (value: unknown): value is string => typeof value === 'string' && value !== '';

/** A record without these names. */
const drop = (record: Readonly<Record<string, unknown>>, ...names: string[]): Record<string, unknown> =>
  Object.fromEntries(Object.entries(record).filter(([name]) => !names.includes(name)));

/** The form's actions in the order they run. */
export function actionsOf(definition: DesignDefinition): readonly DesignAction[] {
  const actions = definition['actions'];
  return Array.isArray(actions)
    ? actions.filter(
        (one): one is DesignAction =>
          isObject(one) && typeof one['key'] === 'string' && typeof one['type'] === 'string',
      )
    : [];
}

/** The definition with these actions, left out where there are none, as the server writes an empty list. */
export function withActions(definition: DesignDefinition, actions: readonly DesignAction[]): DesignDefinition {
  const rest = drop(definition, 'actions') as DesignDefinition;
  return actions.length > 0 ? { ...rest, actions } : rest;
}

/** What starts the action: Submitted where it names none, or one the view does not know. */
export function triggerOf(action: DesignAction): Trigger {
  const trigger = action.trigger;
  return (TRIGGERS as readonly string[]).includes(trigger ?? '') ? (trigger as Trigger) : 'submitted';
}

/** Whether the action runs: it does unless it is switched off. */
export const isOn = (action: DesignAction): boolean => action.enabled !== false;

/** The actions of one trigger, in the order they run. */
export function inTrigger(actions: readonly DesignAction[], trigger: Trigger): DesignAction[] {
  return actions.filter((action) => triggerOf(action) === trigger);
}

/**
 * The list with the action at `from` among its trigger's moved to `to` among them; the other triggers' actions keep
 * their places in the one list, as only the order within a trigger is the order an event's actions run in.
 */
export function movedWithin(
  actions: readonly DesignAction[],
  trigger: Trigger,
  from: number,
  to: number,
): DesignAction[] {
  const slots = actions.flatMap((action, at) => (triggerOf(action) === trigger ? [at] : []));
  const group = slots.map((slot) => actions[slot] as DesignAction);
  const [taken] = group.splice(from, 1);
  if (taken === undefined) return [...actions];
  group.splice(Math.max(0, Math.min(to, group.length)), 0, taken);
  const next = [...actions];
  slots.forEach((slot, at) => (next[slot] = group[at] as DesignAction));
  return next;
}

/** The list with an action replaced by its key, or a new one put at the end of its trigger's. */
export function withAction(actions: readonly DesignAction[], action: DesignAction): DesignAction[] {
  const at = actions.findIndex((one) => one.key === action.key);
  if (at >= 0) return actions.map((one, place) => (place === at ? action : one));
  const trigger = triggerOf(action);
  let place = actions.length;
  for (let at = actions.length - 1; at >= 0; at--) {
    if (triggerOf(actions[at] as DesignAction) !== trigger) continue;
    place = at + 1;
    break;
  }
  return [...actions.slice(0, place), action, ...actions.slice(place)];
}

/** The list with an action moved to another trigger, at the end of that trigger's; as it was for the one it has. */
export function withTrigger(actions: readonly DesignAction[], key: string, trigger: Trigger): DesignAction[] {
  const action = actions.find((one) => one.key === key);
  if (!action || triggerOf(action) === trigger) return [...actions];
  return withAction(
    actions.filter((one) => one.key !== key),
    { ...action, trigger },
  );
}

/**
 * The list as Add or Update keeps an action: in its place, or at the end of its trigger's where it is new or its trigger
 * changed; then at `at` among its trigger's where a place was chosen.
 */
export function keptIn(actions: readonly DesignAction[], kept: DesignAction, at: number | null = null): DesignAction[] {
  const before = actions.find((one) => one.key === kept.key);
  const trigger = triggerOf(kept);
  const placed = before && triggerOf(before) !== trigger ? withTrigger(actions, kept.key, trigger) : actions;
  const next = withAction(placed, kept);
  if (at === null) return next;
  const from = inTrigger(next, trigger).findIndex((one) => one.key === kept.key);
  return from === at ? next : movedWithin(next, trigger, from, at);
}

/** Where an action is among a trigger's, and how many there are with it: where it is, or after the others. */
export function placeOf(
  actions: readonly DesignAction[],
  key: string,
  trigger: Trigger,
): { readonly at: number; readonly count: number } {
  const group = inTrigger(actions, trigger);
  const at = group.findIndex((one) => one.key === key);
  return at >= 0 ? { at, count: group.length } : { at: group.length, count: group.length + 1 };
}

/**
 * The list with a copy of an action right after it: its own key and its rule's, from `newKey`, and its name as
 * `copyName` makes it.
 */
export function duplicated(
  actions: readonly DesignAction[],
  key: string,
  newKey: () => string,
  copyName: (name: string) => string,
): DesignAction[] {
  const at = actions.findIndex((one) => one.key === key);
  const action = actions[at];
  if (!action) return [...actions];
  const rules = (action.rules ?? []).map((rule) => ({ ...rule, key: newKey() }));
  const copy: DesignAction = {
    ...action,
    key: newKey(),
    ...(isText(action.name) ? { name: copyName(action.name) } : {}),
    ...(rules.length > 0 ? { rules } : {}),
  };
  return [...actions.slice(0, at + 1), copy, ...actions.slice(at + 1)];
}

/** The action's `run` rule, of which it has at most one; null for none. */
export function runRuleOf(action: DesignAction): DesignRule | null {
  return (action.rules ?? []).find((rule) => rule.effect === 'run') ?? null;
}

/** Whether the trigger runs on this form, or the setting it needs first. */
export function triggerNeed(definition: DesignDefinition, trigger: Trigger): TriggerNeed | null {
  const settings = definition['settings'];
  const submission = isObject(settings) && isObject(settings['submission']) ? settings['submission'] : {};
  if (trigger === 'rejected' && submission['approval'] !== 'manual') return 'approval';
  if (trigger === 'verified' && !isObject(submission['verification'])) return 'verification';
  return null;
}

/** Whether entries are approved as they come in, so the Approved trigger runs for each. */
export function approvesEachEntry(definition: DesignDefinition): boolean {
  return triggerNeed(definition, 'rejected') === 'approval';
}

// ---- What an action does, in one line ----------------------------------------------------------------------------

/** A line of the Actions area's words: its key, without `jfbActions_`, and what it says. */
export interface Line {
  readonly term: string;
  readonly args: readonly unknown[];
}

const line = (term: string, ...args: unknown[]): Line => ({ term, args });

const texts = (value: unknown): string[] =>
  Array.isArray(value) ? value.filter(isText).map((one) => one.trim()) : isText(value) ? [value.trim()] : [];

/** An address's host, for a webhook's line: the whole text where it reads as none, as one with merge tags may. */
function hostOf(url: string): string {
  try {
    return new URL(url).host || url;
  } catch {
    return url;
  }
}

/**
 * What a built-in action does, in one line (IA 5.3): an email's recipients, where a webhook goes, what a change of the
 * state does; null for a site's own type, which the view names by its type.
 */
export function summaryOf(action: DesignAction, fieldName: (key: string) => string | undefined): Line | null {
  const settings = isObject(action.settings) ? action.settings : {};
  switch (action.type) {
    case 'email': {
      const to = texts(settings['to']);
      if (to.length > 0) return line('summary_emailTo', to);
      const field = isText(settings['toField']) ? fieldName(settings['toField']) : undefined;
      if (field !== undefined) return line('summary_emailToField', field);
      if (settings['toCreator'] === true) return line('summary_emailToCreator');
      if (isObject(settings['recipientMap'])) return line('summary_emailRouted');
      return line('summary_emailNoOne');
    }
    case 'webhook': {
      if (settings['target'] === 'slack') return line('summary_slack');
      if (settings['target'] === 'teams') return line('summary_teams');
      const url = isText(settings['url']) ? settings['url'].trim() : '';
      return url === '' ? line('summary_webhookNowhere') : line('summary_webhook', hostOf(url));
    }
    case 'setState': {
      const state = settings['state'];
      return line(state === 'rejected' ? 'summary_reject' : state === 'spam' ? 'summary_spam' : 'summary_approve');
    }
    case 'content':
      return line(settings['publish'] === true ? 'summary_contentPublished' : 'summary_content');
    default:
      return null;
  }
}

// ---- What a new action does ----------------------------------------------------------------------------------------

/** A choice of what a new action does: a preset of a type, or a type without presets as itself (ADR-0188). */
export interface ActionChoice {
  /** The choice's own id: the type's alias, and the preset's place among its presets. */
  readonly id: string;
  readonly type: string;
  /** Its name: plain text, or a `#` key of the backoffice's words. */
  readonly name: string;
  /** One of Umbraco's icons, the type's. */
  readonly icon: string;
  /** The settings the action starts with. */
  readonly settings: Readonly<Record<string, unknown>>;
}

/** A preset's settings, as the server serves them in JSON; none where they read as no object. */
function parsedSettings(json: string): Readonly<Record<string, unknown>> {
  try {
    const parsed: unknown = JSON.parse(json);
    return isObject(parsed) ? parsed : {};
  } catch {
    return {};
  }
}

/** What a new action may do, in the registry's order: every preset of every type, a type without presets as itself. */
export function choicesOf(types: readonly ActionTypeResponseModel[]): ActionChoice[] {
  return types.flatMap((type) =>
    type.presets.length > 0
      ? type.presets.map((preset, at) => ({
          id: `${type.alias}:${at}`,
          type: type.alias,
          name: preset.name,
          icon: type.icon,
          settings: parsedSettings(preset.settings),
        }))
      : [{ id: type.alias, type: type.alias, name: type.name, icon: type.icon, settings: {} }],
  );
}

// ---- An action as its sidebar edits it -----------------------------------------------------------------------------

/** An action as its sidebar edits it. */
export interface ActionEdit {
  readonly key: string;
  readonly type: string;
  readonly name: string;
  /** Whether the name was typed: until it is, a new action's name follows the choice of what it does. */
  readonly named: boolean;
  /** What a new action does, by its choice's id; null for an action that has its type. */
  readonly choice: string | null;
  readonly trigger: Trigger;
  readonly enabled: boolean;
  readonly stopOnFailure: boolean;
  readonly settings: Readonly<Record<string, unknown>>;
  readonly rule: DesignRule | null;
  /** What else the action held, kept as it came. */
  readonly rest: Readonly<Record<string, unknown>>;
}

/** An action of the definition as its sidebar edits it. */
export function editOf(action: DesignAction): ActionEdit {
  return {
    key: action.key,
    type: action.type,
    name: isText(action.name) ? action.name : '',
    named: true,
    choice: null,
    trigger: triggerOf(action),
    enabled: isOn(action),
    stopOnFailure: action.stopOnFailure === true,
    settings: isObject(action.settings) ? action.settings : {},
    rule: runRuleOf(action),
    rest: drop(action, 'key', 'type', 'name', 'enabled', 'trigger', 'stopOnFailure', 'rules', 'settings'),
  };
}

/** A new action as its sidebar starts it, from a choice of what it does, its name the choice's own, said; Submitted. */
export function newEdit(key: string, choice: ActionChoice, name: string): ActionEdit {
  return {
    key,
    type: choice.type,
    name,
    named: false,
    choice: choice.id,
    trigger: 'submitted',
    enabled: true,
    stopOnFailure: false,
    settings: choice.settings,
    rule: null,
    rest: {},
  };
}

/** A new action's edit with another choice: its settings replaced, its name the choice's own until one was typed. */
export function withChoice(edit: ActionEdit, choice: ActionChoice, name: string): ActionEdit {
  return {
    ...edit,
    type: choice.type,
    choice: choice.id,
    settings: choice.settings,
    name: edit.named ? edit.name : name,
  };
}

/**
 * The action as Update or Add writes it, the way the server keeps it: every value the server writes, the name only
 * where there is one, the rule only where there is one, the settings without what is unset.
 */
export function actionOf(edit: ActionEdit): DesignAction {
  const name = edit.name.trim();
  const settings = Object.fromEntries(Object.entries(edit.settings).filter(([, value]) => value !== undefined));
  return {
    ...edit.rest,
    key: edit.key,
    type: edit.type,
    ...(name !== '' ? { name } : {}),
    enabled: edit.enabled,
    trigger: edit.trigger,
    stopOnFailure: edit.stopOnFailure,
    ...(edit.rule ? { rules: [edit.rule] } : {}),
    ...(Object.keys(settings).length > 0 ? { settings } : {}),
  };
}

/** The edit with one setting, at its path ("headers", "format.decimals"), set; undefined takes it away. */
export function withSettingAt(edit: ActionEdit, path: string, value: unknown): ActionEdit {
  const steps = path.split('.');
  const set = (record: Readonly<Record<string, unknown>>, at: number): Readonly<Record<string, unknown>> => {
    const step = steps[at] as string;
    if (at === steps.length - 1) return value === undefined ? drop(record, step) : { ...record, [step]: value };
    const inner = set(isObject(record[step]) ? record[step] : {}, at + 1);
    return Object.keys(inner).length > 0 ? { ...record, [step]: inner } : drop(record, step);
  };
  return { ...edit, settings: set(edit.settings, 0) };
}

/** A setting's value at its path in the edit's settings, as stored. */
export function settingAt(edit: ActionEdit, path: string): unknown {
  let value: unknown = edit.settings;
  for (const step of path.split('.')) value = isObject(value) ? value[step] : undefined;
  return value;
}

// ---- Fix it --------------------------------------------------------------------------------------------------------

/** Where in an action's sidebar a problem's path leads: one of its own boxes, a setting of its type, or the action. */
export type ActionSetting =
  | { readonly kind: 'action' }
  | { readonly kind: 'name' }
  | { readonly kind: 'trigger' }
  | { readonly kind: 'stopOnFailure' }
  | { readonly kind: 'rule' }
  | { readonly kind: 'setting'; readonly path: string };

/** The box a path within an action names (`settings.to`, `settings.headers[2]`, `rules[0]`); the action for its own. */
export function settingAtPath(steps: readonly (string | number)[]): ActionSetting {
  const [first, ...rest] = steps;
  switch (first) {
    case 'name':
      return { kind: 'name' };
    case 'trigger':
      return { kind: 'trigger' };
    case 'stopOnFailure':
      return { kind: 'stopOnFailure' };
    case 'rules':
      return { kind: 'rule' };
    case 'settings': {
      // A setting's own steps: a nested setting's names, up to the first item of a list.
      const end = rest.findIndex((step) => typeof step !== 'string');
      const names = end < 0 ? rest : rest.slice(0, end);
      return names.length > 0 ? { kind: 'setting', path: names.join('.') } : { kind: 'action' };
    }
    default:
      return { kind: 'action' };
  }
}
