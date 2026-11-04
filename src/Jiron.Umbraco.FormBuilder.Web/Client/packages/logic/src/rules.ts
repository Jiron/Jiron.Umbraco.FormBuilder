// Conditions and rules (ADR-0005), the same in C#: checked as a whole first (unknown fields and operators, operands,
// patterns, groups nested too deeply), then evaluated. Checking first means both runtimes report the same error whatever
// order they evaluate in. The form engine (form.ts, ADR-0061) evaluates a whole form's rules on these. Condition, Group
// and Rule are Core's LogicCondition, LogicGroup and LogicRule; Core's names say they belong to its logic, beside the
// definition's own conditions (ADR-0030).
import { applyRelative, formatDate, parseDate, parseDateTime, parseRelative, parseTime, today } from './dates.ts';
import { compareText } from './decimal.ts';
import { LogicError, own } from './errors.ts';
import { compilePattern, type Pattern } from './pattern.ts';
import { clean, fold } from './text.ts';

/** What a field's answers are compared as. */
export type Kind = 'text' | 'choice' | 'choices' | 'checkbox' | 'files' | 'number' | 'date' | 'time' | 'dateTime';

/** The context of an evaluation. */
export interface Context {
  /** The instant of evaluation, ISO 8601 with Z or an offset (the server injects its TimeProvider's). */
  readonly now: string;
  /** The site's IANA time zone (the TimeZone setting). */
  readonly timeZone: string;
  /** The visitor's culture. */
  readonly culture: string;
  /** The signed-in member, if any. */
  readonly member: { readonly signedIn: boolean; readonly groups: readonly string[] };
  /** The allowed query values of the page. */
  readonly query: Readonly<Record<string, string | readonly string[]>>;
  /** Aliases of the fields a rule currently hides: they count as empty. The form engine decides this itself. */
  readonly hidden: readonly string[];
}

/** An operand: text, another answer, a date relative to today, or a list of operands. */
export type Operand = string | { readonly field: string } | { readonly today: string } | readonly Operand[];

/** Looks at an answer (`field`) or at the context (`context`): exactly one of them. */
export interface Condition {
  readonly field?: string | null;
  readonly context?: string | null;
  readonly op: string;
  readonly value?: Operand | null;
}

/** Conditions that must all match (`all`) or of which one must (`any`). */
export interface Group {
  readonly match: string;
  readonly conditions: readonly (Condition | Group)[];
}

/** A rule: its effect applies while its conditions match; a switched-off rule is kept but ignored. */
export interface Rule extends Group {
  readonly effect: string;
  readonly enabled: boolean;
}

/** What a condition is evaluated against: the form's fields, the answers and the context. */
export interface Subject {
  readonly fields: Readonly<Record<string, Kind>>;
  readonly values: Readonly<Record<string, readonly string[]>>;
  readonly context: Context;
}

/**
 * Where an evaluation finds answers: the kinds of the fields conditions may look at, each field's texts as given (not
 * yet cleaned, none for a hidden field), and the context. The form engine gives each row of a repeatable group a scope
 * of its own (ADR-0061).
 */
export interface Scope {
  readonly fields: Readonly<Record<string, Kind>>;
  readonly answers: (field: string) => readonly string[] | undefined;
  readonly context: Context;
}

/** How deeply groups of conditions nest, the rule's own list being the first level (ADR-0061). */
export const MAX_DEPTH = 10;

/**
 * What an operator compares with: nothing, one operand, a list, two (the ends of a range), a whole number of answers, a
 * pattern, or what is ticked (nothing for a checkbox, the option for a choice field).
 */
export type OperandShape = 'none' | 'one' | 'list' | 'pair' | 'count' | 'pattern' | 'checked';

const ALL: readonly Kind[] = ['text', 'choice', 'choices', 'checkbox', 'files', 'number', 'date', 'time', 'dateTime'];
const TEXT: readonly Kind[] = ['text'];
const ORDERED: readonly Kind[] = ['number', 'date', 'time', 'dateTime'];
const CHECKABLE: readonly Kind[] = ['checkbox', 'choice', 'choices'];

const OPERATORS: ReadonlyMap<string, { readonly kinds: readonly Kind[]; readonly operand: OperandShape }> = new Map([
  ['equals', { kinds: ALL, operand: 'one' }],
  ['notEquals', { kinds: ALL, operand: 'one' }],
  ['isOneOf', { kinds: ALL, operand: 'list' }],
  ['isNoneOf', { kinds: ALL, operand: 'list' }],
  ['isEmpty', { kinds: ALL, operand: 'none' }],
  ['isNotEmpty', { kinds: ALL, operand: 'none' }],
  ['countAtLeast', { kinds: ALL, operand: 'count' }],
  ['countAtMost', { kinds: ALL, operand: 'count' }],
  ['contains', { kinds: TEXT, operand: 'one' }],
  ['notContains', { kinds: TEXT, operand: 'one' }],
  ['startsWith', { kinds: TEXT, operand: 'one' }],
  ['endsWith', { kinds: TEXT, operand: 'one' }],
  ['matches', { kinds: TEXT, operand: 'pattern' }],
  ['greaterThan', { kinds: ORDERED, operand: 'one' }],
  ['lessThan', { kinds: ORDERED, operand: 'one' }],
  ['between', { kinds: ORDERED, operand: 'pair' }],
  ['isChecked', { kinds: CHECKABLE, operand: 'checked' }],
  ['isNotChecked', { kinds: CHECKABLE, operand: 'checked' }],
]);

/** The operators that take a kind of answer, each with its operand's shape, in the order of the check's table. */
export function operatorsFor(kind: Kind): { readonly op: string; readonly operand: OperandShape }[] {
  return [...OPERATORS].filter(([, { kinds }]) => kinds.includes(kind)).map(([op, { operand }]) => ({ op, operand }));
}

/** What a context subject compares as (`culture`, `memberSignedIn`, `memberGroup`, `today`, `query.<name>`); undefined for none. */
export function contextKindOf(name: string): Kind | undefined {
  try {
    return contextKind(name);
  } catch (error) {
    if (error instanceof LogicError) return undefined;
    throw error;
  }
}

export const isGroup = (item: Condition | Group): item is Group => 'match' in item;
const isList = (operand: unknown): operand is readonly Operand[] => Array.isArray(operand);
const isObject = (operand: unknown): operand is Readonly<Record<string, unknown>> =>
  typeof operand === 'object' && operand !== null && !Array.isArray(operand);
// An operand object names another field by a string, or else a relative date by a string; the field comes first.
export const isField = (operand: unknown): operand is { readonly field: string } =>
  isObject(operand) && typeof operand['field'] === 'string';
const isToday = (operand: unknown): operand is { readonly today: string } =>
  isObject(operand) && !isField(operand) && typeof operand['today'] === 'string';

/** `0`, or 1 to 6 digits without a leading zero. */
function isCount(text: string): boolean {
  if (text.length < 1 || text.length > 6 || (text.length > 1 && text.charCodeAt(0) === 48)) return false;
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (c < 48 || c > 57) return false;
  }
  return true;
}

/** Context subjects: what a condition can look at besides answers, each with the kind it compares as. */
function contextKind(name: string): Kind {
  if (name === 'culture' || name.startsWith('query.')) return 'text';
  if (name === 'memberSignedIn') return 'checkbox';
  if (name === 'memberGroup') return 'choices';
  if (name === 'today') return 'date';
  throw new LogicError('unknown_field');
}

function subjectKind(condition: Condition, fields: Readonly<Record<string, Kind>>): Kind {
  // A null name reads as no name, as the C# reader reads it.
  const field = condition.field ?? undefined;
  const context = condition.context ?? undefined;
  if ((field === undefined) === (context === undefined)) throw new LogicError('invalid_condition');
  if (context !== undefined) return contextKind(context);
  const kind = field === undefined ? undefined : own(fields, field);
  if (kind === undefined) throw new LogicError('unknown_field');
  return kind;
}

/** A single operand: text, another answer, or a date relative to today. */
function checkOne(operand: unknown, fields: Readonly<Record<string, Kind>>): void {
  if (typeof operand === 'string') return;
  if (isField(operand)) {
    if (own(fields, operand.field) === undefined) throw new LogicError('unknown_field');
    return;
  }
  if (isToday(operand) && parseRelative(operand.today) !== null) return;
  throw new LogicError('invalid_operand');
}

// Checks a condition; a pattern it compiles goes into patterns, for the evaluation that follows.
function checkCondition(
  condition: Condition,
  fields: Readonly<Record<string, Kind>>,
  patterns: Map<string, Pattern>,
): void {
  const kind = subjectKind(condition, fields);
  const operator = OPERATORS.get(condition.op);
  if (operator === undefined) throw new LogicError('unknown_operator');
  if (!operator.kinds.includes(kind)) throw new LogicError('operator_not_for_kind');
  const value = condition.value;
  switch (operator.operand) {
    case 'none':
      if (value !== undefined) throw new LogicError('invalid_operand');
      return;
    case 'one':
      checkOne(value, fields);
      return;
    case 'list':
      if (isList(value)) value.forEach((item) => checkOne(item, fields));
      else if (isField(value)) checkOne(value, fields);
      else throw new LogicError('invalid_operand');
      return;
    case 'pair':
      if (!isList(value) || value.length !== 2) throw new LogicError('invalid_operand');
      value.forEach((item) => checkOne(item, fields));
      return;
    case 'count':
      if (typeof value !== 'string' || !isCount(value)) throw new LogicError('invalid_operand');
      return;
    case 'pattern':
      if (typeof value !== 'string') throw new LogicError('invalid_operand');
      if (!patterns.has(value)) patterns.set(value, compilePattern(value));
      return;
    case 'checked':
      // A checkbox is checked or not; a choice field names the option.
      if (kind === 'checkbox' ? value !== undefined : typeof value !== 'string')
        throw new LogicError('invalid_operand');
      return;
  }
}

/**
 * Checks a group of conditions and every group nested in it, at its depth (the rule's own list is the first level);
 * the patterns it compiles go into patterns, for the evaluations that follow.
 */
export function checkGroup(
  group: Group,
  fields: Readonly<Record<string, Kind>>,
  patterns: Map<string, Pattern>,
  depth = 1,
): void {
  if (depth > MAX_DEPTH) throw new LogicError('too_deep');
  if (group.match !== 'all' && group.match !== 'any') throw new LogicError('invalid_condition');
  for (const item of group.conditions) {
    if (isGroup(item)) checkGroup(item, fields, patterns, depth + 1);
    else checkCondition(item, fields, patterns);
  }
}

const nonEmpty = (values: readonly string[]): string[] => values.map(clean).filter((value) => value.length > 0);

/** Cleaned answers that are not empty. A hidden field has none. */
function answers(field: string, scope: Scope): string[] {
  return nonEmpty(scope.answers(field) ?? []);
}

function contextValues(name: string, context: Context): string[] {
  if (name === 'culture') return nonEmpty([context.culture]);
  if (name === 'memberSignedIn') return context.member.signedIn ? ['true'] : [];
  if (name === 'memberGroup') return nonEmpty(context.member.groups);
  if (name === 'today') return [formatDate(today(context.now, context.timeZone))];
  const value = own(context.query, name.slice('query.'.length));
  return value === undefined ? [] : nonEmpty(typeof value === 'string' ? [value] : value);
}

function subjectValues(condition: Condition, scope: Scope): string[] {
  const context = condition.context ?? undefined;
  if (context !== undefined) return contextValues(context, scope.context);
  return answers(condition.field ?? '', scope);
}

/** An operand's values, cleaned. A relative date outside years 1 to 9999 has none. */
function resolve(operand: unknown, scope: Scope): string[] {
  if (typeof operand === 'string') return [clean(operand)];
  if (isList(operand)) return operand.flatMap((item) => resolve(item, scope));
  if (isField(operand)) return answers(operand.field, scope);
  const steps = isToday(operand) ? parseRelative(operand.today) : null;
  if (steps === null) throw new LogicError('invalid_operand');
  const day = applyRelative(today(scope.context.now, scope.context.timeZone), steps);
  return day === null ? [] : [formatDate(day)];
}

const text = (n: number | null): string | null => (n === null ? null : String(n));

/** The key two values other than numbers are equal by: folded text, an exact option key, or the parsed value; null when invalid. */
function key(kind: Kind, value: string): string | null {
  switch (kind) {
    case 'text':
      return fold(value);
    case 'date':
      return text(parseDate(value));
    case 'time':
      return text(parseTime(value));
    case 'dateTime':
      return text(parseDateTime(value));
    default:
      return value;
  }
}

/**
 * Whether a field's values match another field's, as a field's matchesField check needs (ADR-0062): as many values,
 * each equal to one of the other's as `equals` compares a value of the field's kind with an operand; a field without a
 * kind compares exactly.
 */
export function sameValues(kind: Kind | undefined, values: readonly string[], others: readonly string[]): boolean {
  const equal = (a: string, b: string): boolean => {
    if (kind === 'number') return compareText(a, b) === 0;
    if (kind === undefined) return a === b;
    const x = key(kind, a);
    return x !== null && x === key(kind, b);
  };
  return values.length === others.length && values.every((value) => others.some((other) => equal(value, other)));
}

/** Orders two values of an ordered kind; null when either is not a valid value of that kind. */
function order(kind: Kind, a: string, b: string): number | null {
  if (kind === 'number') return compareText(a, b);
  const read = kind === 'date' ? parseDate : kind === 'time' ? parseTime : parseDateTime;
  const x = read(a);
  const y = read(b);
  return x === null || y === null ? null : Math.sign(x - y);
}

function compareFirst(
  kind: Kind,
  values: readonly string[],
  operands: readonly string[],
  test: (n: number) => boolean,
): boolean {
  const value = values[0];
  const operand = operands[0];
  if (value === undefined || operand === undefined) return false;
  const result = order(kind, value, operand);
  return result !== null && test(result);
}

function isChecked(condition: Condition, kind: Kind, values: readonly string[]): boolean {
  const expected = kind === 'checkbox' ? 'true' : clean(typeof condition.value === 'string' ? condition.value : '');
  return values.includes(expected);
}

// Evaluates a checked condition, with the patterns its check compiled.
function evaluate(condition: Condition, scope: Scope, patterns: ReadonlyMap<string, Pattern>): boolean {
  const kind = subjectKind(condition, scope.fields);
  const values = subjectValues(condition, scope);
  const value = condition.value;
  const operands = (): string[] => resolve(value, scope);
  // Each value and each operand becomes its key once, however many of the others it meets. Numbers compare as the
  // values they are, digit by digit, without a number or a key being built (ADR-0005).
  const anyEqual = (): boolean => {
    const list = operands();
    if (kind === 'number') return values.some((v) => list.some((o) => compareText(v, o) === 0));
    const keys = list.map((o) => key(kind, o));
    return values.some((v) => {
      const x = key(kind, v);
      return x !== null && keys.includes(x);
    });
  };
  // Each value and each operand is folded once.
  const anyText = (test: (v: string, o: string) => boolean): boolean => {
    const list = operands().map(fold);
    return values.map(fold).some((v) => list.some((o) => test(v, o)));
  };
  switch (condition.op) {
    case 'equals':
    case 'isOneOf':
      return anyEqual();
    case 'notEquals':
    case 'isNoneOf':
      return !anyEqual();
    case 'isEmpty':
      return values.length === 0;
    case 'isNotEmpty':
      return values.length > 0;
    case 'countAtLeast':
      return values.length >= Number(value);
    case 'countAtMost':
      return values.length <= Number(value);
    case 'contains':
      return anyText((v, o) => v.includes(o));
    case 'notContains':
      return !anyText((v, o) => v.includes(o));
    case 'startsWith':
      return anyText((v, o) => v.startsWith(o));
    case 'endsWith':
      return anyText((v, o) => v.endsWith(o));
    case 'matches': {
      const pattern = typeof value === 'string' ? patterns.get(value) : undefined;
      if (pattern === undefined) throw new LogicError('invalid_operand');
      return values.some((v) => pattern.test(v));
    }
    case 'greaterThan':
      return compareFirst(kind, values, operands(), (n) => n > 0);
    case 'lessThan':
      return compareFirst(kind, values, operands(), (n) => n < 0);
    case 'between': {
      const [low, high] = isList(value) ? value : [];
      return (
        compareFirst(kind, values, resolve(low, scope), (n) => n >= 0) &&
        compareFirst(kind, values, resolve(high, scope), (n) => n <= 0)
      );
    }
    case 'isChecked':
      return isChecked(condition, kind, values);
    case 'isNotChecked':
      return !isChecked(condition, kind, values);
    default:
      throw new LogicError('unknown_operator');
  }
}

/** Evaluates a checked group of conditions: all of them, or one, with the patterns its check compiled. */
export function matchesGroup(group: Group, scope: Scope, patterns: ReadonlyMap<string, Pattern>): boolean {
  const test = (item: Condition | Group): boolean =>
    isGroup(item) ? matchesGroup(item, scope, patterns) : evaluate(item, scope, patterns);
  return group.match === 'all' ? group.conditions.every(test) : group.conditions.some(test);
}

// A subject's answers as one condition sees them: a field in the context's hidden list has none.
const scopeOf = (subject: Subject): Scope => ({
  fields: subject.fields,
  answers: (field) => (subject.context.hidden.includes(field) ? undefined : own(subject.values, field)),
  context: subject.context,
});

/** Checks the condition, then evaluates it. */
export function evaluateCondition(condition: Condition, subject: Subject): boolean {
  const patterns = new Map<string, Pattern>();
  checkCondition(condition, subject.fields, patterns);
  return evaluate(condition, scopeOf(subject), patterns);
}

/** Checks the whole rule, then evaluates it: null when the rule is switched off. */
export function evaluateRule(rule: Rule, subject: Subject): boolean | null {
  const patterns = new Map<string, Pattern>();
  checkGroup(rule, subject.fields, patterns);
  if (!rule.enabled) return null;
  return matchesGroup(rule, scopeOf(subject), patterns);
}
