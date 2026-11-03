// A folder's workspace, which shows its list (ADR-0166).
import { FOLDER_ENTITY_TYPE } from '../section/entity-types';
import { libraryWorkspace } from './library-workspace';

export const element = libraryWorkspace(
  'jfb-folder-workspace',
  FOLDER_ENTITY_TYPE,
  () => import('./folder-list.element'),
  true,
);
