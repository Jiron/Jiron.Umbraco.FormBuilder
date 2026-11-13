// The Recycle Bin's tree (ADR-0166): a tree of its own under the forms tree, as Umbraco's own recycle bins are. Its root is
// the bin, its first level the trash's top items, and a folder in the trash holds what went there with it.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbApi } from '@umbraco-cms/backoffice/extension-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  UmbTreeRepositoryBase,
  UmbTreeServerDataSourceBase,
  type UmbTreeChildrenOfRequestArgs,
  type UmbTreeRootItemsRequestArgs,
  type UmbTreeRootModel,
} from '@umbraco-cms/backoffice/tree';
import { trashList, treeChildren, type LibraryItemResponseModel } from '../api';
import { RECYCLE_BIN_ENTITY_TYPE } from '../section/entity-types';
import { readLevel, treeItemOf, type LibraryTreeItem, type TreePaging } from './library-tree.repository';

// The trash's top items sit right under the bin, whatever folder outside the trash a form went there from.
const underTheBin = (items: LibraryItemResponseModel[]) => items.map((item) => ({ ...item, parentKey: null }));

class RecycleBinTreeDataSource extends UmbTreeServerDataSourceBase<LibraryItemResponseModel, LibraryTreeItem> {
  constructor(host: UmbControllerHost) {
    const top = (paging: TreePaging) =>
      readLevel(host, (skip, take) => trashList({ query: { skip, take } }), paging, underTheBin);
    super(host, {
      getRootItems: (args: UmbTreeRootItemsRequestArgs) => top(args.paging),
      getChildrenOf: (args: UmbTreeChildrenOfRequestArgs) => {
        const parentKey = args.parent.unique;
        return parentKey === null
          ? top(args.paging)
          : readLevel(host, (skip, take) => treeChildren({ query: { parentKey, skip, take } }), args.paging);
      },
      getAncestorsOf: () => Promise.resolve({ data: [] }),
      mapper: treeItemOf,
    });
  }
}

export class RecycleBinTreeRepository
  extends UmbTreeRepositoryBase<LibraryTreeItem, UmbTreeRootModel>
  implements UmbApi
{
  constructor(host: UmbControllerHost) {
    super(
      host,
      RecycleBinTreeDataSource as unknown as ConstructorParameters<
        typeof UmbTreeRepositoryBase<LibraryTreeItem, UmbTreeRootModel>
      >[1],
    );
  }

  // The bin opens only when something is in it, which a page of one item says.
  override async requestTreeRoot() {
    const { data: page } = await tryExecute(this, trashList({ query: { take: 1 } }), { disableNotifications: true });
    const data: UmbTreeRootModel = {
      unique: null,
      entityType: RECYCLE_BIN_ENTITY_TYPE,
      name: '#jfbLibrary_recycleBin',
      hasChildren: (page?.total ?? 0) > 0,
      isFolder: true,
      icon: 'icon-trash',
    };
    return { data };
  }
}

export { RecycleBinTreeRepository as api };
