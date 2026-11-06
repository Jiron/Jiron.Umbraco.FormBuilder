// Review both versions' model (ROADMAP 10.3c, ADR-0200): an element added or removed comes with what it holds, as one
// difference; the side each starts on is the side that made it since the draft both started from; and the merge takes
// yours where chosen, key by key, and never loses an element either side keeps.
import { describe, expect, it } from 'vitest';
import type { DefinitionChangeResponseModel } from '../../api';
import type { DesignDefinition } from '../definition';
import { differencesOf, merged, startingSide, type Difference } from './review-model';

type Change = Partial<DefinitionChangeResponseModel> & Pick<DefinitionChangeResponseModel, 'kind' | 'change'>;
const change = (one: Change): DefinitionChangeResponseModel => ({ moved: false, properties: [], ...one });
/** A property change, so a change of an element is one of its own properties. */
const LABEL = [{ path: ['label', ''], from: 'Name', to: 'Full name' }];

const field = (key: string, label: string, more: Record<string, unknown> = {}) => ({
  key,
  type: 'text',
  label: { '': label },
  ...more,
});

/** A form of one page: the groups given, with the fields given. */
const formOf = (groups: Record<string, unknown>[], more: Record<string, unknown> = {}): DesignDefinition =>
  ({ pages: [{ key: 'p1', groups }], ...more }) as unknown as DesignDefinition;

const group = (key: string, fields: Record<string, unknown>[], more: Record<string, unknown> = {}) => ({
  key,
  fields,
  ...more,
});

const labels = (definition: DesignDefinition): string[][] =>
  definition.pages.flatMap((page) => page.groups.map((one) => one.fields.map((each) => each.label?.[''] ?? '')));

const names = { theirs: 'Contact', yours: 'Contact', base: 'Contact' };

/** The differences of a comparison, every one starting on the side given. */
const all = (changes: readonly DefinitionChangeResponseModel[]): Difference[] => differencesOf(changes, null, names);

describe('the differences of two drafts', () => {
  it('holds an element added or removed with what it holds, as one difference, its own change first', () => {
    const differences = all([
      change({ kind: 'group', key: 'g2', parentKey: 'p1', change: 'added' }),
      change({ kind: 'field', key: 'f3', parentKey: 'g2', change: 'added' }),
      change({ kind: 'rule', key: 'r1', parentKey: 'f3', change: 'added' }),
      change({ kind: 'field', key: 'f1', parentKey: 'g1', change: 'changed', properties: LABEL }),
      change({ kind: 'rule', key: 'r2', parentKey: 'f1', change: 'removed' }),
      change({ kind: 'settings', settingsGroup: 'appearance', change: 'changed' }),
      change({ kind: 'page', key: 'p2', change: 'removed' }),
      change({ kind: 'group', key: 'g9', parentKey: 'p2', change: 'removed' }),
    ]);
    expect(differences.map((one) => [one.id, one.changes.map((each) => each.key ?? each.settingsGroup)])).toEqual([
      ['group:g2', ['g2', 'f3', 'r1']],
      ['field:f1', ['f1']],
      // A rule of an element both keep is a difference of its own.
      ['rule:r2', ['r2']],
      ['settings:appearance', ['appearance']],
      ['page:p2', ['p2', 'g9']],
    ]);
  });

  it('starts each on the side that made it since the draft both started from, and waits where both did', () => {
    const changed = (key: string) => change({ kind: 'field', key, change: 'changed', properties: LABEL });
    const changes = [changed('f1'), changed('f2'), changed('f3'), changed('f4')];
    const fromBase = { theirs: [changed('f1'), changed('f3')], yours: [changed('f2'), changed('f3')] };
    const differences = differencesOf(changes, fromBase, names);
    expect(differences.map((one) => [one.id, one.by, startingSide(one)])).toEqual([
      ['field:f1', 'theirs', 'theirs'],
      ['field:f2', 'yours', 'yours'],
      ['field:f3', 'both', undefined],
      // Neither side's comparison names it: not known to be changed in both, and it waits too.
      ['field:f4', 'unknown', undefined],
    ]);
    // Without the draft both started from, each waits for a choice, and none is said to be changed in both.
    expect(differencesOf(changes, null, names).map((one) => [one.by, startingSide(one)])).toEqual([
      ['unknown', undefined],
      ['unknown', undefined],
      ['unknown', undefined],
      ['unknown', undefined],
    ]);
  });

  it("holds an element's own properties and its place as two choices, each by the side that changed it", () => {
    // Theirs moved Name; yours relabelled it.
    const differences = differencesOf(
      [change({ kind: 'field', key: 'f1', parentKey: 'g1', change: 'changed', moved: true, properties: LABEL })],
      {
        theirs: [change({ kind: 'field', key: 'f1', change: 'changed', moved: true })],
        yours: [change({ kind: 'field', key: 'f1', change: 'changed', properties: LABEL })],
      },
      names,
    );
    expect(
      differences.map((one) => [one.id, one.by, one.changes[0]?.moved, one.changes[0]?.properties.length]),
    ).toEqual([
      ['field:f1', 'yours', false, 1],
      ['place:f1', 'theirs', true, 0],
    ]);
    // A rule is one choice, its place with it.
    const rule = differencesOf(
      [change({ kind: 'rule', key: 'r1', parentKey: 'f1', change: 'changed', moved: true, properties: LABEL })],
      null,
      names,
    );
    expect(rule.map((one) => one.id)).toEqual(['rule:r1']);
  });

  it('knows who moved an element only from the comparisons that name it', () => {
    // From A, B, C theirs made C, A, B and yours B, A, C: seen from theirs, yours moved A, which neither did.
    const moved = (key: string) => change({ kind: 'field', key, parentKey: 'g1', change: 'changed', moved: true });
    const differences = differencesOf([moved('fa')], { theirs: [moved('fc')], yours: [moved('fb')] }, names);
    expect(differences.map((one) => [one.id, one.by])).toEqual([['place:fa', 'unknown']]);
  });

  it('holds the form name first, where the two differ, on the side that changed it', () => {
    const name = (theirs: string, yours: string, base: string | null) =>
      differencesOf([], null, { theirs, yours, base }).map((one) => [one.id, one.by]);
    expect(name('Contact', 'Contact', 'Contact')).toEqual([]);
    expect(name('Contact us', 'Contact', 'Contact')).toEqual([['name', 'theirs']]);
    expect(name('Contact', 'Get in touch', 'Contact')).toEqual([['name', 'yours']]);
    expect(name('Contact us', 'Get in touch', 'Contact')).toEqual([['name', 'both']]);
    expect(name('Contact us', 'Get in touch', null)).toEqual([['name', 'unknown']]);
  });
});

describe('the draft both versions make', () => {
  const theirs = formOf(
    [
      group('g1', [
        field('f1', 'Name', { rules: [{ key: 'r1', effect: 'show', conditions: [] }] }),
        field('f2', 'Email'),
      ]),
    ],
    {
      settings: { appearance: { theme: 'calm' } },
      actions: [{ key: 'a1', type: 'email', name: 'Notify' }],
    },
  );

  it('is theirs where nothing is taken from yours', () => {
    const yours = formOf([group('g1', [field('f1', 'Full name'), field('f2', 'Email')])]);
    const differences = all([change({ kind: 'field', key: 'f1', change: 'changed', properties: LABEL })]);
    expect(merged(theirs, yours, differences, new Set())).toEqual(theirs);
  });

  it("takes an element's own properties from yours, and keeps its rules and place from theirs", () => {
    const yours = formOf([group('g1', [field('f2', 'Email'), field('f1', 'Full name', { required: true })])]);
    const differences = all([change({ kind: 'field', key: 'f1', change: 'changed', moved: true, properties: LABEL })]);
    const result = merged(theirs, yours, differences, new Set(['field:f1']));
    const first = result.pages[0]?.groups[0]?.fields[0];
    expect([first?.key, first?.label, first?.['required'], first?.rules?.length]).toEqual([
      'f1',
      { '': 'Full name' },
      true,
      1,
    ]);
    // A property yours took away goes too.
    const without = formOf([group('g1', [field('f1', 'Name'), field('f2', 'Email')])]);
    const plain = merged(
      formOf([group('g1', [field('f1', 'Name', { placeholder: { '': 'Ada' } }), field('f2', 'Email')])]),
      without,
      differences,
      new Set(['field:f1']),
    );
    expect(plain.pages[0]?.groups[0]?.fields[0]).toEqual(field('f1', 'Name'));
    // Its place is a choice of its own.
    expect(labels(merged(theirs, yours, differences, new Set(['field:f1', 'place:f1'])))).toEqual([
      ['Email', 'Full name'],
    ]);
  });

  it('moves an element where yours has it, when its change moved it', () => {
    const yours = formOf([group('g1', [field('f2', 'Email'), field('f1', 'Name')])]);
    const differences = all([change({ kind: 'field', key: 'f1', change: 'changed', moved: true })]);
    expect(differences.map((one) => one.id)).toEqual(['place:f1']);
    expect(labels(merged(theirs, yours, differences, new Set(['place:f1'])))).toEqual([['Email', 'Name']]);
  });

  it('takes a settings group whole', () => {
    const yours = formOf([], { settings: { appearance: { theme: 'bold', cssClass: 'x' } } });
    const differences = all([change({ kind: 'settings', settingsGroup: 'appearance', change: 'changed' })]);
    expect(merged(theirs, yours, differences, new Set(['settings:appearance']))['settings']).toEqual({
      appearance: { theme: 'bold', cssClass: 'x' },
    });
  });

  it('adds what yours added where yours has it, or leaves it out', () => {
    const yours = formOf([group('g1', [field('f1', 'Name'), field('f9', 'Phone'), field('f2', 'Email')])]);
    const differences = all([change({ kind: 'field', key: 'f9', parentKey: 'g1', change: 'added' })]);
    expect(labels(merged(theirs, yours, differences, new Set(['field:f9'])))).toEqual([['Name', 'Phone', 'Email']]);
    expect(labels(merged(theirs, yours, differences, new Set()))).toEqual([['Name', 'Email']]);
  });

  it('adds a group with its fields, and an element yours moved into it only where its own change is taken', () => {
    const yours = formOf([
      group('g1', [field('f1', 'Name')]),
      group('g2', [field('f9', 'Phone'), field('f2', 'Email')]),
    ]);
    const changes = [
      change({ kind: 'group', key: 'g2', parentKey: 'p1', change: 'added' }),
      change({ kind: 'field', key: 'f9', parentKey: 'g2', change: 'added' }),
      change({ kind: 'field', key: 'f2', parentKey: 'g2', previousParentKey: 'g1', change: 'changed', moved: true }),
    ];
    const differences = all(changes);
    expect(differences.map((one) => one.id)).toEqual(['group:g2', 'place:f2']);
    expect(labels(merged(theirs, yours, differences, new Set(['group:g2', 'place:f2'])))).toEqual([
      ['Name'],
      ['Phone', 'Email'],
    ]);
    // The group alone: Email stays where theirs has it.
    expect(labels(merged(theirs, yours, differences, new Set(['group:g2'])))).toEqual([['Name', 'Email'], ['Phone']]);
  });

  it('removes what yours removed, and what yours keeps of it goes where yours has it', () => {
    const yours = formOf([group('g1', [field('f1', 'Name')])]);
    const removed = all([change({ kind: 'field', key: 'f2', parentKey: 'g1', change: 'removed' })]);
    expect(labels(merged(theirs, yours, removed, new Set(['field:f2'])))).toEqual([['Name']]);

    // Yours moved Name out of g1 into g3 and removed g1.
    const elsewhere = formOf([group('g3', [field('f1', 'Name')])]);
    const changes = [
      change({ kind: 'group', key: 'g3', parentKey: 'p1', change: 'added' }),
      change({ kind: 'field', key: 'f1', parentKey: 'g3', previousParentKey: 'g1', change: 'changed', moved: true }),
      change({ kind: 'group', key: 'g1', parentKey: 'p1', change: 'removed' }),
      change({ kind: 'field', key: 'f2', parentKey: 'g1', change: 'removed' }),
    ];
    const differences = all(changes);
    expect(differences.map((one) => one.id)).toEqual(['group:g3', 'place:f1', 'group:g1']);
    const groups = (definition: DesignDefinition) =>
      definition.pages[0]?.groups.map((one) => [one.key, one.fields.map((each) => each.key)]);
    // With Name's move left on theirs' side, the removal still takes Name where yours has it.
    expect(groups(merged(theirs, elsewhere, differences, new Set(['group:g3', 'group:g1'])))).toEqual([['g3', ['f1']]]);
    expect(groups(merged(theirs, elsewhere, differences, new Set(['group:g3', 'place:f1', 'group:g1'])))).toEqual([
      ['g3', ['f1']],
    ]);
    // Without g3, Name has nowhere to go: g1 stays, holding Name alone.
    expect(groups(merged(theirs, elsewhere, differences, new Set(['group:g1'])))).toEqual([['g1', ['f1']]]);
    expect(groups(merged(theirs, elsewhere, differences, new Set(['group:g3'])))).toEqual([
      ['g3', []],
      ['g1', ['f1', 'f2']],
    ]);
  });

  it('adds, changes and removes a rule of an element both keep, and an action or check like any element', () => {
    const yours = formOf(
      [
        group('g1', [
          field('f1', 'Name', {
            rules: [
              { key: 'r1', effect: 'hide', conditions: [] },
              { key: 'r2', effect: 'show', conditions: [] },
            ],
          }),
          field('f2', 'Email'),
        ]),
      ],
      { actions: [], validationRules: [{ key: 'v1', field: 'f2', conditions: [] }] },
    );
    const differences = all([
      change({ kind: 'rule', key: 'r1', parentKey: 'f1', change: 'changed' }),
      change({ kind: 'rule', key: 'r2', parentKey: 'f1', change: 'added' }),
      change({ kind: 'action', key: 'a1', change: 'removed' }),
      change({ kind: 'validationRule', key: 'v1', change: 'added' }),
    ]);
    const result = merged(theirs, yours, differences, new Set(differences.map((one) => one.id)));
    expect(result.pages[0]?.groups[0]?.fields[0]?.rules).toEqual([
      { key: 'r1', effect: 'hide', conditions: [] },
      { key: 'r2', effect: 'show', conditions: [] },
    ]);
    expect([result['actions'], result['validationRules']]).toEqual([[], [{ key: 'v1', field: 'f2', conditions: [] }]]);
  });

  it('keeps work from both sides: what theirs added stays beside what yours changed', () => {
    const both = formOf([group('g1', [field('f1', 'Name'), field('f2', 'Email'), field('f8', 'Company')])]);
    const yours = formOf([group('g1', [field('f1', 'Full name'), field('f2', 'Email')])]);
    // Theirs added Company, yours renamed Name: seen from theirs, Company is one yours lacks.
    const differences = differencesOf(
      [
        change({ kind: 'field', key: 'f1', change: 'changed', properties: LABEL }),
        change({ kind: 'field', key: 'f8', parentKey: 'g1', change: 'removed' }),
      ],
      {
        theirs: [change({ kind: 'field', key: 'f8', change: 'added' })],
        yours: [change({ kind: 'field', key: 'f1', change: 'changed', properties: LABEL })],
      },
      names,
    );
    const fromYours = new Set(differences.filter((one) => startingSide(one) === 'yours').map((one) => one.id));
    expect(labels(merged(both, yours, differences, fromYours))).toEqual([['Full name', 'Email', 'Company']]);
  });
});
