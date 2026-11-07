// A background export followed until its file is ready (ROADMAP 11.4, IA 6.4, ADR-0210, docs/ux/copy.md): a toast says
// that it is being prepared and how far it is, and once it is ready another links to the file. The follower lives
// outside the screen that started it, so the user keeps working anywhere in the backoffice; every read waits at the idle
// gate first, so following an export never keeps an idle person signed in (section 4.10). A page that loads again finds
// the export in the sidebar, which lists the user's exports of the form.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import {
  UMB_NOTIFICATION_CONTEXT,
  type UmbNotificationHandler,
  type UmbNotificationOptions,
} from '@umbraco-cms/backoffice/notification';
import type { EntryExportStatusResponseModel } from '../api';
import { saveBlob } from '../library/actions/export-form.action';
import { ACTION_NOTIFICATION, type ActionNotificationData, type Said } from '../ui/action-notification.element';
import { idleGate } from '../ui/idle-gate';
import { downloadExport, readExportStatus } from './export-api';
import { isFinished } from './export-model';
import type { PollGate } from './unseen-poll';

/** How often a background export is read while the tab is on screen, and while it is not. */
export const FOLLOW_MS = 2_000;
export const FOLLOW_HIDDEN_MS = 10_000;

/** After this many reads in a row without an answer, the follower stops; the sidebar still lists the export. */
export const MOST_SILENT_READS = 30;

/** What the follower tells: each status read, and its end. */
export interface FollowListener {
  /** A status read while the export is under way. */
  progress(status: EntryExportStatusResponseModel): void;
  /** The export is ready or failed. */
  finished(status: EntryExportStatusResponseModel): void;
  /** The server no longer knows the export, or never answered again. */
  lost(): void;
}

type Read = (form: string, key: string) => Promise<EntryExportStatusResponseModel | 'gone' | undefined>;

/** Waits a time, or less once the signal is aborted. */
const pause = (ms: number, signal: AbortSignal): Promise<void> =>
  new Promise((resolve) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener('abort', () => (clearTimeout(timer), resolve()), { once: true });
  });

/**
 * Follows background exports, each once: it reads an export's status at the gate, a little while apart, until it is
 * ready or failed, and tells its listener. A tab out of sight reads less often.
 */
export class ExportFollower {
  readonly #read: Read;
  readonly #gate: PollGate;
  readonly #wait: (ms: number, signal: AbortSignal) => Promise<void>;
  readonly #page: Document | undefined;
  readonly #following = new Map<string, AbortController>();

  constructor(
    read: Read,
    gate: PollGate,
    wait: (ms: number, signal: AbortSignal) => Promise<void> = pause,
    page: Document | undefined = typeof document === 'undefined' ? undefined : document,
  ) {
    this.#read = read;
    this.#gate = gate;
    this.#wait = wait;
    this.#page = page;
  }

  /** Whether an export is followed now. */
  follows(key: string): boolean {
    return this.#following.has(key);
  }

  /** Follows an export from the status it has; one already followed is left as it is. Gives what stops it. */
  follow(form: string, status: EntryExportStatusResponseModel, listener: FollowListener): () => void {
    const stop = () => this.#following.get(status.key)?.abort();
    if (this.#following.has(status.key)) return stop;
    if (isFinished(status)) {
      listener.finished(status);
      return stop;
    }

    const abort = new AbortController();
    this.#following.set(status.key, abort);
    void this.#loop(form, status.key, listener, abort.signal).finally(() => {
      if (this.#following.get(status.key) === abort) this.#following.delete(status.key);
    });
    return stop;
  }

  async #loop(form: string, key: string, listener: FollowListener, signal: AbortSignal) {
    let silent = 0;
    while (!signal.aborted) {
      await this.#wait(this.#page?.hidden ? FOLLOW_HIDDEN_MS : FOLLOW_MS, signal);
      if (signal.aborted) return;
      await this.#gate.untilActive(signal);
      if (signal.aborted) return;
      const status = await this.#read(form, key);
      if (signal.aborted) return;
      if (status === 'gone') {
        listener.lost();
        return;
      }

      if (status === undefined) {
        silent++;
        if (silent >= MOST_SILENT_READS) {
          listener.lost();
          return;
        }

        continue;
      }

      silent = 0;
      if (isFinished(status)) {
        listener.finished(status);
        return;
      }

      listener.progress(status);
    }
  }
}

/** The one follower of this page. */
export const exportFollower = new ExportFollower(readExportStatus, idleGate);

const said = (key: string, ...args: unknown[]): Said => ({ key: `jfbEntries_${key}`, args });

/** What a background export's toast says while it is under way. */
export function progressSaid(status: EntryExportStatusResponseModel): Said {
  return status.done > 0
    ? said('export_progressToast', status.done, status.total, status.more)
    : said('export_preparingToast', status.total, status.more);
}

/** A toast of the section's own element, with its data. */
type ActionOptions = UmbNotificationOptions<ActionNotificationData>;

/** The toasts Umbraco shows, from its notification context, which outlives every screen. */
export type Notices = Pick<typeof UMB_NOTIFICATION_CONTEXT.TYPE, 'peek' | 'stay'>;

/**
 * Shows a background export's progress and then its file through Umbraco's toasts, which outlive the screen that
 * started it: the host gives the notification context, once, before it goes.
 */
export async function followExport(
  host: UmbClassInterface,
  form: string,
  status: EntryExportStatusResponseModel,
  follower: ExportFollower = exportFollower,
): Promise<void> {
  const notices = await host.getContext(UMB_NOTIFICATION_CONTEXT).catch(() => undefined);
  if (notices) showFollowed(notices, form, status, follower);
}

/** Follows an export, a toast saying how far it is until a second one says it is ready, or failed. */
export function showFollowed(
  notices: Notices,
  form: string,
  status: EntryExportStatusResponseModel,
  follower: ExportFollower = exportFollower,
): void {
  const listeners = new Set<(said: Said) => void>();
  let progress: UmbNotificationHandler | undefined;
  if (!isFinished(status) && !follower.follows(status.key)) {
    const data: ActionNotificationData = {
      message: '',
      said: progressSaid(status),
      watch: (show) => {
        listeners.add(show);
        return () => listeners.delete(show);
      },
    };
    progress = notices.stay<ActionOptions>('default', { elementName: ACTION_NOTIFICATION, data });
  }

  follower.follow(form, status, {
    progress: (now) => {
      for (const show of listeners) show(progressSaid(now));
    },
    finished: (done) => {
      progress?.close();
      if (done.state === 'failed') {
        const data: ActionNotificationData = { message: '', said: said('export_failedToast', done.total, done.more) };
        notices.stay<ActionOptions>('danger', { elementName: ACTION_NOTIFICATION, data });
        return;
      }

      notices.stay<ActionOptions>('positive', {
        elementName: ACTION_NOTIFICATION,
        data: readyToast(notices, form, done),
      });
    },
    lost: () => progress?.close(),
  });
}

/** The toast of a ready export: what it holds, and Download. */
export function readyToast(
  notices: Notices,
  form: string,
  done: EntryExportStatusResponseModel,
): ActionNotificationData {
  return {
    message: '',
    said: said('export_readyToast', done.done),
    positive: true,
    action: { label: said('export_download'), run: async () => void (await saveExport(notices, form, done)) },
  };
}

/** Saves a ready export's file; a file that expired, or did not come, says so in a toast. */
export async function saveExport(
  notices: Notices,
  form: string,
  done: EntryExportStatusResponseModel,
  download: typeof downloadExport = downloadExport,
): Promise<boolean> {
  const blob = await download(form, done.key);
  if (blob) {
    saveBlob(done.fileName ?? 'export', blob);
    return true;
  }

  const data: ActionNotificationData = { message: '', said: said('export_downloadFailed') };
  notices.peek<ActionOptions>('danger', { elementName: ACTION_NOTIFICATION, data });
  return false;
}
