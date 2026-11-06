// Restoring a version as a new draft (ROADMAP 10.3b, ADR-0198): changes not saved yet are saved first, and a refused save
// stops it; the server's answer names the new draft, which the form read again shows; a refusal reads nothing again; the
// steps hold to their form; and one restore runs at a time.
import { describe, expect, it, vi } from 'vitest';
import { oneAtATime, restoreDraft, type RestoreWorkspace, type SendRestore } from './restore-draft';

/** A workspace holding `form1` at draft 16, which a read again moves to what the server holds. */
class Workspace implements RestoreWorkspace {
  unique: string | undefined = 'form1';
  draft: number | null = 16;
  concurrency = 4;
  unsaved = false;
  /** The draft the server holds, which a read again shows; null where the read keeps what it had. */
  stored: number | null = null;
  readonly steps: string[] = [];
  refuseSave = false;
  getUnique = () => this.unique;
  getData = () => ({ draftVersion: this.draft, form: { concurrencyVersion: this.concurrency } });
  getHasUnpersistedChanges = () => this.unsaved;
  requestSubmit = async () => {
    this.steps.push('save');
    if (this.refuseSave) throw new Error('refused');
    this.unsaved = false;
    this.draft = 17;
    this.concurrency = 5;
  };
  reload = async () => {
    this.steps.push('read');
    if (this.stored !== null) this.draft = this.stored;
  };
}

/** A server that restores into the draft after the one the workspace holds, or refuses. */
function server(workspace: Workspace, refuse = false) {
  const sent: [string, number, number][] = [];
  const send: SendRestore = async (key, number, concurrencyVersion) => {
    sent.push([key, number, concurrencyVersion]);
    workspace.steps.push('restore');
    if (refuse) return undefined;
    workspace.stored = (workspace.draft ?? 0) + 1;
    return workspace.stored;
  };
  return { sent, send };
}

describe('restoring a version as a new draft', () => {
  it('restores with the form as read, then reads it again, and names the draft before and the new one', async () => {
    const workspace = new Workspace();
    const { sent, send } = server(workspace);
    const restored = await restoreDraft(workspace, 'form1', 12, send);

    expect(sent).toEqual([['form1', 12, 4]]);
    expect(workspace.steps).toEqual(['restore', 'read']);
    expect(restored).toEqual({ before: 16, after: 17, shown: true });
  });

  it('saves changes not saved yet first, so they stay in the History, and stops where that save is refused', async () => {
    const workspace = new Workspace();
    workspace.unsaved = true;
    const { sent, send } = server(workspace);
    expect(await restoreDraft(workspace, 'form1', 12, send)).toEqual({ before: 17, after: 18, shown: true });
    expect(workspace.steps).toEqual(['save', 'restore', 'read']);
    // The save moved the draft and the concurrency version: the restore is based on the saved form.
    expect(sent).toEqual([['form1', 12, 5]]);

    const refusing = new Workspace();
    refusing.unsaved = true;
    refusing.refuseSave = true;
    const other = server(refusing);
    expect(await restoreDraft(refusing, 'form1', 12, other.send)).toBeUndefined();
    expect(refusing.steps).toEqual(['save']);
  });

  it('reads nothing again after a refusal, which the server answer says', async () => {
    const workspace = new Workspace();
    const { send } = server(workspace, true);
    expect(await restoreDraft(workspace, 'form1', 12, send)).toBeUndefined();
    expect(workspace.steps).toEqual(['restore']);
  });

  it('says where the read again shows another draft than the new one, as the views then show the old one', async () => {
    const workspace = new Workspace();
    const { send } = server(workspace);
    // A read that fails keeps what the workspace had.
    workspace.reload = async () => {
      workspace.steps.push('read');
    };
    expect(await restoreDraft(workspace, 'form1', 12, send)).toEqual({ before: 16, after: 17, shown: false });
  });

  it('holds to its form: another form stops it before the server is asked, and keeps its views after', async () => {
    const elsewhere = new Workspace();
    elsewhere.unique = 'form2';
    const first = server(elsewhere);
    expect(await restoreDraft(elsewhere, 'form1', 12, first.send)).toBeUndefined();
    expect(first.sent).toEqual([]);

    // Another form opened while the changes were saved.
    const saving = new Workspace();
    saving.unsaved = true;
    const second = server(saving);
    const save = saving.requestSubmit;
    saving.requestSubmit = async () => {
      await save();
      saving.unique = 'form2';
    };
    expect(await restoreDraft(saving, 'form1', 12, second.send)).toBeUndefined();
    expect(second.sent).toEqual([]);

    // Another form opened while the server restored: the restore stands, and that form isn't read again.
    const restoring = new Workspace();
    const third = server(restoring);
    const sent = third.send;
    const restored = await restoreDraft(restoring, 'form1', 12, async (...args) => {
      const after = await sent(...args);
      restoring.unique = 'form2';
      return after;
    });
    expect(restored).toEqual({ before: 16, after: 17, shown: false });
    expect(restoring.steps).toEqual(['restore']);
  });
});

describe('one restore at a time', () => {
  it('answers nothing for a second run while the first runs, and runs again once it is done', async () => {
    let finish: (value: number) => void = () => undefined;
    const run = vi.fn(() => new Promise<number>((resolve) => (finish = resolve)));
    const once = oneAtATime(run);

    const first = once();
    expect(await once()).toBeUndefined();
    finish(1);
    expect(await first).toBe(1);
    expect(run).toHaveBeenCalledOnce();

    const third = once();
    finish(3);
    expect(await third).toBe(3);
    expect(run).toHaveBeenCalledTimes(2);
  });

  it('runs again after a run that failed', async () => {
    const once = oneAtATime(vi.fn().mockRejectedValueOnce(new Error('no answer')).mockResolvedValueOnce(2));
    await expect(once()).rejects.toThrow('no answer');
    expect(await once()).toBe(2);
  });
});
