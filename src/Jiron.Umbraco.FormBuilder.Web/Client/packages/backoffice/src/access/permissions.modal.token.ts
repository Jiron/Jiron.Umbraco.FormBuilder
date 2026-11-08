// The Permissions sidebar of a folder or form (ROADMAP 12.4a, IA 8, ADR-0218): its token, what it is opened with and
// what it answers.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { AccessTarget } from './access-api';

export const PERMISSIONS_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Permissions';

/** The folder or form whose access the sidebar shows. */
export interface PermissionsModalData {
  readonly target: AccessTarget;
  readonly key: string;
}

/** Whether the sidebar saved, so a list that names the folder or form reads it again. */
export interface PermissionsModalValue {
  readonly saved: boolean;
}

export const PERMISSIONS_MODAL = new UmbModalToken<PermissionsModalData, PermissionsModalValue>(
  PERMISSIONS_MODAL_ALIAS,
  {
    modal: { type: 'sidebar', size: 'medium' },
  },
);
