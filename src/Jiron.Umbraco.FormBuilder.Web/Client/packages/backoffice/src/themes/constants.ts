// The theme screens' names (ROADMAP 12.1, ADR-0215): the entity types of the list and of one theme, and the aliases of the
// workspace and what is behind it, which manifests, actions and the list screen share without loading each other.
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type { UmbWorkspaceContext } from '@umbraco-cms/backoffice/workspace';
import { screenPath, SECTION_PATHNAME } from '../section/entity-types';
import type { ThemeDetailStore } from './theme-detail.store';
import type { ThemeWorkspaceContext } from './theme-workspace.context';

/** The list of themes, which the sidebar's item opens (section/sidebar.ts). */
export const THEMES_ENTITY_TYPE = 'jfb-themes';

/** One theme of the library, whose workspace a row opens. */
export const THEME_ENTITY_TYPE = 'jfb-theme';

export const THEME_WORKSPACE_ALIAS = 'Jiron.FormBuilder.Workspace.Theme';
export const THEME_DETAIL_REPOSITORY_ALIAS = 'Jiron.FormBuilder.Repository.ThemeDetail';
export const THEME_DETAIL_STORE_ALIAS = 'Jiron.FormBuilder.Store.ThemeDetail';

/** The create dialog, which asks the theme in code it builds on. */
export const NEW_THEME_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.NewTheme';

export const THEME_DETAIL_STORE_CONTEXT = new UmbContextToken<ThemeDetailStore>(THEME_DETAIL_STORE_ALIAS);

/** The open theme's workspace context, which its view and entity actions consume. */
export const THEME_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, ThemeWorkspaceContext>(
  'UmbWorkspaceContext',
  undefined,
  (context): context is ThemeWorkspaceContext => context.getEntityType() === THEME_ENTITY_TYPE,
);

/** The address of a new theme's workspace on a base theme, relative to the backoffice. */
export const newThemePath = (base: string): string =>
  `section/${SECTION_PATHNAME}/workspace/${THEME_ENTITY_TYPE}/create/${encodeURIComponent(base)}`;

/** The address of a theme's workspace. */
export const themePath = (key: string): string => screenPath(THEME_ENTITY_TYPE, key);
