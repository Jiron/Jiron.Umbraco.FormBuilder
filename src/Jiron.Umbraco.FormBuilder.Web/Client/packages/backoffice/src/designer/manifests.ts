// The form workspace (ROADMAP 9.3a, docs/ux/information-architecture.md sections 5 and 11): Umbraco's routable workspace
// over the draft endpoint, its six views in Umbraco's tabs, each for who may use it on the open form, and Save draft and
// Publish... in its footer for who may design it. The views whose steps are still to come show the coming screen,
// which step 13.4 checks is gone.
import { UmbSubmitWorkspaceAction } from '@umbraco-cms/backoffice/workspace';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import {
  FORM_DETAIL_REPOSITORY_ALIAS,
  FORM_DETAIL_STORE_ALIAS,
  FORM_PERMISSION_CONDITION,
  FORM_WORKSPACE_ALIAS,
} from './constants';
import { PASTE_QUESTIONS_MODAL_ALIAS } from './palette/paste-questions.modal.token';
import { PUBLISH_MODAL_ALIAS } from './publish.modal.token';

const IN_FORM_WORKSPACE = { alias: 'Umb.Condition.WorkspaceAlias', match: FORM_WORKSPACE_ALIAS };
const holding = (...anyOf: string[]) => ({ alias: FORM_PERMISSION_CONDITION, anyOf });

/** The views in their order, with who sees each: the first one a user sees is where the form opens. */
const VIEWS: readonly { id: string; icon: string; anyOf: string[]; ready: boolean }[] = [
  { id: 'design', icon: 'icon-layout', anyOf: ['design'], ready: true },
  { id: 'settings', icon: 'icon-settings', anyOf: ['design'], ready: false },
  { id: 'actions', icon: 'icon-lightning', anyOf: ['design', 'manageActions'], ready: false },
  { id: 'entries', icon: 'icon-inbox', anyOf: ['viewEntries'], ready: false },
  { id: 'insights', icon: 'icon-chart', anyOf: ['viewEntries'], ready: false },
  { id: 'history', icon: 'icon-history', anyOf: ['design'], ready: false },
];

const pascal = (id: string): string => id.charAt(0).toUpperCase() + id.slice(1);

export const manifests: UmbExtensionManifest[] = [
  {
    type: 'workspace',
    kind: 'routable',
    alias: FORM_WORKSPACE_ALIAS,
    name: 'Jiron Form Builder form workspace',
    api: () => import('./form-workspace.context'),
    meta: { entityType: FORM_ENTITY_TYPE },
  },
  {
    type: 'repository',
    alias: FORM_DETAIL_REPOSITORY_ALIAS,
    name: 'Jiron Form Builder form detail repository',
    api: () => import('./form-detail.repository'),
  },
  {
    type: 'store',
    alias: FORM_DETAIL_STORE_ALIAS,
    name: 'Jiron Form Builder form detail store',
    api: () => import('./form-detail.store'),
  },
  {
    type: 'condition',
    alias: FORM_PERMISSION_CONDITION,
    name: 'Jiron Form Builder form permission condition',
    api: () => import('./form-permission.condition'),
  },
  {
    type: 'modal',
    alias: PUBLISH_MODAL_ALIAS,
    name: 'Jiron Form Builder publish dialog',
    // The modal's element type asks for a modal context that exactOptionalPropertyTypes says may be missing.
    element: (() => import('./publish.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: PASTE_QUESTIONS_MODAL_ALIAS,
    name: 'Jiron Form Builder paste questions dialog',
    element: (() => import('./palette/paste-questions.modal.element')) as never,
  },
  ...VIEWS.map((view, index): UmbExtensionManifest => ({
    type: 'workspaceView',
    alias: `Jiron.FormBuilder.WorkspaceView.Form.${pascal(view.id)}`,
    name: `Jiron Form Builder form ${view.id} view`,
    element: view.ready ? () => import('./form-design.view') : () => import('../section/coming-screen.element'),
    // Umbraco puts the higher weight first.
    weight: (VIEWS.length - index) * 100,
    meta: { label: `#jfbDesigner_view_${view.id}`, pathname: view.id, icon: view.icon },
    conditions: [IN_FORM_WORKSPACE, holding(...view.anyOf)],
  })),
  {
    type: 'workspaceAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.WorkspaceAction.Form.SaveDraft',
    name: 'Jiron Form Builder save draft',
    api: UmbSubmitWorkspaceAction,
    // Umbraco puts the higher weight first: Save draft, then the primary Publish... at the end.
    weight: 100,
    meta: { label: '#jfbDesigner_saveDraft', look: 'secondary' },
    conditions: [IN_FORM_WORKSPACE, holding('design')],
  },
  {
    type: 'workspaceAction',
    kind: 'default',
    alias: 'Jiron.FormBuilder.WorkspaceAction.Form.Publish',
    name: 'Jiron Form Builder publish',
    api: () => import('./publish.action'),
    weight: 90,
    meta: { label: '#jfbDesigner_publish', look: 'primary', color: 'positive' },
    conditions: [IN_FORM_WORKSPACE, holding('design')],
  },
];
