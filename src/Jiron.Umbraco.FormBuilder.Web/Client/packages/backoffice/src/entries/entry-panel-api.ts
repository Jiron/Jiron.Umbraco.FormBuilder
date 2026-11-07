// What the entry panel reads and writes (ROADMAP 11.2, ADR-0208): an entry, a revealed answer, its timeline and notes,
// an edit of its answers, its forward, a run's retry, a file's bytes and the form of an entry's own link. Each answer is
// the server's, or says why it was refused; the panel says what went wrong in its own words.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbApiError, tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  entryActionRunRetry,
  entryFileDownload,
  entryLocationForm,
  entryPanelAddNote,
  entryPanelChangeNote,
  entryPanelEdit,
  entryPanelForward,
  entryPanelNotes,
  entryPanelOpen,
  entryPanelRecipients,
  entryPanelReveal,
  entryPanelTimeline,
  type EntryEditAnswerRequestModel,
  type EntryEditResponseModel,
  type EntryForwardRecipientsResponseModel,
  type EntryForwardRequestModel,
  type EntryNoteResponseModel,
  type EntryPanelAnswerResponseModel,
  type EntryPanelResponseModel,
  type EntryTimelineResponseModel,
} from '../api';
import { codeOf } from '../library/library-changes';

const quiet = { disableNotifications: true } as const;

/** A refusal's problems, each at its path in the request. */
export interface Problem {
  readonly code: string;
  readonly path: string;
}

/** The problems a refusal names; none for one that names none. */
export function problemsOf(error: unknown): Problem[] {
  if (!error || !UmbApiError.isUmbApiError(error)) return [];
  const errors = (error.problemDetails as { errors?: unknown } | null | undefined)?.errors;
  return Array.isArray(errors)
    ? errors
        .filter((item): item is { code: string; path: string } => typeof item?.code === 'string')
        .map((item) => ({ code: item.code, path: typeof item.path === 'string' ? item.path : '' }))
    : [];
}

/** Opens an entry, which marks it seen; undefined when it is gone or can't be read. */
export async function openEntry(
  host: UmbClassInterface,
  form: string,
  entry: string,
): Promise<EntryPanelResponseModel | undefined> {
  return (await tryExecute(host, entryPanelOpen({ path: { key: form, entryKey: entry } }), quiet)).data;
}

/** One answer unmasked; undefined when it can't be read. */
export async function revealAnswer(
  host: UmbClassInterface,
  form: string,
  entry: string,
  field: string,
): Promise<EntryPanelAnswerResponseModel | undefined> {
  return (await tryExecute(host, entryPanelReveal({ path: { key: form, entryKey: entry }, body: { field } }), quiet))
    .data;
}

/** The entry's events, runs and mail. */
export async function readTimeline(
  host: UmbClassInterface,
  form: string,
  entry: string,
): Promise<EntryTimelineResponseModel | undefined> {
  return (await tryExecute(host, entryPanelTimeline({ path: { key: form, entryKey: entry } }), quiet)).data;
}

/** The entry's notes, newest first. */
export async function readNotes(
  host: UmbClassInterface,
  form: string,
  entry: string,
): Promise<EntryNoteResponseModel[] | undefined> {
  return (await tryExecute(host, entryPanelNotes({ path: { key: form, entryKey: entry } }), quiet)).data;
}

/** Adds a note; undefined when it was refused. */
export async function addNote(
  host: UmbClassInterface,
  form: string,
  entry: string,
  body: string,
): Promise<EntryNoteResponseModel | undefined> {
  return (await tryExecute(host, entryPanelAddNote({ path: { key: form, entryKey: entry }, body: { body } }), quiet))
    .data;
}

/** Changes a note of the user's; undefined when it was refused. */
export async function changeNote(
  host: UmbClassInterface,
  form: string,
  entry: string,
  note: number,
  body: string,
): Promise<EntryNoteResponseModel | undefined> {
  return (
    await tryExecute(
      host,
      entryPanelChangeNote({ path: { key: form, entryKey: entry, noteId: note }, body: { body } }),
      quiet,
    )
  ).data;
}

/** Edits answers: what it did or asks first, or the problems that refused it. */
export async function editAnswers(
  host: UmbClassInterface,
  form: string,
  entry: string,
  answers: EntryEditAnswerRequestModel[],
  overbook: boolean,
): Promise<{ edited?: EntryEditResponseModel; problems: Problem[] }> {
  const { data, error } = await tryExecute(
    host,
    entryPanelEdit({ path: { key: form, entryKey: entry }, body: { answers, overbook } }),
    quiet,
  );
  return data ? { edited: data, problems: [] } : { problems: problemsOf(error) };
}

/** Whom the entry may be forwarded to. */
export async function readRecipients(
  host: UmbClassInterface,
  form: string,
  entry: string,
): Promise<EntryForwardRecipientsResponseModel | undefined> {
  return (await tryExecute(host, entryPanelRecipients({ path: { key: form, entryKey: entry } }), quiet)).data;
}

/** Forwards the entry; whether it was queued. */
export async function forwardEntry(
  host: UmbClassInterface,
  form: string,
  entry: string,
  request: EntryForwardRequestModel,
): Promise<boolean> {
  return !(await tryExecute(host, entryPanelForward({ path: { key: form, entryKey: entry }, body: request }), quiet))
    .error;
}

/** Retries a run: queued, asked to confirm (an uncertain run), or failed. */
export async function retryRun(
  host: UmbClassInterface,
  form: string,
  entry: string,
  run: number,
  confirmed: boolean,
): Promise<'queued' | 'confirm' | 'failed'> {
  const { error } = await tryExecute(
    host,
    entryActionRunRetry({ path: { key: form, entryKey: entry, runId: run }, body: { confirmed } }),
    quiet,
  );
  if (!error) return 'queued';
  return codeOf(error) === 'retry_needs_confirmation' ? 'confirm' : 'failed';
}

/** A file's bytes, through the authorised download, which a plain link could not reach with the backoffice's token. */
export async function fileBlob(host: UmbClassInterface, form: string, file: string): Promise<Blob | undefined> {
  const { data } = await tryExecute(
    host,
    entryFileDownload({ path: { key: form, fileKey: file }, parseAs: 'blob' }),
    quiet,
  );
  return data instanceof Blob ? data : undefined;
}

/** The form of an entry's own link: none for an entry that is gone or that the user may not view. */
export async function locateEntry(host: UmbClassInterface, entry: string): Promise<string | null | undefined> {
  const { data } = await tryExecute(host, entryLocationForm({ path: { entryKey: entry } }), quiet);
  return data ? (data.form ?? null) : undefined;
}
