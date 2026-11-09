// Privacy > People's calls (ROADMAP 13.1 part 13.2, ADR-0223): every request names the person again, an address or a
// member, and the server finds their entries again; the export is followed like a form's background export, by its own
// status and its own one-minute download link.
import { client } from '../api/client.gen';
import { peopleStatus, peopleTicket, type EntryExportStatusResponseModel, type PersonRequestModel } from '../api';
import { ExportFollower } from '../entries/export-follow';
import { followLink } from '../entries/export-api';
import { idleGate } from '../ui/idle-gate';

/** Whom the screen looks for: an address, or a member with the name the picker gave. */
export type Person = { readonly email: string } | { readonly member: string; readonly name: string };

/** The person as every request names them. */
export const bodyOf = (person: Person): PersonRequestModel =>
  'email' in person ? { email: person.email } : { member: person.member };

/**
 * A person's export's status, read by the follower outside any element: 'gone' when the server no longer knows it,
 * undefined when it didn't answer.
 */
export async function readPersonExport(key: string): Promise<EntryExportStatusResponseModel | 'gone' | undefined> {
  try {
    const { data, response } = await peopleStatus({ path: { exportKey: key } });
    if (data) return data;
    return response && (response.status === 404 || response.status === 403) ? 'gone' : undefined;
  } catch {
    return undefined;
  }
}

/** Saves a person's ready export through its one-minute link; false when it is gone or the server didn't answer. */
export async function downloadPersonExport(
  _form: string,
  key: string,
  follow: (url: string) => void = followLink,
): Promise<boolean> {
  try {
    const { data } = await peopleTicket({ path: { exportKey: key } });
    if (!data) return false;
    follow(new URL(data.url, client.getConfig().baseUrl || window.location.origin).href);
    return true;
  } catch {
    return false;
  }
}

/** The one follower of people's exports on this page. */
export const personExportFollower = new ExportFollower((_form, key) => readPersonExport(key), idleGate);
