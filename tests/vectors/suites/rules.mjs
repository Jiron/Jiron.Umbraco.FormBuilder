// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The rules vectors of ROADMAP 5.2a (section 4.3.3, ADR-0005, ADR-0061): every operator against every kind of answer,
// empty, unchecked and multiple answers, "today" in several time zones, context conditions, conditions that compare
// two answers, nested groups and their depth, switched-off rules, and whole forms: visibility and "hidden means gone",
// the require effect, validation rules, actions, outcomes, grid rows, repeatable groups row by row, and what the
// check refuses before anything is evaluated. The last cases hold edges the mutation check (mutants.mjs) found
// untested. Every expectation is written out here, never computed by the code under test. Special characters are
// built from code points, and in patterns "~" stands for a backslash, so this file stays ASCII.

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
  title: 'text',
  nickname: 'text',
  plan: 'choice',
  topics: 'choices',
  agree: 'checkbox',
  uploads: 'files',
  amount: 'number',
  low: 'number',
  limit: 'number',
  arrival: 'date',
  departure: 'date',
  birthday: 'date',
  start: 'time',
  meeting: 'dateTime',
};

const u = (...codePoints) => String.fromCodePoint(...codePoints);
const BS = String.fromCharCode(92);
const p = (pattern) => pattern.replaceAll('~', BS);
const cond = (field, op, value) => (value === undefined ? { field, op } : { field, op, value });
const ctx = (context, op, value) => (value === undefined ? { context, op } : { context, op, value });
const rule = (effect, conditions, more = {}) => ({ effect, enabled: true, match: 'all', conditions, ...more });
const all = (...conditions) => ({ match: 'all', conditions });
const any = (...conditions) => ({ match: 'any', conditions });
/** A group nested `levels` deep, the innermost holding `inner`. */
const nest = (levels, inner) => (levels === 0 ? inner : all(nest(levels - 1, inner)));

// ---- The matrix: each kind with an answer, an operand equal to it, and one that is not ----
// `count` is how many answers `values` holds once cleaned.
const KINDS = [
  { kind: 'text', field: 'title', values: ['Anna Lin'], same: 'anna lin', other: 'Anna', count: 1 },
  { kind: 'choice', field: 'plan', values: ['gold'], same: 'gold', other: 'Gold', count: 1 },
  { kind: 'choices', field: 'topics', values: ['red', 'blue'], same: 'blue', other: 'green', count: 2 },
  { kind: 'checkbox', field: 'agree', values: ['true'], same: 'true', other: 'false', count: 1 },
  { kind: 'files', field: 'uploads', values: ['t1', 't2'], same: 't2', other: 't3', count: 2 },
  { kind: 'number', field: 'amount', values: ['12.50'], same: '12.5', other: '12.51', count: 1 },
  { kind: 'date', field: 'arrival', values: ['2026-06-20'], same: '2026-06-20', other: '2026-06-21', count: 1 },
  { kind: 'time', field: 'start', values: ['14:30'], same: '14:30:00', other: '14:31', count: 1 },
  { kind: 'dateTime', field: 'meeting', values: ['2026-06-20T14:30'], same: '2026-06-20T14:30:00', other: '2026-06-20T14:31', count: 1 },
];

const matrix = [];
for (const k of KINDS) {
  const answered = { [k.field]: k.values };
  const add = (name, values, condition, expect) => matrix.push({ id: `${k.kind}-${name}`, values, condition, expect });
  add('equals-same', answered, cond(k.field, 'equals', k.same), true);
  add('equals-other', answered, cond(k.field, 'equals', k.other), false);
  add('not-equals-other', answered, cond(k.field, 'notEquals', k.other), true);
  add('not-equals-same', answered, cond(k.field, 'notEquals', k.same), false);
  add('is-one-of', answered, cond(k.field, 'isOneOf', [k.other, k.same]), true);
  add('is-one-of-none', answered, cond(k.field, 'isOneOf', [k.other]), false);
  add('is-none-of', answered, cond(k.field, 'isNoneOf', [k.other, k.same]), false);
  add('is-none-of-when-unanswered', {}, cond(k.field, 'isNoneOf', [k.same]), true);
  add('is-empty-when-unanswered', {}, cond(k.field, 'isEmpty'), true);
  add('is-empty-when-answered', answered, cond(k.field, 'isEmpty'), false);
  add('is-not-empty', answered, cond(k.field, 'isNotEmpty'), true);
  add('is-not-empty-when-unanswered', {}, cond(k.field, 'isNotEmpty'), false);
  add('count-at-least-2', answered, cond(k.field, 'countAtLeast', '2'), k.count >= 2);
  add('count-at-most-1', answered, cond(k.field, 'countAtMost', '1'), k.count <= 1);
  add('count-at-least-0-when-unanswered', {}, cond(k.field, 'countAtLeast', '0'), true);
}

// The text operators on text, then refused on every other kind.
const TEXT_ONLY = [
  ['contains', 'NA L', 'xyz', 'a'],
  ['notContains', 'xyz', 'lin', 'a'],
  ['startsWith', 'ANNA', 'lin', 'a'],
  ['endsWith', 'LIN', 'anna', 'a'],
  ['matches', 'Anna.*', 'Lin.*', 'a'],
];
for (const [op, yes, no, operand] of TEXT_ONLY) {
  matrix.push({ id: `text-${op}-true`, values: { title: ['Anna Lin'] }, condition: cond('title', op, yes), expect: true });
  matrix.push({ id: `text-${op}-false`, values: { title: ['Anna Lin'] }, condition: cond('title', op, no), expect: false });
  for (const k of KINDS.filter((k) => k.kind !== 'text')) {
    matrix.push({ id: `${k.kind}-${op}-refused`, condition: cond(k.field, op, operand), expect: { error: 'operator_not_for_kind' } });
  }
}

// The ordered operators on numbers, dates, times and instants: a true and a false case each, the bounds inclusive.
const ORDERED = [
  { kind: 'number', field: 'amount', value: '12.50', greater: ['9.99', '12.5'], less: ['100', '12.5'], between: [['12.5', '13'], ['12.51', '13']] },
  { kind: 'date', field: 'arrival', value: '2026-06-20', greater: ['2026-06-19', '2026-06-20'], less: ['2026-12-01', '2026-06-20'], between: [['2026-06-20', '2026-06-30'], ['2026-06-21', '2026-06-30']] },
  { kind: 'time', field: 'start', value: '14:30', greater: ['09:45', '14:30:00'], less: ['14:30:01', '14:30'], between: [['14:00', '14:30'], ['14:31', '15:00']] },
  { kind: 'dateTime', field: 'meeting', value: '2026-06-20T14:30', greater: ['2026-06-20T14:29:59', '2026-06-20T14:30'], less: ['2026-06-21T00:00', '2026-06-20T14:30:00'], between: [['2026-06-20T00:00', '2026-06-20T23:59'], ['2026-06-20T14:31', '2026-06-20T23:59']] },
];
for (const o of ORDERED) {
  const values = { [o.field]: [o.value] };
  matrix.push({ id: `${o.kind}-greater-than-true`, values, condition: cond(o.field, 'greaterThan', o.greater[0]), expect: true });
  matrix.push({ id: `${o.kind}-greater-than-equal-is-false`, values, condition: cond(o.field, 'greaterThan', o.greater[1]), expect: false });
  matrix.push({ id: `${o.kind}-less-than-true`, values, condition: cond(o.field, 'lessThan', o.less[0]), expect: true });
  matrix.push({ id: `${o.kind}-less-than-equal-is-false`, values, condition: cond(o.field, 'lessThan', o.less[1]), expect: false });
  matrix.push({ id: `${o.kind}-between-inclusive`, values, condition: cond(o.field, 'between', o.between[0]), expect: true });
  matrix.push({ id: `${o.kind}-between-outside`, values, condition: cond(o.field, 'between', o.between[1]), expect: false });
}
for (const k of KINDS.filter((k) => !ORDERED.some((o) => o.kind === k.kind))) {
  matrix.push({ id: `${k.kind}-greater-than-refused`, condition: cond(k.field, 'greaterThan', 'x'), expect: { error: 'operator_not_for_kind' } });
  matrix.push({ id: `${k.kind}-less-than-refused`, condition: cond(k.field, 'lessThan', 'x'), expect: { error: 'operator_not_for_kind' } });
  matrix.push({ id: `${k.kind}-between-refused`, condition: cond(k.field, 'between', ['a', 'b']), expect: { error: 'operator_not_for_kind' } });
}

// The checked operators on a checkbox (no operand) and on choices (the option), then refused elsewhere.
matrix.push(
  { id: 'checkbox-is-checked-true', values: { agree: ['true'] }, condition: cond('agree', 'isChecked'), expect: true },
  { id: 'checkbox-is-checked-when-unchecked', values: {}, condition: cond('agree', 'isChecked'), expect: false },
  { id: 'checkbox-is-not-checked-when-unchecked', values: {}, condition: cond('agree', 'isNotChecked'), expect: true },
  { id: 'checkbox-is-not-checked-when-checked', values: { agree: ['true'] }, condition: cond('agree', 'isNotChecked'), expect: false },
  { id: 'choice-is-checked-true', values: { plan: ['gold'] }, condition: cond('plan', 'isChecked', 'gold'), expect: true },
  { id: 'choice-is-checked-other', values: { plan: ['gold'] }, condition: cond('plan', 'isChecked', 'silver'), expect: false },
  { id: 'choice-is-not-checked-other', values: { plan: ['gold'] }, condition: cond('plan', 'isNotChecked', 'silver'), expect: true },
  { id: 'choice-is-not-checked-same', values: { plan: ['gold'] }, condition: cond('plan', 'isNotChecked', 'gold'), expect: false },
  { id: 'choices-is-checked-true', values: { topics: ['red', 'blue'] }, condition: cond('topics', 'isChecked', 'blue'), expect: true },
  { id: 'choices-is-checked-other', values: { topics: ['red', 'blue'] }, condition: cond('topics', 'isChecked', 'green'), expect: false },
  { id: 'choices-is-not-checked-other', values: { topics: ['red', 'blue'] }, condition: cond('topics', 'isNotChecked', 'green'), expect: true },
  { id: 'choices-is-not-checked-same', values: { topics: ['red', 'blue'] }, condition: cond('topics', 'isNotChecked', 'red'), expect: false },
);
for (const k of KINDS.filter((k) => !['checkbox', 'choice', 'choices'].includes(k.kind))) {
  matrix.push({ id: `${k.kind}-is-checked-refused`, condition: cond(k.field, 'isChecked', 'x'), expect: { error: 'operator_not_for_kind' } });
  matrix.push({ id: `${k.kind}-is-not-checked-refused`, condition: cond(k.field, 'isNotChecked', 'x'), expect: { error: 'operator_not_for_kind' } });
}

const conditions = [
  // ---- Empty, unchecked and multiple answers ----
  { id: 'empty-answers-are-cleaned-away', description: 'an empty text and one of spaces are no answers', values: { title: ['', '   '] }, condition: cond('title', 'isEmpty'), expect: true },
  { id: 'empty-answers-do-not-count', values: { topics: ['', 'red'] }, condition: cond('topics', 'countAtMost', '1'), expect: true },
  { id: 'empty-not-equals-is-true', description: 'a negation is the exact opposite, also when unanswered', values: {}, condition: cond('title', 'notEquals', 'Anna'), expect: true },
  { id: 'empty-between-is-false', values: {}, condition: cond('amount', 'between', ['1', '10']), expect: false },
  { id: 'empty-greater-than-is-false', values: {}, condition: cond('arrival', 'greaterThan', '2000-01-01'), expect: false },
  { id: 'empty-contains-is-false', values: {}, condition: cond('title', 'contains', 'a'), expect: false },
  { id: 'empty-matches-is-false', values: {}, condition: cond('title', 'matches', '.*'), expect: false },
  { id: 'unchecked-checkbox-equals-true-is-false', values: {}, condition: cond('agree', 'equals', 'true'), expect: false },
  { id: 'unchecked-checkbox-count-at-most-0', values: {}, condition: cond('agree', 'countAtMost', '0'), expect: true },
  { id: 'unchecked-choices-is-not-checked', values: {}, condition: cond('topics', 'isNotChecked', 'red'), expect: true },
  { id: 'multi-text-equals-any-answer', values: { title: ['Bea', 'Anna'] }, condition: cond('title', 'equals', 'anna'), expect: true },
  { id: 'multi-text-contains-any-answer', values: { title: ['Bea', 'Anna'] }, condition: cond('title', 'contains', 'NN'), expect: true },
  { id: 'multi-text-not-contains-all-answers', description: 'not contains: no answer contains it', values: { title: ['Bea', 'Anna'] }, condition: cond('title', 'notContains', 'nn'), expect: false },
  { id: 'multi-number-less-than-uses-the-first', values: { amount: ['5', '50'] }, condition: cond('amount', 'lessThan', '10'), expect: true },
  { id: 'multi-date-greater-than-uses-the-first', values: { arrival: ['2026-01-01', '2027-01-01'] }, condition: cond('arrival', 'greaterThan', '2026-06-01'), expect: false },
  { id: 'multi-choices-not-equals-one-of-them', values: { topics: ['red', 'blue'] }, condition: cond('topics', 'notEquals', 'red'), expect: false },
  { id: 'multi-choices-count-at-least-3', values: { topics: ['red', 'blue', 'green'] }, condition: cond('topics', 'countAtLeast', '3'), expect: true },
  { id: 'multi-files-is-none-of', values: { uploads: ['t1', 't2'] }, condition: cond('uploads', 'isNoneOf', ['t3', 't4']), expect: true },
  { id: 'multi-pattern-any-answer', values: { title: ['abc', '123'] }, condition: cond('title', 'matches', '^[0-9]+$'), expect: true },

  // ---- "today" injected, in several time zones ----
  { id: 'relative-today-zurich-after-midnight', description: '22:30Z on 15 June is 00:30 on 16 June in Zurich (UTC+2)', context: { now: '2026-06-15T22:30:00Z' }, values: { arrival: ['2026-06-17'] }, condition: cond('arrival', 'equals', { today: '+1d' }), expect: true },
  { id: 'relative-today-new-york-same-instant', description: 'the same instant is 18:30 on 15 June in New York (UTC-4)', context: { now: '2026-06-15T22:30:00Z', timeZone: 'America/New_York' }, values: { arrival: ['2026-06-17'] }, condition: cond('arrival', 'equals', { today: '+1d' }), expect: false },
  { id: 'relative-today-new-york-day-after', context: { now: '2026-06-15T22:30:00Z', timeZone: 'America/New_York' }, values: { arrival: ['2026-06-16'] }, condition: cond('arrival', 'equals', { today: '+1d' }), expect: true },
  { id: 'context-today-tokyo-ahead', description: '15:30Z is 00:30 the next day in Tokyo (UTC+9)', context: { now: '2026-06-15T15:30:00Z', timeZone: 'Asia/Tokyo' }, condition: ctx('today', 'equals', '2026-06-16'), expect: true },
  { id: 'context-today-new-york-new-years-eve', context: { now: '2026-01-01T03:00:00Z', timeZone: 'America/New_York' }, condition: ctx('today', 'equals', '2025-12-31'), expect: true },
  { id: 'context-today-zurich-new-year', context: { now: '2026-01-01T03:00:00Z' }, condition: ctx('today', 'equals', '2026-01-01'), expect: true },
  { id: 'context-today-between-relative-days', condition: ctx('today', 'between', [{ today: '-1d' }, { today: '+1d' }]), expect: true },
  { id: 'context-today-greater-than', condition: ctx('today', 'greaterThan', '2026-06-14'), expect: true },
  { id: 'context-today-is-not-empty', condition: ctx('today', 'isNotEmpty'), expect: true },
  { id: 'relative-age-on-the-birthday', description: 'born exactly 18 years before today is not before it', values: { birthday: ['2008-06-15'] }, condition: cond('birthday', 'lessThan', { today: '-18y' }), expect: false },
  { id: 'relative-age-the-day-before', values: { birthday: ['2008-06-14'] }, condition: cond('birthday', 'lessThan', { today: '-18y' }), expect: true },
  { id: 'relative-two-weeks-back-is-exclusive', values: { arrival: ['2026-06-01'] }, condition: cond('arrival', 'greaterThan', { today: '-2w' }), expect: false },
  { id: 'relative-two-weeks-back-day-after', values: { arrival: ['2026-06-02'] }, condition: cond('arrival', 'greaterThan', { today: '-2w' }), expect: true },
  { id: 'relative-between-today-and-a-month', values: { arrival: ['2026-07-15'] }, condition: cond('arrival', 'between', [{ today: '+0d' }, { today: '+1m' }]), expect: true },
  { id: 'relative-is-one-of-days', values: { arrival: ['2026-06-17'] }, condition: cond('arrival', 'isOneOf', [{ today: '+1d' }, { today: '+2d' }]), expect: true },

  // ---- Context conditions ----
  { id: 'context-culture-equals-folds-case', condition: ctx('culture', 'equals', 'DE-ch'), expect: true },
  { id: 'context-culture-is-one-of', condition: ctx('culture', 'isOneOf', ['fr-CH', 'de-CH']), expect: true },
  { id: 'context-member-signed-in', context: { member: { signedIn: true, groups: [] } }, condition: ctx('memberSignedIn', 'isChecked'), expect: true },
  { id: 'context-member-anonymous-is-not-checked', condition: ctx('memberSignedIn', 'isChecked'), expect: false },
  { id: 'context-member-groups-empty-when-anonymous', condition: ctx('memberGroup', 'isEmpty'), expect: true },
  { id: 'context-member-groups-count', context: { member: { signedIn: true, groups: ['Partners', 'Staff'] } }, condition: ctx('memberGroup', 'countAtLeast', '2'), expect: true },
  { id: 'context-member-group-is-checked', context: { member: { signedIn: true, groups: ['Partners'] } }, condition: ctx('memberGroup', 'isChecked', 'Partners'), expect: true },
  { id: 'context-member-group-is-exact', description: 'group names compare as options, exactly', context: { member: { signedIn: true, groups: ['Partners'] } }, condition: ctx('memberGroup', 'equals', 'partners'), expect: false },
  { id: 'context-query-absent-is-empty', condition: ctx('query.ref', 'isEmpty'), expect: true },
  { id: 'context-query-several-values', context: { query: { ref: ['a', 'b'] } }, condition: ctx('query.ref', 'isOneOf', ['b']), expect: true },
  { id: 'context-query-contains-folds', context: { query: { ref: 'web' } }, condition: ctx('query.ref', 'contains', 'EB'), expect: true },
  { id: 'context-unknown-name', condition: ctx('weather', 'equals', 'sunny'), expect: { error: 'unknown_field' } },
  { id: 'context-and-field-together', condition: { field: 'title', context: 'culture', op: 'isEmpty' }, expect: { error: 'invalid_condition' } },

  // ---- Conditions that compare two answers ----
  { id: 'field-date-after-another', values: { arrival: ['2026-06-20'], departure: ['2026-06-22'] }, condition: cond('departure', 'greaterThan', { field: 'arrival' }), expect: true },
  { id: 'field-operand-unanswered-never-compares', values: { departure: ['2026-06-22'] }, condition: cond('departure', 'greaterThan', { field: 'arrival' }), expect: false },
  { id: 'field-number-less-than-another', description: 'by value, not as text', values: { amount: ['9.5'], limit: ['10'] }, condition: cond('amount', 'lessThan', { field: 'limit' }), expect: true },
  { id: 'field-text-equals-another-folded', values: { title: ['ANNA'], nickname: ['anna'] }, condition: cond('title', 'equals', { field: 'nickname' }), expect: true },
  { id: 'field-is-one-of-another-fields-answers', values: { plan: ['red'], topics: ['red', 'blue'] }, condition: cond('plan', 'isOneOf', { field: 'topics' }), expect: true },
  { id: 'field-between-two-fields', values: { amount: ['5'], low: ['1'], limit: ['10'] }, condition: cond('amount', 'between', [{ field: 'low' }, { field: 'limit' }]), expect: true },
  { id: 'field-list-of-text-and-field', values: { plan: ['gold'], nickname: ['gold'] }, condition: cond('plan', 'isOneOf', ['silver', { field: 'nickname' }]), expect: true },
  { id: 'field-operand-hidden-is-empty', context: { hidden: ['arrival'] }, values: { arrival: ['2026-06-20'], departure: ['2026-06-22'] }, condition: cond('departure', 'greaterThan', { field: 'arrival' }), expect: false },
  { id: 'field-operand-unknown', condition: cond('departure', 'greaterThan', { field: 'nope' }), expect: { error: 'unknown_field' } },
  { id: 'field-not-equals-another', values: { title: ['Anna'], nickname: ['Bea'] }, condition: cond('title', 'notEquals', { field: 'nickname' }), expect: true },
];

const rules = [
  // ---- Groups: nesting, depth and matching ----
  { id: 'group-depth-ten-is-allowed', description: 'the rule is the first level and nine groups nest inside it', values: { title: ['x'] }, rule: rule('show', [nest(9, cond('title', 'isNotEmpty'))]), expect: true },
  { id: 'group-depth-eleven-is-too-deep', values: { title: ['x'] }, rule: rule('show', [nest(10, cond('title', 'isNotEmpty'))]), expect: { error: 'too_deep' } },
  { id: 'group-too-deep-before-its-broken-condition', description: 'the depth is checked on entering a group, before what it holds', rule: rule('show', [nest(10, cond('title', 'nope'))]), expect: { error: 'too_deep' } },
  { id: 'group-earlier-broken-condition-first', description: 'the check goes in order: a broken condition before a deep group is reported', rule: rule('show', [cond('title', 'nope'), nest(10, cond('title', 'isEmpty'))]), expect: { error: 'unknown_operator' } },
  { id: 'group-too-deep-even-when-switched-off', rule: rule('show', [nest(10, cond('title', 'isEmpty'))], { enabled: false }), expect: { error: 'too_deep' } },
  { id: 'group-empty-all-matches', rule: rule('show', []), expect: true },
  { id: 'group-empty-any-does-not-match', rule: rule('show', [], { match: 'any' }), expect: false },
  { id: 'group-any-inside-all', values: { plan: ['gold'], topics: ['red'] }, rule: rule('show', [cond('plan', 'equals', 'gold'), any(cond('topics', 'isChecked', 'blue'), cond('topics', 'isChecked', 'red'))]), expect: true },
  { id: 'group-all-inside-any', values: { plan: ['gold'] }, rule: rule('show', [all(cond('plan', 'equals', 'gold'), cond('agree', 'isChecked')), cond('title', 'isNotEmpty')], { match: 'any' }), expect: false },
  { id: 'group-unknown-match', rule: rule('show', [{ match: 'some', conditions: [] }]), expect: { error: 'invalid_condition' } },
  { id: 'rule-switched-off-is-still-checked', rule: rule('show', [cond('nope', 'isEmpty')], { enabled: false }), expect: { error: 'unknown_field' } },
  { id: 'rule-switched-off-gives-no-answer', values: { title: ['x'] }, rule: rule('hide', [cond('title', 'isNotEmpty')], { enabled: false }), expect: null },
  { id: 'rule-require-matches', values: { agree: ['true'] }, rule: rule('require', [cond('agree', 'isChecked')]), expect: true },
];

// ---- Whole forms (ADR-0061) ----
const f = (id, kind, more = {}) => (kind === null ? { id, ...more } : { id, kind, ...more });
/** One page with one group holding the fields. */
const form = (fieldList, more = {}) => ({ pages: [{ id: 'p1', groups: [{ id: 'g1', fields: fieldList }] }], ...more });
const none = { hidden: [], required: [], messages: {}, rows: {}, actions: [], outcome: null };
const state = (partial = {}) => ({ ...none, ...partial });
const row = (partial = {}) => ({ hidden: [], required: [], messages: {}, ...partial });
const hiddenByCulture = rule('hide', [ctx('culture', 'startsWith', 'de')]);

const forms = [
  // Visibility.
  { id: 'form-nothing-to-decide', form: form([f('name', 'text')]), expect: state() },
  { id: 'form-required-field', form: form([f('name', 'text', { required: true })]), expect: state({ required: ['name'] }) },
  { id: 'form-show-rule-matches', values: { plan: ['pro'] }, form: form([f('plan', 'choice'), f('contact', 'text', { rules: [rule('show', [cond('plan', 'equals', 'pro')])] })]), expect: state() },
  { id: 'form-show-rule-does-not-match', values: { plan: ['basic'] }, form: form([f('plan', 'choice'), f('contact', 'text', { rules: [rule('show', [cond('plan', 'equals', 'pro')])] })]), expect: state({ hidden: ['contact'] }) },
  { id: 'form-hide-rule-matches', values: { plan: ['pro'] }, form: form([f('plan', 'choice'), f('contact', 'text', { rules: [rule('hide', [cond('plan', 'equals', 'pro')])] })]), expect: state({ hidden: ['contact'] }) },
  { id: 'form-hide-rule-does-not-match', values: {}, form: form([f('plan', 'choice'), f('contact', 'text', { rules: [rule('hide', [cond('plan', 'equals', 'pro')])] })]), expect: state() },
  { id: 'form-switched-off-show-rule-counts-as-none', form: form([f('contact', 'text', { rules: [rule('show', [cond('contact', 'isNotEmpty')], { enabled: false })] })]), expect: state() },
  { id: 'form-switched-off-rule-is-still-checked', form: form([f('contact', 'text', { rules: [rule('show', [cond('nope', 'isEmpty')], { enabled: false })] })]), expect: { error: 'unknown_field' } },
  { id: 'form-context-rule-signed-in-member', context: { member: { signedIn: true, groups: [] } }, form: form([f('discount', 'text', { rules: [rule('show', [ctx('memberSignedIn', 'isChecked')])] })]), expect: state() },
  { id: 'form-context-rule-anonymous', form: form([f('discount', 'text', { rules: [rule('show', [ctx('memberSignedIn', 'isChecked')])] })]), expect: state({ hidden: ['discount'] }) },
  { id: 'form-relative-date-rule', description: 'today is 15 June: an arrival within a week shows the notice', values: { arrival: ['2026-06-20'] }, form: form([f('arrival', 'date'), f('notice', null, { rules: [rule('show', [cond('arrival', 'lessThan', { today: '+7d' })])] })]), expect: state() },
  { id: 'form-context-hidden-list-is-ignored', description: 'the engine decides what is hidden; the context list plays no part', context: { hidden: ['plan'] }, values: { plan: ['pro'] }, form: form([f('plan', 'choice'), f('contact', 'text', { rules: [rule('show', [cond('plan', 'equals', 'pro')])] })]), expect: state() },
  { id: 'form-field-without-kind-can-be-hidden', form: form([f('intro', null, { rules: [hiddenByCulture] })]), expect: state({ hidden: ['intro'] }) },
  { id: 'form-field-without-kind-is-never-required', form: form([f('intro', null, { required: true })]), expect: state() },

  // Hidden means gone.
  { id: 'form-hidden-field-answers-are-gone', values: { a: ['x'] }, form: form([f('a', 'text', { rules: [hiddenByCulture] }), f('b', 'text', { rules: [rule('show', [cond('a', 'equals', 'x')])] })]), expect: state({ hidden: ['a', 'b'] }) },
  { id: 'form-hidden-field-is-empty', values: { a: ['x'] }, form: form([f('a', 'text', { rules: [hiddenByCulture] }), f('b', 'text', { rules: [rule('show', [cond('a', 'isEmpty')])] })]), expect: state({ hidden: ['a'] }) },
  { id: 'form-hidden-field-gone-as-an-operand', values: { a: ['x'], c: ['x'] }, form: form([f('a', 'text', { rules: [hiddenByCulture] }), f('c', 'text'), f('b', 'text', { rules: [rule('show', [cond('c', 'equals', { field: 'a' })])] })]), expect: state({ hidden: ['a', 'b'] }) },
  { id: 'form-hidden-field-not-required', form: form([f('a', 'text', { required: true, rules: [hiddenByCulture] })]), expect: state({ hidden: ['a'] }) },
  {
    id: 'form-hidden-by-its-group',
    values: { a: ['x'] },
    form: { pages: [{ id: 'p1', groups: [{ id: 'g1', fields: [f('a', 'text')] }, { id: 'g2', rules: [rule('hide', [cond('a', 'isNotEmpty')])], fields: [f('b', 'text', { required: true })] }] }] },
    expect: state({ hidden: ['g2', 'b'] }),
  },
  {
    id: 'form-hidden-by-its-page',
    values: { a: ['x'] },
    form: { pages: [{ id: 'p1', groups: [{ id: 'g1', fields: [f('a', 'text')] }] }, { id: 'p2', rules: [rule('show', [cond('a', 'isEmpty')])], groups: [{ id: 'g2', fields: [f('b', 'text', { required: true })] }] }] },
    expect: state({ hidden: ['p2', 'g2', 'b'] }),
  },
  {
    id: 'form-hidden-page-propagates-through-rules',
    description: 'a field on the first page shows by a field on a hidden later page, whose answer is gone',
    values: { x: ['1'] },
    form: { pages: [{ id: 'p1', groups: [{ id: 'g1', fields: [f('y', 'text', { rules: [rule('show', [cond('x', 'isNotEmpty')])] })] }] }, { id: 'p2', rules: [hiddenByCulture], groups: [{ id: 'g2', fields: [f('x', 'text')] }] }] },
    expect: state({ hidden: ['y', 'p2', 'g2', 'x'] }),
  },
  {
    id: 'form-order-follows-dependencies',
    description: 'first depends on second, second on third: decided third, second, first; listed in document order',
    values: { first: ['a'], second: ['yes'], third: ['1'] },
    form: form([
      f('first', 'text', { rules: [rule('show', [cond('second', 'equals', 'yes')])] }),
      f('second', 'text', { rules: [rule('hide', [cond('third', 'isNotEmpty')])] }),
      f('third', 'text'),
    ]),
    expect: state({ hidden: ['first', 'second'] }),
  },
  {
    id: 'form-chain-of-five',
    values: { a: ['go'], b: ['go'], c: ['go'], d: ['go'] },
    form: form([
      f('e', 'text', { rules: [rule('show', [cond('d', 'equals', 'go')])] }),
      f('d', 'text', { rules: [rule('show', [cond('c', 'equals', 'go')])] }),
      f('c', 'text', { rules: [rule('show', [cond('b', 'equals', 'go')])] }),
      f('b', 'text', { rules: [rule('show', [cond('a', 'equals', 'go')])] }),
      f('a', 'text', { rules: [hiddenByCulture] }),
    ]),
    expect: state({ hidden: ['e', 'd', 'c', 'b', 'a'] }),
  },

  // The require effect.
  { id: 'form-require-rule-matches', values: { contact: ['phone'] }, form: form([f('contact', 'choice'), f('phone', 'text', { rules: [rule('require', [cond('contact', 'equals', 'phone')])] })]), expect: state({ required: ['phone'] }) },
  { id: 'form-require-rule-does-not-match', values: { contact: ['email'] }, form: form([f('contact', 'choice'), f('phone', 'text', { rules: [rule('require', [cond('contact', 'equals', 'phone')])] })]), expect: state() },
  { id: 'form-require-rule-switched-off', values: { contact: ['phone'] }, form: form([f('contact', 'choice'), f('phone', 'text', { rules: [rule('require', [cond('contact', 'equals', 'phone')], { enabled: false })] })]), expect: state() },
  { id: 'form-required-itself-whatever-the-rule', values: {}, form: form([f('contact', 'choice'), f('phone', 'text', { required: true, rules: [rule('require', [cond('contact', 'equals', 'phone')])] })]), expect: state({ required: ['phone'] }) },
  { id: 'form-require-rule-on-a-hidden-field', values: { contact: ['phone'] }, form: form([f('contact', 'choice'), f('phone', 'text', { rules: [hiddenByCulture, rule('require', [cond('contact', 'equals', 'phone')])] })]), expect: state({ hidden: ['phone'] }) },
  { id: 'form-require-rule-sees-hidden-as-empty', values: { a: ['x'] }, form: form([f('a', 'text', { rules: [hiddenByCulture] }), f('b', 'text', { rules: [rule('require', [cond('a', 'isEmpty')])] })]), expect: state({ hidden: ['a'], required: ['b'] }) },
  { id: 'form-require-rule-on-itself-is-no-cycle', description: 'only show and hide rules order the form', values: { a: ['x'] }, form: form([f('a', 'text', { rules: [rule('require', [cond('a', 'isNotEmpty')])] })]), expect: state({ required: ['a'] }) },
  { id: 'form-required-in-document-order', form: form([f('b', 'text', { required: true }), f('a', 'text', { required: true })]), expect: state({ required: ['b', 'a'] }) },

  // What the check refuses.
  { id: 'form-require-on-a-field-without-kind', form: form([f('intro', null, { rules: [rule('require', [])] })]), expect: { error: 'invalid_rule' } },
  { id: 'form-require-on-a-page', form: { pages: [{ id: 'p1', rules: [rule('require', [])], groups: [] }] }, expect: { error: 'invalid_rule' } },
  { id: 'form-require-on-a-group', form: { pages: [{ id: 'p1', groups: [{ id: 'g1', rules: [rule('require', [])], fields: [] }] }] }, expect: { error: 'invalid_rule' } },
  { id: 'form-run-on-a-field', form: form([f('a', 'text', { rules: [rule('run', [])] })]), expect: { error: 'invalid_rule' } },
  { id: 'form-unknown-effect', form: form([f('a', 'text', { rules: [rule('blink', [])] })]), expect: { error: 'invalid_rule' } },
  { id: 'form-show-and-hide', form: form([f('a', 'text', { rules: [rule('show', []), rule('hide', [])] })]), expect: { error: 'invalid_rule' } },
  { id: 'form-hide-and-show', form: form([f('a', 'text', { rules: [rule('hide', []), rule('show', [], { enabled: false })] })]), expect: { error: 'invalid_rule' } },
  { id: 'form-two-show-rules', form: form([f('a', 'text', { rules: [rule('show', []), rule('show', [])] })]), expect: { error: 'invalid_rule' } },
  { id: 'form-two-require-rules', form: form([f('a', 'text', { rules: [rule('require', []), rule('require', [], { enabled: false })] })]), expect: { error: 'invalid_rule' } },
  { id: 'form-show-and-require-together', form: form([f('a', 'text', { rules: [rule('show', []), rule('require', [])] })]), expect: state({ required: ['a'] }) },
  { id: 'form-show-on-an-action', form: form([], { actions: [{ id: 'a1', rules: [rule('show', [])] }] }), expect: { error: 'invalid_rule' } },
  { id: 'form-run-on-an-outcome', form: form([], { outcomes: [{ id: 'o1', rules: [rule('run', [])] }] }), expect: { error: 'invalid_rule' } },
  { id: 'form-two-run-rules', form: form([], { actions: [{ id: 'a1', rules: [rule('run', []), rule('run', [])] }] }), expect: { error: 'invalid_rule' } },
  { id: 'form-unknown-field-in-a-rule', form: form([f('a', 'text', { rules: [rule('show', [cond('nope', 'isEmpty')])] })]), expect: { error: 'unknown_field' } },
  { id: 'form-field-without-kind-is-no-subject', form: form([f('intro', null), f('a', 'text', { rules: [rule('show', [cond('intro', 'isEmpty')])] })]), expect: { error: 'unknown_field' } },
  { id: 'form-too-deep-in-a-field-rule', form: form([f('a', 'text', { rules: [rule('show', [nest(10, cond('a', 'isEmpty'))])] })]), expect: { error: 'too_deep' } },
  { id: 'form-too-deep-in-a-validation-rule', form: form([f('a', 'text')], { validationRules: [{ id: 'v1', field: 'a', enabled: true, match: 'all', conditions: [nest(10, cond('a', 'isEmpty'))] }] }), expect: { error: 'too_deep' } },
  { id: 'form-unsafe-pattern-in-an-outcome', form: form([f('a', 'text')], { outcomes: [{ id: 'o1', rules: [rule('use', [cond('a', 'matches', '(?=x)')])] }] }), expect: { error: 'unsafe_pattern' } },
  { id: 'form-duplicate-field-id', form: form([f('a', 'text'), f('a', 'number')]), expect: { error: 'invalid_form' } },
  { id: 'form-page-and-field-share-an-id', form: form([f('p1', 'text')]), expect: { error: 'invalid_form' } },
  { id: 'form-action-shares-a-field-id', form: form([f('a', 'text')], { actions: [{ id: 'a' }] }), expect: { error: 'invalid_form' } },
  { id: 'form-grid-row-shares-a-field-id', form: form([f('a', 'text'), f('grid', null, { rows: [{ id: 'a', kind: 'choice' }] })]), expect: { error: 'invalid_form' } },
  { id: 'form-nested-repeatable-group', form: form([f('outer', null, { fields: [f('inner', null, { fields: [f('x', 'text')] })] })]), expect: { error: 'invalid_form' } },
  { id: 'form-cycle-on-itself', form: form([f('a', 'text', { rules: [rule('show', [cond('a', 'isNotEmpty')])] })]), expect: { error: 'cycle' } },
  { id: 'form-cycle-of-two', form: form([f('a', 'text', { rules: [rule('show', [cond('b', 'isNotEmpty')])] }), f('b', 'text', { rules: [rule('show', [cond('a', 'isNotEmpty')])] })]), expect: { error: 'cycle' } },
  { id: 'form-cycle-through-an-operand', form: form([f('a', 'text', { rules: [rule('show', [cond('c', 'equals', { field: 'b' })])] }), f('b', 'text', { rules: [rule('hide', [cond('a', 'isEmpty')])] }), f('c', 'text')]), expect: { error: 'cycle' } },
  {
    id: 'form-dependency-through-a-list-operand',
    description: 'b, named inside a list, is decided before a, so its hidden answer is gone',
    values: { b: ['y'], c: ['y'] },
    form: form([f('a', 'text', { rules: [rule('show', [cond('c', 'isOneOf', ['x', { field: 'b' }])])] }), f('b', 'text', { rules: [hiddenByCulture] }), f('c', 'text')]),
    expect: state({ hidden: ['a', 'b'] }),
  },
  { id: 'form-cycle-through-a-list-operand', form: form([f('a', 'text', { rules: [rule('show', [cond('c', 'isOneOf', ['x', { field: 'a' }])])] }), f('c', 'text')]), expect: { error: 'cycle' } },
  {
    id: 'form-dependency-through-a-nested-group',
    description: 'b, named inside a group of conditions, is decided before a, so its hidden answer is gone',
    values: { b: ['y'] },
    form: form([f('a', 'text', { rules: [rule('show', [any(cond('b', 'isNotEmpty'), cond('c', 'isNotEmpty'))])] }), f('b', 'text', { rules: [hiddenByCulture] }), f('c', 'text')]),
    expect: state({ hidden: ['a', 'b'] }),
  },
  { id: 'form-cycle-through-a-nested-group', form: form([f('a', 'text', { rules: [rule('show', [any(cond('a', 'isNotEmpty'), cond('c', 'isNotEmpty'))])] }), f('c', 'text')]), expect: { error: 'cycle' } },
  { id: 'form-cycle-through-the-group', form: { pages: [{ id: 'p1', groups: [{ id: 'g1', rules: [rule('show', [cond('a', 'isNotEmpty')])], fields: [f('a', 'text')] }] }] }, expect: { error: 'cycle' } },
  { id: 'form-cycle-through-the-page', form: { pages: [{ id: 'p1', rules: [rule('hide', [cond('a', 'isEmpty')])], groups: [{ id: 'g1', fields: [f('a', 'text')] }] }] }, expect: { error: 'cycle' } },
  { id: 'form-cycle-through-a-grid-row', form: form([f('grid', null, { rows: [{ id: 'r1', kind: 'choice' }], rules: [rule('show', [cond('r1', 'isNotEmpty')])] })]), expect: { error: 'cycle' } },
  { id: 'form-switched-off-rule-makes-no-cycle', values: { b: ['x'] }, form: form([f('a', 'text', { rules: [rule('show', [cond('b', 'isNotEmpty')], { enabled: false })] }), f('b', 'text', { rules: [rule('show', [cond('a', 'isEmpty')])] })]), expect: state() },
  { id: 'form-check-before-cycle', description: 'every rule is checked before the order is: a broken outcome wins over a cycle', form: form([f('a', 'text', { rules: [rule('show', [cond('a', 'isNotEmpty')])] })], { outcomes: [{ id: 'o1', rules: [rule('use', [cond('a', 'nope')])] }] }), expect: { error: 'unknown_operator' } },
  { id: 'form-elements-checked-before-validation-rules', form: form([f('a', 'number', { rules: [rule('show', [cond('a', 'between', '1')])] })], { validationRules: [{ id: 'v1', field: 'nope', enabled: true, match: 'all', conditions: [] }] }), expect: { error: 'invalid_operand' } },
  { id: 'form-validation-rules-checked-before-actions', form: form([f('a', 'text')], { validationRules: [{ id: 'v1', field: 'nope', enabled: true, match: 'all', conditions: [] }], actions: [{ id: 'a1', rules: [rule('show', [])] }] }), expect: { error: 'unknown_field' } },

  // Validation rules.
  { id: 'form-validation-rule-matches', values: { email: ['anna.example.org'] }, form: form([f('email', 'text')], { validationRules: [{ id: 'v1', field: 'email', enabled: true, match: 'all', conditions: [cond('email', 'notContains', '@')] }] }), expect: state({ messages: { email: ['v1'] } }) },
  { id: 'form-validation-rule-does-not-match', values: { email: ['anna@example.org'] }, form: form([f('email', 'text')], { validationRules: [{ id: 'v1', field: 'email', enabled: true, match: 'all', conditions: [cond('email', 'notContains', '@')] }] }), expect: state() },
  { id: 'form-validation-rule-switched-off', values: { email: ['x'] }, form: form([f('email', 'text')], { validationRules: [{ id: 'v1', field: 'email', enabled: false, match: 'all', conditions: [cond('email', 'notContains', '@')] }] }), expect: state() },
  { id: 'form-validation-rule-on-a-hidden-field', values: { email: ['x'] }, form: form([f('email', 'text', { rules: [hiddenByCulture] })], { validationRules: [{ id: 'v1', field: 'email', enabled: true, match: 'all', conditions: [cond('email', 'isNotEmpty')] }] }), expect: state({ hidden: ['email'] }) },
  { id: 'form-validation-rules-in-order', values: { a: ['5'], b: ['3'] }, form: form([f('a', 'number'), f('b', 'number')], { validationRules: [{ id: 'v2', field: 'b', enabled: true, match: 'all', conditions: [cond('b', 'lessThan', { field: 'a' })] }, { id: 'v1', field: 'b', enabled: true, match: 'any', conditions: [cond('a', 'greaterThan', '4')] }] }), expect: state({ messages: { b: ['v2', 'v1'] } }) },
  { id: 'form-validation-rule-sees-hidden-as-empty', values: { a: ['x'] }, form: form([f('a', 'text', { rules: [hiddenByCulture] }), f('b', 'text')], { validationRules: [{ id: 'v1', field: 'b', enabled: true, match: 'all', conditions: [cond('a', 'isEmpty')] }] }), expect: state({ hidden: ['a'], messages: { b: ['v1'] } }) },
  { id: 'form-validation-rule-unknown-field', form: form([f('a', 'text')], { validationRules: [{ id: 'v1', field: 'nope', enabled: true, match: 'all', conditions: [] }] }), expect: { error: 'unknown_field' } },
  { id: 'form-validation-rule-on-a-field-without-kind', form: form([f('intro', null)], { validationRules: [{ id: 'v1', field: 'intro', enabled: true, match: 'all', conditions: [] }] }), expect: { error: 'unknown_field' } },
  { id: 'form-validation-rule-on-a-grid-row', values: { r1: ['bad'] }, form: form([f('grid', null, { rows: [{ id: 'r1', kind: 'choice' }, { id: 'r2', kind: 'choice' }] })], { validationRules: [{ id: 'v1', field: 'r1', enabled: true, match: 'all', conditions: [cond('r1', 'equals', 'bad'), cond('r2', 'isEmpty')] }] }), expect: state({ messages: { r1: ['v1'] } }) },

  // Actions and outcomes.
  { id: 'form-actions-without-rules-run', form: form([], { actions: [{ id: 'a1' }, { id: 'a2', rules: [] }] }), expect: state({ actions: ['a1', 'a2'] }) },
  { id: 'form-action-run-rule-matches', values: { agree: ['true'] }, form: form([f('agree', 'checkbox')], { actions: [{ id: 'a1', rules: [rule('run', [cond('agree', 'isChecked')])] }] }), expect: state({ actions: ['a1'] }) },
  { id: 'form-action-run-rule-does-not-match', form: form([f('agree', 'checkbox')], { actions: [{ id: 'a1', rules: [rule('run', [cond('agree', 'isChecked')])] }, { id: 'a2' }] }), expect: state({ actions: ['a2'] }) },
  { id: 'form-action-run-rule-switched-off', form: form([f('agree', 'checkbox')], { actions: [{ id: 'a1', rules: [rule('run', [cond('agree', 'isChecked')], { enabled: false })] }] }), expect: state({ actions: ['a1'] }) },
  { id: 'form-action-sees-hidden-as-empty', values: { a: ['x'] }, form: form([f('a', 'text', { rules: [hiddenByCulture] })], { actions: [{ id: 'a1', rules: [rule('run', [cond('a', 'isNotEmpty')])] }] }), expect: state({ hidden: ['a'] }) },
  { id: 'form-outcome-first-that-qualifies', values: { plan: ['basic'] }, form: form([f('plan', 'choice')], { outcomes: [{ id: 'o1', rules: [rule('use', [cond('plan', 'equals', 'pro')])] }, { id: 'o2' }, { id: 'o3' }] }), expect: state({ outcome: 'o2' }) },
  { id: 'form-outcome-without-rule-comes-first', values: { plan: ['pro'] }, form: form([f('plan', 'choice')], { outcomes: [{ id: 'o1' }, { id: 'o2', rules: [rule('use', [cond('plan', 'equals', 'pro')])] }] }), expect: state({ outcome: 'o1' }) },
  { id: 'form-outcome-rule-matches', values: { plan: ['pro'] }, form: form([f('plan', 'choice')], { outcomes: [{ id: 'o1', rules: [rule('use', [cond('plan', 'equals', 'pro')])] }, { id: 'o2' }] }), expect: state({ outcome: 'o1' }) },
  { id: 'form-outcome-none-qualifies', form: form([f('plan', 'choice')], { outcomes: [{ id: 'o1', rules: [rule('use', [cond('plan', 'isNotEmpty')])] }] }), expect: state() },
  { id: 'form-outcome-rule-switched-off', form: form([f('plan', 'choice')], { outcomes: [{ id: 'o1', rules: [rule('use', [cond('plan', 'isNotEmpty')], { enabled: false })] }] }), expect: state({ outcome: 'o1' }) },
  { id: 'form-outcome-by-member-group', context: { member: { signedIn: true, groups: ['Partners'] } }, form: form([], { outcomes: [{ id: 'o1', rules: [rule('use', [ctx('memberGroup', 'isChecked', 'Partners')])] }, { id: 'o2' }] }), expect: state({ outcome: 'o1' }) },

  // Grid rows.
  { id: 'form-grid-row-in-a-rule', values: { r1: ['bad'] }, form: form([f('grid', null, { rows: [{ id: 'r1', kind: 'choice' }] }), f('why', 'text', { rules: [rule('show', [cond('r1', 'equals', 'bad')])] })]), expect: state() },
  { id: 'form-hidden-grid-takes-its-rows', values: { r1: ['bad'] }, form: form([f('grid', null, { rows: [{ id: 'r1', kind: 'choice' }], rules: [hiddenByCulture] }), f('why', 'text', { rules: [rule('show', [cond('r1', 'isNotEmpty')])] })]), expect: state({ hidden: ['grid', 'why'] }) },
  { id: 'form-grid-rows-of-several-answers', values: { r1: ['a', 'b'] }, form: form([f('grid', null, { rows: [{ id: 'r1', kind: 'choices' }] }), f('more', 'text', { rules: [rule('show', [cond('r1', 'countAtLeast', '2')])] })]), expect: state() },
  { id: 'form-rule-looks-at-a-later-grid', values: { r1: ['good'] }, form: form([f('why', 'text', { rules: [rule('hide', [cond('r1', 'equals', 'good')])] }), f('grid', null, { rows: [{ id: 'r1', kind: 'choice' }] })]), expect: state({ hidden: ['why'] }) },

  // Repeatable groups, row by row.
  { id: 'form-repeatable-group-without-rows', form: form([f('people', null, { fields: [f('name', 'text', { required: true })] })]), expect: state({ rows: { people: [] } }) },
  {
    id: 'form-repeatable-group-rows-decide-alone',
    values: {},
    rows: { trips: [{ kind: ['range'] }, { kind: ['single'] }] },
    form: form([f('trips', null, { fields: [f('kind', 'choice'), f('end', 'date', { rules: [rule('show', [cond('kind', 'equals', 'range')])] })] })]),
    expect: state({ rows: { trips: [row(), row({ hidden: ['end'] })] } }),
  },
  {
    id: 'form-repeatable-group-child-required-per-row',
    rows: { people: [{ name: ['Anna'] }, {}] },
    form: form([f('people', null, { fields: [f('name', 'text', { required: true }), f('note', 'text')] })]),
    expect: state({ rows: { people: [row({ required: ['name'] }), row({ required: ['name'] })] } }),
  },
  {
    id: 'form-repeatable-group-require-rule-per-row',
    rows: { people: [{ contact: ['phone'] }, { contact: ['email'] }] },
    form: form([f('people', null, { fields: [f('contact', 'choice'), f('phone', 'text', { rules: [rule('require', [cond('contact', 'equals', 'phone')])] })] })]),
    expect: state({ rows: { people: [row({ required: ['phone'] }), row()] } }),
  },
  {
    id: 'form-hidden-repeatable-group-hides-every-row',
    rows: { people: [{ name: ['Anna'] }, { name: ['Bea'] }] },
    form: form([f('people', null, { rules: [hiddenByCulture], fields: [f('name', 'text', { required: true }), f('age', 'number')] })]),
    expect: state({ hidden: ['people'], rows: { people: [row({ hidden: ['name', 'age'] }), row({ hidden: ['name', 'age'] })] } }),
  },
  {
    id: 'form-repeatable-group-hidden-by-its-group',
    rows: { people: [{ name: ['Anna'] }] },
    form: { pages: [{ id: 'p1', groups: [{ id: 'g1', rules: [hiddenByCulture], fields: [f('people', null, { fields: [f('name', 'text')] })] }] }] },
    expect: state({ hidden: ['g1', 'people'], rows: { people: [row({ hidden: ['name'] })] } }),
  },
  {
    id: 'form-outside-rule-sees-every-shown-row',
    rows: { trips: [{ kind: ['single'] }, { kind: ['range'] }] },
    form: form([f('trips', null, { fields: [f('kind', 'choice')] }), f('note', 'text', { rules: [rule('show', [cond('kind', 'equals', 'range')])] })]),
    expect: state({ rows: { trips: [row(), row()] } }),
  },
  {
    id: 'form-outside-rule-loses-hidden-row-answers',
    description: 'the detail is hidden in its only row, so from outside it has no answers',
    rows: { trips: [{ kind: ['single'], detail: ['x'] }] },
    form: form([f('trips', null, { fields: [f('kind', 'choice'), f('detail', 'text', { rules: [rule('show', [cond('kind', 'equals', 'other')])] })] }), f('flag', 'text', { rules: [rule('show', [cond('detail', 'isNotEmpty')])] })]),
    expect: state({ hidden: ['flag'], rows: { trips: [row({ hidden: ['detail'] })] } }),
  },
  {
    id: 'form-outside-rule-counts-answers-across-rows',
    rows: { people: [{ name: ['Anna'] }, { name: ['Bea'] }] },
    form: form([f('people', null, { fields: [f('name', 'text')] }), f('group', 'text', { rules: [rule('show', [cond('name', 'countAtLeast', '2')])] })]),
    expect: state({ rows: { people: [row(), row()] } }),
  },
  {
    id: 'form-outside-rule-compares-the-first-shown-row',
    description: 'the first row\'s age is hidden; the first shown answer is 30',
    rows: { people: [{ adult: [], age: ['12'] }, { adult: ['true'], age: ['30'] }] },
    form: form([f('people', null, { fields: [f('adult', 'checkbox'), f('age', 'number', { rules: [rule('show', [cond('adult', 'isChecked')])] })] }), f('senior', 'text', { rules: [rule('show', [cond('age', 'greaterThan', '18')])] })]),
    expect: state({ rows: { people: [row({ hidden: ['age'] }), row()] } }),
  },
  {
    id: 'form-child-rule-sees-an-outside-field',
    values: { mode: ['detailed'] },
    rows: { people: [{}, {}] },
    form: form([f('mode', 'choice'), f('people', null, { fields: [f('bio', 'text', { rules: [rule('show', [cond('mode', 'equals', 'detailed')])] })] })]),
    expect: state({ rows: { people: [row(), row()] } }),
  },
  {
    id: 'form-child-rule-sees-a-hidden-outside-field-as-empty',
    values: { mode: ['detailed'] },
    rows: { people: [{}] },
    form: form([f('mode', 'choice', { rules: [hiddenByCulture] }), f('people', null, { fields: [f('bio', 'text', { rules: [rule('show', [cond('mode', 'equals', 'detailed')])] })] })]),
    expect: state({ hidden: ['mode'], rows: { people: [row({ hidden: ['bio'] })] } }),
  },
  {
    id: 'form-child-rule-sees-another-repeatable-groups-rows',
    rows: { adults: [{ name: ['Anna'] }, { name: ['Bea'] }], children: [{ age: ['4'] }] },
    form: form([
      f('adults', null, { fields: [f('name', 'text')] }),
      f('children', null, { fields: [f('age', 'number'), f('guardian', 'text', { rules: [rule('show', [cond('name', 'countAtLeast', '2')])] })] }),
    ]),
    expect: state({ rows: { adults: [row(), row()], children: [row()] } }),
  },
  {
    id: 'form-validation-rule-per-row',
    rows: { trips: [{ start: ['2026-07-01'], end: ['2026-06-30'] }, { start: ['2026-07-01'], end: ['2026-07-02'] }] },
    form: form([f('trips', null, { fields: [f('start', 'date'), f('end', 'date')] })], { validationRules: [{ id: 'v1', field: 'end', enabled: true, match: 'all', conditions: [cond('end', 'lessThan', { field: 'start' })] }] }),
    expect: state({ rows: { trips: [row({ messages: { end: ['v1'] } }), row()] } }),
  },
  {
    id: 'form-validation-rule-skips-hidden-rows',
    rows: { trips: [{ kind: ['single'], end: [] }, { kind: ['range'], end: [] }] },
    form: form([f('trips', null, { fields: [f('kind', 'choice'), f('end', 'date', { rules: [rule('show', [cond('kind', 'equals', 'range')])] })] })], { validationRules: [{ id: 'v1', field: 'end', enabled: true, match: 'all', conditions: [cond('end', 'isEmpty')] }] }),
    expect: state({ rows: { trips: [row({ hidden: ['end'] }), row({ messages: { end: ['v1'] } })] } }),
  },
  {
    id: 'form-repeatable-group-row-answers-missing',
    description: 'a row that lacks a child field\'s answers has none',
    rows: { people: [{ note: ['x'] }] },
    form: form([f('people', null, { fields: [f('name', 'text'), f('note', 'text', { rules: [rule('show', [cond('name', 'isNotEmpty')])] })] })]),
    expect: state({ rows: { people: [row({ hidden: ['note'] })] } }),
  },
  {
    id: 'form-repeatable-group-grid-row-per-row',
    rows: { people: [{ r1: ['bad'] }, { r1: ['good'] }] },
    form: form([f('people', null, { fields: [f('grid', null, { rows: [{ id: 'r1', kind: 'choice' }] }), f('why', 'text', { rules: [rule('show', [cond('r1', 'equals', 'bad')])] })] })]),
    expect: state({ rows: { people: [row(), row({ hidden: ['why'] })] } }),
  },
  {
    id: 'form-repeatable-group-hidden-rows-in-document-order',
    rows: { people: [{}] },
    form: form([f('people', null, { fields: [f('c', 'text', { rules: [rule('show', [cond('b', 'isNotEmpty')])] }), f('b', 'text', { rules: [rule('show', [cond('a', 'isNotEmpty')])] }), f('a', 'text')] })]),
    expect: state({ rows: { people: [row({ hidden: ['c', 'b'] })] } }),
  },
  { id: 'form-cycle-inside-a-row', form: form([f('people', null, { fields: [f('a', 'text', { rules: [rule('show', [cond('b', 'isEmpty')])] }), f('b', 'text', { rules: [rule('show', [cond('a', 'isEmpty')])] })] })]), expect: { error: 'cycle' } },
  { id: 'form-cycle-through-the-repeatable-group', form: form([f('people', null, { rules: [rule('show', [cond('name', 'isNotEmpty')])], fields: [f('name', 'text')] })]), expect: { error: 'cycle' } },
  { id: 'form-require-on-a-child-without-kind', form: form([f('people', null, { fields: [f('intro', null, { rules: [rule('require', [])] })] })]), expect: { error: 'invalid_rule' } },
];

// ---- Edges the mutation check found untested (mutants.mjs) ----
const edges = [
  // Every folded block, the capital sharp s, and noncharacters outside the first block.
  { id: 'text-equals-cyrillic-capitals', values: { title: [u(0x414, 0x41e, 0x41c)] }, condition: cond('title', 'equals', u(0x434, 0x43e, 0x43c)), expect: true },
  { id: 'text-equals-armenian-capital', values: { title: [u(0x531)] }, condition: cond('title', 'equals', u(0x561)), expect: true },
  { id: 'text-equals-capital-sharp-s', description: 'U+1E9E folds to the sharp s, which itself stays one letter', values: { title: ['STRA' + u(0x1e9e) + 'E'] }, condition: cond('title', 'equals', 'stra' + u(0xdf) + 'e'), expect: true },
  { id: 'text-equals-greek-extended-capital', values: { title: [u(0x1f08)] }, condition: cond('title', 'equals', u(0x1f00)), expect: true },
  { id: 'text-equals-full-width-capitals', values: { title: [u(0xff21, 0xff22)] }, condition: cond('title', 'equals', u(0xff41, 0xff42)), expect: true },
  { id: 'text-astral-noncharacter-becomes-replacement', description: 'U+1FFFE, the end of plane 1, is cleaned to U+FFFD', values: { title: ['A' + u(0x1fffe)] }, condition: cond('title', 'equals', 'a' + u(0xfffd)), expect: true },
  { id: 'text-noncharacter-block-end-becomes-replacement', description: 'U+FDEF ends the block of noncharacters', values: { title: ['A' + u(0xfdef)] }, condition: cond('title', 'equals', 'a' + u(0xfffd)), expect: true },

  // Counts, numbers, dates, times and instants at their edges.
  { id: 'count-seven-digits-refused', values: { topics: ['red'] }, condition: cond('topics', 'countAtMost', '1000000'), expect: { error: 'invalid_operand' } },
  { id: 'count-six-digits-allowed', values: { topics: ['red'] }, condition: cond('topics', 'countAtMost', '999999'), expect: true },
  { id: 'calc-sum-drops-trailing-zeros', calc: { op: 'add', args: ['1.25', '1.75'] }, expect: '3' },
  { id: 'calc-quotient-drops-trailing-zeros', calc: { op: 'div', args: ['1', '4'] }, expect: '0.25' },
  { id: 'calc-sixty-one-digits-out-of-range', description: 'the sum has one digit more than a result may', calc: { op: 'add', args: ['9'.repeat(60), '1'] }, expect: { error: 'out_of_range' } },
  { id: 'date-november-31-invalid', values: { arrival: ['2026-11-31'] }, condition: cond('arrival', 'greaterThan', '2026-01-01'), expect: false },
  { id: 'date-month-13-invalid', values: { arrival: ['2026-13-01'] }, condition: cond('arrival', 'greaterThan', '2026-01-01'), expect: false },
  { id: 'date-day-zero-invalid', values: { arrival: ['2026-06-00'] }, condition: cond('arrival', 'lessThan', '2026-12-31'), expect: false },
  { id: 'time-minute-60-invalid', values: { start: ['14:60'] }, condition: cond('start', 'greaterThan', '00:00'), expect: false },
  { id: 'date-time-with-a-space-invalid', values: { meeting: ['2026-06-20 14:30'] }, condition: cond('meeting', 'greaterThan', '2026-01-01T00:00'), expect: false },
  { id: 'now-offset-of-24-hours-refused', context: { now: '2026-06-15T10:00:00+24:00' }, condition: ctx('today', 'isNotEmpty'), expect: { error: 'invalid_context' } },

  // Patterns.
  { id: 'pattern-reversed-range-refused', values: { title: ['b'] }, condition: cond('title', 'matches', '[z-a]'), expect: { error: 'unsafe_pattern' } },
  { id: 'pattern-bracket-in-a-class-refused', values: { title: ['a'] }, condition: cond('title', 'matches', '[a[]'), expect: { error: 'unsafe_pattern' } },
  { id: 'pattern-word-takes-the-underscore', values: { title: ['a_b'] }, condition: cond('title', 'matches', p('~w+')), expect: true },
  { id: 'pattern-dot-excludes-the-line-separator', values: { title: ['a' + u(0x2028) + 'c'] }, condition: cond('title', 'matches', 'a.c'), expect: false },
  { id: 'pattern-starred-instructions-at-the-limit-allowed', description: 'five nested stars are 11 instructions: 909 of them and a letter are 10,000', values: { title: ['b'] }, condition: cond('title', 'matches', '(((((a*)*)*)*)*){909}b'), expect: true },
  { id: 'pattern-starred-instructions-over-the-limit-refused', description: 'and a second letter is one instruction too many', values: { title: ['bc'] }, condition: cond('title', 'matches', '(((((a*)*)*)*)*){909}bc'), expect: { error: 'unsafe_pattern' } },

  // Forms.
  { id: 'form-answers-are-cleaned', description: 'an answer of spaces is no answer, in a form as in a condition', values: { a: ['   '] }, form: form([f('a', 'text'), f('b', 'text', { rules: [rule('show', [cond('a', 'isNotEmpty')])] })]), expect: state({ hidden: ['b'] }) },
  {
    id: 'form-hidden-child-answers-gone-in-its-row',
    rows: { trips: [{ kind: ['single'], detail: ['x'] }] },
    form: form([f('trips', null, { fields: [f('kind', 'choice'), f('detail', 'text', { rules: [rule('show', [cond('kind', 'equals', 'other')])] }), f('flag', 'text', { rules: [rule('show', [cond('detail', 'isNotEmpty')])] })] })]),
    expect: state({ rows: { trips: [row({ hidden: ['detail', 'flag'] })] } }),
  },
  { id: 'form-validation-rule-matches-but-its-field-is-hidden', values: { email: ['x'] }, form: form([f('email', 'text', { rules: [hiddenByCulture] })], { validationRules: [{ id: 'v1', field: 'email', enabled: true, match: 'all', conditions: [ctx('culture', 'startsWith', 'de')] }] }), expect: state({ hidden: ['email'] }) },
  { id: 'form-validation-messages-on-two-fields', description: 'the messages by field, in any order', values: { a: ['1'], b: ['2'] }, form: form([f('a', 'number'), f('b', 'number')], { validationRules: [{ id: 'v1', field: 'b', enabled: true, match: 'all', conditions: [cond('b', 'isNotEmpty')] }, { id: 'v2', field: 'a', enabled: true, match: 'all', conditions: [cond('a', 'isNotEmpty')] }] }), expect: state({ messages: { a: ['v2'], b: ['v1'] } }) },
];

const cases = [...matrix, ...conditions, ...rules, ...forms, ...edges];

export default { suite: 'rules', defaults, fields, cases };
