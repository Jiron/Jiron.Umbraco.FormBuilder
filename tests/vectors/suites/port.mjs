// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// What porting the spike to Core and packages/logic found (ROADMAP 3.4b, ADR-0030): limits the spike did not keep,
// names an object's prototype answers in TypeScript, operands that are null, the instant of evaluation in any
// shape either runtime parses, and time zone names only one runtime takes. About half of the cases fail on one of
// the spike's two evaluators (ADR-0030 says which); the others pin the edges next to them. Sections added by later
// reviews name their step. Special characters are built from code points, so this file and the JSON it becomes stay
// ASCII (build.mjs).

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
  name: 'text',
  amount: 'number',
  arrival: 'date',
  phone: 'text',
};

const cond = (field, op, value) => (value === undefined ? { field, op } : { field, op, value });
const ctx = (context, op, value) => (value === undefined ? { context, op } : { context, op, value });
const today = (now, expected, timeZone) => ({
  context: timeZone === undefined ? { now } : { now, timeZone },
  condition: ctx('today', 'equals', expected),
});

const cases = [
  // ---- Patterns: the automaton's size is bounded, also for repeats of what matches no character ----
  { id: 'pattern-anchor-repeats-refused', description: 'anchors match no character, so the weight is 0; unrolled, the automaton would have a million instructions', values: { phone: ['a'] }, condition: cond('phone', 'matches', '((^){1000}){1000}'), expect: { error: 'unsafe_pattern' } },
  { id: 'pattern-empty-group-repeats-refused', description: 'an empty group matches no character; its optional copies are a million splits', values: { phone: ['a'] }, condition: cond('phone', 'matches', '((){0,1000}){1000}'), expect: { error: 'unsafe_pattern' } },
  { id: 'pattern-deeply-nested-counts-refused', description: 'a weight of 10^12 characters; counted in 32-bit integers it wraps around below the limit', values: { phone: ['a'] }, condition: cond('phone', 'matches', '((((a{1000}){1000}){1000}){1000})'), expect: { error: 'unsafe_pattern' } },
  { id: 'pattern-instructions-at-the-limit-allowed', description: 'ten instructions a copy, a thousand copies: exactly 10,000', values: { phone: ['a'] }, condition: cond('phone', 'matches', '(^^^^^^^^^a){1000}'), expect: false },
  { id: 'pattern-instructions-over-the-limit-refused', description: 'eleven instructions a copy: 11,000', values: { phone: ['a'] }, condition: cond('phone', 'matches', '(^^^^^^^^^^a){1000}'), expect: { error: 'unsafe_pattern' } },
  { id: 'pattern-weight-at-the-limit-allowed', values: { phone: ['a'.repeat(1000) + 'b'.repeat(1000)] }, condition: cond('phone', 'matches', 'a{1000}b{1000}'), expect: true },
  { id: 'pattern-weight-over-the-limit-refused', values: { phone: ['a'] }, condition: cond('phone', 'matches', 'a{1000}b{1000}c'), expect: { error: 'unsafe_pattern' } },

  // ---- Names an object's prototype answers: only the form's own fields, operators and query values count ----
  { id: 'field-named-constructor-has-answers', fields: { constructor: 'text' }, values: { constructor: ['x'] }, condition: cond('constructor', 'isNotEmpty'), expect: true },
  { id: 'field-named-has-own-property-without-answers', fields: { hasOwnProperty: 'text' }, condition: cond('hasOwnProperty', 'isEmpty'), expect: true },
  { id: 'field-to-string-unknown', condition: cond('toString', 'isEmpty'), expect: { error: 'unknown_field' } },
  { id: 'operand-field-value-of-unknown', condition: cond('arrival', 'equals', { field: 'valueOf' }), expect: { error: 'unknown_field' } },
  { id: 'operator-constructor-unknown', values: { name: ['Anna'] }, condition: cond('name', 'constructor', 'x'), expect: { error: 'unknown_operator' } },
  { id: 'context-query-constructor-is-empty', condition: ctx('query.constructor', 'isEmpty'), expect: true },
  { id: 'calc-operator-constructor-unknown', calc: { op: 'constructor', args: ['1', '2'] }, expect: { error: 'unknown_operator' } },
  { id: 'calc-operator-to-string-unknown', calc: { op: 'toString', args: ['1'] }, expect: { error: 'unknown_operator' } },

  // ---- Operands and names that are null ----
  { id: 'operand-null-refused', values: { name: ['Anna'] }, condition: cond('name', 'equals', null), expect: { error: 'invalid_operand' } },
  { id: 'operand-null-list-refused', condition: cond('name', 'isOneOf', null), expect: { error: 'invalid_operand' } },
  { id: 'operand-null-pair-refused', condition: cond('amount', 'between', null), expect: { error: 'invalid_operand' } },
  { id: 'operand-null-in-a-list-refused', condition: cond('name', 'isOneOf', ['a', null]), expect: { error: 'invalid_operand' } },
  { id: 'operand-null-where-none-is-taken-refused', condition: cond('name', 'isEmpty', null), expect: { error: 'invalid_operand' } },
  { id: 'condition-null-context-reads-as-none', condition: { field: 'name', context: null, op: 'isEmpty' }, expect: true },

  // ---- The instant of evaluation: one strict form, read the same in both runtimes ----
  { id: 'now-without-zone-refused', description: 'without Z or an offset, each runtime would read its own local time', ...today('2026-06-15T10:00:00', '2026-06-15'), expect: { error: 'invalid_context' } },
  { id: 'now-date-only-refused', ...today('2026-06-15', '2026-06-15'), expect: { error: 'invalid_context' } },
  { id: 'now-without-seconds-refused', ...today('2026-06-15T10:00Z', '2026-06-15'), expect: { error: 'invalid_context' } },
  { id: 'now-lower-case-z-refused', ...today('2026-06-15T10:00:00z', '2026-06-15'), expect: { error: 'invalid_context' } },
  { id: 'now-offset-without-colon-refused', ...today('2026-06-15T10:00:00+0200', '2026-06-15'), expect: { error: 'invalid_context' } },
  { id: 'now-with-negative-offset', description: '23:30 at UTC-2 is 01:30Z on 16 June, 03:30 in Zurich', ...today('2026-06-15T23:30:00-02:00', '2026-06-16'), expect: true },
  { id: 'now-with-positive-offset', description: '01:30 at UTC+5 is 20:30Z on 15 June, 22:30 in Zurich', ...today('2026-06-16T01:30:00+05:00', '2026-06-15'), expect: true },
  { id: 'now-fraction-never-changes-the-date', description: 'the server writes seven places; the last tick before midnight in Zurich (UTC+2)', ...today('2026-06-15T21:59:59.9999999Z', '2026-06-15'), expect: true },
  { id: 'now-fraction-at-midnight', description: 'the browser writes three places', ...today('2026-06-15T22:00:00.000Z', '2026-06-16'), expect: true },
  { id: 'now-ten-fraction-digits-refused', ...today('2026-06-15T10:00:00.0000000000Z', '2026-06-15'), expect: { error: 'invalid_context' } },
  { id: 'now-empty-fraction-refused', ...today('2026-06-15T10:00:00.Z', '2026-06-15'), expect: { error: 'invalid_context' } },
  { id: 'now-leap-second-refused', ...today('2016-12-31T23:59:60Z', '2017-01-01'), expect: { error: 'invalid_context' } },
  { id: 'now-invalid-date-refused', ...today('2026-02-30T10:00:00Z', '2026-03-02'), expect: { error: 'invalid_context' } },
  { id: 'now-first-instant', description: 'time zone data agrees between platforms from 1970', ...today('1970-01-01T00:00:00Z', '1970-01-01', 'UTC'), expect: true },
  { id: 'now-before-1970-refused', ...today('1969-12-31T23:59:59Z', '1969-12-31', 'UTC'), expect: { error: 'invalid_context' } },
  { id: 'now-last-instant', description: 'the last second of 9998 in UTC is already 9999 in Auckland', ...today('9998-12-31T23:59:59Z', '9999-01-01', 'Pacific/Auckland'), expect: true },
  { id: 'now-after-9998-refused', description: 'in Auckland, 9999-12-31T23:00Z would be in year 10000', ...today('9999-01-01T00:00:00Z', '9999-01-01', 'UTC'), expect: { error: 'invalid_context' } },
  { id: 'now-only-read-when-today-is', description: 'a condition that does not look at today never reads the instant', context: { now: 'not an instant' }, values: { name: ['Anna'] }, condition: cond('name', 'equals', 'anna'), expect: true },

  // ---- Time zones: IANA names only, which both runtimes take ----
  { id: 'time-zone-windows-name-refused', description: 'the server on Windows would take it, the browser does not', context: { timeZone: 'W. Europe Standard Time' }, condition: ctx('today', 'equals', '2026-06-15'), expect: { error: 'unknown_time_zone' } },
  { id: 'time-zone-offset-refused', description: 'the browser would take it, the server does not', context: { timeZone: '+01:00' }, condition: ctx('today', 'equals', '2026-06-15'), expect: { error: 'unknown_time_zone' } },
  { id: 'time-zone-empty-refused', context: { timeZone: '' }, condition: ctx('today', 'equals', '2026-06-15'), expect: { error: 'unknown_time_zone' } },
  { id: 'time-zone-etc-name', description: 'Etc/GMT-14 is UTC+14: 10:00Z is midnight on the next day', context: { now: '2026-06-15T10:00:00Z', timeZone: 'Etc/GMT-14' }, condition: ctx('today', 'equals', '2026-06-16'), expect: true },
  { id: 'time-zone-checked-before-the-relative-date', description: 'a relative date reads today too', context: { timeZone: 'Mars/Olympus_Mons' }, values: { arrival: ['2026-06-16'] }, condition: cond('arrival', 'equals', { today: '+1d' }), expect: { error: 'unknown_time_zone' } },

  // ---- Numbers: trailing zeros cost one pass ----
  { id: 'number-ten-thousand-trailing-zeros', description: 'stored as 1; the spike divided by ten once for every zero', number: '1.' + '0'.repeat(10000), expect: '1' },
  { id: 'number-equals-with-ten-thousand-trailing-zeros', values: { amount: ['1'] }, condition: cond('amount', 'equals', '1.' + '0'.repeat(10000)), expect: true },
  { id: 'number-trailing-zeros-after-a-digit', number: '2.50000', expect: '2.5' },
  { id: 'number-negative-zero-with-places', number: '-0.000', expect: '0' },

  // ---- Numbers compare as the values they are, both ways round (ROADMAP 4.8h): the one home of these cases ----
  { id: 'number-zero-equals-minus-zero', description: '-0 is 0', values: { amount: ['0'] }, condition: cond('amount', 'equals', '-0'), expect: true },
  { id: 'number-minus-zero-equals-zero', values: { amount: ['-0'] }, condition: cond('amount', 'equals', '0'), expect: true },
  { id: 'number-minus-zero-not-below-zero', description: 'and is not below it', values: { amount: ['-0'] }, condition: cond('amount', 'lessThan', '0'), expect: false },
  { id: 'number-zero-not-above-minus-zero', values: { amount: ['0'] }, condition: cond('amount', 'greaterThan', '-0'), expect: false },
  { id: 'number-minus-zero-places-equals-zero', values: { amount: ['-0.000'] }, condition: cond('amount', 'equals', '0'), expect: true },
  { id: 'number-10-above-9', description: 'more digits, the larger value', values: { amount: ['10'] }, condition: cond('amount', 'greaterThan', '9'), expect: true },
  { id: 'number-10-not-below-9', values: { amount: ['10'] }, condition: cond('amount', 'lessThan', '9'), expect: false },
  { id: 'number-minus-10-below-minus-9', description: 'negatives in order: more digits, the smaller value', values: { amount: ['-10'] }, condition: cond('amount', 'lessThan', '-9'), expect: true },
  { id: 'number-minus-9-not-below-minus-10', values: { amount: ['-9'] }, condition: cond('amount', 'lessThan', '-10'), expect: false },
  { id: 'number-minus-10-not-above-minus-9', values: { amount: ['-10'] }, condition: cond('amount', 'greaterThan', '-9'), expect: false },
  { id: 'number-1-point-3-above-1-point-25', description: 'a shorter fraction can be the larger', values: { amount: ['1.3'] }, condition: cond('amount', 'greaterThan', '1.25'), expect: true },
  { id: 'number-1-point-25-not-above-1-point-3', values: { amount: ['1.25'] }, condition: cond('amount', 'greaterThan', '1.3'), expect: false },
  { id: 'number-12-below-12-point-0001', description: 'one more place decides', values: { amount: ['12'] }, condition: cond('amount', 'lessThan', '12.0001'), expect: true },
  { id: 'number-12-point-0001-above-12', values: { amount: ['12.0001'] }, condition: cond('amount', 'greaterThan', '12'), expect: true },
  { id: 'number-12-not-equals-12-point-0001', values: { amount: ['12'] }, condition: cond('amount', 'equals', '12.0001'), expect: false },
  { id: 'number-12-point-0001-not-below-12', values: { amount: ['12.0001'] }, condition: cond('amount', 'lessThan', '12'), expect: false },
  { id: 'number-minus-0-point-5-below-zero', values: { amount: ['-0.5'] }, condition: cond('amount', 'lessThan', '0'), expect: true },
  { id: 'number-zero-not-below-minus-0-point-5', values: { amount: ['0'] }, condition: cond('amount', 'lessThan', '-0.5'), expect: false },
  { id: 'number-minus-1-point-5-below-minus-1-point-25', values: { amount: ['-1.5'] }, condition: cond('amount', 'lessThan', '-1.25'), expect: true },
  { id: 'number-minus-1-point-25-not-below-minus-1-point-5', values: { amount: ['-1.25'] }, condition: cond('amount', 'lessThan', '-1.5'), expect: false },
  { id: 'number-thirty-digits-below-thirty-digits-plus-one', description: 'thirty digits, the last one apart', values: { amount: ['123456789012345678901234567890'] }, condition: cond('amount', 'lessThan', '123456789012345678901234567891'), expect: true },
  { id: 'number-thirty-digits-plus-one-above-thirty-digits', values: { amount: ['123456789012345678901234567891'] }, condition: cond('amount', 'greaterThan', '123456789012345678901234567890'), expect: true },
  { id: 'number-thirty-digits-not-equals-thirty-digits-plus-one', values: { amount: ['123456789012345678901234567890'] }, condition: cond('amount', 'equals', '123456789012345678901234567891'), expect: false },
  { id: 'number-wire-exponent-answer-equals-nothing', description: 'read kindly, it would be 1000', values: { amount: ['1e3'] }, condition: cond('amount', 'equals', '1000'), expect: false },
  { id: 'number-wire-exponent-operand-equals-nothing', values: { amount: ['1000'] }, condition: cond('amount', 'equals', '1e3'), expect: false },
  { id: 'number-wire-leading-zero-refused', number: '01', expect: { error: 'not_a_number' } },
  { id: 'number-wire-leading-zero-answer-equals-nothing', description: 'read kindly, it would be 1', values: { amount: ['01'] }, condition: cond('amount', 'equals', '1'), expect: false },
  { id: 'number-wire-leading-zero-operand-equals-nothing', values: { amount: ['1'] }, condition: cond('amount', 'equals', '01'), expect: false },
  { id: 'number-wire-trailing-point-refused', number: '1.', expect: { error: 'not_a_number' } },
  { id: 'number-wire-trailing-point-answer-equals-nothing', description: 'read kindly, it would be 1', values: { amount: ['1.'] }, condition: cond('amount', 'equals', '1'), expect: false },
  { id: 'number-wire-trailing-point-operand-equals-nothing', values: { amount: ['1'] }, condition: cond('amount', 'equals', '1.'), expect: false },
  { id: 'number-wire-leading-point-refused', number: '.5', expect: { error: 'not_a_number' } },
  { id: 'number-wire-leading-point-answer-equals-nothing', description: 'read kindly, it would be 0.5', values: { amount: ['.5'] }, condition: cond('amount', 'equals', '0.5'), expect: false },
  { id: 'number-wire-leading-point-operand-equals-nothing', values: { amount: ['0.5'] }, condition: cond('amount', 'equals', '.5'), expect: false },
  { id: 'number-wire-lone-minus-refused', number: '-', expect: { error: 'not_a_number' } },
  { id: 'number-wire-lone-minus-answer-equals-nothing', description: 'read kindly, it would be 0', values: { amount: ['-'] }, condition: cond('amount', 'equals', '0'), expect: false },
  { id: 'number-wire-lone-minus-operand-equals-nothing', values: { amount: ['0'] }, condition: cond('amount', 'equals', '-'), expect: false },
  { id: 'number-wire-plus-sign-refused', number: '+1', expect: { error: 'not_a_number' } },
  { id: 'number-wire-plus-sign-answer-equals-nothing', description: 'read kindly, it would be 1', values: { amount: ['+1'] }, condition: cond('amount', 'equals', '1'), expect: false },
  { id: 'number-wire-plus-sign-operand-equals-nothing', values: { amount: ['1'] }, condition: cond('amount', 'equals', '+1'), expect: false },
  { id: 'number-wire-comma-refused', number: '1,5', expect: { error: 'not_a_number' } },
  { id: 'number-wire-comma-answer-equals-nothing', description: 'read kindly, it would be 1.5', values: { amount: ['1,5'] }, condition: cond('amount', 'equals', '1.5'), expect: false },
  { id: 'number-wire-comma-operand-equals-nothing', values: { amount: ['1.5'] }, condition: cond('amount', 'equals', '1,5'), expect: false },
  { id: 'number-wire-trailing-letter-refused', number: '1.5x', expect: { error: 'not_a_number' } },
  { id: 'number-wire-trailing-letter-answer-equals-nothing', description: 'read kindly, it would be 1.5', values: { amount: ['1.5x'] }, condition: cond('amount', 'equals', '1.5'), expect: false },
  { id: 'number-wire-trailing-letter-operand-equals-nothing', values: { amount: ['1.5'] }, condition: cond('amount', 'equals', '1.5x'), expect: false },
  { id: 'number-wire-empty-refused', number: '', expect: { error: 'not_a_number' } },
  { id: 'number-wire-exponent-operand-orders-nothing', values: { amount: ['1000'] }, condition: cond('amount', 'lessThan', '1e4'), expect: false },

  // ---- Patterns: the run at the instruction limit with dense splits keeps within its stack (ROADMAP 4.8h) ----
  { id: 'pattern-dense-splits-at-the-limit-allowed', description: 'four letters, then 714 optional choices of four anchors and a letter: 14 instructions each, five of them splits, 10,000 in all; anchors weigh nothing, so the weight is 718', values: { phone: ['abcd' + 'e'.repeat(714)] }, condition: cond('phone', 'matches', 'abcd((^|$|^|$|e)?){714}'), expect: true },
  { id: 'pattern-dense-splits-all-skipped', values: { phone: ['abcd'] }, condition: cond('phone', 'matches', 'abcd((^|$|^|$|e)?){714}'), expect: true },
  { id: 'pattern-dense-splits-one-too-many', values: { phone: ['abcd' + 'e'.repeat(715)] }, condition: cond('phone', 'matches', 'abcd((^|$|^|$|e)?){714}'), expect: false },
  { id: 'pattern-dense-splits-other-letter', values: { phone: ['abcd' + 'e'.repeat(713) + 'f'] }, condition: cond('phone', 'matches', 'abcd((^|$|^|$|e)?){714}'), expect: false },
  { id: 'pattern-dense-splits-over-the-limit-refused', description: 'one letter more: 10,001 instructions', values: { phone: ['abcde'] }, condition: cond('phone', 'matches', 'abcde((^|$|^|$|e)?){714}'), expect: { error: 'unsafe_pattern' } },
];

export default { suite: 'port', defaults, fields, cases };
