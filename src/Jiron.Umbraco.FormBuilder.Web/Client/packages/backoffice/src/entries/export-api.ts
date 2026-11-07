// What the Export sidebar and the background exports read and write (ROADMAP 11.4, ADR-0210): what the sidebar opens
// with, the plan, the file of an export that fits a download, and a background export with its status and file. Each
// answer is the server's, or undefined after a refusal; a refusal's code comes along where the sidebar says it.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  entryExportDownload,
  entryExportPlan,
  entryExportStart,
  entryExportStatus,
  entryExportView,
  entryExportWriteFile,
  type EntryExportPlanResponseModel,
  type EntryExportRequestModel,
  type EntryExportStatusResponseModel,
  type EntryExportViewResponseModel,
} from '../api';
import { codeOf } from '../library/library-changes';

const quiet = { disableNotifications: true } as const;

/** An answer, or the code of its refusal (none for a failure without one). */
export type Answered<T> = { readonly value: T } | { readonly code: string | undefined };

/** What the sidebar opens with: the formats, the user's last choices and their background exports of the form. */
export async function readExportView(
  host: UmbClassInterface,
  form: string,
): Promise<EntryExportViewResponseModel | undefined> {
  return (await tryExecute(host, entryExportView({ path: { key: form } }), quiet)).data;
}

/** How many entries an export holds, and whether it runs in the background. */
export async function planExport(
  host: UmbClassInterface,
  form: string,
  request: EntryExportRequestModel,
): Promise<Answered<EntryExportPlanResponseModel>> {
  const { data, error } = await tryExecute(host, entryExportPlan({ path: { key: form }, body: request }), quiet);
  return data ? { value: data } : { code: codeOf(error) };
}

/** The file of an export that fits a download. */
export async function writeExportFile(
  host: UmbClassInterface,
  form: string,
  request: EntryExportRequestModel,
): Promise<Answered<Blob>> {
  const { data, error } = await tryExecute(
    host,
    entryExportWriteFile({ path: { key: form }, body: request, parseAs: 'blob' }),
    quiet,
  );
  return data instanceof Blob ? { value: data } : { code: codeOf(error) };
}

/** Starts an export in the background. */
export async function startExport(
  host: UmbClassInterface,
  form: string,
  request: EntryExportRequestModel,
): Promise<Answered<EntryExportStatusResponseModel>> {
  const { data, error } = await tryExecute(host, entryExportStart({ path: { key: form }, body: request }), quiet);
  return data ? { value: data } : { code: codeOf(error) };
}

/**
 * A background export's status, read by the follower outside any element, so it calls the API without one: 'gone'
 * when the server no longer knows the export (it expired, or the user lost the form), undefined when it didn't answer.
 */
export async function readExportStatus(
  form: string,
  key: string,
): Promise<EntryExportStatusResponseModel | 'gone' | undefined> {
  try {
    const { data, response } = await entryExportStatus({ path: { key: form, exportKey: key } });
    if (data) return data;
    return response && (response.status === 404 || response.status === 403) ? 'gone' : undefined;
  } catch {
    return undefined;
  }
}

/** A background export's file; undefined when it is gone or the server didn't answer. */
export async function downloadExport(form: string, key: string): Promise<Blob | undefined> {
  try {
    const { data } = await entryExportDownload({ path: { key: form, exportKey: key }, parseAs: 'blob' });
    return data instanceof Blob ? data : undefined;
  } catch {
    return undefined;
  }
}
