// What the option list screens ask the management API (ROADMAP 12.1, ADR-0214): the lists, one list, its changes, the
// forms that use it, the preview of contents not saved yet, a file read into options and a URL list's copy. Each answer
// comes back as what the screens say: a refusal by its code, the problems a save answered with by their paths.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute, UmbApiError } from '@umbraco-cms/backoffice/resources';
import {
  optionListLibraryCreate,
  optionListLibraryDelete,
  optionListLibraryDuplicate,
  optionListLibraryEditor,
  optionListLibraryFetch,
  optionListLibraryFetched,
  optionListLibraryGet,
  optionListLibraryPreview,
  optionListLibraryReadFile,
  optionListLibraryRows,
  optionListLibraryUpdate,
  optionListLibraryUsedBy,
  type OptionListEditorResponseModel,
  type OptionListFetchResponseModel,
  type OptionListOptionResponseModel,
  type OptionListResponseModel,
  type OptionListRowsResponseModel,
  type LibraryUsersResponseModel,
} from '../api';
import type { DesignOption } from '../designer/inspector/option-values';
import { codeOf } from '../library/library-changes';
import { isKind, type ListProblem, type OptionListDetailModel } from './option-list-model';
import { OPTION_LIST_ENTITY_TYPE } from './constants';

/** The workspace's model of a list as the API answers with it; JSON that is not of its shape reads as none. */
export function detailOf(list: OptionListResponseModel): OptionListDetailModel {
  const config =
    list.config !== null && typeof list.config === 'object' && !Array.isArray(list.config) ? list.config : {};
  return {
    entityType: OPTION_LIST_ENTITY_TYPE,
    unique: list.key,
    name: list.name,
    alias: list.alias,
    kind: isKind(list.kind) ? list.kind : null,
    config: config as Record<string, unknown>,
    items: Array.isArray(list.items) ? (list.items as DesignOption[]) : [],
    cacheSeconds: list.cacheSeconds,
    concurrencyVersion: list.concurrencyVersion,
  };
}

/** A list's values as a save sends them. */
const valuesOf = (model: OptionListDetailModel) => ({
  alias: model.alias,
  name: model.name.trim(),
  kind: model.kind ?? 'manual',
  config: model.config,
  items: model.items,
  cacheSeconds: model.cacheSeconds,
});

/** The problems a refusal holds, each at its path. */
export function problemsOf(error: unknown): ListProblem[] {
  const problem = UmbApiError.isUmbApiError(error)
    ? (error.problemDetails as unknown as { errors?: readonly Partial<ListProblem>[] } | null | undefined)
    : undefined;
  return (problem?.errors ?? []).flatMap((one) =>
    one.path ? [{ code: one.code ?? '', path: one.path, message: one.message ?? '' }] : [],
  );
}

/** A page of the lists; 'forbidden' for a user without the library, undefined without an answer. */
export async function readRows(
  host: UmbClassInterface,
  skip: number,
  take: number,
): Promise<OptionListRowsResponseModel | 'forbidden' | undefined> {
  const { data, error } = await tryExecute(host, optionListLibraryRows({ query: { skip, take } }), {
    disableNotifications: true,
  });
  if (data) return data;
  return codeOf(error) === 'forbidden' ? 'forbidden' : undefined;
}

let editor: Promise<OptionListEditorResponseModel | undefined> | undefined;

/** The site's languages and what each kind keeps, read once while the backoffice is open; again after a failed read. */
export function readEditor(host: UmbClassInterface): Promise<OptionListEditorResponseModel | undefined> {
  editor ??= tryExecute(host, optionListLibraryEditor(), { disableNotifications: true }).then(({ data }) => {
    if (!data) editor = undefined;
    return data;
  });
  return editor;
}

/** A list, or the refusal of its read. */
export async function readList(
  host: UmbClassInterface,
  key: string,
): Promise<{ readonly data?: OptionListDetailModel; readonly error?: unknown }> {
  const { data, error } = await tryExecute(host, optionListLibraryGet({ path: { key } }), {
    disableNotifications: true,
  });
  return data ? { data: detailOf(data) } : { error };
}

/** How a save ended: saved, refused with problems at their paths, saved by someone else meanwhile, or refused otherwise. */
export type SaveAnswer =
  | { readonly kind: 'saved'; readonly list: OptionListDetailModel }
  | { readonly kind: 'invalid'; readonly problems: readonly ListProblem[] }
  | { readonly kind: 'conflict' | 'failed'; readonly error: unknown };

function answerOf(data: OptionListResponseModel | undefined, error: unknown): SaveAnswer {
  if (data) return { kind: 'saved', list: detailOf(data) };
  const problems = problemsOf(error);
  if (problems.length > 0) return { kind: 'invalid', problems };
  if (codeOf(error) === 'concurrency_conflict') return { kind: 'conflict', error };
  return { kind: 'failed', error };
}

/** Creates a list under its key. */
export async function createList(host: UmbClassInterface, model: OptionListDetailModel): Promise<SaveAnswer> {
  const { data, error } = await tryExecute(
    host,
    optionListLibraryCreate({ body: { key: model.unique, list: valuesOf(model) } }),
    { disableNotifications: true },
  );
  return answerOf(data, error);
}

/** Saves a list, based on the version read. */
export async function updateList(host: UmbClassInterface, model: OptionListDetailModel): Promise<SaveAnswer> {
  const { data, error } = await tryExecute(
    host,
    optionListLibraryUpdate({
      path: { key: model.unique },
      body: { concurrencyVersion: model.concurrencyVersion, list: valuesOf(model) },
    }),
    { disableNotifications: true },
  );
  return answerOf(data, error);
}

/** How a delete ended. */
export type DeleteAnswer = 'deleted' | 'inUse' | 'conflict' | 'gone' | 'failed';

/** Deletes a list, based on the version read. */
export async function deleteList(
  host: UmbClassInterface,
  key: string,
  concurrencyVersion: number,
): Promise<DeleteAnswer> {
  const { error } = await tryExecute(host, optionListLibraryDelete({ path: { key }, query: { concurrencyVersion } }), {
    disableNotifications: true,
  });
  if (!error) return 'deleted';
  switch (codeOf(error)) {
    case 'in_use':
      return 'inUse';
    case 'concurrency_conflict':
      return 'conflict';
    case 'option_list_not_found':
      return 'gone';
    default:
      return 'failed';
  }
}

/** A copy of a list with the name given; undefined when it was refused. */
export async function duplicateList(
  host: UmbClassInterface,
  key: string,
  name: string,
): Promise<OptionListDetailModel | undefined> {
  const { data } = await tryExecute(host, optionListLibraryDuplicate({ path: { key }, body: { name } }), {
    disableNotifications: true,
  });
  return data ? detailOf(data) : undefined;
}

/** The forms that use a list, as the user may see them; undefined without an answer. */
export async function readUsers(host: UmbClassInterface, key: string): Promise<LibraryUsersResponseModel | undefined> {
  const { data } = await tryExecute(host, optionListLibraryUsedBy({ path: { key } }), { disableNotifications: true });
  return data;
}

/** A preview: the options, whether the source gave them, and the problems that refuse the contents. */
export interface PreviewAnswer {
  readonly options: readonly OptionListOptionResponseModel[];
  readonly isAvailable: boolean;
  readonly problems: readonly ListProblem[];
}

/** The options of contents not saved yet, in a culture; undefined without an answer. */
export async function previewList(
  host: UmbClassInterface,
  model: OptionListDetailModel,
  culture: string,
  signal: AbortSignal,
): Promise<PreviewAnswer | undefined> {
  const { data } = await tryExecute(
    host,
    optionListLibraryPreview({
      body: {
        key: model.unique,
        kind: model.kind ?? 'manual',
        config: model.config,
        items: model.items,
        cacheSeconds: model.cacheSeconds,
        culture,
      },
      signal,
    }),
    { disableNotifications: true },
  );
  return data;
}

/** A file read into options, the problems that refuse it, or no answer. */
export type FileAnswer =
  | { readonly kind: 'read'; readonly items: readonly DesignOption[] }
  | { readonly kind: 'invalid'; readonly problems: readonly ListProblem[] }
  | { readonly kind: 'failed' };

/** A file's bytes as Base64, as JSON carries them. */
async function base64Of(file: Blob): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let text = '';
  // In parts, as spreading a whole file into one call overflows the stack.
  for (let at = 0; at < bytes.length; at += 0x8000) text += String.fromCharCode(...bytes.subarray(at, at + 0x8000));
  return btoa(text);
}

/** Reads a CSV or TXT file into options. */
export async function readFile(host: UmbClassInterface, file: File): Promise<FileAnswer> {
  const { data, error } = await tryExecute(
    host,
    optionListLibraryReadFile({ body: { fileName: file.name, content: await base64Of(file) } }),
    { disableNotifications: true },
  );
  if (data) return { kind: 'read', items: Array.isArray(data.items) ? (data.items as DesignOption[]) : [] };
  const problems = problemsOf(error);
  return problems.length > 0 ? { kind: 'invalid', problems } : { kind: 'failed' };
}

/** What this server keeps of a saved URL list's address; undefined without an answer. */
export async function readFetched(
  host: UmbClassInterface,
  key: string,
): Promise<OptionListFetchResponseModel | undefined> {
  const { data } = await tryExecute(host, optionListLibraryFetched({ path: { key } }), { disableNotifications: true });
  return data;
}

/** Fetch now; what this server keeps then, or undefined without an answer. */
export async function fetchNow(
  host: UmbClassInterface,
  key: string,
): Promise<OptionListFetchResponseModel | undefined> {
  const { data } = await tryExecute(host, optionListLibraryFetch({ path: { key } }), { disableNotifications: true });
  return data;
}
