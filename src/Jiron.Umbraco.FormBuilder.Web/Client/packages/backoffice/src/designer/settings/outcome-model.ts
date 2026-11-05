// The form's outcomes as the Settings view edits them (ROADMAP 10.1b, ADR-0185): the list as the definition holds it,
// what each outcome gives visitors (a message, a redirect, or a message and then a redirect) and whether a visitor can
// get it at all, an outcome as its sidebar edits it and as Update writes it back the way the server keeps it, and where
// a new one goes. Pure.
import type { DesignDefinition, DesignRule, LocalizedText } from '../definition';
import { textIn } from '../preview/field-models';
import { plainText } from '../rules/rule-list';

/** Where an outcome sends visitors: a content page or an address, at once or after its message. */
export interface DesignRedirect {
  readonly content?: string | null;
  readonly url?: string | null;
  readonly delaySeconds?: number | null;
  readonly [other: string]: unknown;
}

/** An outcome as the definition holds it; what the editor does not know about stays as it came. */
export interface DesignOutcome {
  readonly key: string;
  readonly kind?: string | null;
  readonly message?: LocalizedText | null;
  readonly showAnswers?: boolean | null;
  readonly redirect?: DesignRedirect | null;
  readonly rules?: readonly DesignRule[] | null;
  readonly [other: string]: unknown;
}

/** What visitors get from an outcome. */
export type OutcomeKind = 'message' | 'redirect' | 'messageThenRedirect';

export const OUTCOME_KINDS: readonly OutcomeKind[] = ['message', 'redirect', 'messageThenRedirect'];

/** The longest wait before a message's redirect, in seconds, as the server takes it. */
export const MOST_DELAY = 600;

/** The wait a message and then a redirect starts with. */
export const DEFAULT_DELAY = 5;

/** The longest address, as the server takes it. */
export const MOST_URL = 2000;

const isObject = (value: unknown): value is Readonly<Record<string, unknown>> =>
  value !== null && typeof value === 'object' && !Array.isArray(value);

const isText = (value: unknown): value is string => typeof value === 'string' && value !== '';

/** A record without these names. */
const drop = (record: Readonly<Record<string, unknown>>, ...names: string[]): Record<string, unknown> =>
  Object.fromEntries(Object.entries(record).filter(([name]) => !names.includes(name)));

/** The form's outcomes in their order. */
export function outcomesOf(definition: DesignDefinition): readonly DesignOutcome[] {
  const outcomes = definition['outcomes'];
  return Array.isArray(outcomes)
    ? outcomes.filter((one): one is DesignOutcome => isObject(one) && typeof one['key'] === 'string')
    : [];
}

/** The definition with these outcomes, left out where there are none, as the server writes an empty list. */
export function withOutcomes(definition: DesignDefinition, outcomes: readonly DesignOutcome[]): DesignDefinition {
  const rest = drop(definition, 'outcomes') as DesignDefinition;
  return outcomes.length > 0 ? { ...rest, outcomes } : rest;
}

/** What an outcome gives visitors: a message outcome with a redirect shows its message, then leaves. */
export function kindOf(outcome: DesignOutcome): OutcomeKind {
  if (outcome.kind === 'redirect') return 'redirect';
  return isObject(outcome.redirect) ? 'messageThenRedirect' : 'message';
}

/** The outcome's `use` rule, of which it has at most one; null for none. */
export function useRuleOf(outcome: DesignOutcome): DesignRule | null {
  return (outcome.rules ?? []).find((rule) => rule.effect === 'use') ?? null;
}

/** Whether a rule holds for every visitor: switched off, it is as none; "all of" nothing always matches. */
export function holdsAlways(rule: DesignRule | null): boolean {
  if (!rule || rule.enabled === false) return true;
  return rule.match !== 'any' && (rule.conditions ?? []).length === 0;
}

/**
 * Whether visitors can get each outcome: one with a rule when it matches, the first without one always ("always" when
 * it is the first of all, "otherwise" after outcomes with rules), and none after that one, which takes every visitor
 * the outcomes before it leave.
 */
export type Reach = 'ruled' | 'always' | 'otherwise' | 'never';

export function reachOf(outcomes: readonly DesignOutcome[]): Reach[] {
  let taken = false;
  return outcomes.map((outcome, index): Reach => {
    if (taken) return 'never';
    if (!holdsAlways(useRuleOf(outcome))) return 'ruled';
    taken = true;
    return index === 0 ? 'always' : 'otherwise';
  });
}

/** Whether the outcomes leave some visitors to the built-in thank-you message: none of them is for every visitor. */
export const leavesDefault = (outcomes: readonly DesignOutcome[]): boolean =>
  !reachOf(outcomes).some((reach) => reach === 'always' || reach === 'otherwise');

/** Where an outcome's redirect leads: a page by its key, an address, or nowhere yet. */
export type Target =
  | { readonly kind: 'page'; readonly key: string }
  | { readonly kind: 'address'; readonly url: string }
  | { readonly kind: 'none' };

export function targetOf(outcome: DesignOutcome): Target {
  const redirect = isObject(outcome.redirect) ? outcome.redirect : {};
  if (isText(redirect['content'])) return { kind: 'page', key: redirect['content'] };
  if (isText(redirect['url'])) return { kind: 'address', url: redirect['url'] };
  return { kind: 'none' };
}

/** An outcome's message in a language, as one line of plain text; '' where it has none, so visitors get the default. */
export function messageLine(outcome: DesignOutcome, culture: string): string {
  return plainText(textIn(outcome.message, culture));
}

/** An outcome as its sidebar edits it: every kind's settings, kept while another kind is chosen. */
export interface OutcomeEdit {
  readonly kind: OutcomeKind;
  readonly message: LocalizedText | null;
  readonly showAnswers: boolean;
  readonly leadsTo: 'page' | 'address';
  readonly content: string | null;
  readonly url: string;
  readonly delay: number;
  readonly rule: DesignRule | null;
}

/** The outcome's settings as its sidebar opens with them; a new outcome is a message, the default one until it says more. */
export function editOf(outcome: DesignOutcome | null): OutcomeEdit {
  const kind = outcome ? kindOf(outcome) : 'message';
  const target = outcome ? targetOf(outcome) : ({ kind: 'none' } as const);
  const delay = isObject(outcome?.redirect) ? outcome.redirect['delaySeconds'] : undefined;
  return {
    kind,
    message: outcome?.message ?? null,
    showAnswers: outcome?.showAnswers === true,
    leadsTo: target.kind === 'address' ? 'address' : 'page',
    content: target.kind === 'page' ? target.key : null,
    url: target.kind === 'address' ? target.url : '',
    delay: kind === 'messageThenRedirect' && typeof delay === 'number' ? delay : DEFAULT_DELAY,
    rule: outcome ? useRuleOf(outcome) : null,
  };
}

/**
 * The outcome Update keeps: what the chosen kind uses, as the server keeps it (no message or answers on a redirect, a
 * wait only before a message's redirect), with everything else the outcome held as it came. Its `use` rule takes the
 * place of the one it had.
 */
export function outcomeOf(edit: OutcomeEdit, base: DesignOutcome | null, key: string): DesignOutcome {
  const rest = base ? drop(base, 'kind', 'message', 'showAnswers', 'redirect', 'rules') : {};
  const kept = isObject(base?.redirect) ? drop(base.redirect, 'content', 'url', 'delaySeconds') : {};
  const leadsTo =
    edit.leadsTo === 'page'
      ? edit.content
        ? { content: edit.content }
        : {}
      : edit.url !== ''
        ? { url: edit.url }
        : {};
  const before = base?.rules ?? [];
  const at = before.findIndex((rule) => rule.effect === 'use');
  const rules =
    at < 0
      ? [...before, ...(edit.rule ? [edit.rule] : [])]
      : before.flatMap((rule, index) => (index !== at ? [rule] : edit.rule ? [edit.rule] : []));
  const message = edit.message && Object.keys(edit.message).length > 0 ? edit.message : null;
  return {
    ...rest,
    key,
    kind: edit.kind === 'redirect' ? 'redirect' : 'message',
    ...(edit.kind !== 'redirect' && message ? { message } : {}),
    ...(edit.kind !== 'redirect' ? { showAnswers: edit.showAnswers } : {}),
    ...(edit.kind !== 'message'
      ? { redirect: { ...kept, ...leadsTo, delaySeconds: edit.kind === 'messageThenRedirect' ? edit.delay : 0 } }
      : {}),
    ...(rules.length > 0 ? { rules } : {}),
  } as DesignOutcome;
}

/**
 * Where a new outcome goes: at the end, but one with a rule before the first outcome for every visitor, which takes
 * every visitor the outcomes before it leave: after it the new one would never be used.
 */
export function placeOfNew(outcomes: readonly DesignOutcome[], rule: DesignRule | null): number {
  if (holdsAlways(rule)) return outcomes.length;
  const first = outcomes.findIndex((outcome) => holdsAlways(useRuleOf(outcome)));
  return first < 0 ? outcomes.length : first;
}

/** The outcomes with one put in place of the outcome with its key, or added at `at` where none has it. */
export function withOutcome(
  outcomes: readonly DesignOutcome[],
  outcome: DesignOutcome,
  at = outcomes.length,
): DesignOutcome[] {
  const index = outcomes.findIndex((one) => one.key === outcome.key);
  if (index >= 0) return outcomes.map((one, place) => (place === index ? outcome : one));
  return [...outcomes.slice(0, at), outcome, ...outcomes.slice(at)];
}

/** A setting of an outcome, by the box its sidebar edits it in. */
export type OutcomeSetting = 'kind' | 'message' | 'showAnswers' | 'leadsTo' | 'page' | 'address' | 'delay' | 'rule';

/** The setting a path within an outcome names (`message`, `redirect.url`, `rules[0]`); its kind for the outcome itself. */
export function settingAtPath(steps: readonly (string | number)[]): OutcomeSetting {
  const [first, second] = steps;
  switch (first) {
    case 'message':
      return 'message';
    case 'showAnswers':
      return 'showAnswers';
    case 'rules':
      return 'rule';
    case 'redirect':
      return second === 'content'
        ? 'page'
        : second === 'url'
          ? 'address'
          : second === 'delaySeconds'
            ? 'delay'
            : 'leadsTo';
    default:
      return 'kind';
  }
}

/**
 * Where a setting shows as the sidebar draws the outcome now: its own box, or, where the kind or the choice of a page
 * or an address hides it, the choice that does.
 */
export function drawnSetting(setting: OutcomeSetting, edit: OutcomeEdit): OutcomeSetting {
  switch (setting) {
    case 'message':
    case 'showAnswers':
      return edit.kind === 'redirect' ? 'kind' : setting;
    case 'leadsTo':
    case 'page':
    case 'address':
      if (edit.kind === 'message') return 'kind';
      return setting === 'leadsTo' || (setting === 'page') === (edit.leadsTo === 'page') ? setting : 'leadsTo';
    case 'delay':
      return edit.kind === 'messageThenRedirect' ? 'delay' : 'kind';
    default:
      return setting;
  }
}

/** How much of a message an outcome's name says. */
export const EXCERPT = 60;

/**
 * A text as an outcome's name says it: whole where it is short, else cut at the last word that fits, with an ellipsis,
 * so the row wraps a name rather than cutting it.
 */
export function excerptOf(text: string, most = EXCERPT): string {
  const words = text.trim();
  if (words.length <= most) return words;
  const cut = words.slice(0, most + 1);
  const space = cut.lastIndexOf(' ');
  return `${(space > most / 2 ? cut.slice(0, space) : words.slice(0, most)).trimEnd()}${String.fromCharCode(0x2026)}`;
}
