// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The calculation language (ROADMAP 5.3a, ADR-0063): the grammar and where each mistake starts, the functions, what
// each kind of field gives a formula, the date functions around leap days and daylight-saving changes, division and
// rounding at their edges, and calculations in whole forms, worked out in dependency order and read rounded by rules.
// A `formula` case expects canonical text, null without a value, or { error, at } for a mistake in the formula; a
// failure of the arithmetic has no position. Positions are found in the formula's own text, never counted by hand.

const defaults = {
  context: {
    now: '2026-06-15T10:00:00Z',
    timeZone: 'Europe/Zurich',
    culture: 'de-CH',
    member: { signedIn: false, groups: [] },
    query: {},
    hidden: [],
  },
};

const fields = {
  qty: 'number',
  price: 'number',
  round: 'number',
  plan: 'choice',
  extras: 'choices',
  insured: 'checkbox',
  name: 'text',
  photos: 'files',
  start: 'time',
  birth: 'date',
  arrival: 'date',
  departure: 'date',
  meeting: 'dateTime',
  until: 'dateTime',
};

/** A formula case. `more` adds a description, a context or field kinds. */
function c(id, formula, values, expect, more = {}) {
  const { description, context, kinds } = more;
  return {
    id,
    ...(description === undefined ? {} : { description }),
    ...(context === undefined ? {} : { context }),
    ...(kinds === undefined ? {} : { fields: kinds }),
    values,
    formula,
    expect,
  };
}

/** A mistake in the formula, at the first place `token` appears in it (or at `index` when given as a number). */
function at(code, formula, token) {
  const index = typeof token === 'number' ? token : formula.indexOf(token);
  if (index < 0) throw new Error(`"${token}" is not in "${formula}"`);
  return { error: code, at: index };
}

/** A failure of the arithmetic, which has no position. */
const fails = (code) => ({ error: code });

const MAX_DIGITS_NUMBER = '9'.repeat(60);
const FIFTEEN_NINES = '9'.repeat(15);
const pow = (base, times) => Array.from({ length: times }, () => base).join(' * ');

// ---- The grammar ----
const grammar = [
  c('add-is-exact', 'qty + 0.2', { qty: ['0.1'] }, '0.3', { description: 'a binary double makes 0.30000000000000004' }),
  c('subtract-is-exact', 'qty - 0.9', { qty: ['1'] }, '0.1'),
  c('multiply-is-exact', 'qty * 1.1', { qty: ['1.1'] }, '1.21'),
  c('multiply-before-add', 'qty + 2 * 3', { qty: ['1'] }, '7'),
  c('divide-before-subtract', 'qty - 6 / 3', { qty: ['10'] }, '8'),
  c('parentheses-first', '(qty + 2) * 3', { qty: ['1'] }, '9'),
  c('subtract-left-to-right', 'qty - 2 - 3', { qty: ['10'] }, '5'),
  c('divide-left-to-right', 'qty / 2 / 5', { qty: ['100'] }, '10'),
  c('negation', '-qty', { qty: ['5'] }, '-5'),
  c('negation-twice', '--qty', { qty: ['5'] }, '5'),
  c('negation-three-times', '- - -qty', { qty: ['5'] }, '-5'),
  c('negation-after-an-operator', '2 - -qty', { qty: ['3'] }, '5'),
  c('negation-of-a-group', '-(qty - 8)', { qty: ['3'] }, '5'),
  c('negation-of-zero-is-zero', '-qty', { qty: ['0'] }, '0', { description: 'never "-0"' }),
  c('many-negations-are-not-nested', '-'.repeat(901) + 'qty', { qty: ['2'] }, '-2', { description: 'they are counted, so they never reach the depth limit' }),
  c('spaces-tabs-and-line-breaks-between-tokens', ' qty\t*' + String.fromCharCode(10) + '2' + String.fromCharCode(13) + ' ', { qty: ['5'] }, '10'),
  c('no-spaces-needed', 'qty*2+1', { qty: ['5'] }, '11'),
  c('a-literal-with-a-fraction', 'qty * 0.5', { qty: ['3'] }, '1.5'),
  c('a-literal-keeps-its-trailing-zeros-out', 'qty * 1.50', { qty: ['2'] }, '3'),
  c('a-literal-of-sixty-digits', 'qty + ' + MAX_DIGITS_NUMBER, { qty: ['0'] }, MAX_DIGITS_NUMBER),
  c('a-literal-of-sixty-one-digits', 'qty + 1' + '0'.repeat(60), { qty: ['0'] }, at('out_of_range', 'qty + 1' + '0'.repeat(60), '1')),
  c('a-literal-whose-fraction-ends-in-zeros-is-in-range', 'qty + 1.' + '0'.repeat(200), { qty: ['1'] }, '2', { description: 'trailing zeros of a fraction are no digits' }),
  c('a-name-before-a-parenthesis-may-have-spaces', 'min (qty, 1)', { qty: ['5'] }, '1'),
  c('a-field-may-be-named-like-a-function', 'round + 1', { round: ['2'] }, '3', { description: 'a name followed by ( calls a function; any other name is a field' }),
  c('a-field-named-like-a-function-inside-it', 'round(round / 4)', { round: ['2'] }, '1'),
  c('a-comparison-in-parentheses-is-a-number', '(qty < 5) + (qty < 6)', { qty: ['5'] }, '1'),
];

// ---- Mistakes in the formula, each where it starts ----
const mistakes = [
  c('empty-formula', '', {}, at('invalid_formula', '', 0)),
  c('only-spaces', '   ', {}, at('invalid_formula', '   ', 3)),
  c('an-operator-at-the-end', 'qty +', {}, at('invalid_formula', 'qty +', 5)),
  c('two-operators-in-a-row', 'qty * * 2', {}, at('invalid_formula', 'qty * * 2', 6)),
  c('a-parenthesis-left-open', '(qty + 1', {}, at('invalid_formula', '(qty + 1', 8)),
  c('a-parenthesis-closed-twice', 'qty + 1)', {}, at('invalid_formula', 'qty + 1)', ')')),
  c('empty-parentheses', '()', {}, at('invalid_formula', '()', ')')),
  c('a-number-with-a-leading-zero', 'qty * 07', {}, at('invalid_formula', 'qty * 07', '07')),
  c('two-zeros', 'qty * 00', {}, at('invalid_formula', 'qty * 00', '00')),
  c('zero-point-is-a-number', 'qty * 0.5', { qty: ['4'] }, '2'),
  c('a-point-without-digits-after-it', 'qty * 5.', {}, at('invalid_formula', 'qty * 5.', 8)),
  c('a-point-without-digits-before-it', 'qty * .5', {}, at('invalid_formula', 'qty * .5', '.')),
  c('two-points', 'qty * 1.2.3', {}, at('invalid_formula', 'qty * 1.2.3', '.3')),
  c('an-exponent', 'qty * 1e3', {}, at('invalid_formula', 'qty * 1e3', 'e3')),
  c('a-decimal-comma', 'qty * 1,5', {}, at('invalid_formula', 'qty * 1,5', ',')),
  c('an-unknown-operator-character', 'qty % 2', {}, at('invalid_formula', 'qty % 2', '%')),
  c('a-multiplication-sign', 'qty ' + String.fromCharCode(0xd7) + ' 2', {}, at('invalid_formula', 'qty ' + String.fromCharCode(0xd7) + ' 2', 4)),
  c('digits-of-another-script', 'qty * ' + String.fromCharCode(0x663), {}, at('invalid_formula', 'qty * ' + String.fromCharCode(0x663), 6)),
  c('a-letter-outside-ascii-ends-the-name', 'qt' + String.fromCharCode(0xe4), {}, at('invalid_formula', 'qt' + String.fromCharCode(0xe4), 2), { description: 'aliases are ASCII letters and digits' }),
  c('an-underscore-ends-the-name', 'qty_total', {}, at('invalid_formula', 'qty_total', '_')),
  c('a-letter-outside-ascii-starts-no-name', String.fromCharCode(0xe4) + 'qty', {}, at('invalid_formula', String.fromCharCode(0xe4) + 'qty', 0)),
  c('digits-of-another-script-after-a-digit', 'qty * 1' + String.fromCharCode(0x663), {}, at('invalid_formula', 'qty * 1' + String.fromCharCode(0x663), 7), { description: 'a number is ASCII digits; the next digit ends it' }),
  c('a-no-break-space-is-no-space', 'qty' + String.fromCharCode(0xa0) + '* 2', {}, at('invalid_formula', 'qty' + String.fromCharCode(0xa0) + '* 2', 3), { description: 'spaces, tabs and line breaks only' }),
  c('a-plus-sign-before-a-value', '+qty', {}, at('invalid_formula', '+qty', 0), { description: 'only "-" stands before a value' }),
  c('equals-twice', 'qty == 5', {}, at('invalid_formula', 'qty == 5', '= 5')),
  c('not-equal-written-otherwise', 'qty <> 5', {}, at('invalid_formula', 'qty <> 5', '>')),
  c('an-exclamation-mark-alone', '!qty', {}, at('invalid_formula', '!qty', 0)),
  c('an-exclamation-mark-before-a-value', 'qty ! 5', {}, at('invalid_formula', 'qty ! 5', '!')),
  c('two-comparisons-in-a-row', 'qty < 5 < 6', {}, at('invalid_formula', 'qty < 5 < 6', '< 6')),
  c('two-comparisons-in-parentheses', '(qty < 5 < 6)', {}, at('invalid_formula', '(qty < 5 < 6)', '< 6')),
  c('a-comma-before-the-end-of-a-call', 'min(qty, 1,)', {}, at('invalid_formula', 'min(qty, 1,)', ')')),
  c('a-comma-missing-in-a-call', 'min(qty 1)', {}, at('invalid_formula', 'min(qty 1)', '1)')),
  c('a-call-left-open', 'min(qty, 1', {}, at('invalid_formula', 'min(qty, 1', 10)),
  c('an-empty-argument', 'min(, qty)', {}, at('invalid_formula', 'min(, qty)', ',')),
  c('an-unknown-function', 'pow(qty, 2)', {}, at('unknown_operator', 'pow(qty, 2)', 0)),
  c('functions-are-written-as-they-are', 'Min(qty, 1)', {}, at('unknown_operator', 'Min(qty, 1)', 0)),
  c('a-function-named-constructor', 'constructor(qty)', {}, at('unknown_operator', 'constructor(qty)', 0), { description: 'functions are looked up among the language\'s own, never an object\'s inherited names' }),
  c('a-function-named-to-string', 'toString(qty)', {}, at('unknown_operator', 'toString(qty)', 0)),
  c('the-name-of-an-operation-is-no-function', 'add(qty, 1)', {}, at('unknown_operator', 'add(qty, 1)', 0), { description: 'the tree\'s own names (add, neg, store) are not functions of the language' }),
  c('negation-is-no-function', 'neg(qty)', {}, at('unknown_operator', 'neg(qty)', 0)),
  c('store-is-no-function', 'store(qty)', {}, at('unknown_operator', 'store(qty)', 0)),
  c('a-field-is-no-function', 'qty(2)', {}, at('unknown_operator', 'qty(2)', 0)),
  c('a-formula-of-a-thousand-characters', 'qty' + ' + 0'.repeat(249) + ' ', { qty: ['1'] }, '1', { description: 'exactly 1,000 characters' }),
  c('a-formula-of-a-thousand-and-one-characters', 'qty' + ' + 0'.repeat(249) + '  ', { qty: ['1'] }, at('invalid_formula', 'qty', 1000)),
  c('twenty-levels-of-parentheses', '('.repeat(20) + 'qty' + ')'.repeat(20), { qty: ['1'] }, '1'),
  c('twenty-one-levels-of-parentheses', '('.repeat(21) + 'qty' + ')'.repeat(21), { qty: ['1'] }, at('too_deep', 'qty', 20), { description: 'the twenty-first parenthesis' }),
  c('twenty-levels-of-calls', 'not('.repeat(20) + 'qty' + ')'.repeat(20), { qty: ['3'] }, '1', { description: 'not(3) is 0, and an even number of nots gives 1' }),
  c('twenty-one-levels-of-calls', 'not('.repeat(21) + 'qty' + ')'.repeat(21), {}, at('too_deep', 'qty', 80)),
  c('calls-and-parentheses-count-together', 'not(('.repeat(10) + '(qty)' + '))'.repeat(10), {}, at('too_deep', 'qty', 50)),
  c('levels-close-as-they-end', '(((((((((((qty))))))))))) + ' + '('.repeat(20) + 'qty' + ')'.repeat(20), { qty: ['1'] }, '2', { description: 'eleven, then twenty, never thirty-one' }),
  c('a-formula-that-names-no-field', '1 + 2', {}, at('constant_formula', '1 + 2', 0)),
  c('only-today-names-no-field', 'daysBetween(today(), today())', {}, at('constant_formula', 'daysBetween(today(), today())', 0)),
  c('the-grammar-before-the-fields', 'nope + )', {}, at('invalid_formula', 'nope + )', ')'), { description: 'the whole formula is read before any field is looked up' }),
  c('the-first-unknown-field', 'nope + nada', {}, at('unknown_field', 'nope + nada', 'nope')),
  c('an-unknown-field-later', 'qty + nope', {}, at('unknown_field', 'qty + nope', 'nope')),
  c('aliases-are-written-as-they-are', 'Qty + 1', {}, at('unknown_field', 'Qty + 1', 0)),
  c('a-field-named-constructor', 'constructor + 1', {}, at('unknown_field', 'constructor + 1', 0)),
  c('a-field-named-has-own-property', 'hasOwnProperty + 1', {}, at('unknown_field', 'hasOwnProperty + 1', 0)),
];

// ---- Functions ----
const functions = [
  c('min-of-three', 'min(qty, price, 3)', { qty: ['5'], price: ['-2'] }, '-2'),
  c('max-of-three', 'max(qty, price, 3)', { qty: ['5'], price: ['-2'] }, '5'),
  c('min-of-equal-values', 'min(qty, 2.0)', { qty: ['2'] }, '2'),
  c('max-compares-by-value', 'max(qty, 9)', { qty: ['10'] }, '10', { description: 'never as text, where "9" comes after "10"' }),
  c('max-of-fractions', 'max(qty, 0.25)', { qty: ['0.3'] }, '0.3'),
  c('min-takes-two-or-more', 'min(qty)', {}, at('invalid_operand', 'min(qty)', 0)),
  c('max-without-arguments', 'max()', {}, at('invalid_operand', 'max()', 0)),
  c('round-to-whole', 'round(qty)', { qty: ['2.4'] }, '2'),
  c('round-half-away-from-zero', 'round(qty)', { qty: ['2.5'] }, '3'),
  c('round-negative-half-away-from-zero', 'round(qty)', { qty: ['-2.5'] }, '-3'),
  c('round-to-places', 'round(qty, 2)', { qty: ['2.675'] }, '2.68', { description: 'a binary double would round 2.675 down' }),
  c('round-not-to-even', 'round(qty, 2)', { qty: ['0.125'] }, '0.13'),
  c('round-negative-half-to-places', 'round(qty, 1)', { qty: ['-0.25'] }, '-0.3'),
  c('round-to-zero-places', 'round(qty, 0)', { qty: ['2.5'] }, '3'),
  c('round-to-twenty-places', 'round(qty / 3, 20)', { qty: ['1'] }, '0.33333333333333333333'),
  c('round-keeps-fewer-places', 'round(qty, 5)', { qty: ['1.5'] }, '1.5'),
  c('round-thirds', 'round(qty / 3, 2)', { qty: ['2'] }, '0.67'),
  c('round-places-past-twenty', 'round(qty, 21)', {}, at('invalid_operand', 'round(qty, 21)', '21')),
  c('round-places-with-a-fraction', 'round(qty, 1.5)', {}, at('invalid_operand', 'round(qty, 1.5)', '1.5')),
  c('round-places-written-with-a-point', 'round(qty, 2.0)', {}, at('invalid_operand', 'round(qty, 2.0)', '2.0'), { description: 'a whole number as it is written, not one a fraction of zero makes' }),
  c('round-places-negative', 'round(qty, -1)', {}, at('invalid_operand', 'round(qty, -1)', '-1')),
  c('round-places-from-a-field', 'round(qty, price)', {}, at('invalid_operand', 'round(qty, price)', 'price')),
  c('round-places-worked-out', 'round(qty, 1 + 1)', {}, at('invalid_operand', 'round(qty, 1 + 1)', '1 + 1')),
  c('round-places-a-long-number', 'round(qty, 99999999999)', {}, at('invalid_operand', 'round(qty, 99999999999)', '9')),
  c('round-places-in-parentheses', 'round(qty, (2))', { qty: ['1.234'] }, '1.23', { description: 'the parentheses hold the number as it is written' }),
  c('round-takes-one-or-two', 'round(qty, 1, 2)', {}, at('invalid_operand', 'round(qty, 1, 2)', 0)),
  c('if-takes-then', 'if(qty > 10, qty * 0.9, qty)', { qty: ['20'] }, '18'),
  c('if-takes-else', 'if(qty > 10, qty * 0.9, qty)', { qty: ['5'] }, '5'),
  c('if-any-number-but-zero-is-true', 'if(-qty, 1, 2)', { qty: ['0.5'] }, '1'),
  c('if-zero-is-false', 'if(qty, 1, 2)', { qty: ['0'] }, '2'),
  c('if-works-out-only-the-branch-taken', 'if(qty = 0, 0, price / qty)', { qty: ['0'], price: ['5'] }, '0', { description: 'the division by zero in the other branch never happens' }),
  c('if-skips-the-else-branch', 'if(qty, price, price / 0)', { qty: ['1'], price: ['5'] }, '5'),
  c('if-takes-three', 'if(qty, 1)', {}, at('invalid_operand', 'if(qty, 1)', 0)),
  c('and-of-true-values', 'and(qty > 1, price > 1)', { qty: ['2'], price: ['3'] }, '1'),
  c('and-with-a-false-value', 'and(qty > 1, price > 1)', { qty: ['2'], price: ['1'] }, '0'),
  c('and-stops-at-the-first-zero', 'and(qty, price / 0)', { qty: ['0'], price: ['5'] }, '0'),
  c('and-of-three', 'and(qty, price, 1)', { qty: ['1'], price: ['2'] }, '1'),
  c('or-stops-at-the-first-true', 'or(qty, price / 0)', { qty: ['1'], price: ['5'] }, '1'),
  c('or-of-zeros', 'or(qty, price)', { qty: ['0'], price: ['0'] }, '0'),
  c('or-of-three', 'or(qty, price, 0)', { qty: ['0'], price: ['0.1'] }, '1'),
  c('and-takes-two-or-more', 'and(qty)', {}, at('invalid_operand', 'and(qty)', 0)),
  c('not-of-zero', 'not(qty)', { qty: ['0'] }, '1'),
  c('not-of-a-number', 'not(qty)', { qty: ['-3'] }, '0'),
  c('not-takes-one', 'not(qty, 1)', {}, at('invalid_operand', 'not(qty, 1)', 0)),
  c('equals-by-value', 'qty = 5', { qty: ['5.0'] }, '1'),
  c('equals-false', 'qty = 5', { qty: ['4'] }, '0'),
  c('not-equals', 'qty != 5', { qty: ['4'] }, '1'),
  c('not-equals-of-equal-values', 'qty != 5', { qty: ['5'] }, '0'),
  c('less-than-at-the-edge', 'qty < 5', { qty: ['5'] }, '0'),
  c('less-than-or-equal-at-the-edge', 'qty <= 5', { qty: ['5'] }, '1'),
  c('greater-than-at-the-edge', 'qty > 5', { qty: ['5'] }, '0'),
  c('greater-than-or-equal-at-the-edge', 'qty >= 5', { qty: ['5'] }, '1'),
  c('less-than-below', 'qty < 5', { qty: ['4.99'] }, '1'),
  c('greater-than-above', 'qty > 5', { qty: ['5.01'] }, '1'),
  c('greater-than-negative', 'qty > -2', { qty: ['-1.5'] }, '1'),
  c('comparisons-after-arithmetic', 'qty + 1 > 5', { qty: ['5'] }, '1'),
  c('comparisons-compare-by-value', 'qty > 9', { qty: ['10'] }, '1'),
  c('a-comparison-times-a-number', '(qty > 10) * 5 + qty', { qty: ['20'] }, '25'),
];

// ---- What each kind of field gives a formula ----
const kinds = [
  c('a-choice-gives-its-value', 'plan * 2', { plan: ['10'] }, '20'),
  c('choices-add-up', 'extras', { extras: ['5', '7.5'] }, '12.5'),
  c('no-choice-is-zero', 'extras + 1', { extras: [] }, '1'),
  c('a-choice-that-is-no-number-gives-no-value', 'plan * 2', { plan: ['gold'] }, null),
  c('one-choice-that-is-no-number-gives-no-value', 'extras', { extras: ['5', 'gold'] }, null),
  c('a-checked-checkbox-is-one', 'insured * 50', { insured: ['true'] }, '50'),
  c('an-unchecked-checkbox-is-zero', 'insured * 50 + 1', {}, '1'),
  c('a-checkbox-with-another-value-gives-no-value', 'insured * 50', { insured: ['yes'] }, null),
  c('a-checkbox-value-is-cleaned', 'insured * 50', { insured: [' true '] }, '50'),
  c('an-empty-number-is-zero', 'qty + 1', { qty: [] }, '1'),
  c('a-number-not-given-is-zero', 'qty + 1', {}, '1'),
  c('an-empty-text-is-zero', 'qty + 1', { qty: [''] }, '1'),
  c('only-spaces-are-zero', 'qty + 1', { qty: ['   '] }, '1'),
  c('a-number-is-cleaned', 'qty + 1', { qty: [' 5 '] }, '6'),
  c('several-numbers-add-up', 'qty', { qty: ['1', '2.5'] }, '3.5', { description: 'as a child field gives them from outside its rows' }),
  c('a-decimal-comma-gives-no-value', 'qty + 1', { qty: ['1,5'] }, null),
  c('an-exponent-gives-no-value', 'qty + 1', { qty: ['1e3'] }, null),
  c('a-leading-zero-gives-no-value', 'qty + 1', { qty: ['007'] }, null),
  c('a-trailing-point-gives-no-value', 'qty + 1', { qty: ['5.'] }, null),
  c('a-plus-sign-gives-no-value', 'qty + 1', { qty: ['+5'] }, null),
  c('minus-zero-is-zero', 'qty + 1', { qty: ['-0'] }, '1'),
  c('a-long-answer-out-of-range', 'qty + 1', { qty: ['1' + '0'.repeat(60)] }, fails('out_of_range'), { description: 'found before the digits become a number' }),
  c('a-long-answer-whose-zeros-are-a-fraction', 'qty', { qty: ['0.' + '0'.repeat(500) + '1'] }, '0.' + '0'.repeat(500) + '1', { description: 'leading zeros of a fraction are no digits' }),
  c('a-hidden-field-is-zero', 'qty + 1', { qty: ['5'] }, '1', { context: { hidden: ['qty'] } }),
  c('a-text-field-is-refused', 'name * 2', {}, at('operator_not_for_kind', 'name * 2', 0)),
  c('a-files-field-is-refused', 'photos + 1', {}, at('operator_not_for_kind', 'photos + 1', 0)),
  c('a-time-field-is-refused', 'start + 1', {}, at('operator_not_for_kind', 'start + 1', 0)),
  c('a-date-outside-a-date-function', 'birth + 1', {}, at('operator_not_for_kind', 'birth + 1', 0)),
  c('a-number-inside-a-date-function', 'daysBetween(qty, arrival)', {}, at('operator_not_for_kind', 'daysBetween(qty, arrival)', 'qty')),
  c('a-date-function-of-a-text', 'daysBetween(arrival, name)', {}, at('operator_not_for_kind', 'daysBetween(arrival, name)', 'name')),
  c('a-date-function-of-a-time', 'daysBetween(start, arrival)', {}, at('operator_not_for_kind', 'daysBetween(start, arrival)', 'start'), { description: 'a time of day has no date' }),
];

// ---- Dates: leap days, years, daylight-saving changes ----
const nearSpringChange = { now: '2026-03-28T23:30:00Z' };
const dates = [
  c('days-between', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'], departure: ['2026-06-23'] }, '3'),
  c('days-between-a-later-start', 'daysBetween(arrival, departure)', { arrival: ['2026-06-23'], departure: ['2026-06-20'] }, '-3'),
  c('days-between-one-day', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'], departure: ['2026-06-20'] }, '0'),
  c('days-across-a-leap-day', 'daysBetween(arrival, departure)', { arrival: ['2024-02-28'], departure: ['2024-03-01'] }, '2'),
  c('days-across-the-end-of-february-in-a-common-year', 'daysBetween(arrival, departure)', { arrival: ['2026-02-28'], departure: ['2026-03-01'] }, '1'),
  c('days-across-a-century-without-a-leap-day', 'daysBetween(arrival, departure)', { arrival: ['2100-02-28'], departure: ['2100-03-01'] }, '1'),
  c('days-across-four-centuries-with-a-leap-day', 'daysBetween(arrival, departure)', { arrival: ['2000-02-28'], departure: ['2000-03-01'] }, '2'),
  c('days-across-a-new-year', 'daysBetween(arrival, departure)', { arrival: ['2025-12-31'], departure: ['2026-01-01'] }, '1'),
  c('days-from-the-first-to-the-last-date', 'daysBetween(arrival, departure)', { arrival: ['0001-01-01'], departure: ['9999-12-31'] }, '3652058'),
  c('days-between-date-times-count-dates', 'daysBetween(meeting, until)', { meeting: ['2026-06-20T23:30'], until: ['2026-06-22T00:15'] }, '2', { description: 'calendar days, not 24-hour spans' }),
  c('days-across-the-spring-change', 'daysBetween(meeting, until)', { meeting: ['2026-03-28T12:00'], until: ['2026-03-29T12:00'] }, '1', { description: 'date-times are wall-clock values: the hour Europe skips that night costs no day' }),
  c('days-across-the-autumn-change', 'daysBetween(meeting, until)', { meeting: ['2026-10-25T00:30:00'], until: ['2026-10-26T00:30:00'] }, '1'),
  c('days-between-a-date-and-a-date-time', 'daysBetween(arrival, until)', { arrival: ['2026-06-20'], until: ['2026-06-21T08:00'] }, '1'),
  c('days-to-today', 'daysBetween(today(), arrival)', { arrival: ['2026-06-20'] }, '5'),
  c('days-from-today', 'daysBetween(arrival, today())', { arrival: ['2026-06-20'] }, '-5'),
  c('today-just-after-the-spring-change-in-the-site-zone', 'daysBetween(today(), arrival)', { arrival: ['2026-03-30'] }, '1', { context: nearSpringChange, description: '23:30 UTC is 00:30 on 29 March in Zurich' }),
  c('today-in-another-zone', 'daysBetween(today(), arrival)', { arrival: ['2026-03-30'] }, '2', { context: { ...nearSpringChange, timeZone: 'America/New_York' }, description: '19:30 on 28 March in New York' }),
  c('today-just-after-the-autumn-change-in-the-site-zone', 'daysBetween(today(), arrival)', { arrival: ['2026-10-26'] }, '1', { context: { now: '2026-10-24T22:30:00Z' }, description: '00:30 on 25 October in Zurich, still summer time' }),
  c('today-in-parentheses', 'daysBetween((today()), arrival)', { arrival: ['2026-06-20'] }, '5'),
  c('a-date-in-parentheses', 'daysBetween((arrival), departure)', { arrival: ['2026-06-20'], departure: ['2026-06-23'] }, '3'),
  c('years-on-the-birthday', 'yearsBetween(birth, today())', { birth: ['2000-06-15'] }, '26'),
  c('years-the-day-before-the-birthday', 'yearsBetween(birth, today())', { birth: ['2000-06-16'] }, '25'),
  c('years-from-a-leap-day-on-the-day-before-march', 'yearsBetween(birth, arrival)', { birth: ['2000-02-29'], arrival: ['2001-02-28'] }, '0'),
  c('years-from-a-leap-day-complete-on-the-first-of-march', 'yearsBetween(birth, arrival)', { birth: ['2000-02-29'], arrival: ['2001-03-01'] }, '1'),
  c('years-from-a-leap-day-to-a-leap-day', 'yearsBetween(birth, arrival)', { birth: ['2000-02-29'], arrival: ['2004-02-29'] }, '4'),
  c('years-from-a-leap-day-to-the-end-of-february', 'yearsBetween(birth, arrival)', { birth: ['2000-02-29'], arrival: ['2003-02-28'] }, '2'),
  c('years-to-a-leap-day', 'yearsBetween(birth, arrival)', { birth: ['2023-02-28'], arrival: ['2024-02-29'] }, '1'),
  c('years-backwards', 'yearsBetween(birth, arrival)', { birth: ['2001-03-01'], arrival: ['2000-02-29'] }, '-1'),
  c('years-backwards-within-a-year', 'yearsBetween(birth, arrival)', { birth: ['2026-03-02'], arrival: ['2026-03-01'] }, '0'),
  c('years-between-date-times', 'yearsBetween(meeting, until)', { meeting: ['2020-06-15T23:00'], until: ['2026-06-15T01:00'] }, '6', { description: 'the dates count, not the times' }),
  c('years-of-the-whole-calendar', 'yearsBetween(birth, arrival)', { birth: ['0001-01-01'], arrival: ['9999-12-31'] }, '9998'),
  c('an-empty-date-gives-no-value', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'], departure: [] }, null),
  c('a-date-not-given-gives-no-value', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'] }, null),
  c('a-date-that-does-not-exist-gives-no-value', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'], departure: ['2026-02-30'] }, null),
  c('two-dates-give-no-value', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'], departure: ['2026-06-21', '2026-06-22'] }, null),
  c('a-date-time-without-its-time-gives-no-value', 'daysBetween(meeting, until)', { meeting: ['2026-06-20'], until: ['2026-06-21T08:00'] }, null),
  c('a-date-with-a-time-gives-no-value', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20T08:00'], departure: ['2026-06-21'] }, null, { description: 'a date field\'s answer is a date' }),
  c('a-date-is-cleaned', 'daysBetween(arrival, departure)', { arrival: [' 2026-06-20 '], departure: ['2026-06-21'] }, '1'),
  c('a-hidden-date-gives-no-value', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'], departure: ['2026-06-21'] }, null, { context: { hidden: ['departure'] } }),
  c('no-value-spreads-through-arithmetic', 'daysBetween(arrival, departure) * 100 + qty', { arrival: ['2026-06-20'], qty: ['5'] }, null),
  c('no-value-in-a-branch-not-taken', 'if(qty, 7, daysBetween(arrival, departure))', { qty: ['1'] }, '7'),
  c('no-value-as-a-condition', 'if(daysBetween(arrival, departure), 1, 2)', {}, null),
  c('no-value-before-a-zero-in-and', 'and(daysBetween(arrival, departure), qty)', { qty: ['0'] }, null, { description: 'left to right: the first argument already has no value' }),
  c('a-zero-before-no-value-in-and', 'and(qty, daysBetween(arrival, departure))', { qty: ['0'] }, '0'),
  c('no-value-in-min', 'min(qty, daysBetween(arrival, departure))', { qty: ['1'] }, null),
  c('no-value-in-a-comparison', 'daysBetween(arrival, departure) > 1', {}, null),
  c('a-failure-on-the-left-comes-first', 'qty / 0 + price', { qty: ['1'], price: ['abc'] }, fails('division_by_zero'), { description: 'left to right: the division fails before the unreadable answer is read' }),
  c('no-value-on-the-left-comes-first', 'price + qty / 0', { qty: ['1'], price: ['abc'] }, null, { description: 'left to right: the unreadable answer ends it before the division' }),
  c('today-outside-a-date-function', 'qty + today()', {}, at('invalid_operand', 'qty + today()', 'today')),
  c('today-alone', 'today()', {}, at('invalid_operand', 'today()', 0)),
  c('today-in-arithmetic-inside-a-date-function', 'daysBetween(today() + 1, arrival)', {}, at('invalid_operand', 'daysBetween(today() + 1, arrival)', 'today')),
  c('today-deeper-inside-a-date-function', 'daysBetween(arrival, if(qty, today(), arrival))', {}, at('invalid_operand', 'daysBetween(arrival, if(qty, today(), arrival))', 'if')),
  c('arithmetic-on-a-date-argument', 'daysBetween(arrival, departure + 1)', {}, at('invalid_operand', 'daysBetween(arrival, departure + 1)', 'departure')),
  c('a-number-as-a-date-argument', 'daysBetween(arrival, 5)', {}, at('invalid_operand', 'daysBetween(arrival, 5)', '5')),
  c('today-takes-nothing', 'daysBetween(today(1), arrival)', {}, at('invalid_operand', 'daysBetween(today(1), arrival)', 'today')),
  c('days-between-takes-two', 'daysBetween(arrival)', {}, at('invalid_operand', 'daysBetween(arrival)', 0)),
  c('years-between-takes-two', 'yearsBetween(birth, arrival, departure)', {}, at('invalid_operand', 'yearsBetween(birth, arrival, departure)', 0)),
  c('today-needs-a-valid-instant', 'daysBetween(today(), arrival)', { arrival: ['2026-06-20'] }, fails('invalid_context'), { context: { now: '2026-06-15 10:00' } }),
  c('today-needs-a-known-zone', 'daysBetween(today(), arrival)', { arrival: ['2026-06-20'] }, fails('unknown_time_zone'), { context: { timeZone: 'Europe/Nowhere' } }),
  c('the-instant-is-read-only-when-asked', 'daysBetween(arrival, departure)', { arrival: ['2026-06-20'], departure: ['2026-06-21'] }, '1', { context: { now: 'never' } }),
];

// ---- Division and rounding at their edges, and the range ----
const arithmetic = [
  c('divide-into-thirds', 'qty / 3', { qty: ['1'] }, '0.33333333333333333333', { description: 'division keeps 20 places' }),
  c('divide-two-thirds-rounds-the-last-place', 'qty / 3', { qty: ['2'] }, '0.66666666666666666667'),
  c('divide-negative-two-thirds', '-qty / 3', { qty: ['2'] }, '-0.66666666666666666667'),
  c('divide-a-half-place', 'qty / 2', { qty: ['0.00000000000000000001'] }, '0.00000000000000000001', { description: 'half of the twentieth place rounds away from zero' }),
  c('divide-a-negative-half-place', '-qty / 2', { qty: ['0.00000000000000000001'] }, '-0.00000000000000000001'),
  c('divide-then-multiply-keeps-the-twenty-places', 'qty / 3 * 3', { qty: ['1'] }, '0.99999999999999999999'),
  c('divide-exactly', 'qty / 8', { qty: ['1'] }, '0.125'),
  c('divide-by-zero', 'qty / 0', { qty: ['1'] }, fails('division_by_zero')),
  c('divide-by-zero-written-with-a-fraction', 'qty / 0.0', { qty: ['1'] }, fails('division_by_zero')),
  c('divide-by-an-empty-field', 'price / qty', { price: ['5'] }, fails('division_by_zero'), { description: 'an empty number is 0' }),
  c('divide-zero', 'qty / 4', { qty: ['0'] }, '0'),
  c('sixty-digits-are-in-range', pow('qty', 4), { qty: [FIFTEEN_NINES] }, ((10n ** 15n - 1n) ** 4n).toString()),
  c('past-sixty-digits-is-out-of-range', pow('qty', 5), { qty: [FIFTEEN_NINES] }, fails('out_of_range')),
  c('a-sum-past-sixty-digits', 'qty + qty', { qty: [MAX_DIGITS_NUMBER] }, fails('out_of_range')),
  c('a-sum-of-answers-past-sixty-digits', 'qty', { qty: [MAX_DIGITS_NUMBER, '1'] }, fails('out_of_range')),
];

// ---- Whole forms (ADR-0061): calculations in dependency order, rounded, and read by rules ----
const f = (id, kind, more = {}) => (kind === null ? { id, ...more } : { id, kind, ...more });
const calc = (id, alias, formula, decimals, more = {}) =>
  f(id, 'number', { alias, calculation: decimals === undefined ? { formula } : { formula, decimals }, ...more });
const n = (id, alias, more = {}) => f(id, 'number', { alias, ...more });
const form = (fieldList, more = {}) => ({ pages: [{ id: 'p1', groups: [{ id: 'g1', fields: fieldList }] }], ...more });
const none = { hidden: [], required: [], messages: {}, calculations: {}, rows: {}, actions: [], outcome: null };
const state = (partial = {}) => ({ ...none, ...partial });
const row = (partial = {}) => ({ hidden: [], required: [], messages: {}, calculations: {}, ...partial });
const cond = (field, op, value) => (value === undefined ? { field, op } : { field, op, value });
const rule = (effect, conditions, more = {}) => ({ effect, enabled: true, match: 'all', conditions, ...more });
const show = (...conditions) => [rule('show', conditions)];
const hide = (...conditions) => [rule('hide', conditions)];
const nights = [n('fn', 'nights'), calc('fc', 'cost', 'nights * rate', 0)];
const trips = (more = {}) => f('trips', null, { fields: nights, ...more });

const forms = [
  { id: 'form-a-calculation-rounded-to-its-places', values: { fq: ['3'], fp: ['0.3333'] }, form: form([n('fq', 'qty'), n('fp', 'price'), calc('ft', 'total', 'qty * price', 2)]), expect: state({ calculations: { ft: '1' } }), description: '0.9999 to two places' },
  { id: 'form-two-places-when-unset', values: { fq: ['1'] }, form: form([n('fq', 'qty'), calc('ft', 'third', 'qty / 3')]), expect: state({ calculations: { ft: '0.33' } }) },
  { id: 'form-no-places', values: { fq: ['2.5'] }, form: form([n('fq', 'qty'), calc('ft', 'whole', 'qty', 0)]), expect: state({ calculations: { ft: '3' } }) },
  { id: 'form-fifteen-places', values: { fq: ['1'] }, form: form([n('fq', 'qty'), calc('ft', 'third', 'qty / 3', 15)]), expect: state({ calculations: { ft: '0.333333333333333' } }) },
  { id: 'form-the-result-in-canonical-form', values: { fq: ['1.5'] }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 2)]), expect: state({ calculations: { ft: '3' } }), description: 'the places are how far it rounds, not how it is written' },
  { id: 'form-a-rule-reads-the-rounded-result', values: { fq: ['0.995'] }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 1', 2), f('notice', 'text', { rules: show(cond('ft', 'equals', '1')) })]), expect: state({ calculations: { ft: '1' } }), description: '0.995 rounds half away from zero to 1, and the rule compares 1' },
  { id: 'form-a-rule-never-reads-the-unrounded-result', values: { fq: ['0.995'] }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 1', 2), f('notice', 'text', { rules: show(cond('ft', 'equals', '0.995')) })]), expect: state({ hidden: ['notice'], calculations: { ft: '1' } }) },
  { id: 'form-what-is-posted-for-a-calculation-is-ignored', values: { fq: ['2'], ft: ['99'] }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 0), f('notice', 'text', { rules: show(cond('ft', 'equals', '99')) })]), expect: state({ hidden: ['notice'], calculations: { ft: '4' } }) },
  { id: 'form-a-hidden-field-reads-as-zero', values: { fq: ['5'], flag: ['true'] }, form: form([f('flag', 'checkbox'), n('fq', 'qty', { rules: hide(cond('flag', 'isChecked')) }), calc('ft', 'total', 'qty + 1', 0)]), expect: state({ hidden: ['fq'], calculations: { ft: '1' } }) },
  { id: 'form-a-hidden-calculation-is-gone', values: { fq: ['5'], flag: ['true'] }, form: form([f('flag', 'checkbox'), n('fq', 'qty'), calc('ft', 'total', 'qty + 1', 0, { rules: hide(cond('flag', 'isChecked')) }), f('notice', 'text', { rules: show(cond('ft', 'isEmpty')) })]), expect: state({ hidden: ['ft'] }), description: 'it has no result, and rules read it as empty' },
  { id: 'form-a-calculation-on-a-hidden-page-is-gone', values: { flag: ['true'] }, form: { pages: [{ id: 'p1', groups: [{ id: 'g1', fields: [f('flag', 'checkbox'), n('fq', 'qty')] }] }, { id: 'p2', rules: hide(cond('flag', 'isChecked')), groups: [{ id: 'g2', fields: [calc('ft', 'total', 'qty + 1', 0)] }] }] }, expect: state({ hidden: ['p2', 'g2', 'ft'] }) },
  { id: 'form-a-calculation-of-a-calculation-comes-after-it', values: { fq: ['3'], fp: ['10'] }, form: form([calc('fg', 'gross', 'net * 1.081', 2), calc('fn', 'net', 'qty * price', 2), n('fq', 'qty'), n('fp', 'price')]), expect: state({ calculations: { fg: '32.43', fn: '30' } }), description: 'in document order the gross comes first; it is worked out after the net it reads' },
  { id: 'form-a-calculation-reads-the-rounded-result-of-another', values: { fq: ['1'] }, form: form([n('fq', 'qty'), calc('fa', 'third', 'qty / 3', 2), calc('fb', 'whole', 'third * 3', 2)]), expect: state({ calculations: { fa: '0.33', fb: '0.99' } }) },
  { id: 'form-a-division-by-zero-has-no-value', values: { fq: ['0'], fp: ['5'] }, form: form([n('fq', 'qty'), n('fp', 'price'), calc('ft', 'each', 'price / qty', 2), f('notice', 'text', { rules: show(cond('ft', 'isEmpty')) })]), expect: state({ calculations: { ft: null } }) },
  { id: 'form-out-of-range-has-no-value', values: { fq: [FIFTEEN_NINES] }, form: form([n('fq', 'qty'), calc('ft', 'huge', pow('qty', 5), 0)]), expect: state({ calculations: { ft: null } }) },
  { id: 'form-an-empty-date-has-no-value', values: { fa: ['2026-06-20'] }, form: form([f('fa', 'date', { alias: 'arrival' }), f('fd', 'date', { alias: 'departure' }), calc('ft', 'nights', 'daysBetween(arrival, departure)', 0)]), expect: state({ calculations: { ft: null } }) },
  { id: 'form-an-age-from-today', values: { fb: ['2000-06-16'] }, form: form([f('fb', 'date', { alias: 'birth' }), calc('ft', 'age', 'yearsBetween(birth, today())', 0), f('adult', 'text', { rules: show(cond('ft', 'greaterThan', '17')) })]), expect: state({ calculations: { ft: '25' } }) },
  { id: 'form-a-calculation-required-by-a-rule', values: { fq: ['20'] }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 0, { rules: [rule('require', [cond('ft', 'greaterThan', '10')])] })]), expect: state({ required: ['ft'], calculations: { ft: '40' } }) },
  { id: 'form-a-validation-rule-reads-a-calculation', values: { fq: ['20'] }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 0)], { validationRules: [{ id: 'v1', field: 'fq', enabled: true, match: 'all', conditions: [cond('ft', 'greaterThan', '30')] }] }), expect: state({ messages: { fq: ['v1'] }, calculations: { ft: '40' } }) },
  { id: 'form-an-outcome-reads-a-calculation', values: { fq: ['20'] }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 0)], { outcomes: [{ id: 'o1', rules: [rule('use', [cond('ft', 'lessThan', '10')])] }, { id: 'o2' }] }), expect: state({ outcome: 'o2', calculations: { ft: '40' } }) },
  { id: 'form-a-row-works-out-its-own-calculation', values: { fr: ['100'] }, rows: { trips: [{ fn: ['2'] }, { fn: ['3'] }] }, form: form([n('fr', 'rate'), trips()]), expect: state({ rows: { trips: [row({ calculations: { fc: '200' } }), row({ calculations: { fc: '300' } })] } }) },
  { id: 'form-a-child-field-from-outside-its-rows-adds-up', values: { fr: ['100'] }, rows: { trips: [{ fn: ['2'] }, { fn: ['3'] }] }, form: form([n('fr', 'rate'), trips(), calc('ft', 'total', 'nights * 10', 0)]), expect: state({ calculations: { ft: '50' }, rows: { trips: [row({ calculations: { fc: '200' } }), row({ calculations: { fc: '300' } })] } }) },
  { id: 'form-the-rows-calculations-add-up', values: { fr: ['100'] }, rows: { trips: [{ fn: ['2'] }, { fn: ['3'] }] }, form: form([calc('ft', 'total', 'cost', 0), n('fr', 'rate'), trips()]), expect: state({ calculations: { ft: '500' }, rows: { trips: [row({ calculations: { fc: '200' } }), row({ calculations: { fc: '300' } })] } }) },
  {
    id: 'form-a-hidden-row-field-is-left-out',
    values: { fr: ['100'] },
    rows: { trips: [{ fn: ['2'] }, { fn: ['3'], skip: ['true'] }] },
    form: form([n('fr', 'rate'), f('trips', null, { fields: [f('skip', 'checkbox'), n('fn', 'nights', { rules: hide(cond('skip', 'isChecked')) }), calc('fc', 'cost', 'nights * rate', 0)] }), calc('ft', 'total', 'nights', 0)]),
    expect: state({ calculations: { ft: '2' }, rows: { trips: [row({ calculations: { fc: '200' } }), row({ hidden: ['fn'], calculations: { fc: '0' } })] } }),
  },
  { id: 'form-a-hidden-row-calculation-is-gone', values: { fr: ['100'], off: ['true'] }, rows: { trips: [{ fn: ['2'] }] }, form: form([f('off', 'checkbox'), n('fr', 'rate'), f('trips', null, { fields: [n('fn', 'nights'), calc('fc', 'cost', 'nights * rate', 0, { rules: hide(cond('off', 'isChecked')) })] })]), expect: state({ rows: { trips: [row({ hidden: ['fc'] })] } }) },
  { id: 'form-a-calculation-that-reads-itself', form: form([calc('ft', 'total', 'total + 1', 0)]), expect: { error: 'cycle' } },
  { id: 'form-calculations-that-read-each-other', form: form([calc('fa', 'first', 'second + 1', 0), calc('fb', 'second', 'first + 1', 0)]), expect: { error: 'cycle' } },
  { id: 'form-a-calculation-that-decides-whether-its-own-field-is-shown', form: form([n('fq', 'qty', { rules: show(cond('ft', 'greaterThan', '1')) }), calc('ft', 'total', 'qty * 2', 0)]), expect: { error: 'cycle' } },
  { id: 'form-a-calculation-hidden-by-its-own-result', form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 0, { rules: hide(cond('ft', 'greaterThan', '1')) })]), expect: { error: 'cycle' } },
  { id: 'form-a-formula-that-names-an-unknown-field', form: form([calc('ft', 'total', 'nope * 2', 0)]), expect: { error: 'unknown_field', at: 0 } },
  { id: 'form-a-formula-that-names-a-field-by-its-id', form: form([n('fq', 'qty'), calc('ft', 'total', 'fq * 2', 0)]), expect: { error: 'unknown_field', at: 0 }, description: 'formulas name fields by alias' },
  { id: 'form-a-formula-that-names-a-field-without-a-value', form: form([f('intro', null, { alias: 'intro' }), calc('ft', 'total', 'intro * 2', 0)]), expect: { error: 'unknown_field', at: 0 } },
  { id: 'form-a-formula-that-does-not-parse', form: form([n('fq', 'qty'), calc('ft', 'total', 'qty +', 0)]), expect: { error: 'invalid_formula', at: 5 } },
  { id: 'form-a-formula-that-names-no-field', form: form([calc('ft', 'total', '2 + 2', 0)]), expect: { error: 'constant_formula', at: 0 } },
  { id: 'form-a-formula-of-the-wrong-kind', form: form([f('fn', 'text', { alias: 'name' }), calc('ft', 'total', 'name * 2', 0)]), expect: { error: 'operator_not_for_kind', at: 0 } },
  { id: 'form-two-fields-with-one-alias', form: form([n('fa', 'qty'), n('fb', 'qty')]), expect: { error: 'invalid_form' } },
  { id: 'form-a-field-without-a-value-claims-no-alias', values: { fq: ['4'] }, form: form([f('intro', null, { alias: 'qty' }), n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 0)]), expect: state({ calculations: { ft: '8' } }), description: 'a heading holds no value, so formulas cannot name it and its alias is free' },
  { id: 'form-aliases-differ-by-letter-case', values: { fa: ['2'], fb: ['3'] }, form: form([n('fa', 'qty'), n('fb', 'Qty'), calc('ft', 'total', 'qty * Qty', 0)]), expect: state({ calculations: { ft: '6' } }), description: 'the engine compares aliases exactly; publishing refuses two that differ only in letter case' },
  { id: 'form-a-calculation-on-a-text-field', form: form([f('ft', 'text', { alias: 'total', calculation: { formula: 'qty' } }), n('fq', 'qty')]), expect: { error: 'invalid_form' } },
  { id: 'form-sixteen-places', form: form([n('fq', 'qty'), calc('ft', 'total', 'qty', 16)]), expect: { error: 'invalid_form' } },
  { id: 'form-negative-places', form: form([n('fq', 'qty'), calc('ft', 'total', 'qty', -1)]), expect: { error: 'invalid_form' } },
  { id: 'form-places-with-a-fraction', form: form([n('fq', 'qty'), calc('ft', 'total', 'qty', 1.5)]), expect: { error: 'invalid_form' } },
  { id: 'form-rules-are-checked-before-formulas', form: form([n('fq', 'qty', { rules: show(cond('fq', 'nope')) }), calc('ft', 'total', 'qty +', 0)]), expect: { error: 'unknown_operator' } },
  { id: 'form-a-formula-is-checked-even-when-its-field-is-hidden', values: { flag: ['true'] }, form: form([f('flag', 'checkbox'), calc('ft', 'total', 'nope', 0, { rules: hide(cond('flag', 'isChecked')) })]), expect: { error: 'unknown_field', at: 0 } },
  { id: 'form-a-hidden-calculation-is-not-worked-out', values: { flag: ['true'], fa: ['2026-06-20'] }, context: { now: 'never' }, form: form([f('flag', 'checkbox'), f('fa', 'date', { alias: 'arrival' }), calc('ft', 'wait', 'daysBetween(today(), arrival)', 0, { rules: hide(cond('flag', 'isChecked')) })]), expect: state({ hidden: ['ft'] }), description: 'it never asks for today, so an instant that does not read changes nothing' },
  { id: 'form-a-hidden-row-calculation-is-not-worked-out', values: { off: ['true'] }, rows: { trips: [{ fa: ['2026-06-20'] }] }, context: { now: 'never' }, form: form([f('off', 'checkbox'), f('trips', null, { fields: [f('fa', 'date', { alias: 'arrival' }), calc('fc', 'wait', 'daysBetween(today(), arrival)', 0, { rules: hide(cond('off', 'isChecked')) })] })]), expect: state({ rows: { trips: [row({ hidden: ['fc'] })] } }) },
  { id: 'form-the-instant-is-read-once-and-only-when-asked', values: { fq: ['2'] }, context: { now: 'never' }, form: form([n('fq', 'qty'), calc('ft', 'total', 'qty * 2', 0)]), expect: state({ calculations: { ft: '4' } }) },
];

export default {
  suite: 'calculations',
  defaults,
  fields,
  cases: [...grammar, ...mistakes, ...functions, ...kinds, ...dates, ...arithmetic, ...forms],
};
