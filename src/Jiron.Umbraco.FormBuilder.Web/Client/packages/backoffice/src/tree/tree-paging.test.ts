// How the forms tree and the Recycle Bin's tree answer Umbraco 18's paging (ADR-0250): a page from an offset, or a window
// around one of a level's items, from a server that pages by offset only.
import { describe, expect, it, vi } from 'vitest';
import type { LibraryItemResponseModel, PagedLibraryItemResponseModel } from '../api';
import { LEVEL_PAGE, readLevel, type LevelReader } from './library-tree.repository';

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const host = {} as never;

const item = (key: string, kind: 'folder' | 'form' = 'form'): LibraryItemResponseModel => ({
  key,
  kind,
  name: key,
  isRestricted: false,
  isPublished: true,
  isTrashed: false,
  hasChildren: false,
  permissions: ['design'],
  hasUnseenEntries: false,
});

/** A level of the given items, read by offset, which records each read. */
function level(items: LibraryItemResponseModel[]) {
  const reads: [number, number][] = [];
  const read: LevelReader = (skip, take) => {
    reads.push([skip, take]);
    const data: PagedLibraryItemResponseModel = { items: items.slice(skip, skip + take), total: items.length };
    return Promise.resolve({ data });
  };
  return { read, reads };
}

const many = (count: number) => Array.from({ length: count }, (_, index) => item(`i${index}`));
const keys = (page: { items: LibraryItemResponseModel[] } | undefined) => page?.items.map((entry) => entry.key);
const target = (unique: string, takeBefore: number, takeAfter: number) => ({
  target: { unique, entityType: 'jfb-form' },
  takeBefore,
  takeAfter,
});

describe("a level's page from an offset", () => {
  it('is the page asked for, with how many come before and after it', async () => {
    const { read, reads } = level(many(240));

    const { data } = await readLevel(host, read, { skip: 100, take: 50 });

    expect(reads).toEqual([[100, 50]]);
    expect(data).toMatchObject({ total: 240, totalBefore: 100, totalAfter: 90 });
    expect(keys(data)?.[0]).toBe('i100');
  });

  it("is the level's first page when the tree names none", async () => {
    const { read, reads } = level(many(3));

    const { data } = await readLevel(host, read, undefined);

    expect(reads).toEqual([[0, LEVEL_PAGE]]);
    expect(data).toMatchObject({ total: 3, totalBefore: 0, totalAfter: 0 });
  });
});

describe("a level's window around an item", () => {
  it('reads from the start until it holds the item and what comes after it, and gives the window', async () => {
    const { read, reads } = level(many(250));

    const { data } = await readLevel(host, read, target('i160', 5, 5));

    expect(reads).toEqual([
      [0, LEVEL_PAGE],
      [LEVEL_PAGE, LEVEL_PAGE],
    ]);
    expect(keys(data)).toEqual([
      'i155',
      'i156',
      'i157',
      'i158',
      'i159',
      'i160',
      'i161',
      'i162',
      'i163',
      'i164',
      'i165',
    ]);
    expect(data).toMatchObject({ total: 250, totalBefore: 155, totalAfter: 84 });
  });

  it('reads the next page when the items after the item run past the one that holds it', async () => {
    const { read, reads } = level(many(250));

    const { data } = await readLevel(host, read, target('i98', 0, 5));

    expect(reads).toHaveLength(2);
    expect(keys(data)).toEqual(['i98', 'i99', 'i100', 'i101', 'i102', 'i103']);
    expect(data).toMatchObject({ totalBefore: 98, totalAfter: 146 });
  });

  it('starts at the first item and ends at the last', async () => {
    const { read } = level(many(4));

    const { data } = await readLevel(host, read, target('i1', 5, 5));

    expect(keys(data)).toEqual(['i0', 'i1', 'i2', 'i3']);
    expect(data).toMatchObject({ total: 4, totalBefore: 0, totalAfter: 0 });
  });

  it('answers "not found" for an item the level no longer holds, so the tree picks another or starts from the top', async () => {
    const { read, reads } = level(many(150));

    const { data, error } = await readLevel(host, read, target('gone', 5, 5));

    expect(data).toBeUndefined();
    expect(error?.message).toContain('not found');
    expect(reads).toHaveLength(2);
  });

  it('holds only folders for a folders-only picker, which end where the forms start', async () => {
    const { read } = level([item('f0', 'folder'), item('f1', 'folder'), item('f2', 'folder'), item('x', 'form')]);
    const folders = (items: LibraryItemResponseModel[]) => items.filter((entry) => entry.kind === 'folder');

    const { data } = await readLevel(host, read, target('f1', 1, 5), folders);

    expect(keys(data)).toEqual(['f0', 'f1', 'f2']);
    expect(data).toMatchObject({ total: 3, totalBefore: 0, totalAfter: 0 });
  });

  it("passes the server's error on", async () => {
    const failure = new Error('offline');
    const read: LevelReader = () => Promise.resolve({ error: failure } as never);

    const { data, error } = await readLevel(host, read, target('i1', 1, 1));

    expect(data).toBeUndefined();
    expect(error).toBe(failure);
  });
});
