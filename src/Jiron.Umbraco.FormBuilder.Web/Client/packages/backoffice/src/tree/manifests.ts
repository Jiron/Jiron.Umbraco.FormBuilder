// The forms tree's extensions (ADR-0021, ADR-0165). The menu item shows the root's children directly under Umbraco's sidebar header.
import { FORMS_NEEDS } from '../section/sidebar';
import { PERMISSION_CONDITION } from '../section/my-permissions';
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE } from '../section/entity-types';

const TREE_ALIAS = 'Jiron.FormBuilder.Tree.Library';
const TREE_REPOSITORY_ALIAS = 'Jiron.FormBuilder.Repository.LibraryTree';

/** The search provider's alias. */
const SEARCH_PROVIDER_ALIAS = 'Jiron.FormBuilder.SearchProvider.Forms';

/** The tree and the header's search. */
export const manifests = (formsMenu: string, section: string): UmbExtensionManifest[] => [
  {
    type: 'repository',
    alias: TREE_REPOSITORY_ALIAS,
    name: 'Jiron Form Builder library tree repository',
    api: () => import('./library-tree.repository'),
  },
  {
    type: 'tree',
    kind: 'default',
    alias: TREE_ALIAS,
    name: 'Jiron Form Builder library tree',
    meta: { repositoryAlias: TREE_REPOSITORY_ALIAS },
  },
  {
    type: 'treeItem',
    kind: 'default',
    alias: 'Jiron.FormBuilder.TreeItem.Library',
    name: 'Jiron Form Builder library tree item',
    forEntityTypes: [LIBRARY_ROOT_ENTITY_TYPE, FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE],
  },
  {
    type: 'menuItem',
    kind: 'tree',
    alias: 'Jiron.FormBuilder.MenuItem.Forms',
    name: 'Jiron Form Builder forms tree menu item',
    weight: 200,
    meta: { label: '#jfbSection_forms', menus: [formsMenu], treeAlias: TREE_ALIAS, hideTreeRoot: true },
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.Form.Trash',
    name: 'Jiron Form Builder trash a form',
    forEntityTypes: [FORM_ENTITY_TYPE],
    api: () => import('./trash-form.action'),
    meta: { icon: 'icon-trash', label: '#jfbSection_trash' },
    conditions: [{ alias: PERMISSION_CONDITION, needs: [{ anyOf: ['design'], anywhere: true }] }],
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
