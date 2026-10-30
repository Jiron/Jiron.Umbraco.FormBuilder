// Calculations (ROADMAP section 4.3.3, ADR-0063), the same in C# (Core's Calculations): a formula an editor types,
// parsed into a tree of numbers, fields' aliases and the language's operations, and worked out over the exact decimals
// of ADR-0005. Nothing in a formula runs as code. The spike's vectors write the same trees by hand, with `store` for a
// number as a field stores it. It is a module of its own, `@jfb/logic/calc`, which the form engine takes as `FORMULAS`
// for a form with a calculation, so the website runtime loads it only for such a form (ADR-0114).
import { civil, parseDate, parseDateTime } from './dates.ts';
import {
  add,
  compare,
  divide,
  formatDecimal,
  integer,
  MAX_ROUND_PLACES,
  multiply,
  negate,
  operand,
  parseDecimal,
  readNumber,
  round,
  stored,
  subtract,
  type Decimal,
} from './decimal.ts';
import { LogicError } from './errors.ts';
import type { Kind } from './rules.ts';
import { clean } from './text.ts';

/** A field's answers in a calculation, by alias; `at` is where the formula names it. */
export interface FieldReference {
  readonly field: string;
  readonly at?: number;
}

/** An operation over calculations; `scale` is round's places, `at` where the formula writes it. */
export interface Operation {
  readonly op: string;
  readonly args: readonly Calculation[];
  readonly scale?: number;
  readonly at?: number;
}

/** A calculation: a decimal constant in the wire form, a field's answers, or an operation. */
export type Calculation = string | FieldReference | Operation;

/** Where a calculation reads the fields it names (ADR-0063). */
export interface FormulaScope {
  /** The kind of the field with the alias; none when the form has no such field or the browser cannot read it. */
  readonly kind: (alias: string) => Kind | undefined;
  /** The field's answers as given, not yet cleaned; none for a hidden field. */
  readonly answers: (alias: string) => readonly string[] | undefined;
  /** The site's date as a day number, read from the context only when a formula asks for it. */
  readonly today: () => number;
  /**
   * Whether the field's answers are ones its type could not read or found over its limits, which leave a calculation
   * that reads them without a value (ADR-0114); none are when absent.
   */
  readonly unreadable?: (alias: string) => boolean;
}

/** The longest formula, in UTF-16 code units. */
export const MAX_FORMULA_LENGTH = 1000;
/** How deeply parentheses and calls nest in a formula. */
export const MAX_FORMULA_DEPTH = 20;

const ZERO = integer(0);
const ONE = integer(1);

/** Nothing to read: what a calculation without fields sees. */
const NOTHING: FormulaScope = {
  kind: () => undefined,
  answers: () => undefined,
  today: () => {
    throw new LogicError('invalid_context');
  },
};

/** The kinds a formula reads as numbers, and those it reads as dates. */
const NUMBERS: ReadonlySet<Kind> = new Set<Kind>(['number', 'choice', 'choices', 'checkbox']);
const DATES: ReadonlySet<Kind> = new Set<Kind>(['date', 'dateTime']);

const BINARY: ReadonlyMap<string, (a: Decimal, b: Decimal) => Decimal> = new Map([
  ['add', add],
  ['sub', subtract],
  ['mul', multiply],
  ['div', divide],
]);

const COMPARISONS: ReadonlyMap<string, (order: number) => boolean> = new Map([
  ['eq', (order: number) => order === 0],
  ['ne', (order: number) => order !== 0],
  ['lt', (order: number) => order < 0],
  ['le', (order: number) => order <= 0],
  ['gt', (order: number) => order > 0],
  ['ge', (order: number) => order >= 0],
]);

/** The fewest and the most arguments of each operation that is not binary. */
const ARITY: ReadonlyMap<string, readonly [number, number]> = new Map([
  ['neg', [1, 1]],
  ['round', [1, 1]],
  ['store', [1, 1]],
  ['min', [2, Infinity]],
  ['max', [2, Infinity]],
  ['if', [3, 3]],
  ['and', [2, Infinity]],
  ['or', [2, Infinity]],
  ['not', [1, 1]],
  ['daysBetween', [2, 2]],
  ['yearsBetween', [2, 2]],
  ['today', [0, 0]],
]);

/** The functions a formula may call, with their fewest and most arguments as written. */
const FUNCTIONS: ReadonlyMap<string, readonly [number, number]> = new Map([
  ['min', [2, Infinity]],
  ['max', [2, Infinity]],
  ['round', [1, 2]],
  ['if', [3, 3]],
  ['and', [2, Infinity]],
  ['or', [2, Infinity]],
  ['not', [1, 1]],
  ['daysBetween', [2, 2]],
  ['yearsBetween', [2, 2]],
  ['today', [0, 0]],
]);

const DATE_FUNCTIONS: ReadonlySet<string> = new Set(['daysBetween', 'yearsBetween']);

const isField = (calculation: Calculation): calculation is FieldReference =>
  typeof calculation === 'object' && 'field' in calculation;
const isToday = (calculation: Calculation): boolean =>
  typeof calculation === 'object' && !isField(calculation) && calculation.op === 'today';

// ---- Parsing ----

const isDigit = (code: number): boolean => code >= 48 && code <= 57;
const isLetter = (code: number): boolean => (code >= 65 && code <= 90) || (code >= 97 && code <= 122);
const isSpace = (code: number): boolean => code === 32 || code === 9 || code === 10 || code === 13;

const OPEN = 40;
const CLOSE = 41;
const COMMA = 44;

/** Reads a formula by the grammar of ADR-0063, left to right, reporting the first mistake where it starts. */
class FormulaParser {
  private readonly text: string;
  private position = 0;
  private depth = 0;

  constructor(text: string) {
    this.text = text;
  }

  parse(): Calculation {
    if (this.text.length > MAX_FORMULA_LENGTH) throw new LogicError('invalid_formula', MAX_FORMULA_LENGTH);
    const tree = this.comparison();
    if (this.peek() !== -1) throw new LogicError('invalid_formula', this.position);
    return tree;
  }

  /** The next character after any spaces, or -1 at the end; the position is left on it. */
  private peek(): number {
    while (this.position < this.text.length && isSpace(this.text.charCodeAt(this.position))) this.position++;
    return this.position < this.text.length ? this.text.charCodeAt(this.position) : -1;
  }

  private comparison(): Calculation {
    const left = this.sum();
    const c = this.peek();
    const at = this.position;
    const next = this.text.charCodeAt(at + 1);
    let op: string;
    if (c === 61) op = 'eq';
    else if (c === 33 && next === 61) op = 'ne';
    else if (c === 60) op = next === 61 ? 'le' : 'lt';
    else if (c === 62) op = next === 61 ? 'ge' : 'gt';
    else return left;
    this.position += op === 'ne' || op === 'le' || op === 'ge' ? 2 : 1;
    return { op, args: [left, this.sum()], at };
  }

  private sum(): Calculation {
    let left = this.product();
    for (;;) {
      const c = this.peek();
      if (c !== 43 && c !== 45) return left;
      const at = this.position++;
      left = { op: c === 43 ? 'add' : 'sub', args: [left, this.product()], at };
    }
  }

  private product(): Calculation {
    let left = this.unary();
    for (;;) {
      const c = this.peek();
      if (c !== 42 && c !== 47) return left;
      const at = this.position++;
      left = { op: c === 42 ? 'mul' : 'div', args: [left, this.unary()], at };
    }
  }

  /** Negations are counted rather than nested, so a run of them costs nothing. */
  private unary(): Calculation {
    let negations = 0;
    let at = 0;
    while (this.peek() === 45) {
      if (negations === 0) at = this.position;
      negations++;
      this.position++;
    }
    const value = this.primary();
    return negations % 2 === 1 ? { op: 'neg', args: [value], at } : value;
  }

  private primary(): Calculation {
    const c = this.peek();
    const start = this.position;
    if (isDigit(c)) return this.number();
    if (c === OPEN) {
      this.enter(start);
      this.position++;
      const inner = this.comparison();
      this.close();
      return inner;
    }
    if (isLetter(c)) {
      while (this.position < this.text.length) {
        const code = this.text.charCodeAt(this.position);
        if (!isLetter(code) && !isDigit(code)) break;
        this.position++;
      }
      const name = this.text.slice(start, this.position);
      return this.peek() === OPEN ? this.call(name, start) : { field: name, at: start };
    }
    throw new LogicError('invalid_formula', start);
  }

  /** The wire form without a sign: no leading zero, and at least one digit after a point. */
  private number(): string {
    const start = this.position;
    const text = this.text;
    if (text.charCodeAt(start) === 48 && isDigit(text.charCodeAt(start + 1))) {
      throw new LogicError('invalid_formula', start);
    }
    let i = start;
    while (i < text.length && isDigit(text.charCodeAt(i))) i++;
    if (text.charCodeAt(i) === 46) {
      const fraction = ++i;
      while (i < text.length && isDigit(text.charCodeAt(i))) i++;
      if (i === fraction) throw new LogicError('invalid_formula', i);
    }
    this.position = i;
    const literal = text.slice(start, i);
    try {
      readNumber(literal);
    } catch (error) {
      if (error instanceof LogicError) throw new LogicError(error.code, start);
      throw error;
    }
    return literal;
  }

  private call(name: string, start: number): Calculation {
    const arity = FUNCTIONS.get(name);
    if (arity === undefined) throw new LogicError('unknown_operator', start);
    this.enter(start);
    this.position++;
    const args: Calculation[] = [];
    const starts: number[] = [];
    if (this.peek() !== CLOSE) {
      for (;;) {
        this.peek();
        starts.push(this.position);
        args.push(this.comparison());
        if (this.peek() !== COMMA) break;
        this.position++;
      }
    }
    this.close();
    if (args.length < arity[0] || args.length > arity[1]) throw new LogicError('invalid_operand', start);
    if (DATE_FUNCTIONS.has(name)) {
      args.forEach((arg, k) => {
        if (!isField(arg) && !isToday(arg)) throw new LogicError('invalid_operand', starts[k]);
      });
    }
    if (name !== 'round') return { op: name, args, at: start };
    const [value, places] = args;
    if (value === undefined) throw new LogicError('invalid_operand', start);
    if (places === undefined) return { op: 'round', args: [value], scale: 0, at: start };
    // Places are a whole number written as it is: the literal has no sign, and no leading zero.
    if (typeof places !== 'string' || places.includes('.') || Number(places) > MAX_ROUND_PLACES) {
      throw new LogicError('invalid_operand', starts[1]);
    }
    return { op: 'round', args: [value], scale: Number(places), at: start };
  }

  private enter(at: number): void {
    if (++this.depth > MAX_FORMULA_DEPTH) throw new LogicError('too_deep', at);
  }

  private close(): void {
    if (this.peek() !== CLOSE) throw new LogicError('invalid_formula', this.position);
    this.position++;
    this.depth--;
  }
}

/** `today()` only as a date function's argument; a function's own arguments were checked as it was read. */
function placeToday(calculation: Calculation): void {
  if (typeof calculation === 'string' || isField(calculation)) return;
  if (calculation.op === 'today') throw new LogicError('invalid_operand', calculation.at);
  if (DATE_FUNCTIONS.has(calculation.op)) return;
  for (const arg of calculation.args) placeToday(arg);
}

/**
 * Reads a formula into its tree: the grammar, the depth, the numbers' range, the functions and their arguments
 * (ADR-0063). A mistake is a LogicError with the position where it starts.
 */
export function parseFormula(formula: string): Calculation {
  const tree = new FormulaParser(formula).parse();
  placeToday(tree);
  return tree;
}

/**
 * Checks the fields a formula names against the form: each alias must be a field the scope can read, a number where a
 * number goes and a date in a date function, and the formula must name one. The aliases, in the order first named.
 */
export function checkFormula(calculation: Calculation, kind: (alias: string) => Kind | undefined): string[] {
  const named = new Set<string>();
  const visit = (item: Calculation, date: boolean): void => {
    if (typeof item === 'string') return;
    if (isField(item)) {
      const found = kind(item.field);
      if (found === undefined) throw new LogicError('unknown_field', item.at);
      if (!(date ? DATES : NUMBERS).has(found)) throw new LogicError('operator_not_for_kind', item.at);
      named.add(item.field);
      return;
    }
    const dates = DATE_FUNCTIONS.has(item.op);
    for (const arg of item.args) visit(arg, dates);
  };
  visit(calculation, false);
  if (named.size === 0) throw new LogicError('constant_formula', 0);
  return [...named];
}

// ---- Working it out ----

/** A field's answers as a number: their sum, none being 0; null when one does not read, or its type could not read them. */
function numberOf(reference: FieldReference, scope: FormulaScope): Decimal | null {
  const kind = scope.kind(reference.field);
  if (kind === undefined) throw new LogicError('unknown_field', reference.at);
  if (!NUMBERS.has(kind)) throw new LogicError('operator_not_for_kind', reference.at);
  if (scope.unreadable?.(reference.field) === true) return null;
  let sum = ZERO;
  for (const answer of scope.answers(reference.field) ?? []) {
    const text = clean(answer);
    if (text.length === 0) continue;
    const value = kind === 'checkbox' ? (text === 'true' ? ONE : null) : readNumber(text);
    if (value === null) return null;
    sum = add(sum, value);
  }
  return sum;
}

/** A date function's argument as a day number: the field's one date or today; null when there is none. */
function dateOf(calculation: Calculation, scope: FormulaScope): number | null {
  if (isToday(calculation)) return scope.today();
  if (!isField(calculation)) throw new LogicError('invalid_operand');
  const kind = scope.kind(calculation.field);
  if (kind === undefined) throw new LogicError('unknown_field', calculation.at);
  if (!DATES.has(kind)) throw new LogicError('operator_not_for_kind', calculation.at);
  if (scope.unreadable?.(calculation.field) === true) return null;
  const answers = (scope.answers(calculation.field) ?? []).map(clean).filter((text) => text.length > 0);
  const [text] = answers;
  if (answers.length !== 1 || text === undefined) return null;
  if (kind === 'date') return parseDate(text);
  return parseDateTime(text) === null ? null : parseDate(text.slice(0, 10));
}

/** Whole years from one date to another, as an age counts them; from 29 February a year is complete on 1 March. */
function yearsFrom(from: number, to: number): number {
  if (to < from) return -yearsFrom(to, from);
  const a = civil(from);
  const b = civil(to);
  const years = b.y - a.y;
  return b.m * 100 + b.d < a.m * 100 + a.d ? years - 1 : years;
}

/**
 * Works out a calculation over a scope: null when an answer it reads does not read, or a date it needs is empty
 * (ADR-0063). Each operation's shape is checked, then its arguments run left to right; `if`, `and` and `or` stop as
 * soon as they know the answer. A failure of the arithmetic (`division_by_zero`, `out_of_range`) is a LogicError.
 */
export function calculate(calculation: Calculation, scope: FormulaScope = NOTHING): Decimal | null {
  if (typeof calculation === 'string') return operand(calculation);
  if (isField(calculation)) return numberOf(calculation, scope);
  const { op, args } = calculation;
  const binary = BINARY.get(op);
  const comparison = COMPARISONS.get(op);
  if (binary !== undefined || comparison !== undefined) {
    const [first, second] = args;
    if (args.length !== 2 || first === undefined || second === undefined) throw new LogicError('invalid_operand');
    const a = calculate(first, scope);
    if (a === null) return null;
    const b = calculate(second, scope);
    if (b === null) return null;
    if (binary !== undefined) return binary(a, b);
    return comparison?.(compare(a, b)) === true ? ONE : ZERO;
  }
  const arity = ARITY.get(op);
  if (arity === undefined) throw new LogicError('unknown_operator');
  if (args.length < arity[0] || args.length > arity[1]) throw new LogicError('invalid_operand');
  const value = (k: number): Decimal | null => calculate(args[k] ?? '0', scope);
  switch (op) {
    case 'neg': {
      const a = value(0);
      return a === null ? null : negate(a);
    }
    case 'round': {
      const scale = calculation.scale;
      if (scale === undefined || !Number.isInteger(scale) || scale < 0 || scale > MAX_ROUND_PLACES) {
        throw new LogicError('invalid_operand');
      }
      const a = value(0);
      return a === null ? null : round(a, scale);
    }
    case 'store': {
      const a = value(0);
      return a === null ? null : stored(a);
    }
    case 'min':
    case 'max': {
      let best: Decimal | null = null;
      for (let k = 0; k < args.length; k++) {
        const a = value(k);
        if (a === null) return null;
        if (best === null || (op === 'min' ? compare(a, best) < 0 : compare(a, best) > 0)) best = a;
      }
      return best;
    }
    case 'if': {
      const condition = value(0);
      if (condition === null) return null;
      return value(condition.n !== 0n ? 1 : 2);
    }
    case 'and':
    case 'or': {
      for (let k = 0; k < args.length; k++) {
        const a = value(k);
        if (a === null) return null;
        if ((a.n !== 0n) === (op === 'or')) return op === 'or' ? ONE : ZERO;
      }
      return op === 'or' ? ZERO : ONE;
    }
    case 'not': {
      const a = value(0);
      return a === null ? null : a.n === 0n ? ONE : ZERO;
    }
    case 'daysBetween':
    case 'yearsBetween': {
      const from = dateOf(args[0] ?? '', scope);
      if (from === null) return null;
      const to = dateOf(args[1] ?? '', scope);
      if (to === null) return null;
      return integer(op === 'daysBetween' ? to - from : yearsFrom(from, to));
    }
    default:
      // today() is a date, never a number.
      throw new LogicError('invalid_operand');
  }
}

/**
 * Reads, checks and works out a formula over a scope (ADR-0063): canonical text, or null without a value. A mistake
 * in the formula is a LogicError with its position; a failure of the arithmetic is one without.
 */
export function evaluateFormula(formula: string, scope: FormulaScope): string | null {
  const tree = parseFormula(formula);
  checkFormula(tree, scope.kind);
  const result = calculate(tree, scope);
  return result === null ? null : formatDecimal(result);
}

/** A number as a field stores it: the wire form within the storage limits, in canonical form. */
export function storedNumber(text: string): string {
  const d = parseDecimal(text);
  if (d === null) throw new LogicError('not_a_number');
  return formatDecimal(stored(d));
}

/** The language as the form engine takes it (ADR-0114): reading a formula, checking it and working it out. */
export interface Formulas {
  readonly parse: (formula: string) => Calculation;
  readonly check: (calculation: Calculation, kind: (alias: string) => Kind | undefined) => string[];
  readonly calculate: (calculation: Calculation, scope: FormulaScope) => Decimal | null;
}

/** The calculation language, which the website runtime loads as a chunk of its own for a form with a calculation. */
export const FORMULAS: Formulas = { parse: parseFormula, check: checkFormula, calculate };
