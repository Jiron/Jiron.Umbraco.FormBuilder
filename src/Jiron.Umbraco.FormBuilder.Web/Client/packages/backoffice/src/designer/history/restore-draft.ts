// Restoring a version as a new draft, as the form workspace does it (ROADMAP 10.3b, ADR-0198): changes not saved yet are
// saved first, so they stay in the History; then the server copies the version into a new draft, and the form is read
// again, so every view shows it. The steps hold to the form they started with: where the workspace holds another form
// meanwhile, they stop, or, once the server has restored, leave that form's views as they are. One restore runs at a
// time, as a second would be refused as a change made meanwhile.

/** A version restored as a new draft: the draft's number before, the new draft's, and whether the workspace shows it. */
export interface RestoredDraft {
  readonly before: number;
  readonly after: number;
  readonly shown: boolean;
}

/** What a restore needs of the workspace. */
export interface RestoreWorkspace {
  getUnique(): string | null | undefined;
  getData():
    { readonly draftVersion: number | null; readonly form: { readonly concurrencyVersion: number } } | undefined;
  getHasUnpersistedChanges(): boolean;
  /** Saves the changes not saved yet; rejects where the save is refused, which the workspace says. */
  requestSubmit(): Promise<unknown>;
  /** Reads the form again. */
  reload(): Promise<unknown>;
}

/** Copies a version into a new draft on the server: the new draft's number, or undefined after a refusal, which is said. */
export type SendRestore = (key: string, number: number, concurrencyVersion: number) => Promise<number | undefined>;

/** Restores a version of the form `key`; undefined where it is refused, or where the workspace holds another form. */
export async function restoreDraft(
  workspace: RestoreWorkspace,
  key: string,
  number: number,
  send: SendRestore,
): Promise<RestoredDraft | undefined> {
  if (workspace.getUnique() !== key) return undefined;
  if (workspace.getHasUnpersistedChanges()) {
    try {
      await workspace.requestSubmit();
    } catch {
      return undefined;
    }
  }

  const data = workspace.getData();
  if (workspace.getUnique() !== key || !data || data.draftVersion === null) return undefined;
  const after = await send(key, number, data.form.concurrencyVersion);
  if (after === undefined) return undefined;
  const before = data.draftVersion;
  // Another form opened meanwhile keeps its views as they are; the restore stands all the same.
  if (workspace.getUnique() !== key) return { before, after, shown: false };
  await workspace.reload();
  return { before, after, shown: workspace.getUnique() === key && workspace.getData()?.draftVersion === after };
}

/** A run that answers undefined at once while the one before is still running. */
export function oneAtATime<A extends unknown[], R>(
  run: (...args: A) => Promise<R | undefined>,
): (...args: A) => Promise<R | undefined> {
  let running = false;
  return async (...args) => {
    if (running) return undefined;
    running = true;
    try {
      return await run(...args);
    } finally {
      running = false;
    }
  };
}
