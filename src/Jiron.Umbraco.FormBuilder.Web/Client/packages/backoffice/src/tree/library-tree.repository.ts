// The forms tree (ADR-0021, ADR-0165): the folders and forms the user sees, from the server, which filters them by permission.
// Step 9.2 adds its actions, its marks and the Recycle Bin.
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
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE } from '../section/entity-types';

/** A folder or a form in the tree. */
export type LibraryTreeItem = UmbTreeItemModel;

/** A library item as the tree shows it; its parent is a folder, or the root. */
export function treeItemOf(item: LibraryItemResponseModel): LibraryTreeItem {
  const isFolder = item.kind === 'folder';
  return {
    unique: item.key,
    entityType: isFolder ? FOLDER_ENTITY_TYPE : FORM_ENTITY_TYPE,
    parent: item.parentKey
      ? { unique: item.parentKey, entityType: FOLDER_ENTITY_TYPE }
      : { unique: null, entityType: LIBRARY_ROOT_ENTITY_TYPE },
    name: item.name,
    hasChildren: item.hasChildren,
    isFolder,
    icon: isFolder ? 'icon-folder' : 'icon-document',
  };
}

/** What the base expects of an answer, which holds either the page or the error. */
type TreePage = UmbDataSourceResponse<UmbTargetPagedModel<LibraryItemResponseModel>>;

class LibraryTreeDataSource extends UmbTreeServerDataSourceBase<LibraryItemResponseModel, LibraryTreeItem> {
  constructor(host: UmbControllerHost) {
    const read = async (request: Promise<{ data?: PagedLibraryItemResponseModel | undefined }>): Promise<TreePage> => {
      const { data, error } = await tryExecute(host, request);
      return data ? { data } : { error: error as Error };
    };
    super(host, {
      getRootItems: (args: UmbTreeRootItemsRequestArgs) =>
        read(treeRoot({ query: { skip: args.skip ?? 0, take: args.take ?? 100 } })),
      getChildrenOf: (args: UmbTreeChildrenOfRequestArgs) =>
        read(
          args.parent.unique === null
            ? treeRoot({ query: { skip: args.skip ?? 0, take: args.take ?? 100 } })
            : treeChildren({ query: { parentKey: args.parent.unique, skip: args.skip ?? 0, take: args.take ?? 100 } }),
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
