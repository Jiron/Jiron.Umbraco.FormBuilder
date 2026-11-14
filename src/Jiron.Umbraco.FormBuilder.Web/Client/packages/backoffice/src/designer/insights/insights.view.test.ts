// A form's Insights view (ROADMAP 11.5, IA 5.5, ADR-0211): it reads the last 30 days by day; it shows the numbers with
// each change in words once the comparison is on, the entries per day, who reaches each page with the median time,
// the fields that stop visitors with the kind of error, the website pages and the hours; a box switches to its table
// and the choice is kept; a date range and Group by read the report again; insights off for the site or the form say
// where to turn them on and nothing else; a form with no visit says when numbers appear; a failed read offers Try again;
// while another range loads, the numbers stay dimmed and busy, and a read that fails then says so in their place.
// The switch Overview and Answers (11.5b, ADR-0212) shows the Answers in the same range, without Compare or Group by,
// with the entries list's filter, its dialog and its chips, and one switch for every answer's table, kept for the user;
// a range chosen there reads the report once the Overview shows again, and the answers are there with insights off.
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { EntryListFieldResponseModel, InsightReportResponseModel } from '../../api';
import type { InsightAnswersElement } from '../../insights/insight-answers.element';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { FORM_WORKSPACE_CONTEXT } from '../constants';

/** What the API answers, in turn, and what was asked of it. */
const api = vi.hoisted(() => ({
  reports: [] as unknown[],
  asked: [] as { form: string; span: unknown; groupBy: string; compare: unknown }[],
  kept: [] as string[][],
  /** What a save waits for before it answers. */
  hold: Promise.resolve() as Promise<unknown>,
  answersAsked: [] as { span: unknown; filter: unknown }[],
  /** The form's fields for the Answers' filter, in turn; undefined where they can't be read. */
  views: [] as ({ fields: EntryListFieldResponseModel[] } | undefined)[],
  viewsAsked: [] as string[],
  /** What the filter's dialog answers, and what it was opened with. */
  dialog: undefined as unknown,
  opened: [] as { token: string; data: unknown }[],
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: { toString(): string }, args?: { data?: unknown }) => {
    api.opened.push({ token: token.toString(), data: args?.data });
    if (api.dialog === undefined) throw new Error('closed');
    return api.dialog;
  },
}));

vi.mock('../../entries/entries-api', async (original) => ({
  ...(await original<typeof import('../../entries/entries-api')>()),
  readView: async (_host: unknown, form: string) => {
    api.viewsAsked.push(form);
    const view = api.views.length > 1 ? api.views.shift() : api.views[0];
    return view ? { columns: [], filterSets: [], ...view } : undefined;
  },
}));

vi.mock('../../insights/insight-api', async (original) => ({
  ...(await original<typeof import('../../insights/insight-api')>()),
  readReport: async (_host: unknown, form: string, span: unknown, groupBy: string, compare: unknown) => {
    api.asked.push({ form, span, groupBy, compare });
    return api.reports.length > 1 ? api.reports.shift() : api.reports[0];
  },
  readAnswers: async (_host: unknown, _form: string, span: unknown, filter: unknown) => {
    api.answersAsked.push({ span, filter });
    return { entries: 3, fields: [] };
  },
  keepTables: async (_host: unknown, tables: readonly string[]) => {
    api.kept.push([...tables]);
    await api.hold;
    return true;
  },
}));

const { FormInsightsViewElement } = await import('./insights.view');

const totals = (views: number, starts: number, entries: number, spam = 0) => ({
  views,
  starts,
  entries,
  spam,
  conversion: views > 0 ? entries / views : null,
});

const report = (more: Partial<InsightReportResponseModel> = {}): InsightReportResponseModel => ({
  siteOn: true,
  formOn: true,
  totals: totals(4812, 1307, 389, 12),
  before: null,
  periods: [
    { start: '2026-11-06', entries: 7, before: null },
    { start: '2026-11-07', entries: 3, before: null },
  ],
  pages: [{ key: 'p2', name: 'Your message', reached: 601 }],
  medianCompletion: '180',
  fields: [
    { key: 'f1', label: 'Email address', stops: 212, kind: 'invalid' },
    { key: 'f2', label: null, stops: 25, kind: 'required' },
  ],
  origins: [{ key: 'c1', name: 'Contact', entries: 214 }],
  hours: Array.from({ length: 24 }, (_, hour) => (hour === 10 ? 38 : 0)),
  members: 0,
  memberEntries: 0,
  membersBefore: null,
  timeZone: 'Europe/Berlin',
  tables: [],
  ...more,
});

async function viewOf(...reports: unknown[]) {
  api.reports = reports;
  api.asked = [];
  api.kept = [];
  api.hold = Promise.resolve();
  api.answersAsked = [];
  api.views = [{ fields: [TOPIC] }];
  api.viewsAsked = [];
  api.dialog = undefined;
  api.opened = [];
  const host = testHost();
  host.provideContext(FORM_WORKSPACE_CONTEXT, {
    unique: new UmbBasicState<string | undefined>('form1').asObservable(),
    getEntityType: () => 'jfb-form',
    getUnique: () => 'form1',
    getHostElement: () => host,
  } as never);
  const view = new FormInsightsViewElement();
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('dl.stats, uui-box .line, [role="alert"]')).not.toBeNull());
  await settle(view);
  const root = shadow(view);
  const box = (heading: string) =>
    must(
      all(view, 'uui-box').find((each) => each.getAttribute('headline') === heading),
      heading,
    );
  return { view, root, box };
}

/** A field the Answers' filter offers, with its options. */
const TOPIC: EntryListFieldResponseModel = {
  key: 'topic',
  label: 'Topic',
  alias: 'topic',
  type: 'radio',
  kind: 'choice',
  sensitive: false,
  removed: false,
  column: true,
  sorts: true,
  tests: ['oneOf', 'noneOf'],
  options: [{ value: 'sales', label: 'Sales' }],
};

/** Shows the view's Answers, with what they need for their filter. */
async function answersIn(view: HTMLElement): Promise<InsightAnswersElement> {
  must(
    all<HTMLButtonElement>(view, '.modes button').find((each) => text(each) === 'Answers'),
    'Answers',
  ).click();
  await vi.waitFor(() => expect(shadow(view).querySelector('jfb-insight-answers')).not.toBeNull());
  await settle(view as HTMLElement & { updateComplete: Promise<unknown> });
  return must(shadow(view).querySelector('jfb-insight-answers'), 'the answers');
}

/** A table row's cells, each as its text, apart by one space. */
const cells = (row: Element): string => [...row.children].map((cell) => text(cell)).join(' ');

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the Insights view', () => {
  it('reads the last 30 days by day, and shows the numbers, the charts and the lines under them', async () => {
    const { root, box } = await viewOf(report());
    expect(api.asked[0]).toMatchObject({ form: 'form1', groupBy: 'day', compare: null });
    expect(all(root.host, 'dl.stats .stat').map((stat) => text(stat))).toEqual([
      'Views 4,812',
      'Starts 1,307',
      'Entries 389 and 12 caught as spam',
      'Conversion 8.1%',
    ]);
    expect(text(root.querySelector('.toolbar uui-button'))).toBe('Last 30 days');
    expect(root.querySelector('.toolbar uui-button')?.getAttribute('label'), 'its name holds what it shows').toBe(
      'Date range: Last 30 days',
    );
    const entries = box('Entries per day').querySelector('jfb-insight-columns');
    expect(entries?.values.map((value) => value.value)).toEqual([7, 3]);
    expect(entries?.label).toMatch(/^Entries per day: 2 bars, the most Fri, Nov 6 \(7\)/);
    const funnel = box('Who reaches each page').querySelector('jfb-insight-bars');
    expect(funnel?.values.map((value) => `${value.name} ${value.said}`)).toEqual([
      'Started 1,307',
      'Your message 601',
      'Sent 389',
    ]);
    expect(text(box('Who reaches each page').querySelector('.note'))).toBe(
      'Half of those who send it finish within 3 minutes.',
    );
    const fields = box('Fields that stop visitors most').querySelector('jfb-insight-bars');
    expect(fields?.values.map((value) => `${value.name}: ${value.said}`)).toEqual([
      'Email address: 212, 16.2% of starts, mostly the format',
      'A field no longer on the form: 25, 1.9% of starts, mostly left empty',
    ]);
    expect(box('Top website pages').querySelector('jfb-insight-bars')?.values[0]?.said).toBe('214 entries');
    expect(text(box('Entries by hour of day').querySelector('.note'))).toBe('Time zone: Europe/Berlin');
    expect(all(root.host, 'uui-box').some((each) => each.getAttribute('headline') === 'Members')).toBe(false);
  });

  it('says each change from the period before in words once the comparison is on', async () => {
    const { root, view } = await viewOf(
      report(),
      report({ before: totals(4540, 1257, 350), membersBefore: 36, members: 41, memberEntries: 58 }),
    );
    const toggle = must(root.querySelector<HTMLInputElement>('uui-toggle'), 'the switch');
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(api.asked).toHaveLength(2));
    await settle(view);
    expect(api.asked[1]?.compare).toEqual(
      expect.objectContaining({ from: expect.any(String), to: expect.any(String) }),
    );
    expect(all(root.host, 'dl.stats .change').map((change) => text(change))).toEqual([
      'up 6% from 4,540',
      'up 4% from 1,257',
      'up 11% from 350',
      'and 12 caught as spam',
      'up 0.4 points from 7.7%',
    ]);
    const members = must(
      all(root.host, 'uui-box').find((each) => each.getAttribute('headline') === 'Members'),
      'members',
    );
    expect(text(members)).toBe(
      '41 members sent 58 entries, up from 36 members. The other 331 came from visitors not signed in.',
    );
  });

  it('switches a box to its table and keeps the choice, and shows a box as its table where it was kept', async () => {
    const { box, view } = await viewOf(report(), report({ tables: ['hours'] }));
    const fields = box('Fields that stop visitors most');
    must(fields.querySelector<HTMLElement>('uui-button[slot="header-actions"]'), 'the switch').click();
    await settle(view);
    expect(api.kept).toEqual([['fields']]);
    expect([...fields.querySelectorAll('tbody tr')].map(cells)).toEqual([
      'Email address 212 16.2% the format',
      'A field no longer on the form 25 1.9% left empty',
    ]);
    expect(fields.querySelector('uui-button[slot="header-actions"]')?.getAttribute('label')).toBe('Show as chart');
    expect(
      [...fields.querySelectorAll('thead th')].map((cell) => cell.classList.contains('numeric')),
      'the kind that stops most is words, not a number',
    ).toEqual([false, true, true, false]);

    const again = await viewOf(report({ tables: ['hours'] }));
    const hours = again.box('Entries by hour of day');
    expect([...hours.querySelectorAll('tbody tr')]).toHaveLength(24);
    expect(cells(must([...hours.querySelectorAll('tbody tr')][10], 'ten'))).toBe('10:00 38');
  });

  it('saves two quick switches one after the other, so the last choice is the one kept', async () => {
    const { box, view } = await viewOf(report());
    let release: (value: unknown) => void = () => undefined;
    api.hold = new Promise((resolve) => (release = resolve));
    const switchOf = (heading: string) =>
      must(box(heading).querySelector<HTMLElement>('uui-button[slot="header-actions"]'), heading);
    switchOf('Fields that stop visitors most').click();
    switchOf('Entries by hour of day').click();
    await settle(view);
    expect(api.kept, 'the second waits for the first').toEqual([['fields']]);
    release(undefined);
    await vi.waitFor(() => expect(api.kept).toEqual([['fields'], ['fields', 'hours']]));
  });

  it('keeps a box switched while a report is on its way, whatever the report says of the tables', async () => {
    const { root, box, view } = await viewOf(report(), report({ tables: [] }));
    must(
      box('Fields that stop visitors most').querySelector<HTMLElement>('uui-button[slot="header-actions"]'),
      'the switch',
    ).click();
    must(
      all<HTMLButtonElement>(root.host, '.menu button').find((each) => text(each) === 'Last 7 days'),
      'last 7 days',
    ).click();
    await vi.waitFor(() => expect(api.asked).toHaveLength(2));
    await settle(view);
    expect(box('Fields that stop visitors most').querySelector('table'), 'still its table').not.toBeNull();
  });

  it("turns the range button's chevron while its menu is open", async () => {
    const { root, view } = await viewOf(report());
    const chevron = () => root.querySelector('.toolbar uui-symbol-expand')?.hasAttribute('open');
    expect(chevron()).toBe(false);
    const menu = must(root.querySelector('#range'), 'the menu');
    menu.dispatchEvent(Object.assign(new Event('toggle'), { newState: 'open' }));
    await settle(view);
    expect(chevron()).toBe(true);
    menu.dispatchEvent(Object.assign(new Event('toggle'), { newState: 'closed' }));
    await settle(view);
    expect(chevron()).toBe(false);
  });

  it("ends each range on the UTC day the stats count, not the user's own", async () => {
    const early = Date.UTC(2026, 10, 7, 0, 30);
    const instants = [early, Date.UTC(2026, 10, 7, 23, 30)];
    const now = instants.find((at) => new Date(at).getDate() !== new Date(at).getUTCDate()) ?? early;
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(now);
    try {
      const { root, view } = await viewOf(report());
      expect(api.asked[0]?.span).toEqual({ from: '2026-10-09', to: '2026-11-07' });
      must(
        all<HTMLButtonElement>(root.host, '.menu button').find((each) => text(each) === 'Last 7 days'),
        'last 7 days',
      ).click();
      await vi.waitFor(() => expect(api.asked).toHaveLength(2));
      await settle(view);
      expect(api.asked[1]?.span).toEqual({ from: '2026-11-01', to: '2026-11-07' });
    } finally {
      vi.useRealTimers();
    }
  });

  it('reads the report again for another date range and another grouping', async () => {
    const { root, view } = await viewOf(report());
    const button = (name: string) =>
      must(
        all<HTMLButtonElement>(root.host, '.menu button').find((each) => text(each) === name),
        name,
      );
    button('Last 7 days').click();
    await vi.waitFor(() => expect(api.asked).toHaveLength(2));
    await settle(view);
    expect(text(root.querySelector('.toolbar uui-button'))).toBe('Last 7 days');
    button('Week').click();
    await vi.waitFor(() => expect(api.asked).toHaveLength(3));
    expect(api.asked[2]?.groupBy).toBe('week');
    button('Choose dates\u2026').click();
    await settle(view);
    const [from, to] = all<HTMLInputElement>(root.host, '.dates input');
    must(from, 'from').value = '2026-11-08';
    must(from, 'from').dispatchEvent(new Event('input'));
    must(to, 'to').value = '2026-11-01';
    must(to, 'to').dispatchEvent(new Event('input'));
    await settle(view);
    must(root.querySelector<HTMLElement>('.dates uui-button'), 'show').click();
    await settle(view);
    expect(text(root.querySelector('.dates .why'))).toBe(
      'Choose a first day on or before the last, at most a year apart.',
    );
    expect(root.querySelector('.dates .why')?.getAttribute('role'), 'said as it appears').toBe('alert');
    expect(api.asked).toHaveLength(3);
  });

  it('keeps the numbers busy while another range loads, and says so in their place when that read fails', async () => {
    let answer: (value: unknown) => void = () => undefined;
    const waiting = new Promise((resolve) => (answer = resolve));
    const { root, view } = await viewOf(report(), waiting);
    must(
      all<HTMLButtonElement>(root.host, '.menu button').find((each) => text(each) === 'Last 7 days'),
      'last 7 days',
    ).click();
    await vi.waitFor(() => expect(api.asked).toHaveLength(2));
    await settle(view);
    expect(root.querySelector('.report')?.getAttribute('aria-busy')).toBe('true');
    expect(text(root.querySelector('[role="status"]'))).toBe('Loading insights');
    answer(undefined);
    await vi.waitFor(() => expect(root.querySelector('[role="alert"]')).not.toBeNull());
    expect(text(root.querySelector('[role="alert"]'))).toBe("Insights couldn't be loaded.");
    expect(root.querySelector('dl.stats')).toBeNull();
  });

  it('says where to turn insights on when they are off, says when numbers appear, and offers Try again', async () => {
    expect(text((await viewOf(report({ siteOn: false }))).root.querySelector('uui-box'))).toBe(
      'Insights are off for this site. An administrator can turn them on in Privacy \u203a Data collection.',
    );
    const off = await viewOf(report({ formOn: false }));
    expect(text(off.root.querySelector('uui-box'))).toBe(
      'Insights are off for this form. Turn them on in its Settings view, under Privacy.',
    );
    expect(off.root.querySelector('dl.stats')).toBeNull();
    const empty = await viewOf(report({ totals: totals(0, 0, 0), periods: [], fields: [], origins: [] }));
    expect(text(empty.root.querySelector('uui-box'))).toBe(
      'No data yet. Numbers appear after the first visit to a page with this form.',
    );
    const failed = await viewOf(undefined);
    expect(text(failed.root.querySelector('[role="alert"]'))).toBe("Insights couldn't be loaded.");
  });
});

describe('the Answers of the Insights view', () => {
  it('switch in the same range, read the fields the filter offers, and have no Compare or Group by', async () => {
    const { root, view } = await viewOf(report());
    const mode = (name: string) =>
      must(
        all<HTMLButtonElement>(root.host, '.modes button').find((each) => text(each) === name),
        name,
      );
    expect(root.querySelector('.modes')?.getAttribute('aria-label')).toBe('View');
    expect(mode('Overview').getAttribute('aria-pressed')).toBe('true');
    const answers = await answersIn(view);
    expect(mode('Answers').getAttribute('aria-pressed')).toBe('true');
    expect(mode('Overview').getAttribute('aria-pressed')).toBe('false');
    expect(api.viewsAsked).toEqual(['form1']);
    expect(answers.form).toBe('form1');
    expect(answers.span).toEqual(api.asked[0]?.span);
    expect(answers.filter).toEqual({ states: [], fields: [], forms: [] });
    expect(answers.tables).toBe(false);
    expect(all(root.host, 'uui-toggle').map((each) => each.getAttribute('label'))).toEqual(['Show as tables']);
    expect(root.querySelector('#range .group-by'), "Group by is the Overview's").toBeNull();
    expect(root.querySelector('dl.stats')).toBeNull();
    expect(all(root.host, '#filter-menu button, #filter-menu h3').map((each) => text(each))).toEqual([
      'State',
      'Actions',
      'Fields',
      'Topic',
    ]);
    mode('Overview').click();
    await settle(view);
    expect(root.querySelector('dl.stats')).not.toBeNull();
    expect(root.querySelector('jfb-insight-answers')).toBeNull();
    expect(all(root.host, 'uui-toggle').map((each) => each.getAttribute('label'))).toEqual([
      'Compare with the previous period',
    ]);
    expect(api.asked, 'the same range needs no new report').toHaveLength(1);
    await answersIn(view);
    expect(api.viewsAsked, 'the fields are read once').toEqual(['form1']);
  });

  it("filter with the entries list's dialog, without Spam, and its chips change and take away each part", async () => {
    const { root, view } = await viewOf(report());
    const answers = await answersIn(view);
    const item = (name: string) =>
      must(
        all<HTMLButtonElement>(root.host, '#filter-menu button').find((each) => text(each) === name),
        name,
      );
    api.dialog = { filter: { states: ['approved'], fields: [], forms: [] } };
    item('State').click();
    await settle(view);
    expect(api.opened[0]).toEqual({
      token: 'Jiron.FormBuilder.Modal.EntryFilter',
      data: {
        part: 'state',
        filter: { states: [], fields: [], forms: [] },
        states: ['unverified', 'submitted', 'approved', 'rejected'],
      },
    });
    expect(answers.filter.states).toEqual(['approved']);
    const condition = { field: 'topic', test: 'oneOf', values: ['sales'] };
    api.dialog = { filter: { states: ['approved'], fields: [condition], forms: [] } };
    item('Topic').click();
    await settle(view);
    expect(api.opened[1]?.data).toMatchObject({ part: 'field', field: TOPIC });
    expect(root.querySelector('.chips')?.getAttribute('aria-label')).toBe('Filters');
    expect(all(root.host, '.chips .chip-text').map((each) => text(each))).toEqual(['State: Approved', 'Topic: Sales']);
    expect(answers.filter.fields).toEqual([condition]);

    api.dialog = undefined;
    must(all<HTMLButtonElement>(root.host, '.chips .chip-text')[1], 'the field chip').click();
    await settle(view);
    expect(api.opened[2]?.data, 'the chip opens its own part').toMatchObject({ part: 'field', field: TOPIC, index: 0 });
    must(all<HTMLButtonElement>(root.host, '.chips .chip-remove')[0], 'the state chip').click();
    await settle(view);
    expect(answers.filter).toEqual({ states: [], fields: [condition], forms: [] });
    expect(api.answersAsked.at(-1)?.filter).toEqual(answers.filter);
    must(root.querySelector<HTMLElement>('.chips uui-button'), 'Clear filters').click();
    await settle(view);
    expect(answers.filter).toEqual({ states: [], fields: [], forms: [] });
    expect(root.querySelector('.chips')).toBeNull();
  });

  it('read the report for a range chosen in the Answers once the Overview shows again', async () => {
    const { root, view } = await viewOf(report());
    const answers = await answersIn(view);
    must(
      all<HTMLButtonElement>(root.host, '#range button').find((each) => text(each) === 'Last 7 days'),
      'last 7 days',
    ).click();
    await settle(view);
    expect(api.asked).toHaveLength(1);
    expect(answers.span).not.toEqual(api.asked[0]?.span);
    const span = answers.span;
    must(
      all<HTMLButtonElement>(root.host, '.modes button').find((each) => text(each) === 'Overview'),
      'Overview',
    ).click();
    await vi.waitFor(() => expect(api.asked).toHaveLength(2));
    expect(api.asked[1]?.span).toEqual(span);
  });

  it('show every answer as its table with one switch, kept for the user', async () => {
    const { root, view } = await viewOf(report({ tables: ['hours'] }));
    const answers = await answersIn(view);
    const toggle = must(root.querySelector<HTMLInputElement>('uui-toggle'), 'Show as tables');
    toggle.dispatchEvent(new Event('change'));
    await settle(view);
    expect(api.kept).toEqual([['hours', 'answers']]);
    expect(answers.tables).toBe(true);
  });

  it('are there while insights are off, as they come from the entries', async () => {
    const { view } = await viewOf(report({ siteOn: false, formOn: false }));
    const answers = await answersIn(view);
    expect(answers.form).toBe('form1');
  });

  it("offer Try again where the form's fields could not be read", async () => {
    const { root } = await viewOf(report());
    api.views = [undefined, { fields: [TOPIC] }];
    must(
      all<HTMLButtonElement>(root.host, '.modes button').find((each) => text(each) === 'Answers'),
      'Answers',
    ).click();
    await vi.waitFor(() => expect(root.querySelector('[role="alert"]')).not.toBeNull());
    expect(text(root.querySelector('[role="alert"] span'))).toBe("The answers couldn't be loaded.");
    expect(root.querySelector('#filter-menu')).toBeNull();
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(root.querySelector('jfb-insight-answers')).not.toBeNull());
    expect(api.viewsAsked).toEqual(['form1', 'form1']);
  });
});
