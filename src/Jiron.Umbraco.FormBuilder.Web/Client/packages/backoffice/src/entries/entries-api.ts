// What a form's Entries view reads and writes (ROADMAP 11.1, ADR-0207): what it opens with, a page of entries, the
// user's columns and filter sets, a change of state and its Undo, and the bulk bar's delete and re-run, each asked first.
// Each answer is the server's, or undefined after a refusal; the view says what went wrong in its own words, as only it
// knows what the user was doing.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  entryFilterSetDelete,
  entryFilterSetSave,
  entryListColumns,
  entryListDelete,
  entryListList,
  entryListRunActions,
  entryListView,
  entryStateChange,
  entryStateUndo,
  type EntryDeleteResponseModel,
  type EntryFilterSetResponseModel,
  type EntryListFilterRequestModel,
  type EntryListPageResponseModel,
  type EntryListRequestModel,
  type EntryListViewResponseModel,
  type EntryRerunResponseModel,
  type EntryStateResponseModel,
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

/** Keeps the user's columns for the form; whether the server kept them. */
export async function saveColumns(host: UmbClassInterface, form: string, columns: readonly string[]): Promise<boolean> {
  return !(await tryExecute(host, entryListColumns({ path: { key: form }, body: { columns: [...columns] } }), quiet))
    .error;
}

/** Saves a filter set of the form: a new one, or the one with this key. */
export async function saveSet(
  host: UmbClassInterface,
  key: string,
  name: string,
  form: string,
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

/** Moves entries to a state, with the change's Undo window. */
export async function changeState(
  host: UmbClassInterface,
  form: string,
  entries: readonly string[],
  state: string,
): Promise<EntryStateResponseModel | undefined> {
  return (
    await tryExecute(host, entryStateChange({ path: { key: form }, body: { entries: [...entries], state } }), quiet)
  ).data;
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
