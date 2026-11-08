// A form's Entries view (ROADMAP 11.1, IA 6.1, ADR-0207): its rows, names and marks, the count, the columns and what a
// filter fixes, the search with its empty state, the chips with Save as... and Clear filters, Show more, the bulk bar
// with Undo, Delete... and Re-run actions asked first, the keys of section 4.13, the first use and a read that fails;
// and the same list as the Inbox and Spam (ADR-0209): every form's entries with their form, the Form filter and its one
// form's fields, acting per form with one Undo, Spam without a state, Empty spam... and each place's first use.
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  EntryListFieldResponseModel,
  EntryListFilterRequestModel,
  EntryListPageResponseModel,
  EntryListRequestModel,
  EntryListRowResponseModel,
  EntryMoveAsksResponseModel,
  EntryPanelResponseModel,
} from '../../api';
import type { EntryPanelElement } from '../../entries/entry-panel.element';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import {
  ENTRY_COLUMNS_MODAL_ALIAS,
  ENTRY_DIGEST_MODAL_ALIAS,
  ENTRY_FILTER_MODAL_ALIAS,
  ENTRY_FORMS_MODAL_ALIAS,
  ENTRY_KEYS_MODAL_ALIAS,
  ENTRY_SET_NAME_MODAL_ALIAS,
  type EntrySetNameModalData,
} from '../../entries/entry-modals';

/** The modals opened, with their data, and what each answers by its alias. */
const opened = vi.hoisted(() => ({
  modals: [] as { token: string; data: unknown }[],
  answers: {} as Record<string, unknown>,
  /** Whether the confirmation is confirmed. */
  confirms: true,
  confirmations: [] as { headline?: string; confirmLabel?: string }[],
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: { toString(): string }, args?: { data?: unknown }) => {
    opened.modals.push({ token: token.toString(), data: args?.data });
    const answer = opened.answers[token.toString()];
    if (answer === undefined) throw new Error('closed');
    return answer;
  },
  umbConfirmModal: async (_host: unknown, data: { headline?: string; confirmLabel?: string }) => {
    opened.confirmations.push(data);
    if (!opened.confirms) throw new Error('cancelled');
  },
}));

/** The server, as the view's calls reach it. */
const server = vi.hoisted(() => ({
  fields: [] as EntryListFieldResponseModel[],
  columns: [] as string[],
  sets: [] as { key: string; name: string; form?: string | null; filter: EntryListFilterRequestModel }[],
  rows: [] as EntryListRowResponseModel[],
  page: 50,
  failsView: false,
  failsPage: false,
  requests: [] as EntryListRequestModel[],
  moves: [] as { entries: readonly string[]; state: string; overbook?: boolean }[],
  /** What a move back asks first, until the user agrees to overbook. */
  asks: undefined as EntryMoveAsksResponseModel | undefined,
  /** The forms whose move back asks; every form when none is named. */
  askingForms: undefined as string[] | undefined,
  /** The forms a move was checked for, without moving. */
  checks: [] as string[],
  undos: [] as string[],
  undoAnswer: 'done' as 'done' | 'tooLate' | 'failed',
  deletes: [] as { entries: readonly string[]; confirm: boolean }[],
  reruns: [] as { entries: readonly string[]; confirm: boolean }[],
  rerunActions: [{ key: 'a1', type: 'webhook', name: 'Tell the team', trigger: 'approved' }],
  savedSets: [] as { name: string; filter: EntryListFilterRequestModel }[],
  setForms: [] as (string | null)[],
  setCode: undefined as string | undefined,
  savedColumns: [] as (readonly string[])[],
  // The Inbox and Spam: the forms' names, what the user may do on each, and what their calls were.
  everything: ['viewEntries', 'editEntries', 'deleteEntries', 'manageActions'],
  formNames: { form1: 'Job application', form2: 'Contact', form3: 'Newsletter' } as Record<string, string>,
  formPermissions: {} as Record<string, string[]>,
  inboxSets: [] as { key: string; name: string; form?: string | null; filter: EntryListFilterRequestModel }[],
  inboxForms: [] as { key: string; name: string; permissions: string[] }[],
  across: [] as { place: string; request: EntryListRequestModel }[],
  viewsRead: [] as string[],
  moveForms: [] as string[],
  empties: [] as boolean[],
  emptyPlan: { entries: 0, files: 0, more: false },
  anywhere: [] as string[],
}));

vi.mock('../../entries/entries-api', () => ({
  readView: async (_host: unknown, form: string) => {
    server.viewsRead.push(form);
    return server.failsView ? undefined : { fields: server.fields, columns: server.columns, filterSets: server.sets };
  },
  readInboxView: async () =>
    server.failsView ? undefined : { filterSets: server.inboxSets, forms: server.inboxForms },
  readAcross: async (
    _host: unknown,
    place: 'inbox' | 'spam',
    request: EntryListRequestModel,
  ): Promise<EntryListPageResponseModel | undefined> => {
    server.across.push({ place, request });
    if (server.failsPage) return undefined;
    const chosen = request.filter?.forms ?? [];
    const items = server.rows.filter(
      (one) => (place === 'spam') === (one.state === 'spam') && (chosen.length === 0 || chosen.includes(one.form)),
    );
    const forms = [...new Set([...items.map((one) => one.form), ...chosen])];
    return {
      items,
      next: null,
      ...(request.count ? { total: items.length } : {}),
      more: false,
      forms: forms.map((key) => ({
        key,
        name: server.formNames[key] ?? key,
        permissions: server.formPermissions[key] ?? server.everything,
      })),
    };
  },
  emptySpam: async (_host: unknown, confirm: boolean) => {
    server.empties.push(confirm);
    return { ...server.emptyPlan, deleted: confirm };
  },
  readPage: async (
    _host: unknown,
    _form: string,
    request: EntryListRequestModel,
  ): Promise<EntryListPageResponseModel | undefined> => {
    server.requests.push(request);
    if (server.failsPage) return undefined;
    const start = request.after ? Number(request.after) : 0;
    const items = server.rows.slice(start, start + server.page);
    const next = start + server.page < server.rows.length ? String(start + server.page) : null;
    return { items, next, ...(request.count ? { total: server.rows.length } : {}), more: false, forms: [] };
  },
  saveColumns: async (_host: unknown, _form: string, columns: readonly string[]) => {
    server.savedColumns.push(columns);
    return true;
  },
  saveSet: async (
    _host: unknown,
    key: string,
    name: string,
    form: string | null,
    filter: EntryListFilterRequestModel,
  ) => {
    if (server.setCode) return { code: server.setCode };
    server.savedSets.push({ name, filter });
    server.setForms.push(form);
    return { set: { key, name, form, filter } };
  },
  deleteSet: async () => true,
  checkMove: async (_host: unknown, form: string) => {
    server.checks.push(form);
    return server.asks && (server.askingForms?.includes(form) ?? true) ? server.asks : undefined;
  },
  changeState: async (_host: unknown, form: string, entries: readonly string[], state: string, overbook = false) => {
    server.moves.push({ entries, state, ...(overbook ? { overbook } : {}) });
    server.moveForms.push(form);
    if (server.asks && !overbook && (server.askingForms?.includes(form) ?? true))
      return { changeKey: 'change0', undoUntilUtc: null, moved: [], unmoved: [], asks: server.asks };
    return {
      changeKey: form === 'form1' ? 'change1' : `change-${form}`,
      undoUntilUtc: new Date(Date.now() + 10_000).toISOString(),
      moved: entries.map((key) => ({ key, state })),
      unmoved: [],
    };
  },
  undoChange: async (_host: unknown, _form: string, change: string) => {
    server.undos.push(change);
    return server.undoAnswer;
  },
  deleteEntries: async (_host: unknown, _form: string, entries: readonly string[], confirm: boolean) => {
    server.deletes.push({ entries, confirm });
    return { entries: entries.length, files: 4, deleted: confirm };
  },
  rerunActions: async (_host: unknown, _form: string, entries: readonly string[], confirm: boolean) => {
    server.reruns.push({ entries, confirm });
    return {
      actions: server.rerunActions,
      entries: server.rerunActions.length === 0 ? 0 : entries.length,
      leftOut: 0,
      queued: confirm,
    };
  },
}));

// What the user holds on any form, which offers Empty spam... to who may delete entries.
vi.mock('../../section/my-permissions', () => ({
  myPermissions: async () => ({ global: [], anywhere: server.anywhere }),
}));

// The entry panel reads each entry as its row says it is.
vi.mock('../../entries/entry-panel-api', () => ({
  openEntry: async (_host: unknown, _form: string, entry: string): Promise<EntryPanelResponseModel | undefined> => {
    const found = server.rows.find((one) => one.key === entry);
    return found
      ? {
          key: found.key,
          form: 'form1',
          formName: 'Job application',
          number: found.number,
          title: found.title ?? null,
          state: found.state,
          received: found.received,
          manualApproval: true,
          isTest: false,
          details: {
            page: null,
            culture: 'en',
            duration: null,
            member: null,
            memberName: null,
            ipAddress: null,
            spamReasons: [],
          },
          pages: [],
          notes: 0,
          permissions: ['viewEntries', 'editEntries', 'deleteEntries', 'manageActions'],
        }
      : undefined;
  },
}));

const { EntriesViewElement } = await import('./entries.view');

// The tests run at a fixed midday, so an entry received some minutes before was received today.
const MIDDAY = new Date(2026, 10, 7, 12, 0);

const row = (number: number, more: Partial<EntryListRowResponseModel> = {}): EntryListRowResponseModel => ({
  key: `e${number}`,
  form: 'form1',
  number,
  title: `Person ${number}`,
  state: 'submitted',
  received: new Date(MIDDAY.getTime() - number * 60_000).toISOString(),
  unseen: false,
  actions: { delivered: 0, failed: 0, uncertain: 0, waiting: 0, skipped: 0 },
  cells: [],
  ...more,
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

/** The form's workspace, as the view reads it: the form's key and what the user may do with it. */
class StandIn {
  readonly uniques = new UmbBasicState<string | undefined>('form1');
  readonly unique = this.uniques.asObservable();
  readonly forms = new UmbBasicState<{ permissions: string[] } | undefined>({
    permissions: ['viewEntries', 'editEntries', 'deleteEntries', 'manageActions'],
  });
  readonly form = this.forms.asObservable();
  getEntityType = () => 'jfb-form';
  getUnique = () => this.uniques.getValue();
  host: Element | undefined;
  getHostElement = () => this.host;
}

async function viewOf(prepare: (workspace: StandIn) => void = () => undefined) {
  const workspace = new StandIn();
  prepare(workspace);
  const host = testHost();
  workspace.host = host;
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const view = new EntriesViewElement();
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('tbody tr, .empty, [role="alert"]')).not.toBeNull());
  await settle(view);
  return { workspace, ...helpersOf(view, peek) };
}

/** Opens the Inbox or Spam as their screens do: no workspace, the place set before the list is on screen. */
async function placeOf(place: 'inbox' | 'spam') {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const view = new EntriesViewElement();
  view.place = place;
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('tbody tr, .empty, [role="alert"]')).not.toBeNull());
  await settle(view);
  return helpersOf(view, peek);
}

/** What a test reads and does on a list. */
function helpersOf(view: InstanceType<typeof EntriesViewElement>, peek: ReturnType<typeof vi.fn>) {
  const root = shadow(view);
  const rows = () => all(view, 'tbody tr');
  const said = () => text(root.querySelector('[role="status"]'));
  const button = (name: string) =>
    must(
      [...root.querySelectorAll<HTMLElement>('uui-button, button')].find(
        (one) => one.getAttribute('label') === name || one.getAttribute('aria-label') === name || text(one) === name,
      ),
      `the button ${name}`,
    );
  const tick = async (index: number) => {
    const box = must(rows()[index]?.querySelector<HTMLInputElement>('uui-checkbox'), `box ${index}`);
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    await settle(view);
  };
  const press = async (key: string, target: EventTarget = view) => {
    target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, composed: true }));
    await settle(view);
  };
  return { view, root, rows, said, button, tick, press, peek };
}

beforeAll(registerEnglish);

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(MIDDAY);
  opened.modals = [];
  opened.answers = {};
  opened.confirms = true;
  opened.confirmations = [];
  Object.assign(server, {
    fields: [
      field('position', 'Position', {
        options: [
          { value: 'p1', label: 'Frontend developer' },
          { value: 'p2', label: 'Accountant' },
        ],
      }),
      field('salary', 'Salary', { sensitive: true, column: false, sorts: false, tests: [] }),
    ],
    columns: [],
    sets: [],
    rows: [
      row(1, { unseen: true, actions: { delivered: 2, failed: 0, uncertain: 0, waiting: 0, skipped: 0 } }),
      row(2, {
        title: null,
        state: 'approved',
        actions: { delivered: 1, failed: 1, uncertain: 0, waiting: 0, skipped: 0 },
      }),
      row(3, { state: 'rejected' }),
    ],
    page: 50,
    failsView: false,
    failsPage: false,
    requests: [],
    moves: [],
    asks: undefined,
    askingForms: undefined,
    checks: [],
    undos: [],
    undoAnswer: 'done',
    deletes: [],
    reruns: [],
    rerunActions: [{ key: 'a1', type: 'webhook', name: 'Tell the team', trigger: 'approved' }],
    savedSets: [],
    setForms: [],
    setCode: undefined,
    savedColumns: [],
    formPermissions: {},
    inboxSets: [],
    inboxForms: [],
    across: [],
    viewsRead: [],
    moveForms: [],
    empties: [],
    emptyPlan: { entries: 0, files: 0, more: false },
    anywhere: [],
  });
});

afterEach(() => {
  vi.useRealTimers();
  document.body.replaceChildren();
});

describe("a form's Entries view", () => {
  it('lists the entries by title or number, with the unseen mark, the state, when received and the action status', async () => {
    const { rows, root } = await viewOf();
    expect(server.requests[0]).toEqual({ filter: { states: [], forms: [], fields: [] }, columns: [], count: true });
    expect(rows().map((one) => text(one.querySelector('th .name')))).toEqual(['Person 1, unseen', '#2', 'Person 3']);
    expect(rows()[0]?.classList.contains('unseen')).toBe(true);
    expect(rows()[0]?.querySelector('.dot')?.getAttribute('aria-hidden')).toBe('true');
    expect(rows().map((one) => text(one.querySelector('uui-tag')))).toEqual([
      'Waiting for approval',
      'Approved',
      'Rejected',
    ]);
    expect(text(rows()[0]?.querySelector('.received'))).toMatch(/^Today, \d{1,2}:\d{2}/);
    // An entry no action ran for reads none (section 4.13).
    expect(rows().map((one) => text(one.querySelector('.runs')))).toEqual(['2/2 delivered', '1 failed', 'none']);
    expect(rows()[1]?.querySelector('.runs')?.classList.contains('tone-danger')).toBe(true);
    // The failure is said under the state too, which shows only where the action status gives way.
    expect(text(rows()[1]?.querySelector('td .below'))).toBe('1 failed');
    expect(text(root.querySelector('.count'))).toBe('3 entries');
    // The boxes are named by the entry, and show no label beside them.
    expect(rows()[1]?.querySelector('uui-checkbox')?.getAttribute('aria-label')).toBe('Select entry 2');
    expect(rows()[1]?.querySelector('uui-checkbox')?.hasAttribute('label')).toBe(false);
    expect(root.querySelector('thead th[aria-sort="descending"]')).not.toBeNull();
  });

  it('shows the chosen columns, a sensitive answer masked, one not given as none, and keeps the choice', async () => {
    server.columns = ['position'];
    server.rows = [
      row(1, { cells: [{ field: 'position', texts: ['Accountant'], masked: false }] }),
      row(2, { cells: [{ field: 'position', texts: [], masked: true }] }),
      row(3, { cells: [{ field: 'position', texts: [], masked: false }] }),
    ];
    const { rows, root, button, view } = await viewOf();
    expect(server.requests[0]?.columns).toEqual(['position']);
    expect([...root.querySelectorAll('thead th')].map((th) => text(th))).toContain('Position');
    expect(text(rows()[0]?.querySelectorAll('td')[4])).toBe('Accountant');
    expect(text(rows()[1]?.querySelectorAll('td')[4])).toBe(String.fromCharCode(0x2022).repeat(3));
    expect(text(rows()[2]?.querySelectorAll('td')[4])).toBe('none');

    opened.answers[ENTRY_COLUMNS_MODAL_ALIAS] = { columns: [] };
    button('Choose columns').click();
    await settle(view);
    expect(opened.modals.at(-1)).toEqual({
      token: ENTRY_COLUMNS_MODAL_ALIAS,
      data: { fields: server.fields, columns: ['position'] },
    });
    expect(server.savedColumns).toEqual([[]]);
    expect(server.requests.at(-1)?.columns).toEqual([]);
  });

  it('hides a column whose value a filter fixes, and the state for one state', async () => {
    server.columns = ['position'];
    server.sets = [
      {
        key: 's1',
        name: 'Accountants waiting',
        form: 'form1',
        filter: { states: ['submitted'], forms: [], fields: [{ field: 'position', test: 'oneOf', values: ['p2'] }] },
      },
    ];
    const { root, button, view } = await viewOf();
    button('Accountants waiting').click();
    await settle(view);
    expect(server.requests.at(-1)).toEqual({
      filter: { states: ['submitted'], forms: [], fields: [{ field: 'position', test: 'oneOf', values: ['p2'] }] },
      columns: [],
      count: true,
    });
    const headers = [...root.querySelectorAll('thead th')].map((th) => text(th));
    expect(headers).not.toContain('Position');
    expect(headers).not.toContain('State');
    expect([...root.querySelectorAll('.chip-text')].map((chip) => text(chip))).toEqual([
      'State: Waiting for approval',
      'Position: Accountant',
    ]);
  });

  it('searches once the typing stops, and says when nothing is found that sensitive fields are not searched', async () => {
    const { root, view } = await viewOf();
    server.rows = [];
    const search = must(root.querySelector<HTMLInputElement>('.search'), 'the search');
    search.value = 'Jana';
    search.dispatchEvent(new Event('input'));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await vi.waitFor(() =>
      expect(text(root.querySelector('.empty p'))).toBe(
        "No entries match \u201cJana\u201d. Sensitive fields aren't searched.",
      ),
    );
    expect(server.requests.at(-1)?.filter?.search).toBe('Jana');

    server.rows = [row(1)];
    must(
      [...root.querySelectorAll('uui-button')].find((one) => one.getAttribute('label') === 'Clear search'),
      'Clear search',
    ).click();
    await settle(view);
    expect(server.requests.at(-1)?.filter?.search).toBeUndefined();
  });

  it('filters through a dialog, shows the chips, removes one, saves the filters as a set and clears them', async () => {
    const { root, button, view } = await viewOf();
    opened.answers[ENTRY_FILTER_MODAL_ALIAS] = { filter: { states: ['approved', 'rejected'], forms: [], fields: [] } };
    button('State').click();
    await settle(view);
    expect(opened.modals.at(-1)?.data).toEqual({ part: 'state', filter: { states: [], forms: [], fields: [] } });
    expect(text(root.querySelector('.chip-text'))).toBe('State: Approved or Rejected');
    expect(server.requests.at(-1)?.filter?.states).toEqual(['approved', 'rejected']);

    // Save as... saves the filters; a name the form's sets have is said in the dialog.
    opened.answers[ENTRY_SET_NAME_MODAL_ALIAS] = true;
    button('Save as\u2026').click();
    await settle(view);
    const save = (opened.modals.at(-1)?.data as EntrySetNameModalData).save;
    server.setCode = 'name_taken';
    expect(await save('Done')).toBe('Another filter set of this form has this name.');
    server.setCode = undefined;
    expect(await save('Done')).toBeUndefined();
    expect(server.savedSets).toEqual([
      { name: 'Done', filter: { states: ['approved', 'rejected'], forms: [], fields: [] } },
    ]);

    button('Remove the filter State: Approved or Rejected').click();
    await settle(view);
    expect(root.querySelector('.chips')).toBeNull();
    expect(server.requests.at(-1)?.filter?.states).toEqual([]);
  });

  it('shows more after the last page, and the keyboard reads on at the first new row', async () => {
    server.page = 2;
    const { rows, button, view, said } = await viewOf();
    expect(rows()).toHaveLength(2);
    button('Show more').click();
    await vi.waitFor(() => expect(rows()).toHaveLength(3));
    expect(server.requests.at(-1)).toEqual({
      filter: { states: [], forms: [], fields: [] },
      columns: [],
      after: '2',
      count: false,
    });
    expect(view.shadowRoot?.activeElement).toBe(rows()[2]);
    expect(said()).toBe('1 entry more shown.');
  });

  it('approves the ticked entries that wait, says those left out, and Undo puts them back', async () => {
    const { rows, root, tick, button, view, peek } = await viewOf();
    expect(root.querySelector('table')?.classList.contains('selecting')).toBe(false);
    await tick(0);
    await tick(1);
    const bar = must(root.querySelector('.bulk'), 'the bulk bar');
    expect(text(bar.querySelector('.selected'))).toBe('2 selected');
    // While the bar is up, a row the cursor reaches stops above it.
    expect(root.querySelector('table')?.classList.contains('selecting')).toBe(true);
    // The first three actions show, the rest are in the "...".
    expect([...bar.querySelectorAll('.actions > uui-button')].map((one) => one.getAttribute('label'))).toEqual([
      'Approve',
      'Reject',
      'Mark as spam',
      'More actions for the selected entries',
    ]);

    button('Approve').click();
    await settle(view);
    expect(server.moves).toEqual([{ entries: ['e1'], state: 'approved' }]);
    expect(text(rows()[0]?.querySelector('uui-tag'))).toBe('Approved');
    expect(root.querySelector('.bulk')).toBeNull();
    expect(root.querySelector('table')?.classList.contains('selecting')).toBe(false);
    const toast = peek.mock.calls.at(-1)?.[1] as {
      duration: number;
      data: { message: string; until: number; tooLate: string; undo: () => Promise<void> };
    };
    expect(toast.data.message).toBe("1 entry approved. 1 wasn't waiting for approval.");
    // The toast offers Undo until the server's window ends, then says for a while that it is too late.
    expect([toast.data.until - Date.now(), toast.duration, toast.data.tooLate]).toEqual([
      10_000,
      16_000,
      "It's too late to undo this. Change the state back by hand if needed.",
    ]);

    await toast.data.undo();
    await settle(view);
    expect(server.undos).toEqual(['change1']);
    expect(text(rows()[0]?.querySelector('uui-tag'))).toBe('Waiting for approval');
  });

  it('says when it is too late to undo', async () => {
    server.undoAnswer = 'tooLate';
    const { tick, button, view, peek } = await viewOf();
    await tick(0);
    button('Approve').click();
    await settle(view);
    const toast = peek.mock.calls.at(-1)?.[1] as { data: { undo: () => Promise<void> } };
    await toast.data.undo();
    await settle(view);
    expect(peek).toHaveBeenLastCalledWith('warning', {
      data: { message: "It's too late to undo this. Change the state back by hand if needed." },
    });
  });

  it('asks before deleting, with the files that go too, and takes the rows away', async () => {
    const { rows, root, tick, view, peek } = await viewOf();
    await tick(0);
    await tick(2);
    must(root.querySelector<HTMLElement>('#bulk-more button'), 'Delete...').click();
    await settle(view);
    expect(server.deletes).toEqual([
      { entries: ['e1', 'e3'], confirm: false },
      { entries: ['e1', 'e3'], confirm: true },
    ]);
    expect(opened.confirmations.at(-1)).toMatchObject({
      headline: 'Delete 2 entries?',
      confirmLabel: 'Delete 2 entries',
    });
    expect(rows().map((one) => one.dataset['key'])).toEqual(['e2']);
    expect(text(root.querySelector('.count'))).toBe('1 entry');
    expect(peek).toHaveBeenLastCalledWith('positive', { data: { message: '2 entries deleted.' } });
  });

  it('keeps the entries when the user does not confirm the delete', async () => {
    opened.confirms = false;
    const { rows, root, tick, view } = await viewOf();
    await tick(0);
    must(root.querySelector<HTMLElement>('#bulk-more button'), 'Delete...').click();
    await settle(view);
    expect(server.deletes).toEqual([{ entries: ['e1'], confirm: false }]);
    expect(rows()).toHaveLength(3);
  });

  it('asks before running actions again, naming them, and says when there are none', async () => {
    const { root, tick, view, peek } = await viewOf();
    await tick(1);
    const more = [...root.querySelectorAll<HTMLElement>('#bulk-more button')];
    must(
      more.find((one) => text(one) === 'Re-run actions'),
      'Re-run actions',
    ).click();
    await settle(view);
    expect(opened.confirmations.at(-1)).toMatchObject({
      headline: 'Run actions again for 1 entry?',
      confirmLabel: 'Run actions for 1 entry',
    });
    expect(server.reruns.map((one) => one.confirm)).toEqual([false, true]);
    expect(peek).toHaveBeenLastCalledWith('positive', { data: { message: 'Actions queued for 1 entry.' } });

    server.rerunActions = [];
    await settle(view);
    const again = await viewOf();
    await again.tick(1);
    must(
      [...again.root.querySelectorAll<HTMLElement>('#bulk-more button')].find((one) => text(one) === 'Re-run actions'),
      'Re-run actions',
    ).click();
    await settle(again.view);
    expect(again.peek).toHaveBeenLastCalledWith('warning', {
      data: { message: 'None of these entries has an action to run again.' },
    });
  });

  it('offers only what the user may do', async () => {
    const { root, tick } = await viewOf((workspace) =>
      workspace.forms.setValue({ permissions: ['viewEntries', 'deleteEntries'] }),
    );
    await tick(0);
    expect([...root.querySelectorAll('.bulk .actions > uui-button')].map((one) => one.getAttribute('label'))).toEqual([
      'Delete\u2026',
    ]);
  });

  it('moves a cursor with j and k, approves the cursor row with a, searches with / and lists the keys with ?', async () => {
    const { rows, root, view, press } = await viewOf();
    await press('j');
    expect(rows()[0]?.classList.contains('cursor')).toBe(true);
    await press('j');
    await press('k');
    expect(view.shadowRoot?.activeElement).toBe(rows()[0]);
    await press('a', must(rows()[0], 'the first row'));
    expect(server.moves).toEqual([{ entries: ['e1'], state: 'approved' }]);
    await press('/');
    expect(view.shadowRoot?.activeElement).toBe(root.querySelector('.search'));
    // A key typed in the search box is the box's.
    await press('j', must(root.querySelector('.search'), 'the search'));
    expect(rows()[1]?.classList.contains('cursor')).toBe(false);
    await press('?');
    expect(opened.modals.at(-1)?.token).toBe(ENTRY_KEYS_MODAL_ALIAS);
  });

  it('says on first use what makes entries appear, without a toolbar, in the words of what the user may do', async () => {
    server.rows = [];
    const designer = await viewOf((workspace) => workspace.forms.setValue({ permissions: ['viewEntries', 'design'] }));
    expect(text(designer.root.querySelector('.empty p'))).toBe(
      'No entries yet. Share the form, or preview it and send a test entry.',
    );
    expect(designer.root.querySelector('.toolbar')).toBeNull();
    expect(designer.root.querySelector('.empty uui-button'), 'who may share it is told to').toBeNull();
    document.body.replaceChildren();
    const handler = await viewOf();
    expect(text(handler.root.querySelector('.empty p'))).toBe(
      'No entries yet. They appear here as soon as visitors send the form.',
    );
    // Everyone else is offered the summary email; the toast says what it is now.
    opened.answers[ENTRY_DIGEST_MODAL_ALIAS] = { frequency: 'daily' };
    handler.button('Email me a summary\u2026').click();
    await settle(handler.view);
    await settle(handler.view);
    expect(opened.modals.at(-1)?.token).toBe(ENTRY_DIGEST_MODAL_ALIAS);
    expect(handler.peek).toHaveBeenLastCalledWith('positive', {
      data: { message: "You'll get a summary each morning." },
    });
  });

  it("offers the summary email in the toolbar's menu, before the shortcuts, and says nothing when it is cancelled", async () => {
    const { root, button, view, peek } = await viewOf();
    const menu = [...root.querySelectorAll('#more-menu .menu > *')].map((item) => text(item));
    expect(menu).toEqual(['Email me a summary\u2026', 'Keyboard shortcuts']);
    button('Email me a summary\u2026').click();
    await settle(view);
    await settle(view);
    expect(opened.modals.at(-1)?.token).toBe(ENTRY_DIGEST_MODAL_ALIAS);
    expect(peek).not.toHaveBeenCalled();
  });

  it('says when the entries cannot be read, and reads them again on Try again', async () => {
    server.failsPage = true;
    const { root, button, view, rows } = await viewOf();
    expect(text(root.querySelector('[role="alert"] span'))).toBe("Entries couldn't be loaded.");
    server.failsPage = false;
    button('Try again').click();
    await vi.waitFor(() => expect(rows()).toHaveLength(3));
    await settle(view);
  });

  it('asks before moving entries back onto a full option, and overbooks only once the user agrees', async () => {
    server.asks = {
      full: [{ field: 'seat', fieldLabel: 'Seat', option: 'front', optionLabel: 'Front row', places: 2, taken: 2 }],
      held: [],
    };
    opened.confirms = false;
    const { tick, button, view, rows } = await viewOf();
    await tick(2);
    button('Approve').click();
    await settle(view);
    expect(opened.confirmations.at(-1)).toMatchObject({
      headline: 'Move it back anyway?',
      confirmLabel: 'Move back anyway',
    });
    expect(server.moves).toEqual([{ entries: ['e3'], state: 'approved' }]);
    expect(text(rows()[2]?.querySelector('uui-tag'))).toBe('Rejected');

    opened.confirms = true;
    await tick(2);
    button('Approve').click();
    await settle(view);
    expect(server.moves.slice(1)).toEqual([
      { entries: ['e3'], state: 'approved' },
      { entries: ['e3'], state: 'approved', overbook: true },
    ]);
    expect(text(rows()[2]?.querySelector('uui-tag'))).toBe('Approved');
  });
});

describe('the entry panel beside the list', () => {
  const panelIn = (view: Element) => shadow(view).querySelector<EntryPanelElement>('jfb-entry-panel');

  it('opens an entry beside the list, narrows the rows to name, state and time, and Esc closes it where it was', async () => {
    const { rows, root, view, press } = await viewOf();
    // Wide, the states have a column of their own.
    expect(rows()[0]?.querySelector('td uui-tag')).not.toBeNull();
    must(rows()[0]?.querySelector<HTMLElement>('.name'), 'the first name').click();
    await settle(view);
    const panel = must(panelIn(view), 'the panel');
    expect([panel.entry, panel.place, panel.rowShowsTitle]).toEqual(['e1', 'beside', true]);
    expect(view.hasAttribute('split')).toBe(true);
    expect(rows()[0]?.getAttribute('aria-current')).toBe('true');
    expect(root.querySelector('table')?.classList.contains('narrowed')).toBe(true);
    // Beside the panel a row's state goes under its name, so neither takes two lines.
    expect([...root.querySelectorAll('thead th')].map((one) => text(one))).not.toContain('State');
    expect(
      rows().every((row) => !row.querySelector('td uui-tag') && !!row.querySelector('th .state-below uui-tag')),
    ).toBe(true);
    // Beside the panel no chosen column shows, so there is nothing to choose, and no empty column for it.
    expect(root.querySelector('.choose, th.end, td.end')).toBeNull();
    // Opening marks the entry seen.
    await vi.waitFor(() => expect(rows()[0]?.classList.contains('unseen')).toBe(false));
    await settle(view);

    // While the panel's "..." is open, Esc closes that menu only (here the browser's own light dismiss).
    const menu = must(panel.shadowRoot?.querySelector<HTMLElement>('uui-popover-container'), 'the menu');
    const open = vi.spyOn(menu, 'matches').mockImplementation((selector: string) => selector === ':popover-open');
    await press('Escape', panel);
    expect(panelIn(view)).not.toBeNull();
    open.mockRestore();

    await press('Escape', panel);
    expect(panelIn(view)).toBeNull();
    expect(view.hasAttribute('split')).toBe(false);
    expect(view.shadowRoot?.activeElement).toBe(rows()[0]);
  });

  it('opens the cursor row on Enter, reads on with j, and a approves the open entry', async () => {
    const { rows, view, press } = await viewOf();
    await press('j');
    await press('Enter', must(rows()[0], 'the first row'));
    expect(panelIn(view)?.entry).toBe('e1');
    await press('j');
    // The second entry has no title, so its row names it by number and the panel's header leaves the number out.
    expect([panelIn(view)?.entry, panelIn(view)?.rowShowsTitle]).toEqual(['e2', false]);
    expect(view.shadowRoot?.activeElement).toBe(rows()[1]);
    await press('k');
    await settle(must(panelIn(view), 'the panel'));
    await press('a', must(rows()[0], 'the first row'));
    await settle(must(panelIn(view), 'the panel'));
    expect(server.moves).toEqual([{ entries: ['e1'], state: 'approved' }]);
    // The panel stays on the entry and the row's chip changes in place.
    expect(panelIn(view)?.entry).toBe('e1');
    expect(text(rows()[0]?.querySelector('uui-tag'))).toBe('Approved');
  });

  it('takes the row away when the entry is deleted in the panel', async () => {
    const { rows, root, view } = await viewOf();
    must(rows()[1]?.querySelector<HTMLElement>('.name'), 'the second name').click();
    await settle(view);
    must(panelIn(view), 'the panel').dispatchEvent(
      new CustomEvent('jfb-entry-gone', { detail: { key: 'e2', state: 'approved' }, bubbles: true, composed: true }),
    );
    await settle(view);
    expect(rows().map((one) => one.dataset['key'])).toEqual(['e1', 'e3']);
    expect(panelIn(view)).toBeNull();
    expect(text(root.querySelector('.count'))).toBe('2 entries');
  });

  it('covers the list where the view is narrow, which is out of reach until the panel closes', async () => {
    const before = globalThis.ResizeObserver;
    globalThis.ResizeObserver = class {
      constructor(private readonly report: ResizeObserverCallback) {}
      // Only the view is measured: the panel's own elements watch their size too.
      observe = (target: Element) => {
        if (target.localName === 'jfb-entries-view')
          this.report([{ contentRect: { width: 620 } } as ResizeObserverEntry], this as never);
      };
      unobserve = () => undefined;
      disconnect = () => undefined;
    } as never;
    try {
      const { rows, root, view } = await viewOf();
      // At 620 px, as on a phone or in German at 768 px, a row's state goes under its name.
      expect([...root.querySelectorAll('thead th')].map((one) => text(one))).not.toContain('State');
      expect(rows().every((row) => !!row.querySelector('th .state-below uui-tag'))).toBe(true);
      must(rows()[0]?.querySelector<HTMLElement>('.name'), 'the first name').click();
      await settle(view);
      expect(panelIn(view)?.place).toBe('covering');
      expect(view.hasAttribute('covering')).toBe(true);
      expect(root.querySelector('.list')?.hasAttribute('inert')).toBe(true);
      // Covered, the rows keep their columns for when the panel closes.
      expect(root.querySelector('table')?.classList.contains('narrowed')).toBe(false);
    } finally {
      globalThis.ResizeObserver = before;
    }
  });
});

describe('the Inbox and Spam', () => {
  const tags = (rows: HTMLElement[]) => rows.map((one) => text(one.querySelector('uui-tag')));

  it("lists every form's entries with their form, in the Inbox's own order, and offers the Form filter", async () => {
    server.rows = [row(1, { unseen: true }), row(2, { form: 'form2' }), row(3, { state: 'spam' })];
    const { rows, root, button, view } = await placeOf('inbox');
    expect(server.across[0]).toEqual({
      place: 'inbox',
      request: { filter: { states: [], forms: [], fields: [] }, columns: [], count: true },
    });
    expect(rows().map((one) => one.dataset['key'])).toEqual(['e1', 'e2']);
    expect(rows().map((one) => text(one.querySelector('td.form-column')))).toEqual(['Job application', 'Contact']);
    expect([...root.querySelectorAll('thead th')].map((th) => text(th))).toContain('Form');
    expect(server.viewsRead).toEqual([]);

    // Unseen first is the Inbox's own order, so Received is sorted only once chosen, and then newest first.
    expect(root.querySelector('thead th[aria-sort="descending"]')).toBeNull();
    must(root.querySelector<HTMLElement>('thead .sort'), 'the Received header').click();
    await settle(view);
    expect(server.across.at(-1)?.request.filter?.sort).toEqual({ field: null, descending: true });

    // The menu starts with Form; its State dialog offers no Spam, which has a list of its own.
    expect([...root.querySelectorAll('#filter-menu .menu > button')].map((one) => text(one))).toEqual([
      'Form',
      'State',
      'Received',
      'Actions',
    ]);
    button('State').click();
    await settle(view);
    expect((opened.modals.at(-1)?.data as { states: string[] }).states).toEqual([
      'unverified',
      'submitted',
      'approved',
      'rejected',
    ]);
  });

  it("offers one chosen form's fields and columns, and none while no form or several are chosen", async () => {
    server.rows = [row(1), row(2, { form: 'form2' })];
    const { root, button, view } = await placeOf('inbox');
    opened.answers[ENTRY_FORMS_MODAL_ALIAS] = { forms: [{ key: 'form1', name: 'Job application' }] };
    button('Form').click();
    await settle(view);
    expect(opened.modals.at(-1)).toEqual({ token: ENTRY_FORMS_MODAL_ALIAS, data: { chosen: [] } });
    expect(server.viewsRead).toEqual(['form1']);
    expect(server.across.at(-1)?.request.filter?.forms).toEqual(['form1']);
    expect([...root.querySelectorAll('.chip-text')].map((chip) => text(chip))).toEqual(['Form: Job application']);
    // The chip says the form once: the rows leave it out while it is the only one.
    expect(root.querySelector('.form-column, .form-below')).toBeNull();

    // Its fields are filters now, and the columns chosen are the user's for that form.
    expect(button('Position')).toBeTruthy();
    opened.answers[ENTRY_COLUMNS_MODAL_ALIAS] = { columns: ['position'] };
    button('Choose columns').click();
    await settle(view);
    expect(server.savedColumns).toEqual([['position']]);
    expect(server.across.at(-1)?.request.columns).toEqual(['position']);

    // Without it, its fields and columns go, and the chooser says why there are none.
    button('Remove the filter Form: Job application').click();
    await settle(view);
    expect(server.across.at(-1)?.request).toEqual({
      filter: { states: [], forms: [], fields: [] },
      columns: [],
      count: true,
    });
    opened.answers = {};
    button('Choose columns').click();
    await settle(view);
    expect(opened.modals.at(-1)?.data).toEqual({ fields: [], columns: [], noForm: true });
  });

  it('acts on each form in a request of its own, leaves out what a form does not allow, and one Undo takes all back', async () => {
    server.formPermissions = { form2: ['viewEntries'] };
    server.rows = [row(1), row(2), row(4, { form: 'form3' }), row(5, { form: 'form2' })];
    const { rows, tick, button, view, peek } = await placeOf('inbox');
    for (const index of [0, 1, 2, 3]) await tick(index);
    button('Approve').click();
    await settle(view);
    expect(server.moveForms).toEqual(['form1', 'form3']);
    expect(server.moves).toEqual([
      { entries: ['e1', 'e2'], state: 'approved' },
      { entries: ['e4'], state: 'approved' },
    ]);
    expect(tags(rows())).toEqual(['Approved', 'Approved', 'Approved', 'Waiting for approval']);
    const toast = peek.mock.calls.at(-1)?.[1] as { data: { message: string; undo: () => Promise<void> } };
    expect(toast.data.message).toBe("3 entries approved. 1 was left out: their form doesn't let you do this.");

    await toast.data.undo();
    await settle(view);
    expect(server.undos).toEqual(['change1', 'change-form3']);
    expect(tags(rows())).toEqual(Array.from({ length: 4 }, () => 'Waiting for approval'));
  });

  it('asks once before moving entries of several forms back, and moves no form before the user agrees', async () => {
    server.asks = { full: [], held: [{ field: 'email', fieldLabel: 'Email', value: 'a@b.c' }] } as never;
    server.askingForms = ['form2'];
    server.rows = [row(1, { state: 'rejected' }), row(2, { state: 'rejected', form: 'form2' })];
    const { tick, button, view, peek } = await placeOf('inbox');
    await tick(0);
    await tick(1);

    // Cancelled, nothing moved, also in the form that asked nothing.
    opened.confirms = false;
    button('Approve').click();
    await settle(view);
    await settle(view);
    expect(server.checks).toEqual(['form1', 'form2']);
    expect(opened.confirmations).toHaveLength(1);
    expect(server.moves).toEqual([]);
    expect(peek).not.toHaveBeenCalled();

    // Agreed, each form moves once, overbooking only where it asked.
    opened.confirms = true;
    button('Approve').click();
    await settle(view);
    await settle(view);
    expect(opened.confirmations).toHaveLength(2);
    expect(server.moveForms).toEqual(['form1', 'form2']);
    expect(server.moves.map((one) => one.overbook ?? false)).toEqual([false, true]);
  });

  it('lists Spam without a state, offers Not spam and Delete..., and a row Not spam took out says its state', async () => {
    server.rows = [row(1, { state: 'spam' }), row(2, { state: 'spam', form: 'form2' }), row(3)];
    const { rows, root, tick, button, view } = await placeOf('spam');
    expect(server.across[0]?.place).toBe('spam');
    expect(rows().map((one) => one.dataset['key'])).toEqual(['e1', 'e2']);
    expect([...root.querySelectorAll('thead th')].map((th) => text(th))).not.toContain('State');
    expect(root.querySelector('tbody uui-tag')).toBeNull();
    // In Spam every entry is in Spam and runs no action: the menu has neither State nor Actions.
    expect([...root.querySelectorAll('#filter-menu .menu > button')].map((one) => text(one))).toEqual([
      'Form',
      'Received',
    ]);

    await tick(0);
    expect([...root.querySelectorAll('.bulk .actions > uui-button')].map((one) => one.getAttribute('label'))).toEqual([
      'Not spam',
      'Delete\u2026',
    ]);
    button('Not spam').click();
    await settle(view);
    expect(server.moves).toEqual([{ entries: ['e1'], state: 'submitted' }]);
    expect(text(rows()[0]?.querySelector('.moved-state'))).toBe('Waiting for approval');
    expect(rows()[1]?.querySelector('.moved-state')).toBeNull();
  });

  it('empties Spam after asking how many go and stay, and offers it only to who may delete entries', async () => {
    server.anywhere = ['viewEntries', 'deleteEntries'];
    server.rows = [
      row(1, { state: 'spam' }),
      row(2, { state: 'spam', form: 'form2' }),
      row(3, { state: 'spam', form: 'form3' }),
    ];
    server.emptyPlan = { entries: 2, files: 1, more: false };
    const { button, view, peek } = await placeOf('spam');
    button('Empty spam\u2026').click();
    await settle(view);
    expect(server.empties).toEqual([false, true]);
    expect(opened.confirmations.at(-1)).toMatchObject({ headline: 'Empty Spam?', confirmLabel: 'Delete 2 entries' });
    // The content goes as text, in a template of its one value (ui/confirm.ts).
    expect((opened.confirmations.at(-1) as { content?: { values: unknown[] } }).content?.values).toEqual([
      "2 entries in Spam go for good. 1 entry stays: its form doesn't let you delete entries. " +
        "This also deletes their uploaded file and their timelines. You can't undo this.",
    ]);
    expect(peek).toHaveBeenCalledWith('positive', { data: { message: '2 entries deleted from Spam.' } });
    // Emptied, the list reads again.
    expect(server.across.filter((one) => one.request.count)).toHaveLength(2);

    document.body.replaceChildren();
    server.anywhere = ['viewEntries'];
    const viewer = await placeOf('spam');
    expect(viewer.root.querySelector('uui-button[label="Empty spam\u2026"]')).toBeNull();
  });

  it('counts no entry as staying that Not spam already took out of Spam', async () => {
    server.anywhere = ['deleteEntries'];
    server.rows = [row(1, { state: 'spam' }), row(2, { state: 'spam' }), row(3, { state: 'spam' })];
    server.emptyPlan = { entries: 2, files: 0, more: false };
    const { tick, button, view } = await placeOf('spam');
    await tick(0);
    button('Not spam').click();
    await settle(view);
    button('Empty spam\u2026').click();
    await settle(view);
    expect((opened.confirmations.at(-1) as { content?: { values: unknown[] } }).content?.values).toEqual([
      "2 entries in Spam go for good. This also deletes their timelines. You can't undo this.",
    ]);
  });

  it('says its own first use, without a toolbar', async () => {
    server.rows = [];
    const inbox = await placeOf('inbox');
    expect(text(inbox.root.querySelector('.empty p'))).toBe(
      'No entries yet. They arrive here from every form you may view.',
    );
    expect(inbox.root.querySelector('.toolbar')).toBeNull();
    expect(
      inbox.root.querySelector('.empty uui-button')?.getAttribute('label'),
      'the Inbox offers the summary email',
    ).toBe('Email me a summary\u2026');
    document.body.replaceChildren();
    const spam = await placeOf('spam');
    expect(text(spam.root.querySelector('.empty p'))).toBe(
      'No spam. Entries that look automated land here, and nothing in them is lost.',
    );
    expect(spam.root.querySelector('.empty uui-button')).toBeNull();
  });

  it("offers the summary email in the Inbox's menu, not in Spam's", async () => {
    server.rows = [row(1), row(2, { state: 'spam' })];
    const menuOf = async (place: 'inbox' | 'spam') => {
      const list = await placeOf(place);
      const items = [...list.root.querySelectorAll('#more-menu .menu > *')].map((item) => text(item));
      document.body.replaceChildren();
      return items;
    };
    expect(await menuOf('inbox')).toEqual(['Email me a summary\u2026', 'Keyboard shortcuts']);
    expect(await menuOf('spam')).toEqual(['Keyboard shortcuts']);
  });

  it('opens an entry on its own form, the panel naming the form, and in Spam without the state', async () => {
    server.rows = [row(1), row(2, { form: 'form2' })];
    const inbox = await placeOf('inbox');
    must(inbox.rows()[1]?.querySelector<HTMLElement>('.name'), 'the second name').click();
    await settle(inbox.view);
    const panel = must(shadow(inbox.view).querySelector<EntryPanelElement>('jfb-entry-panel'), 'the panel');
    expect([panel.form, panel.showsForm, panel.hidesState]).toEqual(['form2', true, false]);
    // Beside the panel the rows leave their form out, which the panel's header names.
    expect(inbox.root.querySelector('td.form-column, th.form-column')).toBeNull();

    document.body.replaceChildren();
    server.rows = [row(1, { state: 'spam' })];
    const spam = await placeOf('spam');
    must(spam.rows()[0]?.querySelector<HTMLElement>('.name'), 'the name').click();
    await settle(spam.view);
    expect(shadow(spam.view).querySelector<EntryPanelElement>('jfb-entry-panel')?.hidesState).toBe(true);
  });

  it("names the form of a set, filters the Inbox to it when chosen, and saves a set of one form as that form's", async () => {
    server.inboxSets = [
      {
        key: 's1',
        name: 'Accountants',
        form: 'form1',
        filter: { states: ['submitted', 'spam'], forms: [], fields: [] },
      },
    ];
    server.inboxForms = [{ key: 'form1', name: 'Job application', permissions: server.everything }];
    const { root, button, view } = await placeOf('inbox');
    const set = must(
      [...root.querySelectorAll<HTMLElement>('.set button')].find((one) => text(one).startsWith('Accountants')),
      'the set',
    );
    expect(text(set.querySelector('.set-form'))).toBe('Job application');
    set.click();
    await settle(view);
    expect(server.across.at(-1)?.request.filter).toEqual({ forms: ['form1'], states: ['submitted'], fields: [] });
    expect(server.viewsRead).toEqual(['form1']);

    opened.answers[ENTRY_SET_NAME_MODAL_ALIAS] = true;
    button('Save as\u2026').click();
    await settle(view);
    expect(await (opened.modals.at(-1)?.data as EntrySetNameModalData).save('Mine')).toBeUndefined();
    expect(server.setForms).toEqual(['form1']);
  });
});
