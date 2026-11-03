// The forms tree and the Recycle Bin's tree (ADR-0021, ADR-0165, ADR-0166). The forms tree's menu item shows the root's children
// directly under Umbraco's sidebar header; the bin's shows its root, the Recycle Bin, as the last item. A restricted item's lock
// and a form's unseen entries are Umbraco's entity signs.
import { FORMS_NEEDS } from '../section/sidebar';
import { PERMISSION_CONDITION } from '../section/my-permissions';
import {
  FOLDER_ENTITY_TYPE,
  FORM_ENTITY_TYPE,
  LIBRARY_ROOT_ENTITY_TYPE,
  RECYCLE_BIN_ENTITY_TYPE,
  TRASHED_FOLDER_ENTITY_TYPE,
  TRASHED_FORM_ENTITY_TYPE,
} from '../section/entity-types';
import {
  LIBRARY_TREE_ALIAS,
  LIBRARY_TREE_REPOSITORY_ALIAS,
  RECYCLE_BIN_TREE_ALIAS,
  RECYCLE_BIN_TREE_REPOSITORY_ALIAS,
  RESTRICTED_FLAG,
  UNSEEN_FLAG,
} from './aliases';

/** The search provider's alias. */
const SEARCH_PROVIDER_ALIAS = 'Jiron.FormBuilder.SearchProvider.Forms';

/** The trees, their signs and the header's search. */
export const manifests = (formsMenu: string, section: string): UmbExtensionManifest[] => [
  {
    type: 'repository',
    alias: LIBRARY_TREE_REPOSITORY_ALIAS,
    name: 'Jiron Form Builder library tree repository',
    api: () => import('./library-tree.repository'),
  },
  {
    type: 'tree',
    kind: 'default',
    alias: LIBRARY_TREE_ALIAS,
    name: 'Jiron Form Builder library tree',
    meta: { repositoryAlias: LIBRARY_TREE_REPOSITORY_ALIAS },
  },
  {
    type: 'treeItem',
    kind: 'default',
    alias: 'Jiron.FormBuilder.TreeItem.Library',
    name: 'Jiron Form Builder library tree item',
    element: () => import('./library-tree-item.element'),
    forEntityTypes: [
      LIBRARY_ROOT_ENTITY_TYPE,
      FOLDER_ENTITY_TYPE,
      FORM_ENTITY_TYPE,
      RECYCLE_BIN_ENTITY_TYPE,
      TRASHED_FOLDER_ENTITY_TYPE,
      TRASHED_FORM_ENTITY_TYPE,
    ],
  },
  {
    type: 'menuItem',
    kind: 'tree',
    alias: 'Jiron.FormBuilder.MenuItem.Forms',
    name: 'Jiron Form Builder forms tree menu item',
    weight: 200,
    meta: { label: '#jfbSection_forms', menus: [formsMenu], treeAlias: LIBRARY_TREE_ALIAS, hideTreeRoot: true },
  },
  {
    type: 'repository',
    alias: RECYCLE_BIN_TREE_REPOSITORY_ALIAS,
    name: 'Jiron Form Builder Recycle Bin tree repository',
    api: () => import('./recycle-bin-tree.repository'),
  },
  {
    type: 'tree',
    kind: 'default',
    alias: RECYCLE_BIN_TREE_ALIAS,
    name: 'Jiron Form Builder Recycle Bin tree',
    meta: { repositoryAlias: RECYCLE_BIN_TREE_REPOSITORY_ALIAS },
  },
  {
    type: 'menuItem',
    kind: 'tree',
    alias: 'Jiron.FormBuilder.MenuItem.RecycleBin',
    name: 'Jiron Form Builder Recycle Bin menu item',
    weight: 100,
    meta: {
      label: '#jfbLibrary_recycleBin',
      menus: [formsMenu],
      treeAlias: RECYCLE_BIN_TREE_ALIAS,
      hideTreeRoot: false,
    },
  },
  {
    type: 'entitySign',
    kind: 'icon',
    alias: 'Jiron.FormBuilder.EntitySign.Restricted',
    name: 'Jiron Form Builder restricted sign',
    forEntityFlags: [RESTRICTED_FLAG],
    meta: { iconName: 'icon-lock', label: '#jfbLibrary_restricted' },
  },
  {
    type: 'entitySign',
    kind: 'icon',
    alias: 'Jiron.FormBuilder.EntitySign.Unseen',
    name: 'Jiron Form Builder unseen entries sign',
    forEntityFlags: [UNSEEN_FLAG],
    meta: { iconName: 'icon-record', label: '#jfbLibrary_unseenEntries', iconColorAlias: 'blue' },
  },
  {
    type: 'searchProvider',
    alias: SEARCH_PROVIDER_ALIAS,
    name: 'Jiron Form Builder form search',
    api: () => import('./form-search.provider'),
  },
  {
    // Umbraco's own element, which its results show only for the entity types a manifest names.
    type: 'searchResultItem',
    alias: 'Jiron.FormBuilder.SearchResultItem.Form',
    name: 'Jiron Form Builder search result',
    elementName: 'umb-search-result-item',
    forEntityTypes: [FORM_ENTITY_TYPE],
  },
  {
    type: 'globalSearch',
    alias: 'Jiron.FormBuilder.GlobalSearch.Forms',
    name: 'Jiron Form Builder forms in the header search',
    // Umbraco shows a tab's label as written, in every language, as its own "Documents".
    meta: { label: 'Forms', searchProviderAlias: SEARCH_PROVIDER_ALIAS },
    conditions: [
      { alias: 'Umb.Condition.SectionUserPermission', match: section },
      { alias: PERMISSION_CONDITION, needs: FORMS_NEEDS },
    ],
  },
];
