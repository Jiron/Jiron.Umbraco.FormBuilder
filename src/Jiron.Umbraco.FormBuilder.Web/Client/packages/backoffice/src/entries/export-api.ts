// What the Export sidebar and the background exports read and write (ROADMAP 11.4, ADR-0210): what the sidebar opens
// with, the plan, the file of an export that fits a download, and a background export with its status and the link to
// its file. Each answer is the server's, or undefined after a refusal; a refusal's code comes along where the sidebar
// says it.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  entryExportPlan,
  entryExportStart,
  entryExportStatus,
  entryExportTicket,
  entryExportView,
  entryExportWriteFile,
  type EntryExportPlanResponseModel,
  type EntryExportRequestModel,
  type EntryExportStatusResponseModel,
  type EntryExportViewResponseModel,
} from '../api';
import { client } from '../api/client.gen';
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

/**
 * Downloads a background export's file through a link with a one-minute ticket, which the browser saves as it comes,
 * whatever its size, rather than in the page's memory (ADR-0210): a link's request carries no backoffice token. False
 * when the export is gone, its owner may no longer download it, or the server didn't answer.
 */
export async function downloadExport(
  form: string,
  key: string,
  follow: (url: string) => void = followLink,
): Promise<boolean> {
  try {
    const { data } = await entryExportTicket({ path: { key: form, exportKey: key } });
    if (!data) return false;
    follow(new URL(data.url, client.getConfig().baseUrl || window.location.origin).href);
    return true;
  } catch {
    return false;
  }
}

/**
 * Follows a link that saves a file, which the server names as an attachment. The link stays off the page: an `a` element
 * follows its link without it, and its click then never reaches the backoffice's router, which would take the address
 * for one of its own routes.
 */
export function followLink(url: string): void {
  const link = document.createElement('a');
  link.href = url;
  link.download = '';
  link.rel = 'noopener';
  link.click();
}
