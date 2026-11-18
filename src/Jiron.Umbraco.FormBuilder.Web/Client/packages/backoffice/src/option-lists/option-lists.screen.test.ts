// Library > Option lists and its create dialog (ROADMAP 12.1, IA 7, ADR-0214): the lists by name with their kind and the
// forms that use them, each name a link to its list; a page more on request, the focus on its first row; one sentence
// when there are none; Try again after a failed read and the permission a user lacks; and Create option list, which asks
// the kind first and opens the new list of that kind. The dialog offers the seven kinds in order, each with its line.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { OptionListRowResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { NewOptionListModalValue } from './new-option-list.modal.token';

const api = vi.hoisted(() => ({
  rows: [] as unknown[],
  asked: [] as unknown[],
  chosen: undefined as unknown,
}));

// What the user holds, which the place's tabs draw from.
vi.mock('../section/my-permissions', () => ({
  myPermissions: async () => ({ global: ['manageLibrary'], anywhere: [] }),
  myPermissionsNow: () => undefined,
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async () => (api.chosen ? api.chosen : Promise.reject(new Error('closed'))),
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  optionListLibraryRows: async (options: { query: unknown }) => {
    api.asked.push(options.query);
    return api.rows.shift();
  },
}));

const { OptionListsScreenElement } = await import('./option-lists.screen');
const { NewOptionListModalElement } = await import('./new-option-list.modal.element');

/** The elements of a root that match the selector. */
const all = (root: ParentNode, selector: string): HTMLElement[] => [...root.querySelectorAll<HTMLElement>(selector)];

const row = (at: number, more: Partial<OptionListRowResponseModel> = {}): OptionListRowResponseModel => ({
  key: `00000000-0000-4000-8000-${String(at).padStart(12, '0')}`,
  alias: `list${at}`,
  name: `List ${at}`,
  kind: 'manual',
  usedBy: at,
  updatedUtc: '2026-11-08T10:00:00Z',
  ...more,
});

async function screenOf(...reads: unknown[]) {
  api.rows = reads;
  api.asked = [];
  const host = testHost();
  const screen = new OptionListsScreenElement();
  host.append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('uui-box, .empty')).not.toBeNull());
  await settle(screen);
  return { screen, root: shadow(screen) };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  api.chosen = undefined;
  vi.restoreAllMocks();
});

describe('Library \u203a Option lists', () => {
  it('lists each list by name with its kind and the forms that use it, the name a link to the list', async () => {
    const { root } = await screenOf({
      data: { total: 3, items: [row(0, { kind: 'url' }), row(1, { kind: 'spreadsheet' }), row(2)] },
    });
    expect(all(root, 'th').map(text)).toEqual(['Name', 'Kind', 'Used by']);
    expect(all(root, 'tbody tr').map((each) => [...each.querySelectorAll('td')].map(text))).toEqual([
      ['List 0', 'A JSON address', 'no forms'],
      ['List 1', 'A kind this version lacks', '1 form'],
      ['List 2', 'Fixed options', '2 forms'],
    ]);
    expect(root.querySelector('tbody a')?.getAttribute('href')).toBe(
      'section/jfb-forms/workspace/jfb-option-list/edit/00000000-0000-4000-8000-000000000000',
    );
    // No list uses it: said more quietly.
    expect(root.querySelector('tbody td.muted')).not.toBeNull();
    expect(root.querySelector('.more')).toBeNull();
    expect(root.querySelector('h1, h2')).toBeNull();
    expect(api.asked).toEqual([{ skip: 0, take: 100 }]);
  });

  it('reads a page more on request, and puts the focus on its first row', async () => {
    const first = Array.from({ length: 100 }, (_, at) => row(at));
    const { root, screen } = await screenOf(
      { data: { total: 101, items: first } },
      { data: { total: 101, items: [row(99), row(100)] } },
    );
    must(root.querySelector<HTMLElement>('.more uui-button'), 'Load more').click();
    await vi.waitFor(() => expect(all(root, 'tbody tr')).toHaveLength(101));
    await settle(screen);
    expect(api.asked).toEqual([
      { skip: 0, take: 100 },
      { skip: 100, take: 100 },
    ]);
    // The row read twice shows once.
    expect(root.activeElement?.textContent).toBe('List 100');
    expect(root.querySelector('.more')).toBeNull();
  });

  it('says what lists are for when there are none, with Create option list in the footer', async () => {
    const { root } = await screenOf({ data: { total: 0, items: [] } });
    expect(text(root.querySelector('.empty'))).toBe(
      'No option lists yet. Make one when several forms offer the same choices, so they change in one place.',
    );
    const create = must(root.querySelector('uui-button[slot="actions"]'), 'Create option list');
    expect([create.getAttribute('label'), create.getAttribute('look'), create.getAttribute('color')]).toEqual([
      'Create option list',
      'primary',
      'positive',
    ]);
  });

  it('offers Try again after a read that failed, and says the permission a user lacks', async () => {
    const failed = await screenOf({}, { data: { total: 0, items: [] } });
    expect(text(failed.root.querySelector('[role="alert"]'))).toMatch(/^The option lists couldn't be loaded\./);
    expect(failed.root.querySelector('uui-button[slot="actions"]')).toBeNull();
    must(failed.root.querySelector<HTMLElement>('.failed uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(failed.root.querySelector('.empty')).not.toBeNull());
    document.body.replaceChildren();

    const forbidden = await screenOf({
      error: new UmbApiError('forbidden', 403, null, { code: 'forbidden', permission: 'manageLibrary' } as never),
    });
    expect(text(forbidden.root.querySelector('[role="alert"]'))).toContain('Manage the library');
  });

  it('asks the kind first, then opens the new list of that kind; closing the dialog opens nothing', async () => {
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { root, screen } = await screenOf({ data: { total: 0, items: [] } });
    const create = () =>
      must(root.querySelector<HTMLElement>('uui-button[slot="actions"]'), 'Create option list').click();
    create();
    await settle(screen);
    expect(pushed).not.toHaveBeenCalled();
    api.chosen = { kind: 'url' } satisfies NewOptionListModalValue;
    create();
    await vi.waitFor(() => expect(pushed).toHaveBeenCalledOnce());
    expect(String(pushed.mock.calls[0]?.[2])).toMatch(/\/section\/jfb-forms\/workspace\/jfb-option-list\/create\/url$/);
  });
});

describe('the create dialog', () => {
  it('offers the seven kinds in order, each with its line and icon, and the one chosen is the answer', async () => {
    const state = new UmbObjectState<Partial<NewOptionListModalValue>>({});
    const modal = {
      value: state.asObservable(),
      updateValue: (partial: Partial<NewOptionListModalValue>) => state.update(partial),
      getValue: () => state.getValue(),
      submit: vi.fn(),
      reject: vi.fn(),
    };
    const dialog = new NewOptionListModalElement();
    dialog.modalContext = modal as never;
    dialog.data = {};
    document.body.append(dialog);
    await settle(dialog);
    const root = shadow(dialog);
    expect(root.querySelector('uui-dialog-layout')?.getAttribute('headline')).toBe('New option list');
    expect(text(root.querySelector('p'))).toBe('Where do its options come from?');
    const kinds = all(root, 'umb-ref-item');
    expect(kinds.map((kind) => [kind.getAttribute('name'), kind.getAttribute('icon')])).toEqual([
      ['Fixed options', 'icon-bulleted-list'],
      ['Pages', 'icon-document'],
      ["A data type's values", 'icon-autofill'],
      ['A JSON address', 'icon-link'],
      ['An upload', 'icon-cloud-upload'],
      ['Countries', 'icon-globe'],
      ['A standard list', 'icon-code'],
    ]);
    expect(kinds[3]?.getAttribute('detail')).toBe('Fetched from a web address and kept for a while');
    kinds[4]?.dispatchEvent(new CustomEvent('open'));
    expect(state.getValue()).toEqual({ kind: 'file' });
    expect(modal.submit).toHaveBeenCalledOnce();
    // Dispatched: the test DOM drops a synthetic click() on the dialog's button.
    must(root.querySelector<HTMLElement>('uui-button[slot="actions"]'), 'Cancel').dispatchEvent(new Event('click'));
    expect(modal.reject).toHaveBeenCalledOnce();
  });
});
