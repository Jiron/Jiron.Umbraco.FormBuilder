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

export const FORM_DETAIL_STORE_CONTEXT = new UmbContextToken<FormDetailStore>(FORM_DETAIL_STORE_ALIAS);

/** The open form's workspace context, which the views and actions inside the form workspace consume. */
export const FORM_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, FormWorkspaceContext>(
  'UmbWorkspaceContext',
  undefined,
  (context): context is FormWorkspaceContext => context.getEntityType() === FORM_ENTITY_TYPE,
);
