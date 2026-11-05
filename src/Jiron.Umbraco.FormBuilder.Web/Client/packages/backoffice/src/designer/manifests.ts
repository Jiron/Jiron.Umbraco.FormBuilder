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
import { CHANGE_TYPE_MODAL_ALIAS } from './change-type/change-type.modal.token';
import { FORM_CHECK_MODAL_ALIAS } from './check/form-check.modal.token';
import { FIRST_VIEW } from './form-views';
import { MOVE_TO_MODAL_ALIAS } from './moves/move-to.modal.token';
import { SHORTCUTS_MODAL_ALIAS } from './moves/shortcuts.modal.token';
import { PASTE_QUESTIONS_MODAL_ALIAS } from './palette/paste-questions.modal.token';
import { PUBLISH_MODAL_ALIAS } from './publish.modal.token';
import { RULE_BUILDER_MODAL_ALIAS } from './rules/rule-builder.modal.token';
import { OUTCOME_MODAL_ALIAS } from './settings/outcome.modal.token';
import { RULES_OVERVIEW_MODAL_ALIAS } from './rules/rules-overview.modal.token';

const IN_FORM_WORKSPACE = { alias: 'Umb.Condition.WorkspaceAlias', match: FORM_WORKSPACE_ALIAS };
const holding = (...anyOf: string[]) => ({ alias: FORM_PERMISSION_CONDITION, anyOf });

type ViewElement = NonNullable<Extract<UmbExtensionManifest, { type: 'workspaceView' }>['element']>;

/**
 * The views in their order, with who sees each and the element of each view built: the first one a user sees is where
 * the form opens.
 */
const VIEWS: readonly { id: string; icon: string; anyOf: string[]; element?: ViewElement }[] = [
  { id: FIRST_VIEW, icon: 'icon-layout', anyOf: ['design'], element: () => import('./form-design.view') },
  { id: 'settings', icon: 'icon-settings', anyOf: ['design'], element: () => import('./settings/form-settings.view') },
  { id: 'actions', icon: 'icon-lightning', anyOf: ['design', 'manageActions'] },
  { id: 'entries', icon: 'icon-inbox', anyOf: ['viewEntries'] },
  { id: 'insights', icon: 'icon-chart', anyOf: ['viewEntries'] },
  { id: 'history', icon: 'icon-history', anyOf: ['design'] },
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
  {
    type: 'modal',
    alias: MOVE_TO_MODAL_ALIAS,
    name: 'Jiron Form Builder move to dialog',
    element: (() => import('./moves/move-to.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: SHORTCUTS_MODAL_ALIAS,
    name: 'Jiron Form Builder keyboard shortcuts dialog',
    element: (() => import('./moves/shortcuts.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: CHANGE_TYPE_MODAL_ALIAS,
    name: 'Jiron Form Builder change type dialog',
    element: (() => import('./change-type/change-type.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: RULE_BUILDER_MODAL_ALIAS,
    name: 'Jiron Form Builder rules builder',
    element: (() => import('./rules/rule-builder.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: OUTCOME_MODAL_ALIAS,
    name: 'Jiron Form Builder outcome sidebar',
    element: (() => import('./settings/outcome.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: RULES_OVERVIEW_MODAL_ALIAS,
    name: 'Jiron Form Builder rules overview',
    element: (() => import('./rules/rules-overview.modal.element')) as never,
  },
  {
    type: 'modal',
    alias: FORM_CHECK_MODAL_ALIAS,
    name: 'Jiron Form Builder form check',
    element: (() => import('./check/form-check.modal.element')) as never,
  },
  ...VIEWS.map((view, index): UmbExtensionManifest => ({
    type: 'workspaceView',
    alias: `Jiron.FormBuilder.WorkspaceView.Form.${pascal(view.id)}`,
    name: `Jiron Form Builder form ${view.id} view`,
    element: view.element ?? (() => import('../section/coming-screen.element')),
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
