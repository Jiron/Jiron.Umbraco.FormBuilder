// The option list screens' names (ROADMAP 12.1, ADR-0214): the entity types of the list and of one list, and the aliases
// of the workspace and what is behind it, which manifests, actions and the list screen share without loading each other.
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type { UmbWorkspaceContext } from '@umbraco-cms/backoffice/workspace';
import { screenPath, SECTION_PATHNAME } from '../section/entity-types';
import type { OptionListDetailStore } from './option-list-detail.store';
import type { OptionListWorkspaceContext } from './option-list-workspace.context';

/** The list of option lists, Library's first tab (section/sidebar.ts). */
export const OPTION_LISTS_ENTITY_TYPE = 'jfb-option-lists';

/** One option list, whose workspace a row opens. */
export const OPTION_LIST_ENTITY_TYPE = 'jfb-option-list';

export const OPTION_LIST_WORKSPACE_ALIAS = 'Jiron.FormBuilder.Workspace.OptionList';
export const OPTION_LIST_DETAIL_REPOSITORY_ALIAS = 'Jiron.FormBuilder.Repository.OptionListDetail';
export const OPTION_LIST_DETAIL_STORE_ALIAS = 'Jiron.FormBuilder.Store.OptionListDetail';

/** The create dialog, which asks the kind first. */
export const NEW_OPTION_LIST_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.NewOptionList';

export const OPTION_LIST_DETAIL_STORE_CONTEXT = new UmbContextToken<OptionListDetailStore>(
  OPTION_LIST_DETAIL_STORE_ALIAS,
);

/** The open list's workspace context, which its view and entity actions consume. */
export const OPTION_LIST_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, OptionListWorkspaceContext>(
  'UmbWorkspaceContext',
  undefined,
  (context): context is OptionListWorkspaceContext => context.getEntityType() === OPTION_LIST_ENTITY_TYPE,
);

/** The address of a list's workspace, or of a new list's of a kind, relative to the backoffice. */
export const listPath = (key: string): string => screenPath(OPTION_LIST_ENTITY_TYPE, key);
export const newListPath = (kind: string): string =>
  `section/${SECTION_PATHNAME}/workspace/${OPTION_LIST_ENTITY_TYPE}/create/${kind}`;
