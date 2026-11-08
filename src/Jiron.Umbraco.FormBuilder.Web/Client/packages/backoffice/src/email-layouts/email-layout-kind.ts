// Email layouts as a kind of the library (ROADMAP 12.1, ADR-0215): what the shared list screen, workspace header and
// actions need of them. A layout's header counts the email actions that use it.
import type { LibraryKind } from '../library/items/library-items';
import { EMAIL_LAYOUT_ENTITY_TYPE, EMAIL_LAYOUTS_ENTITY_TYPE } from './constants';
import { isLayoutAlias, layoutAliasOf, type EmailLayoutDetailModel } from './email-layout-model';
import { loadEmailLayoutWords } from './email-layout-words';
import { duplicateLayout, readLayout, readUsers } from './email-layouts-api';

export const EMAIL_LAYOUT_KIND: LibraryKind<EmailLayoutDetailModel> = {
  words: 'jfbEmailLayouts',
  load: loadEmailLayoutWords,
  entityType: EMAIL_LAYOUT_ENTITY_TYPE,
  listEntityType: EMAIL_LAYOUTS_ENTITY_TYPE,
  aliasPattern: '^[a-z][A-Za-z0-9]{0,63}$',
  isAlias: isLayoutAlias,
  aliasOf: layoutAliasOf,
  counts: 'uses',
  read: readLayout,
  duplicate: duplicateLayout,
  users: readUsers,
  repository: () => import('./email-layout-detail.repository').then((module) => module.EmailLayoutDetailRepository),
};
