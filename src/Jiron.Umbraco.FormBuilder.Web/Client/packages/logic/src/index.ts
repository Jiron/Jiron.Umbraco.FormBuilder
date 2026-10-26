/**
 * The shared logic (ROADMAP section 4.2, ADR-0005): rules, validation, merge tags and calculations, written once for
 * the website runtime, the headless element and the backoffice, the same as Core's in C#, and tested against the
 * shared vectors in tests/vectors. It has no DOM and no dependencies: its compiler settings leave out the DOM's types,
 * and the lint refuses imports from anywhere else. Step 3.4b ported the modules of the 1.5 spike; 5.2a added
 * the form engine (ADR-0061), 5.2b and 5.2c the built-in types' checks (ADR-0062), 5.3a the calculation language
 * (ADR-0063), and 5.3b and 5.3c the merge tags.
 */
export {
  calculate,
  checkFormula,
  evaluateFormula,
  MAX_FORMULA_DEPTH,
  MAX_FORMULA_LENGTH,
  parseFormula,
  storedNumber,
  type Calculation,
  type FieldReference,
  type FormulaScope,
  type Operation,
} from './calc.ts';
export {
  checkField,
  type CheckedField,
  type FieldAnswers,
  type FieldCheck,
  type FieldErrors,
  type FieldFile,
} from './checks.ts';
export { formatDecimal, parseDecimal, type Decimal } from './decimal.ts';
export { LogicError, type LogicErrorCode } from './errors.ts';
export {
  evaluateForm,
  type FormAnswers,
  type FormState,
  type LogicAction,
  type LogicCalculation,
  type LogicField,
  type LogicForm,
  type LogicFormGroup,
  type LogicGridRow,
  type LogicOutcome,
  type LogicPage,
  type LogicValidationRule,
  type RowState,
} from './form.ts';
export { compilePattern, type Pattern } from './pattern.ts';
export {
  evaluateCondition,
  evaluateRule,
  MAX_DEPTH,
  type Condition,
  type Context,
  type Group,
  type Kind,
  type Operand,
  type Rule,
  type Subject,
} from './rules.ts';
export { clean, countWords, fold, normalise } from './text.ts';
