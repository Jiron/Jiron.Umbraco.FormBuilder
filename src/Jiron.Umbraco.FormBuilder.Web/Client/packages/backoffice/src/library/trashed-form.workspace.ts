// The workspace of a form in the Recycle Bin (ADR-0166).
import { TRASHED_FORM_ENTITY_TYPE } from '../section/entity-types';
import { libraryWorkspace } from './library-workspace';

export const element = libraryWorkspace(
  'jfb-trashed-form-workspace',
  TRASHED_FORM_ENTITY_TYPE,
  () => import('./trashed-item.element'),
  true,
);
