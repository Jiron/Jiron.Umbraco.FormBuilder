// The option list workspace (ROADMAP 12.1, IA 7, ADR-0214): Umbraco's routable workspace over the list endpoints, with
// its one view, Save in its footer, and Duplicate and Delete... in its "...". The list screen itself is the sidebar
// item's workspace (section/manifests.ts); the create dialog it opens is registered here.
import { UmbSubmitWorkspaceAction } from '@umbraco-cms/backoffice/workspace';
import {
  NEW_OPTION_LIST_MODAL_ALIAS,
  OPTION_LIST_DETAIL_REPOSITORY_ALIAS,
  OPTION_LIST_DETAIL_STORE_ALIAS,
  OPTION_LIST_ENTITY_TYPE,
  OPTION_LIST_WORKSPACE_ALIAS,
} from './constants';

const IN_LIST_WORKSPACE = { alias: 'Umb.Condition.WorkspaceAlias', match: OPTION_LIST_WORKSPACE_ALIAS };

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'workspace',
    kind: 'routable',
    alias: OPTION_LIST_WORKSPACE_ALIAS,
    name: 'Jiron Form Builder option list workspace',
    api: () => import('./option-list-workspace.context'),
    meta: { entityType: OPTION_LIST_ENTITY_TYPE },
  },
  {
    type: 'repository',
    alias: OPTION_LIST_DETAIL_REPOSITORY_ALIAS,
    name: 'Jiron Form Builder option list detail repository',
    api: () => import('./option-list-detail.repository'),
  },
  {
    type: 'store',
    alias: OPTION_LIST_DETAIL_STORE_ALIAS,
    name: 'Jiron Form Builder option list detail store',
    api: () => import('./option-list-detail.store'),
  },
  {
    type: 'modal',
    alias: NEW_OPTION_LIST_MODAL_ALIAS,
    name: 'Jiron Form Builder new option list dialog',
    // The modal's element type asks for a modal context that exactOptionalPropertyTypes says may be missing.
    element: (() => import('./new-option-list.modal.element')) as never,
  },
  {
    // The one view, so Umbraco shows no tabs.
    type: 'workspaceView',
    alias: 'Jiron.FormBuilder.WorkspaceView.OptionList',
    name: 'Jiron Form Builder option list view',
    element: () => import('./option-list.view'),
    weight: 100,
    meta: { label: '#jfbOptionLists_view', pathname: 'options', icon: 'icon-bulleted-list' },
    conditions: [IN_LIST_WORKSPACE],
  },
  {
    type: 'workspaceAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.WorkspaceAction.OptionList.Save',
    name: 'Jiron Form Builder save option list',
    api: UmbSubmitWorkspaceAction,
    weight: 100,
    meta: { label: '#buttons_save', look: 'primary', color: 'positive' },
    conditions: [IN_LIST_WORKSPACE],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.OptionList.Duplicate',
    name: 'Jiron Form Builder duplicate option list',
    forEntityTypes: [OPTION_LIST_ENTITY_TYPE],
    // Umbraco puts the higher weight first.
    weight: 200,
    api: () => import('./duplicate-option-list.action'),
    meta: { icon: 'icon-documents', label: '#jfbOptionLists_duplicate' },
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.OptionList.Delete',
    name: 'Jiron Form Builder delete option list',
    forEntityTypes: [OPTION_LIST_ENTITY_TYPE],
    weight: 100,
    api: () => import('./delete-option-list.action'),
    meta: { icon: 'icon-delete', label: '#jfbOptionLists_delete' },
  },
];
