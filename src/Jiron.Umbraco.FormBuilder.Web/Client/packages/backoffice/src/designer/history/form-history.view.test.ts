// The form's History view (ROADMAP 10.3a, IA 5.6, ADR-0196): the published versions newest first, each with when and by
// whom, what changed, its comment and pin, the live one's chip; the draft saves on request, muted, the current draft
// first; Show more; a pin and a comment from a row's "..."; two ticked versions compared, the older first, and a version
// compared with the current draft; a read that fails; and a save that adds a version reads the list again. A version is
// restored as a new draft from its sidebar, with Restore and publish..., and from its row's "...", with Undo (10.3b).
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DefinitionChangeResponseModel, FormVersionEntryResponseModel, FormVersionResponseModel } from '../../api';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { FORM_WORKSPACE_CONTEXT, VERSION_COMPARISON_MODAL_ALIAS } from '../constants';
import type { DesignDefinition } from '../definition';
import { builtInTypes, englishLook } from '../preview/fixtures';
import { PUBLISH_MODAL_ALIAS } from '../publish.modal.token';
import type { CommentModalData } from './comment.modal.token';
import type { ComparisonModalData } from './comparison.modal.token';
import type { RestoredDraft } from './restore-draft';
import type { VersionComparison, VersionPage } from './versions';

/** The modals opened, with their data, and what the one open answers. */
const opened = vi.hoisted(() => ({
  modals: [] as { token: string; data: unknown }[],
  answer: undefined as unknown,
  /** What a dialog answers by its alias, before the answer for every other. */
  answers: {} as Record<string, unknown>,
}));

/** Whether the view's own words fail to load. */
const words = vi.hoisted(() => ({ fail: false }));

vi.mock('./history-words', async (original) => {
  const real = await original<typeof import('./history-words')>();
  return {
    ...real,
    loadHistoryWords: async (lang: string) => {
      if (words.fail) throw new Error('No words');
      return real.loadHistoryWords(lang);
    },
  };
});

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: { toString(): string }, args?: { data?: unknown }) => {
    opened.modals.push({ token: token.toString(), data: args?.data });
    const answer = token.toString() in opened.answers ? opened.answers[token.toString()] : opened.answer;
    if (answer === undefined) throw new Error('closed');
    return answer;
  },
}));

/** The pages asked for by key, each answered with its name. */
const pages = vi.hoisted(() => ({ asked: [] as string[][] }));

vi.mock('@umbraco-cms/backoffice/document', () => ({
  UmbDocumentItemRepository: class {
    async requestItems(keys: string[]) {
      pages.asked.push(keys);
      return { data: keys.map((unique) => ({ unique, variants: [{ culture: null, name: 'Thank you' }] })) };
    }
  },
}));

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => ({ data: await request }),
}));

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  actionTypeList: async () => ({ types: [] }),
}));

vi.mock('../preview/look', async (original) => ({
  ...(await original<typeof import('../preview/look')>()),
  readLook: async () => englishLook,
}));

const { FormHistoryViewElement } = await import('./form-history.view');

// The tests run at a fixed midday, so a version made some minutes before was made today whenever they run.
const MIDDAY = new Date(2026, 10, 6, 12, 0);
const minutesAgo = (minutes: number) => new Date(MIDDAY.getTime() - minutes * 60_000).toISOString();

const version = (number: number, more: Partial<FormVersionResponseModel> = {}): FormVersionResponseModel => ({
  number,
  kind: 'published',
  isPinned: false,
  createdUtc: minutesAgo(100 - number),
  publishedUtc: minutesAgo(100 - number),
  isDraft: false,
  isLive: false,
  ...more,
});

const entry = (
  number: number,
  more: Partial<FormVersionResponseModel> = {},
  changes: FormVersionEntryResponseModel['changes'] = [
    { kind: 'field', change: 'changed', moved: false, label: 'Email address' },
  ],
  by: string | null = 'Ana Novak',
): FormVersionEntryResponseModel => ({ version: version(number, more), changes, by });

const definition: DesignDefinition = {
  pages: [
    { key: 'p1', groups: [{ key: 'g1', fields: [{ key: 'mail', type: 'email', label: { '': 'Email address' } }] }] },
  ],
};

/** A workspace that answers the versions as the server would, and records what was asked of it. */
class StandIn {
  readonly uniques = new UmbBasicState<string | undefined>('form1');
  readonly unique = this.uniques.asObservable();
  readonly drafts = new UmbBasicState<number | null>(16);
  readonly draftVersion = this.drafts.asObservable();
  readonly publishes = new UmbBasicState<number | null | undefined>(14);
  readonly publishedVersion = this.publishes.asObservable();
  host: Element | undefined;
  /** The publishes, and every version with the draft saves, newest first. */
  published: FormVersionEntryResponseModel[] = [
    entry(14, { isLive: true, isPinned: true, comment: 'Press enquiries go to the press page' }),
    entry(12, {}, [
      { kind: 'field', change: 'added', moved: false, label: 'A' },
      { kind: 'field', change: 'added', moved: false, label: 'B' },
      { kind: 'settings', change: 'changed', moved: false, settingsGroup: 'protection' },
    ]),
    entry(10, {}, null, null),
  ];
  everything: FormVersionEntryResponseModel[] = [
    entry(16, { kind: 'draft', isDraft: true, publishedUtc: null }, [], 'Lea Hoffmann'),
    entry(15, { kind: 'draft', publishedUtc: null }, [], 'Lea Hoffmann'),
    ...this.published,
  ];
  fails = false;
  /** The reads asked for: whether with the draft saves, and from where. */
  readonly reads: [boolean, number][] = [];
  page = 20;
  versionsOf = async (includeDrafts: boolean, skip: number): Promise<VersionPage | undefined> => {
    this.reads.push([includeDrafts, skip]);
    if (this.fails) return undefined;
    const all = includeDrafts ? this.everything : this.published;
    return { total: all.length, items: all.slice(skip, skip + this.page) };
  };
  readonly compared: [number, number][] = [];
  comparison: VersionComparison = { kind: 'compared', changes: [], older: definition, newer: definition };
  comparisonOf = async (from: number, to: number) => {
    this.compared.push([from, to]);
    return this.comparison;
  };
  readonly pins: [number, boolean][] = [];
  /** Whether the server refuses a pin, which the workspace's notification says. */
  refuses = false;
  pinVersion = async (number: number, pinned: boolean) => {
    this.pins.push([number, pinned]);
    return this.refuses ? undefined : version(number, { isPinned: pinned, isLive: number === 14 });
  };
  readonly comments: [number, string][] = [];
  commentVersion = async (number: number, comment: string) => {
    this.comments.push([number, comment]);
    return version(number, { comment: comment || null });
  };
  /** The versions restored with their forms, and what a restore answers: the drafts before and after, or a refusal. */
  readonly restores: number[] = [];
  readonly restoredForms: string[] = [];
  restoring: (number: number) => RestoredDraft | undefined = () => {
    const before = this.drafts.getValue() ?? 0;
    // The workspace reads the form again, which holds the new draft.
    this.drafts.setValue(before + 1);
    return { before, after: before + 1, shown: true };
  };
  restoreVersion = async (key: string, number: number) => {
    this.restoredForms.push(key);
    this.restores.push(number);
    return this.restoring(number);
  };
  /** Whether the draft has changes not saved yet. */
  unsaved = false;
  getHasUnpersistedChanges = () => this.unsaved;
  /** What the publish dialog's flow reads. */
  getData = () => ({ draftVersion: this.drafts.getValue(), form: { name: 'Contact' } });
  getName = () => 'Contact';
  check = new UmbBasicState({}).asObservable();
  checkNow = vi.fn();
  publish = vi.fn(async () => true);
  getFieldTypes = () => builtInTypes;
  getHostElement = () => this.host;
  getEntityType = () => 'jfb-form';
  getUnique = () => this.uniques.getValue();
}

async function viewOf(prepare: (workspace: StandIn) => void = () => undefined) {
  const workspace = new StandIn();
  prepare(workspace);
  const host = testHost();
  workspace.host = host;
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const view = new FormHistoryViewElement();
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('tbody tr, .empty, [role="alert"]')).not.toBeNull());
  await settle(view);
  const root = shadow(view);
  const rows = () => all(view, 'tbody tr');
  const cell = (row: number, column: number) => text(rows()[row]?.querySelectorAll('td, th')[column]);
  const said = () => text(root.querySelector('[role="status"]'));
  const toggle = root.querySelector<HTMLInputElement>('uui-toggle') as HTMLInputElement;
  return { workspace, view, root, rows, cell, said, toggle, peek };
}

beforeAll(registerEnglish);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(MIDDAY);
  opened.modals = [];
  opened.answer = undefined;
  opened.answers = {};
  words.fail = false;
  pages.asked = [];
});

afterEach(() => {
  vi.useRealTimers();
  document.body.replaceChildren();
});

describe("a form's History", () => {
  it('lists the published versions newest first: when and by whom, what changed, the comment, the pin and the live one', async () => {
    const { rows, cell, workspace, root } = await viewOf();
    expect(workspace.reads).toEqual([[false, 0]]);
    expect(rows().map((row) => text(row.querySelector('.name, .plain')))).toEqual([
      'Version 14',
      'Version 12',
      'Version 10',
    ]);
    expect(text(rows()[0]?.querySelector('uui-tag'))).toBe('Published');
    // The pin is said once: by its hidden word, its icon only for sight.
    expect(rows()[0]?.querySelector('uui-icon.pin')?.getAttribute('title')).toBe('Pinned');
    expect(rows()[0]?.querySelector('uui-icon.pin')?.getAttribute('aria-hidden')).toBe('true');
    expect(rows()[0]?.querySelector('th[scope="row"] .name')).not.toBeNull();
    expect(rows()[1]?.querySelector('button.name')?.getAttribute('aria-label')).toBe(
      'Version 12, compared with the current draft',
    );
    expect(text(rows()[0]?.querySelector('.made'))).toMatch(/^Today, \d{1,2}:\d{2}.* \u00b7 Ana Novak$/);
    expect(text(rows()[2]?.querySelector('.made'))).toMatch(/^Today, \d{1,2}:\d{2}[^\u00b7]*$/);
    expect(cell(0, 2)).toBe('Email address Comment: Press enquiries go to the press page');
    expect(cell(1, 2)).toBe('2 fields, Spam protection');
    expect(cell(2, 2)).toBe('First published version');
    expect([cell(0, 3), cell(1, 3)]).toEqual(['Press enquiries go to the press page', '']);
    // The draft saves are one switch away; nothing says how long they are kept until they show.
    expect(root.querySelector('.note')).toBeNull();
    expect(rows()[0]?.querySelector('uui-checkbox')?.getAttribute('aria-label')).toBe('Select Version 14');
    expect(rows()[0]?.querySelector('uui-checkbox')?.hasAttribute('label')).toBe(false);
  });

  it('adds the draft saves, muted, and the current draft first, with Show draft saves', async () => {
    const { rows, toggle, workspace, root } = await viewOf();
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(rows()).toHaveLength(5));
    expect(workspace.reads).toEqual([
      [false, 0],
      [true, 0],
    ]);
    expect(rows().map((row) => [text(row.querySelector('.name, .plain')), row.classList.contains('draft')])).toEqual([
      ['Current draft', true],
      ['Draft save', true],
      ['Version 14', false],
      ['Version 12', false],
      ['Version 10', false],
    ]);
    // The current draft is what the others are compared with: its name opens nothing.
    expect(rows()[0]?.querySelector('button.name')).toBeNull();
    expect(rows()[1]?.querySelector('button.name')).not.toBeNull();
    // There may be many draft saves: their controls name each by its moment.
    expect(rows()[1]?.querySelector('uui-checkbox')?.getAttribute('aria-label')).toMatch(
      /^Select Draft save, Today, \d{1,2}:\d{2}/,
    );
    expect(text(rows()[1]?.querySelector('.changed'))).toBe('No changes');
    // Among the draft saves the first publish is still the first of its kind.
    expect(text(rows()[4]?.querySelector('.changed'))).toBe('First published version');
    expect(text(root.querySelector('.note'))).toBe('Published and pinned versions are kept for good.');
  });

  it('shows Compare for two ticked versions, rests the other boxes, and compares them the older first', async () => {
    const { rows, root, workspace, said } = await viewOf();
    expect(root.querySelector('.toolbar uui-button')).toBeNull();
    const box = (row: number) => must(rows()[row]?.querySelector<HTMLInputElement>('uui-checkbox'), `box ${row}`);
    for (const row of [0, 1]) {
      box(row).checked = true;
      box(row).dispatchEvent(new Event('change'));
    }
    await settle();
    expect(said()).toBe('Two versions ticked. Compare is in the toolbar.');
    expect(box(2).hasAttribute('disabled')).toBe(true);
    const compare = must(root.querySelector<HTMLElement>('.toolbar uui-button'), 'Compare');
    expect(compare.getAttribute('label')).toBe('Compare');
    const change: DefinitionChangeResponseModel = {
      kind: 'field',
      key: 'mail',
      change: 'changed',
      moved: false,
      label: 'Email address',
      properties: [{ path: ['required'], from: false, to: true }],
    };
    workspace.comparison = { kind: 'compared', changes: [change], older: definition, newer: definition };
    compare.click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(1));
    const data = opened.modals[0]?.data as ComparisonModalData;
    expect([data.headline, data.olderName, data.newerName, data.made]).toEqual([
      'Version 12 and version 14',
      'Version 12',
      'Version 14',
      undefined,
    ]);
    expect(await data.read()).toEqual({
      kind: 'compared',
      parts: [{ name: 'fields', lines: [], rows: [{ what: 'Email address, Required', older: 'No', newer: 'Yes' }] }],
    });
    expect(workspace.compared).toEqual([[12, 14]]);
    // Unticking one frees the others.
    box(0).checked = false;
    box(0).dispatchEvent(new Event('change'));
    await settle();
    expect(box(2).hasAttribute('disabled')).toBe(false);
    expect(root.querySelector('.toolbar uui-button')).toBeNull();
  });

  it("opens a version compared with the current draft, with when and by whom, and says one that can't be read", async () => {
    const { rows, workspace } = await viewOf();
    must(rows()[1]?.querySelector<HTMLElement>('button.name'), 'Version 12').click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(1));
    const data = opened.modals[0]?.data as ComparisonModalData;
    expect([data.headline, data.olderName, data.newerName]).toEqual([
      'Version 12 and the current draft',
      'Version 12',
      'The current draft',
    ]);
    expect(data.made).toMatch(/^Today, .* \u00b7 Ana Novak$/);
    workspace.comparison = { kind: 'unreadable', number: 12 };
    expect(await data.read()).toEqual({ kind: 'unreadable', name: 'Version 12' });
    workspace.comparison = { kind: 'failed' };
    expect(await data.read()).toEqual({ kind: 'failed' });
    expect(workspace.compared).toEqual([
      [12, 16],
      [12, 16],
    ]);
  });

  it("names a redirect's page by its name in the site, read once for both versions", async () => {
    const { rows, workspace } = await viewOf();
    const PAGE = '00000000-0000-4000-8000-0000000000a9';
    const outcome = (redirect: Record<string, unknown>) => ({ key: 'o1', kind: 'redirect', redirect });
    workspace.comparison = {
      kind: 'compared',
      changes: [
        {
          kind: 'outcome',
          key: 'o1',
          change: 'changed',
          moved: false,
          properties: [{ path: ['redirect', 'content'], from: PAGE, to: null }],
        },
      ],
      older: { ...definition, outcomes: [outcome({ content: PAGE })] },
      newer: { ...definition, outcomes: [outcome({ url: '/thanks' })] },
    };
    must(rows()[1]?.querySelector<HTMLElement>('button.name'), 'Version 12').click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(1));
    const read = await (opened.modals[0]?.data as ComparisonModalData).read();
    // After sending is a part of its own, which names an outcome by its place (ADR-0256).
    expect(read.kind === 'compared' ? [read.parts[0]?.name, read.parts[0]?.rows] : read).toEqual([
      'after',
      [{ what: 'Outcome 1, Redirect, Page', older: 'Thank you', newer: 'none' }],
    ]);
    expect(pages.asked).toEqual([[PAGE]]);
  });

  it('pins and unpins a version from its "\u2026", and says so', async () => {
    const { rows, said, workspace } = await viewOf();
    const items = (row: number) => [...(rows()[row]?.querySelectorAll<HTMLElement>('.menu button') ?? [])];
    expect(items(1).map((item) => text(item))).toEqual(['Restore as a new draft', 'Pin', 'Add comment\u2026']);
    expect(items(0).map((item) => text(item))).toEqual(['Restore as a new draft', 'Unpin', 'Change comment\u2026']);
    items(1)[1]?.click();
    await vi.waitFor(() => expect(rows()[1]?.querySelector('uui-icon.pin')).not.toBeNull());
    expect(said()).toBe('Version 12 pinned. The cleanup keeps this version.');
    items(0)[1]?.click();
    await vi.waitFor(() => expect(rows()[0]?.querySelector('uui-icon.pin')).toBeNull());
    expect(said()).toBe('Version 14 unpinned.');
    // A refused pin leaves the row as it was and says nothing of its own: the notification says why.
    workspace.refuses = true;
    items(2)[1]?.click();
    await vi.waitFor(() => expect(workspace.pins).toHaveLength(3));
    await settle();
    expect(rows()[2]?.querySelector('uui-icon.pin')).toBeNull();
    expect(said()).toBe('Version 14 unpinned.');
    expect(workspace.pins).toEqual([
      [12, true],
      [14, false],
      [10, true],
    ]);
  });

  it('writes a comment in a dialog, and removes it when saved empty', async () => {
    const { rows, cell, said, workspace } = await viewOf();
    const menu = (row: number) => [...(rows()[row]?.querySelectorAll<HTMLElement>('.menu button') ?? [])];
    opened.answer = { comment: 'Shorter for the autumn' };
    menu(1)[2]?.click();
    await vi.waitFor(() => expect(cell(1, 3)).toBe('Shorter for the autumn'));
    expect(opened.modals[0]?.data as CommentModalData).toEqual({ name: 'Version 12', comment: '' });
    expect(said()).toBe('Comment on Version 12 saved.');
    opened.answer = { comment: '' };
    menu(1)[2]?.click();
    await vi.waitFor(() => expect(cell(1, 3)).toBe(''));
    expect(said()).toBe('Comment on Version 12 removed.');
    // Cancelled, or saved as it was: nothing is sent.
    opened.answer = undefined;
    menu(0)[2]?.click();
    await settle();
    opened.answer = { comment: 'Press enquiries go to the press page' };
    menu(0)[2]?.click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(4));
    await settle();
    expect(workspace.comments).toEqual([
      [12, 'Shorter for the autumn'],
      [12, ''],
    ]);
  });

  it('shows more versions a page at a time, the focus on the first new one', async () => {
    const { rows, root, workspace, view } = await viewOf((one) => (one.page = 2));
    expect(rows()).toHaveLength(2);
    must(root.querySelector<HTMLElement>('.footer uui-button'), 'Show more').click();
    await vi.waitFor(() => expect(rows()).toHaveLength(3));
    await settle(view);
    expect(workspace.reads).toEqual([
      [false, 0],
      [false, 2],
    ]);
    expect(root.activeElement?.closest('tr')).toBe(rows()[2]);
    expect(root.querySelector('.footer')).toBeNull();
  });

  it('says a read that fails, with Try again, and says when nothing is published yet', async () => {
    const { root, workspace, rows } = await viewOf((one) => (one.fails = true));
    expect(text(root.querySelector('[role="alert"]'))).toBe("The versions couldn't be read.");
    workspace.fails = false;
    workspace.published = [];
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await vi.waitFor(() =>
      expect(text(root.querySelector('.empty'))).toBe(
        'No published version yet. "Show draft saves" lists the draft\'s saves.',
      ),
    );
    expect(rows()).toHaveLength(0);
    expect(workspace.reads).toEqual([
      [false, 0],
      [false, 0],
    ]);
  });

  it("says only the latest read's versions, though an older read answers last", async () => {
    const { workspace, rows, toggle } = await viewOf();
    const answers: ((page: VersionPage) => void)[] = [];
    workspace.versionsOf = (includeDrafts: boolean, skip: number) => {
      workspace.reads.push([includeDrafts, skip]);
      return new Promise<VersionPage | undefined>((resolve) => answers.push(resolve));
    };
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    toggle.checked = false;
    toggle.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(answers).toHaveLength(2));
    answers[1]?.({ total: 1, items: [entry(14, { isLive: true })] });
    await vi.waitFor(() => expect(rows()).toHaveLength(1));
    answers[0]?.({ total: 5, items: workspace.everything });
    await settle();
    expect(rows()).toHaveLength(1);
  });

  it('reads the list again when a save or a publish adds a version', async () => {
    const { workspace, rows } = await viewOf();
    workspace.published = [entry(17, { isLive: true }), ...workspace.published];
    workspace.drafts.setValue(17);
    await vi.waitFor(() => expect(rows()).toHaveLength(4));
    expect(workspace.reads).toEqual([
      [false, 0],
      [false, 0],
    ]);
  });

  it('reads the list again when a publish adds the live version, though the draft stays where it was', async () => {
    const { workspace, rows } = await viewOf();
    workspace.published = [entry(17, { isLive: true }), ...workspace.published.map((one) => entry(one.version.number))];
    workspace.publishes.setValue(17);
    await vi.waitFor(() => expect(rows()).toHaveLength(4));
    expect(rows().map((row) => row.querySelectorAll('uui-tag').length)).toEqual([1, 0, 0, 0]);
    expect(text(rows()[0]?.querySelector('uui-tag'))).toBe('Published');
    expect(workspace.reads).toEqual([
      [false, 0],
      [false, 0],
    ]);
  });

  it('lists the current draft first, which the others are compared with', async () => {
    const { rows, toggle, workspace } = await viewOf((one) => {
      one.everything = [
        one.everything[1],
        ...one.published.slice(0, 1),
        one.everything[0],
        ...one.published.slice(1),
      ].filter((row): row is FormVersionEntryResponseModel => row !== undefined);
    });
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(rows()).toHaveLength(5));
    expect(rows().map((row) => text(row.querySelector('.name, .plain')))).toEqual([
      'Current draft',
      'Draft save',
      'Version 14',
      'Version 12',
      'Version 10',
    ]);
    expect(workspace.reads).toHaveLength(2);
  });

  it("says a page of Show more that can't be read beside it, and reads it with Show more again", async () => {
    const { rows, root, workspace } = await viewOf((one) => (one.page = 2));
    workspace.fails = true;
    must(root.querySelector<HTMLElement>('.footer uui-button'), 'Show more').click();
    await vi.waitFor(() =>
      expect(text(root.querySelector('.footer [role="alert"]'))).toBe(
        "More versions couldn't be read. Try Show more again.",
      ),
    );
    expect(rows()).toHaveLength(2);
    workspace.fails = false;
    must(root.querySelector<HTMLElement>('.footer uui-button'), 'Show more').click();
    await vi.waitFor(() => expect(rows()).toHaveLength(3));
    expect(root.querySelector('[role="alert"]')).toBeNull();
  });

  it("says in the backoffice's start words when its own can't be loaded, with Try again", async () => {
    words.fail = true;
    const { root, rows } = await viewOf();
    const alert = must(root.querySelector('[role="alert"]'), 'the failure');
    expect(text(alert.querySelector('span'))).toBe("The History couldn't be shown. Try again in a moment.");
    expect(alert.querySelector('uui-button')?.getAttribute('label')).toBe('Try again');
    expect(root.querySelector('.toolbar')).toBeNull();
    words.fail = false;
    must(alert.querySelector<HTMLElement>('uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(rows()).toHaveLength(3));
  });

  it('starts afresh for another form: its own versions, nothing ticked', async () => {
    const { workspace, rows, root, said } = await viewOf();
    const box = must(rows()[0]?.querySelector<HTMLInputElement>('uui-checkbox'), 'box');
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    await settle();
    expect(said()).toBe('One version ticked.');
    // The other form has a version of the same number, which starts unticked all the same.
    workspace.published = [entry(14, { isLive: true, isDraft: true })];
    workspace.uniques.setValue('form2');
    await vi.waitFor(() => expect(rows()).toHaveLength(1));
    expect(rows()[0]?.querySelector<HTMLInputElement>('uui-checkbox')?.checked).toBe(false);
    expect(root.querySelector('.toolbar uui-button')).toBeNull();
    expect(workspace.reads).toEqual([
      [false, 0],
      [false, 0],
    ]);
  });
});

describe('restoring a version (ROADMAP 10.3b, ADR-0198)', () => {
  const twoChanges: DefinitionChangeResponseModel[] = [
    { kind: 'field', key: 'mail', change: 'changed', moved: false, label: 'Email address', properties: [] },
    { kind: 'settings', change: 'changed', moved: false, settingsGroup: 'appearance', properties: [] },
  ];

  /** The toasts said, with their data. */
  const toasts = (peek: ReturnType<typeof vi.fn>) =>
    peek.mock.calls.map(([, options]) => options as { elementName?: string; data: Record<string, unknown> });

  it('restores from its sidebar as a new draft, names what changed, and Undo brings the draft before back', async () => {
    const { rows, workspace, peek } = await viewOf((one) => {
      one.comparison = { kind: 'compared', changes: twoChanges, older: definition, newer: definition };
    });
    opened.answers[VERSION_COMPARISON_MODAL_ALIAS] = { restore: 'draft' };
    must(rows()[1]?.querySelector<HTMLElement>('button.name'), 'Version 12').click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());

    expect((opened.modals[0]?.data as ComparisonModalData).restorable).toBe(true);
    expect((opened.modals[0]?.data as ComparisonModalData).publishable).toBe(true);
    expect(workspace.restores).toEqual([12]);
    // What changed is the new draft against the one before.
    expect(workspace.compared).toEqual([[16, 17]]);
    const [toast] = toasts(peek);
    expect(toast?.elementName).toBe('jfb-undo-notification');
    expect([toast?.data['message'], toast?.data['undoLabel']]).toEqual([
      'Version 12 is the new draft: Email address and Appearance changed.',
      'Undo',
    ]);
    expect(opened.modals.map((modal) => modal.token)).toEqual([VERSION_COMPARISON_MODAL_ALIAS]);

    await (toast?.data['undo'] as () => Promise<void>)();
    expect(workspace.restores).toEqual([12, 16]);
    expect(toasts(peek)[1]?.data['message']).toBe('The draft before is back.');
  });

  it('keeps the draft as it is where Undo comes after a change since, with changes not saved, or in another form', async () => {
    const { rows, workspace, peek } = await viewOf();
    const stale =
      "The draft has changed since, so the restore can't be undone here. The draft before stays in History.";
    const restoreAt = async () => {
      peek.mockClear();
      rows()[1]?.querySelector<HTMLElement>('.menu button')?.click();
      await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());
      return toasts(peek)[0]?.data['undo'] as () => Promise<void>;
    };

    let undo = await restoreAt();
    workspace.unsaved = true;
    await undo();
    expect([peek.mock.calls[1]?.[0], toasts(peek)[1]?.data['message']]).toEqual(['warning', stale]);
    workspace.unsaved = false;

    undo = await restoreAt();
    workspace.drafts.setValue(30);
    await undo();
    expect(toasts(peek)[1]?.data['message']).toBe(stale);

    undo = await restoreAt();
    workspace.uniques.setValue('form2');
    await undo();
    expect(toasts(peek)[1]?.data['message']).toBe(stale);
    // Each Undo restored nothing: only the three restores reached the workspace, each of the form it began in.
    expect(workspace.restores).toEqual([12, 12, 12]);
    expect(workspace.restoredForms).toEqual(['form1', 'form1', 'form1']);
  });

  it('says nothing more of a restore whose form was left meanwhile, and that a new draft not read again is not shown', async () => {
    const left = await viewOf((one) => {
      one.restoring = () => {
        one.uniques.setValue('form2');
        return { before: 16, after: 17, shown: false };
      };
    });
    left.rows()[1]?.querySelector<HTMLElement>('.menu button')?.click();
    await vi.waitFor(() => expect(left.workspace.restores).toEqual([12]));
    await settle();
    expect(left.peek).not.toHaveBeenCalled();

    const unread = await viewOf((one) => (one.restoring = () => ({ before: 16, after: 17, shown: false })));
    unread.rows()[1]?.querySelector<HTMLElement>('.menu button')?.click();
    await vi.waitFor(() => expect(unread.peek).toHaveBeenCalledOnce());
    expect([unread.peek.mock.calls[0]?.[0], toasts(unread.peek)[0]]).toEqual([
      'warning',
      { data: { message: "The new draft couldn't be read here. Reload the page to see it." } },
    ]);
  });

  it('tells the sidebar of changes not saved yet, which its comparison leaves out', async () => {
    const { rows } = await viewOf((one) => (one.unsaved = true));
    must(rows()[1]?.querySelector<HTMLElement>('button.name'), 'Version 12').click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(1));
    expect((opened.modals[0]?.data as ComparisonModalData).unsaved).toBe(true);
  });

  it('keeps the focus on its version where a read adds a row above it', async () => {
    const { rows, workspace, root, toggle } = await viewOf();
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(rows()).toHaveLength(5));
    must(rows()[3]?.querySelector<HTMLElement>('button.more'), 'the "..." of version 12').focus();

    // A restore elsewhere adds draft 17; draft 16 is a draft save now.
    workspace.everything = [
      entry(17, { kind: 'draft', isDraft: true, publishedUtc: null }, [], 'Lea Hoffmann'),
      entry(16, { kind: 'draft', publishedUtc: null }, [], 'Lea Hoffmann'),
      ...workspace.everything.slice(1),
    ];
    workspace.drafts.setValue(17);
    await vi.waitFor(() => expect(rows()).toHaveLength(6));
    expect((root.activeElement as HTMLElement | null)?.closest('tr')?.dataset['number']).toBe('12');
  });

  it('restores and then opens the publish dialog for the new draft, which may be closed without publishing', async () => {
    const { rows, workspace } = await viewOf();
    opened.answers[VERSION_COMPARISON_MODAL_ALIAS] = { restore: 'publish' };
    must(rows()[1]?.querySelector<HTMLElement>('button.name'), 'Version 12').click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(2));

    expect(workspace.restores).toEqual([12]);
    expect(opened.modals[1]?.token).toBe(PUBLISH_MODAL_ALIAS);
    expect((opened.modals[1]?.data as { name: string }).name).toBe('Contact');
    await settle();
    expect(workspace.publish).not.toHaveBeenCalled();
  });

  it('restores at once from a row\'s "...", and says when the draft holds what it held', async () => {
    const { rows, workspace, peek } = await viewOf();
    const items = (row: number) => [...(rows()[row]?.querySelectorAll<HTMLElement>('.menu button') ?? [])];
    items(1)[0]?.click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());

    expect(opened.modals).toEqual([]);
    expect(workspace.restores).toEqual([12]);
    expect(toasts(peek)[0]?.data['message']).toBe('Version 12 is the new draft. It holds what the draft before held.');
  });

  it("says a restore whose changes can't be read plainly, and nothing of one the server refuses", async () => {
    const { rows, workspace, peek } = await viewOf((one) => (one.comparison = { kind: 'failed' }));
    const items = (row: number) => [...(rows()[row]?.querySelectorAll<HTMLElement>('.menu button') ?? [])];
    items(2)[0]?.click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());
    expect(toasts(peek)[0]?.data['message']).toBe('Version 10 is the new draft.');

    // A refusal is said by the workspace, which notifies; nothing more is said here.
    workspace.restoring = () => undefined;
    items(1)[0]?.click();
    await vi.waitFor(() => expect(workspace.restores).toEqual([10, 12]));
    await settle();
    expect(peek).toHaveBeenCalledOnce();
  });

  it('offers the live version back as a new draft, but not to publish again, which would change nothing', async () => {
    const { rows } = await viewOf();
    must(rows()[0]?.querySelector<HTMLElement>('button.name'), 'Version 14').click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(1));
    const data = opened.modals[0]?.data as ComparisonModalData;
    expect([data.restorable, data.publishable]).toEqual([true, undefined]);
  });

  it('offers no restore on the current draft, nor for two ticked versions, where which one would come back is unclear', async () => {
    const { rows, toggle, root } = await viewOf();
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(rows()).toHaveLength(5));
    const firstItem = (row: number) => text(rows()[row]?.querySelector('.menu button'));
    expect([firstItem(0), firstItem(1)]).toEqual(['Pin', 'Restore as a new draft']);

    for (const row of [1, 2]) {
      const box = must(rows()[row]?.querySelector<HTMLInputElement>('uui-checkbox'), `box ${row}`);
      box.checked = true;
      box.dispatchEvent(new Event('change'));
    }
    await settle();
    must(root.querySelector<HTMLElement>('.toolbar uui-button'), 'Compare').click();
    await vi.waitFor(() => expect(opened.modals).toHaveLength(1));
    expect((opened.modals[0]?.data as ComparisonModalData).restorable).toBeUndefined();
  });
});
