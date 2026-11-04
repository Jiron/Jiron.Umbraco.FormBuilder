// A conflict between a field's rules (ROADMAP 9.5c, IA 5.1, ADR-0178): a hide rule, or a show rule that doesn't hold,
// and a require rule that hold for the same answers, found by the engine with the answers the rules name, their
// neighbours and none; never said where no such answers make both hold.
import { describe, expect, it } from 'vitest';
import type { DesignRule } from '../definition';
import { conflictOf, type ConflictScope } from './rule-conflicts';

const SCOPE: ConflictScope = {
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

const rule = (effect: string, conditions: readonly unknown[], extra: Partial<DesignRule> = {}): DesignRule => ({
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

  it('tries only answers a visitor can give', () => {
    const require = rule('require', [{ field: 'topic', op: 'isNotEmpty' }]);
    // No option of the topic is none of its options, and a language is one of the site's.
    const none = rule('hide', [{ field: 'topic', op: 'isNoneOf', value: ['delivery', 'shipping', 'press'] }]);
    expect(conflictOf([none, require], SCOPE)).toBeNull();
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
  });
});
