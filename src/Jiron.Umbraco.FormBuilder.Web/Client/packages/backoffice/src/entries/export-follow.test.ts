// A background export followed to its file (ROADMAP 11.4, ADR-0210): each read waits at the idle gate, a little while
// apart, until the export is ready or failed; an export the server no longer knows, or that never answers again, ends
// the following; each export is followed once. Its toasts: one that says how far it is, then one with Download.
import { describe, expect, it, vi } from 'vitest';
import type { EntryExportStatusResponseModel } from '../api';
import type { ActionNotificationData } from '../ui/action-notification.element';
import {
  ExportFollower,
  FOLLOW_HIDDEN_MS,
  FOLLOW_MS,
  MOST_SILENT_READS,
  saveExport,
  showFollowed,
  type FollowListener,
} from './export-follow';

const status = (
  state: string,
  done = 0,
  extra: Partial<EntryExportStatusResponseModel> = {},
): EntryExportStatusResponseModel => ({
  key: 'k1',
  state,
  total: 12400,
  more: false,
  done,
  fileName: 'contact-entries.csv',
  ...extra,
});

/** An item of a list a test expects to hold it. */
function at<T>(list: readonly T[], index: number): T {
  const item = list[index];
  if (item === undefined) throw new Error(`The list holds no item ${index}.`);
  return item;
}

/** A gate that counts its waits, a wait that never waits, and the reads the server answers in turn. */
function follower(answers: (EntryExportStatusResponseModel | 'gone' | undefined)[]) {
  const gate = { waits: 0, untilActive: async () => void gate.waits++ };
  const read = vi.fn(async () => answers.shift());
  return { gate, read, follower: new ExportFollower(read, gate, async () => undefined, undefined) };
}

/** What a listener hears, and a promise of the end it hears. */
function listener() {
  const heard: string[] = [];
  let end: () => void = () => undefined;
  const done = new Promise<void>((resolve) => (end = resolve));
  const listen: FollowListener = {
    progress: (now) => heard.push(`progress ${now.done}`),
    finished: (now) => {
      heard.push(`finished ${now.state}`);
      end();
    },
    lost: (gone) => {
      heard.push(gone ? 'gone' : 'lost');
      end();
    },
  };
  return { heard, listen, done };
}

describe('the follower', () => {
  it('reads at the gate until the export is ready, telling each step', async () => {
    const {
      gate,
      read,
      follower: one,
    } = follower([status('running', 250), status('running', 5000), status('ready', 12400)]);
    const { heard, listen, done } = listener();

    one.follow('f1', status('queued'), listen);
    expect(one.follows('k1')).toBe(true);
    await done;

    expect(heard).toEqual(['progress 250', 'progress 5000', 'finished ready']);
    expect(read).toHaveBeenCalledWith('f1', 'k1');
    expect(gate.waits, 'every read waits at the idle gate').toBe(3);
    await vi.waitFor(() => expect(one.follows('k1')).toBe(false));
  });

  it('ends with the export the server no longer knows, or one that stops answering', async () => {
    const gone = follower([undefined, 'gone']);
    const first = listener();
    gone.follower.follow('f1', status('running', 10), first.listen);
    await first.done;
    expect(first.heard).toEqual(['gone']);

    const silent = follower(Array.from({ length: MOST_SILENT_READS + 5 }, () => undefined));
    const second = listener();
    silent.follower.follow('f1', status('running', 10), second.listen);
    await second.done;
    expect(second.heard).toEqual(['lost']);
    expect(silent.read).toHaveBeenCalledTimes(MOST_SILENT_READS);
  });

  it('reads less often while the tab is out of sight', async () => {
    const page = { hidden: true };
    const waits: number[] = [];
    const answers = [status('running', 10), status('ready', 12400)];
    const one = new ExportFollower(
      async () => {
        page.hidden = false;
        return answers.shift();
      },
      { untilActive: async () => undefined },
      async (ms) => void waits.push(ms),
      page as Document,
    );
    const { done, listen } = listener();

    one.follow('f1', status('queued'), listen);
    await done;

    expect(waits, 'hidden, then on screen again').toEqual([FOLLOW_HIDDEN_MS, FOLLOW_MS]);
    expect(FOLLOW_HIDDEN_MS).toBeGreaterThan(FOLLOW_MS);
  });

  it('follows each export once, and tells a finished one at once', async () => {
    const { read, follower: one } = follower([status('ready', 12400)]);
    const first = listener();
    one.follow('f1', status('running', 1), first.listen);
    const again = listener();
    one.follow('f1', status('running', 1), again.listen);
    await first.done;
    expect(again.heard).toEqual([]);
    expect(read).toHaveBeenCalledTimes(1);

    const finished = listener();
    one.follow('f1', status('failed', 0, { key: 'k2' }), finished.listen);
    expect(finished.heard).toEqual(['finished failed']);
  });
});

describe("a background export's toasts", () => {
  function notices() {
    const closed = vi.fn();
    const shown: { kind: string; color: string; data: ActionNotificationData }[] = [];
    const show = (kind: string) => (color: string, options: { data?: ActionNotificationData }) => {
      shown.push({ kind, color, data: options.data as ActionNotificationData });
      return { close: closed } as never;
    };
    return { closed, shown, notices: { stay: show('stay'), peek: show('peek') } as never };
  }

  it('say how far it is, then stay with Download once it is ready', async () => {
    const { follower: one } = follower([status('running', 2500), status('ready', 12400)]);
    const { closed, shown, notices: toasts } = notices();
    const said: string[] = [];

    showFollowed(toasts, 'f1', status('queued'), one);
    at(shown, 0).data.watch?.((now) => said.push(`${now.key} ${now.args?.join(' ')}`));
    await vi.waitFor(() => expect(shown).toHaveLength(2));

    expect([at(shown, 0).kind, at(shown, 0).color, at(shown, 0).data.said.key]).toEqual([
      'stay',
      'default',
      'jfbEntries_export_preparingToast',
    ]);
    expect(said).toEqual(['jfbEntries_export_progressToast 2500 12400 false']);
    expect(closed, 'the progress toast goes').toHaveBeenCalledTimes(1);
    expect([at(shown, 1).kind, at(shown, 1).color, at(shown, 1).data.said.key, at(shown, 1).data.positive]).toEqual([
      'stay',
      'positive',
      'jfbEntries_export_readyToast',
      true,
    ]);
    expect(at(shown, 1).data.action?.label.key).toBe('jfbEntries_export_download');
  });

  it('say when the export is gone, or its progress no longer answers, and never close without a word', async () => {
    const gone = follower(['gone']);
    const first = notices();
    showFollowed(first.notices, 'f1', status('running', 1), gone.follower);
    await vi.waitFor(() => expect(first.shown).toHaveLength(2));
    expect(first.closed).toHaveBeenCalledTimes(1);
    expect([at(first.shown, 1).color, at(first.shown, 1).data.said.key]).toEqual([
      'danger',
      'jfbEntries_export_goneToast',
    ]);

    const silent = follower(Array.from({ length: MOST_SILENT_READS }, () => undefined));
    const second = notices();
    showFollowed(second.notices, 'f1', status('running', 1), silent.follower);
    await vi.waitFor(() => expect(second.shown).toHaveLength(2));
    expect([at(second.shown, 1).color, at(second.shown, 1).data.said.key]).toEqual([
      'warning',
      'jfbEntries_export_lostToast',
    ]);
  });

  it('say that it failed', async () => {
    const { follower: one } = follower([status('failed', 0)]);
    const { shown, notices: toasts } = notices();

    showFollowed(toasts, 'f1', status('running', 1), one);
    await vi.waitFor(() => expect(shown).toHaveLength(2));

    expect([at(shown, 1).color, at(shown, 1).data.said.key]).toEqual(['danger', 'jfbEntries_export_failedToast']);
  });

  it('save the file through its link, or say it could not come', async () => {
    const { shown, notices: toasts } = notices();
    const download = vi.fn(async () => true);

    expect(await saveExport(toasts, 'f1', status('ready', 2), download)).toBe(true);
    expect(download).toHaveBeenCalledWith('f1', 'k1');
    expect(shown, 'a link that came says nothing').toEqual([]);
    expect(await saveExport(toasts, 'f1', status('ready', 2), async () => false)).toBe(false);
    expect([at(shown, 0).kind, at(shown, 0).color, at(shown, 0).data.said.key]).toEqual([
      'peek',
      'danger',
      'jfbEntries_export_downloadFailed',
    ]);
  });
});
