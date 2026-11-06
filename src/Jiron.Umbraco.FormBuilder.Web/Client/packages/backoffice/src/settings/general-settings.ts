// What Settings > General reads and saves (ROADMAP 10.3b, IA 8, ADR-0198): the general settings with their version, limits
// and the host's locks, and a save of the groups the screen holds, whose refusal comes back as what the screen says.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute, UmbApiError } from '@umbraco-cms/backoffice/resources';
import {
  generalSettingsGet,
  generalSettingsSave,
  type GeneralSettingsResponseModel,
  type HistorySettingsRequestModel,
} from '../api';
import { codeOf } from '../library/library-changes';

/** How a save ended: saved, values refused by field, saved by someone else meanwhile, locked by the host, or no answer. */
export type SaveAnswer =
  | { readonly kind: 'saved'; readonly settings: GeneralSettingsResponseModel }
  | { readonly kind: 'invalid'; readonly fields: ReadonlySet<string> }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'locked' }
  | { readonly kind: 'failed' };

/** The general settings; 'forbidden' for a user who may not manage settings, or undefined without an answer. */
export async function readGeneral(
  host: UmbClassInterface,
): Promise<GeneralSettingsResponseModel | 'forbidden' | undefined> {
  const { data, error } = await tryExecute(host, generalSettingsGet(), { disableNotifications: true });
  if (data) return data;
  return codeOf(error) === 'forbidden' ? 'forbidden' : undefined;
}

/** Saves the History group with the version read. */
export async function saveGeneral(
  host: UmbClassInterface,
  version: number,
  history: HistorySettingsRequestModel,
): Promise<SaveAnswer> {
  const { data, error } = await tryExecute(host, generalSettingsSave({ body: { version, history } }), {
    disableNotifications: true,
  });
  if (data) return { kind: 'saved', settings: data };
  switch (codeOf(error)) {
    case 'validation_failed':
      return { kind: 'invalid', fields: fieldsOf(error) };
    case 'concurrency_conflict':
      return { kind: 'conflict' };
    case 'setting_locked':
      return { kind: 'locked' };
    default:
      return { kind: 'failed' };
  }
}

/** The fields a refusal names, within the section: `general.history.draftDays` is `history.draftDays`. */
function fieldsOf(error: unknown): ReadonlySet<string> {
  const problem = UmbApiError.isUmbApiError(error)
    ? (error.problemDetails as unknown as { errors?: readonly { path?: string }[] } | null | undefined)
    : undefined;
  return new Set((problem?.errors ?? []).flatMap((one) => (one.path ? [one.path.replace(/^general\./, '')] : [])));
}
