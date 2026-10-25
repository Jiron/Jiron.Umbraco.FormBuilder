// Patterns (ADR-0005): a small subset of regular-expression syntax that Jiron Form Builder runs with its own matcher,
// the same algorithm in C#. A pattern is parsed (everything outside the subset is refused), compiled to a Thompson
// automaton and run over the value's code points in time linear in the value's length, so no pattern can hang a
// browser or a server, and no engine's dialect decides what a pattern means. A pattern always matches the whole
// value; it is case-sensitive.
import { LogicError } from './errors.ts';
import { normalise } from './text.ts';

/** The longest pattern, in code points. */
export const MAX_PATTERN_LENGTH = 500;
/** The largest count a {n,m} quantifier may name. */
export const MAX_COUNT = 1000;
/** The largest pattern once counted repeats are unrolled, in matched characters. */
export const MAX_WEIGHT = 2000;
/**
 * The largest automaton, in instructions: bounds the work per character, also for repeats of what matches no
 * character (anchors, empty groups), which MAX_WEIGHT does not count (ADR-0030).
 */
export const MAX_INSTRUCTIONS = 10000;

type Range = readonly [number, number];

interface CharSet {
  readonly ranges: readonly Range[];
  readonly negated: boolean;
}

type Node =
  | { readonly kind: 'set'; readonly set: CharSet }
  | { readonly kind: 'seq'; readonly items: readonly Node[] }
  | { readonly kind: 'alt'; readonly options: readonly Node[] }
  | { readonly kind: 'repeat'; readonly node: Node; readonly min: number; readonly max: number | null }
  | { readonly kind: 'start' }
  | { readonly kind: 'end' };

const MAX_CODE_POINT = 0x10ffff;
const DIGITS: readonly Range[] = [[48, 57]];
const WORD: readonly Range[] = [
  [48, 57],
  [65, 90],
  [95, 95],
  [97, 122],
];
const SPACE: readonly Range[] = [
  [9, 13],
  [32, 32],
];
const LINE_ENDS: readonly Range[] = [
  [10, 10],
  [13, 13],
  [0x2028, 0x2029],
];

// The characters the parser looks for, as code points.
const BAR = 124;
const OPEN = 40;
const CLOSE = 41;
const QUESTION = 63;
const COLON = 58;
const BRACKET = 91;
const BRACKET_CLOSE = 93;
const DOT = 46;
const BACKSLASH = 92;
const CARET = 94;
const DOLLAR = 36;
const STAR = 42;
const PLUS = 43;
const BRACE = 123;
const BRACE_CLOSE = 125;
const COMMA = 44;
const HYPHEN = 45;

function refuse(): never {
  throw new LogicError('unsafe_pattern');
}

const isDigit = (c: number): boolean => c >= 48 && c <= 57;
const isAlphanumeric = (c: number): boolean => isDigit(c) || (c >= 65 && c <= 90) || (c >= 97 && c <= 122);
const isSurrogate = (c: number): boolean => c >= 0xd800 && c <= 0xdfff;

function hexValue(c: number): number {
  if (isDigit(c)) return c - 48;
  if (c >= 65 && c <= 70) return c - 55;
  if (c >= 97 && c <= 102) return c - 87;
  return -1;
}

/** The ranges outside the given ones, within all code points. */
function complement(ranges: readonly Range[]): Range[] {
  const sorted = [...ranges].sort((a, b) => a[0] - b[0]);
  const out: Range[] = [];
  let next = 0;
  for (const [low, high] of sorted) {
    if (low > next) out.push([next, low - 1]);
    next = Math.max(next, high + 1);
  }
  if (next <= MAX_CODE_POINT) out.push([next, MAX_CODE_POINT]);
  return out;
}

const codePoints = (text: string): number[] => Array.from(text, (ch) => ch.codePointAt(0) ?? 0);

/** What an escape or class member stands for: one code point, or a set of ranges (d, w, s and their negations). */
interface Escaped {
  readonly point: number;
  readonly ranges: readonly Range[] | null;
  readonly raw: boolean;
}

const point = (value: number, raw = false): Escaped => ({ point: value, ranges: null, raw });
const ranges = (value: readonly Range[]): Escaped => ({ point: 0, ranges: value, raw: false });

class Parser {
  private readonly points: readonly number[];
  private i = 0;

  constructor(points: readonly number[]) {
    this.points = points;
  }

  get done(): boolean {
    return this.i >= this.points.length;
  }

  /** alternation: sequence ("|" sequence)* */
  alternation(): Node {
    const options = [this.sequence()];
    while (this.peek() === BAR) {
      this.next();
      options.push(this.sequence());
    }
    return options.length === 1 ? (options[0] ?? refuse()) : { kind: 'alt', options };
  }

  private peek(offset = 0): number {
    return this.points[this.i + offset] ?? -1;
  }

  private next(): number {
    const c = this.points[this.i];
    if (c === undefined) refuse();
    this.i++;
    return c;
  }

  private sequence(): Node {
    const items: Node[] = [];
    while (!this.done && this.peek() !== BAR && this.peek() !== CLOSE) {
      const atom = this.atom();
      items.push(atom.kind === 'start' || atom.kind === 'end' ? atom : this.quantified(atom));
    }
    return { kind: 'seq', items };
  }

  private atom(): Node {
    const c = this.next();
    switch (c) {
      case OPEN: {
        if (this.peek() === QUESTION) {
          this.next();
          if (this.next() !== COLON) refuse(); // only "(?:" among the "(?" forms
        }
        const inner = this.alternation();
        if (this.next() !== CLOSE) refuse();
        return inner;
      }
      case BRACKET:
        return this.characterClass();
      case DOT:
        return { kind: 'set', set: { ranges: LINE_ENDS, negated: true } };
      case BACKSLASH: {
        const escaped = this.escape();
        return { kind: 'set', set: { ranges: escaped.ranges ?? [[escaped.point, escaped.point]], negated: false } };
      }
      case CARET:
        return { kind: 'start' };
      case DOLLAR:
        return { kind: 'end' };
      case STAR:
      case PLUS:
      case QUESTION:
      case BRACE:
        return refuse(); // nothing to repeat
      case BRACKET_CLOSE:
      case BRACE_CLOSE:
        return refuse(); // a lone "]" or "}" must be escaped
      default:
        return { kind: 'set', set: { ranges: [[c, c]], negated: false } };
    }
  }

  private quantified(atom: Node): Node {
    const c = this.peek();
    let min: number;
    let max: number | null;
    if (c === STAR) [min, max] = [0, null];
    else if (c === PLUS) [min, max] = [1, null];
    else if (c === QUESTION) [min, max] = [0, 1];
    else if (c === BRACE) {
      this.next();
      [min, max] = this.counts();
    } else return atom;
    if (c !== BRACE) this.next();
    if (this.peek() === QUESTION) this.next(); // a lazy quantifier matches the same values
    // A second quantifier now finds nothing to repeat, so stacked quantifiers are refused by atom().
    return { kind: 'repeat', node: atom, min, max };
  }

  /** Reads "n}", "n,}" or "n,m}" after a "{", up to and including the "}". */
  private counts(): [number, number | null] {
    const number = (): number | null => {
      let digits = 0;
      let value = 0;
      while (isDigit(this.peek()) && digits < 5) {
        value = value * 10 + (this.next() - 48);
        digits++;
      }
      return digits === 0 ? null : value;
    };
    const low = number() ?? refuse();
    let high: number | null = low;
    if (this.peek() === COMMA) {
      this.next();
      high = this.peek() === BRACE_CLOSE ? null : (number() ?? refuse());
    }
    if (this.next() !== BRACE_CLOSE || low > MAX_COUNT || (high !== null && (high > MAX_COUNT || high < low))) refuse();
    return [low, high];
  }

  private hex(count: number): number {
    let value = 0;
    for (let k = 0; k < count; k++) {
      const digit = hexValue(this.next());
      if (digit < 0) refuse();
      value = value * 16 + digit;
    }
    return value;
  }

  /** Reads what follows a backslash. Refuses backreferences, boundaries, properties and unknown letters. */
  private escape(): Escaped {
    const c = this.next();
    switch (String.fromCharCode(c)) {
      case 'd':
        return ranges(DIGITS);
      case 'w':
        return ranges(WORD);
      case 's':
        return ranges(SPACE);
      case 'D':
        return ranges(complement(DIGITS));
      case 'W':
        return ranges(complement(WORD));
      case 'S':
        return ranges(complement(SPACE));
      case 't':
        return point(9);
      case 'n':
        return point(10);
      case 'v':
        return point(11);
      case 'f':
        return point(12);
      case 'r':
        return point(13);
      case 'u':
      case 'x': {
        const value = this.hex(c === 117 ? 4 : 2);
        return isSurrogate(value) ? refuse() : point(value);
      }
      default:
        return c >= 32 && c <= 126 && !isAlphanumeric(c) ? point(c) : refuse();
    }
  }

  /** A class member: an escape or a raw code point. A raw "[" is refused, as in no common dialect it is plain. */
  private member(c: number): Escaped {
    if (c === BRACKET) refuse();
    return c === BACKSLASH ? this.escape() : point(c, true);
  }

  /**
   * Reads a class after its "[": code points, ranges between two code points, and the escaped sets. A raw "-" is a
   * literal only first or last; anywhere else it must join a range, and it never ends one.
   */
  private characterClass(): Node {
    let negated = false;
    if (this.peek() === CARET) {
      this.next();
      negated = true;
    }
    if (this.peek() === BRACKET_CLOSE) refuse(); // "[]" and "[^]" mean different things in common dialects
    const members: Range[] = [];
    let first = true;
    for (;;) {
      const c = this.next();
      if (c === BRACKET_CLOSE) return { kind: 'set', set: { ranges: members, negated } };
      const item = this.member(c);
      const hyphen = item.ranges === null && item.raw && item.point === HYPHEN;
      const range = this.peek() === HYPHEN && this.peek(1) !== BRACKET_CLOSE;
      if (hyphen && !first && this.peek() !== BRACKET_CLOSE) refuse();
      first = false;
      if (!range) {
        if (item.ranges === null) members.push([item.point, item.point]);
        else members.push(...item.ranges);
        continue;
      }
      if (item.ranges !== null || hyphen) refuse(); // a set or a raw "-" cannot start a range
      this.next();
      const high = this.member(this.next());
      if (high.ranges !== null || (high.raw && high.point === HYPHEN) || high.point < item.point) refuse();
      members.push([item.point, high.point]);
    }
  }
}

/** Caps a count just past its limit, so nested repeats stay small numbers while they are added up. */
const cap = (value: number, limit: number): number => Math.min(value, limit + 1);

/** How many characters the node matches once counted repeats are unrolled, capped past MAX_WEIGHT. */
function weight(node: Node): number {
  switch (node.kind) {
    case 'set':
      return 1;
    case 'seq':
      return node.items.reduce((sum, item) => cap(sum + weight(item), MAX_WEIGHT), 0);
    case 'alt':
      return node.options.reduce((sum, option) => cap(sum + weight(option), MAX_WEIGHT), 0);
    case 'repeat':
      return cap(weight(node.node) * Math.max(node.max ?? node.min, 1), MAX_WEIGHT);
    default:
      return 0;
  }
}

/** How many instructions the compiler emits for the node, capped past MAX_INSTRUCTIONS. */
function size(node: Node): number {
  switch (node.kind) {
    case 'seq':
      return node.items.reduce((sum, item) => cap(sum + size(item), MAX_INSTRUCTIONS), 0);
    case 'alt': {
      // Every option but the last brings a split and a jump.
      const options = node.options.reduce((sum, option) => cap(sum + size(option), MAX_INSTRUCTIONS), 0);
      return cap(options + 2 * (node.options.length - 1), MAX_INSTRUCTIONS);
    }
    case 'repeat':
      // x{n,} is n copies and a split; x* is one copy, a split and a jump; x{n,m} is m copies, of which the last
      // m - n each start with a split.
      if (node.max === null) {
        return cap(size(node.node) * Math.max(node.min, 1) + (node.min > 0 ? 1 : 2), MAX_INSTRUCTIONS);
      }
      return cap(size(node.node) * node.max + (node.max - node.min), MAX_INSTRUCTIONS);
    default:
      return 1; // a set, a start or an end
  }
}

// The automaton as a program: each instruction is [op, a, b].
const CHAR = 0; // a: index of the set; then the next instruction
const SPLIT = 1; // continue at a and at b
const JUMP = 2; // continue at a
const START = 3; // only at the first position; then the next instruction
const END = 4; // only after the last position; then the next instruction
const MATCH = 5;

type Instruction = [op: number, a: number, b: number];

interface Program {
  readonly code: readonly Readonly<Instruction>[];
  readonly sets: readonly CharSet[];
}

function compile(root: Node): Program {
  const code: Instruction[] = [];
  const sets: CharSet[] = [];
  const emit = (op: number, a = 0, b = 0): number => code.push([op, a, b]) - 1;
  const patch = (at: number, a: number, b?: number): void => {
    const instruction = code[at];
    if (instruction === undefined) return;
    instruction[1] = a;
    if (b !== undefined) instruction[2] = b;
  };
  const walk = (node: Node): void => {
    switch (node.kind) {
      case 'set':
        emit(CHAR, sets.push(node.set) - 1);
        return;
      case 'start':
        emit(START);
        return;
      case 'end':
        emit(END);
        return;
      case 'seq':
        node.items.forEach(walk);
        return;
      case 'alt': {
        const jumps: number[] = [];
        node.options.forEach((option, index) => {
          if (index === node.options.length - 1) {
            walk(option);
            return;
          }
          const split = emit(SPLIT);
          walk(option);
          jumps.push(emit(JUMP));
          patch(split, split + 1, code.length);
        });
        jumps.forEach((jump) => patch(jump, code.length));
        return;
      }
      case 'repeat': {
        const { min, max } = node;
        for (let k = 0; k < min - (max === null && min > 0 ? 1 : 0); k++) walk(node.node);
        if (max === null) {
          if (min > 0) {
            const loop = code.length; // x+ : x, then back to x or on
            walk(node.node);
            const split = emit(SPLIT);
            patch(split, loop, split + 1);
          } else {
            const split = emit(SPLIT); // x* : x and back, or on
            walk(node.node);
            emit(JUMP, split);
            patch(split, split + 1, code.length);
          }
          return;
        }
        const splits: number[] = [];
        for (let k = min; k < max; k++) {
          splits.push(emit(SPLIT));
          walk(node.node);
        }
        splits.forEach((split) => patch(split, split + 1, code.length));
        return;
      }
    }
  };
  walk(root);
  emit(MATCH);
  return { code, sets };
}

function inSet(set: CharSet | undefined, value: number): boolean {
  if (set === undefined) return false;
  for (const [low, high] of set.ranges) if (value >= low && value <= high) return !set.negated;
  return set.negated;
}

/**
 * Runs the automaton over the code points: every thread advances together, so time is linear in the length. One set
 * of buffers serves the whole run: the threads at a position, those that advance, and the closure's stack.
 */
function run(program: Program, points: readonly number[]): boolean {
  const { code, sets } = program;
  const seen = new Int32Array(code.length).fill(-1);
  const threads = new Int32Array(code.length);
  const advanced = new Int32Array(code.length);
  // A closure reaches each instruction once and pushes at most two more from it, besides its starts. A typed array
  // drops a write past its end without a word, so every push checks the bound and stops the run instead.
  const stack = new Int32Array(3 * code.length);
  const push = (top: number, pc: number): number => {
    if (top >= stack.length) throw new Error('The pattern run outgrew its stack.');
    stack[top] = pc;
    return top + 1;
  };
  let generation = 0;
  let threadCount = 0;
  let advancedCount = 1; // the first instruction, where every run starts
  // Fills threads with what the advanced threads reach at the position without reading a character.
  const closure = (position: number): void => {
    generation++;
    threadCount = 0;
    let top = 0;
    for (let k = advancedCount - 1; k >= 0; k--) top = push(top, advanced[k] ?? 0);
    while (top > 0) {
      const pc = stack[--top] ?? 0;
      const instruction = code[pc];
      if (instruction === undefined || seen[pc] === generation) continue;
      seen[pc] = generation;
      const [op, a, b] = instruction;
      if (op === SPLIT) {
        top = push(top, b);
        top = push(top, a);
      } else if (op === JUMP) top = push(top, a);
      else if (op === START) {
        if (position === 0) top = push(top, pc + 1);
      } else if (op === END) {
        if (position === points.length) top = push(top, pc + 1);
      } else threads[threadCount++] = pc;
    }
  };
  closure(0);
  for (let position = 0; position < points.length && threadCount > 0; position++) {
    const value = points[position] ?? 0;
    advancedCount = 0;
    for (let t = 0; t < threadCount; t++) {
      const pc = threads[t] ?? 0;
      const instruction = code[pc];
      if (instruction !== undefined && instruction[0] === CHAR && inSet(sets[instruction[1]], value))
        advanced[advancedCount++] = pc + 1;
    }
    closure(position + 1);
  }
  for (let t = 0; t < threadCount; t++) if (code[threads[t] ?? 0]?.[0] === MATCH) return true;
  return false;
}

/** A compiled pattern: test() tells whether a whole value matches. */
export interface Pattern {
  test(value: string): boolean;
}

/** Parses and compiles an editor's pattern, or refuses it with `unsafe_pattern`. */
export function compilePattern(pattern: string): Pattern {
  const points = codePoints(normalise(pattern));
  if (points.length > MAX_PATTERN_LENGTH) refuse();
  const parser = new Parser(points);
  const root = parser.alternation();
  if (!parser.done) refuse(); // an unmatched ")"
  if (weight(root) > MAX_WEIGHT || size(root) > MAX_INSTRUCTIONS) refuse();
  const program = compile(root);
  return { test: (value) => run(program, codePoints(value)) };
}
