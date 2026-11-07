// An entry's workspace (ROADMAP 11.2, ADR-0208): its key from `edit/<key>`, as the library's screens take theirs, so a
// link to another entry gives the screen its new key.
import { libraryWorkspace } from '../library/library-workspace';
import { ENTRY_ENTITY_TYPE } from '../section/entity-types';

export const element = libraryWorkspace(
  'jfb-entry-workspace',
  ENTRY_ENTITY_TYPE,
  () => import('./entry-screen.element'),
  true,
);
