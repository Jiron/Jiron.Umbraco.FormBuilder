// A form's Entries view (ROADMAP 11.1, IA 6.1, ADR-0207): its rows, names and marks, the count, the columns and what a
// filter fixes, the search with its empty state, the chips with Save as... and Clear filters, Show more, the bulk bar
// with Undo, Delete... and Re-run actions asked first, the keys of section 4.13, the first use and a read that fails.
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  EntryListFieldResponseModel,
  EntryListFilterRequestModel,
  EntryListPageResponseModel,
  EntryListRequestModel,
  EntryListRowResponseModel,
} from '../../api';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import {
  ENTRY_COLUMNS_MODAL_ALIAS,
  ENTRY_FILTER_MODAL_ALIAS,
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
  moves: [] as { entries: readonly string[]; state: string }[],
  undos: [] as string[],
  undoAnswer: 'done' as 'done' | 'tooLate' | 'failed',
  deletes: [] as { entries: readonly string[]; confirm: boolean }[],
  reruns: [] as { entries: readonly string[]; confirm: boolean }[],
  rerunActions: [{ key: 'a1', type: 'webhook', name: 'Tell the team', trigger: 'approved' }],
  savedSets: [] as { name: string; filter: EntryListFilterRequestModel }[],
  setCode: undefined as string | undefined,
  savedColumns: [] as (readonly string[])[],
}));

vi.mock('../../entries/entries-api', () => ({
  readView: async () =>
    server.failsView ? undefined : { fields: server.fields, columns: server.columns, filterSets: server.sets },
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
    return { items, next, ...(request.count ? { total: server.rows.length } : {}), more: false };
  },
  saveColumns: async (_host: unknown, _form: string, columns: readonly string[]) => {
    server.savedColumns.push(columns);
    return true;
  },
  saveSet: async (_host: unknown, key: string, name: string, form: string, filter: EntryListFilterRequestModel) => {
    if (server.setCode) return { code: server.setCode };
    server.savedSets.push({ name, filter });
    return { set: { key, name, form, filter } };
  },
  deleteSet: async () => true,
  changeState: async (_host: unknown, _form: string, entries: readonly string[], state: string) => {
    server.moves.push({ entries, state });
    return {
      changeKey: 'change1',
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

const { EntriesViewElement } = await import('./entries.view');

// The tests run at a fixed midday, so an entry received some minutes before was received today.
const MIDDAY = new Date(2026, 10, 7, 12, 0);

const row = (number: number, more: Partial<EntryListRowResponseModel> = {}): EntryListRowResponseModel => ({
  key: `e${number}`,
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
  return { workspace, view, root, rows, said, button, tick, press, peek };
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
    undos: [],
    undoAnswer: 'done',
    deletes: [],
    reruns: [],
    rerunActions: [{ key: 'a1', type: 'webhook', name: 'Tell the team', trigger: 'approved' }],
    savedSets: [],
    setCode: undefined,
    savedColumns: [],
  });
});

afterEach(() => {
  vi.useRealTimers();
  document.body.replaceChildren();
});

describe("a form's Entries view", () => {
  it('lists the entries by title or number, with the unseen mark, the state, when received and the action status', async () => {
    const { rows, root } = await viewOf();
    expect(server.requests[0]).toEqual({ filter: { states: [], fields: [] }, columns: [], count: true });
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
        filter: { states: ['submitted'], fields: [{ field: 'position', test: 'oneOf', values: ['p2'] }] },
      },
    ];
    const { root, button, view } = await viewOf();
    button('Accountants waiting').click();
    await settle(view);
    expect(server.requests.at(-1)).toEqual({
      filter: { states: ['submitted'], fields: [{ field: 'position', test: 'oneOf', values: ['p2'] }] },
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
    opened.answers[ENTRY_FILTER_MODAL_ALIAS] = { filter: { states: ['approved', 'rejected'], fields: [] } };
    button('State').click();
    await settle(view);
    expect(opened.modals.at(-1)?.data).toEqual({ part: 'state', filter: { states: [], fields: [] } });
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
    expect(server.savedSets).toEqual([{ name: 'Done', filter: { states: ['approved', 'rejected'], fields: [] } }]);

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
      filter: { states: [], fields: [] },
      columns: [],
      after: '2',
      count: false,
    });
    expect(view.shadowRoot?.activeElement).toBe(rows()[2]);
    expect(said()).toBe('1 entry more shown.');
  });

  it('approves the ticked entries that wait, says those left out, and Undo puts them back', async () => {
    const { rows, root, tick, button, view, peek } = await viewOf();
    await tick(0);
    await tick(1);
    const bar = must(root.querySelector('.bulk'), 'the bulk bar');
    expect(text(bar.querySelector('.selected'))).toBe('2 selected');
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
    document.body.replaceChildren();
    const handler = await viewOf();
    expect(text(handler.root.querySelector('.empty p'))).toBe(
      'No entries yet. They appear here as soon as visitors send the form.',
    );
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
});
