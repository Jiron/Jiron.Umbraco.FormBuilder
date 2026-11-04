// A rule in words (ROADMAP 9.5a, 9.5c, ADR-0175): "Shown when Country is Other and Phone is empty". The builder's rows,
// the inspector's list of an element's rules, the rules overview and the chips on the canvas all say rules with these
// words, so a rule reads the same wherever it shows. Pure: what the words need comes in a context.
import { parseRelative, type Kind, type Operand } from '@jfb/logic';
import type { LocalizedText } from '../definition';
import { EFFECTS, otherFieldOf, relativeOf } from './rule-draft';

/** What the words need of the backoffice and the form. */
export interface RuleWordsContext {
  readonly term: (key: string, ...args: unknown[]) => string;
  /** A page's, group's or field's name, by its key. */
  readonly nameOf: (key: string) => string;
  /** The kind a field's answers compare as, by its key. */
  readonly kindOf: (field: string) => Kind | undefined;
  /** An option's label, by its field's key and its value; the value itself where the label is not known. */
  readonly optionOf: (field: string, value: string) => string;
  /** A language's name, by its culture. */
  readonly languageOf: (culture: string) => string;
  /** A number, date, time or date and time as the backoffice's language writes it, from its wire form. */
  readonly valueOf: (kind: Kind, text: string) => string;
  /** A text the editor wrote (a validation rule's message), in the language the designer shows. */
  readonly textOf: (texts: LocalizedText | null | undefined) => string;
}

/** What a condition looks at and how it compares, as a condition of the definition holds it. */
export interface WordsCondition {
  readonly field?: string | null | undefined;
  readonly context?: string | null | undefined;
  readonly op?: string | null | undefined;
  readonly value?: Operand | null | undefined;
}

const CONTEXT_TERMS: Readonly<Record<string, string>> = {
  culture: 'jfbRules_subjectCulture',
  memberSignedIn: 'jfbRules_subjectSignedIn',
  memberGroup: 'jfbRules_subjectGroups',
  today: 'jfbRules_subjectToday',
};

/** The kind of the visit's parts, as the engine compares them. */
const CONTEXT_KINDS: Readonly<Record<string, Kind>> = {
  culture: 'text',
  memberSignedIn: 'checkbox',
  memberGroup: 'choices',
  today: 'date',
};

const DATED: ReadonlySet<Kind> = new Set(['date', 'time', 'dateTime']);

/** The name of what a condition looks at: a field's name, or a part of the visit. */
export function subjectWords(context: RuleWordsContext, condition: WordsCondition): string {
  if (condition.field) return context.nameOf(condition.field);
  const name = condition.context ?? '';
  if (name.startsWith('query.')) return context.term('jfbRules_subjectQuery', name.slice('query.'.length));
  const term = CONTEXT_TERMS[name];
  return term ? context.term(term) : name;
}

/** The kind of what a condition looks at; undefined for nothing known. */
export function kindOfCondition(context: RuleWordsContext, condition: WordsCondition): Kind | undefined {
  if (condition.field) return context.kindOf(condition.field);
  const name = condition.context ?? '';
  return name.startsWith('query.') ? 'text' : CONTEXT_KINDS[name];
}

/**
 * The words of an operator for what a condition looks at: dates and times are after and before, several choices have
 * ticked an option, the visitor is signed in, member groups include one. Elsewhere `jfbRules_op_<op>`.
 */
export function operatorTerm(op: string, kind: Kind | undefined, subjectContext: string | null): string {
  if (subjectContext === 'memberSignedIn' && (op === 'isChecked' || op === 'isNotChecked'))
    return op === 'isChecked' ? 'jfbRules_op_signedIn' : 'jfbRules_op_notSignedIn';
  if (subjectContext === 'memberGroup') {
    const group: Readonly<Record<string, string>> = {
      isChecked: 'jfbRules_op_groupsInclude',
      isNotChecked: 'jfbRules_op_groupsExclude',
      isOneOf: 'jfbRules_op_groupsIncludeOneOf',
      isNoneOf: 'jfbRules_op_groupsIncludeNoneOf',
    };
    if (group[op]) return group[op];
  }
  if ((kind !== undefined && DATED.has(kind)) || subjectContext === 'today') {
    if (op === 'greaterThan') return 'jfbRules_op_after';
    if (op === 'lessThan') return 'jfbRules_op_before';
  }
  if (kind === 'choices' && (op === 'isChecked' || op === 'isNotChecked'))
    return op === 'isChecked' ? 'jfbRules_op_hasTicked' : 'jfbRules_op_hasNotTicked';
  return `jfbRules_op_${op}`;
}

/** A date relative to today in words: "today", "2 days after today", or the offset as written where it has several steps. */
export function relativeWords(context: RuleWordsContext, offset: string): string {
  const steps = parseRelative(offset);
  if (steps === null) return offset;
  if (steps.length === 0) return context.term('jfbRules_today');
  const [step] = steps;
  if (steps.length > 1 || !step) return context.term('jfbRules_todayOffset', offset);
  const amount = Math.abs(step.amount);
  return context.term(step.amount < 0 ? 'jfbRules_beforeToday' : 'jfbRules_afterToday', amount, step.unit);
}

/** One operand in words, read as the kind of what the condition looks at. */
function operandWords(
  context: RuleWordsContext,
  condition: WordsCondition,
  kind: Kind | undefined,
  operand: Operand,
): string {
  const other = otherFieldOf(operand);
  if (other !== null) return context.term('jfbRules_answerTo', context.nameOf(other));
  const relative = relativeOf(operand);
  if (relative !== null) return relativeWords(context, relative);
  if (typeof operand !== 'string') return '';
  if (condition.context === 'culture') return context.languageOf(operand);
  if (condition.field && (kind === 'choice' || kind === 'choices')) return context.optionOf(condition.field, operand);
  if (kind === 'number' || (kind !== undefined && DATED.has(kind)) || condition.context === 'today')
    return context.valueOf(kind ?? 'date', operand);
  if (condition.context === 'memberGroup') return operand;
  return context.term('jfbRules_quoted', operand);
}

/** What a condition compares with, in words; '' for an operator that compares with nothing. */
export function valueWords(context: RuleWordsContext, condition: WordsCondition): string {
  const value = condition.value;
  if (value === undefined || value === null) return '';
  const kind = kindOfCondition(context, condition);
  const op = condition.op ?? '';
  if (op === 'countAtLeast' || op === 'countAtMost') {
    const count = Number(typeof value === 'string' ? value : NaN);
    return context.term(
      kind === 'files' ? 'jfbRules_countFiles' : 'jfbRules_countTicked',
      Number.isFinite(count) ? count : 0,
    );
  }
  if (Array.isArray(value)) {
    const items = (value as readonly Operand[]).map((item) => operandWords(context, condition, kind, item));
    return op === 'between'
      ? context.term('jfbRules_betweenValues', items[0] ?? '', items[1] ?? '')
      : items.join(context.term('jfbRules_listSeparator'));
  }
  return operandWords(context, condition, kind, value as Operand);
}

/** A condition in words: "Country is Other", "Arrival is after today", "The visitor is signed in". */
export function conditionWords(context: RuleWordsContext, condition: WordsCondition): string {
  const kind = kindOfCondition(context, condition);
  const subject = subjectWords(context, condition);
  const op = condition.op ? context.term(operatorTerm(condition.op, kind, condition.context ?? null)) : '';
  const value = valueWords(context, condition);
  return [subject, op, value].filter((part) => part !== '').join(' ');
}

/** A list of conditions and groups in words, joined by "and" or "or"; a nested group in brackets. */
export function itemsWords(context: RuleWordsContext, match: string | undefined, items: readonly unknown[]): string {
  const joiner = context.term(match === 'any' ? 'jfbRules_or' : 'jfbRules_and');
  return items
    .map((item) => {
      const given = (typeof item === 'object' && item !== null ? item : {}) as Readonly<Record<string, unknown>>;
      if (Array.isArray(given['conditions'])) {
        const inner = itemsWords(
          context,
          given['match'] as string | undefined,
          given['conditions'] as readonly unknown[],
        );
        return (given['conditions'] as readonly unknown[]).length > 1 ? `(${inner})` : inner;
      }
      return conditionWords(context, given as WordsCondition);
    })
    .join(` ${joiner} `);
}

/**
 * A rule in words: "Shown when Country is Other", "Required when Topic is one of Shipping, Press"; a validation rule's
 * "Shows "Use your work address" when Email ends with gmail.com", and an outcome's "Used when Topic is Press".
 */
export function ruleWords(
  context: RuleWordsContext,
  rule: {
    readonly effect: string;
    readonly match?: string;
    readonly conditions?: readonly unknown[] | null;
    readonly message?: unknown;
  },
): string {
  const words = itemsWords(context, rule.match, rule.conditions ?? []);
  if (rule.effect === 'message') {
    const message = context.textOf(rule.message as LocalizedText | null | undefined).trim();
    return message === ''
      ? context.term('jfbRules_when_messageStandard', words)
      : context.term('jfbRules_when_message', message, words);
  }
  const effect = rule.effect === 'use' || (EFFECTS as readonly string[]).includes(rule.effect) ? rule.effect : 'show';
  return context.term(`jfbRules_when_${effect}`, words);
}
