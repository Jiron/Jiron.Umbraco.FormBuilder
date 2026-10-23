/**
 * The shared logic (ROADMAP section 4.2, ADR-0005): rules, validation, merge tags and calculations, written once for
 * the website runtime, the headless element and the backoffice, the same as Core's in C#, and tested against the
 * shared vectors in tests/vectors. It has no DOM and no dependencies: its compiler settings leave out the DOM's types,
 * and the lint refuses imports from anywhere else. Step 3.4b ported the modules of the 1.5 spike; 5.2 and 5.3
 * complete the engines.
 */
export { calculate, storedNumber, type Calculation } from './calc.ts';
export { formatDecimal, parseDecimal, type Decimal } from './decimal.ts';
export { LogicError, type LogicErrorCode } from './errors.ts';
export { compilePattern, type Pattern } from './pattern.ts';
export {
  evaluateCondition,
  evaluateRule,
  type Condition,
  type Context,
  type Group,
  type Kind,
  type Operand,
  type Rule,
  type Subject,
} from './rules.ts';
export { clean, fold, normalise } from './text.ts';
