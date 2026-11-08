// What the global settings screens read and save (ROADMAP 12.4a, IA 8 and 9, ADR-0218): one section in a scope, all
// sites or one, with its fields, locks and facts; a save of the fields the screen changed, whose refusal comes back as
// what the screen says; and the test mail Email sends to the user who asks.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute, UmbApiError } from '@umbraco-cms/backoffice/resources';
import {
  settingsGet,
  settingsSave,
  testEmailSend,
  type SettingsSectionRequestModel,
  type SettingsSectionResponseModel,
} from '../api';
import { codeOf } from '../library/library-changes';

/** A field the server refused: why in its words, and the code that says what kind of refusal it is. */
export interface Refused {
  readonly code: string;
  readonly message: string;
}

/** How a save ended: saved, refused by field, saved by someone else meanwhile, locked by the host, or no answer. */
export type SaveAnswer =
  | { readonly kind: 'saved'; readonly section: SettingsSectionResponseModel }
  | { readonly kind: 'invalid'; readonly fields: ReadonlyMap<string, Refused> }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'locked' }
  | { readonly kind: 'siteUnknown' }
  | { readonly kind: 'failed' };

/** A section in a scope; 'forbidden' for a user who may not manage settings, or undefined without an answer. */
export async function readSection(
  host: UmbClassInterface,
  section: string,
  site: string | null,
): Promise<SettingsSectionResponseModel | 'forbidden' | undefined> {
  const { data, error } = await tryExecute(host, settingsGet({ path: { section }, query: site ? { site } : {} }), {
    disableNotifications: true,
  });
  if (data) return data;
  return codeOf(error) === 'forbidden' ? 'forbidden' : undefined;
}

/** Saves the fields a request sends, with the version read, in its scope. */
export async function saveSection(
  host: UmbClassInterface,
  section: string,
  body: SettingsSectionRequestModel,
): Promise<SaveAnswer> {
  const { data, error } = await tryExecute(host, settingsSave({ path: { section }, body }), {
    disableNotifications: true,
  });
  if (data) return { kind: 'saved', section: data };
  switch (codeOf(error)) {
    case 'validation_failed':
      return { kind: 'invalid', fields: refusedIn(section, error) };
    case 'concurrency_conflict':
      return { kind: 'conflict' };
    case 'setting_locked':
      return { kind: 'locked' };
    case 'site_unknown':
      return { kind: 'siteUnknown' };
    default:
      return { kind: 'failed' };
  }
}

/** How a test mail ended: queued to the user's own address, or not sent. */
export type TestMail = { readonly kind: 'sent'; readonly to: string } | { readonly kind: 'failed' };

/** Queues a test mail of a scope's sender and layout to the user who asks. */
export async function sendTestMail(host: UmbClassInterface, site: string | null): Promise<TestMail> {
  const { data } = await tryExecute(host, testEmailSend({ body: { site } }), { disableNotifications: true });
  return data ? { kind: 'sent', to: data.to } : { kind: 'failed' };
}

/** The fields a refusal names, within the section: `general.history.draftDays` is `history.draftDays`. */
function refusedIn(section: string, error: unknown): ReadonlyMap<string, Refused> {
  const problem = UmbApiError.isUmbApiError(error)
    ? (error.problemDetails as unknown as
        { errors?: readonly { path?: string; code?: string; message?: string }[] } | null | undefined)
    : undefined;
  const prefix = `${section}.`;
  const fields = new Map<string, Refused>();
  for (const one of problem?.errors ?? []) {
    if (!one.path) continue;
    const path = one.path.startsWith(prefix) ? one.path.slice(prefix.length) : one.path;
    if (!fields.has(path)) fields.set(path, { code: one.code ?? '', message: one.message ?? '' });
  }
  return fields;
}
