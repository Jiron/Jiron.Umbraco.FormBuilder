// The workspace of the top of Forms, which shows its list as a folder's screen does (ADR-0166).
import { LIBRARY_ROOT_ENTITY_TYPE } from '../section/entity-types';
import { libraryWorkspace } from './library-workspace';

export const element = libraryWorkspace(
  'jfb-library-root-workspace',
  LIBRARY_ROOT_ENTITY_TYPE,
  () => import('./folder-list.element'),
  false,
);
