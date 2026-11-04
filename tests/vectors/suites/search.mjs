// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The rule search (ROADMAP 9.5c, 9.5d; ADR-0178, ADR-0179): what rules do for the answers a visitor can give, which the
// rules overview and the form check ask of both runtimes. A `conflict` case gives a require rule and the rules that may
// hide its field (its own, its group's, its page's) and expects null, or the index of the hiding rule that hides it and
// the answers that matter; with a `form`, the fields that form hides for the answers tried count as unanswered, as the
// form engine decides (`formSight`). A `shows` case gives an element's show and hide rules and the fields not yet
// answered (and those whose answers are `several`, a repeatable group's children), and expects whether it shows for some
// answers (`shows`), for none a visitor can give (`never`), or can't be told (`unknown`). A case may add `fields` and
// choices' `options` of its own. Rules are written in full, as the engine reads them. Every expectation is written out
// here, never computed by the code under test.

const cond = (field, op, value) => (value === undefined ? { field, op } : { field, op, value });
const ctx = (context, op, value) => (value === undefined ? { context, op } : { context, op, value });
const rule = (effect, conditions, more = {}) => ({ effect, enabled: true, match: 'all', conditions, ...more });
const any = (...conditions) => ({ match: 'any', conditions });
const conflict = (requiring, hidings, form) => (form === undefined ? { requiring, hidings } : { requiring, hidings, form });
const answer = (subject, values, noneOf) => (noneOf === undefined ? { subject, values } : { subject, values, noneOf });

const everAnswer = any(cond('agree', 'isChecked'), cond('agree', 'isNotChecked'));

// A form of one page and group, as the engine reads it.
const field = (id, kind, rules = [], more = {}) => ({ id, kind, rules, ...more });
const formOf = (...fields) => ({ pages: [{ id: 'p1', groups: [{ id: 'g1', fields }] }] });

// The name is hidden for more than four guests and required for some topics; the guests are asked only about the press.
const manyGuests = rule('hide', [cond('guests', 'greaterThan', '4')]);
const forDelivery = rule('require', [cond('topic', 'equals', 'delivery')]);
const forDeliveryOrPress = rule('require', [cond('topic', 'isOneOf', ['delivery', 'press'])]);
const pressOnly = rule('show', [cond('topic', 'equals', 'press')]);
const guestsForPress = (requiring, shown = pressOnly) =>
  formOf(field('topic', 'choice'), field('guests', 'number', [shown]), field('name', 'text', [manyGuests, requiring]));
const notDelivery = rule('hide', [cond('topic', 'equals', 'delivery')]);
const nearZero = rule('hide', [cond('guests', 'lessThan', '1.00001')]);
const thirteen = Array.from({ length: 13 }, (_, at) => `box${at + 1}`);

export default {
  suite: 'search',
  defaults: {
    search: {
      options: { topic: ['delivery', 'shipping', 'press'], topics: ['delivery', 'shipping', 'press'] },
      cultures: ['en-US', 'de-DE'],
      today: '2026-11-04',
    },
  },
  fields: {
    topic: 'choice',
    topics: 'choices',
    guests: 'number',
    kids: 'number',
    arrival: 'date',
    agree: 'checkbox',
    name: 'text',
    nick: 'text',
    start: 'time',
    uploads: 'files',
  },
  cases: [
    // Conflicts.
    {
      id: 'conflict-an-option',
      description: 'hidden when the topic is press, required when it is shipping or press',
      conflict: conflict(rule('require', [cond('topic', 'isOneOf', ['shipping', 'press'])]), [rule('hide', [cond('topic', 'equals', 'press')])]),
      expect: { hiding: 0, answers: [answer('field:topic', ['press'])] },
    },
    {
      id: 'conflict-a-show-rule-not-holding',
      description: 'shown for shipping, required once answered: delivery hides it',
      conflict: conflict(rule('require', [cond('topic', 'isNotEmpty')]), [rule('show', [cond('topic', 'equals', 'shipping')])]),
      expect: { hiding: 0, answers: [answer('field:topic', ['delivery'])] },
    },
    {
      id: 'conflict-none-for-different-answers',
      conflict: conflict(rule('require', [cond('topic', 'equals', 'shipping')]), [rule('hide', [cond('topic', 'equals', 'press')])]),
      expect: null,
    },
    {
      id: 'conflict-none-for-a-rule-that-is-off',
      conflict: conflict(rule('require', [cond('topic', 'equals', 'press')]), [rule('hide', [cond('topic', 'equals', 'press')], { enabled: false })]),
      expect: null,
    },
    {
      id: 'conflict-none-for-a-show-rule-the-engine-refuses',
      conflict: conflict(rule('require', [cond('topic', 'isNotEmpty')]), [rule('show', [cond('topic', 'nope', 'press')])]),
      expect: null,
    },
    {
      id: 'conflict-one-more-than-a-number',
      conflict: conflict(rule('require', [cond('guests', 'greaterThan', '2')]), [rule('hide', [cond('guests', 'greaterThan', '4')])]),
      expect: { hiding: 0, answers: [answer('field:guests', ['5'])] },
    },
    {
      id: 'conflict-the-day-between',
      conflict: conflict(rule('require', [cond('arrival', 'greaterThan', '2026-11-29')]), [rule('hide', [cond('arrival', 'lessThan', '2026-12-01')])]),
      expect: { hiding: 0, answers: [answer('field:arrival', ['2026-11-30'])] },
    },
    {
      id: 'conflict-a-minute-before',
      conflict: conflict(rule('require', [cond('start', 'isNotEmpty')]), [rule('hide', [cond('start', 'lessThan', '09:00')])]),
      expect: { hiding: 0, answers: [answer('field:start', ['08:59'])] },
    },
    {
      id: 'conflict-days-from-today',
      description: 'today is 2026-11-04, so a week from today is 2026-11-11',
      conflict: conflict(rule('require', [cond('arrival', 'isNotEmpty')]), [rule('hide', [cond('arrival', 'lessThan', { today: '+7d' })])]),
      expect: { hiding: 0, answers: [answer('field:arrival', ['2026-11-10'])] },
    },
    {
      id: 'conflict-the-visit',
      conflict: conflict(rule('require', [ctx('memberSignedIn', 'isChecked')]), [rule('hide', [ctx('culture', 'equals', 'de-DE')])]),
      expect: { hiding: 0, answers: [answer('context:culture', ['de-DE']), answer('context:memberSignedIn', ['true'])] },
    },
    {
      id: 'conflict-member-groups-signed-in',
      conflict: conflict(rule('require', [ctx('memberSignedIn', 'isChecked')]), [rule('hide', [ctx('memberGroup', 'isChecked', 'staff')])]),
      expect: { hiding: 0, answers: [answer('context:memberGroup', ['staff']), answer('context:memberSignedIn', ['true'])] },
    },
    {
      id: 'conflict-none-for-member-groups-without-signing-in',
      conflict: conflict(rule('require', [ctx('memberSignedIn', 'isNotChecked')]), [rule('hide', [ctx('memberGroup', 'isChecked', 'staff')])]),
      expect: null,
    },
    {
      id: 'conflict-two-answers-compared',
      description: 'the guests, named only by their comparison with the kids, are tried with the kids\' values',
      conflict: conflict(rule('require', [cond('kids', 'equals', '2')]), [rule('hide', [cond('guests', 'greaterThan', { field: 'kids' })])]),
      expect: { hiding: 0, answers: [answer('field:guests', ['3']), answer('field:kids', ['2'])] },
    },
    {
      id: 'conflict-a-text-the-rules-do-not-name',
      conflict: conflict(rule('require', [cond('name', 'isNotEmpty')]), [rule('hide', [cond('name', 'notEquals', 'Ann')])]),
      expect: { hiding: 0, answers: [answer('field:name', [], ['Ann'])] },
    },
    {
      id: 'conflict-none-for-a-text-another-looks-inside',
      description: '"b" is none of "abc", yet "abc" contains it',
      conflict: conflict(rule('require', [cond('name', 'isNotEmpty')]), [
        rule('hide', [cond('name', 'notContains', { field: 'nick' }), cond('nick', 'isNotEmpty'), cond('name', 'isOneOf', ['abc'])]),
      ]),
      expect: null,
    },
    {
      id: 'conflict-answers-free-together',
      description: 'the topic is free while the name is empty, but not together with it',
      conflict: conflict(rule('require', [everAnswer]), [rule('hide', [any(cond('topic', 'isEmpty'), cond('name', 'isEmpty'))])]),
      expect: { hiding: 0, answers: [answer('field:name', [])] },
    },
    {
      id: 'conflict-files-by-count',
      conflict: conflict(rule('require', [cond('uploads', 'isNotEmpty')]), [rule('hide', [cond('uploads', 'countAtLeast', '3')])]),
      expect: { hiding: 0, answers: [answer('field:uploads', ['file1', 'file2', 'file3'])] },
    },
    {
      id: 'conflict-the-groups-rule',
      conflict: conflict(rule('require', [cond('topic', 'equals', 'press')]), [
        rule('show', [cond('topic', 'isNotEmpty')]),
        rule('hide', [cond('topic', 'equals', 'press')]),
      ]),
      expect: { hiding: 1, answers: [answer('field:topic', ['press'])] },
    },
    {
      id: 'conflict-the-pages-rule',
      description: 'the page shows only to members, so a visitor not signed in has the field hidden while it is required',
      conflict: conflict(rule('require', [cond('topic', 'equals', 'press')]), [
        rule('show', [cond('topic', 'isNotEmpty')]),
        rule('show', [ctx('memberSignedIn', 'isChecked')]),
      ]),
      expect: { hiding: 1, answers: [answer('field:topic', ['press']), answer('context:memberSignedIn', [])] },
    },
    {
      id: 'conflict-without-the-form-every-field-shows',
      description: 'the rules alone conflict for five guests and delivery',
      conflict: conflict(forDelivery, [manyGuests]),
      expect: { hiding: 0, answers: [answer('field:guests', ['5']), answer('field:topic', ['delivery'])] },
    },
    {
      id: 'conflict-none-for-a-field-the-form-hides',
      description: 'for delivery the form hides the guests, whose answer then counts as none, so the name never hides',
      conflict: conflict(forDelivery, [manyGuests], guestsForPress(forDelivery)),
      expect: null,
    },
    {
      id: 'conflict-with-the-form-a-field-it-shows',
      description: 'for the press the form asks the guests, and five hide the name it requires',
      conflict: conflict(forDeliveryOrPress, [manyGuests], guestsForPress(forDeliveryOrPress)),
      expect: { hiding: 0, answers: [answer('field:guests', ['5']), answer('field:topic', ['press'])] },
    },
    {
      id: 'conflict-none-where-the-engine-refuses-the-form',
      description: 'a form the engine refuses can\'t tell what it hides, so no conflict is said',
      conflict: conflict(forDelivery, [manyGuests], guestsForPress(forDelivery, rule('show', [cond('topic', 'nope', 'press')]))),
      expect: null,
    },
    {
      id: 'conflict-none-for-a-repeatable-groups-child-it-hides',
      description: 'the party is hidden for delivery, and with it the guests of its row',
      conflict: conflict(forDelivery, [manyGuests], formOf(
        field('topic', 'choice'),
        field('party', null, [notDelivery], { fields: [field('guests', 'number')] }),
        field('name', 'text', [manyGuests, forDelivery]),
      )),
      expect: null,
    },
    {
      id: 'conflict-none-for-a-grid-row-hidden-with-its-grid',
      fields: { meals: 'choice' },
      conflict: conflict(forDelivery, [rule('hide', [cond('meals', 'equals', 'yes')])], formOf(
        field('topic', 'choice'),
        field('plan', null, [notDelivery], { rows: [{ id: 'meals', kind: 'choice' }] }),
        field('name', 'text', [rule('hide', [cond('meals', 'equals', 'yes')]), forDelivery]),
      )),
      expect: null,
    },
    {
      id: 'conflict-a-neighbour-near-zero-is-not-tried',
      description: 'one less than 1.00001 each runtime writes otherwise, so 1 is found, between 0 and 1.00001',
      conflict: conflict(rule('require', [cond('guests', 'greaterThan', '0')]), [nearZero]),
      expect: { hiding: 0, answers: [answer('field:guests', ['1'])] },
    },
    {
      id: 'conflict-a-neighbour-near-zero-is-tried-by-neither',
      description: 'C# would write one less than 1.00001 with an exponent, which no number reads as and so is not 1.00001',
      conflict: conflict(rule('require', [cond('guests', 'isNotEmpty')]), [rule('hide', [cond('guests', 'notEquals', '1.00001')])]),
      expect: { hiding: 0, answers: [answer('field:guests', ['2.00001'])] },
    },

    // Whether an element ever shows.
    { id: 'shows-without-rules', shows: { rules: [], unanswered: [] }, expect: 'shows' },
    { id: 'shows-for-an-option', shows: { rules: [rule('show', [cond('topic', 'equals', 'press')])], unanswered: [] }, expect: 'shows' },
    { id: 'shows-with-a-rule-off', shows: { rules: [rule('show', [cond('topic', 'equals', 'nowhere')], { enabled: false })], unanswered: [] }, expect: 'shows' },
    { id: 'shows-once-a-field-is-answered', shows: { rules: [rule('show', [cond('name', 'isNotEmpty')])], unanswered: [] }, expect: 'shows' },
    { id: 'never-for-an-option-the-choice-lacks', shows: { rules: [rule('show', [cond('topic', 'equals', 'nowhere')])], unanswered: [] }, expect: 'never' },
    { id: 'never-before-a-later-answer', description: 'the name is on a later page, so going forward it is not yet answered', shows: { rules: [rule('show', [cond('name', 'isNotEmpty')])], unanswered: ['name'] }, expect: 'never' },
    { id: 'never-when-hidden-whatever-the-answer', shows: { rules: [rule('hide', [everAnswer])], unanswered: [] }, expect: 'never' },
    { id: 'never-in-a-language-the-site-lacks', shows: { rules: [rule('show', [ctx('culture', 'equals', 'fr-FR')])], unanswered: [] }, expect: 'never' },
    {
      id: 'unknown-between-two-numbers',
      description: 'between 4 and 5 lies 4.5, which no neighbour of either is',
      shows: { rules: [rule('show', [cond('guests', 'greaterThan', '4'), cond('guests', 'lessThan', '5')])], unanswered: [] },
      expect: 'unknown',
    },
    { id: 'unknown-for-a-text-looked-inside', shows: { rules: [rule('show', [cond('name', 'notContains', 'a'), cond('name', 'isNotEmpty')])], unanswered: [] }, expect: 'unknown' },
    { id: 'unknown-for-four-ticks', shows: { rules: [rule('show', [cond('topics', 'countAtLeast', '4')])], unanswered: [] }, expect: 'unknown' },
    { id: 'unknown-for-days-from-today', shows: { rules: [rule('show', [cond('arrival', 'greaterThan', { today: '+7d' })])], unanswered: ['arrival'] }, expect: 'unknown' },
    { id: 'unknown-for-a-rule-the-engine-refuses', shows: { rules: [rule('show', [cond('topic', 'nope', 'press')])], unanswered: [] }, expect: 'unknown' },
    {
      id: 'never-for-two-options-of-one-choice',
      shows: { rules: [rule('show', [cond('topic', 'equals', 'delivery'), cond('topic', 'equals', 'shipping')])], unanswered: [] },
      expect: 'never',
    },
    {
      id: 'unknown-for-a-repeatable-groups-child',
      description: 'read from outside its group, a child has the answers of every row, so two rows answer both',
      shows: { rules: [rule('show', [cond('topic', 'equals', 'delivery'), cond('topic', 'equals', 'shipping')])], unanswered: [], several: ['topic'] },
      expect: 'unknown',
    },
    {
      id: 'never-for-two-of-one-option',
      description: 'one choice of several ticks an option once, so "at least two" of none but delivery never holds',
      shows: { rules: [rule('show', [cond('topics', 'countAtLeast', '2'), cond('topics', 'isNoneOf', ['shipping', 'press'])])], unanswered: [] },
      expect: 'never',
    },
    {
      id: 'unknown-for-two-of-one-option-in-two-rows',
      description: 'two rows of a repeatable group may tick delivery each',
      shows: { rules: [rule('show', [cond('topics', 'countAtLeast', '2'), cond('topics', 'isNoneOf', ['shipping', 'press'])])], unanswered: [], several: ['topics'] },
      expect: 'unknown',
    },
    {
      id: 'unknown-for-two-names-in-two-rows',
      shows: { rules: [rule('show', [cond('name', 'equals', 'Ann'), cond('name', 'equals', 'Bob')])], unanswered: [], several: ['name'] },
      expect: 'unknown',
    },
    {
      id: 'never-for-two-days-at-once',
      shows: { rules: [rule('show', [cond('arrival', 'equals', '2026-12-01'), cond('arrival', 'equals', '2026-12-02')])], unanswered: [] },
      expect: 'never',
    },
    {
      id: 'unknown-for-two-days-in-two-rows',
      shows: { rules: [rule('show', [cond('arrival', 'equals', '2026-12-01'), cond('arrival', 'equals', '2026-12-02')])], unanswered: [], several: ['arrival'] },
      expect: 'unknown',
    },
    { id: 'unknown-for-three-ticks', shows: { rules: [rule('show', [cond('topics', 'countAtLeast', '3')])], unanswered: [] }, expect: 'unknown' },
    {
      id: 'unknown-for-a-query-value',
      description: 'a link may give a query value twice',
      shows: { rules: [rule('show', [ctx('query.ref', 'equals', 'a'), ctx('query.ref', 'equals', 'b')])], unanswered: [] },
      expect: 'unknown',
    },
    {
      id: 'unknown-for-a-day-compared-with-another',
      description: 'a day after another is no neighbour of a day the rules name',
      fields: { leave: 'date' },
      shows: { rules: [rule('show', [cond('arrival', 'greaterThan', { field: 'leave' })])], unanswered: [] },
      expect: 'unknown',
    },
    {
      id: 'unknown-for-at-most-two-ticked',
      description: 'hidden for two ticked or fewer, so only three ticked show it, which is not tried',
      shows: { rules: [rule('hide', [cond('topics', 'countAtMost', '2')])], unanswered: [] },
      expect: 'unknown',
    },
    { id: 'shows-for-at-most-one-ticked', shows: { rules: [rule('hide', [cond('topics', 'countAtMost', '1')])], unanswered: [] }, expect: 'shows' },
    {
      id: 'never-before-today',
      description: 'today is 2026-11-04, and no visit is on an earlier day',
      shows: { rules: [rule('show', [ctx('today', 'lessThan', '2026-11-03')])], unanswered: [] },
      expect: 'never',
    },
    {
      id: 'never-for-a-number-only-tested-for-being-equal',
      shows: { rules: [rule('show', [cond('guests', 'equals', '5'), cond('guests', 'notEquals', '5')])], unanswered: [] },
      expect: 'never',
    },
    {
      id: 'unknown-for-a-number-too-large-to-step-from',
      description: 'no other number is tried, so the answers tried don\'t stand for all',
      shows: { rules: [rule('show', [cond('guests', 'notEquals', '1000000000000000'), cond('guests', 'isNotEmpty')])], unanswered: [] },
      expect: 'unknown',
    },
    {
      id: 'unknown-between-two-minutes',
      description: 'a time may hold seconds, so 10:00:30 lies between',
      shows: { rules: [rule('show', [cond('start', 'greaterThan', '10:00'), cond('start', 'lessThan', '10:01')])], unanswered: [] },
      expect: 'unknown',
    },
    {
      id: 'never-for-files-and-none',
      shows: { rules: [rule('show', [cond('uploads', 'countAtMost', '0'), cond('uploads', 'isNotEmpty')])], unanswered: [] },
      expect: 'never',
    },
    {
      id: 'unknown-for-more-sets-than-tried',
      description: '13 ticks make 8,192 sets of answers, more than the 4,096 tried',
      fields: Object.fromEntries(thirteen.map((box) => [box, 'checkbox'])),
      shows: { rules: [rule('show', [...thirteen.map((box) => cond(box, 'isChecked')), cond('agree', 'isChecked'), cond('agree', 'isNotChecked')])], unanswered: [] },
      expect: 'unknown',
    },
    {
      id: 'unknown-for-more-options-than-tried-together',
      description: 'eight options make 37 answers of one or two ticked, more than the 32 tried',
      fields: { many: 'choices' },
      options: { many: ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'] },
      shows: { rules: [rule('show', [cond('many', 'isChecked', 'a'), cond('many', 'isNotChecked', 'a')])], unanswered: [] },
      expect: 'unknown',
    },
    {
      id: 'unknown-for-member-groups-tested-for-being-empty',
      description: 'a member may be in a group no rule names',
      shows: { rules: [rule('show', [ctx('memberGroup', 'isNotEmpty'), ctx('memberGroup', 'isNoneOf', ['staff'])])], unanswered: [] },
      expect: 'unknown',
    },
  ],
};
