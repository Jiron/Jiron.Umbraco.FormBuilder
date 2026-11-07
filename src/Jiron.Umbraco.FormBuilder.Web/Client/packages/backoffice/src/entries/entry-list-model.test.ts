// What the Entries view says of its entries and filters (ROADMAP 11.1, IA 6.1, ADR-0207): names, action status, the chips
// of each filter, the columns a filter fixes, which bulk actions apply, the request a list sends, and the sort's turn;
// and what the Inbox and Spam add (IA 6.1, 6.2, ADR-0209): the Form chip, a set as each place applies it, the forms'
// fields going with their one form, actions weighed per form, and the Inbox's own order.
import { beforeAll, describe, expect, it } from 'vitest';
import type { EntryListFieldResponseModel } from '../api';
import { flatten } from '../designer/actions/action-words';
import entriesEn from '../lang/entries.en';
import {
  bulkActions,
  bulkActionsOf,
  chipsOf,
  cleanFilter,
  conditionText,
  emptyFilter,
  filterOfSet,
  fixedColumns,
  isFiltered,
  isTyping,
  leftOut,
  nextSort,
  oneForm,
  splitTicked,
  withForms,
  receivedText,
  requestOf,
  rowLabel,
  rowName,
  runsText,
} from './entry-list-model';

let term: (key: string, ...args: unknown[]) => string;

beforeAll(() => {
  const words = flatten('en', entriesEn) as Record<string, unknown>;
  term = (key, ...args) => {
    const word = words[key];
    return typeof word === 'function' ? String((word as (...values: unknown[]) => unknown)(...args)) : String(word);
  };
});

const field = (
  key: string,
  label: string,
  more: Partial<EntryListFieldResponseModel> = {},
): EntryListFieldResponseModel => ({
  key,
  label,
  alias: key,
  type: 'text',
  kind: 'text',
  sensitive: false,
  removed: false,
  column: true,
  sorts: true,
  tests: ['oneOf', 'noneOf', 'startsWith', 'answered', 'notAnswered'],
  options: [],
  ...more,
});

const runs = (
  more: Partial<{ delivered: number; failed: number; uncertain: number; waiting: number; skipped: number }>,
) => ({
  delivered: 0,
  failed: 0,
  uncertain: 0,
  waiting: 0,
  skipped: 0,
  ...more,
});

describe('a row', () => {
  it('is named by its title, or by its number when the form has none', () => {
    expect(rowName({ title: 'Ada Lovelace', number: 7 }, term)).toBe('Ada Lovelace');
    expect(rowName({ title: null, number: 1042 }, term)).toBe('#1042');
    expect(rowLabel({ title: null, number: 1042 }, term)).toBe('entry 1042');
  });

  it('says its runs, the worst first, and nothing without runs', () => {
    expect(runsText(runs({}), term)).toBeNull();
    expect(runsText(runs({ delivered: 3 }), term)).toEqual({ text: '3/3 delivered', tone: 'default' });
    expect(runsText(runs({ delivered: 2, skipped: 1 }), term)?.text).toBe('2/2 delivered');
    expect(runsText(runs({ delivered: 1, failed: 1, uncertain: 1 }), term)).toEqual({
      text: '1 failed',
      tone: 'danger',
    });
    expect(runsText(runs({ delivered: 1, uncertain: 2 }), term)).toEqual({ text: '2 uncertain', tone: 'warning' });
    expect(runsText(runs({ delivered: 1, waiting: 2 }), term)?.text).toBe('2 waiting');
    expect(runsText(runs({ skipped: 2 }), term)).toEqual({ text: 'Not run', tone: 'muted' });
  });

  it('says when it was received: today with the time, another day by its date', () => {
    const now = new Date(2026, 10, 7, 12, 0);
    expect(receivedText(new Date(2026, 10, 7, 7, 48).toISOString(), now, 'en-GB', term)).toBe('Today, 07:48');
    expect(receivedText(new Date(2026, 10, 6, 18, 2).toISOString(), now, 'en-GB', term)).toBe('Yesterday, 18:02');
    expect(receivedText(new Date(2026, 9, 20, 9, 30).toISOString(), now, 'en-GB', term)).toBe('20 Oct, 09:30');
  });
});

describe('the chips', () => {
  const fields = new Map([
    ['position', field('position', 'Position')],
    ['amount', field('amount', 'Amount', { kind: 'number', tests: ['between', 'answered', 'notAnswered'] })],
    ['day', field('day', 'Day', { kind: 'date', tests: ['between', 'answered', 'notAnswered'] })],
  ]);

  it('say each part of the filter once, in the order of the menu', () => {
    const chips = chipsOf(
      {
        states: ['rejected', 'submitted'],
        forms: [],
        received: { preset: 'last7Days' },
        actions: 'failed',
        fields: [
          { field: 'position', test: 'oneOf', values: ['Frontend developer', 'Accountant', 'Designer'] },
          { field: 'amount', test: 'between', values: [], min: 5, max: 1500 },
        ],
      },
      fields,
      'en-GB',
      term,
    );

    expect(chips.map((chip) => chip.text)).toEqual([
      'State: Waiting for approval or Rejected',
      'Received: Last 7 days',
      'Actions: Failed',
      'Position: Frontend developer, Accountant or Designer',
      'Amount: 5 to 1,500',
    ]);
    expect(chips.map((chip) => chip.index)).toEqual([undefined, undefined, undefined, 0, 1]);
  });

  it('say a range open at either end, each test in words, and a ticked option by its label', () => {
    expect(conditionText({ field: 'amount', test: 'between', values: [], min: 5 }, 'en-GB', term)).toBe('from 5');
    expect(conditionText({ field: 'day', test: 'between', values: [], to: '2026-03-01' }, 'en-GB', term)).toBe(
      'until 1 Mar 2026',
    );
    expect(conditionText({ field: 'position', test: 'noneOf', values: ['A'] }, 'en-GB', term)).toBe('not A');
    expect(conditionText({ field: 'position', test: 'startsWith', values: ['Fr'] }, 'en-GB', term)).toBe(
      'starts with Fr',
    );
    expect(conditionText({ field: 'position', test: 'notAnswered', values: [] }, 'en-GB', term)).toBe('not answered');
    const options = [
      { value: 'p1', label: 'Frontend developer' },
      { value: 'p3', label: 'Accountant' },
    ];
    expect(conditionText({ field: 'position', test: 'oneOf', values: ['p3', 'p9'] }, 'en-GB', term, options)).toBe(
      'Accountant or p9',
    );
    expect(conditionText({ field: 'position', test: 'noneOf', values: ['p1'] }, 'en-GB', term, options)).toBe(
      'not Frontend developer',
    );
    expect(
      chipsOf(
        { states: [], forms: [], fields: [], received: { from: '2026-10-01', to: '2026-10-15' } },
        fields,
        'en-GB',
        term,
      )[0]?.text,
    ).toBe('Received: 1 Oct 2026 to 15 Oct 2026');
  });
});

describe('the columns a filter fixes', () => {
  it('hide the state for one state and a field fixed to one value; a range keeps its column', () => {
    const fixed = fixedColumns({
      states: ['approved'],
      forms: [],
      fields: [
        { field: 'position', test: 'oneOf', values: ['Accountant'] },
        { field: 'team', test: 'oneOf', values: ['A', 'B'] },
        { field: 'agreed', test: 'checked', values: [] },
        { field: 'amount', test: 'between', values: [], min: 1 },
      ],
    });

    expect(fixed.state).toBe(true);
    expect([...fixed.fields]).toEqual(['position', 'agreed']);
    expect(fixedColumns({ states: ['approved', 'rejected'], forms: [], fields: [] }).state).toBe(false);
  });
});

describe('the bulk bar', () => {
  const everything = ['editEntries', 'deleteEntries', 'manageActions'];

  it('offers what applies to one ticked entry at least, in its order, as the user may', () => {
    expect(bulkActions(['submitted'], everything)).toEqual(['approve', 'reject', 'markSpam', 'delete', 'rerun']);
    expect(bulkActions(['approved', 'spam'], everything)).toEqual(['reject', 'markSpam', 'notSpam', 'delete', 'rerun']);
    expect(bulkActions(['unverified'], everything)).toEqual(['markSpam', 'delete']);
    expect(bulkActions(['submitted'], ['deleteEntries'])).toEqual(['delete']);
    expect(bulkActions(['submitted'], [])).toEqual([]);
  });

  it('counts the entries an action leaves out', () => {
    expect(leftOut('approve', ['submitted', 'approved', 'rejected', 'spam'])).toBe(2);
    expect(leftOut('delete', ['spam'])).toBe(0);
  });

  it('weighs each ticked entry by what its own form lets the user do, and Spam offers its two only', () => {
    const viewer = { state: 'submitted', permissions: ['viewEntries'] };
    const handler = { state: 'submitted', permissions: ['viewEntries', 'editEntries'] };
    const spam = { state: 'spam', permissions: everything };

    expect(bulkActionsOf([viewer])).toEqual([]);
    expect(bulkActionsOf([viewer, handler])).toEqual(['approve', 'reject', 'markSpam']);
    expect(bulkActionsOf([spam, handler], 'spam')).toEqual(['notSpam', 'delete']);
    // Re-run applies to the waiting entry, but only the spam entry's form lets the user run actions: not offered.
    expect(bulkActionsOf([spam, handler])).toEqual(['approve', 'reject', 'markSpam', 'notSpam', 'delete']);

    const split = splitTicked('approve', [viewer, handler, { ...handler, state: 'approved' }]);
    expect(split.acting).toEqual([handler]);
    expect([split.leftOut, split.notAllowed]).toEqual([1, 1]);
  });
});

describe('the Inbox and Spam', () => {
  it('say the forms filtered by in one chip, by name once known, before the other chips', () => {
    const filter = { ...emptyFilter(), forms: ['f1', 'f2'], states: ['approved'] };
    const names = new Map([
      ['f1', 'Contact'],
      ['f2', 'Job application'],
    ]);

    expect(chipsOf(filter, new Map(), 'en-GB', term, names).map((chip) => [chip.kind, chip.text])).toEqual([
      ['form', 'Form: Contact or Job application'],
      ['state', 'State: Approved'],
    ]);
    expect(chipsOf(filter, new Map(), 'en-GB', term)[0]?.text).toBe('Form: 2 forms');
    expect(isFiltered({ ...emptyFilter(), forms: ['f1'] })).toBe(true);
  });

  it("let a form's fields go once it is no longer the only form chosen, and keep them while it is", () => {
    const one = {
      ...emptyFilter(),
      forms: ['f1'],
      fields: [{ field: 'position', test: 'answered', values: [] }],
      sort: { field: 'position', descending: false },
      actions: 'failed',
    };

    expect(oneForm(one)).toBe('f1');
    expect(withForms(one, ['f1', 'f1'])).toEqual(one);
    expect(withForms(one, ['f1', 'f2'])).toEqual({ ...emptyFilter(), forms: ['f1', 'f2'], actions: 'failed' });
    expect(withForms({ ...one, sort: { field: null, descending: false } }, [])).toEqual({
      ...emptyFilter(),
      actions: 'failed',
      sort: { field: null, descending: false },
    });
    expect(oneForm(withForms(one, ['f2', 'f3']))).toBeUndefined();
  });

  it('apply a set as each place may: its form chosen, no Spam in the Inbox, only Spam in Spam', () => {
    const set = { ...emptyFilter(), states: ['approved', 'spam'], received: { preset: 'today' } };

    expect(filterOfSet(set, 'inbox', 'f1')).toEqual({ ...set, forms: ['f1'], states: ['approved'] });
    expect(filterOfSet(set, 'spam', null)).toEqual({ ...set, states: [] });
    expect(filterOfSet({ ...set, forms: ['f2', 'f3'] }, 'form', 'f1')).toEqual(set);
    expect(filterOfSet({ ...set, forms: ['f2', 'f3'] }, 'inbox', null).forms).toEqual(['f2', 'f3']);
  });

  it('start the Inbox newest first at the first click on Received, as its own order is unseen first', () => {
    expect(nextSort(undefined, null, false)).toEqual({ field: null, descending: true });
    expect(nextSort({ field: null, descending: true }, null, false)).toEqual({ field: null, descending: false });
    expect(nextSort(undefined, 'amount', false)).toEqual({ field: 'amount', descending: false });
  });
});

describe('the request', () => {
  it('sends only what the filter sets, counts on the first page, and goes on after a cursor', () => {
    const filter = { ...emptyFilter(), search: '  ada ', sort: { field: null, descending: false } };

    expect(requestOf(filter, ['position'], null)).toEqual({
      filter: { states: [], forms: [], fields: [], search: 'ada', sort: { field: null, descending: false } },
      columns: ['position'],
      count: true,
    });
    expect(requestOf(emptyFilter(), [], 'cursor')).toEqual({
      filter: { states: [], forms: [], fields: [] },
      columns: [],
      after: 'cursor',
      count: false,
    });
    expect(cleanFilter({ ...emptyFilter(), search: '   ' })).toEqual({ states: [], forms: [], fields: [] });
    expect(isFiltered(emptyFilter())).toBe(false);
    expect(isFiltered({ ...emptyFilter(), search: 'ada' })).toBe(false);
    expect(isFiltered({ ...emptyFilter(), actions: 'failed' })).toBe(true);
  });

  it('turns a sorted column, and starts another in its natural order', () => {
    expect(nextSort(undefined, null)).toEqual({ field: null, descending: false });
    expect(nextSort({ field: null, descending: false }, null)).toEqual({ field: null, descending: true });
    expect(nextSort({ field: null, descending: true }, 'amount')).toEqual({ field: 'amount', descending: false });
    expect(nextSort({ field: 'amount', descending: false }, 'amount')).toEqual({ field: 'amount', descending: true });
    expect(nextSort({ field: 'amount', descending: true }, null)).toEqual({ field: null, descending: true });
  });
});

describe('the keyboard', () => {
  it('leaves keys typed in a box to the box', () => {
    expect(isTyping(document.createElement('input'))).toBe(true);
    expect(isTyping(document.createElement('textarea'))).toBe(true);
    expect(isTyping(document.createElement('tr'))).toBe(false);
    expect(isTyping(null)).toBe(false);
  });
});
