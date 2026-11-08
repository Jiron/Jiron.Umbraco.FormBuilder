// The theme workspace (ROADMAP 12.1, IA 7, ADR-0215): Umbraco's routable workspace over the theme endpoints, with its one
// view, Save in its footer, and Duplicate, Export and Delete... in its "...". The list screen itself is the sidebar item's
// workspace (section/manifests.ts); the create dialog it opens is registered here.
import { UmbSubmitWorkspaceAction } from '@umbraco-cms/backoffice/workspace';
import {
  NEW_THEME_MODAL_ALIAS,
  THEME_DETAIL_REPOSITORY_ALIAS,
  THEME_DETAIL_STORE_ALIAS,
  THEME_ENTITY_TYPE,
  THEME_WORKSPACE_ALIAS,
} from './constants';

const IN_THEME_WORKSPACE = { alias: 'Umb.Condition.WorkspaceAlias', match: THEME_WORKSPACE_ALIAS };

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'workspace',
    kind: 'routable',
    alias: THEME_WORKSPACE_ALIAS,
    name: 'Jiron Form Builder theme workspace',
    api: () => import('./theme-workspace.context'),
    meta: { entityType: THEME_ENTITY_TYPE },
  },
  {
    type: 'repository',
    alias: THEME_DETAIL_REPOSITORY_ALIAS,
    name: 'Jiron Form Builder theme detail repository',
    api: () => import('./theme-detail.repository'),
  },
  {
    type: 'store',
    alias: THEME_DETAIL_STORE_ALIAS,
    name: 'Jiron Form Builder theme detail store',
    api: () => import('./theme-detail.store'),
  },
  {
    type: 'modal',
    alias: NEW_THEME_MODAL_ALIAS,
    name: 'Jiron Form Builder new theme dialog',
    // The modal's element type asks for a modal context that exactOptionalPropertyTypes says may be missing.
    element: (() => import('./new-theme.modal.element')) as never,
  },
  {
    // The one view, so Umbraco shows no tabs.
    type: 'workspaceView',
    alias: 'Jiron.FormBuilder.WorkspaceView.Theme',
    name: 'Jiron Form Builder theme view',
    element: () => import('./theme.view'),
    weight: 100,
    meta: { label: '#jfbThemes_view', pathname: 'theme', icon: 'icon-palette' },
    conditions: [IN_THEME_WORKSPACE],
  },
  {
    type: 'workspaceAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.WorkspaceAction.Theme.Save',
    name: 'Jiron Form Builder save theme',
    api: UmbSubmitWorkspaceAction,
    weight: 100,
    meta: { label: '#buttons_save', look: 'primary', color: 'positive' },
    conditions: [IN_THEME_WORKSPACE],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.Theme.Duplicate',
    name: 'Jiron Form Builder duplicate theme',
    forEntityTypes: [THEME_ENTITY_TYPE],
    // Umbraco puts the higher weight first.
    weight: 300,
    api: () => import('./duplicate-theme.action'),
    meta: { icon: 'icon-documents', label: '#jfbThemes_duplicate' },
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.Theme.Export',
    name: 'Jiron Form Builder export theme',
    forEntityTypes: [THEME_ENTITY_TYPE],
    weight: 200,
    api: () => import('./export-theme.action'),
    meta: { icon: 'icon-download-alt', label: '#jfbThemes_export' },
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.Theme.Delete',
    name: 'Jiron Form Builder delete theme',
    forEntityTypes: [THEME_ENTITY_TYPE],
    weight: 100,
    api: () => import('./delete-theme.action'),
    meta: { icon: 'icon-delete', label: '#jfbThemes_delete' },
  },
];
