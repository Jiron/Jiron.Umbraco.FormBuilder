// The library's stores (ADR-0166): a list's pages, the gallery's search and choice, and the import's steps.
import { describe, expect, it, vi } from 'vitest';
import type { ImportCheckResponseModel, LibraryRowResponseModel, TemplateResponseModel } from '../api';
import { deferred } from '../testing';
import {
  GalleryStore,
  ImportStore,
  ListStore,
  MAX_IMPORT_BYTES,
  PAGE_SIZE,
  type Answer,
  type ImportRefusal,
} from './stores';

const row = (key: string): LibraryRowResponseModel => ({
  item: {
    key,
    kind: 'form',
    name: key,
    isRestricted: false,
    isPublished: true,
    isTrashed: false,
    hasChildren: false,
    permissions: [],
    hasUnseenEntries: false,
  },
  updatedUtc: '2026-11-01T10:00:00Z',
});

describe('a list', () => {
  it('loads the first page and says whether more wait', async () => {
    const fetch = vi.fn((skip: number) =>
      Promise.resolve({ data: { items: skip === 0 ? [row('a'), row('b')] : [row('c')], total: 3 } }),
    );
    const changed = vi.fn();
    const list = new ListStore(fetch, changed);

    await list.load();
    expect(fetch).toHaveBeenCalledWith(0, PAGE_SIZE);
    expect(list.rows).toEqual({ state: 'ready', value: [row('a'), row('b')] });
    expect(list.hasMore).toBe(true);

    await list.more();
    expect(fetch).toHaveBeenLastCalledWith(2, PAGE_SIZE);
    expect(list.rows.state === 'ready' && list.rows.value.map((r) => r.item.key)).toEqual(['a', 'b', 'c']);
    expect(list.hasMore).toBe(false);
    expect(changed).toHaveBeenCalled();
  });

  it('keeps a row once when a page repeats it, after something moved meanwhile', async () => {
    const pages = [
      [row('a'), row('b')],
      [row('b'), row('c')],
    ];
    const list = new ListStore(() => Promise.resolve({ data: { items: pages.shift() ?? [], total: 3 } }), vi.fn());
    await list.load();
    await list.more();
    expect(list.rows.state === 'ready' && list.rows.value.map((r) => r.item.key)).toEqual(['a', 'b', 'c']);
  });

  it('fails when the server does not answer, and loads again after', async () => {
    const answers: Answer<{ items: LibraryRowResponseModel[]; total: number }>[] = [
      { error: new Error('offline') },
      { data: { items: [row('a')], total: 1 } },
    ];
    const list = new ListStore(() => Promise.resolve(answers.shift() ?? {}), vi.fn());
    await list.load();
    expect(list.rows).toEqual({ state: 'failed' });
    expect(list.hasMore).toBe(false);
    await list.load();
    expect(list.rows.state).toBe('ready');
  });

  it('drops an older answer that comes after a newer one', async () => {
    const first = deferred<Answer<{ items: LibraryRowResponseModel[]; total: number }>>();
    const second = deferred<Answer<{ items: LibraryRowResponseModel[]; total: number }>>();
    const answers = [first.promise, second.promise];
    const list = new ListStore(() => answers.shift() ?? Promise.resolve({}), vi.fn());

    const older = list.load();
    const newer = list.load();
    second.resolve({ data: { items: [row('new')], total: 1 } });
    await newer;
    first.resolve({ data: { items: [row('old')], total: 1 } });
    await older;
    expect(list.rows.state === 'ready' && list.rows.value.map((r) => r.item.key)).toEqual(['new']);
  });

  it('keeps its rows on screen while it loads again', async () => {
    const pending = deferred<Answer<{ items: LibraryRowResponseModel[]; total: number }>>();
    const answers = [Promise.resolve({ data: { items: [row('a')], total: 1 } }), pending.promise];
    const list = new ListStore(() => answers.shift() ?? Promise.resolve({}), vi.fn());
    await list.load();
    const again = list.load();
    expect(list.rows.state).toBe('ready');
    pending.resolve({ data: { items: [row('b')], total: 1 } });
    await again;
    expect(list.rows.state === 'ready' && list.rows.value[0]?.item.key).toBe('b');
  });

  it('asks for no more before it has loaded', async () => {
    const fetch = vi.fn(() => Promise.resolve({}));
    await new ListStore(fetch, vi.fn()).more();
    expect(fetch).not.toHaveBeenCalled();
  });
});

const template = (key: string, name: string, builtIn = false): TemplateResponseModel => ({
  key,
  builtIn,
  alias: builtIn ? key : null,
  name,
  description: null,
  holds: { pages: 1, fields: 3, rules: 0, outcomes: 1, actions: 1 },
  definition: { schemaVersion: 1, key, pages: [] } as unknown as TemplateResponseModel['definition'],
});

const gallery = (templates: TemplateResponseModel[]) =>
  new GalleryStore(
    () => Promise.resolve({ data: { items: templates, total: templates.length } }),
    {
      name: (t) => (t.builtIn ? `Built-in ${t.name}` : t.name),
      description: (t) => (t.key === 'event' ? 'party' : ''),
    },
    vi.fn(),
  );

describe('the gallery', () => {
  const templates = [
    template('contact', 'Contact', true),
    template('event', 'Event sign-up'),
    template('job', 'Job application'),
  ];

  it('chooses the first template once loaded, so the preview never stands empty', async () => {
    const store = gallery(templates);
    expect(store.visible).toEqual([]);
    await store.load();
    expect(store.chosen?.key).toBe('contact');
  });

  it('searches the names and descriptions in the user language, keeping the order', async () => {
    const store = gallery(templates);
    await store.load();
    store.search('built-in');
    expect(store.visible.map((t) => t.key)).toEqual(['contact']);
    store.search('PARTY');
    expect(store.visible.map((t) => t.key)).toEqual(['event']);
    store.search('  ');
    expect(store.visible).toHaveLength(3);
  });

  it('moves a choice the search hides to the first template it shows, and keeps one it shows', async () => {
    const store = gallery(templates);
    await store.load();
    store.select('job');
    store.search('a');
    expect(store.chosen?.key).toBe('job');
    store.search('event');
    expect(store.chosen?.key).toBe('event');
    store.search('nothing matches');
    expect(store.chosen).toBeUndefined();
  });

  it('steps through the templates as arrow keys do, wrapping at both ends', async () => {
    const store = gallery(templates);
    await store.load();
    store.step(-1);
    expect(store.selected).toBe('job');
    store.step(1);
    expect(store.selected).toBe('contact');
    store.step(1);
    expect(store.selected).toBe('event');
  });

  it('steps to the first or last template when none is chosen, and does nothing when none shows', async () => {
    const store = gallery(templates);
    await store.load();
    store.selected = undefined;
    store.step(-1);
    expect(store.selected).toBe('job');
    store.selected = undefined;
    store.step(1);
    expect(store.selected).toBe('contact');
    store.search('nothing matches');
    expect(store.selected).toBeUndefined();
    store.step(1);
    expect(store.selected).toBeUndefined();
    store.search('');
    expect(store.selected).toBe('contact');
  });

  it('fails when the server does not answer', async () => {
    const store = new GalleryStore(
      () => Promise.resolve({ error: new Error('offline') }),
      { name: () => '', description: () => '' },
      vi.fn(),
    );
    await store.load();
    expect(store.templates).toEqual({ state: 'failed' });
    expect(store.chosen).toBeUndefined();
  });
});

const check: ImportCheckResponseModel = {
  name: 'Contact',
  alias: 'contact',
  packageVersion: '17.0.0',
  exportedUtc: '2026-10-24T00:00:00Z',
  holds: { pages: 1, fields: 3, rules: 0, outcomes: 1, actions: 0 },
  notes: [],
};

const file = (text: string, size = text.length) => ({ name: 'contact.json', size, text: () => Promise.resolve(text) });

describe('the import', () => {
  it('checks a chosen file, then imports it as a new copy', async () => {
    const calls = {
      check: vi.fn(() => Promise.resolve({ data: check })),
      import: vi.fn(() => Promise.resolve({ data: { form: { key: 'new' } } })),
      refusal: (): ImportRefusal => ({ kind: 'failed' }),
    };
    const steps: string[] = [];
    const store = new ImportStore(calls, () => steps.push(store.state.step));

    await store.choose(file('{"format":"jfb-form-export/1"}'));
    expect(calls.check).toHaveBeenCalledWith('{"format":"jfb-form-export/1"}');
    expect(store.state).toMatchObject({ step: 'checked', check });

    await store.import();
    expect(calls.import).toHaveBeenCalledWith('{"format":"jfb-form-export/1"}');
    expect(store.state).toEqual({ step: 'imported', formKey: 'new' });
    expect(steps).toEqual(['checking', 'checked', 'importing', 'imported']);
  });

  it('refuses a file that is too large before reading it or asking the server', async () => {
    const calls = { check: vi.fn(), import: vi.fn(), refusal: (): ImportRefusal => ({ kind: 'failed' }) };
    const text = vi.fn(() => Promise.resolve(''));
    const store = new ImportStore(calls, vi.fn());
    await store.choose({ name: 'huge.json', size: MAX_IMPORT_BYTES + 1, text });
    expect(store.state).toMatchObject({ step: 'refused', reason: { kind: 'tooLarge' } });
    expect(text).not.toHaveBeenCalled();
    expect(calls.check).not.toHaveBeenCalled();
  });

  it("says the server's reason when the check refuses the file, and starts over on reset", async () => {
    const problem = new Error('not an export');
    const store = new ImportStore(
      {
        check: () => Promise.resolve({ error: problem }),
        import: vi.fn(),
        refusal: (error): ImportRefusal => ({ kind: 'unreadable', message: (error as Error).message }),
      },
      vi.fn(),
    );
    await store.choose(file('{}'));
    expect(store.state).toMatchObject({ step: 'refused', reason: { kind: 'unreadable', message: 'not an export' } });
    store.reset();
    expect(store.state).toEqual({ step: 'choose' });
  });

  it('says a missing permission when the import itself is refused', async () => {
    const store = new ImportStore(
      {
        check: () => Promise.resolve({ data: check }),
        import: () => Promise.resolve({ error: new Error('forbidden') }),
        refusal: (): ImportRefusal => ({ kind: 'forbidden', permission: 'manageActions' }),
      },
      vi.fn(),
    );
    await store.choose(file('{}'));
    await store.import();
    expect(store.state).toMatchObject({ step: 'refused', reason: { kind: 'forbidden', permission: 'manageActions' } });
  });

  it('imports nothing before a file is checked', async () => {
    const calls = { check: vi.fn(), import: vi.fn(), refusal: (): ImportRefusal => ({ kind: 'failed' }) };
    await new ImportStore(calls, vi.fn()).import();
    expect(calls.import).not.toHaveBeenCalled();
  });
});
