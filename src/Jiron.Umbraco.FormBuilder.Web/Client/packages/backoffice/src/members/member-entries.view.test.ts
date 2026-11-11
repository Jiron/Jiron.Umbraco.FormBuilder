import { BehaviorSubject } from '@umbraco-cms/backoffice/external/rxjs';
import { UMB_MEMBER_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/member';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { EntryListPageResponseModel, EntryListRowResponseModel } from '../api';
import { ENTRY_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';

// What the server answers each page, by its cursor, and what was asked.
const server = vi.hoisted(() => ({
  pages: new Map<string, unknown>(),
  asked: [] as { member: string; after: string | undefined }[],
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  memberEntriesEntries: async (options: { path: { key: string }; query?: { after?: string } }) => {
    server.asked.push({ member: options.path.key, after: options.query?.after });
    const page = server.pages.get(options.query?.after ?? '');
    return page === 'throw' ? Promise.reject(new Error('offline')) : { data: page };
  },
}));

await import('./member-entries.view');

beforeAll(() => registerEnglish());

afterEach(() => {
  document.body.replaceChildren();
  server.pages.clear();
  server.asked = [];
});

const row = (key: string, number: number, form: string, state = 'approved'): EntryListRowResponseModel => ({
  key,
  number,
  title: 'Mira Holt',
  state,
  received: '2026-11-07T11:00:00Z',
  unseen: false,
  actions: { total: 0, failed: 0, waiting: 0 } as never,
  cells: [],
  form,
});

const page = (
  items: EntryListRowResponseModel[],
  next: string | null = null,
  total = items.length,
): EntryListPageResponseModel => ({
  items,
  next,
  total,
  more: false,
  forms: [
    { key: 'f-1', name: 'Book a workshop', permissions: ['viewEntries'] },
    { key: 'f-2', name: 'Feedback', permissions: ['viewEntries'] },
  ],
});

async function viewOf(member = 'm-1') {
  const host = testHost();
  host.provideContext(UMB_MEMBER_WORKSPACE_CONTEXT, {
    unique: new BehaviorSubject<string | undefined>(member).asObservable(),
    getEntityType: () => 'member',
    getHostElement: () => host,
  } as never);
  const view = document.createElement('jfb-member-entries-view');
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('table, p')).not.toBeNull());
  await settle(view);
  return { view, root: shadow(view) };
}

describe("the member editor's Form entries view", () => {
  it('lists the entries the member sent, by number, with the form, the state and when, each opening the entry', async () => {
    server.pages.set('', page([row('e-2', 12, 'f-2'), row('e-1', 3, 'f-1', 'submitted')]));
    const { root } = await viewOf();
    const rows = [...root.querySelectorAll('tbody tr')];

    expect(server.asked).toEqual([{ member: 'm-1', after: undefined }]);
    expect(root.querySelector('uui-box')?.getAttribute('headline')).toBe('2 entries');
    expect(rows.map((one) => text(one.querySelector('a')))).toEqual(['Entry #12', 'Entry #3']);
    expect(rows[0]?.querySelector('a')?.getAttribute('href')).toBe(screenPath(ENTRY_ENTITY_TYPE, 'e-2'));
    expect(rows.map((one) => text(one.querySelector('td.wide')))).toEqual(['Feedback', 'Book a workshop']);
    expect(rows.map((one) => text(one.querySelectorAll('td')[2]))).toEqual(['Approved', 'Waiting for approval']);
  });

  it('shows the next 50 under the first, and says when the member has none', async () => {
    server.pages.set('', page([row('e-2', 12, 'f-2')], 'cursor-1', 2));
    server.pages.set('cursor-1', page([row('e-1', 3, 'f-1')]));
    const { view, root } = await viewOf();
    must(
      all<HTMLElement>(view, 'uui-button').find((one) => one.getAttribute('label') === 'Show more'),
      'Show more',
    ).click();
    await vi.waitFor(() => expect(root.querySelectorAll('tbody tr')).toHaveLength(2));
    await settle(view);
    expect(all<HTMLElement>(view, 'uui-button').some((one) => one.getAttribute('label') === 'Show more')).toBe(false);
    document.body.replaceChildren();

    server.pages.set('', page([]));
    const empty = await viewOf('m-2');
    expect(text(empty.root.querySelector('p'))).toBe(
      'No entries yet. Entries this member sends while signed in appear here.',
    );
  });

  it('says so when the entries could not be loaded, and tries again', async () => {
    server.pages.set('', 'throw');
    const { view, root } = await viewOf();
    expect(text(root.querySelector('[role="alert"]'))).toBe("The member's entries couldn't be loaded.");
    server.pages.set('', page([row('e-1', 3, 'f-1')]));
    must(
      all<HTMLElement>(view, 'uui-button').find((one) => one.getAttribute('label') === 'Try again'),
      'Try again',
    ).click();
    await vi.waitFor(() => expect(root.querySelectorAll('tbody tr')).toHaveLength(1));
  });
});
