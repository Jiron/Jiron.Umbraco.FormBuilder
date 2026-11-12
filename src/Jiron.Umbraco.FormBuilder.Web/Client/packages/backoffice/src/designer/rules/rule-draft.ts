// The rules builder's model (ROADMAP 9.5a, ADR-0175): a rule as the builder edits it, with an id on each condition and
// group for the rows it draws, and what the builder offers: what a condition may look at, the operators each takes,
// what an operator compares with, and why a row can't be kept yet. Every check is the engine's own (`@jfb/logic`), so the
// builder never writes a condition that publishing refuses. Pure: the sidebar draws it, and the tests read it.
import {
  checkGroup,
  contextKindOf,
  LogicError,
  MAX_DEPTH,
  operatorsFor,
  parseDate,
  parseDateTime,
  parseDecimal,
  parseRelative,
  parseTime,
  type Condition,
  type Kind,
  type Operand,
  type OperandShape,
} from '@jfb/logic';
import {
  allFields,
  isField,
  isPage,
  locate,
  type DesignDefinition,
  type DesignField,
  type DesignGroup,
  type DesignPage,
  type DesignRule,
  type LocalizedText,
} from '../definition';

/**
 * What a rule does while it matches: a page's, group's or field's rule shows, hides or requires it; a form-level
 * validation rule shows a message on its field (ROADMAP 9.5c), which the builder offers as a fourth effect; an
 * outcome's rule has visitors get the outcome (ROADMAP 10.1b), and an action's runs the action (10.2a, ADR-0188),
 * neither of which the builder offers to an element.
 */
export type Effect = 'show' | 'hide' | 'require' | 'message' | 'use' | 'run';

/** Whether a rule is read once the form is sent, as an outcome's and an action's are: the server's, not the page's. */
export const afterSending = (effect: string): boolean => effect === 'use' || effect === 'run';

export const EFFECTS: readonly Effect[] = ['show', 'hide', 'require', 'message'];

/** A condition as the builder edits it: what it looks at (a field's key, or a context's name), its operator and operand. */
export interface DraftCondition {
  readonly id: string;
  readonly field: string | null;
  readonly context: string | null;
  readonly op: string | null;
  readonly value: Operand | undefined;
}

/** A group of conditions: all must match, or one is enough. The rule's own list is the group at its root. */
export interface DraftGroup {
  readonly id: string;
  readonly match: 'all' | 'any';
  readonly items: readonly DraftItem[];
}

export type DraftItem = DraftCondition | DraftGroup;

export const isDraftGroup = (item: DraftItem): item is DraftGroup => 'items' in item;

/** A rule as the builder edits it: the element it sits on, what it does, whether it is used, and its conditions. */
export interface DraftRule {
  readonly key: string;
  readonly target: string;
  readonly effect: Effect;
  readonly enabled: boolean;
  readonly root: DraftGroup;
  /** A validation rule's message in each language; what a rule of another effect keeps for a change back. */
  readonly message: LocalizedText | null;
  /** What else the rule held, kept as it came. */
  readonly rest: Readonly<Record<string, unknown>>;
}

/** The id of a rule's own list of conditions. */
export const ROOT = 'root';

// An outcome's `use` and an action's `run` are read as themselves, though the builder offers them to no element.
const asEffect = (effect: string): Effect =>
  afterSending(effect) || (EFFECTS as readonly string[]).includes(effect) ? (effect as Effect) : 'show';
const textOrNull = (value: unknown): string | null => (typeof value === 'string' && value !== '' ? value : null);

/** A rule of the definition as the builder edits it; newId gives each condition and group its id. */
export function draftOf(rule: DesignRule, target: string, newId: () => string): DraftRule {
  const { key, effect, enabled, match, conditions, message, ...rest } = rule;
  const item = (one: unknown): DraftItem => {
    const given = (typeof one === 'object' && one !== null ? one : {}) as Readonly<Record<string, unknown>>;
    if (Array.isArray(given['conditions']))
      return {
        id: newId(),
        match: given['match'] === 'any' ? 'any' : 'all',
        items: (given['conditions'] as readonly unknown[]).map(item),
      };
    return {
      id: newId(),
      field: textOrNull(given['field']),
      context: textOrNull(given['context']),
      op: textOrNull(given['op']),
      value: given['value'] === null ? undefined : (given['value'] as Operand | undefined),
    };
  };
  return {
    key,
    target,
    effect: asEffect(effect),
    enabled: enabled ?? true,
    root: { id: ROOT, match: match === 'any' ? 'any' : 'all', items: (conditions ?? []).map(item) },
    message: typeof message === 'object' && message !== null ? (message as LocalizedText) : null,
    rest,
  };
}

/** A new condition that looks at nothing yet; `field` starts it on a field. */
export const emptyCondition = (id: string, field: string | null = null): DraftCondition => ({
  id,
  field,
  context: null,
  op: null,
  value: undefined,
});

/** A new rule on an element, with one empty condition. */
export function newDraft(key: string, target: string, effect: Effect, newId: () => string): DraftRule {
  return {
    key,
    target,
    effect,
    enabled: true,
    root: { id: ROOT, match: 'all', items: [emptyCondition(newId())] },
    message: null,
    rest: {},
  };
}

/**
 * The rule the definition keeps: its conditions as the engine reads them, and everything else it held; a validation
 * rule's message where its effect is one.
 */
export function ruleOf(draft: DraftRule): DesignRule {
  const item = (one: DraftItem): Readonly<Record<string, unknown>> => {
    if (isDraftGroup(one)) return { match: one.match, conditions: one.items.map(item) };
    return {
      ...(one.field !== null ? { field: one.field } : {}),
      ...(one.context !== null ? { context: one.context } : {}),
      op: one.op ?? '',
      ...(one.value !== undefined ? { value: one.value } : {}),
    };
  };
  return {
    ...draft.rest,
    key: draft.key,
    effect: draft.effect,
    enabled: draft.enabled,
    match: draft.root.match,
    conditions: draft.root.items.map(item),
    ...(draft.effect === 'message' && draft.message !== null ? { message: draft.message } : {}),
  };
}

/** Whether a validation rule's message says something in some language; one that doesn't can't be kept. */
export const hasMessage = (draft: Pick<DraftRule, 'message'>): boolean =>
  Object.values(draft.message ?? {}).some((text) => text.trim() !== '');

/** Whether two rules say the same, an unstated `enabled` being true and an unstated `match` being all. */
export function sameRule(one: DesignRule, other: DesignRule): boolean {
  // Properties in any order say the same; a group without its match matches all, and a value of null is none.
  const plain = (value: unknown, group: boolean): unknown => {
    if (Array.isArray(value)) return value.map((item) => plain(item, false));
    if (typeof value !== 'object' || value === null) return value;
    const given = value as Readonly<Record<string, unknown>>;
    const grouped = group || Array.isArray(given['conditions']);
    const entries = Object.entries(given)
      .filter(([, item]) => item !== null && item !== undefined)
      .map(([key, item]): [string, unknown] => [key, key === 'value' ? item : plain(item, false)]);
    if (grouped && !entries.some(([key]) => key === 'match')) entries.push(['match', 'all']);
    return Object.fromEntries(entries.sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
  };
  const whole = (rule: DesignRule) => JSON.stringify(plain({ ...rule, enabled: rule.enabled ?? true }, true));
  return whole(one) === whole(other);
}

// Editing the tree. Each function gives a new rule and leaves the one it is given as it was.

function mapItems(group: DraftGroup, update: (item: DraftItem) => DraftItem | null): DraftGroup {
  const items: DraftItem[] = [];
  for (const one of group.items) {
    const next = update(one);
    if (next === null) continue;
    // A group whose last condition went goes with it.
    if (isDraftGroup(next) && next.items.length === 0) continue;
    items.push(next);
  }
  return { ...group, items };
}

/** Changes the condition or group with that id, wherever it is; null removes it, and a group left empty goes too. */
export function updateItem(draft: DraftRule, id: string, update: (item: DraftItem) => DraftItem | null): DraftRule {
  const walk = (group: DraftGroup): DraftGroup =>
    mapItems(group, (one) => {
      if (one.id === id) return update(one);
      return isDraftGroup(one) ? walk(one) : one;
    });
  if (id === ROOT) {
    const root = update(draft.root);
    return root && isDraftGroup(root) ? { ...draft, root } : draft;
  }
  return { ...draft, root: walk(draft.root) };
}

/** Finds a condition or group by its id, with how deep it is (the rule's own list is the first level). */
export function findItem(
  draft: DraftRule,
  id: string,
): { readonly item: DraftItem; readonly depth: number } | undefined {
  const walk = (group: DraftGroup, depth: number): { item: DraftItem; depth: number } | undefined => {
    if (group.id === id) return { item: group, depth };
    for (const one of group.items) {
      if (one.id === id) return { item: one, depth: isDraftGroup(one) ? depth + 1 : depth };
      const found = isDraftGroup(one) ? walk(one, depth + 1) : undefined;
      if (found) return found;
    }
    return undefined;
  };
  return walk(draft.root, 1);
}

/** Adds an empty condition at the end of a group. */
export function addCondition(draft: DraftRule, group: string, id: string, field: string | null = null): DraftRule {
  return updateItem(draft, group, (one) =>
    isDraftGroup(one) ? { ...one, items: [...one.items, emptyCondition(id, field)] } : one,
  );
}

/** Whether a group may take a group of its own: the nested one would be at most the tenth level (ADR-0061). */
export function mayNest(draft: DraftRule, group: string): boolean {
  const found = findItem(draft, group);
  return found !== undefined && isDraftGroup(found.item) && found.depth < MAX_DEPTH;
}

/**
 * Adds a group to a group. A rule's own list that holds only conditions becomes its first group, with the other kind
 * of match, and the new group follows it ("any of these groups"), as an editor reads two sets of conditions; elsewhere
 * the new group joins the end. Each new group starts with one empty condition.
 */
export function addGroup(draft: DraftRule, group: string, newId: () => string): DraftRule {
  if (!mayNest(draft, group)) return draft;
  const fresh = (): DraftGroup => ({ id: newId(), match: 'all', items: [emptyCondition(newId())] });
  const root = draft.root;
  if (group === ROOT && root.items.length > 0 && root.items.every((one) => !isDraftGroup(one))) {
    const first: DraftGroup = { id: newId(), match: root.match, items: root.items };
    return { ...draft, root: { ...root, match: root.match === 'all' ? 'any' : 'all', items: [first, fresh()] } };
  }
  return updateItem(draft, group, (one) => (isDraftGroup(one) ? { ...one, items: [...one.items, fresh()] } : one));
}

/** Every condition of a rule, in the order the builder draws them. */
export function conditionsOf(draft: DraftRule): DraftCondition[] {
  const out: DraftCondition[] = [];
  const walk = (group: DraftGroup) => {
    for (const one of group.items) {
      if (isDraftGroup(one)) walk(one);
      else out.push(one);
    }
  };
  walk(draft.root);
  return out;
}

// What a condition may look at.

/** What a condition may look at: a field of the form, or a part of the visit. */
export interface RuleSubject {
  /** `field:<key>` or `context:<name>`, as the builder's list names it. */
  readonly id: string;
  readonly kind: Kind;
  /** The field's key; null for the visit. */
  readonly field: string | null;
  /** The visit's part; null for a field. */
  readonly context: string | null;
  /** The index of the field's page; null for the visit. */
  readonly page: number | null;
}

/** What the builder knows of the visit: whether the site has several languages, and the query values the form allows. */
export interface Visit {
  readonly languages: boolean;
  readonly queryValues: readonly string[];
}

/** The id the builder's list gives what a condition looks at; '' for nothing. */
export const subjectIdOf = (condition: Pick<DraftCondition, 'field' | 'context'>): string =>
  condition.field !== null
    ? `field:${condition.field}`
    : condition.context !== null
      ? `context:${condition.context}`
      : '';

/** The parts of the visit the check takes: the language too on a site of one language, where a rule may keep one. */
export function scopeContexts(visit: Visit): string[] {
  return contextsOf({ ...visit, languages: true });
}

/** The parts of the visit a condition may look at, in the builder's order. */
export function contextsOf(visit: Visit): string[] {
  return [
    ...(visit.languages ? ['culture'] : []),
    'memberSignedIn',
    'memberGroup',
    ...visit.queryValues.map((name) => `query.${name}`),
    'today',
  ];
}

/** What the builder knows of a field type, from the field types endpoint. */
export interface TypeFacts {
  /** The kind rules compare its answers as; null for a type no rule looks at. */
  readonly kind: Kind | null;
  /** Its features: whether it takes show, hide and require rules, and whether it has a grid's rows. */
  readonly features: readonly string[];
  /** Whether its setting `multiple` turns one choice into several. */
  readonly multipleSetting: boolean;
  /** Whether its value stays on the server (the hidden field), so the visitor's page has no answer of it. */
  readonly serverOnlyDefault: boolean;
  /** Whether it holds a value, which a validation rule's message may then be about. */
  readonly holdsValue: boolean;
}

/** Whether a member's value stands in for a field, which a signed-in member's page then leaves out (ADR-0124). */
function hidesFromMember(field: DesignField, facts: TypeFacts): boolean {
  const prefill = field['prefill'] as { member?: unknown; memberMode?: unknown } | null | undefined;
  return facts.features.includes('prefill') && prefill?.member != null && prefill.memberMode === 'hide';
}

/**
 * What conditions may look at in a field, by key, with their kinds, as the server reads them: the field itself, a choice
 * with several answers where its `multiple` is on, and each row of a grid, one choice or several. A visitor's rules (a
 * page's, group's or field's) never look at a value that stays on the server, nor at a field a member's value stands in
 * for: the visitor's page has neither.
 */
export function subjectKinds(field: DesignField, facts: TypeFacts | undefined, visitor = true): [string, Kind][] {
  if (!facts) return [];
  const settings = field['settings'] as { multiple?: unknown } | null | undefined;
  const multiple = facts.multipleSetting && settings?.multiple === true;
  const out: [string, Kind][] = [];
  const answered = !visitor || (!facts.serverOnlyDefault && !hidesFromMember(field, facts));
  if (facts.kind !== null && answered)
    out.push([field.key, facts.kind === 'choice' && multiple ? 'choices' : facts.kind]);
  if (facts.features.includes('rows'))
    for (const row of field.rows ?? []) out.push([row.key, multiple ? 'choices' : 'choice']);
  return out;
}

/** The keys of an element and of everything inside it. */
function keysIn(element: DesignPage | DesignGroup | DesignField): Set<string> {
  const keys = new Set<string>([element.key]);
  const page: DesignPage = isPage(element)
    ? element
    : isField(element)
      ? { key: '', groups: [{ key: '', fields: [element] }] }
      : { key: '', groups: [element] };
  for (const group of page.groups) keys.add(group.key);
  for (const field of allFields({ pages: [page] })) keys.add(field.key);
  return keys;
}

/**
 * What a rule's conditions may look at: each field of the form whose type rules compare, by page in the form's order,
 * then the visit. The element the rule sits on is left out, and for a show or hide rule so is everything inside it: its
 * being shown would wait on itself. A validation rule may look at its own field ("ends with gmail.com"): what its
 * message says changes nothing a rule reads. An outcome's or an action's rule is the server's, once the form is sent: it may look at a
 * value that stays on the server and at a field a member's value stands in for. `keep` is a subject a condition looks
 * at already, which stays on the list.
 */
export function subjectsFor(
  definition: DesignDefinition,
  factsOf: (type: string) => TypeFacts | undefined,
  target: string,
  effect: Effect,
  visit: Visit,
  keep = '',
): RuleSubject[] {
  const found = locate(definition, target);
  const excluded =
    !found || effect === 'message'
      ? new Set<string>()
      : effect === 'require'
        ? new Set([target])
        : keysIn(found.element);
  const subjects: RuleSubject[] = [];
  definition.pages.forEach((page, index) => {
    for (const field of allFields({ pages: [page] })) {
      // A grid's rows are left out with it: their answers are its own.
      const out = excluded.has(field.key);
      for (const [key, kind] of subjectKinds(field, factsOf(field.type), !afterSending(effect))) {
        const id = `field:${key}`;
        if (out && id !== keep) continue;
        subjects.push({ id, kind, field: key, context: null, page: index });
      }
    }
  });
  const contexts = contextsOf(visit);
  if (keep === 'context:culture' && !contexts.includes('culture')) contexts.unshift('culture');
  for (const context of contexts) {
    const kind = contextKindOf(context);
    if (kind !== undefined) subjects.push({ id: `context:${context}`, kind, field: null, context, page: null });
  }
  return subjects;
}

/** The kinds of the form's fields and grid rows, by key, as the engine's check reads them. */
export function fieldKinds(
  definition: DesignDefinition,
  factsOf: (type: string) => TypeFacts | undefined,
  visitor = true,
): Record<string, Kind> {
  const kinds: Record<string, Kind> = {};
  for (const field of allFields(definition))
    for (const [key, kind] of subjectKinds(field, factsOf(field.type), visitor)) kinds[key] = kind;
  return kinds;
}

// Operators and what they compare with.

/** The operators a sentence about a kind of answer means, in the order the builder lists them (ADR-0175). */
const NARROWED: Readonly<Record<Kind, readonly string[]>> = {
  text: [
    'equals',
    'notEquals',
    'contains',
    'notContains',
    'startsWith',
    'endsWith',
    'isOneOf',
    'isNoneOf',
    'matches',
    'isEmpty',
    'isNotEmpty',
  ],
  choice: ['equals', 'notEquals', 'isOneOf', 'isNoneOf', 'isEmpty', 'isNotEmpty'],
  choices: ['isChecked', 'isNotChecked', 'isOneOf', 'isNoneOf', 'countAtLeast', 'countAtMost', 'isEmpty', 'isNotEmpty'],
  checkbox: ['isChecked', 'isNotChecked'],
  files: ['countAtLeast', 'countAtMost', 'isEmpty', 'isNotEmpty'],
  number: ['equals', 'notEquals', 'greaterThan', 'lessThan', 'between', 'isEmpty', 'isNotEmpty'],
  date: ['equals', 'notEquals', 'greaterThan', 'lessThan', 'between', 'isEmpty', 'isNotEmpty'],
  time: ['equals', 'notEquals', 'greaterThan', 'lessThan', 'between', 'isEmpty', 'isNotEmpty'],
  dateTime: ['equals', 'notEquals', 'greaterThan', 'lessThan', 'between', 'isEmpty', 'isNotEmpty'],
};

/** The visit's parts mean fewer: a language is or isn't one, a visitor is signed in or not, today is a date with no "empty". */
const NARROWED_CONTEXT: Readonly<Record<string, readonly string[]>> = {
  culture: ['equals', 'notEquals', 'isOneOf', 'isNoneOf'],
  memberSignedIn: ['isChecked', 'isNotChecked'],
  memberGroup: ['isChecked', 'isNotChecked', 'isOneOf', 'isNoneOf'],
  today: ['equals', 'greaterThan', 'lessThan', 'between'],
};

/**
 * The operators the builder offers for what a condition looks at: those a sentence about it means, each one the engine
 * takes for its kind (a test holds the tables to `operatorsFor`). An operator the condition holds already stays on the
 * list, so opening a rule never changes it.
 */
export function operatorsOf(subject: Pick<RuleSubject, 'kind' | 'context'>, current: string | null = null): string[] {
  const offered =
    subject.context !== null && !subject.context.startsWith('query.')
      ? (NARROWED_CONTEXT[subject.context] ?? NARROWED[subject.kind])
      : NARROWED[subject.kind];
  return current !== null && !offered.includes(current) ? [...offered, current] : [...offered];
}

/** What an operator compares with for a kind of answer; undefined for an operator the kind doesn't take. */
export function shapeOf(kind: Kind, op: string): OperandShape | undefined {
  return operatorsFor(kind).find((each) => each.op === op)?.operand;
}

const firstOf = (value: Operand | undefined): Operand | undefined =>
  Array.isArray(value) ? (value as readonly Operand[])[0] : value;

/** The other field an operand names; null for a value or a relative date. */
export const otherFieldOf = (operand: Operand | undefined): string | null =>
  typeof operand === 'object' && operand !== null && !Array.isArray(operand) && 'field' in operand
    ? (operand as { readonly field: string }).field
    : null;

/** The offset of a date relative to today an operand holds (`+2d`, '' for today); null for any other operand. */
export const relativeOf = (operand: Operand | undefined): string | null =>
  typeof operand === 'object' &&
  operand !== null &&
  !Array.isArray(operand) &&
  'today' in operand &&
  !('field' in operand)
    ? (operand as { readonly today: string }).today
    : null;

/** An operand kept as far as it fits another shape: one value into a list or a range's start, a list's first item. */
function reshape(value: Operand | undefined, from: OperandShape | undefined, to: OperandShape): Operand | undefined {
  if (value === undefined || from === to) return value;
  // A number of answers, or a pattern, is no option, text or date of another operator.
  if (from === 'count' || from === 'pattern') return undefined;
  const one = firstOf(value);
  switch (to) {
    case 'one':
      return one;
    case 'list':
      return typeof one === 'string' ? [one] : undefined;
    case 'pair':
      return one === undefined ? undefined : [one, ''];
    case 'checked':
      return typeof one === 'string' ? one : undefined;
    default:
      return undefined;
  }
}

/**
 * A condition looking at something else. Of the same kind, it keeps its operator and value; else it takes the first
 * operator the new subject offers and no value.
 */
export function withSubject(
  condition: DraftCondition,
  subject: RuleSubject | undefined,
  before: Kind | undefined,
): DraftCondition {
  if (!subject) return { ...condition, field: null, context: null, op: null, value: undefined };
  const looking = { ...condition, field: subject.field, context: subject.context };
  // An answer compared with itself compares nothing, so such a value goes.
  const self = otherFieldOf(firstOf(condition.value)) === subject.field && subject.field !== null;
  if (before === subject.kind && condition.op !== null && operatorsOf(subject).includes(condition.op))
    return self ? { ...looking, value: undefined } : looking;
  // Another kind of answer reads values another way, so the value goes with the operator.
  return { ...looking, op: operatorsOf(subject)[0] ?? null, value: undefined };
}

/** A condition with another operator, keeping as much of its value as the new operator's operand takes. */
export function withOperator(condition: DraftCondition, op: string, kind: Kind): DraftCondition {
  const from = condition.op === null ? undefined : shapeOf(kind, condition.op);
  const to = shapeOf(kind, op);
  if (to === undefined) return { ...condition, op, value: undefined };
  // A checkbox's "ticked" compares with nothing; a choice's names the option.
  const value =
    to === 'none' || (to === 'checked' && kind === 'checkbox') ? undefined : reshape(condition.value, from, to);
  return { ...condition, op, value };
}

// Why a row can't be kept yet.

/** Why a condition can't be kept: what it lacks, or what of it the engine's check refuses. */
export type RowProblem =
  /** It looks at nothing yet. */
  | 'subject'
  /** What it looks at is gone, has no answers to compare, or is a query value the form no longer allows. */
  | 'unknown'
  /** It has no operator. */
  | 'operator'
  /** The operator doesn't take the kind of answer (a type change can leave one, ADR-0174). */
  | 'operatorNotForKind'
  /** It lacks its value, or a value it compares with is empty. */
  | 'value'
  /** The other field it compares with is gone, or has no answers on the visitor's page. */
  | 'unknownOther'
  /** Its operator compares with nothing, yet it holds a value (one written by hand or through the API). */
  | 'noValue'
  /** A value is not a number, a date, a time, a date and time, or a whole number of answers, as the kind needs. */
  | 'number'
  | 'date'
  | 'time'
  | 'dateTime'
  | 'count'
  /** The pattern uses syntax outside the safe subset, or exceeds its limits. */
  | 'pattern';

/** What the check knows: the fields' kinds by key, and the parts of the visit the builder offers. */
export interface RuleScope {
  readonly fields: Readonly<Record<string, Kind>>;
  readonly contexts: readonly string[];
}

const WIRE_CHECKS: Partial<Record<Kind, { readonly problem: RowProblem; readonly reads: (text: string) => boolean }>> =
  {
    number: { problem: 'number', reads: (text) => parseDecimal(text) !== null },
    date: { problem: 'date', reads: (text) => parseDate(text) !== null },
    time: { problem: 'time', reads: (text) => parseTime(text) !== null },
    dateTime: { problem: 'dateTime', reads: (text) => parseDateTime(text) !== null },
  };

/** `0`, or 1 to 6 digits without a leading zero, as the engine reads a count. */
const isCount = (text: string): boolean => /^(0|[1-9][0-9]{0,5})$/.test(text);

/** Why one operand can't be kept; null when it can. */
function operandProblem(operand: unknown, kind: Kind, scope: RuleScope): RowProblem | null {
  if (typeof operand === 'string') {
    const text = operand.trim();
    if (text === '') return 'value';
    const wire = WIRE_CHECKS[kind];
    return wire && !wire.reads(text) ? wire.problem : null;
  }
  if (typeof operand === 'object' && operand !== null && !Array.isArray(operand)) {
    const given = operand as Readonly<Record<string, unknown>>;
    if (typeof given['field'] === 'string') return Object.hasOwn(scope.fields, given['field']) ? null : 'unknownOther';
    if (typeof given['today'] === 'string') return parseRelative(given['today']) !== null ? null : 'date';
  }
  return 'value';
}

/** The kind of what a condition looks at in a scope; undefined when it looks at nothing the scope has. */
export function kindIn(condition: Pick<DraftCondition, 'field' | 'context'>, scope: RuleScope): Kind | undefined {
  if (condition.field !== null)
    return Object.hasOwn(scope.fields, condition.field) ? scope.fields[condition.field] : undefined;
  if (condition.context !== null && scope.contexts.includes(condition.context)) return contextKindOf(condition.context);
  return undefined;
}

/** Why a condition can't be kept yet, the first thing a row says; null when it can. */
export function problemOf(condition: DraftCondition, scope: RuleScope): RowProblem | null {
  if (condition.field === null && condition.context === null) return 'subject';
  const known = kindIn(condition, scope);
  if (known === undefined) return 'unknown';
  if (condition.op === null) return 'operator';
  const shape = shapeOf(known, condition.op);
  if (shape === undefined) return 'operatorNotForKind';
  const value = condition.value;
  const each = (items: readonly unknown[]): RowProblem | null => {
    for (const item of items) {
      const problem = operandProblem(item, known, scope);
      if (problem) return problem;
    }
    return null;
  };
  let problem: RowProblem | null = null;
  switch (shape) {
    case 'one':
      problem = operandProblem(value, known, scope);
      break;
    case 'list':
      problem = Array.isArray(value)
        ? value.length === 0
          ? 'value'
          : each(value)
        : operandProblem(value, known, scope);
      break;
    case 'pair':
      problem = Array.isArray(value) && value.length === 2 ? each(value) : 'value';
      break;
    case 'count':
      problem = typeof value !== 'string' || value === '' ? 'value' : isCount(value) ? null : 'count';
      break;
    case 'pattern':
      problem = typeof value !== 'string' || value === '' ? 'value' : null;
      break;
    case 'checked':
      problem =
        known === 'checkbox'
          ? value === undefined
            ? null
            : 'noValue'
          : typeof value === 'string' && value !== ''
            ? null
            : 'value';
      break;
    case 'none':
      problem = value === undefined ? null : 'noValue';
      break;
  }
  return problem ?? engineProblem(condition, scope);
}

/** What the engine's own check says of a condition, which both runtimes run before they evaluate (ADR-0005). */
function engineProblem(condition: DraftCondition, scope: RuleScope): RowProblem | null {
  const checked: Condition = {
    ...(condition.field !== null ? { field: condition.field } : {}),
    ...(condition.context !== null ? { context: condition.context } : {}),
    op: condition.op ?? '',
    ...(condition.value !== undefined ? { value: condition.value } : {}),
  };
  try {
    checkGroup({ match: 'all', conditions: [checked] }, scope.fields, new Map());
    return null;
  } catch (error) {
    if (!(error instanceof LogicError)) throw error;
    switch (error.code) {
      case 'unknown_operator':
      case 'operator_not_for_kind':
        return 'operatorNotForKind';
      case 'unsafe_pattern':
        return 'pattern';
      case 'unknown_field':
        return condition.field !== null && !Object.hasOwn(scope.fields, condition.field) ? 'unknown' : 'unknownOther';
      default:
        return 'value';
    }
  }
}

/** Each condition's problem, by its id, in the order the builder draws them; empty when the rule can be kept. */
export function problemsOf(draft: DraftRule, scope: RuleScope): Map<string, RowProblem> {
  const problems = new Map<string, RowProblem>();
  for (const condition of conditionsOf(draft)) {
    const problem = problemOf(condition, scope);
    if (problem) problems.set(condition.id, problem);
  }
  return problems;
}

// The first line: what the rule does, and to what.

/**
 * Whether a field may show a validation rule's message: one whose answer rules compare and the visitor gives, so not a
 * name, an address, a grid or a repeatable group, nor a value kept on the server or one a member's value stands in for.
 * Both runtimes refuse the whole form otherwise, and publishing refuses it first (ADR-0124, ADR-0178).
 */
export function takesMessage(
  definition: DesignDefinition,
  key: string,
  factsOf: (type: string) => TypeFacts | undefined,
): boolean {
  const found = locate(definition, key);
  if (found?.kind !== 'field') return false;
  const facts = factsOf(found.element.type);
  return (
    facts !== undefined &&
    facts.kind !== null &&
    facts.holdsValue &&
    !facts.serverOnlyDefault &&
    !hidesFromMember(found.element, facts)
  );
}

/** The effects the builder offers an element: those its rules take, then a message for a field that may show one. */
export function builderEffectsOf(
  definition: DesignDefinition,
  key: string,
  factsOf: (type: string) => TypeFacts | undefined,
): Effect[] {
  return [
    ...effectsOf(definition, key, (type) => factsOf(type)?.features ?? []),
    ...(takesMessage(definition, key, factsOf) ? (['message'] as const) : []),
  ];
}

/** The effects an element's own rules take: show and hide for a page or a group; for a field, as its type allows. */
export function effectsOf(
  definition: DesignDefinition,
  key: string,
  featuresOf: (type: string) => readonly string[],
): Effect[] {
  const found = locate(definition, key);
  if (!found) return [];
  if (found.kind !== 'field') return ['show', 'hide'];
  const features = featuresOf(found.element.type);
  return [
    ...(features.includes('visibilityRules') ? (['show', 'hide'] as const) : []),
    ...(features.includes('required') ? (['require'] as const) : []),
  ];
}

/**
 * Another rule of the element that a rule with this effect can't sit beside: one with the same effect, or show beside
 * hide. A message never clashes: validation rules are kept apart from the element's own, so a field shows as many
 * messages as it is given.
 */
export function clashOf(
  definition: DesignDefinition,
  target: string,
  effect: Effect,
  ruleKey: string,
): DesignRule | undefined {
  const rules = locate(definition, target)?.element.rules ?? [];
  const opposite = effect === 'show' ? 'hide' : effect === 'hide' ? 'show' : null;
  return rules.find((rule) => rule.key !== ruleKey && (rule.effect === effect || rule.effect === opposite));
}

/** The rules of an element with one rule put in place of the one with its key, or at the end; or taken out with null. */
export function withRule(
  rules: readonly DesignRule[] | null | undefined,
  key: string,
  rule: DesignRule | null,
): DesignRule[] {
  const list = [...(rules ?? [])];
  const at = list.findIndex((each) => each.key === key);
  if (rule === null) return at < 0 ? list : list.filter((_, index) => index !== at);
  if (at < 0) return [...list, rule];
  list[at] = rule;
  return list;
}
