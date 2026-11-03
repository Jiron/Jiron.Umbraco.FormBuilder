// The workspace of a folder in the Recycle Bin (ADR-0166).
import { TRASHED_FOLDER_ENTITY_TYPE } from '../section/entity-types';
import { libraryWorkspace } from './library-workspace';

export const element = libraryWorkspace(
  'jfb-trashed-folder-workspace',
  TRASHED_FOLDER_ENTITY_TYPE,
  () => import('./trashed-item.element'),
  true,
);
