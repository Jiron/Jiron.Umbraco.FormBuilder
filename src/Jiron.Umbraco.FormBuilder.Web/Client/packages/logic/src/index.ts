/**
 * The shared logic (ROADMAP section 4.2, ADR-0005): rules, validation, merge tags and calculations, written once for
 * the website runtime, the headless element and the backoffice, the same as Core's in C#, and tested against the
 * shared vectors in tests/vectors. It has no DOM and no dependencies: its compiler settings leave out the DOM's types,
 * and the lint refuses imports from anywhere else. Step 3.4b ported the modules of the 1.5 spike; 5.2a added
 * the form engine (ADR-0061), 5.2b and 5.2c the built-in types' checks (ADR-0062), 5.3a the calculation language
 * (ADR-0063), 5.3b and 5.3c the merge tags, 5.3f answer recall with the answers' display texts (ADR-0068), and 5.4a
 * the forms of an email address, a phone number and a web address (ADR-0069), 6.2g the visitor messages
 * (ADR-0099), whose words are tables of their own in lang/, 6.4a the reading of answers before the rules and checks
 * (ADR-0109), 6.4b the reading of a page's post by its names, as the server reads one (ADR-0110), 6.4c the form
 * engine's model made of the form endpoint's answer, as Core makes it of the definition (ADR-0111), and 6.4d the empty
 * submission and the words of each language as a module of its own, `@jfb/logic/lang/<language>` (ADR-0112),
 * 6.4f the calculation language as one, `@jfb/logic/calc`, which the form engine takes (ADR-0114), and 6.4g the
 * merge tags as two, `@jfb/logic/template` and `@jfb/logic/recall`, which only a form with a text that recalls
 * answers loads (ADR-0115), and 9.5d the rule search, which the rules overview and the form check ask whether rules
 * conflict or an element ever shows (ADR-0179); the website runtime leaves it out of its bundle. 23.1 added the view
 * of an object's members through its prototype chain, `@jfb/logic/proto`, which the website's code reads every member
 * of a form through (ADR-0260).
 */
// An email address as the email field checks it, which the backoffice's address boxes check too (ADR-0189).
export { isEmailAddress } from './forms.ts';
// The calculation language is a module of its own, `@jfb/logic/calc`, that only a form with a calculation loads.
export type { Calculation, FieldReference, Formulas, FormulaScope, Operation } from './calc.ts';
export {
  checkField,
  type CheckedField,
  type CheckedOption,
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
export { applyRelative, formatDate, parseDate, parseDateTime, parseRelative, parseTime, today } from './dates.ts';
export { formatDecimal, parseDecimal, type Decimal } from './decimal.ts';
export {
  calculationText,
  displayAnswer,
  sliderText,
  type AnswerValue,
  type DisplayedField,
  type DisplayOption,
  type DisplayRow,
  type DisplayWords,
} from './display.ts';
export { LogicError, own, type LogicErrorCode } from './errors.ts';
export {
  evaluateForm,
  hasCalculations,
  hasCycle,
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
  rewordMessages,
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
  logicFormOf,
  type FieldLogic,
  type ProjectedField,
  type ProjectedForm,
  type ProjectedItem,
  type ProjectedRule,
  type ProjectedValidationRule,
} from './projection.ts';
export {
  holdQuery,
  isEmptyPost,
  LONGEST_QUERY_VALUE,
  MOST_QUERY_VALUES,
  MOST_ROWS,
  readPost,
  type PostAnswer,
  type PostField,
  type ReadPost,
} from './posts.ts';
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
// The merge tags are two modules of their own, `@jfb/logic/template` and `@jfb/logic/recall`, that only a form with
// a text that recalls answers loads.
export type {
  Recall,
  RecallAnswer,
  RecallAnswers,
  RecallField,
  RecallForm,
  RecallGroup,
  RecallPage,
} from './recall.ts';
export {
  checkGroup,
  contextKindOf,
  evaluateCondition,
  evaluateRule,
  isGroup,
  MAX_DEPTH,
  operatorsFor,
  type Condition,
  type Context,
  type Group,
  type Kind,
  type Operand,
  type OperandShape,
  type Rule,
  type Subject,
} from './rules.ts';
export {
  conflictOf,
  formSight,
  MOST_TRIED,
  showsAtAll,
  type Conflict,
  type SearchAnswer,
  type SearchRule,
  type SearchScope,
  type Showing,
} from './search.ts';
export type {
  MergeItem,
  MergeValue,
  MergeValues,
  ParsedTemplate,
  PathProblem,
  RenderedTemplate,
  TemplateContext,
  TemplateProblem,
  TemplateProblemCode,
} from './template.ts';
export { clean, countWords, fold, lower, normalise, upper } from './text.ts';
