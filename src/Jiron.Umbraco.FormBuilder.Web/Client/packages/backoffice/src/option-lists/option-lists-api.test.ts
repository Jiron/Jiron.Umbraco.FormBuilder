// What the option list screens ask the management API (ROADMAP 12.1, ADR-0214): a list reads into the workspace's model,
// JSON of another shape as none; a save sends the values and answers saved, refused with problems, saved meanwhile or
// failed; a delete answers by its refusal's code; a file goes as Base64, in parts, and comes back as options or as the
// problems that refuse it; and the editor's settings are read once while they read.
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OptionListResponseModel } from '../api';

const api = vi.hoisted(() => ({
  answers: [] as unknown[],
  sent: [] as { name: string; options: unknown }[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const answering =
  (name: string) =>
  async (options: unknown): Promise<unknown> => {
    api.sent.push({ name, options });
    return api.answers.shift();
  };

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  optionListLibraryCreate: answering('create'),
  optionListLibraryUpdate: answering('update'),
  optionListLibraryDelete: answering('delete'),
  optionListLibraryReadFile: answering('readFile'),
  optionListLibraryEditor: answering('editor'),
  optionListLibraryPreview: answering('preview'),
}));

const { createList, deleteList, detailOf, previewList, problemsOf, readEditor, readFile, updateList } =
  await import('./option-lists-api');

const host = {} as never;

const response = (more: Partial<OptionListResponseModel> = {}): OptionListResponseModel => ({
  key: 'a1b2c3d4-0000-4000-8000-000000000001',
  alias: 'sizes',
  name: 'Sizes',
  kind: 'manual',
  config: {},
  items: [{ value: 's' }],
  cacheSeconds: 0,
  concurrencyVersion: 2,
  updatedUtc: '2026-11-08T10:00:00Z',
  ...more,
});

const refusal = (status: number, details: Record<string, unknown>) => ({
  error: new UmbApiError('refused', status, null, details as never),
});

afterEach(() => {
  api.answers = [];
  api.sent = [];
});

describe('the option list API', () => {
  it("reads a list into the workspace's model, JSON of another shape and an unknown kind as none", () => {
    expect(detailOf(response())).toEqual({
      entityType: 'jfb-option-list',
      unique: 'a1b2c3d4-0000-4000-8000-000000000001',
      name: 'Sizes',
      alias: 'sizes',
      kind: 'manual',
      config: {},
      items: [{ value: 's' }],
      cacheSeconds: 0,
      concurrencyVersion: 2,
    });
    const odd = detailOf(response({ kind: 'spreadsheet', config: [1, 2], items: { value: 'x' } }));
    expect([odd.kind, odd.config, odd.items]).toEqual([null, {}, []]);
    expect(detailOf(response({ config: null })).config).toEqual({});
  });

  it('sends the values with a trimmed name, a new list under its key, a saved one with the version read', async () => {
    const model = { ...detailOf(response()), name: '  Sizes  ' };
    api.answers = [{ data: response() }, { data: response({ concurrencyVersion: 3 }) }];
    expect((await createList(host, model)).kind).toBe('saved');
    const updated = await updateList(host, model);
    expect(updated.kind === 'saved' && updated.list.concurrencyVersion).toBe(3);
    const values = {
      alias: 'sizes',
      name: 'Sizes',
      kind: 'manual',
      config: {},
      items: [{ value: 's' }],
      cacheSeconds: 0,
    };
    expect(api.sent).toEqual([
      { name: 'create', options: { body: { key: model.unique, list: values } } },
      { name: 'update', options: { path: { key: model.unique }, body: { concurrencyVersion: 2, list: values } } },
    ]);
  });

  it('answers a refused save with its problems by path, a save meanwhile as a conflict, anything else as failed', async () => {
    const model = detailOf(response());
    api.answers = [
      refusal(400, {
        code: 'validation_failed',
        errors: [
          { code: 'required', path: '$.name', message: 'Give it a name.' },
          { code: 'odd', message: 'a problem with no path is said otherwise' },
        ],
      }),
      refusal(409, { code: 'concurrency_conflict' }),
      refusal(500, { title: 'Server error' }),
    ];
    expect(await updateList(host, model)).toEqual({
      kind: 'invalid',
      problems: [{ code: 'required', path: '$.name', message: 'Give it a name.' }],
    });
    expect((await updateList(host, model)).kind).toBe('conflict');
    expect((await updateList(host, model)).kind).toBe('failed');
    expect(problemsOf(new Error('no answer'))).toEqual([]);
  });

  it("answers a delete by its refusal's code, sending the version read", async () => {
    api.answers = [
      {},
      refusal(409, { code: 'in_use' }),
      refusal(409, { code: 'concurrency_conflict' }),
      refusal(404, { code: 'option_list_not_found' }),
      refusal(500, {}),
    ];
    const answers = [];
    for (let round = 0; round < 5; round++) answers.push(await deleteList(host, 'k', 4));
    expect(answers).toEqual(['deleted', 'inUse', 'conflict', 'gone', 'failed']);
    expect(api.sent[0]).toEqual({ name: 'delete', options: { path: { key: 'k' }, query: { concurrencyVersion: 4 } } });
  });

  it('sends a file as Base64, a large one too, and answers with its options or the problems that refuse it', async () => {
    // Larger than one part of 32 KB, with every byte value, so the parts join exactly.
    const bytes = Uint8Array.from({ length: 100_000 }, (_, at) => at % 256);
    api.answers = [
      { data: { items: [{ value: 'a' }, { value: 'b' }] } },
      refusal(400, { code: 'validation_failed', errors: [{ code: 'bad_line', path: '$.file', message: 'Line 3.' }] }),
      refusal(500, {}),
    ];
    const file = new File([bytes], 'options.csv', { type: 'text/csv' });
    expect(await readFile(host, file)).toEqual({ kind: 'read', items: [{ value: 'a' }, { value: 'b' }] });
    const sent = api.sent[0]?.options as { body: { fileName: string; content: string } };
    expect(sent.body.fileName).toBe('options.csv');
    expect(Uint8Array.from(atob(sent.body.content), (char) => char.charCodeAt(0))).toEqual(bytes);
    expect(await readFile(host, file)).toEqual({
      kind: 'invalid',
      problems: [{ code: 'bad_line', path: '$.file', message: 'Line 3.' }],
    });
    expect(await readFile(host, file)).toEqual({ kind: 'failed' });
  });

  it('previews the contents shown, under the list key and in the culture asked, so a later change cancels it', async () => {
    const model = detailOf(response({ kind: 'code', config: { source: 'months' }, cacheSeconds: 60 }));
    const signal = new AbortController().signal;
    api.answers = [{ data: { options: [{ value: '1', label: 'January' }], isAvailable: true, problems: [] } }];
    expect((await previewList(host, model, 'de-DE', signal))?.options).toHaveLength(1);
    expect(api.sent[0]).toEqual({
      name: 'preview',
      options: {
        body: {
          key: model.unique,
          kind: 'code',
          config: { source: 'months' },
          items: [{ value: 's' }],
          cacheSeconds: 60,
          culture: 'de-DE',
        },
        signal,
      },
    });
  });

  it("reads the editor's settings once, and again after a read that failed", async () => {
    const editor = { languages: [], kinds: [], maxCacheSeconds: 1, maxOptions: 1, maxFileBytes: 1 };
    api.answers = [{}, { data: editor }];
    expect(await readEditor(host)).toBeUndefined();
    expect(await readEditor(host)).toEqual(editor);
    expect(await readEditor(host)).toEqual(editor);
    expect(api.sent.filter((each) => each.name === 'editor')).toHaveLength(2);
  });
});
