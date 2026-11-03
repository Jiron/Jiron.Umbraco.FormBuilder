// The form library's extensions (docs/ux/information-architecture.md, sections 3 and 4; ADR-0166): the screens of the top of Forms,
// a folder, the Recycle Bin and what it holds; the create dialog's options; each item's "..." in the order the IA gives, its
// most common action first, which Umbraco shows beside it; the dialogs and sidebars; and the two conditions the menus need.
import { PERMISSION_CONDITION } from '../section/my-permissions';
import {
  FOLDER_ENTITY_TYPE,
  FORM_ENTITY_TYPE,
  LIBRARY_ROOT_ENTITY_TYPE,
  RECYCLE_BIN_ENTITY_TYPE,
  TRASHED_FOLDER_ENTITY_TYPE,
  TRASHED_FORM_ENTITY_TYPE,
} from '../section/entity-types';
import { EMPTY_FOLDER_CONDITION, IN_TREE_CONDITION } from '../tree/aliases';
import {
  COPY_ACTIONS_MODAL_ALIAS,
  DUPLICATE_MODAL_ALIAS,
  FOLDER_NAME_MODAL_ALIAS,
  IMPORT_MODAL_ALIAS,
  RESTORE_MODAL_ALIAS,
  TEMPLATE_GALLERY_MODAL_ALIAS,
} from './modals/tokens';

// Umbraco's modal base types its optional members as "T | undefined", which exactOptionalPropertyTypes does not accept for a
// manifest's element (as for the picker's sidebar, ADR-0134).
type Element = UmbExtensionManifest extends { element?: infer E } ? E : never;
const element = (loader: () => Promise<unknown>) => loader as unknown as Element;

const DESIGN = { alias: PERMISSION_CONDITION, needs: [{ anyOf: ['design'], anywhere: true }] };
const MANAGE_ACTIONS = { alias: PERMISSION_CONDITION, needs: [{ anyOf: ['manageActions'], anywhere: true }] };
const PLACES = [LIBRARY_ROOT_ENTITY_TYPE, FOLDER_ENTITY_TYPE];
const TRASHED = [TRASHED_FORM_ENTITY_TYPE, TRASHED_FOLDER_ENTITY_TYPE];

const workspace = (id: string, entityType: string, loader: () => Promise<unknown>): UmbExtensionManifest => ({
  type: 'workspace',
  alias: `Jiron.FormBuilder.Workspace.${id}`,
  name: `Jiron Form Builder ${id} workspace`,
  element: element(loader),
  meta: { entityType },
});

const action = (
  id: string,
  forEntityTypes: string[],
  weight: number,
  icon: string,
  loader: () => Promise<unknown>,
  conditions: { alias: string }[] = [DESIGN],
): UmbExtensionManifest => ({
  type: 'entityAction',
  kind: 'default',
  alias: `Jiron.FormBuilder.EntityAction.${id}`,
  name: `Jiron Form Builder ${id}`,
  forEntityTypes,
  weight,
  api: loader as never,
  meta: { icon, label: `#jfbLibrary_action${id}` },
  conditions,
});

const option = (id: string, weight: number, icon: string, loader: () => Promise<unknown>): UmbExtensionManifest => ({
  type: 'entityCreateOptionAction',
  alias: `Jiron.FormBuilder.EntityCreateOptionAction.${id}`,
  name: `Jiron Form Builder create ${id}`,
  forEntityTypes: PLACES,
  weight,
  api: loader as never,
  meta: { icon, label: `#jfbLibrary_option${id}`, description: `#jfbLibrary_option${id}Description` },
});

const modal = (alias: string, loader: () => Promise<unknown>): UmbExtensionManifest => ({
  type: 'modal',
  alias,
  name: alias.replace('Jiron.FormBuilder.Modal.', 'Jiron Form Builder ') + ' modal',
  element: element(loader),
});

export const manifests: UmbExtensionManifest[] = [
  workspace('Root', LIBRARY_ROOT_ENTITY_TYPE, () => import('./root.workspace')),
  workspace('Folder', FOLDER_ENTITY_TYPE, () => import('./folder.workspace')),
  workspace('RecycleBin', RECYCLE_BIN_ENTITY_TYPE, () => import('./recycle-bin.workspace')),
  workspace('TrashedForm', TRASHED_FORM_ENTITY_TYPE, () => import('./trashed-form.workspace')),
  workspace('TrashedFolder', TRASHED_FOLDER_ENTITY_TYPE, () => import('./trashed-folder.workspace')),

  {
    type: 'entityAction',
    kind: 'create',
    alias: 'Jiron.FormBuilder.EntityAction.Create',
    name: 'Jiron Form Builder create',
    forEntityTypes: PLACES,
    weight: 1200,
    // Umbraco's own label: the kind adds the ellipsis, as for its other create menus.
    meta: { icon: 'icon-add', label: '#actions_create', additionalOptions: true },
    conditions: [DESIGN],
  },
  option('Form', 400, 'icon-document', () => import('./actions/create-form.option')),
  option('FromTemplate', 300, 'icon-layers', () => import('./actions/create-from-template.option')),
  option('Import', 200, 'icon-cloud-upload', () => import('./actions/import-form.option')),
  option('Folder', 100, 'icon-folder', () => import('./actions/create-folder.option')),
  {
    type: 'entityAction',
    kind: 'reloadTreeItemChildren',
    alias: 'Jiron.FormBuilder.EntityAction.Reload',
    name: 'Jiron Form Builder reload',
    forEntityTypes: [LIBRARY_ROOT_ENTITY_TYPE],
    weight: 100,
    meta: { icon: 'icon-refresh', label: '#jfbLibrary_actionReload' },
  },

  action('RenameFolder', [FOLDER_ENTITY_TYPE], 1100, 'icon-edit', () => import('./actions/rename-folder.action'), [
    DESIGN,
    { alias: IN_TREE_CONDITION },
  ]),
  action('Move', [FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE], 900, 'icon-enter', () => import('./actions/move.action')),
  action('DeleteFolder', [FOLDER_ENTITY_TYPE], 800, 'icon-delete', () => import('./actions/delete-folder.action'), [
    DESIGN,
    { alias: EMPTY_FOLDER_CONDITION },
  ]),

  action('Duplicate', [FORM_ENTITY_TYPE], 1000, 'icon-documents', () => import('./actions/duplicate-form.action')),
  action('CopyActions', [FORM_ENTITY_TYPE], 800, 'icon-split', () => import('./actions/copy-actions.action'), [
    MANAGE_ACTIONS,
  ]),
  action('Export', [FORM_ENTITY_TYPE], 700, 'icon-download-alt', () => import('./actions/export-form.action')),
  action('Trash', [FORM_ENTITY_TYPE], 100, 'icon-trash', () => import('./actions/trash-form.action')),

  action('EmptyBin', [RECYCLE_BIN_ENTITY_TYPE], 1000, 'icon-trash', () => import('./actions/empty-bin.action'), []),
  action('Restore', TRASHED, 1000, 'icon-undo', () => import('./actions/restore.action')),
  action('DeleteForGood', TRASHED, 900, 'icon-delete', () => import('./actions/delete-trashed.action')),

  {
    type: 'condition',
    alias: IN_TREE_CONDITION,
    name: 'Jiron Form Builder in the tree',
    api: () => import('./in-tree.condition'),
  },
  {
    type: 'condition',
    alias: EMPTY_FOLDER_CONDITION,
    name: 'Jiron Form Builder empty folder',
    api: () => import('./empty-folder.condition'),
  },

  modal(FOLDER_NAME_MODAL_ALIAS, () => import('./modals/folder-name.modal.element')),
  modal(DUPLICATE_MODAL_ALIAS, () => import('./modals/duplicate.modal.element')),
  modal(RESTORE_MODAL_ALIAS, () => import('./modals/restore.modal.element')),
  modal(COPY_ACTIONS_MODAL_ALIAS, () => import('./modals/copy-actions.modal.element')),
  modal(TEMPLATE_GALLERY_MODAL_ALIAS, () => import('./modals/template-gallery.modal.element')),
  modal(IMPORT_MODAL_ALIAS, () => import('./modals/import.modal.element')),
];
