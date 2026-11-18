// The email layout screens' names (ROADMAP 12.1, ADR-0215): the entity types of the list and of one layout, and the
// aliases of the workspace and what is behind it, which manifests, actions and the list screen share without loading
// each other.
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type { UmbWorkspaceContext } from '@umbraco-cms/backoffice/workspace';
import { SECTION_PATHNAME } from '../section/entity-types';
import type { EmailLayoutDetailStore } from './email-layout-detail.store';
import type { EmailLayoutWorkspaceContext } from './email-layout-workspace.context';

/** The list of email layouts, Library's Email layouts tab (section/sidebar.ts). */
export const EMAIL_LAYOUTS_ENTITY_TYPE = 'jfb-email-layouts';

/** One email layout of the library, whose workspace a row opens. */
export const EMAIL_LAYOUT_ENTITY_TYPE = 'jfb-email-layout';

export const EMAIL_LAYOUT_WORKSPACE_ALIAS = 'Jiron.FormBuilder.Workspace.EmailLayout';
export const EMAIL_LAYOUT_DETAIL_REPOSITORY_ALIAS = 'Jiron.FormBuilder.Repository.EmailLayoutDetail';
export const EMAIL_LAYOUT_DETAIL_STORE_ALIAS = 'Jiron.FormBuilder.Store.EmailLayoutDetail';

export const EMAIL_LAYOUT_DETAIL_STORE_CONTEXT = new UmbContextToken<EmailLayoutDetailStore>(
  EMAIL_LAYOUT_DETAIL_STORE_ALIAS,
);

/** The open layout's workspace context, which its view and entity actions consume. */
export const EMAIL_LAYOUT_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, EmailLayoutWorkspaceContext>(
  'UmbWorkspaceContext',
  undefined,
  (context): context is EmailLayoutWorkspaceContext => context.getEntityType() === EMAIL_LAYOUT_ENTITY_TYPE,
);

/** The address of a new layout's workspace, relative to the backoffice. */
export const newLayoutPath = (): string => `section/${SECTION_PATHNAME}/workspace/${EMAIL_LAYOUT_ENTITY_TYPE}/create`;
