// The Recycle Bin's workspace (ADR-0166).
import { RECYCLE_BIN_ENTITY_TYPE } from '../section/entity-types';
import { libraryWorkspace } from './library-workspace';

export const element = libraryWorkspace(
  'jfb-recycle-bin-workspace',
  RECYCLE_BIN_ENTITY_TYPE,
  () => import('./recycle-bin.element'),
  false,
);
