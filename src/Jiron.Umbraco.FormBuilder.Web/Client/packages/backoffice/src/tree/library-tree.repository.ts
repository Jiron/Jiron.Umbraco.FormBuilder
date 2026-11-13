// The forms tree (ADR-0021, ADR-0165, ADR-0166): the folders and forms the user sees, from the server, which filters them by
// permission. Each item carries its marks as Umbraco's entity flags (restricted, unseen entries), and whether a form is
// published, which the tree's own item element mutes.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbApi } from '@umbraco-cms/backoffice/extension-api';
import type { UmbDataSourceResponse, UmbTargetPagedModel } from '@umbraco-cms/backoffice/repository';
import type { UmbTargetPaginationRequestModel } from '@umbraco-cms/backoffice/utils';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  UmbTreeRepositoryBase,
  UmbTreeServerDataSourceBase,
  type UmbTreeChildrenOfRequestArgs,
  type UmbTreeItemModel,
  type UmbTreeRootItemsRequestArgs,
  type UmbTreeRootModel,
} from '@umbraco-cms/backoffice/tree';
import { treeChildren, treeRoot, type LibraryItemResponseModel, type PagedLibraryItemResponseModel } from '../api';
import {
  FOLDER_ENTITY_TYPE,
  FORM_ENTITY_TYPE,
  LIBRARY_ROOT_ENTITY_TYPE,
  RECYCLE_BIN_ENTITY_TYPE,
  TRASHED_FOLDER_ENTITY_TYPE,
  TRASHED_FORM_ENTITY_TYPE,
} from '../section/entity-types';
import { RESTRICTED_FLAG, UNSEEN_FLAG } from './aliases';

/** A folder or a form in the tree, with what its marks and menus need. */
export interface LibraryTreeItem extends UmbTreeItemModel {
  /** Whether a form has a published version; true for a folder, which has nothing to publish. */
  isPublished: boolean;
  /** What the user holds on it, by the permissions' stable names. */
  permissions: readonly string[];
  flags: { alias: string }[];
}

/** A library item as a tree shows it; its parent is a folder, the root, or in the trash the Recycle Bin. */
export function treeItemOf(item: LibraryItemResponseModel): LibraryTreeItem {
  const isFolder = item.kind === 'folder';
  const folderType = item.isTrashed ? TRASHED_FOLDER_ENTITY_TYPE : FOLDER_ENTITY_TYPE;
  const flags = [
    ...(item.isRestricted ? [{ alias: RESTRICTED_FLAG }] : []),
    ...(item.hasUnseenEntries ? [{ alias: UNSEEN_FLAG }] : []),
  ];
  return {
    unique: item.key,
    entityType: isFolder ? folderType : item.isTrashed ? TRASHED_FORM_ENTITY_TYPE : FORM_ENTITY_TYPE,
    parent: item.parentKey
      ? { unique: item.parentKey, entityType: folderType }
      : { unique: null, entityType: item.isTrashed ? RECYCLE_BIN_ENTITY_TYPE : LIBRARY_ROOT_ENTITY_TYPE },
    name: item.name,
    hasChildren: item.hasChildren,
    isFolder,
    icon: isFolder ? 'icon-folder' : 'icon-document',
    isPublished: isFolder || item.isPublished,
    permissions: item.permissions,
    flags,
  };
}

/** What the base expects of an answer, which holds either the page or the error. */
export type TreePage = UmbDataSourceResponse<UmbTargetPagedModel<LibraryItemResponseModel>>;

/** What the tree asks of a level on Umbraco 18: a page from an offset, or a window around one of its items (ADR-0250). */
export type TreePaging = UmbTreeRootItemsRequestArgs['paging'];

/** Reads a page of a level from the server, which pages by offset. */
export type LevelReader = (skip: number, take: number) => Promise<{ data?: PagedLibraryItemResponseModel | undefined }>;

/** Changes a page's items, such as a folders-only picker leaving the forms out. */
export type PageChange = (items: LibraryItemResponseModel[]) => LibraryItemResponseModel[];

/** The page the server answers with when the tree names none, and by which a window around an item is read. */
export const LEVEL_PAGE = 100;

const unchanged: PageChange = (items) => items;

/**
 * A page of items as the tree takes it, changed as asked, with how many come before and after it. A page that drops items
 * is the last one: a picker that asks for folders only drops forms, which come after every folder, so what stays ends
 * with it, and the tree asks for no more.
 */
export function pageOf(
  data: PagedLibraryItemResponseModel,
  change: PageChange,
  skip: number,
): UmbTargetPagedModel<LibraryItemResponseModel> {
  const items = change(data.items);
  const total = items.length < data.items.length ? skip + items.length : data.total;
  return { items, total, totalBefore: skip, totalAfter: Math.max(total - skip - items.length, 0) };
}

/** Reads a page of items as the tree base takes it; a picker that asks for folders only gets the folders. */
export async function readPage(
  host: UmbControllerHost,
  request: Promise<{ data?: PagedLibraryItemResponseModel | undefined }>,
  change: PageChange = unchanged,
  skip = 0,
): Promise<TreePage> {
  const { data, error } = await tryExecute(host, request);
  if (!data) return { error: error as Error };
  return { data: pageOf(data, change, skip) };
}

/**
 * Reads what the tree asks of a level: a page from an offset, or a window around an item. The server pages by offset
 * only, so a window is read page by page from the start until it holds the item and what the tree wants after it; an
 * item the level no longer holds answers "not found", on which the tree picks another item or starts from the top.
 */
export async function readLevel(
  host: UmbControllerHost,
  read: LevelReader,
  paging: TreePaging,
  change: PageChange = unchanged,
): Promise<TreePage> {
  if (paging && 'target' in paging) return readWindow(host, read, paging, change);
  const skip = paging?.skip ?? 0;
  return readPage(host, read(skip, paging?.take ?? LEVEL_PAGE), change, skip);
}

async function readWindow(
  host: UmbControllerHost,
  read: LevelReader,
  paging: UmbTargetPaginationRequestModel,
  change: PageChange,
): Promise<TreePage> {
  const items: LibraryItemResponseModel[] = [];
  let at = -1;
  for (let skip = 0; ; skip += LEVEL_PAGE) {
    const { data, error } = await tryExecute(host, read(skip, LEVEL_PAGE));
    if (!data) return { error: error as Error };
    const kept = change(data.items);
    items.push(...kept);
    const dropped = kept.length < data.items.length;
    const last = dropped || data.items.length === 0 || skip + data.items.length >= data.total;
    if (at < 0) at = items.findIndex((item) => item.key === paging.target.unique);
    if (last || (at >= 0 && items.length > at + paging.takeAfter)) {
      return windowOf(items, at, dropped ? items.length : data.total, paging);
    }
  }
}

// The window around the item at index `at` of the items read so far, of a level of `total` items; "not found" without it.
function windowOf(
  items: LibraryItemResponseModel[],
  at: number,
  total: number,
  paging: UmbTargetPaginationRequestModel,
): TreePage {
  if (at < 0) return { error: new Error(`The tree item ${paging.target.unique ?? ''} was not found.`) };
  const start = Math.max(0, at - paging.takeBefore);
  const end = Math.min(items.length, at + paging.takeAfter + 1);
  return { data: { items: items.slice(start, end), total, totalBefore: start, totalAfter: Math.max(total - end, 0) } };
}

const onlyFolders =
  (args: { foldersOnly?: boolean }): PageChange =>
  (items) =>
    args.foldersOnly ? items.filter((item) => item.kind === 'folder') : items;

class LibraryTreeDataSource extends UmbTreeServerDataSourceBase<LibraryItemResponseModel, LibraryTreeItem> {
  constructor(host: UmbControllerHost) {
    super(host, {
      getRootItems: (args: UmbTreeRootItemsRequestArgs) =>
        readLevel(host, (skip, take) => treeRoot({ query: { skip, take } }), args.paging, onlyFolders(args)),
      getChildrenOf: (args: UmbTreeChildrenOfRequestArgs) => {
        const parentKey = args.parent.unique;
        return readLevel(
          host,
          (skip, take) =>
            parentKey === null
              ? treeRoot({ query: { skip, take } })
              : treeChildren({ query: { parentKey, skip, take } }),
          args.paging,
          onlyFolders(args),
        );
      },
      // The tree opens from the root; a workspace that shows where it sits comes with 9.3a.
      getAncestorsOf: () => Promise.resolve({ data: [] }),
      mapper: treeItemOf,
    });
  }
}

export class LibraryTreeRepository extends UmbTreeRepositoryBase<LibraryTreeItem, UmbTreeRootModel> implements UmbApi {
  constructor(host: UmbControllerHost) {
    // The base's answer type names "data?: undefined", which exactOptionalPropertyTypes refuses; the class is what it asks for.
    super(
      host,
      LibraryTreeDataSource as unknown as ConstructorParameters<
        typeof UmbTreeRepositoryBase<LibraryTreeItem, UmbTreeRootModel>
      >[1],
    );
  }

  override async requestTreeRoot() {
    const data: UmbTreeRootModel = {
      unique: null,
      entityType: LIBRARY_ROOT_ENTITY_TYPE,
      name: '#jfbSection_forms',
      hasChildren: true,
      isFolder: true,
    };
    return { data };
  }
}

export { LibraryTreeRepository as api };
