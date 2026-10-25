// Conditions and rules (ADR-0005), the same in C#: checked as a whole first (unknown fields and operators, operands,
// patterns), then evaluated. Checking first means both runtimes report the same error whatever order they evaluate
// in. Step 5.2 builds the rules engine on these. Condition, Group and Rule are Core's LogicCondition, LogicGroup and
// LogicRule; Core's names say they belong to its logic, beside the definition's own conditions (ADR-0030).
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
  /** Aliases of the fields a rule currently hides: they count as empty. */
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

type OperandShape = 'none' | 'one' | 'list' | 'pair' | 'count' | 'pattern' | 'checked';

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

const isGroup = (item: Condition | Group): item is Group => 'match' in item;
const isList = (operand: unknown): operand is readonly Operand[] => Array.isArray(operand);
const isObject = (operand: unknown): operand is Readonly<Record<string, unknown>> =>
  typeof operand === 'object' && operand !== null && !Array.isArray(operand);
// An operand object names another field by a string, or else a relative date by a string; the field comes first.
const isField = (operand: unknown): operand is { readonly field: string } =>
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

function subjectKind(condition: Condition, subject: Subject): Kind {
  // A null name reads as no name, as the C# reader reads it.
  const field = condition.field ?? undefined;
  const context = condition.context ?? undefined;
  if ((field === undefined) === (context === undefined)) throw new LogicError('invalid_condition');
  if (context !== undefined) return contextKind(context);
  const kind = field === undefined ? undefined : own(subject.fields, field);
  if (kind === undefined) throw new LogicError('unknown_field');
  return kind;
}

/** A single operand: text, another answer, or a date relative to today. */
function checkOne(operand: unknown, subject: Subject): void {
  if (typeof operand === 'string') return;
  if (isField(operand)) {
    if (own(subject.fields, operand.field) === undefined) throw new LogicError('unknown_field');
    return;
  }
  if (isToday(operand) && parseRelative(operand.today) !== null) return;
  throw new LogicError('invalid_operand');
}

// Checks a condition; a pattern it compiles goes into patterns, for the evaluation that follows.
function checkCondition(condition: Condition, subject: Subject, patterns: Map<string, Pattern>): void {
  const kind = subjectKind(condition, subject);
  const operator = OPERATORS.get(condition.op);
  if (operator === undefined) throw new LogicError('unknown_operator');
  if (!operator.kinds.includes(kind)) throw new LogicError('operator_not_for_kind');
  const value = condition.value;
  switch (operator.operand) {
    case 'none':
      if (value !== undefined) throw new LogicError('invalid_operand');
      return;
    case 'one':
      checkOne(value, subject);
      return;
    case 'list':
      if (isList(value)) value.forEach((item) => checkOne(item, subject));
      else if (isField(value)) checkOne(value, subject);
      else throw new LogicError('invalid_operand');
      return;
    case 'pair':
      if (!isList(value) || value.length !== 2) throw new LogicError('invalid_operand');
      value.forEach((item) => checkOne(item, subject));
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

function checkGroup(group: Group, subject: Subject, patterns: Map<string, Pattern>): void {
  if (group.match !== 'all' && group.match !== 'any') throw new LogicError('invalid_condition');
  for (const item of group.conditions) {
    if (isGroup(item)) checkGroup(item, subject, patterns);
    else checkCondition(item, subject, patterns);
  }
}

const nonEmpty = (values: readonly string[]): string[] => values.map(clean).filter((value) => value.length > 0);

/** Cleaned answers that are not empty. A hidden field has none. */
function answers(alias: string, subject: Subject): string[] {
  if (subject.context.hidden.includes(alias)) return [];
  return nonEmpty(own(subject.values, alias) ?? []);
}

function contextValues(name: string, context: Context): string[] {
  if (name === 'culture') return nonEmpty([context.culture]);
  if (name === 'memberSignedIn') return context.member.signedIn ? ['true'] : [];
  if (name === 'memberGroup') return nonEmpty(context.member.groups);
  if (name === 'today') return [formatDate(today(context.now, context.timeZone))];
  const value = own(context.query, name.slice('query.'.length));
  return value === undefined ? [] : nonEmpty(typeof value === 'string' ? [value] : value);
}

function subjectValues(condition: Condition, subject: Subject): string[] {
  const context = condition.context ?? undefined;
  if (context !== undefined) return contextValues(context, subject.context);
  return answers(condition.field ?? '', subject);
}

/** An operand's values, cleaned. A relative date outside years 1 to 9999 has none. */
function resolve(operand: unknown, subject: Subject): string[] {
  if (typeof operand === 'string') return [clean(operand)];
  if (isList(operand)) return operand.flatMap((item) => resolve(item, subject));
  if (isField(operand)) return answers(operand.field, subject);
  const steps = isToday(operand) ? parseRelative(operand.today) : null;
  if (steps === null) throw new LogicError('invalid_operand');
  const day = applyRelative(today(subject.context.now, subject.context.timeZone), steps);
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
function evaluate(condition: Condition, subject: Subject, patterns: ReadonlyMap<string, Pattern>): boolean {
  const kind = subjectKind(condition, subject);
  const values = subjectValues(condition, subject);
  const value = condition.value;
  const operands = (): string[] => resolve(value, subject);
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
        compareFirst(kind, values, resolve(low, subject), (n) => n >= 0) &&
        compareFirst(kind, values, resolve(high, subject), (n) => n <= 0)
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

function evaluateGroup(group: Group, subject: Subject, patterns: ReadonlyMap<string, Pattern>): boolean {
  const test = (item: Condition | Group): boolean =>
    isGroup(item) ? evaluateGroup(item, subject, patterns) : evaluate(item, subject, patterns);
  return group.match === 'all' ? group.conditions.every(test) : group.conditions.some(test);
}

/** Checks the condition, then evaluates it. */
export function evaluateCondition(condition: Condition, subject: Subject): boolean {
  const patterns = new Map<string, Pattern>();
  checkCondition(condition, subject, patterns);
  return evaluate(condition, subject, patterns);
}

/** Checks the whole rule, then evaluates it: null when the rule is switched off. */
export function evaluateRule(rule: Rule, subject: Subject): boolean | null {
  const patterns = new Map<string, Pattern>();
  checkGroup(rule, subject, patterns);
  if (!rule.enabled) return null;
  return evaluateGroup(rule, subject, patterns);
}
