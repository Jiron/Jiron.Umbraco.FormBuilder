// Option lists as a kind of the library (ROADMAP 12.1, ADR-0214): what the shared list screen, workspace header and
// actions need of them.
import type { LibraryKind } from '../library/items/library-items';
import { OPTION_LIST_ENTITY_TYPE, OPTION_LISTS_ENTITY_TYPE } from './constants';
import { aliasOfName, isAlias, type OptionListDetailModel } from './option-list-model';
import { loadOptionListWords } from './option-list-words';
import { duplicateList, readList, readUsers } from './option-lists-api';

export const OPTION_LIST_KIND: LibraryKind<OptionListDetailModel> = {
  words: 'jfbOptionLists',
  load: loadOptionListWords,
  entityType: OPTION_LIST_ENTITY_TYPE,
  listEntityType: OPTION_LISTS_ENTITY_TYPE,
  aliasPattern: '^[a-z][A-Za-z0-9]{0,63}$',
  isAlias,
  aliasOf: aliasOfName,
  counts: 'forms',
  read: readList,
  duplicate: duplicateList,
  users: readUsers,
  repository: () => import('./option-list-detail.repository').then((module) => module.OptionListDetailRepository),
};
