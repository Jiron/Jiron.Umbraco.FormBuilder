// A save of the form workspace (ROADMAP 9.3a and 10.3c, ADR-0200): a draft someone saved since the form was read, or a
// name both changed, is a conflict, said to the workspace, and nothing is sent; a move, an access change or a publish
// is none. The draft is saved before the name, each with the version the last answer gave; a conflict the server finds
// is a conflict too, never a toast that invites a second save; and a name refused after the draft went in is said, with
// the form as saved read again.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FormDraftResponseModel, FormResponseModel } from '../api';
import { registerEnglish, testHost } from '../testing';

/** What the API answers, in turn, and what it was asked. */
const api = vi.hoisted(() => ({
  draft: undefined as unknown,
  saves: [] as unknown[],
  renames: [] as unknown[],
  asked: [] as string[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  formGet: async () => {
    api.asked.push('read form');
    return { data: (api.draft as FormDraftResponseModel).form };
  },
  formDraft: async () => {
    api.asked.push('read draft');
    return { data: api.draft };
  },
  formSaveDraft: async (options: { body: { concurrencyVersion: number } }) => {
    api.asked.push(`save draft at ${String(options.body.concurrencyVersion)}`);
    return api.saves.shift();
  },
  formRename: async (options: { body: { concurrencyVersion: number; name: string } }) => {
    api.asked.push(`rename to ${options.body.name} at ${String(options.body.concurrencyVersion)}`);
    return api.renames.shift();
  },
}));

const { detailOf, FormDetailDataSource, SAVE_CONFLICT } = await import('./form-detail.data-source');

const FORM: FormResponseModel = {
  key: 'f',
  alias: 'contact',
  name: 'Contact us',
  folderKey: null,
  concurrencyVersion: 3,
  isPublished: false,
  publicRevision: 0,
  isRestricted: false,
  trashedUtc: null,
  createdUtc: '2026-11-03T08:00:00Z',
  updatedUtc: '2026-11-03T08:00:00Z',
  permissions: ['design'],
};

const DRAFT: FormDraftResponseModel = {
  form: FORM,
  definition: { pages: [{ key: 'p', groups: [] }] },
  problems: [],
  draftVersion: 2,
  publishedVersion: null,
  hasUnpublishedChanges: true,
  hasEntries: false,
};

/** The draft as the user changed it: another page, and a new name. */
const changed = () => ({
  ...detailOf(DRAFT),
  name: 'Write to us',
  definition: {
    pages: [
      { key: 'p', groups: [] },
      { key: 'q', groups: [] },
    ],
  },
});

const refusal = (status: number, code: string) => ({
  error: new UmbApiError('refused', status, null, { code, title: 'Refused' } as never),
});

function sourceOf() {
  const host = testHost();
  const peek = vi.fn();
  const conflicts = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  host.addEventListener(SAVE_CONFLICT, conflicts);
  return { source: new FormDetailDataSource(host as never), peek, conflicts };
}

beforeAll(registerEnglish);

beforeEach(() => {
  api.draft = DRAFT;
  api.saves = [];
  api.renames = [];
  api.asked = [];
});

describe('a save', () => {
  it('sends nothing for a draft someone saved since it was read, and says a conflict, not a toast', async () => {
    api.draft = { ...DRAFT, form: { ...FORM, concurrencyVersion: 4 }, draftVersion: 3 };
    const { source, peek, conflicts } = sourceOf();
    const answer = await source.update(changed());
    expect(answer.error?.message).toBe(SAVE_CONFLICT);
    expect(api.asked).toEqual(['read draft']);
    expect(peek).not.toHaveBeenCalled();
    expect(conflicts).toHaveBeenCalledOnce();
  });

  it('sends nothing for a name both changed, and keeps a name someone else changed where the user did not', async () => {
    api.draft = { ...DRAFT, form: { ...FORM, name: 'Get in touch', concurrencyVersion: 4 } };
    const both = sourceOf();
    expect((await both.source.update(changed())).error?.message).toBe(SAVE_CONFLICT);
    expect(both.conflicts).toHaveBeenCalledOnce();

    api.asked = [];
    api.saves = [{ data: { form: { ...FORM, name: 'Get in touch', concurrencyVersion: 5 }, version: 3 } }];
    const theirs = sourceOf();
    const answer = await theirs.source.update({ ...changed(), name: 'Contact us' });
    expect(answer.error).toBeUndefined();
    expect(api.asked).toEqual(['read draft', 'save draft at 4', 'read form', 'read draft']);
  });

  it('saves over a move, an access change or a publish, with the version it gave', async () => {
    api.draft = { ...DRAFT, form: { ...FORM, folderKey: 'elsewhere', concurrencyVersion: 6 } };
    api.saves = [{ data: { form: { ...FORM, concurrencyVersion: 7 }, version: 3 } }];
    api.renames = [{ data: { ...FORM, name: 'Write to us', concurrencyVersion: 8 } }];
    const { source, conflicts } = sourceOf();
    expect((await source.update(changed())).error).toBeUndefined();
    expect(api.asked.slice(0, 3)).toEqual(['read draft', 'save draft at 6', 'rename to Write to us at 7']);
    expect(conflicts).not.toHaveBeenCalled();
  });

  it('saves the draft first, then the name with the version the draft gave, then reads the form as saved', async () => {
    api.saves = [{ data: { form: { ...FORM, concurrencyVersion: 4 }, version: 3 } }];
    api.renames = [{ data: { ...FORM, name: 'Write to us', concurrencyVersion: 5 } }];
    const { source } = sourceOf();
    const answer = await source.update(changed());
    expect(answer.error).toBeUndefined();
    expect(api.asked).toEqual([
      'read draft',
      'save draft at 3',
      'rename to Write to us at 4',
      'read form',
      'read draft',
    ]);
  });

  it('says a conflict the server finds as a conflict too, unsaid as a toast', async () => {
    api.saves = [refusal(409, 'concurrency_conflict')];
    const { source, peek, conflicts } = sourceOf();
    expect((await source.update(changed())).error?.message).toBe(SAVE_CONFLICT);
    expect(peek).not.toHaveBeenCalled();
    expect(conflicts).toHaveBeenCalledOnce();
    // The name waits for the draft: nothing was renamed.
    expect(api.asked.some((one) => one.startsWith('rename'))).toBe(false);
  });

  it('says a name refused after the draft went in, and reads the form as saved', async () => {
    api.saves = [{ data: { form: { ...FORM, concurrencyVersion: 4 }, version: 3 } }];
    api.renames = [refusal(400, 'validation_failed')];
    const { source, peek } = sourceOf();
    const answer = await source.update(changed());
    expect(answer.data?.unique).toBe('f');
    expect(peek).toHaveBeenCalledWith('danger', { data: { message: 'Refused' } });
    expect(api.asked.slice(-2)).toEqual(['read form', 'read draft']);
  });

  it('says any other refusal and passes it on', async () => {
    api.saves = [refusal(403, 'forbidden')];
    const { source, peek } = sourceOf();
    const answer = await source.update(changed());
    expect(answer.error).toBeInstanceOf(UmbApiError);
    expect(peek).toHaveBeenCalledOnce();
  });
});
