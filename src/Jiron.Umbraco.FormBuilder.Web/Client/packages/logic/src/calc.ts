// Calculations over exact decimals, and numbers as they are stored (ADR-0005); step 5.3 adds the expression language.
import {
  add,
  divide,
  formatDecimal,
  MAX_ROUND_PLACES,
  multiply,
  operand,
  parseDecimal,
  round,
  stored,
  subtract,
  type Decimal,
} from './decimal.ts';
import { LogicError } from './errors.ts';

/** A calculation: a decimal constant in the wire form, or an operation over calculations. */
export type Calculation =
  string | { readonly op: string; readonly args: readonly Calculation[]; readonly scale?: number };

const BINARY: ReadonlyMap<string, (a: Decimal, b: Decimal) => Decimal> = new Map([
  ['add', add],
  ['sub', subtract],
  ['mul', multiply],
  ['div', divide],
]);

/** Evaluates a calculation: each operation's shape is checked, then its arguments run left to right. */
export function calculate(calculation: Calculation): Decimal {
  if (typeof calculation === 'string') return operand(calculation);
  const { op, args } = calculation;
  const binary = BINARY.get(op);
  if (binary !== undefined) {
    const [first, second] = args;
    if (args.length !== 2 || first === undefined || second === undefined) throw new LogicError('invalid_operand');
    return binary(calculate(first), calculate(second));
  }
  if (op !== 'round' && op !== 'store') throw new LogicError('unknown_operator');
  const [only] = args;
  if (args.length !== 1 || only === undefined) throw new LogicError('invalid_operand');
  if (op === 'store') return stored(calculate(only));
  const scale = calculation.scale;
  if (scale === undefined || !Number.isInteger(scale) || scale < 0 || scale > MAX_ROUND_PLACES) {
    throw new LogicError('invalid_operand');
  }
  return round(calculate(only), scale);
}

/** A number as a field stores it: the wire form within the storage limits, in canonical form. */
export function storedNumber(text: string): string {
  const d = parseDecimal(text);
  if (d === null) throw new LogicError('not_a_number');
  return formatDecimal(stored(d));
}
