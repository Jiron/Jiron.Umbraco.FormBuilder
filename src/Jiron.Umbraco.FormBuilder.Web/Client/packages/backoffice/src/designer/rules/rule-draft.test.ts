// The rules builder's model (ROADMAP 9.5a, ADR-0175): a rule edited and written back, its groups, what a condition may
// look at, the operators each offers, how a condition changes with them, why a row can't be kept, and the first line.
import { contextKindOf, operatorsFor, type Kind } from '@jfb/logic';
import { describe, expect, it } from 'vitest';
import { must } from '../../testing';
import type { DesignDefinition, DesignField, DesignRule } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import {
  addCondition,
  addGroup,
  clashOf,
  conditionsOf,
  draftOf,
  effectsOf,
  fieldKinds,
  findItem,
  isDraftGroup,
  mayNest,
  newDraft,
  operatorsOf,
  problemOf,
  ROOT,
  ruleOf,
  sameRule,
  scopeContexts,
  shapeOf,
  subjectKinds,
  subjectsFor,
  updateItem,
  withOperator,
  withRule,
  withSubject,
  type DraftCondition,
  type DraftGroup,
  type DraftRule,
  type RuleScope,
} from './rule-draft';
import { typeFactsOf } from './rule-names';

const field = (key: string, type: string, extra: Partial<DesignField> = {}): DesignField => ({
  key,
  type,
  alias: key,
  label: { '': key[0]?.toUpperCase() + key.slice(1) },
  ...extra,
});

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      title: { '': 'About you' },
      groups: [
        {
          key: 'g1',
          fields: [
            field('country', 'dropdown', { options: { items: [{ value: 'de' }, { value: 'other' }] } }),
            field('which', 'text'),
            field('topics', 'checkboxes'),
            field('agree', 'checkbox'),
            field('heading', 'heading'),
          ],
        },
      ],
    },
    {
      key: 'p2',
      title: { '': 'Your trip' },
      groups: [
        {
          key: 'g2',
          fields: [
            field('arrival', 'date'),
            field('departure', 'date'),
            field('guests', 'number'),
            field('uploads', 'file'),
            field('many', 'dropdown', { settings: { multiple: true } }),
            field('grid', 'grid', { rows: [{ key: 'r1', label: { '': 'Food' } }] }),
            field('people', 'repeater', { fields: [field('child', 'text')] }),
          ],
        },
      ],
    },
  ],
  settings: { developer: { queryValues: ['ref'] } },
};

const FACTS = typeFactsOf(builtInTypes);
const factsOf = (type: string) => FACTS[type];
const VISIT = { languages: true, queryValues: ['ref'] };
const SCOPE: RuleScope = {
  fields: fieldKinds(DEFINITION, factsOf),
  contexts: ['culture', 'memberSignedIn', 'memberGroup', 'query.ref', 'today'],
};

const ids = () => {
  let next = 0;
  return () => `i${++next}`;
};

const condition = (extra: Partial<DraftCondition>): DraftCondition => ({
  id: 'c',
  field: null,
  context: null,
  op: null,
  value: undefined,
  ...extra,
});

describe('a rule as the builder edits it', () => {
  it('reads a rule with nested groups and writes it back as it came, with what else it held', () => {
    const rule: DesignRule = {
      key: 'r',
      effect: 'show',
      enabled: false,
      match: 'any',
      note: 'kept',
      conditions: [
        { field: 'country', op: 'equals', value: 'other' },
        { match: 'all', conditions: [{ context: 'today', op: 'greaterThan', value: { today: '+2d' } }] },
      ],
    };
    const draft = draftOf(rule, 'which', ids());
    expect(draft).toMatchObject({ key: 'r', target: 'which', effect: 'show', enabled: false });
    expect(draft.root.match).toBe('any');
    expect(conditionsOf(draft).map((one) => one.id)).toEqual(['i1', 'i3']);
    expect(ruleOf(draft)).toEqual(rule);
    // A value given as null reads as none, and a rule without match or enabled says the same as one with the defaults.
    const nulled = draftOf(
      { key: 'n', effect: 'hide', conditions: [{ field: 'agree', op: 'isChecked', value: null }] },
      'x',
      ids(),
    );
    expect(ruleOf(nulled).conditions).toEqual([{ field: 'agree', op: 'isChecked' }]);
    expect(
      sameRule(
        { key: 'n', effect: 'hide', conditions: [] },
        { key: 'n', effect: 'hide', enabled: true, match: 'all', conditions: [] },
      ),
    ).toBe(true);
    expect(sameRule({ key: 'n', effect: 'hide', conditions: [] }, { key: 'n', effect: 'show', conditions: [] })).toBe(
      false,
    );
    // Read and written back, a rule with its defaults unstated anywhere in it says the same.
    const bare: DesignRule = {
      key: 'b',
      effect: 'show',
      conditions: [
        { field: 'agree', op: 'isChecked', value: null },
        { conditions: [{ field: 'which', op: 'isEmpty' }] },
      ],
    };
    expect(sameRule(bare, ruleOf(draftOf(bare, 'x', ids())))).toBe(true);
    expect(sameRule(bare, { ...bare, conditions: [{ field: 'agree', op: 'isNotChecked' }] })).toBe(false);
  });

  it('starts a new rule used, matching all, with one empty condition', () => {
    const draft = newDraft('k', 'which', 'require', ids());
    expect(ruleOf(draft)).toEqual({
      key: 'k',
      effect: 'require',
      enabled: true,
      match: 'all',
      conditions: [{ op: '' }],
    });
    expect(conditionsOf(draft)).toEqual([condition({ id: 'i1' })]);
  });
});

describe('groups of conditions', () => {
  const flat = (): DraftRule => {
    const next = ids();
    return addCondition(newDraft('k', 'which', 'show', next), ROOT, next());
  };

  it('adds a condition at the end of the group it is asked for', () => {
    const draft = flat();
    expect(draft.root.items.map((item) => item.id)).toEqual(['i1', 'i2']);
  });

  it("turns a rule's own conditions into its first group, with the other match around the two", () => {
    const next = ids();
    let draft = newDraft('k', 'which', 'show', next);
    draft = addCondition(draft, ROOT, next());
    const grouped = addGroup(draft, ROOT, next);
    expect(grouped.root.match).toBe('any');
    const [first, second] = (grouped.root.items as DraftGroup[]).map((one, at) => must(one, `group ${at + 1}`));
    expect([isDraftGroup(must(first, 'group 1')), isDraftGroup(must(second, 'group 2'))]).toEqual([true, true]);
    expect(first?.match).toBe('all');
    expect(first?.items.map((item) => item.id)).toEqual(['i1', 'i2']);
    expect(second?.items).toHaveLength(1);
    // With groups there already, a group joins the end; inside a group, one nests.
    const third = addGroup(grouped, ROOT, next);
    expect(third.root.items).toHaveLength(3);
    const nested = addGroup(grouped, must(first, 'group 1').id, next);
    const inner = (nested.root.items[0] as DraftGroup).items;
    expect(inner.map((item) => isDraftGroup(item))).toEqual([false, false, true]);
    expect(findItem(nested, must(inner[2], 'the nested group').id)?.depth).toBe(3);
  });

  it('nests to the tenth level and no deeper', () => {
    const next = ids();
    let draft = newDraft('k', 'which', 'show', next);
    let group = ROOT;
    for (let level = 2; level <= 10; level++) {
      draft = addGroup(draft, group, next);
      const found = must(findItem(draft, group), 'the group').item as DraftGroup;
      group = must(found.items.filter(isDraftGroup).at(-1), 'the group nested').id;
      expect(findItem(draft, group)?.depth).toBe(level);
    }
    expect(mayNest(draft, group)).toBe(false);
    expect(addGroup(draft, group, next)).toBe(draft);
  });

  it("takes a group away with its last condition, and changes the rule's own list but never removes it", () => {
    const next = ids();
    let draft = addGroup(addCondition(newDraft('k', 'which', 'show', next), ROOT, next()), ROOT, next);
    const second = draft.root.items[1] as DraftGroup;
    draft = updateItem(draft, must(second.items[0], 'its condition').id, () => null);
    expect(draft.root.items).toHaveLength(1);
    expect(updateItem(draft, ROOT, () => null)).toBe(draft);
    expect(updateItem(draft, ROOT, (root) => ({ ...(root as DraftGroup), match: 'all' })).root.match).toBe('all');
  });
});

describe('what a condition may look at', () => {
  it('is every field rules compare, by page in the form order, then the visit; never the element itself', () => {
    const subjects = subjectsFor(DEFINITION, factsOf, 'which', 'show', VISIT);
    expect(subjects.map((one) => `${one.id} ${one.kind} ${one.page ?? '-'}`)).toEqual([
      'field:country choice 0',
      'field:topics choices 0',
      'field:agree checkbox 0',
      'field:arrival date 1',
      'field:departure date 1',
      'field:guests number 1',
      'field:uploads files 1',
      // A dropdown with several answers compares as several choices, a grid's row as one choice.
      'field:many choices 1',
      'field:r1 choice 1',
      'field:child text 1',
      'context:culture text -',
      'context:memberSignedIn checkbox -',
      'context:memberGroup choices -',
      'context:query.ref text -',
      'context:today date -',
    ]);
  });

  it("leaves out what a show or hide rule's element holds, but only the field itself for a require rule", () => {
    const group = subjectsFor(DEFINITION, factsOf, 'g1', 'hide', VISIT).map((one) => one.id);
    expect(group).not.toContain('field:country');
    expect(group).toContain('field:arrival');
    const people = subjectsFor(DEFINITION, factsOf, 'people', 'show', VISIT).map((one) => one.id);
    expect(people).not.toContain('field:child');
    const grid = subjectsFor(DEFINITION, factsOf, 'grid', 'show', VISIT).map((one) => one.id);
    expect(grid).not.toContain('field:r1');
    const required = subjectsFor(DEFINITION, factsOf, 'which', 'require', VISIT).map((one) => one.id);
    expect(required).toContain('field:country');
    expect(required).not.toContain('field:which');
    // What a condition looks at already stays on its list.
    expect(subjectsFor(DEFINITION, factsOf, 'which', 'show', VISIT, 'field:which').map((one) => one.id)).toContain(
      'field:which',
    );
    const plain = subjectsFor(DEFINITION, factsOf, 'which', 'show', { languages: false, queryValues: [] });
    expect(plain.filter((one) => one.context).map((one) => one.context)).toEqual([
      'memberSignedIn',
      'memberGroup',
      'today',
    ]);
  });
});

describe("what a visitor's rules can't look at", () => {
  it('is a value kept on the server, or a field a member value stands in for; a grid row stays', () => {
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                field('source', 'hidden'),
                field('email', 'email', { prefill: { member: 'email', memberMode: 'hide' } }),
                field('phone', 'phone', { prefill: { member: 'phone', memberMode: 'show' } }),
                field('which', 'text'),
                field('grid', 'grid', { rows: [{ key: 'r1' }] }),
              ],
            },
          ],
        },
      ],
    };
    const ids = subjectsFor(definition, factsOf, 'which', 'show', { languages: false, queryValues: [] })
      .filter((one) => one.field)
      .map((one) => one.id);
    expect(ids).toEqual(['field:phone', 'field:r1']);
    expect(Object.keys(fieldKinds(definition, factsOf))).toEqual(['phone', 'which', 'r1']);
    // An action's rule runs on the server, where the hidden field has its value.
    expect(subjectKinds(field('source', 'hidden'), factsOf('hidden'), false)).toEqual([['source', 'text']]);
  });

  it('keeps the language in the check on a site of one language, offering it only where a condition looks at it', () => {
    expect(scopeContexts({ languages: false, queryValues: [] })).toContain('culture');
    const plain = { languages: false, queryValues: [] };
    expect(subjectsFor(DEFINITION, factsOf, 'which', 'show', plain).some((one) => one.context === 'culture')).toBe(
      false,
    );
    expect(subjectsFor(DEFINITION, factsOf, 'which', 'show', plain, 'context:culture')[10]?.context).toBe('culture');
  });
});

describe('operators', () => {
  const offered = (kind: Kind, context: string | null = null) => operatorsOf({ kind, context });

  it('are those a sentence about the kind means, in the builder order', () => {
    expect(offered('text')).toEqual([
      'equals',
      'notEquals',
      'contains',
      'notContains',
      'startsWith',
      'endsWith',
      'isOneOf',
      'isNoneOf',
      'matches',
      'isEmpty',
      'isNotEmpty',
    ]);
    expect(offered('choice')).toEqual(['equals', 'notEquals', 'isOneOf', 'isNoneOf', 'isEmpty', 'isNotEmpty']);
    expect(offered('checkbox')).toEqual(['isChecked', 'isNotChecked']);
    expect(offered('files')).toEqual(['countAtLeast', 'countAtMost', 'isEmpty', 'isNotEmpty']);
    expect(offered('date')).toEqual([
      'equals',
      'notEquals',
      'greaterThan',
      'lessThan',
      'between',
      'isEmpty',
      'isNotEmpty',
    ]);
    expect(offered('text', 'culture')).toEqual(['equals', 'notEquals', 'isOneOf', 'isNoneOf']);
    expect(offered('checkbox', 'memberSignedIn')).toEqual(['isChecked', 'isNotChecked']);
    expect(offered('choices', 'memberGroup')).toEqual(['isChecked', 'isNotChecked', 'isOneOf', 'isNoneOf']);
    expect(offered('date', 'today')).toEqual(['equals', 'greaterThan', 'lessThan', 'between']);
    expect(offered('text', 'query.ref')).toEqual(offered('text'));
  });

  it('offer each of the engine operators for some kind, only ones it takes, and keep one a draft holds', () => {
    const kinds: Kind[] = ['text', 'choice', 'choices', 'checkbox', 'files', 'number', 'date', 'time', 'dateTime'];
    const engine = new Set(kinds.flatMap((kind) => operatorsFor(kind).map((one) => one.op)));
    const builder = new Set(kinds.flatMap((kind) => offered(kind)));
    expect([...engine].filter((op) => !builder.has(op))).toEqual([]);
    // Every operator offered, for every kind and part of the visit, is one the engine takes for that kind.
    const visit = ['culture', 'memberSignedIn', 'memberGroup', 'today', 'query.ref'];
    const refused = [
      ...kinds.map((kind) => [kind, null] as const),
      ...visit.map((context) => [must(contextKindOf(context), context), context] as const),
    ].flatMap(([kind, context]) => {
      const takes = new Set(operatorsFor(kind).map((one) => one.op));
      return offered(kind, context)
        .filter((op) => !takes.has(op))
        .map((op) => `${context ?? kind}: ${op}`);
    });
    expect(refused).toEqual([]);
    expect(operatorsOf({ kind: 'choice', context: null }, 'countAtLeast').at(-1)).toBe('countAtLeast');
    expect(shapeOf('choices', 'isChecked')).toBe('checked');
    expect(shapeOf('choice', 'contains')).toBeUndefined();
  });
});

describe('a condition changed', () => {
  const subjects = subjectsFor(DEFINITION, factsOf, 'which', 'show', VISIT);
  const subject = (id: string) => subjects.find((one) => one.id === id);

  it('keeps its operator and value for another answer of the same kind, and starts again for another kind', () => {
    const before = condition({ field: 'arrival', op: 'greaterThan', value: '2026-12-01' });
    expect(withSubject(before, subject('field:departure'), 'date')).toEqual({ ...before, field: 'departure' });
    expect(withSubject(before, subject('field:guests'), 'date')).toEqual({
      ...before,
      field: 'guests',
      op: 'equals',
      value: undefined,
    });
    expect(withSubject(before, subject('context:today'), 'date')).toEqual({ ...before, field: null, context: 'today' });
    // An answer compared with itself compares nothing.
    const compared = condition({ field: 'arrival', op: 'lessThan', value: { field: 'departure' } });
    expect(withSubject(compared, subject('field:departure'), 'date').value).toBeUndefined();
    expect(withSubject(compared, undefined, 'date')).toEqual(condition({}));
  });

  it("keeps as much of its value as another operator's operand takes", () => {
    const one = condition({ field: 'which', op: 'equals', value: 'x' });
    expect(withOperator(one, 'notEquals', 'text').value).toBe('x');
    expect(withOperator(one, 'isOneOf', 'text').value).toEqual(['x']);
    expect(withOperator(withOperator(one, 'isOneOf', 'text'), 'contains', 'text').value).toBe('x');
    expect(withOperator(one, 'isEmpty', 'text').value).toBeUndefined();
    expect(withOperator(condition({ field: 'guests', op: 'equals', value: '2' }), 'between', 'number').value).toEqual([
      '2',
      '',
    ]);
    expect(
      withOperator(condition({ field: 'country', op: 'equals', value: 'de' }), 'isNoneOf', 'choice').value,
    ).toEqual(['de']);
    expect(
      withOperator(condition({ field: 'topics', op: 'isOneOf', value: ['a', 'b'] }), 'isChecked', 'choices').value,
    ).toBe('a');
    expect(
      withOperator(condition({ field: 'agree', op: 'isChecked' }), 'isNotChecked', 'checkbox').value,
    ).toBeUndefined();
    // A checkbox's "ticked" compares with nothing, so a stray value goes; an option's "ticked" keeps its option.
    expect(
      withOperator(condition({ field: 'agree', op: 'isChecked', value: 'true' }), 'isNotChecked', 'checkbox').value,
    ).toBeUndefined();
    expect(
      withOperator(condition({ field: 'topics', op: 'isChecked', value: 'a' }), 'isNotChecked', 'choices').value,
    ).toBe('a');
    expect(withOperator(one, 'nope', 'text')).toEqual({ ...one, op: 'nope', value: undefined });
    // A number of answers is no option, nor a pattern a text.
    expect(
      withOperator(condition({ field: 'topics', op: 'countAtLeast', value: '2' }), 'isChecked', 'choices').value,
    ).toBeUndefined();
    expect(
      withOperator(condition({ field: 'which', op: 'matches', value: '[A-Z]+' }), 'contains', 'text').value,
    ).toBeUndefined();
  });
});

describe("why a row can't be kept", () => {
  const cases: [string, Partial<DraftCondition>, string | null][] = [
    ['nothing looked at', {}, 'subject'],
    ['a field since deleted', { field: 'gone', op: 'equals', value: 'x' }, 'unknown'],
    ['a query value the form no longer allows', { context: 'query.old', op: 'equals', value: 'x' }, 'unknown'],
    ['no operator', { field: 'which' }, 'operator'],
    ['an operator the kind does not take', { field: 'country', op: 'contains', value: 'x' }, 'operatorNotForKind'],
    ['an empty value', { field: 'which', op: 'equals', value: '  ' }, 'value'],
    ['no value', { field: 'which', op: 'equals' }, 'value'],
    ['a text', { field: 'which', op: 'contains', value: 'Dr' }, null],
    ['a number in a culture', { field: 'guests', op: 'greaterThan', value: '2,5' }, 'number'],
    ['a number', { field: 'guests', op: 'greaterThan', value: '2.5' }, null],
    ['no date', { field: 'arrival', op: 'equals', value: '1.12.2026' }, 'date'],
    ['a day counted from today', { field: 'arrival', op: 'greaterThan', value: { today: '+2w' } }, null],
    ['an offset that is none', { field: 'arrival', op: 'greaterThan', value: { today: 'soon' } }, 'date'],
    ['another answer', { field: 'departure', op: 'greaterThan', value: { field: 'arrival' } }, null],
    ['an answer since deleted', { field: 'departure', op: 'greaterThan', value: { field: 'gone' } }, 'unknownOther'],
    ['a value where the operator takes none', { field: 'which', op: 'isEmpty', value: 'x' }, 'noValue'],
    ['a checkbox ticked with a value', { field: 'agree', op: 'isChecked', value: 'true' }, 'noValue'],
    ['the language, on a site of one language', { context: 'culture', op: 'equals', value: 'de' }, null],
    ['a list with nothing ticked', { field: 'country', op: 'isOneOf', value: [] }, 'value'],
    ['a list', { field: 'country', op: 'isOneOf', value: ['de', 'other'] }, null],
    ['a range with one end', { field: 'guests', op: 'between', value: ['1', ''] }, 'value'],
    ['a range', { field: 'guests', op: 'between', value: ['1', '4'] }, null],
    ['a count with a leading zero', { field: 'uploads', op: 'countAtLeast', value: '01' }, 'count'],
    ['a count', { field: 'uploads', op: 'countAtLeast', value: '2' }, null],
    ['a pattern the matcher refuses', { field: 'which', op: 'matches', value: '(?=a)' }, 'pattern'],
    ['a pattern', { field: 'which', op: 'matches', value: '[A-Z]+' }, null],
    ['a checkbox ticked', { field: 'agree', op: 'isChecked' }, null],
    ['an option ticked without the option', { field: 'topics', op: 'isChecked' }, 'value'],
    ['the visitor signed in', { context: 'memberSignedIn', op: 'isChecked' }, null],
    ['a time', { context: null, field: 'arrival', op: 'equals', value: '2026-12-01' }, null],
  ];
  it.each(cases)('%s', (_, given, problem) => {
    expect(problemOf(condition(given), SCOPE)).toBe(problem);
  });

  it('says the other kinds of value by their own problem', () => {
    const scope: RuleScope = { fields: { t: 'time', d: 'dateTime' }, contexts: [] };
    expect(problemOf(condition({ field: 't', op: 'equals', value: '25:00' }), scope)).toBe('time');
    expect(problemOf(condition({ field: 'd', op: 'equals', value: '2026-12-01' }), scope)).toBe('dateTime');
    expect(problemOf(condition({ field: 'd', op: 'equals', value: '2026-12-01T10:30' }), scope)).toBeNull();
  });
});

describe('the first line', () => {
  it('offers show and hide for pages and groups, and what a field type takes', () => {
    const featuresOf = (type: string) => FACTS[type]?.features ?? [];
    expect(effectsOf(DEFINITION, 'p1', featuresOf)).toEqual(['show', 'hide']);
    expect(effectsOf(DEFINITION, 'g2', featuresOf)).toEqual(['show', 'hide']);
    expect(effectsOf(DEFINITION, 'which', featuresOf)).toEqual(['show', 'hide', 'require']);
    expect(effectsOf(DEFINITION, 'heading', featuresOf)).toEqual(['show', 'hide']);
    expect(effectsOf(DEFINITION, 'gone', featuresOf)).toEqual([]);
  });

  it("finds a rule of the element that a rule can't sit beside, never the rule itself", () => {
    const show: DesignRule = { key: 's', effect: 'show', conditions: [] };
    const ruled: DesignDefinition = {
      pages: [{ key: 'p', groups: [{ key: 'g', fields: [field('which', 'text', { rules: [show] })] }] }],
    };
    expect(clashOf(ruled, 'which', 'hide', 'new')).toBe(show);
    expect(clashOf(ruled, 'which', 'show', 'new')).toBe(show);
    expect(clashOf(ruled, 'which', 'require', 'new')).toBeUndefined();
    expect(clashOf(ruled, 'which', 'hide', 's')).toBeUndefined();
  });

  it('puts a rule in place of the one with its key, at the end, or takes it out', () => {
    const one: DesignRule = { key: 'a', effect: 'show', conditions: [] };
    const two: DesignRule = { key: 'b', effect: 'require', conditions: [] };
    expect(withRule([one], 'b', two)).toEqual([one, two]);
    expect(withRule([one, two], 'a', { ...one, effect: 'hide' })).toEqual([{ ...one, effect: 'hide' }, two]);
    expect(withRule([one, two], 'a', null)).toEqual([two]);
    expect(withRule(null, 'x', null)).toEqual([]);
  });
});
