// What Settings > Access and the Permissions sidebar read and save (ROADMAP 12.4a, ADR-0218): every group's global
// grant, and a folder's or form's grants and restriction; a save's refusal comes back as what the screen says.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  accessGet,
  accessGetFolder,
  accessGetForm,
  accessSave,
  accessSaveFolder,
  accessSaveForm,
  type AccessResponseModel,
  type AccessTargetResponseModel,
} from '../api';
import { codeOf } from '../library/library-changes';
import type { GrantEdit } from './access-model';

/** A folder or a form, whose own access a sidebar shows. */
export type AccessTarget = 'folder' | 'form';

/** How a save ended: saved, refused, changed meanwhile, locked by the host, in the trash, gone, or no answer. */
export type AccessSaveAnswer<T> =
  | { readonly kind: 'saved'; readonly answer: T }
  | { readonly kind: 'invalid' }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'locked' }
  | { readonly kind: 'trashed' }
  | { readonly kind: 'gone' }
  | { readonly kind: 'failed' };

function refusal<T>(error: unknown): AccessSaveAnswer<T> {
  switch (codeOf(error)) {
    case 'validation_failed':
      return { kind: 'invalid' };
    case 'concurrency_conflict':
      return { kind: 'conflict' };
    case 'setting_locked':
      return { kind: 'locked' };
    case 'trashed':
      return { kind: 'trashed' };
    case 'folder_not_found':
    case 'form_not_found':
      return { kind: 'gone' };
    default:
      return { kind: 'failed' };
  }
}

/** Every group's global grant; 'forbidden' for a user who may not manage permissions, or undefined without an answer. */
export async function readAccess(host: UmbClassInterface): Promise<AccessResponseModel | 'forbidden' | undefined> {
  const { data, error } = await tryExecute(host, accessGet(), { disableNotifications: true });
  if (data) return data;
  return codeOf(error) === 'forbidden' ? 'forbidden' : undefined;
}

/** Saves the changed groups' global grants in one unit of work. */
export async function saveAccess(
  host: UmbClassInterface,
  grants: readonly GrantEdit[],
): Promise<AccessSaveAnswer<AccessResponseModel>> {
  const { data, error } = await tryExecute(host, accessSave({ body: { grants: [...grants] } }), {
    disableNotifications: true,
  });
  return data ? { kind: 'saved', answer: data } : refusal(error);
}

/** A folder's or form's grants and restriction; 'gone' when it no longer exists, or undefined without an answer. */
export async function readTarget(
  host: UmbClassInterface,
  target: AccessTarget,
  key: string,
): Promise<AccessTargetResponseModel | 'gone' | undefined> {
  const { data, error } = await tryExecute(
    host,
    target === 'folder' ? accessGetFolder({ path: { key } }) : accessGetForm({ path: { key } }),
    { disableNotifications: true },
  );
  if (data) return data;
  const code = codeOf(error);
  return code === 'folder_not_found' || code === 'form_not_found' ? 'gone' : undefined;
}

/** Saves a folder's or form's grants and restriction in one unit of work, a form's at the version read. */
export async function saveTarget(
  host: UmbClassInterface,
  target: AccessTarget,
  key: string,
  body: { readonly grants: readonly GrantEdit[]; readonly isRestricted: boolean; readonly version: number | null },
): Promise<AccessSaveAnswer<AccessTargetResponseModel>> {
  const request = { grants: [...body.grants], isRestricted: body.isRestricted, version: body.version };
  const { data, error } = await tryExecute(
    host,
    target === 'folder'
      ? accessSaveFolder({ path: { key }, body: request })
      : accessSaveForm({ path: { key }, body: request }),
    { disableNotifications: true },
  );
  return data ? { kind: 'saved', answer: data } : refusal(error);
}
