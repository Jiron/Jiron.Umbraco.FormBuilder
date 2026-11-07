// What a form's Entries view, the Inbox and Spam read and write (ROADMAP 11.1, ADR-0207, ADR-0209): what each opens
// with, a page of entries, the user's columns and filter sets, a change of state and its Undo, the bulk bar's delete and
// re-run, each asked first, the forms of the Form filter, and Empty spam. Each answer is the server's, or undefined after
// a refusal; the view says what went wrong in its own words, as only it knows what the user was doing. A change across
// forms goes through each form's own endpoints, so each is checked as on its own Entries view.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  entryDigestGet,
  entryDigestPreview,
  entryDigestSave,
  entryDigestTest,
  entryFilterSetDelete,
  entryFilterSetSave,
  entryInboxEmptySpam,
  entryInboxForms,
  entryInboxInbox,
  entryInboxSpam,
  entryInboxUnseen,
  entryInboxView,
  entryListColumns,
  entryListDelete,
  entryListList,
  entryListRunActions,
  entryListView,
  entryStateChange,
  entryStateUndo,
  type EmailPreviewResponseModel,
  type EntryDeleteResponseModel,
  type EntryDigestResponseModel,
  type EntryFilterSetResponseModel,
  type EntryInboxViewResponseModel,
  type EntryListFilterRequestModel,
  type EntryListFormResponseModel,
  type EntryListPageResponseModel,
  type EntryListRequestModel,
  type EntryListViewResponseModel,
  type EntryMoveAsksResponseModel,
  type EntryRerunResponseModel,
  type EntrySpamEmptyResponseModel,
  type EntryStateResponseModel,
  type EntryUnseenResponseModel,
} from '../api';
import { codeOf } from '../library/library-changes';

const quiet = { disableNotifications: true } as const;

/** What the view opens with: the form's fields, the user's columns and filter sets. */
export async function readView(host: UmbClassInterface, form: string): Promise<EntryListViewResponseModel | undefined> {
  return (await tryExecute(host, entryListView({ path: { key: form } }), quiet)).data;
}

/** A page of entries; undefined when it can't be read. */
export async function readPage(
  host: UmbClassInterface,
  form: string,
  request: EntryListRequestModel,
): Promise<EntryListPageResponseModel | undefined> {
  return (await tryExecute(host, entryListList({ path: { key: form }, body: request }), quiet)).data;
}

/** What the Inbox and Spam open with: the user's filter sets and the forms they name. */
export async function readInboxView(host: UmbClassInterface): Promise<EntryInboxViewResponseModel | undefined> {
  return (await tryExecute(host, entryInboxView(), quiet)).data;
}

/** A page of the Inbox or of Spam; undefined when it can't be read. */
export async function readAcross(
  host: UmbClassInterface,
  place: 'inbox' | 'spam',
  request: EntryListRequestModel,
): Promise<EntryListPageResponseModel | undefined> {
  const call = place === 'inbox' ? entryInboxInbox({ body: request }) : entryInboxSpam({ body: request });
  return (await tryExecute(host, call, quiet)).data;
}

/** The forms the user may view entries of whose name holds the text, for the Form filter; undefined on a failure. */
export async function findForms(
  host: UmbClassInterface,
  search: string,
): Promise<EntryListFormResponseModel[] | undefined> {
  const text = search.trim();
  return (await tryExecute(host, entryInboxForms({ query: text ? { search: text } : {} }), quiet)).data;
}

/** How many entries nobody opened yet the Inbox holds, up to 99, for its badge; undefined on a failure. */
export async function readUnseen(host: UmbClassInterface): Promise<EntryUnseenResponseModel | undefined> {
  return (await tryExecute(host, entryInboxUnseen(), quiet)).data;
}

/** How often the summary email goes out to the user. */
export type DigestFrequency = 'off' | 'daily' | 'weekly';

/** The user's choice of the summary email, with when and where it goes; undefined on a failure. */
export async function readDigest(host: UmbClassInterface): Promise<EntryDigestResponseModel | undefined> {
  return (await tryExecute(host, entryDigestGet(), quiet)).data;
}

/** Keeps how often the user gets the summary email; whether the server kept it. */
export async function saveDigest(host: UmbClassInterface, frequency: DigestFrequency): Promise<boolean> {
  return !(await tryExecute(host, entryDigestSave({ body: { frequency } }), quiet)).error;
}

/** The summary email as it would go out now, daily or weekly; undefined on a failure. */
export async function previewDigest(
  host: UmbClassInterface,
  frequency: 'daily' | 'weekly',
): Promise<EmailPreviewResponseModel | undefined> {
  return (await tryExecute(host, entryDigestPreview({ query: { frequency } }), quiet)).data;
}

/** Sends the summary email now, marked as a test; the address it went to, or undefined on a failure. */
export async function testDigest(host: UmbClassInterface, frequency: 'daily' | 'weekly'): Promise<string | undefined> {
  return (await tryExecute(host, entryDigestTest({ body: { frequency } }), quiet)).data?.to;
}

/** What emptying Spam deletes, without <code>confirm</code>, or what it deleted. */
export async function emptySpam(
  host: UmbClassInterface,
  confirm: boolean,
): Promise<EntrySpamEmptyResponseModel | undefined> {
  return (await tryExecute(host, entryInboxEmptySpam({ body: { confirm } }), quiet)).data;
}

/** Keeps the user's columns for the form; whether the server kept them. */
export async function saveColumns(host: UmbClassInterface, form: string, columns: readonly string[]): Promise<boolean> {
  return !(await tryExecute(host, entryListColumns({ path: { key: form }, body: { columns: [...columns] } }), quiet))
    .error;
}

/** Saves a filter set, of a form or of every form (null): a new one, or the one with this key. */
export async function saveSet(
  host: UmbClassInterface,
  key: string,
  name: string,
  form: string | null,
  filter: EntryListFilterRequestModel,
): Promise<{ set?: EntryFilterSetResponseModel; code?: string }> {
  const { data, error } = await tryExecute(
    host,
    entryFilterSetSave({ path: { setKey: key }, body: { name, form, filter } }),
    quiet,
  );
  return data ? { set: data } : { code: codeOf(error) ?? 'failed' };
}

/** Deletes a filter set; whether the server answered. */
export async function deleteSet(host: UmbClassInterface, key: string): Promise<boolean> {
  return !(await tryExecute(host, entryFilterSetDelete({ path: { setKey: key } }), quiet)).error;
}

/**
 * Moves entries to a state, with the change's Undo window; a move back that would overbook or double a unique answer
 * moves nothing and says so in `asks`, until `overbook` agrees to it (ADR-0208).
 */
export async function changeState(
  host: UmbClassInterface,
  form: string,
  entries: readonly string[],
  state: string,
  overbook = false,
): Promise<EntryStateResponseModel | undefined> {
  return (
    await tryExecute(
      host,
      entryStateChange({ path: { key: form }, body: { entries: [...entries], state, overbook, check: false } }),
      quiet,
    )
  ).data;
}

/**
 * What moving entries to a state would overbook or double, read without moving anything, so a move across forms asks
 * once before any form moves (ADR-0209); undefined when it asks nothing or can't be read.
 */
export async function checkMove(
  host: UmbClassInterface,
  form: string,
  entries: readonly string[],
  state: string,
): Promise<EntryMoveAsksResponseModel | undefined> {
  const { data } = await tryExecute(
    host,
    entryStateChange({ path: { key: form }, body: { entries: [...entries], state, overbook: false, check: true } }),
    quiet,
  );
  return data?.asks ?? undefined;
}

/** Undoes a change: done, too late, or failed. */
export async function undoChange(
  host: UmbClassInterface,
  form: string,
  change: string,
): Promise<'done' | 'tooLate' | 'failed'> {
  const { error } = await tryExecute(host, entryStateUndo({ path: { key: form }, body: { changeKey: change } }), quiet);
  if (!error) return 'done';
  return codeOf(error) === 'entry_undo_too_late' ? 'tooLate' : 'failed';
}

/** What a delete takes, without <code>confirm</code>, or what it took. */
export async function deleteEntries(
  host: UmbClassInterface,
  form: string,
  entries: readonly string[],
  confirm: boolean,
): Promise<EntryDeleteResponseModel | undefined> {
  return (
    await tryExecute(host, entryListDelete({ path: { key: form }, body: { entries: [...entries], confirm } }), quiet)
  ).data;
}

/** What a re-run of actions runs, without <code>confirm</code>, or what it queued. */
export async function rerunActions(
  host: UmbClassInterface,
  form: string,
  entries: readonly string[],
  confirm: boolean,
): Promise<EntryRerunResponseModel | undefined> {
  return (
    await tryExecute(
      host,
      entryListRunActions({ path: { key: form }, body: { entries: [...entries], confirm } }),
      quiet,
    )
  ).data;
}
