// Themes as a kind of the library (ROADMAP 12.1, ADR-0215): what the shared list screen, workspace header and actions
// need of them. Forms name a theme by its alias, so a saved theme keeps it.
import type { LibraryKind } from '../library/items/library-items';
import { THEME_ENTITY_TYPE, THEMES_ENTITY_TYPE } from './constants';
import { isThemeAlias, themeAliasOf, type ThemeDetailModel } from './theme-model';
import { loadThemeWords } from './theme-words';
import { duplicateTheme, readTheme, readUsers } from './themes-api';

export const THEME_KIND: LibraryKind<ThemeDetailModel> = {
  words: 'jfbThemes',
  load: loadThemeWords,
  entityType: THEME_ENTITY_TYPE,
  listEntityType: THEMES_ENTITY_TYPE,
  aliasPattern: '^[a-z][a-z0-9-]{0,63}$',
  isAlias: isThemeAlias,
  aliasOf: themeAliasOf,
  aliasFixed: true,
  counts: 'forms',
  read: readTheme,
  duplicate: duplicateTheme,
  users: readUsers,
  repository: () => import('./theme-detail.repository').then((module) => module.ThemeDetailRepository),
};
