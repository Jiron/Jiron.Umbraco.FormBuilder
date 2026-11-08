// The email layout workspace (ROADMAP 12.1, IA 7, ADR-0215): Umbraco's routable workspace over the layout endpoints, with
// its one view, Save in its footer, and Duplicate and Delete... in its "...". The list screen itself is the sidebar
// item's workspace (section/manifests.ts).
import { UmbSubmitWorkspaceAction } from '@umbraco-cms/backoffice/workspace';
import {
  EMAIL_LAYOUT_DETAIL_REPOSITORY_ALIAS,
  EMAIL_LAYOUT_DETAIL_STORE_ALIAS,
  EMAIL_LAYOUT_ENTITY_TYPE,
  EMAIL_LAYOUT_WORKSPACE_ALIAS,
} from './constants';

const IN_LAYOUT_WORKSPACE = { alias: 'Umb.Condition.WorkspaceAlias', match: EMAIL_LAYOUT_WORKSPACE_ALIAS };

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'workspace',
    kind: 'routable',
    alias: EMAIL_LAYOUT_WORKSPACE_ALIAS,
    name: 'Jiron Form Builder email layout workspace',
    api: () => import('./email-layout-workspace.context'),
    meta: { entityType: EMAIL_LAYOUT_ENTITY_TYPE },
  },
  {
    type: 'repository',
    alias: EMAIL_LAYOUT_DETAIL_REPOSITORY_ALIAS,
    name: 'Jiron Form Builder email layout detail repository',
    api: () => import('./email-layout-detail.repository'),
  },
  {
    type: 'store',
    alias: EMAIL_LAYOUT_DETAIL_STORE_ALIAS,
    name: 'Jiron Form Builder email layout detail store',
    api: () => import('./email-layout-detail.store'),
  },
  {
    // The one view, so Umbraco shows no tabs.
    type: 'workspaceView',
    alias: 'Jiron.FormBuilder.WorkspaceView.EmailLayout',
    name: 'Jiron Form Builder email layout view',
    element: () => import('./email-layout.view'),
    weight: 100,
    meta: { label: '#jfbEmailLayouts_view', pathname: 'layout', icon: 'icon-layout' },
    conditions: [IN_LAYOUT_WORKSPACE],
  },
  {
    type: 'workspaceAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.WorkspaceAction.EmailLayout.Save',
    name: 'Jiron Form Builder save email layout',
    api: UmbSubmitWorkspaceAction,
    weight: 100,
    meta: { label: '#buttons_save', look: 'primary', color: 'positive' },
    conditions: [IN_LAYOUT_WORKSPACE],
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.EmailLayout.Duplicate',
    name: 'Jiron Form Builder duplicate email layout',
    forEntityTypes: [EMAIL_LAYOUT_ENTITY_TYPE],
    // Umbraco puts the higher weight first.
    weight: 200,
    api: () => import('./duplicate-email-layout.action'),
    meta: { icon: 'icon-documents', label: '#jfbEmailLayouts_duplicate' },
  },
  {
    type: 'entityAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.EntityAction.EmailLayout.Delete',
    name: 'Jiron Form Builder delete email layout',
    forEntityTypes: [EMAIL_LAYOUT_ENTITY_TYPE],
    weight: 100,
    api: () => import('./delete-email-layout.action'),
    meta: { icon: 'icon-delete', label: '#jfbEmailLayouts_delete' },
  },
];
