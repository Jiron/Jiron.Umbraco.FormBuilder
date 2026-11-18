// An option list's view (ROADMAP 12.1, IA 7, ADR-0214): the kind's box holds the inspector's option editor with its
// source fixed, and a change it makes becomes the list's; a list of fixed options or of a file takes a file's options,
// the fixed ones replaced with Undo, and a file too large or not readable is said under the button; a JSON address and a
// standard list keep their options for a time from their kind's shortest on, and a saved JSON address says what this
// server fetched, with Fetch now; the preview follows each change after a pause, in the language chosen, as a dropdown and
// as radio buttons with the first eight; it says why it shows none; and a save's problems show at their places.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { OptionListEditorResponseModel, OptionSourcesResponseModel } from '../api';
import type { DesignField } from '../designer/definition';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { UNDO_NOTIFICATION, type UndoNotificationData } from '../ui/undo-notification.element';
import { OPTION_LIST_WORKSPACE_CONTEXT } from './constants';
import type { ListProblem, OptionListDetailModel } from './option-list-model';
import { loadOptionListWords } from './option-list-words';

const api = vi.hoisted(() => ({
  previews: [] as unknown[],
  previewed: [] as { kind: string; culture: string; items: unknown; config: unknown }[],
  files: [] as unknown[],
  fetched: [] as unknown[],
  fetches: [] as unknown[],
  fetchCalls: 0,
  fetchedReads: 0,
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const editor: OptionListEditorResponseModel = {
  languages: [
    { culture: 'en-US', name: 'English (United States)', isDefault: true },
    { culture: 'de-DE', name: 'Deutsch (Deutschland)', isDefault: false },
  ],
  kinds: [
    { kind: 'manual', hasItems: true, keepsOptions: false, minCacheSeconds: 0 },
    { kind: 'file', hasItems: true, keepsOptions: false, minCacheSeconds: 0 },
    { kind: 'content', hasItems: false, keepsOptions: false, minCacheSeconds: 0 },
    { kind: 'url', hasItems: false, keepsOptions: true, minCacheSeconds: 60 },
    { kind: 'code', hasItems: false, keepsOptions: true, minCacheSeconds: 0 },
  ],
  maxCacheSeconds: 30 * 86400,
  maxOptions: 5000,
  maxFileBytes: 1024 * 1024,
};

const sources = JSON.parse(
  readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), ...Array(7).fill('..'), 'docs', 'api', 'option-sources.json'),
    'utf8',
  ),
) as OptionSourcesResponseModel;

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  optionListLibraryEditor: async () => ({ data: editor }),
  optionSourceList: async () => ({ data: sources }),
  optionListLibraryPreview: async (options: { body: (typeof api.previewed)[number] }) => {
    api.previewed.push(options.body);
    return api.previews.shift() ?? { data: { options: [], isAvailable: true, problems: [] } };
  },
  optionListLibraryReadFile: async () => api.files.shift(),
  optionListLibraryFetched: async () => {
    api.fetchedReads++;
    return api.fetched.shift() ?? {};
  },
  optionListLibraryFetch: async () => {
    api.fetchCalls++;
    return api.fetches.shift();
  },
}));

const { OptionListViewElement } = await import('./option-list.view');

const list = (more: Partial<OptionListDetailModel> = {}): OptionListDetailModel => ({
  entityType: 'jfb-option-list',
  unique: 'a1b2c3d4-0000-4000-8000-000000000001',
  name: 'Sizes',
  alias: 'sizes',
  kind: 'manual',
  config: {},
  items: [{ value: 's', label: { '': 'Small' } }],
  cacheSeconds: 0,
  concurrencyVersion: 2,
  ...more,
});

const options = (count: number) =>
  Array.from({ length: count }, (_, at) => ({ value: `v${at}`, label: `Option ${at}` }));

/** A workspace context as the view uses it: the list shown, the list saved, a save's problems and the changes. */
function contextOf(shown: OptionListDetailModel, saved: OptionListDetailModel | undefined = shown) {
  const data = new UmbObjectState<OptionListDetailModel | undefined>(shown);
  const persisted = new UmbObjectState<OptionListDetailModel | undefined>(saved);
  const problems = new UmbObjectState<readonly ListProblem[]>([]);
  return {
    getEntityType: () => 'jfb-option-list',
    getHostElement: () => document.body,
    data: data.asObservable(),
    persistedData: persisted.asObservable(),
    problems: problems.asObservable(),
    setContents: vi.fn((contents: Pick<OptionListDetailModel, 'config' | 'items'>) => data.update(contents)),
    setCacheSeconds: vi.fn((cacheSeconds: number) => data.update({ cacheSeconds })),
    isSavedAsShown: () =>
      shown.concurrencyVersion > 0 && JSON.stringify(data.getValue()) === JSON.stringify(persisted.getValue()),
    data$: data,
    problems$: problems,
  };
}

async function viewOf(shown: OptionListDetailModel, saved?: OptionListDetailModel) {
  const context = contextOf(shown, saved ?? shown);
  const host = testHost();
  const peek = vi.fn(() => ({ close: vi.fn() }));
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  host.provideContext(OPTION_LIST_WORKSPACE_CONTEXT, context as never);
  const view = new OptionListViewElement();
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('.page')).not.toBeNull());
  await settle(view);
  return { view, root: shadow(view), context, peek };
}

/** The preview once it has its answer. */
async function previewed(root: ShadowRoot, count = 1) {
  await vi.waitFor(() => expect(api.previewed).toHaveLength(count), { timeout: 3000 });
  await vi.waitFor(() => expect(root.querySelector('.preview .skeleton, .preview [aria-busy]')).toBeNull());
}

const choose = (select: HTMLSelectElement, value: string) => {
  select.value = value;
  select.dispatchEvent(new Event('change'));
};

beforeAll(async () => {
  registerEnglish();
  await loadOptionListWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
  api.previews = [];
  api.previewed = [];
  api.files = [];
  api.fetched = [];
  api.fetches = [];
  api.fetchCalls = 0;
  api.fetchedReads = 0;
});

describe("an option list's view", () => {
  it("edits the list in the inspector's option editor with its source fixed, each change the list's", async () => {
    const { root, context } = await viewOf(list());
    const box = must(root.querySelector('uui-box'), 'the kind box');
    expect(box.getAttribute('headline')).toBe('Fixed options');
    const editorElement = must(
      root.querySelector<
        HTMLElement & {
          field: DesignField;
          fixed: boolean;
          cards: boolean;
          culture: string;
          change: (update: (field: DesignField) => DesignField) => void;
        }
      >('jfb-option-editor'),
      'the option editor',
    );
    expect([editorElement.fixed, editorElement.cards, editorElement.culture]).toEqual([true, true, 'en-US']);
    expect(editorElement.field.options).toEqual({ source: 'items', items: [{ value: 's', label: { '': 'Small' } }] });
    editorElement.change((field) => ({ ...field, options: { source: 'items', items: [{ value: 'm' }] } }));
    expect(context.setContents).toHaveBeenCalledWith({ items: [{ value: 'm' }], config: {} });
    // Its own words, the inspector's: no "Options from".
    expect(root.querySelector('#options-source')).toBeNull();
    // The box's headline names the rows, and nothing names them again (ROADMAP 22.4).
    await settle(root.host as never);
    expect(root.querySelector('#options-heading')).toBeNull();
    expect(root.querySelector('.option-rows')?.getAttribute('aria-label')).toBe('Fixed options');
  });

  it('previews the options after a pause as a dropdown and radio buttons, the first eight and how many more', async () => {
    api.previews = [{ data: { options: options(11), isAvailable: true, problems: [] } }];
    const { root } = await viewOf(list());
    await previewed(root);
    expect(api.previewed[0]).toMatchObject({ kind: 'manual', culture: '' });
    const dropdown = must(root.querySelector<HTMLSelectElement>('#as-dropdown'), 'the dropdown');
    expect([...dropdown.options].map((option) => option.text)).toEqual([
      'Choose one',
      ...options(11).map((option) => option.label),
    ]);
    expect(root.querySelectorAll('.radios input[type="radio"]')).toHaveLength(8);
    expect(text(root.querySelector('fieldset legend'))).toBe('As radio buttons');
    expect(text(root.querySelector('fieldset .help'))).toBe('and 3 more');
  });

  it('asks again once after changes in quick succession, and in the language chosen', async () => {
    const { root, context } = await viewOf(list());
    await previewed(root);
    context.setContents({ config: {}, items: [{ value: 'a' }] });
    context.setContents({ config: {}, items: [{ value: 'a' }, { value: 'b' }] });
    await previewed(root, 2);
    expect(api.previewed[1]?.items).toEqual([{ value: 'a' }, { value: 'b' }]);
    const language = must(root.querySelector<HTMLSelectElement>('#language'), 'the language');
    expect([...language.options].map((option) => option.text)).toEqual([
      'English (United States)',
      'Deutsch (Deutschland)',
    ]);
    choose(language, 'de-DE');
    await previewed(root, 3);
    expect(api.previewed[2]?.culture).toBe('de-DE');
    expect(must(root.querySelector<HTMLElement & { culture: string }>('jfb-option-editor'), 'editor').culture).toBe(
      'de-DE',
    );
    await new Promise((done) => setTimeout(done, 600));
    expect(api.previewed).toHaveLength(3);
  });

  it('says why it shows no options: the problems, a source that gave none, none yet, a page not known yet', async () => {
    api.previews = [
      { data: { options: [], isAvailable: true, problems: [{ code: 'x', path: '$.config.url', message: 'm' }] } },
    ];
    const problems = await viewOf(list({ kind: 'url', config: { url: 'x' }, cacheSeconds: 3600 }));
    await previewed(problems.root);
    expect(text(problems.root.querySelector('.preview'))).toBe('Fix what is marked to see the options.');
    document.body.replaceChildren();
    api.previewed = [];

    api.previews = [{ data: { options: [], isAvailable: false, problems: [] } }];
    const unavailable = await viewOf(list({ kind: 'code', config: { source: 'months' } }));
    await previewed(unavailable.root);
    expect(text(unavailable.root.querySelector('.preview'))).toBe(
      'The source gave no options. Check its settings, or try again later.',
    );
    document.body.replaceChildren();
    api.previewed = [];

    const empty = await viewOf(list({ items: [] }));
    await previewed(empty.root);
    expect(text(empty.root.querySelector('.preview'))).toBe('No options yet.');
    document.body.replaceChildren();

    const pages = await viewOf(list({ kind: 'content', config: {} }));
    expect(text(pages.root.querySelector('.preview'))).toBe(
      'Its options are the pages below the page the form is on, so they show on that page.',
    );
  });

  it("replaces fixed options with a file's, with Undo, and says a file too large or not readable", async () => {
    const { root, context, peek } = await viewOf(list());
    const input = must(root.querySelector<HTMLInputElement>('#option-file'), 'the file input');
    expect(input.accept).toBe('.csv,.txt,text/csv,text/plain');
    const button = must(root.querySelector('.upload uui-button'), 'the upload button');
    expect(button.getAttribute('label')).toBe('Upload a CSV or TXT file\u2026');
    const send = (file: File) => {
      Object.defineProperty(input, 'files', { value: [file], configurable: true });
      input.dispatchEvent(new Event('change'));
    };

    api.files = [{ data: { items: [{ value: 'x' }, { value: 'y' }] } }];
    send(new File(['x\ny'], 'sizes.csv'));
    await vi.waitFor(() => expect(context.setContents).toHaveBeenCalledOnce());
    expect(context.setContents).toHaveBeenCalledWith({ config: {}, items: [{ value: 'x' }, { value: 'y' }] });
    await vi.waitFor(() => expect(peek).toHaveBeenCalledOnce());
    const [color, toast] = peek.mock.calls[0] as unknown as [
      string,
      { elementName: string; data: UndoNotificationData },
    ];
    expect([color, toast.elementName, toast.data.message, toast.data.undoLabel]).toEqual([
      'positive',
      UNDO_NOTIFICATION,
      "Replaced with the file's 2 options.",
      'Undo',
    ]);
    await toast.data.undo();
    expect(context.setContents).toHaveBeenLastCalledWith({
      config: {},
      items: [{ value: 's', label: { '': 'Small' } }],
    });

    send(new File([new Uint8Array(1024 * 1024 + 1)], 'big.csv'));
    await vi.waitFor(() => expect(root.querySelector('#option-file-problems')).not.toBeNull());
    expect(text(root.querySelector('#option-file-problems'))).toBe('A file of options holds at most 1 MB.');
    expect(button.getAttribute('aria-describedby')).toBe('option-file-help option-file-problems');

    api.files = [
      {
        error: Object.assign(new Error('refused'), {}),
      },
    ];
    send(new File(['x'], 'odd.txt'));
    await vi.waitFor(() =>
      expect(text(root.querySelector('#option-file-problems'))).toBe("The file couldn't be read. Try again."),
    );
    expect(context.setContents).toHaveBeenCalledTimes(2);
  });

  it("says how many options an upload list's last file gave, or that it needs one, and offers no Undo", async () => {
    const needing = await viewOf(list({ kind: 'file', items: [] }));
    expect(must(needing.root.querySelector('uui-box'), 'box').getAttribute('headline')).toBe('An upload');
    expect(text(needing.root.querySelector('.upload .lead'))).toBe('No file yet.');
    expect(needing.root.querySelector('jfb-option-editor')).toBeNull();
    document.body.replaceChildren();

    const { root, context, peek } = await viewOf(list({ kind: 'file', items: [{ value: 'a' }, { value: 'b' }] }));
    expect(text(root.querySelector('.upload .lead'))).toBe('2 options from the last file');
    expect(root.querySelector('.upload uui-button')?.getAttribute('label')).toBe('Upload another file\u2026');
    api.files = [{ data: { items: [{ value: 'c' }] } }];
    const input = must(root.querySelector<HTMLInputElement>('#option-file'), 'input');
    Object.defineProperty(input, 'files', { value: [new File(['c'], 'c.csv')], configurable: true });
    input.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(context.setContents).toHaveBeenCalledOnce());
    await settle();
    expect(peek).not.toHaveBeenCalled();
  });

  it("keeps a JSON address's options for a time from its kind's shortest on, says what was fetched, and fetches now", async () => {
    const saved = list({ kind: 'url', config: { url: 'http://127.0.0.1:5560/o.json' }, items: [], cacheSeconds: 3600 });
    api.fetched = [{ data: { fetchedUtc: null, count: 0, failing: false } }];
    api.fetches = [{ data: { fetchedUtc: new Date().toISOString(), count: 3, failing: false } }];
    const { root, context } = await viewOf(saved);
    const keep = must(root.querySelector<HTMLSelectElement>('#keep'), 'the keep time');
    expect(text(root.querySelector('label[for="keep"]'))).toBe('Keep fetched options for');
    expect([...keep.options].map((option) => option.text)).toEqual([
      '1 minute',
      '5 minutes',
      '15 minutes',
      '1 hour',
      '6 hours',
      '1 day',
      '7 days',
      '30 days',
    ]);
    expect(keep.value).toBe('3600');
    await vi.waitFor(() => expect(text(root.querySelector('.fetched p'))).toBe('Not fetched on this server yet.'));
    must(root.querySelector<HTMLElement>('.fetched uui-button'), 'Fetch now').click();
    await vi.waitFor(() =>
      expect(text(root.querySelector('.fetched p'))).toMatch(/^Fetched today at .+: 3 options\.$/),
    );
    expect(api.fetchCalls).toBe(1);
    // What was fetched is read once, however often the list's state changed meanwhile.
    expect(api.fetchedReads).toBe(1);

    choose(keep, '86400');
    expect(context.setCacheSeconds).toHaveBeenCalledWith(86400);
    // Changed and not saved: Fetch now would fetch what is saved, so it waits for the save.
    await vi.waitFor(() =>
      expect(text(root.querySelector('.item .help:last-child'))).toBe('Save the list to fetch its address here.'),
    );
    expect(root.querySelector('.fetched')).toBeNull();
  });

  it("offers no fetch for a new JSON address, and a standard list's keep time from nothing", async () => {
    const fresh = await viewOf(list({ kind: 'url', concurrencyVersion: 0, items: [], cacheSeconds: 3600 }), undefined);
    expect(fresh.root.querySelector('.fetched')).toBeNull();
    expect(text(fresh.root.querySelector('#keep-help ~ .help'))).toBe('');
    document.body.replaceChildren();

    const code = await viewOf(list({ kind: 'code', config: { source: 'months' }, items: [] }));
    expect(text(code.root.querySelector('label[for="keep"]'))).toBe('Keep the options for');
    const keep = must(code.root.querySelector<HTMLSelectElement>('#keep'), 'the keep time');
    expect(keep.options[0]?.text).toBe("Don't keep");
    expect(code.root.querySelector('.upload')).toBeNull();
  });

  it("shows a save's problems at their places: the header's on top, the contents' and the keep time's in the box", async () => {
    const { root, context, view } = await viewOf(list({ kind: 'url', cacheSeconds: 3600, items: [] }));
    context.problems$.setValue([
      { code: 'required', path: '$.name', message: 'server words' },
      { code: 'alias_duplicate', path: '$.alias', message: 'server words' },
      { code: 'invalid_url', path: '$.config.url', message: 'The address must start with https://.' },
      { code: 'out_of_range', path: '$.cacheSeconds', message: 'Keep it at least a minute.' },
    ]);
    await settle(view);
    expect(text(root.querySelector('.alert'))).toBe('Give the list a name. Another option list has this alias.');
    expect(text(root.querySelector('#contents-problems'))).toBe('The address must start with https://.');
    expect(text(root.querySelector('#keep-problems'))).toBe('Keep it at least a minute.');
    expect(root.querySelector('#keep')?.getAttribute('aria-describedby')).toBe('keep-help keep-problems');
  });
});
