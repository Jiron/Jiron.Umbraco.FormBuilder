// The rule search (ROADMAP 9.5c, 9.5d; ADR-0178, ADR-0179), beside the shared vectors of tests/vectors/search.json: a
// conflict, a require rule and a rule that hides its field holding for the same answers, found by the engine with
// answers a visitor can give and never said where none make both hold; whether an element ever shows, said to never
// show only where the answers tried stand for all; and what a form hides for a set of answers, as its engine decides.
import { describe, expect, it } from 'vitest';
import { FORMULAS } from '../src/calc.ts';
import type { LogicField, LogicForm } from '../src/form.ts';
import type { Context, Rule } from '../src/rules.ts';
import { conflictOf as search, formSight, showsAtAll, type SearchRule, type SearchScope } from '../src/search.ts';

interface KeyedRule extends SearchRule {
  readonly key: string;
}

/** The conflict of a field's own rules: its require rule against its show and hide rules. */
const conflictOf = (rules: readonly KeyedRule[], scope: SearchScope) => {
  const requiring = rules.find((one) => one.effect === 'require');
  return requiring ? search(requiring, rules, scope) : null;
};

const SCOPE: SearchScope = {
  fields: {
    topic: 'choice',
    topics: 'choices',
    guests: 'number',
    arrival: 'date',
    agree: 'checkbox',
    name: 'text',
    start: 'time',
    uploads: 'files',
  },
  options: (key) => (key === 'topic' || key === 'topics' ? ['delivery', 'shipping', 'press'] : []),
  cultures: ['en-US', 'de-DE'],
  today: '2026-11-04',
};

const rule = (effect: string, conditions: readonly unknown[], extra: Partial<KeyedRule> = {}): KeyedRule => ({
  key: effect,
  effect,
  conditions,
  ...extra,
});

describe("a conflict of a field's rules", () => {
  it('is the answer for which one rule hides the field and the other requires it', () => {
    const hide = rule('hide', [{ field: 'topic', op: 'equals', value: 'press' }]);
    const require = rule('require', [{ field: 'topic', op: 'isOneOf', value: ['shipping', 'press'] }]);
    const conflict = conflictOf([hide, require], SCOPE);
    expect(conflict).toEqual({
      hiding: hide,
      requiring: require,
      answers: [{ subject: 'field:topic', values: ['press'] }],
    });
  });

  it('is an answer for which a show rule does not hold while the require rule does', () => {
    const show = rule('show', [{ field: 'topic', op: 'equals', value: 'shipping' }]);
    const require = rule('require', [{ field: 'topic', op: 'isNotEmpty' }]);
    expect(conflictOf([show, require], SCOPE)?.answers).toEqual([{ subject: 'field:topic', values: ['delivery'] }]);
  });

  it('is none where no answer makes both hold, where a rule is off or empty, or where one of them is missing', () => {
    const hide = rule('hide', [{ field: 'topic', op: 'equals', value: 'press' }]);
    const require = rule('require', [{ field: 'topic', op: 'equals', value: 'shipping' }]);
    expect(conflictOf([hide, require], SCOPE)).toBeNull();
    const shown = rule('show', [{ field: 'topic', op: 'equals', value: 'shipping' }]);
    expect(conflictOf([shown, require], SCOPE)).toBeNull();
    const both = rule('require', [{ field: 'topic', op: 'equals', value: 'press' }]);
    expect(conflictOf([{ ...hide, enabled: false }, both], SCOPE)).toBeNull();
    expect(conflictOf([hide, { ...both, enabled: false }], SCOPE)).toBeNull();
    // A show rule that is off leaves the field shown, though it never holds.
    expect(conflictOf([{ ...shown, enabled: false }, both], SCOPE)).toBeNull();
    expect(conflictOf([rule('hide', []), both], SCOPE)).toBeNull();
    expect(conflictOf([both], SCOPE)).toBeNull();
    expect(conflictOf([hide], SCOPE)).toBeNull();
  });

  it('tries one more and one less than a number, the days around a date, a minute around a time', () => {
    const above = conflictOf(
      [
        rule('hide', [{ field: 'guests', op: 'greaterThan', value: '4' }]),
        rule('require', [{ field: 'guests', op: 'greaterThan', value: '2' }]),
      ],
      SCOPE,
    );
    expect(above?.answers).toEqual([{ subject: 'field:guests', values: ['5'] }]);
    const late = conflictOf(
      [
        rule('hide', [{ field: 'arrival', op: 'lessThan', value: '2026-12-01' }]),
        rule('require', [{ field: 'arrival', op: 'greaterThan', value: '2026-11-29' }]),
      ],
      SCOPE,
    );
    expect(late?.answers).toEqual([{ subject: 'field:arrival', values: ['2026-11-30'] }]);
    const early = conflictOf(
      [
        rule('hide', [{ field: 'start', op: 'lessThan', value: '09:00' }]),
        rule('require', [{ field: 'start', op: 'isNotEmpty' }]),
      ],
      SCOPE,
    );
    expect(early?.answers).toEqual([{ subject: 'field:start', values: ['08:59'] }]);
  });

  it('counts days from today as the rule does, and tries today itself', () => {
    const soon = conflictOf(
      [
        rule('hide', [{ field: 'arrival', op: 'lessThan', value: { today: '+7d' } }]),
        rule('require', [{ field: 'arrival', op: 'isNotEmpty' }]),
      ],
      SCOPE,
    );
    expect(soon?.answers).toEqual([{ subject: 'field:arrival', values: ['2026-11-10'] }]);
  });

  it('tries ticks, several choices, files, the visit, and two answers compared', () => {
    const ticked = conflictOf(
      [rule('hide', [{ field: 'agree', op: 'isChecked' }]), rule('require', [{ field: 'agree', op: 'isChecked' }])],
      SCOPE,
    );
    expect(ticked?.answers).toEqual([{ subject: 'field:agree', values: ['true'] }]);
    const several = conflictOf(
      [
        rule('hide', [{ field: 'topics', op: 'isChecked', value: 'press' }]),
        rule('require', [{ field: 'topics', op: 'countAtLeast', value: '2' }]),
      ],
      SCOPE,
    );
    expect(several?.answers[0]?.values).toContain('press');
    expect(several?.answers[0]?.values.length).toBeGreaterThanOrEqual(2);
    const files = conflictOf(
      [
        rule('hide', [{ field: 'uploads', op: 'countAtLeast', value: '3' }]),
        rule('require', [{ field: 'uploads', op: 'isNotEmpty' }]),
      ],
      SCOPE,
    );
    expect(files?.answers[0]?.values).toHaveLength(3);
    const german = conflictOf(
      [
        rule('hide', [{ context: 'culture', op: 'equals', value: 'de-DE' }]),
        rule('require', [{ context: 'memberSignedIn', op: 'isChecked' }]),
      ],
      SCOPE,
    );
    expect(german?.answers).toEqual([
      { subject: 'context:culture', values: ['de-DE'] },
      { subject: 'context:memberSignedIn', values: ['true'] },
    ]);
    const compared = conflictOf(
      [
        rule('hide', [{ field: 'guests', op: 'greaterThan', value: { field: 'name' } }]),
        rule('require', [{ field: 'guests', op: 'equals', value: '3' }]),
      ],
      { ...SCOPE, fields: { ...SCOPE.fields, name: 'number' } },
    );
    expect(compared?.answers).toEqual([
      { subject: 'field:guests', values: ['3'] },
      { subject: 'field:name', values: ['2'] },
    ]);
  });

  it("tries each side of a comparison with the other's values", () => {
    // The guests are named by no value of their own, only compared with the kids, who are required at two.
    const more = conflictOf(
      [
        rule('hide', [{ field: 'guests', op: 'greaterThan', value: { field: 'kids' } }]),
        rule('require', [{ field: 'kids', op: 'equals', value: '2' }]),
      ],
      { ...SCOPE, fields: { ...SCOPE.fields, kids: 'number' } },
    );
    expect(more?.answers).toEqual([
      { subject: 'field:guests', values: ['3'] },
      { subject: 'field:kids', values: ['2'] },
    ]);
  });

  it('leaves out an answer the conflict holds for whatever it is, and has no answers where it holds for all', () => {
    const conflict = conflictOf(
      [
        rule('hide', [{ field: 'topic', op: 'equals', value: 'press' }]),
        rule('require', [
          {
            match: 'any',
            conditions: [
              { field: 'topic', op: 'equals', value: 'press' },
              { field: 'name', op: 'isEmpty' },
            ],
          },
        ]),
      ],
      SCOPE,
    );
    expect(conflict?.answers).toEqual([{ subject: 'field:topic', values: ['press'] }]);
    const always = conflictOf(
      [
        rule('hide', [
          {
            match: 'any',
            conditions: [
              { field: 'name', op: 'isEmpty' },
              { field: 'name', op: 'isNotEmpty' },
            ],
          },
        ]),
        rule('require', [
          {
            match: 'any',
            conditions: [
              { field: 'name', op: 'isEmpty' },
              { field: 'name', op: 'isNotEmpty' },
            ],
          },
        ]),
      ],
      SCOPE,
    );
    expect(always?.answers).toEqual([]);
  });

  it('says an answer only where its fellows, left out together, conflict whatever they are', () => {
    // Each of the two answers alone keeps the field hidden, so neither matters while the other stays; together they do.
    const conflict = conflictOf(
      [
        rule('hide', [
          {
            match: 'any',
            conditions: [
              { field: 'topic', op: 'isEmpty' },
              { field: 'name', op: 'isEmpty' },
            ],
          },
        ]),
        rule('require', [
          {
            match: 'any',
            conditions: [
              { field: 'agree', op: 'isChecked' },
              { field: 'agree', op: 'isNotChecked' },
            ],
          },
        ]),
      ],
      SCOPE,
    );
    expect(conflict?.answers).toEqual([{ subject: 'field:name', values: [] }]);
  });

  it('says an answer rather than call it free where trying it with the others would take too long', () => {
    // Five texts, each tested against nine values: leaving out four together would take 11^4 tries.
    const texts = ['t1', 't2', 't3', 't4', 't5'];
    const nine = (name: string) => Array.from({ length: 9 }, (_, i) => `${name}-${i}`);
    const conflict = conflictOf(
      [
        rule('hide', [{ match: 'any', conditions: texts.map((t) => ({ field: t, op: 'isOneOf', value: nine(t) })) }]),
        rule('require', [
          {
            match: 'any',
            conditions: [
              { field: 'agree', op: 'isChecked' },
              { field: 'agree', op: 'isNotChecked' },
            ],
          },
        ]),
      ],
      { ...SCOPE, fields: { ...SCOPE.fields, ...Object.fromEntries(texts.map((t) => [t, 'text' as const])) } },
    );
    // t2 to t4 are free together; t5 would be too, but is said, as trying it with them is past the limit.
    expect(conflict?.answers).toEqual([
      { subject: 'field:t1', values: ['t1-0'] },
      { subject: 'field:t5', values: [] },
    ]);
  });

  it('tries only answers a visitor can give', () => {
    const require = rule('require', [{ field: 'topic', op: 'isNotEmpty' }]);
    // No option of the topic is none of its options, and a language is one of the site's.
    const none = rule('hide', [{ field: 'topic', op: 'isNoneOf', value: ['delivery', 'shipping', 'press'] }]);
    expect(conflictOf([none, require], SCOPE)).toBeNull();
    const ticked = rule('require', [{ field: 'topics', op: 'isNotEmpty' }]);
    const unticked = rule('hide', [{ field: 'topics', op: 'isNoneOf', value: ['delivery', 'shipping', 'press'] }]);
    expect(conflictOf([unticked, ticked], SCOPE)).toBeNull();
    // A language found is said as itself, never as none.
    const english = rule('hide', [{ context: 'culture', op: 'equals', value: 'en-US' }]);
    expect(
      conflictOf([english, rule('require', [{ context: 'memberSignedIn', op: 'isChecked' }])], SCOPE)?.answers,
    ).toEqual([
      { subject: 'context:culture', values: ['en-US'] },
      { subject: 'context:memberSignedIn', values: ['true'] },
    ]);
    const signedIn = rule('require', [{ context: 'memberSignedIn', op: 'isChecked' }]);
    const foreign = rule('hide', [{ context: 'culture', op: 'isNoneOf', value: ['en-US', 'de-DE'] }]);
    expect(conflictOf([foreign, signedIn], SCOPE)).toBeNull();
    expect(conflictOf([rule('hide', [{ context: 'culture', op: 'isEmpty' }]), signedIn], SCOPE)).toBeNull();
    // Only a signed-in member is in groups.
    const staff = rule('hide', [{ context: 'memberGroup', op: 'isChecked', value: 'staff' }]);
    const guest = rule('require', [{ context: 'memberSignedIn', op: 'isNotChecked' }]);
    expect(conflictOf([staff, guest], SCOPE)).toBeNull();
    expect(conflictOf([staff, signedIn], SCOPE)?.answers).toEqual([
      { subject: 'context:memberGroup', values: ['staff'] },
      { subject: 'context:memberSignedIn', values: ['true'] },
    ]);
    // A text a rule looks inside has no answer that stands for the others.
    const plain = rule('hide', [{ field: 'name', op: 'notContains', value: 'a' }]);
    expect(conflictOf([plain, rule('require', [{ field: 'name', op: 'isNotEmpty' }])], SCOPE)).toBeNull();
    // Nor does a text another answer is looked inside of: "b" is none of "abc", yet "abc" contains it.
    const inside = rule('hide', [
      { field: 'name', op: 'notContains', value: { field: 'nick' } },
      { field: 'nick', op: 'isNotEmpty' },
      { field: 'name', op: 'isOneOf', value: ['abc'] },
    ]);
    expect(
      conflictOf([inside, rule('require', [{ field: 'name', op: 'isNotEmpty' }])], {
        ...SCOPE,
        fields: { ...SCOPE.fields, nick: 'text' },
      }),
    ).toBeNull();
  });

  it('says a text the rules only test for being equal as none of the texts they name', () => {
    const other = conflictOf(
      [
        rule('hide', [{ field: 'name', op: 'notEquals', value: 'Ann' }]),
        rule('require', [{ field: 'name', op: 'isNotEmpty' }]),
      ],
      SCOPE,
    );
    expect(other?.answers).toEqual([{ subject: 'field:name', values: [], noneOf: ['Ann'] }]);
    const given = conflictOf(
      [
        rule('hide', [{ context: 'query.ref', op: 'isNotEmpty' }]),
        rule('require', [{ field: 'agree', op: 'isChecked' }]),
      ],
      SCOPE,
    );
    expect(given?.answers).toEqual([
      { subject: 'context:query.ref', values: [], noneOf: [] },
      { subject: 'field:agree', values: ['true'] },
    ]);
  });

  it('says nothing of a rule the engine refuses', () => {
    const broken = rule('hide', [{ field: 'topic', op: 'nope', value: 'press' }]);
    expect(conflictOf([broken, rule('require', [{ field: 'topic', op: 'isNotEmpty' }])], SCOPE)).toBeNull();
    // Nor of a show rule it refuses, which would otherwise read as never holding.
    const unread = rule('show', [{ field: 'topic', op: 'nope', value: 'press' }]);
    expect(conflictOf([unread, rule('require', [{ field: 'topic', op: 'isNotEmpty' }])], SCOPE)).toBeNull();
  });
});

describe('a conflict with the rules of what holds a field', () => {
  it("weighs a group's and a page's rule as hiding the field, and names the one that does", () => {
    const require = rule('require', [{ field: 'topic', op: 'equals', value: 'press' }]);
    const own = rule('show', [{ field: 'topic', op: 'isNotEmpty' }]);
    const group = rule('hide', [{ field: 'topic', op: 'equals', value: 'press' }], { key: 'group' });
    const page = rule('show', [{ context: 'memberSignedIn', op: 'isChecked' }], { key: 'page' });
    expect(search(require, [own, group], SCOPE)).toEqual({
      hiding: group,
      requiring: require,
      answers: [{ subject: 'field:topic', values: ['press'] }],
    });
    // The page shows only to members, so a visitor not signed in has the field hidden while it is required.
    expect(search(require, [own, page], SCOPE)).toEqual({
      hiding: page,
      requiring: require,
      answers: [
        { subject: 'field:topic', values: ['press'] },
        { subject: 'context:memberSignedIn', values: [] },
      ],
    });
    expect(search(require, [own], SCOPE)).toBeNull();
    expect(search(rule('show', [{ field: 'topic', op: 'isNotEmpty' }]), [group], SCOPE)).toBeNull();
  });
});

describe('whether an element ever shows', () => {
  it('shows without rules in use, and for answers its rules name', () => {
    expect(showsAtAll([], SCOPE, [])).toBe('shows');
    expect(showsAtAll([rule('show', [])], SCOPE, [])).toBe('shows');
    expect(showsAtAll([rule('show', [{ field: 'topic', op: 'equals', value: 'press' }])], SCOPE, [])).toBe('shows');
    expect(showsAtAll([rule('hide', [{ context: 'memberSignedIn', op: 'isChecked' }])], SCOPE, [])).toBe('shows');
    // A rule that is off changes nothing.
    const off = rule('show', [{ field: 'topic', op: 'equals', value: 'nowhere' }], { enabled: false });
    expect(showsAtAll([off], SCOPE, [])).toBe('shows');
  });

  it('never shows where no answer a visitor can give makes it, every answer being tried', () => {
    // An option the topic lacks.
    expect(showsAtAll([rule('show', [{ field: 'topic', op: 'equals', value: 'nowhere' }])], SCOPE, [])).toBe('never');
    // A field not yet answered, as one on a later page is going forward.
    expect(showsAtAll([rule('show', [{ field: 'name', op: 'isNotEmpty' }])], SCOPE, ['name'])).toBe('never');
    expect(showsAtAll([rule('show', [{ field: 'name', op: 'isNotEmpty' }])], SCOPE, [])).toBe('shows');
    const always = rule('hide', [
      {
        match: 'any',
        conditions: [
          { field: 'agree', op: 'isChecked' },
          { field: 'agree', op: 'isNotChecked' },
        ],
      },
    ]);
    expect(showsAtAll([always], SCOPE, [])).toBe('never');
    expect(showsAtAll([rule('show', [{ context: 'culture', op: 'equals', value: 'fr-FR' }])], SCOPE, [])).toBe('never');
  });

  it("can't tell where the answers tried don't stand for all, or the engine refuses a rule", () => {
    // Between 4 and 5 lies 4.5, which no neighbour of either is.
    const between = rule('show', [
      { field: 'guests', op: 'greaterThan', value: '4' },
      { field: 'guests', op: 'lessThan', value: '5' },
    ]);
    expect(showsAtAll([between], SCOPE, [])).toBe('unknown');
    // A text a rule looks inside, three ticks of several choices, and days counted from today.
    const inside = rule('show', [
      { field: 'name', op: 'notContains', value: 'a' },
      { field: 'name', op: 'isNotEmpty' },
    ]);
    expect(showsAtAll([inside], SCOPE, [])).toBe('unknown');
    expect(showsAtAll([rule('show', [{ field: 'topics', op: 'countAtLeast', value: '4' }])], SCOPE, [])).toBe(
      'unknown',
    );
    const soon = rule('show', [{ field: 'arrival', op: 'greaterThan', value: { today: '+7d' } }]);
    expect(showsAtAll([soon], SCOPE, ['arrival'])).toBe('unknown');
    expect(showsAtAll([rule('show', [{ field: 'topic', op: 'nope', value: 'press' }])], SCOPE, [])).toBe('unknown');
  });

  it("can't tell for more sets of answers than C# counts in a long, trimmed to what it tries", () => {
    // Seven choices of 1,100 options, as Core's RuleSearchTests has them.
    const choices = Array.from({ length: 7 }, (_, at) => `choice${at + 1}`);
    const options = Array.from({ length: 1100 }, (_, at) => `option${at + 1}`);
    const scope: SearchScope = {
      fields: Object.fromEntries(choices.map((key) => [key, 'choice'])),
      options: () => options,
      cultures: ['en-US'],
      today: '2026-11-04',
    };
    const nowhere = rule(
      'show',
      choices.map((field) => ({ field, op: 'equals', value: 'nowhere' })),
    );
    expect(showsAtAll([nowhere], scope, [])).toBe('unknown');
  });
});

describe('what a form hides for a set of answers', () => {
  const when = (effect: string, value: string): Rule => ({
    effect,
    enabled: true,
    match: 'all',
    conditions: [{ field: 'topic', op: 'equals', value }],
  });
  const CONTEXT: Context = {
    now: '2026-11-04T12:00:00Z',
    timeZone: 'UTC',
    culture: 'en-US',
    member: { signedIn: false, groups: [] },
    query: {},
    hidden: [],
  };
  // The notes' group is hidden for the press, the party for delivery and the plan for shipping.
  const form = (calculation: LogicField['calculation'] = null, nope = false): LogicForm => ({
    pages: [
      {
        id: 'p1',
        groups: [
          {
            id: 'g1',
            fields: [
              {
                id: 'topic',
                kind: 'choice',
                rules: nope ? [{ ...when('show', 'press'), conditions: [{ field: 'topic', op: 'nope' }] }] : [],
              },
              { id: 'party', rules: [when('hide', 'delivery')], fields: [{ id: 'guests', kind: 'number' }] },
              { id: 'plan', rules: [when('hide', 'shipping')], rows: [{ id: 'meals', kind: 'choice' }] },
              { id: 'count', kind: 'number', alias: 'count' },
              { id: 'total', kind: 'number', alias: 'total', calculation },
            ],
          },
          { id: 'g2', rules: [when('hide', 'press')], fields: [{ id: 'notes', kind: 'text' }] },
        ],
      },
    ],
  });

  it('is each field hidden with what holds it, a repeatable group child in its row, a grid row with its grid', () => {
    const sight = formSight(form(), null);
    expect([...(sight({ topic: ['press'] }, CONTEXT) ?? [])]).toEqual(['g2', 'notes']);
    expect([...(sight({ topic: ['delivery'], guests: ['5'] }, CONTEXT) ?? [])]).toEqual(['party', 'guests']);
    expect([...(sight({ topic: ['shipping'], meals: ['yes'] }, CONTEXT) ?? [])]).toEqual(['plan', 'meals']);
  });

  it("can't be told for a form the engine refuses, or one it can't work out without the calculation language", () => {
    expect(formSight(form(null, true), null)({ topic: ['press'] }, CONTEXT)).toBeNull();
    expect(formSight(form({ formula: 'count * 2' }), null)({ topic: ['press'] }, CONTEXT)).toBeNull();
    expect([...(formSight(form({ formula: 'count * 2' }), FORMULAS)({ topic: ['press'] }, CONTEXT) ?? [])]).toEqual([
      'g2',
      'notes',
    ]);
  });
});
