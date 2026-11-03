// The forms tree (ADR-0021, ADR-0165, ADR-0166): the folders and forms the user sees, from the server, which filters them by
// permission. Each item carries its marks as Umbraco's entity flags (restricted, unseen entries), and whether a form is
// published, which the tree's own item element mutes.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbApi } from '@umbraco-cms/backoffice/extension-api';
import type { UmbDataSourceResponse, UmbTargetPagedModel } from '@umbraco-cms/backoffice/repository';
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

/** Reads a page of items as the tree base takes it; a picker that asks for folders only gets the folders. */
export async function readPage(
  host: UmbControllerHost,
  request: Promise<{ data?: PagedLibraryItemResponseModel | undefined }>,
  change: (items: LibraryItemResponseModel[]) => LibraryItemResponseModel[] = (items) => items,
): Promise<TreePage> {
  const { data, error } = await tryExecute(host, request);
  if (!data) return { error: error as Error };
  const items = change(data.items);
  return { data: { items, total: data.total - (data.items.length - items.length) } };
}

const onlyFolders = (args: { foldersOnly?: boolean }) => (items: LibraryItemResponseModel[]) =>
  args.foldersOnly ? items.filter((item) => item.kind === 'folder') : items;

class LibraryTreeDataSource extends UmbTreeServerDataSourceBase<LibraryItemResponseModel, LibraryTreeItem> {
  constructor(host: UmbControllerHost) {
    super(host, {
      getRootItems: (args: UmbTreeRootItemsRequestArgs) =>
        readPage(host, treeRoot({ query: { skip: args.skip ?? 0, take: args.take ?? 100 } }), onlyFolders(args)),
      getChildrenOf: (args: UmbTreeChildrenOfRequestArgs) =>
        readPage(
          host,
          args.parent.unique === null
            ? treeRoot({ query: { skip: args.skip ?? 0, take: args.take ?? 100 } })
            : treeChildren({ query: { parentKey: args.parent.unique, skip: args.skip ?? 0, take: args.take ?? 100 } }),
          onlyFolders(args),
        ),
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
