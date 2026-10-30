/**
 * The shared logic (ROADMAP section 4.2, ADR-0005): rules, validation, merge tags and calculations, written once for
 * the website runtime, the headless element and the backoffice, the same as Core's in C#, and tested against the
 * shared vectors in tests/vectors. It has no DOM and no dependencies: its compiler settings leave out the DOM's types,
 * and the lint refuses imports from anywhere else. Step 3.4b ported the modules of the 1.5 spike; 5.2a added
 * the form engine (ADR-0061), 5.2b and 5.2c the built-in types' checks (ADR-0062), 5.3a the calculation language
 * (ADR-0063), 5.3b and 5.3c the merge tags, 5.3f answer recall with the answers' display texts (ADR-0068), and 5.4a
 * the forms of an email address, a phone number and a web address (ADR-0069), 6.2g the visitor messages
 * (ADR-0099), whose words are tables of their own in lang/, and 6.4a the reading of answers before the rules and checks
 * (ADR-0109).
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
  type CheckedRow,
  type FieldAnswers,
  type FieldCheck,
  type FieldErrors,
  type FieldFile,
} from './checks.ts';
export {
  DATE_FORMATS,
  formatCalendar,
  formatCurrency,
  formatNumber,
  minorUnits,
  type CultureFormats,
  type CurrencyFormat,
  type DateFormat,
  type DatePatterns,
  type NumberFormat,
} from './culture.ts';
export { formatDecimal, parseDecimal, type Decimal } from './decimal.ts';
export {
  displayAnswer,
  type AnswerValue,
  type DisplayedField,
  type DisplayOption,
  type DisplayRow,
  type DisplayWords,
} from './display.ts';
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
export {
  defaultMessage,
  fill,
  messageWordsOf,
  plural,
  summaryEntry,
  summaryHeading,
  type MessageField,
  type MessageOption,
  type MessageWords,
  type PluralWords,
  type VisitorError,
} from './messages.ts';
export { compilePattern, type Pattern } from './pattern.ts';
export {
  BUILT_IN_TYPES,
  checkRows,
  cleanAnswer,
  postedNumber,
  readField,
  type DeclaredType,
  type FieldReading,
  type PostedAnswer,
  type ReadField,
  type ReadValue,
} from './reading.ts';
export {
  MASKED,
  recallAnswers,
  type Recall,
  type RecallAnswer,
  type RecallAnswers,
  type RecallField,
  type RecallForm,
  type RecallGroup,
  type RecallPage,
} from './recall.ts';
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
export {
  MAX_TAG_FILTERS,
  MAX_TEMPLATE_OUTPUT,
  MAX_TEMPLATE_TAGS,
  parseTemplate,
  renderTemplate,
  TEMPLATE_CONTEXTS,
  TEMPLATE_WARNINGS,
  type MergeItem,
  type MergeValue,
  type MergeValues,
  type ParsedTemplate,
  type PathProblem,
  type RenderedTemplate,
  type TemplateContext,
  type TemplateProblem,
  type TemplateProblemCode,
} from './template.ts';
export { clean, countWords, fold, lower, normalise, upper } from './text.ts';
