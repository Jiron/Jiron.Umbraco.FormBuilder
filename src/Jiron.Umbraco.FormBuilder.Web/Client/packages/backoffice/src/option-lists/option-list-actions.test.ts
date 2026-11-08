// An option list's "..." (ROADMAP 12.1, IA 7, ADR-0214): Duplicate makes a copy named after the list, says so and opens
// it; Delete... names the forms that use a list instead of deleting it, asks first otherwise, deletes through the
// workspace's repository and leaves the list's workspace for the list screen; a delete refused since says why, and a
// form that took the list up meanwhile is named. The data source makes a new list's scaffold with its kind's keep time,
// and hands a refused save's problems to its host first.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { OptionListResponseModel } from '../api';
import { registerEnglish, settle, testHost } from '../testing';
import { loadOptionListWords } from './option-list-words';

const api = vi.hoisted(() => ({
  reads: [] as unknown[],
  users: [] as unknown[],
  duplicates: [] as unknown[],
  duplicated: [] as unknown[],
  deletes: [] as unknown[],
  confirmed: true,
  confirms: [] as unknown[],
  infos: [] as { headline: string }[],
  saves: [] as unknown[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbConfirmModal: async (_host: unknown, data: unknown) => {
    api.confirms.push(data);
    if (!api.confirmed) throw new Error('cancelled');
  },
  umbInfoModal: async (_host: unknown, data: { headline: string }) => {
    api.infos.push(data);
  },
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  optionListLibraryGet: async () => api.reads.shift(),
  optionListLibraryUsedBy: async () => api.users.shift(),
  optionListLibraryCreate: async () => api.saves.shift(),
  optionListLibraryUpdate: async () => api.saves.shift(),
  optionListLibraryDuplicate: async (options: unknown) => {
    api.duplicated.push(options);
    return api.duplicates.shift();
  },
}));

const repository = vi.hoisted(() => ({ deleted: [] as string[], destroyed: 0 }));

vi.mock('./option-list-detail.repository', () => ({
  OptionListDetailRepository: class {
    async delete(key: string) {
      repository.deleted.push(key);
      return (api.deletes.shift() as { error?: Error } | undefined) ?? {};
    }
    destroy() {
      repository.destroyed++;
    }
  },
}));

const { DuplicateOptionListAction } = await import('./duplicate-option-list.action');
const { DeleteOptionListAction } = await import('./delete-option-list.action');
const { OptionListDetailDataSource, SAVE_PROBLEMS, scaffoldOf } = await import('./option-list-detail.data-source');

const KEY = 'a1b2c3d4-0000-4000-8000-000000000001';

const response = (more: Partial<OptionListResponseModel> = {}): OptionListResponseModel => ({
  key: KEY,
  alias: 'sizes',
  name: 'Sizes',
  kind: 'manual',
  config: {},
  items: [],
  cacheSeconds: 0,
  concurrencyVersion: 2,
  updatedUtc: '2026-11-08T10:00:00Z',
  ...more,
});

function hosted() {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  return { host, peek };
}

const args = { entityType: 'jfb-option-list', unique: KEY, meta: {} } as never;

beforeAll(async () => {
  registerEnglish();
  await loadOptionListWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
  Object.assign(api, {
    reads: [],
    users: [],
    duplicates: [],
    duplicated: [],
    deletes: [],
    confirms: [],
    infos: [],
    saves: [],
  });
  api.confirmed = true;
  repository.deleted = [];
  repository.destroyed = 0;
  vi.restoreAllMocks();
});

describe("an option list's Duplicate", () => {
  it('makes a copy named after the list, says so and opens it', async () => {
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { host, peek } = hosted();
    api.reads = [{ data: response() }];
    api.duplicates = [{ data: response({ key: 'b2', name: 'Sizes (copy)', alias: 'sizes2' }) }];
    await new DuplicateOptionListAction(host, args).execute();
    expect(api.duplicated).toEqual([{ path: { key: KEY }, body: { name: 'Sizes (copy)' } }]);
    expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'Sizes (copy) was made.' } });
    expect(String(pushed.mock.calls[0]?.[2])).toMatch(/\/section\/jfb-forms\/workspace\/jfb-option-list\/edit\/b2$/);
  });

  it('says a copy that was refused, and opens nothing', async () => {
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { host, peek } = hosted();
    api.reads = [{ data: response() }];
    api.duplicates = [{ error: new UmbApiError('refused', 500, null, {} as never) }];
    await new DuplicateOptionListAction(host, args).execute();
    expect(peek).toHaveBeenCalledWith('danger', { data: { message: "The copy couldn't be made." } });
    expect(pushed).not.toHaveBeenCalled();
  });
});

describe("an option list's Delete...", () => {
  it('names the forms that use the list instead of deleting it', async () => {
    const { host } = hosted();
    api.reads = [{ data: response() }];
    api.users = [
      {
        data: {
          forms: [{ key: 'f', name: 'Order', inDraft: true, inLive: true, isTrashed: false, uses: 1 }],
          others: 1,
        },
      },
    ];
    await new DeleteOptionListAction(host, args).execute();
    expect(api.infos.map((info) => info.headline)).toEqual(['Sizes is in use']);
    const content = (api.infos[0] as unknown as { content: { values: unknown[] } }).content;
    expect(JSON.stringify(content.values)).toContain('Order');
    expect(api.confirms).toEqual([]);
    expect(repository.deleted).toEqual([]);
  });

  it('asks first, deletes through the repository, says so and leaves the list for the list screen', async () => {
    const path = `/umbraco/section/jfb-forms/workspace/jfb-option-list/edit/${KEY}/options`;
    history.replaceState(null, '', path);
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { host, peek } = hosted();
    api.reads = [{ data: response() }];
    api.users = [{ data: { forms: [], others: 0 } }];
    await new DeleteOptionListAction(host, args).execute();
    expect(api.confirms).toEqual([
      expect.objectContaining({
        headline: 'Delete Sizes?',
        confirmLabel: 'Delete',
        color: 'danger',
      }),
    ]);
    expect(repository).toEqual({ deleted: [KEY], destroyed: 1 });
    expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'Sizes was deleted.' } });
    expect(String(pushed.mock.calls[0]?.[2])).toMatch(/\/section\/jfb-forms\/workspace\/jfb-option-lists$/);
    history.replaceState(null, '', '/');
  });

  it('deletes nothing when the question is cancelled', async () => {
    const { host } = hosted();
    api.reads = [{ data: response() }];
    api.users = [{ data: { forms: [], others: 0 } }];
    api.confirmed = false;
    await expect(new DeleteOptionListAction(host, args).execute()).rejects.toThrow('cancelled');
    expect(repository.deleted).toEqual([]);
  });

  it('says a delete refused since, and names a form that took the list up meanwhile', async () => {
    const { host, peek } = hosted();
    api.reads = [{ data: response() }, { data: response() }, { data: response() }];
    api.users = [
      { data: { forms: [], others: 0 } },
      { data: { forms: [], others: 0 } },
      { data: { forms: [], others: 0 } },
      { data: { forms: [], others: 3 } },
    ];
    api.deletes = [{ error: new Error('conflict') }, { error: new Error('failed') }, { error: new Error('inUse') }];
    for (let round = 0; round < 3; round++) await new DeleteOptionListAction(host, args).execute();
    expect(peek.mock.calls).toEqual([
      [
        'danger',
        { data: { message: 'Someone else changed this list meanwhile. Load it again, then make your change.' } },
      ],
      ['danger', { data: { message: "The list couldn't be deleted." } }],
    ]);
    expect(api.infos.map((info) => info.headline)).toEqual(['Sizes is in use']);
  });
});

describe("the option list workspace's data source", () => {
  it("makes a new list's scaffold of its kind with the kind's keep time, a kind it lacks as fixed options", () => {
    expect(scaffoldOf('url', 'k')).toEqual({
      entityType: 'jfb-option-list',
      unique: 'k',
      name: '',
      alias: '',
      kind: 'url',
      config: {},
      items: [],
      cacheSeconds: 3600,
      concurrencyVersion: 0,
    });
    expect(scaffoldOf('spreadsheet', 'k').kind).toBe('manual');
  });

  it("hands a refused save's problems to its host first, and clears them after a save that went through", async () => {
    const { host, peek } = hosted();
    const said: unknown[] = [];
    host.addEventListener(SAVE_PROBLEMS, (event) => said.push((event as Event & { problems: unknown }).problems));
    const source = new OptionListDetailDataSource(host);
    const { data: scaffold } = await source.createScaffold({ kind: 'code' });
    expect(scaffold?.kind).toBe('code');
    expect(scaffold?.unique).toMatch(/^[0-9a-f-]{36}$/);
    const model = { ...scaffoldOf('manual', KEY), name: 'Sizes', alias: 'sizes' };
    const problem = { code: 'required', path: '$.items', message: 'Add an option.' };
    api.saves = [
      { error: new UmbApiError('refused', 400, null, { code: 'validation_failed', errors: [problem] } as never) },
      { data: response() },
      { error: new UmbApiError('refused', 409, null, { code: 'concurrency_conflict' } as never) },
    ];
    const refused = await source.create(model);
    expect(refused.error?.message).toBe(SAVE_PROBLEMS);
    expect(said).toEqual([[problem]]);
    const saved = await source.update({ ...model, concurrencyVersion: 2 });
    expect(saved.data?.concurrencyVersion).toBe(2);
    expect(said).toEqual([[problem], []]);
    // Saved meanwhile: said as a toast, the problems left as they were.
    const conflict = await source.update({ ...model, concurrencyVersion: 1 });
    await settle();
    expect(conflict.error?.message).toBe('conflict');
    expect(said).toHaveLength(2);
    expect(peek).toHaveBeenCalledWith('danger', {
      data: { message: expect.stringMatching(/changed/i) },
    });
  });
});
