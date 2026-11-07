// The form workspace's names (ROADMAP 9.3a): its alias, which the views, actions and conditions name, and those of the
// repository and store behind it.
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type { UmbWorkspaceContext } from '@umbraco-cms/backoffice/workspace';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import type { FormDetailStore } from './form-detail.store';
import type { FormWorkspaceContext } from './form-workspace.context';

/** The form workspace, which the views, the footer's actions and the form permission condition name. */
export const FORM_WORKSPACE_ALIAS = 'Jiron.FormBuilder.Workspace.Form';

export const FORM_DETAIL_REPOSITORY_ALIAS = 'Jiron.FormBuilder.Repository.FormDetail';
export const FORM_DETAIL_STORE_ALIAS = 'Jiron.FormBuilder.Store.FormDetail';

/** The condition that shows a view or an action by what the user holds on the open form. */
export const FORM_PERMISSION_CONDITION = 'Jiron.FormBuilder.Condition.FormPermission';

/**
 * An action's sidebar (ROADMAP 10.2a): named here, which the backoffice loads at its start, so its token loads with the
 * Actions view (ADR-0188).
 */
export const ACTION_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Action';

/** The Details of an action's last run (ROADMAP 10.2f, ADR-0195), named here for the same reason. */
export const ACTION_RUN_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.ActionRun';

/** Two versions compared in words, in History (ROADMAP 10.3a, ADR-0196), named here for the same reason. */
export const VERSION_COMPARISON_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.VersionComparison';

/** A version's comment, in History (ROADMAP 10.3a, ADR-0196). */
export const VERSION_COMMENT_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.VersionComment';

/** Review both versions, after a save someone else's save refused (ROADMAP 10.3c, ADR-0200). */
export const REVIEW_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.ReviewBoth';

/** The earlier versions to restore one from, in the form's "..." (ROADMAP 10.3b, ADR-0198). */
export const VERSION_PICKER_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.VersionPicker';

/** The preview, from the form workspace's footer (ROADMAP 10.3e, ADR-0203). */
export const PREVIEW_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Preview';

export const FORM_DETAIL_STORE_CONTEXT = new UmbContextToken<FormDetailStore>(FORM_DETAIL_STORE_ALIAS);

/** The open form's workspace context, which the views and actions inside the form workspace consume. */
export const FORM_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, FormWorkspaceContext>(
  'UmbWorkspaceContext',
  undefined,
  (context): context is FormWorkspaceContext => context.getEntityType() === FORM_ENTITY_TYPE,
);
