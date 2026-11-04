// Try it, the designer's live rule preview (ROADMAP 9.5a, ADR-0175, D10): its one adapter to the engine agrees with
// every condition and rule case of the shared vectors, the boxes give the engine the answers and the day they hold,
// and the verdict follows the rule's effect.
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { formatDate, LogicError, today, type Context, type Kind, type Rule } from '@jfb/logic';
import { describe, expect, it } from 'vitest';
import type { DesignRule } from '../definition';
import { draftOf, ruleOf } from './rule-draft';
import { answersOf, triedSubjects, tryContext, tryRule, verdictTerm, type TryBox } from './rule-try';

interface VectorCase {
  readonly id: string;
  readonly context?: Partial<Context>;
  readonly fields?: Readonly<Record<string, Kind>>;
  readonly values?: Readonly<Record<string, readonly string[]>>;
  readonly condition?: Rule['conditions'][number];
  readonly rule?: Rule;
  readonly expect: boolean | null | { readonly error: string };
}

interface VectorFile {
  readonly defaults: { readonly context: Context };
  readonly fields: Readonly<Record<string, Kind>>;
  readonly cases: readonly (VectorCase & Readonly<Record<string, unknown>>)[];
}

/** The shared rules vectors, read in place from the repository's tests/vectors. */
function rulesVectors(): VectorFile {
  let folder = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) folder = dirname(folder);
  return JSON.parse(readFileSync(join(folder, 'tests', 'vectors', 'rules.json'), 'utf8')) as VectorFile;
}

const file = rulesVectors();
const cases = file.cases.filter((one) => one.condition !== undefined || one.rule !== undefined);
const ruleOfCase = (one: VectorCase): Rule =>
  one.rule ?? {
    effect: 'show',
    enabled: true,
    match: 'all',
    conditions: [one.condition as Rule['conditions'][number]],
  };
const expected = (one: VectorCase) =>
  typeof one.expect === 'object' && one.expect !== null ? { error: one.expect.error } : { matches: one.expect };

describe('Try it agrees with the shared vectors (D10)', () => {
  it('reads every condition and rule case of rules.json', () => {
    expect(cases.length).toBeGreaterThan(300);
  });

  it.each(cases.map((one) => [one.id, one] as const))('%s', (_, one) => {
    const context = { ...file.defaults.context, ...one.context };
    expect(tryRule(ruleOfCase(one), one.fields ?? file.fields, one.values ?? {}, context)).toEqual(expected(one));
  });

  it("gives the engine the builder's own rule and the answers its boxes hold", () => {
    // What a box can hold of a field's answers: one text, the options ticked, a checkbox's state, how many files.
    const boxOf = (kind: Kind, values: readonly string[]): TryBox | undefined => {
      if (kind === 'checkbox')
        return values.length === 0 ? false : values.length === 1 && values[0] === 'true' ? true : undefined;
      // A files box holds how many: only a rule that counts files, or asks for none, can be tried through it.
      if (kind === 'files') return values.every((value, at) => value === `file${at + 1}`) ? values.length : undefined;
      if (kind === 'choices') return values;
      return values.length === 0 ? '' : values.length === 1 ? values[0] : undefined;
    };
    let through = 0;
    for (const one of cases) {
      const fields = one.fields ?? file.fields;
      const boxed: Record<string, string[]> = {};
      let fits = true;
      for (const [field, values] of Object.entries(one.values ?? {})) {
        const kind = fields[field];
        const box = kind ? boxOf(kind, values) : undefined;
        if (!kind || box === undefined) fits = false;
        else boxed[field] = answersOf(kind, box);
      }
      // A draft holds only matches of all or any: the server reads them as such (RuleMatch), so another is no draft.
      const matches = (group: { match?: string; conditions?: readonly unknown[] }): boolean =>
        (group.match === 'all' || group.match === 'any') &&
        (group.conditions ?? []).every((item) => {
          const given = item as { match?: string; conditions?: readonly unknown[] };
          return !Array.isArray(given.conditions) || matches(given);
        });
      if (!fits || !matches(ruleOfCase(one))) continue;
      through++;
      // The rule as the builder edits it and writes it back.
      const rule = ruleOf(draftOf({ key: 'k', ...ruleOfCase(one) } as DesignRule, 't', () => 'id')) as unknown as Rule;
      const context = { ...file.defaults.context, ...one.context };
      expect([one.id, tryRule(rule, fields, boxed, context)]).toEqual([one.id, expected(one)]);
    }
    expect(through).toBeGreaterThan(300);
  });

  it("gives the engine the vectors' visit through the boxes, today the day the box holds", () => {
    // The boxes give a day, one query text per name and no hidden fields; a case of a broken instant or time zone,
    // which they can't give, is the engine's own to answer, above.
    const dayOf = (context: Context): string | null => {
      try {
        return formatDate(today(context.now, context.timeZone));
      } catch (error) {
        if (error instanceof LogicError) return null;
        throw error;
      }
    };
    const visited = cases.filter((one) => {
      const query = one.context?.query ?? {};
      return (
        (one.context?.hidden ?? []).length === 0 &&
        Object.values(query).every((value) => typeof value === 'string') &&
        dayOf({ ...file.defaults.context, ...one.context }) !== null
      );
    });
    expect(visited.length).toBeGreaterThan(300);
    for (const one of visited) {
      const context = { ...file.defaults.context, ...one.context };
      const day = dayOf(context) ?? '';
      const boxes = tryContext({
        culture: context.culture,
        signedIn: context.member.signedIn,
        groups: context.member.groups,
        query: context.query as Readonly<Record<string, string>>,
        today: day,
      });
      expect([one.id, tryRule(ruleOfCase(one), one.fields ?? file.fields, one.values ?? {}, boxes)]).toEqual([
        one.id,
        expected(one),
      ]);
    }
  });
});

describe('the boxes of Try it', () => {
  it('give answers as a post does', () => {
    expect(answersOf('checkbox', true)).toEqual(['true']);
    expect(answersOf('checkbox', false)).toEqual([]);
    expect(answersOf('files', 2)).toEqual(['file1', 'file2']);
    expect(answersOf('files', -1)).toEqual([]);
    expect(answersOf('choices', ['a', 'b'])).toEqual(['a', 'b']);
    expect(answersOf('text', '')).toEqual([]);
    expect(answersOf('number', '12.5')).toEqual(['12.5']);
    expect(answersOf('text', undefined)).toEqual([]);
  });

  it('make the day given today, wherever the site is', () => {
    const context = tryContext({
      culture: 'de-DE',
      signedIn: true,
      groups: ['Gold'],
      query: { ref: 'web' },
      today: '2026-12-01',
    });
    expect(context).toEqual({
      now: '2026-12-01T12:00:00Z',
      timeZone: 'UTC',
      culture: 'de-DE',
      member: { signedIn: true, groups: ['Gold'] },
      query: { ref: 'web' },
      hidden: [],
    });
    expect(formatDate(today(context.now, context.timeZone))).toBe('2026-12-01');
  });

  it('ask for each field and part of the visit a rule looks at or compares with, once, in order', () => {
    const draft = draftOf(
      {
        key: 'r',
        effect: 'show',
        conditions: [
          { field: 'departure', op: 'greaterThan', value: { field: 'arrival' } },
          {
            match: 'any',
            conditions: [
              { context: 'culture', op: 'equals', value: 'de' },
              { field: 'arrival', op: 'lessThan', value: { today: '+1w' } },
            ],
          },
          { field: 'guests', op: 'between', value: ['1', { field: 'limit' }] },
        ],
      },
      'x',
      () => 'id',
    );
    expect(triedSubjects(draft)).toEqual([
      'field:departure',
      'field:arrival',
      'context:culture',
      'context:today',
      'field:guests',
      'field:limit',
    ]);
  });

  it("say what the element does, by the rule's effect", () => {
    expect(verdictTerm('show', true)).toBe('jfbRules_tryShows');
    expect(verdictTerm('show', false)).toBe('jfbRules_tryHidden');
    expect(verdictTerm('hide', true)).toBe('jfbRules_tryHidden');
    expect(verdictTerm('hide', false)).toBe('jfbRules_tryShows');
    expect(verdictTerm('require', true)).toBe('jfbRules_tryRequired');
    expect(verdictTerm('require', false)).toBe('jfbRules_tryNotRequired');
    // A rule that is off does nothing: the element shows and is not required.
    expect(verdictTerm('hide', null)).toBe('jfbRules_tryShows');
    expect(verdictTerm('require', null)).toBe('jfbRules_tryNotRequired');
    // A validation rule's message shows while it matches, and never while it is off (ADR-0178).
    expect(verdictTerm('message', true)).toBe('jfbRules_tryMessageShows');
    expect(verdictTerm('message', false)).toBe('jfbRules_tryMessageHidden');
    expect(verdictTerm('message', null)).toBe('jfbRules_tryMessageHidden');
  });
});
